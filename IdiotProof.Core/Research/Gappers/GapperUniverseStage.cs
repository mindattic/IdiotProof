using System.Diagnostics;
using System.Net;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public sealed record UniverseStageRequest(DateOnly From, DateOnly To, bool Smoke, IReadOnlyList<string> SmokeSymbols, Guid? Owner, int MaxCalls);

public sealed record UniverseStageResult(
    string KeySource, int Assets, int DailyBarCalls, int DailyBarsInserted, int SplitCalls, int SplitsInserted,
    int Symbols, int ReusedTickerSymbols, int UniverseDays, int Candidates, Dictionary<string, int> ExcludedByReason,
    double Minutes, string SampleFile);

/// <summary>
/// <c>research gappers build-universe</c>: assets (active + inactive), T1 daily bars through the
/// multi-symbol loader, T6 splits, then U(D), the exclusions, candidates, deciles and price tiers
/// per the frozen rule, and <c>sample.json</c>. Every pull is resumable by coverage mark; the
/// universe rows for the range are rebuilt from scratch so a re-run never mixes rules.
/// </summary>
public sealed class GapperUniverseStage(GapperStudyServices s)
{
    public const string DailyTier = "daily";
    public const string SplitsTier = "splits";
    private const int SymbolBatch = 400;

    public async Task<UniverseStageResult> RunAsync(UniverseStageRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        var dailyFrom = req.Smoke ? req.From.AddDays(-60) : p.DataTiers.DailyBars.From;
        var dailyTo = req.Smoke ? req.To : (req.To > p.DataTiers.DailyBars.To ? req.To : p.DataTiers.DailyBars.To);
        var calls = new CallBudget(req.MaxCalls);

        var assets = await LoadAssetsAsync(keys, req, ct);
        s.Logger.LogInformation("[{Now}] universe: {Count} assets ({Active} active)", GapperStudyServices.Et(s.UtcNow), assets.Count, assets.Count(a => a.Active));
        await UpsertAssetsAsync(assets, ct);
        var symbols = assets.Select(a => a.Symbol).ToList();

        await using var feed = s.Feed(keys);
        var (dailyCalls, dailyInserted) = await PullDailyBarsAsync(feed, symbols, dailyFrom, dailyTo, calls, ct);
        using var splitSource = s.Splits(keys);
        var (splitCalls, splitsInserted) = await PullSplitsAsync(splitSource, symbols, dailyFrom.AddDays(-30), dailyTo.AddDays(2), calls, ct);

        var stats = await BuildRowsAsync(symbols, assets.ToDictionary(a => a.Symbol, a => a.Name, StringComparer.Ordinal), req, dailyFrom, ct);
        await AssignDecilesAsync(req, ct);

        var sample = await GapperStudySample.WriteAsync(s, req.Smoke, new Dictionary<string, object?>
        {
            ["universeBuild"] = new
            {
                from = req.From, to = req.To, dailyFrom, dailyTo, assets = assets.Count, symbolsWithBars = stats.Symbols,
                reusedTickerSymbols = stats.ReusedTickerSymbols, dailyBarCalls = dailyCalls, splitCalls,
                keySource = keys.Source, minutes = Math.Round(sw.Elapsed.TotalMinutes, 2), builtUtc = s.UtcNow,
            },
        }, ct);
        return new UniverseStageResult(keys.Source, assets.Count, dailyCalls, dailyInserted, splitCalls, splitsInserted,
            stats.Symbols, stats.ReusedTickerSymbols, stats.UniverseDays, stats.Candidates, stats.ExcludedByReason,
            Math.Round(sw.Elapsed.TotalMinutes, 2), s.Paths.SampleFile(req.Smoke));
    }

    private async Task<List<StudyAsset>> LoadAssetsAsync(StudyKeys keys, UniverseStageRequest req, CancellationToken ct)
    {
        List<StudyAsset> all;
        try { all = await s.LoadAssetsAsync(keys, ct); }
        catch (Exception ex) when (req.Smoke)
        {
            s.Logger.LogWarning(ex, "assets endpoint failed; smoke run continues with the named symbols only");
            all = [];
        }
        if (!req.Smoke) return all;
        var wanted = req.SmokeSymbols.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        var known = all.Where(a => wanted.Contains(a.Symbol)).ToList();
        foreach (var w in wanted.Where(w => known.All(k => k.Symbol != w))) known.Add(new StudyAsset(w, "", true));
        return known.OrderBy(a => a.Symbol, StringComparer.Ordinal).ToList();
    }

