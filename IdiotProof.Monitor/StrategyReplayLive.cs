using System.Globalization;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Monitor;

/// <summary>
/// LIVE replay (CLI: <c>replay-live</c>) — the truthful counterpart to the
/// hypothetical <see cref="StrategyReplay.RunAsync"/> backtest. Where `replay`
/// re-simulates what a strategy WOULD have done on a day's bars (filling at bar
/// close via GapperExitEvaluator), `replay-live` renders what the Monitor
/// ACTUALLY did: it reads the executed orders from the trade diary
/// (<see cref="TradeDiaryEntry"/>) — real entry/exit price, time, quantity,
/// exit reason, realized $ P&amp;L, broker order ids — and draws them on the SAME
/// day's bars fetched from Alpaca. No simulation, no drift; the markers are the
/// real fills.
///
/// Output is a <see cref="ReplayRun"/> with <c>Kind = "live"</c>, so it sits in
/// the same archive as the sim replays and `/replays` publishes both, tagged
/// LIVE vs SIM. One live run per (symbol, strategy, ET day); re-running replaces
/// the prior live run for that key so it stays a faithful mirror of the diary.
/// </summary>
public static partial class StrategyReplay
{
    public static async Task<int> RunLiveAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        // Date: the ET calendar day whose live trades to render. Default = today
        // ET (the session that just traded); pass --date for a past day.
        var dateEt = opt.TryGetValue("date", out var dRaw) && DateOnly.TryParse(dRaw, out var d)
            ? d
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketTime.Eastern));

        var symbolFilter = opt.TryGetValue("symbol", out var symRaw) && !string.IsNullOrWhiteSpace(symRaw)
            ? symRaw.Trim().ToUpperInvariant() : null;
        var feedTier = (opt.GetValueOrDefault("feed", "sip")).ToLowerInvariant();
        var outRoot = opt.GetValueOrDefault("out",
            @"D:\Projects\MindAttic\mindattic.com\idiotproof\replays");

        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("No --user given and no single user found.");

        var keys = sp.GetRequiredService<UserKeyService>();
        var k = await keys.GetOrCreateAsync(userId.Value);
        if (string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
            return Fail($"user {userId} has no Alpaca data keys — set them first (set-keys).");

        // ── Pull the day's executed trades from the diary (system of record) ──
        // Window the query by the ET day's UTC bounds on EntryUtc, so a trade is
        // grouped under the day it was ENTERED (a hold-through-midnight exit still
        // belongs to its entry day).
        var dayStartUtc = EtToUtc(dateEt, new TimeSpan(0, 0, 0));
        var dayEndUtc = EtToUtc(dateEt.AddDays(1), new TimeSpan(0, 0, 0));
        var dbf = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        List<TradeDiaryEntry> diary;
        await using (var db = await dbf.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            var q = db.TradeDiary.Where(t => t.OwnerUserId == userId.Value
                                          && t.EntryUtc >= dayStartUtc && t.EntryUtc < dayEndUtc
                                          && t.Status != TradeDiaryStatus.Orphaned);
            if (symbolFilter is not null) q = q.Where(t => t.Symbol == symbolFilter);
            diary = await q.OrderBy(t => t.EntryUtc).ToListAsync();
        }

        if (diary.Count == 0)
            return Fail($"no live trades in the diary for {dateEt:yyyy-MM-dd}" +
                        (symbolFilter is not null ? $" / {symbolFilter}" : "") +
                        ". Nothing to render (did the Monitor fire that day?).");

        // Strategy defs (for the condition band + phase cards + flow), by id.
        var repo = sp.GetRequiredService<StrategyRepository>();
        var strategies = (await repo.GetAllForUserAsync(userId.Value))
            .ToDictionary(s => s.Id, s => s);

        // One run per (symbol, strategy) — matches the ReplayRun's single-strategy
        // shape; a repeating strategy's multiple round-trips become multiple
        // payoffs in that one run.
        var groups = diary
            .GroupBy(t => (t.Symbol.ToUpperInvariant(), t.StrategyId, t.StrategyTitle))
            .ToList();

        Console.WriteLine($"replay-live {dateEt:yyyy-MM-dd} · {diary.Count} trade(s) across {groups.Count} (symbol,strategy) group(s)");

        int rendered = 0;
        var touchedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var symbol = g.Key.Item1;
            var trades = g.OrderBy(t => t.EntryUtc).ToList();
            var strategyTitle = string.IsNullOrWhiteSpace(g.Key.StrategyTitle) ? $"{symbol} (untitled)" : g.Key.StrategyTitle;

            var ok = await RenderLiveGroupAsync(sp, dbf, k, feedTier, outRoot, dateEt,
                symbol, strategyTitle, trades, strategies.GetValueOrDefault(g.Key.StrategyId));
            if (ok) { rendered++; touchedSymbols.Add(symbol); }
        }

        // Rebuild the affected ticker indexes + the root index from SQL.
        foreach (var sym in touchedSymbols)
            await WriteTickerIndexAsync(dbf, outRoot, sym);
        await WriteRootIndexAsync(dbf, outRoot);

        Console.WriteLine($"  {rendered} live run(s) written across {touchedSymbols.Count} ticker(s) → {outRoot}");
        Console.WriteLine("  publish: cd MindAttic.Deploy && node --use-system-ca src/deploy.js --site idiotproof-replays  (or /replays)");
        return 0;
    }

    private static async Task<bool> RenderLiveGroupAsync(
        IServiceProvider sp, IDbContextFactory<AppDbContext> dbf, UserApiKeys k,
        string feedTier, string outRoot, DateOnly dateEt,
        string symbol, string strategyTitle, List<TradeDiaryEntry> trades, Strategy? strategyRow)
    {
        // Resolve the strategy definition (best-effort — the diary outlives its
        // strategy, so a deleted/edited row just costs us the condition band).
        StrategyDefinition? def = null;
        if (strategyRow is not null)
        {
            var loaded = StrategyLoader.Load(strategyRow.ScriptJson, strategyRow.ScriptText);
            def = loaded.Definition;
        }
        var condLabels = def?.EntryConditions.Select(c => c.ToScript()).ToList() ?? new List<string>();
        var session = def?.Session ?? TradingSession.Extended;
        var isShort = trades[0].Direction.Equals(nameof(TradeDirection.Short), StringComparison.OrdinalIgnoreCase);

        // ── Fetch the day's bars (04:00–20:00 ET) on the chosen tier ──
        var barStartUtc = EtToUtc(dateEt, new TimeSpan(4, 0, 0));
        var barEndUtc = EtToUtc(dateEt, new TimeSpan(20, 0, 0));
        var sipWall = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        if (feedTier == "sip" && barEndUtc > sipWall) barEndUtc = sipWall;
        if (barEndUtc <= barStartUtc)
        {
            Console.Error.WriteLine($"  {symbol}: session for {dateEt:yyyy-MM-dd} hasn't opened yet — skipped.");
            return false;
        }

        await using var feed = new AlpacaDataFeed(k.AlpacaApiKeyId!, k.AlpacaApiSecretKey!, feedTier);
        var candles = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, barStartUtc, barEndUtc, TimeSpan.FromMinutes(1)))
            candles.Add(c);
        if (candles.Count == 0)
        {
            Console.Error.WriteLine($"  {symbol}: no {feed.FeedTier} bars for {dateEt:yyyy-MM-dd} — skipped.");
            return false;
        }

        var previousClose = await ((IMarketDataFeed)feed).GetPreviousCloseAsync(symbol, barStartUtc);
        var emas = def is not null ? EmaPeriodCollector.Collect(def) : new HashSet<int>();

        int n = candles.Count;
        var et = new string[n];
        var cnd = new bool[n][];
        var inSess = new bool[n];
        var snapCache = new (double o, double h, double l, double c, long v, double vwap, double whigh, double volx)[n];
        var etIndex = new Dictionary<string, int>(); // "HH:mm" → bar index (first wins)

        for (int i = 0; i < n; i++)
        {
            var lo = Math.Max(0, i - (CandleWindow - 1));
            var window = candles.GetRange(lo, i - lo + 1);
            var snap = IndicatorSnapshotBuilder.BuildWithEmas(symbol, window, emas, previousClose);
            inSess[i] = MarketTime.IsInsideSession(session, candles[i].EndUtc);

            var row = new bool[condLabels.Count];
            if (def is not null)
                for (int j = 0; j < def.EntryConditions.Count; j++)
                    row[j] = def.EntryConditions[j].Evaluate(snap);
            cnd[i] = row;

            var tod = MarketTime.ToEasternTimeOfDay(candles[i].StartUtc);
            et[i] = $"{(int)tod.TotalHours:00}:{tod.Minutes:00}";
            etIndex.TryAdd(et[i], i);
            snapCache[i] = ((double)candles[i].Open, (double)candles[i].High, (double)candles[i].Low,
                (double)candles[i].Close, (long)candles[i].Volume,
                Round(snap.Vwap ?? snap.Price), Round(snap.WindowHigh ?? snap.Price), Math.Round(snap.VolumeRatio, 2));
        }

        // ── Build payoffs from the ACTUAL trades; mark their real fill bars ──
        var entryIdx = new HashSet<int>();
        var exitIdx = new HashSet<int>();
        var payoffs = new List<object>();
        double totalPnlPct = 0; decimal totalPnlUsd = 0;
        foreach (var t in trades)
        {
            var entryHm = EtHm(t.EntryUtc);
            var ei = NearestBar(etIndex, et, entryHm);
            if (ei >= 0) entryIdx.Add(ei);

            var open = t.Status == TradeDiaryStatus.Open || t.ExitUtc is null;
            string exitHm = ""; int xi = -1;
            if (!open && t.ExitUtc is { } xu)
            {
                exitHm = EtHm(xu);
                xi = NearestBar(etIndex, et, exitHm);
                if (xi >= 0) exitIdx.Add(xi);
            }

            // % return: prefer the recorded ReturnPercent; else derive from fills.
            double pnlPct = t.ReturnPercent is { } rp ? (double)rp
                : (t.ExitPrice is { } xp && t.EntryPrice > 0
                    ? (double)((isShort ? (t.EntryPrice - xp) : (xp - t.EntryPrice)) / t.EntryPrice) * 100.0
                    : 0);
            totalPnlPct += pnlPct;
            totalPnlUsd += t.RealizedPnL ?? 0;

            payoffs.Add(new
            {
                entryEt = entryHm,
                entryPx = Round((double)t.EntryPrice),
                exitEt = open ? "—" : exitHm,
                exitPx = t.ExitPrice is { } xpv ? Round((double)xpv) : (double?)null,
                reason = open ? "Open" : (t.ExitReason ?? "Closed"),
                pnlPct = Math.Round(pnlPct, 2),
                // live-only extras the template renders when DATA.live is true:
                qty = t.Quantity,
                pnlUsd = t.RealizedPnL is { } pu ? Math.Round((double)pu, 2) : (double?)null,
                orderId = t.EntryOrderId,
                open,
            });
        }

        // Build the bar rows now that entry/exit indices are known.
        var rows = new List<BarRow>(n);
        for (int i = 0; i < n; i++)
        {
            var s = snapCache[i];
            rows.Add(new BarRow(et[i], s.o, s.h, s.l, s.c, s.v, s.vwap, s.whigh, s.volx,
                cnd[i], inSess[i], entryIdx.Contains(i), exitIdx.Contains(i)));
        }

        var firstFireEt = payoffs.Count > 0 ? ((dynamic)payoffs[0]).entryEt as string : null;
        double? entryPrice = payoffs.Count > 0 ? ((dynamic)payoffs[0]).entryPx : null;
        var broker = trades[0].Broker;
        var isPaper = trades[0].IsPaper;
        var genUtc = DateTime.UtcNow;
        var genEt = TimeZoneInfo.ConvertTimeFromUtc(genUtc, MarketTime.Eastern);

        var data = new
        {
            symbol,
            strategy = strategyTitle,
            dateEt = dateEt.ToString("yyyy-MM-dd"),
            feed = feed.FeedTier.ToUpperInvariant(),
            generatedEt = genEt.ToString("yyyy-MM-dd HH:mm:ss 'ET'", CultureInfo.InvariantCulture),
            session = session.ToString(),
            side = isShort ? "SELL short" : "BUY long",
            conditions = condLabels,
            repeat = trades.Count > 1,
            firstFire = firstFireEt,
            entryPrice,
            fires = payoffs.Select(p => ((dynamic)p).entryEt).Cast<string>().ToList(),
            payoffs,
            payoffCount = payoffs.Count,
            totalPnl = Math.Round(totalPnlPct, 2),
            bestPassed = condLabels.Count, // live: the trade proves the gate cleared
            bestFail = (string?)null,
            mermaid = def?.ToMermaid() ?? "",
            svgFlow = def?.ToSvg() ?? "",
            bars = rows,
            // ── live-only payload the template keys off ──
            live = true,
            broker,
            isPaper,
            totalPnlUsd = Math.Round((double)totalPnlUsd, 2),
            tradeCount = trades.Count,
        };
        var dataJson = JsonSerializer.Serialize(data, JsonOpts);

        var run = new ReplayRun
        {
            Symbol = symbol, Strategy = strategyTitle, DateEt = data.dateEt, Feed = data.feed, Kind = "live",
            Stamp = "", GeneratedUtc = genUtc, GeneratedEt = data.generatedEt,
            Fired = payoffs.Count > 0, PayoffCount = payoffs.Count, TotalPnl = Math.Round(totalPnlPct, 2),
            FirstFireEt = firstFireEt, EntryPrice = entryPrice, DataJson = dataJson,
            StrategyHtml = def?.ToHtml() ?? "<p style=\"color:var(--faint)\">Strategy definition unavailable (edited or deleted since the trade).</p>",
        };

        // Stamp is deterministic for a live run: date + a stable "-live" folder so
        // re-running replaces the same page rather than piling up duplicates. A
        // second strategy on the same symbol/day gets a strategy-hash suffix.
        var titleHash = Math.Abs(strategyTitle.GetHashCode()) % 1000;
        run.Stamp = $"{dateEt:yyyy-MM-dd}-live-{titleHash:000}";

        // Idempotent refresh: drop any prior LIVE run for this exact (symbol,
        // date, strategy) — cascade removes its feature rows — then insert fresh.
        await using (var db = await dbf.CreateDbContextAsync())
        {
            var stale = await db.ReplayRuns
                .Where(r => r.Kind == "live" && r.Symbol == symbol && r.DateEt == data.dateEt && r.Strategy == strategyTitle)
                .ToListAsync();
            if (stale.Count > 0) db.ReplayRuns.RemoveRange(stale);

            var (tradeRows, barRows) = ReplayFeatures.Extract(run);
            db.ReplayRuns.Add(run);
            db.ReplayTrades.AddRange(tradeRows);
            db.ReplayBars.AddRange(barRows);
            await db.SaveChangesAsync();
        }

        RenderRunPage(run, outRoot);

        var sign = totalPnlUsd >= 0 ? "+" : "";
        Console.WriteLine($"  ● {symbol} \"{strategyTitle}\" — {trades.Count} trade(s), {sign}${totalPnlUsd:0.##} ({totalPnlPct:+0.##;-0.##;0}%)  → {run.Stamp}/");
        return true;
    }

    /// <summary>ET "HH:mm" (floored to the minute) of a UTC instant.</summary>
    private static string EtHm(DateTime utc)
    {
        var tod = MarketTime.ToEasternTimeOfDay(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return $"{(int)tod.TotalHours:00}:{tod.Minutes:00}";
    }

    /// <summary>
    /// Bar index whose ET minute equals <paramref name="hm"/>; if that exact
    /// minute has no bar (thin tape), fall back to the nearest earlier minute,
    /// else the nearest later one. −1 only if the array is empty.
    /// </summary>
    private static int NearestBar(Dictionary<string, int> etIndex, string[] et, string hm)
    {
        if (etIndex.TryGetValue(hm, out var exact)) return exact;
        if (et.Length == 0) return -1;
        int target = HmToMin(hm), best = -1, bestDist = int.MaxValue;
        for (int i = 0; i < et.Length; i++)
        {
            int dist = Math.Abs(HmToMin(et[i]) - target);
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }

    private static int HmToMin(string hm)
    {
        var p = (hm ?? "").Split(':');
        return p.Length == 2 && int.TryParse(p[0], out var h) && int.TryParse(p[1], out var m) ? h * 60 + m : 0;
    }
}
