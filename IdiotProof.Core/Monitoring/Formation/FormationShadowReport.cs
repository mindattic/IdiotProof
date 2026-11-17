using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Monitoring.Formation;

public sealed record ShadowFeatureDrift(string Feature, double TrainMissing, double LiveMissing);

public sealed record ShadowDay(DateOnly DateEt, int Scored, int Labeled, int Positives, int TopHits, IReadOnlyList<string> Top);

public sealed record FormationShadowResult(
    DateOnly? From, DateOnly? To, int Days, int Rows, int Labeled, int Positives,
    double? DetectorAuc, double? BenchmarkAuc, double? Difference, double? Low, double? High,
    double? TopPrecision, double? BaseRate, IReadOnlyList<ShadowFeatureDrift> Drift, IReadOnlyList<ShadowDay> PerDay,
    string Verdict, IReadOnlyList<string> Notes);

/// <summary>
/// The 20-trading-day paper shadow of the formation alert (IP-T447; gapper study amendment 06): the live recorder's
/// 08:00 rows, each scored by the frozen H11p detector, labeled afterwards with the study's own reach label (the highest
/// high by 09:45 at least +20% over the prior close, <see cref="GapperStudyLabeler.Reach"/>), then compared with the
/// study's benchmark (a catalyst 60 minutes old or newer) by day-clustered AUC, plus a check that no input is missing
/// live much more often than it was in training. Read-only.
/// </summary>
public sealed class FormationShadowReport(
    IDbContextFactory<AppDbContext> dbFactory,
    IHistoricalBarStore bars,
    FormationDetectorScorer detector,
    TimeProvider time)
{
    /// <summary>Pre-registered in amendment 06: the window length, the benchmark, the drift limit, the bootstrap.</summary>
    public const int RequiredDays = 20;
    public const double BenchmarkMaxCatalystAgeMinutes = 60;
    public const double DriftLimit = 0.20;
    public const double ReachThreshold = 0.20;
    public static readonly TimeSpan ReachByEt = new(9, 45, 0);
    public const int Draws = 10000, Seed = 1337;
    public const double CiLevel = 0.95; // the study's own level (params statistics.ciLevel)

    public async Task<FormationShadowResult> RunAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var notes = new List<string>();
        var label = FormationSchedule.Label(FormationDetector.DecisionTimeEt);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.GapperFormationSnapshots.AsNoTracking().Where(e => e.Source == FormationSnapshotRow.LiveSource && e.DecisionLabel == label);
        if (from is { } f) q = q.Where(e => e.DateEt >= f);
        if (to is { } t) q = q.Where(e => e.DateEt <= t);
        var rows = await q.OrderBy(e => e.DateEt).ThenBy(e => e.Symbol).ToListAsync(ct);
        if (!detector.Loaded) notes.Add("The detector is off (" + detector.Error + "): rows without a stored score cannot be scored.");

        var nowUtc = time.GetUtcNow().UtcDateTime;
        var scored = new List<(GapperFormationSnapshotRow Row, double Score, bool Benchmark, FormationDetectorValues Values)>();
        foreach (var r in rows)
        {
            var extras = GapperSnapshotExtras.Parse(r.ExtraJson);
            var values = FormationDetectorScorer.ValuesOf(r, extras.Number(GapperSnapshotExtras.TradesRoc));
            var score = extras.Number(FormationDetectorScorer.ScoreExtraKey) ?? detector.Score(values);
            if (score is null) continue;
            scored.Add((r, score.Value, r.CatalystAgeMinutes is { } age && age <= BenchmarkMaxCatalystAgeMinutes, values));
        }

        // outcomes, only for sessions past 09:45 ET
        var labeled = new List<(GapperFormationSnapshotRow Row, double Score, bool Benchmark, bool Reached)>();
        foreach (var day in scored.GroupBy(x => x.Row.DateEt))
        {
            if (MinuteBarMath.ToUtc(day.Key, ReachByEt).AddMinutes(1) > nowUtc) { notes.Add($"{day.Key:yyyy-MM-dd}: not labeled yet (before 09:46 ET)"); continue; }
            foreach (var x in day)
            {
                var minute = await bars.GetBarsAsync(x.Row.Symbol, day.Key, day.Key, BarTimeframe.Minute1, ct);
                var daily = await bars.GetBarsAsync(x.Row.Symbol, day.Key.AddDays(-10), day.Key.AddDays(-1), BarTimeframe.Day1, ct);
                var prevClose = daily.Count > 0 ? daily[^1].Close : 0m;
                if (GapperStudyLabeler.Reach(minute, prevClose, MinuteBarMath.ToUtc(day.Key, ReachByEt), ReachThreshold) is { } reached)
                    labeled.Add((x.Row, x.Score, x.Benchmark, reached));
            }
        }

        var perDay = labeled.GroupBy(x => x.Row.DateEt).OrderBy(g => g.Key).Select(g =>
        {
            var top = g.OrderByDescending(x => x.Score).Take(FormationDetectorScorer.AlertTop).ToList();
            return new ShadowDay(g.Key, scored.Count(s => s.Row.DateEt == g.Key), g.Count(), g.Count(x => x.Reached), top.Count(x => x.Reached),
                top.Select(x => x.Row.Symbol + (x.Reached ? "*" : "")).ToList());
        }).ToList();

        double? auc = null, bench = null, diff = null, lo = null, hi = null;
        int positives = labeled.Count(x => x.Reached);
        if (positives > 0 && positives < labeled.Count)
        {
            var cmp = Auc.CompareByDay(labeled.Select(x => x.Score).ToList(), labeled.Select(x => x.Benchmark ? 1.0 : 0.0).ToList(),
                labeled.Select(x => x.Reached).ToList(), labeled.Select(x => x.Row.DateEt).ToList(), Draws, Seed, CiLevel, minDays: 5);
            auc = cmp.AucA; bench = cmp.AucB; diff = cmp.Difference;
            lo = double.IsNaN(cmp.Low) ? null : cmp.Low; hi = double.IsNaN(cmp.High) ? null : cmp.High;
        }
        else if (labeled.Count > 0) notes.Add("Every labeled row has the same outcome: no AUC.");

        var drift = new List<ShadowFeatureDrift>();
        if (detector.Meta?.TrainMissingShare is { } trainMissing && scored.Count > 0)
            for (int j = 0; j < FormationDetector.Features.Count && j < trainMissing.Count; j++)
            {
                var live = scored.Count(x => { var v = FormationDetector.Row(x.Values)[j]; return double.IsNaN(v) || double.IsInfinity(v); }) / (double)scored.Count;
                if (live - trainMissing[j] > DriftLimit) drift.Add(new(FormationDetector.Features[j], trainMissing[j], Math.Round(live, 4)));
            }
        else if (scored.Count > 0) notes.Add("The detector file carries no train missing shares: rerun `research gappers detector-export` for the drift check.");

        var days = perDay.Count;
        var verdict = days < RequiredDays ? $"in progress ({days} of {RequiredDays} labeled sessions)"
            : drift.Count > 0 ? "FAIL: live inputs drift from training (" + string.Join(", ", drift.Select(d => d.Feature)) + ")"
            : lo is > 0 ? "PASS: the detector beats the catalyst-latency benchmark live (lower bound > 0) with inputs as complete as in training"
            : "FAIL: the detector does not beat the catalyst-latency benchmark live (lower bound <= 0)";
        double? topPrecision = perDay.Count == 0 ? null : perDay.Average(d => d.TopHits / (double)Math.Max(1, Math.Min(FormationDetectorScorer.AlertTop, d.Labeled)));
        double? baseRate = labeled.Count == 0 ? null : positives / (double)labeled.Count;
        return new(perDay.FirstOrDefault()?.DateEt, perDay.LastOrDefault()?.DateEt, days, scored.Count, labeled.Count, positives,
            auc, bench, diff, lo, hi, topPrecision, baseRate, drift, perDay, verdict, notes);
    }
}
