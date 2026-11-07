using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>One (symbol, day, decision time) of the study panel: the universe facts, the snapshot and the labels joined.</summary>
public sealed record PanelRow(
    string Symbol, DateOnly DateEt, bool IsCandidate, bool IsControl, bool IsGapper, string? GapTier, string? SubType,
    GapperFormationSnapshotRow Snapshot, GapperStudyLabel Label, GapperSnapshotExtras Extras)
{
    public bool IsTier50 => GapTier is "Tier50" or "Tier100";

    /// <summary>Amendment 01: a non-candidate day drawn into the population sample. No frozen hypothesis reads such a row.</summary>
    public bool IsPopulationSample { get; init; }

    /// <summary>The row's weight in a population estimate: 1 for a candidate day (census), 1/rate for a population-sample day.</summary>
    public double Weight { get; init; } = 1;

    /// <summary>A row of the amendment's population: every candidate day and every population-sample day.</summary>
    public bool InPopulation => IsCandidate || IsPopulationSample;

    /// <summary>A hypothesis feature name resolved against the row; null when the column is empty.</summary>
    public double? Column(string name) => name switch
    {
        "pmVolRatio" => Snapshot.VolRatioTod,
        "ownProfileRatio" => Snapshot.OwnProfileRatio,
        "v_1" => Snapshot.Velocity1m,
        "v_3" => Snapshot.Velocity3m,
        "v_5" => Snapshot.Velocity5m,
        "v_15" => Snapshot.Velocity15m,
        "accel" => Snapshot.Acceleration,
        "volRoc" => Snapshot.VolumeRoc,
        "abnVol_1" => Snapshot.VolumeRatioD1,
        "daysSinceReverseSplit" => Snapshot.DaysSinceReverseSplit,
        "daysSince424B" => Snapshot.DaysSince424B,
        "daysSinceS1" => Snapshot.DaysSinceS1,
        "pubToTMinutes" or "catalystAgeMinutes" => Snapshot.CatalystAgeMinutes,
        "ext0945" => Label.ExtensionFromOpen0945,
        "form4BuysD30" => Extras.Number(GapperSnapshotExtras.Form4BuysD30),
        "tradesRoc" => Extras.Number(GapperSnapshotExtras.TradesRoc),
        _ => Extras.Number(name),
    };

    /// <summary>A catalyst (news or 8-K) published inside the lookback before t.</summary>
    public bool HasCatalyst(int lookbackHours) => Snapshot.CatalystAgeMinutes is { } age && age <= lookbackHours * 60.0;
}

/// <summary>One member of U(D) with a daily-bar feature and its gapper outcome (for the hypotheses whose population is the universe, not the candidates).</summary>
public readonly record struct UniverseObservation(string Symbol, DateOnly Day, double Value, bool IsGapper, bool IsCandidate);