    private async Task UpsertAssetsAsync(List<StudyAsset> assets, CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var existing = await db.GapperStudyCikMap.ToDictionaryAsync(r => r.Symbol, StringComparer.Ordinal, ct);
        foreach (var a in assets)
        {
            var isAdr = GapperUniverseBuilder.IsAdr(a.Name, s.Params.Universe.AdrNamePatterns);
            if (existing.TryGetValue(a.Symbol, out var row))
            {
                row.Name = a.Name;
                row.Active = a.Active;
                row.IsAdr = isAdr;
            }
            else db.GapperStudyCikMap.Add(new GapperStudyCikMap { Symbol = a.Symbol, Name = a.Name, Active = a.Active, IsAdr = isAdr });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>T1: 100 symbols per request, resumable per (symbol, range); a 4xx on a chunk retries symbol by symbol so one bad ticker never blocks 99 good ones.</summary>
    private async Task<(int Calls, int Inserted)> PullDailyBarsAsync(AlpacaDataFeed feed, List<string> symbols, DateOnly from, DateOnly to, CallBudget calls, CancellationToken ct)
    {
        var covered = await s.CoveredAsync(DailyTier, ct);
        string Key(string sym) => $"{sym}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}";
        var missing = symbols.Where(x => !covered.Contains(Key(x))).ToList();
        s.Logger.LogInformation("[{Now}] daily bars: {Missing} of {Total} symbols to pull for {From}..{To}", GapperStudyServices.Et(s.UtcNow), missing.Count, symbols.Count, from, to);
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        int callCount = 0, inserted = 0, done = 0;

        foreach (var chunk in missing.Chunk(AlpacaDataFeed.MaxSymbolsPerRequest))
        {
            if (!calls.Allow()) { s.Logger.LogWarning("daily bars: --max-calls reached after {Calls} calls", callCount); break; }
            MultiSymbolBars result;
            try
            {
                result = await feed.GetHistoricalCandlesForSymbolsAsync(chunk, TimeSpan.FromDays(1), start, end, 10000, s.RateGate.WaitAsync, ct);
                callCount += result.Pages;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
            {
                var perSymbol = new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.OrdinalIgnoreCase);
                foreach (var sym in chunk)
                {
                    try
                    {
                        var one = await feed.GetHistoricalCandlesForSymbolsAsync([sym], TimeSpan.FromDays(1), start, end, 10000, s.RateGate.WaitAsync, ct);
                        callCount += one.Pages;
                        perSymbol[sym] = one.Bars.TryGetValue(sym, out var list) ? list : [];
                    }
                    catch (HttpRequestException inner) when (inner.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
                    {
                        callCount++;
                        perSymbol[sym] = [];
                    }
                }
                result = new MultiSymbolBars(perSymbol, 0, feed.FeedTier);
            }

            await using (var db = await s.Db.CreateDbContextAsync(ct))
            {
                var chunkList = chunk.ToList();
                var present = (await db.GapperStudyDailyBars.AsNoTracking()
                        .Where(b => chunkList.Contains(b.Symbol) && b.DateEt >= from && b.DateEt <= to)
                        .Select(b => new { b.Symbol, b.DateEt }).ToListAsync(ct))
                    .Select(x => (x.Symbol, x.DateEt)).ToHashSet();
                foreach (var sym in chunk)
                {
                    if (!result.Bars.TryGetValue(sym, out var bars)) continue;
                    foreach (var c in bars)
                    {
                        var date = DateOnly.FromDateTime(c.StartUtc);
                        if (date < from || date > to || !present.Add((sym, date))) continue;
                        var d = c as DetailedCandle;
                        db.GapperStudyDailyBars.Add(new GapperStudyDailyBar
                        {
                            Symbol = sym, DateEt = date, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume,
                            TradeCount = d?.TradeCount ?? 0, Vwap = d?.Vwap ?? 0, Feed = BarProvenance.SourceOf(c, feed.FeedName),
                        });
                        inserted++;
                    }
                }
                await db.SaveChangesAsync(ct);
            }
            await s.MarkAsync(DailyTier, chunk.Select(sym => (Key(sym), result.Bars.TryGetValue(sym, out var b) ? b.Count : 0)), ct);
            done += chunk.Length;
            s.Logger.LogInformation("[{Now}] daily bars: {Done}/{Total} symbols, {Calls} calls, {Rows} rows inserted", GapperStudyServices.Et(s.UtcNow), done, missing.Count, callCount, inserted);
        }
        return (callCount, inserted);
    }

    /// <summary>T6: forward and reverse splits for every symbol, 50 per request through the shared rate gate.</summary>
    private async Task<(int Calls, int Inserted)> PullSplitsAsync(AlpacaSplitSource source, List<string> symbols, DateOnly from, DateOnly to, CallBudget calls, CancellationToken ct)
    {
        var covered = await s.CoveredAsync(SplitsTier, ct);
        string Key(string sym) => $"{sym}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}";
        var missing = symbols.Where(x => !covered.Contains(Key(x))).ToList();
        int callCount = 0, inserted = 0;
        foreach (var chunk in missing.Chunk(50))
        {
            if (!calls.Allow()) { s.Logger.LogWarning("splits: --max-calls reached"); break; }
            await s.RateGate.WaitAsync(ct);
            callCount++;
            var found = await source.GetSplitsAsync(chunk, from, to, ct);
            await using var db = await s.Db.CreateDbContextAsync(ct);
            var chunkList = chunk.ToList();
            var present = (await db.GapperStudySplits.AsNoTracking().Where(x => chunkList.Contains(x.Symbol)).Select(x => new { x.Symbol, x.ExDate }).ToListAsync(ct))
                .Select(x => (x.Symbol, x.ExDate)).ToHashSet();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (sym, list) in found)
            {
                foreach (var e in list)
                {
                    if (!present.Add((sym, e.ExDate))) continue;
                    db.GapperStudySplits.Add(new GapperStudySplit { Symbol = sym, ExDate = e.ExDate, OldRate = e.OldRate, NewRate = e.NewRate });
                    inserted++;
                    counts[sym] = counts.GetValueOrDefault(sym) + 1;
                }
            }
            await db.SaveChangesAsync(ct);
            await s.MarkAsync(SplitsTier, chunk.Select(sym => (Key(sym), counts.GetValueOrDefault(sym))), ct);
        }
        s.Logger.LogInformation("[{Now}] splits: {Calls} calls, {Rows} rows inserted", GapperStudyServices.Et(s.UtcNow), callCount, inserted);
        return (callCount, inserted);
    }

    private async Task<UniverseBuildStats> BuildRowsAsync(List<string> symbols, Dictionary<string, string> names, UniverseStageRequest req, DateOnly dailyFrom, CancellationToken ct)
    {
        var p = s.Params;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var stale = db.GapperStudyUniverseDays.Where(r => r.DateEt >= req.From && r.DateEt <= req.To);
            stale = req.Smoke ? stale.Where(r => r.Split == GapperStudyServices.SmokeSplit) : stale.Where(r => r.Split != GapperStudyServices.SmokeSplit);
            await GapperStudyServices.DeleteAsync(db, stale, ct);
        }

        int withBars = 0, reused = 0, universeDays = 0, candidates = 0;
        var excluded = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var batch in symbols.Chunk(SymbolBatch))
        {
            var batchList = batch.ToList();
            await using var db = await s.Db.CreateDbContextAsync(ct);
            var bars = await db.GapperStudyDailyBars.AsNoTracking()
                .Where(b => batchList.Contains(b.Symbol) && b.DateEt >= dailyFrom && b.DateEt <= req.To)
                .OrderBy(b => b.Symbol).ThenBy(b => b.DateEt)
                .Select(b => new { b.Symbol, b.DateEt, b.Open, b.High, b.Low, b.Close, b.Volume })
                .ToListAsync(ct);
            var splits = await db.GapperStudySplits.AsNoTracking().Where(x => batchList.Contains(x.Symbol)).ToListAsync(ct);
            var daily = bars.GroupBy(b => b.Symbol)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<StudyDailyBar>)g.Select(b => new StudyDailyBar(b.DateEt, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList(), StringComparer.Ordinal);
            var splitMap = splits.GroupBy(x => x.Symbol)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<SplitEvent>)g.Select(x => new SplitEvent(x.Symbol, x.ExDate, x.OldRate, x.NewRate)).ToList(), StringComparer.Ordinal);
            var (rows, stats) = GapperUniverseBuilder.BuildRows(p, daily, splitMap, names, req.From, req.To, d => s.SplitOf(d, req.Smoke));
            db.GapperStudyUniverseDays.AddRange(rows);
            await db.SaveChangesAsync(ct);
            withBars += daily.Count;
            reused += stats.ReusedTickerSymbols;
            universeDays += stats.UniverseDays;
            candidates += stats.Candidates;
            foreach (var (k, v) in stats.ExcludedByReason) excluded[k] = excluded.GetValueOrDefault(k) + v;
            s.Logger.LogInformation("[{Now}] universe rows: {Rows} written for {Symbols} symbols (running total {Days} universe days, {Cands} candidates)",
                GapperStudyServices.Et(s.UtcNow), rows.Count, batch.Length, universeDays, candidates);
        }
        return new UniverseBuildStats(withBars, reused, universeDays, candidates, excluded);
    }

    private async Task AssignDecilesAsync(UniverseStageRequest req, CancellationToken ct)
    {
        foreach (var day in MarketCalendar.TradingDays(req.From, req.To))
        {
            await using var db = await s.Db.CreateDbContextAsync(ct);
            var rows = db.GapperStudyUniverseDays.Where(r => r.DateEt == day && r.ExcludedReason == null);
            rows = req.Smoke ? rows.Where(r => r.Split == GapperStudyServices.SmokeSplit) : rows.Where(r => r.Split != GapperStudyServices.SmokeSplit);
            var list = await rows.ToListAsync(ct);
            if (list.Count == 0) continue;
            GapperUniverseBuilder.AssignDeciles(list);
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>The <c>--max-calls</c> safety cap: Allow() is false once the budget is spent (0 = unlimited).</summary>
public sealed class CallBudget(int max)
{
    private int used;

    public int Used => used;

    public bool Allow()
    {
        if (max > 0 && used >= max) return false;
        used++;
        return true;
    }
}
