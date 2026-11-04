using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Microstructure;
using IdiotProof.Evidence.StateSpace;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceKalmanArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. SNDL MULN.")]
    public string[] Symbols { get; init; } = [];

    [Option("leader", Description = "Leader for the pair-residual feature (dynamic hedge ratio), e.g. SPY or IWM. Omit to skip it.")]
    public string? Leader { get; init; }

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 20 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: yesterday ET).")]
    public DateOnly? To { get; init; }

    [Option("horizon", Description = "Minute bars ahead to measure (default 5).")]
    public int Horizon { get; init; } = 5;

    [Option("bins", Description = "Quantile buckets per feature (default 5).")]
    public int Bins { get; init; } = 5;

    [Option("train", Description = "Share of sessions used to fit the filter and learn bucket edges (default 0.67); the rest is the held-out test.")]
    public double Train { get; init; } = 0.67;

    [Option("entry", Description = "next-open (default: enter at the next bar's open, immune to bid-ask bounce) or close (enter at the signal bar's close).")]
    public KalmanEntry Entry { get; init; } = KalmanEntry.NextOpen;
}

public enum KalmanEntry { NextOpen, Close }

/// <summary>
/// Kalman state-space features (trend slope, innovation surprise, pair residual against a leader)
/// scored against forward returns. The filter's noise variances and the bucket edges come from the
/// training sessions only; a feature counts as evidence only if a held-out bucket beats the base rate
/// after a Bonferroni correction.
/// </summary>
[Operation("evidence kalman", Summary = "Kalman trend slope, innovation z and leader pair-residual z bucketed against forward returns, fitted on training sessions and tested on held-out ones.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence kalman SNDL --from 2026-09-01 --to 2026-10-30")]
[Example("idiotproof evidence kalman MULN SNDL --leader IWM --horizon 10")]
public sealed class EvidenceKalmanOperation : IOperation<EvidenceKalmanArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceKalmanArgs args, OperationContext ctx)
    {
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol.");
        if (args.Horizon < 1) return OperationResult.Usage("--horizon must be >= 1.");
        if (args.Bins < 2) return OperationResult.Usage("--bins must be >= 2.");
        if (args.Train is <= 0 or >= 1) return OperationResult.Usage("--train must be between 0 and 1.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx).AddDays(-1);
        var from = args.From ?? to.AddDays(-20);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");
        var leader = string.IsNullOrWhiteSpace(args.Leader) ? null : args.Leader.Trim().ToUpperInvariant();

        var store = ctx.Get<IHistoricalBarStore>();
        var wanted = leader is null ? symbols : symbols.Append(leader).Distinct().ToList();
        var bars = await store.GetBarsForSymbolsAsync(wanted, from, to, BarTimeframe.Minute1, ct: ctx.CancellationToken);
        var leaderByTime = leader is not null && bars.TryGetValue(leader, out var lraw)
            ? EvidenceSupport.Filter(lraw, EvidenceTimeframe.Minute, EvidenceSession.Rth).GroupBy(c => c.StartUtc).ToDictionary(g => g.Key, g => Math.Log((double)g.Last().Close))
            : null;

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("symbol").Column("feature").Column("bucket", Align.Right).Column("test up", Align.Right)
            .Column("n", Align.Right).Column("lift", Align.Right).Column("adj p", Align.Right);
        var results = new List<object>();
        var notes = new List<string>();
        foreach (var symbol in symbols)
        {
            var sessions = (bars.TryGetValue(symbol, out var raw) ? EvidenceSupport.Filter(raw, EvidenceTimeframe.Minute, EvidenceSession.Rth) : [])
                .GroupBy(EvidenceSupport.DateEt).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
            if (sessions.Count < 2)
            {
                notes.Add($"{symbol}: need at least 2 sessions of minute bars, found {sessions.Count}.");
                results.Add(new { Symbol = symbol, Sessions = sessions.Count, Error = "too few sessions" });
                continue;
            }
            int trainSessions = Math.Clamp((int)Math.Round(sessions.Count * args.Train), 1, sessions.Count - 1);
            var parameters = KalmanFit.FitLocalTrend(sessions.Take(trainSessions).Select(s => (IReadOnlyList<double>)LogCloses(s)).ToList());

            var slope = new List<double>();
            var surprise = new List<double>();
            var pair = new List<double>();
            var labels = new List<ForwardLabel>();
            int trainEnd = 0;
            for (int s = 0; s < sessions.Count; s++)
            {
                if (s == trainSessions) trainEnd = slope.Count;
                var session = sessions[s];
                var logs = LogCloses(session);
                IReadOnlyList<double>? lead = leaderByTime is null ? null
                    : session.Select(c => leaderByTime.TryGetValue(c.StartUtc, out var v) ? v : double.NaN).ToList();
                var f = KalmanFeatures.Compute(logs, parameters, lead);
                int offset = slope.Count;
                slope.AddRange(f.Slope);
                surprise.AddRange(f.InnovationZ);
                pair.AddRange(f.PairResidualZ);
                var sessionLabels = args.Entry == KalmanEntry.Close ? ForwardReturnLabeler.Label(session, args.Horizon) : NextOpenLabels(session, args.Horizon);
                labels.AddRange(sessionLabels.Select(l => l with { Index = l.Index + offset }));
            }

            var evaluations = new List<FlowEvaluation>
            {
                FlowEvaluator.Evaluate("slope", slope, labels, args.Horizon, trainEnd, args.Bins),
                FlowEvaluator.Evaluate("innovation-z", surprise, labels, args.Horizon, trainEnd, args.Bins),
            };
            if (leader is not null && pair.Any(v => !double.IsNaN(v)))
                evaluations.Add(FlowEvaluator.Evaluate("pair-residual-z", pair, labels, args.Horizon, trainEnd, args.Bins));

            var features = new List<object>();
            foreach (var e in evaluations)
            {
                var tests = BucketSignificance.Test(e.Buckets.Select(b => (b.Bin, b.Test.Successes, b.Test.Samples)).ToList(), e.TestBaseRate);
                bool significant = BucketSignificance.AnySignificant(tests);
                foreach (var t in tests)
                    table.Row(symbol, e.Metric + (significant ? " *" : ""), t.Bin.ToString(),
                        t.Samples == 0 ? "—" : EvidenceSupport.Rate(t.Rate), t.Samples.ToString(),
                        double.IsNaN(t.Lift) ? "—" : t.Lift.ToString("0.00"), t.Samples == 0 ? "—" : t.AdjustedPValue.ToString("0.000"));
                features.Add(new
                {
                    e.Metric, e.TestBaseRate, e.TestSamples, Significant = significant,
                    Buckets = tests.Select(t => new { t.Bin, Up = t.Successes, N = t.Samples, t.Rate, t.Lift, t.PValue, t.AdjustedPValue }),
                });
            }
            results.Add(new
            {
                Symbol = symbol, Sessions = sessions.Count, TrainSessions = trainSessions,
                Fit = new { parameters.QLevel, parameters.QSlope, parameters.R },
                TestBaseRate = evaluations[0].TestBaseRate, Features = features,
            });
        }

        var human = $"Kalman evidence {from:yyyy-MM-dd}..{to:yyyy-MM-dd}  horizon {args.Horizon} min  entry {(args.Entry == KalmanEntry.Close ? "signal close" : "next open")}" + (leader is null ? "" : $"  leader {leader}") + "\n" +
            (notes.Count > 0 ? string.Join("\n", notes) + "\n" : "") + table.Render() +
            "\nup = forward return > 0. Filter variances (MLE) and bucket edges come from training sessions only; adj p = two-sided binomial vs the held-out base rate, Bonferroni across buckets. * = some bucket significant at 5%.";
        return OperationResult.Ok(new { From = from, To = to, args.Horizon, Entry = args.Entry.ToString(), Leader = leader, Symbols = results }, human);
    }

    /// <summary>
    /// Bar i's outcome entered at bar i+1's open and exited at bar i+h's close. A close-entry label
    /// rewards bid-ask bounce on low-priced names (a close printed at the bid "reverts" to the ask);
    /// the next open is the first price a signal on bar i could actually trade at.
    /// </summary>
    private static List<ForwardLabel> NextOpenLabels(IReadOnlyList<Candle> session, int horizon)
    {
        var labels = new List<ForwardLabel>();
        for (int i = 0; i + horizon < session.Count; i++)
        {
            double entry = (double)session[i + 1].Open;
            if (entry <= 0) continue;
            double ret = (double)session[i + horizon].Close / entry - 1;
            labels.Add(new ForwardLabel(i, ret, ret > 0 ? Direction.Up : ret < 0 ? Direction.Down : Direction.Flat, 0, 0));
        }
        return labels;
    }

    private static List<double> LogCloses(IReadOnlyList<Candle> session) => session.Select(c => Math.Log((double)c.Close)).ToList();
}