/// <summary>
/// Reads the study panel for one split: snapshot rows joined to labels and universe days at one
/// decision time, and the universe-population features (D-1 abnormal volume, prior-day top-mover
/// rank, Form 4 purchases) streamed one trading day at a time so a three-year split never sits in
/// memory whole.
/// </summary>
public sealed class GapperStudyPanel(GapperStudyServices s)
{
    public async Task<List<PanelRow>> LoadAsync(string split, string decisionLabel, CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var snapshots = await db.GapperFormationSnapshots.AsNoTracking()
            .Where(r => r.Source == GapperSnapshotStage.Source && r.Split == split && r.DecisionLabel == decisionLabel).ToListAsync(ct);
        if (snapshots.Count == 0) return [];
        var labels = (await db.GapperStudyLabels.AsNoTracking().Where(r => r.Split == split && r.DecisionLabel == decisionLabel).ToListAsync(ct))
            .ToDictionary(r => (r.Symbol, r.DateEt));
        var days = (await db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.Split == split && r.ExcludedReason == null && (r.IsCandidate || r.ControlForSymbol != null))
                .Select(r => new { r.Symbol, r.DateEt, r.IsCandidate, r.ControlForSymbol, r.IsGapper, r.GapTier, r.GapperSubType }).ToListAsync(ct))
            .ToDictionary(r => (r.Symbol, r.DateEt));
        var amendment = s.Amendment;
        var rows = new List<PanelRow>(snapshots.Count);
        foreach (var snap in snapshots.OrderBy(r => r.DateEt).ThenBy(r => r.Symbol, StringComparer.Ordinal))
        {
            if (!labels.TryGetValue((snap.Symbol, snap.DateEt), out var label)) continue;
            if (days.TryGetValue((snap.Symbol, snap.DateEt), out var u))
            {
                bool sampled = !u.IsCandidate && amendment is not null && split != GapperStudyServices.SmokeSplit && amendment.IsSampleMember(snap.Symbol, snap.DateEt);
                rows.Add(new PanelRow(snap.Symbol, snap.DateEt, u.IsCandidate, u.ControlForSymbol != null, u.IsGapper, u.GapTier, u.GapperSubType, snap, label, GapperSnapshotExtras.Parse(snap.ExtraJson))
                {
                    IsPopulationSample = sampled, Weight = sampled ? amendment!.SampleWeight : 1,
                });
                continue;
            }
            // Snapshot rows exist only for scoped days, so a row outside candidates and controls is a population-sample day.
            if (amendment is null || snap.IsCandidate || split == GapperStudyServices.SmokeSplit || !amendment.IsSampleMember(snap.Symbol, snap.DateEt)) continue;
            rows.Add(new PanelRow(snap.Symbol, snap.DateEt, false, false, false, null, null, snap, label, GapperSnapshotExtras.Parse(snap.ExtraJson))
            {
                IsPopulationSample = true, Weight = amendment.SampleWeight,
            });
        }
        return rows;
    }

    public async Task<List<DateOnly>> TradingDaysAsync(string split, CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        return await db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.Split == split && r.ExcludedReason == null).Select(r => r.DateEt).Distinct().OrderBy(d => d).ToListAsync(ct);
    }

    private sealed record UniverseDayRow(string Symbol, bool IsGapper, bool IsCandidate, decimal? DayVolume, decimal Adv20Shares);

    private static async Task<Dictionary<string, UniverseDayRow>> UniverseDayAsync(AppDbContext db, string split, DateOnly day, CancellationToken ct) =>
        (await db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.Split == split && r.DateEt == day && r.ExcludedReason == null)
            .Select(r => new UniverseDayRow(r.Symbol, r.IsGapper, r.IsCandidate, r.DayVolume, r.Adv20Shares)).ToListAsync(ct))
        .GroupBy(r => r.Symbol).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>abnVol_1 = volume(D-1) / ADV20(D-1) for every member of U(D) that was also in U(D-1).</summary>
    public async Task<List<UniverseObservation>> AbnormalVolumeD1Async(string split, CancellationToken ct)
    {
        var result = new List<UniverseObservation>();
        await using var db = await s.Db.CreateDbContextAsync(ct);
        Dictionary<string, UniverseDayRow>? previous = null;
        DateOnly? previousDay = null;
        foreach (var day in await TradingDaysAsync(split, ct))
        {
            var prevTrading = MarketCalendar.PreviousTradingDay(day);
            if (previousDay != prevTrading) previous = await UniverseDayAsync(db, split, prevTrading, ct);
            var today = await UniverseDayAsync(db, split, day, ct);
            foreach (var (symbol, row) in today)
            {
                if (previous is null || !previous.TryGetValue(symbol, out var p) || p.DayVolume is not { } v || p.Adv20Shares <= 0) continue;
                result.Add(new UniverseObservation(symbol, day, (double)(v / p.Adv20Shares), row.IsGapper, row.IsCandidate));
            }
            previous = today;
            previousDay = day;
        }
        return result;
    }

    /// <summary>priorDayTopMoverRank for every member of U(D): its rank by D-1 return among U(D-1), null (omitted) past <paramref name="maxRank"/>.</summary>
    public async Task<List<UniverseObservation>> PriorDayTopMoverAsync(string split, int maxRank, CancellationToken ct)
    {
        var result = new List<UniverseObservation>();
        await using var db = await s.Db.CreateDbContextAsync(ct);
        foreach (var day in await TradingDaysAsync(split, ct))
        {
            var ranks = await GapperSnapshotStage.TopMoverRanksAsync(db, day, split, ct);
            if (ranks.Count == 0) continue;
            foreach (var (symbol, row) in await UniverseDayAsync(db, split, day, ct))
                result.Add(new UniverseObservation(symbol, day, ranks.TryGetValue(symbol, out var r) && r <= maxRank ? r : maxRank + 1, row.IsGapper, row.IsCandidate));
        }
        return result;
    }

    /// <summary>Form 4 filings with a purchase line accepted in [D - lookback, decision time] for every CIK-mapped member of U(D); the coverage is the mapped share of candidate symbols.</summary>
    public async Task<(List<UniverseObservation> Rows, double CikCoverage)> Form4BuysAsync(string split, DecisionTime decision, CancellationToken ct)
    {
        var p = s.Params;
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var mapped = (await db.GapperStudyCikMap.AsNoTracking().Where(m => m.Cik != null).Select(m => m.Symbol).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var candidateSymbols = await db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.Split == split && r.IsCandidate).Select(r => r.Symbol).Distinct().ToListAsync(ct);
        double coverage = candidateSymbols.Count == 0 ? 0 : (double)candidateSymbols.Count(mapped.Contains) / candidateSymbols.Count;

        var claims = await db.ResearchClaims.AsNoTracking()
            .Where(c => c.ClaimType == Form4Submission.ClaimType && c.SourceName == Form4Submission.SourceName)
            .Select(c => new { c.Id, c.Ticker, c.SourceUrl, c.ArticleDate }).ToListAsync(ct);
        var code = p.Features.Form4.TransactionCode;
        var withPurchase = (await db.InsiderTransactions.AsNoTracking().Where(t => t.TransactionCode == code).Select(t => t.ClaimId).Distinct().ToListAsync(ct)).ToHashSet();
        var accepted = (await db.EdgarFilingIndex.AsNoTracking().Where(f => f.FormType == Form4Submission.FormType && f.AcceptedUtc != null)
                .Select(f => new { f.AccessionNumber, f.AcceptedUtc }).ToListAsync(ct))
            .GroupBy(f => f.AccessionNumber).ToDictionary(g => g.Key, g => g.First().AcceptedUtc!.Value, StringComparer.Ordinal);
        var buysBySymbol = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        foreach (var c in claims)
        {
            if (!withPurchase.Contains(c.Id)) continue;
            var accession = Form4Submission.AccessionOf(c.SourceUrl);
            var when = accession is not null && accepted.TryGetValue(accession, out var at) ? at : EasternTime.ToUtc(c.ArticleDate.ToDateTime(new TimeOnly(23, 59, 59)));
            if (!buysBySymbol.TryGetValue(c.Ticker, out var list)) buysBySymbol[c.Ticker] = list = [];
            list.Add(when);
        }

        var result = new List<UniverseObservation>();
        foreach (var day in await TradingDaysAsync(split, ct))
        {
            var decisionDay = decision.DayOffset == 0 ? day : GapperSnapshotStage.OffsetTradingDay(day, decision.DayOffset);
            var to = MinuteBarMath.ToUtc(decisionDay, decision.TimeEt);
            var from = MinuteBarMath.ToUtc(day.AddDays(-p.Features.Form4.LookbackDays), TimeSpan.Zero);
            foreach (var (symbol, row) in await UniverseDayAsync(db, split, day, ct))
            {
                if (!mapped.Contains(symbol)) continue;
                int count = buysBySymbol.TryGetValue(symbol, out var list) ? list.Count(t => t >= from && t <= to) : 0;
                result.Add(new UniverseObservation(symbol, day, count, row.IsGapper, row.IsCandidate));
            }
        }
        return (result, coverage);
    }

    /// <summary>Closes of the sessions before D, ascending, for the cost model's daily volatility.</summary>
    public async Task<List<decimal>> PriorClosesAsync(string symbol, DateOnly dateEt, int sessions, CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var from = dateEt.AddDays(-(sessions + 1) * 2);
        return await db.GapperStudyDailyBars.AsNoTracking().Where(b => b.Symbol == symbol && b.DateEt >= from && b.DateEt < dateEt)
            .OrderBy(b => b.DateEt).Select(b => b.Close).ToListAsync(ct);
    }

    /// <summary>
    /// Share of minute bars carrying an "Alpaca:sip" tag across the needed symbol-days (candidates
    /// plus controls) of the given splits, live from the file store; null when nothing is needed yet.
    /// The kill criterion is checked pre-holdout, so callers pass train and validation only.
    /// </summary>
    public async Task<double?> SipShareAsync(IEnumerable<string> splits, CancellationToken ct)
    {
        var set = splits.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var needed = await db.GapperStudyUniverseDays.AsNoTracking()
            .Where(r => set.Contains(r.Split) && (r.IsCandidate || r.ControlForSymbol != null))
            .Select(r => new { r.Symbol, r.DateEt }).ToListAsync(ct);
        if (needed.Count == 0) return null;
        var store = await s.StoreAsync(ct);
        long bars = 0, sip = 0;
        foreach (var group in needed.GroupBy(n => n.Symbol))
        {
            var days = group.Select(g => g.DateEt).ToList();
            var cov = store.Coverage(group.Key, days.Min(), days.Max());
            bars += cov.Bars;
            sip += cov.SipBars;
        }
        return bars == 0 ? null : (double)sip / bars;
    }

    public async Task<FileBackedBarStore> StoreAsync(CancellationToken ct) => await s.StoreAsync(ct);

    public static IReadOnlyList<Candle> Bars(FileBackedBarStore store, string symbol, DateOnly day) => store.ReadDay(symbol, day);
}
