using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Microstructure;
using IdiotProof.Evidence.PointProcess;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceHawkesArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. SNDL.")]
    public string Symbol { get; init; } = "";

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 10 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: yesterday ET).")]
    public DateOnly? To { get; init; }

    [Option("horizon", Description = "Minute bars ahead to measure (default 5).")]
    public int Horizon { get; init; } = 5;

    [Option("bins", Description = "Quantile buckets for the continuous features (default 5).")]
    public int Bins { get; init; } = 5;

    [Option("train", Description = "Share of scored sessions used to learn bucket edges (default 0.67); the rest is the held-out test.")]
    public double Train { get; init; } = 0.67;

    [Option("window", Description = "Trailing minutes the detector looks at (default 10).")]
    public int Window { get; init; } = 10;

    [Option("spike", Description = "Trade-rate multiple of the prior session's baseline that counts as a spike (default 3).")]
    public double Spike { get; init; } = 3;

    [Option("branching", Description = "Rolling branching ratio that counts as self-exciting (default 0.7).")]
    public double Branching { get; init; } = 0.7;
}

/// <summary>
/// Fits a Hawkes self-exciting process to each session's trade arrivals and tests a momentum-ignition
/// detector against what price did next. The detector's baseline for a session is the PREVIOUS
/// session's fit, so the first session is warm-up only and nothing is scored with its own day's fit.
/// </summary>
[Operation("evidence hawkes", Summary = "Hawkes self-exciting fit of trade arrivals (branching ratio = endogenous share) and a momentum-ignition detector scored against forward returns on held-out sessions.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence hawkes SNDL --from 2026-10-20 --to 2026-10-31")]
[Example("idiotproof evidence hawkes MULN --window 5 --spike 4 --branching 0.8")]
public sealed class EvidenceHawkesOperation : IOperation<EvidenceHawkesArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceHawkesArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol.");
        if (args.Horizon < 1) return OperationResult.Usage("--horizon must be >= 1.");
        if (args.Bins < 2) return OperationResult.Usage("--bins must be >= 2.");
        if (args.Train is <= 0 or >= 1) return OperationResult.Usage("--train must be between 0 and 1.");
        if (args.Window < 1) return OperationResult.Usage("--window must be >= 1.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx).AddDays(-1);
        var from = args.From ?? to.AddDays(-10);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var raw = await ctx.Get<IHistoricalBarStore>().GetBarsAsync(symbol, from, to, BarTimeframe.Minute1, ctx.CancellationToken);
        var sessions = EvidenceSupport.Filter(raw, EvidenceTimeframe.Minute, EvidenceSession.Rth)
            .GroupBy(EvidenceSupport.DateEt).OrderBy(g => g.Key).Select(g => (Day: g.Key, Bars: g.ToList())).ToList();
        if (sessions.Count < 3)
            return OperationResult.Ok(new { Symbol = symbol, Sessions = sessions.Count },
                $"{symbol}: need at least 3 sessions of minute bars (one warm-up, then train and test), found {sessions.Count}.");

        var ticks = ctx.Services.GetService<ITickStore>() ?? new AlpacaTickStore(ctx);
        var detector = new IgnitionDetector { WindowMinutes = args.Window, SpikeThreshold = args.Spike, BranchingThreshold = args.Branching };

        int scored = sessions.Count - 1;
        int trainSessions = Math.Clamp((int)Math.Round(scored * args.Train), 1, scored - 1);
        var fits = new List<(DateOnly Day, HawkesFit Fit)>();
        var intensity = new List<double>();
        var branching = new List<double>();
        var ignition = new List<int>();
        var labels = new List<ForwardLabel>();
        int trainEnd = 0, ignitionBars = 0;
        HawkesFit? previous = null;

        for (int s = 0; s < sessions.Count; s++)
        {
            var bars = sessions[s].Bars;
            var prints = await ticks.GetTradesAsync(symbol, sessions[s].Day, ctx.CancellationToken);
            var origin = bars[0].StartUtc;
            double horizon = Math.Max(1, (bars[^1].EndUtc - origin).TotalSeconds);
            var times = prints.Select(p => (p.TimeUtc - origin).TotalSeconds).Where(t => t >= 0 && t < horizon).OrderBy(t => t).ToList();
            var fit = HawkesProcess.Fit(times, horizon);
            fits.Add((sessions[s].Day, fit));

            if (previous is not null)
            {
                if (s == trainSessions + 1) trainEnd = intensity.Count;
                double baseline = previous.Params.UnconditionalRate;
                var readings = detector.Read(bars, prints, baseline);
                int offset = intensity.Count;
                intensity.AddRange(readings.Select(r => r.IntensityRatio));
                branching.AddRange(readings.Select(r => r.BranchingRatio));
                ignition.AddRange(readings.Select(r => r.Ignition ? 1 : 0));
                ignitionBars += readings.Count(r => r.Ignition);
                labels.AddRange(ForwardReturnLabeler.Label(bars, args.Horizon).Select(l => l with { Index = l.Index + offset }));
            }
            previous = fit;
        }

        var evaluations = new[]
        {
            FlowEvaluator.Evaluate("intensity-ratio", intensity, labels, args.Horizon, trainEnd, args.Bins),
            FlowEvaluator.Evaluate("branching", branching, labels, args.Horizon, trainEnd, args.Bins),
            FlowEvaluator.EvaluateCategories("ignition", ignition, labels, args.Horizon, trainEnd, 2),
        };

        var fitTable = new TextTable { MaxWidth = ctx.Width }
            .Column("day").Column("trades", Align.Right).Column("μ/s", Align.Right).Column("α", Align.Right)
            .Column("β", Align.Right).Column("n", Align.Right).Column("LR vs Poisson", Align.Right).Column("role");
        for (int i = 0; i < fits.Count; i++)
        {
            var (day, f) = fits[i];
            string role = i == 0 ? "warm-up" : i <= trainSessions ? "train" : "test";
            fitTable.Row(day.ToString("yyyy-MM-dd"), f.Events.ToString("N0"), f.Params.Mu.ToString("0.000"), f.Params.Alpha.ToString("0.000"),
                f.Params.Beta.ToString("0.000"), f.Params.BranchingRatio.ToString("0.00"), f.LikelihoodRatio.ToString("0.0"), role);
        }

        var bucketTable = new TextTable { MaxWidth = ctx.Width }
            .Column("feature").Column("bucket", Align.Right).Column("test up", Align.Right).Column("95% CI", Align.Right)
            .Column("n", Align.Right).Column("p vs base", Align.Right).Column("verdict");
        var scoredBuckets = new List<object>();
        foreach (var e in evaluations)
            foreach (var b in e.Buckets)
            {
                double p = b.Test.Samples == 0 ? 1 : TwoSidedP(b.Test.Successes, b.Test.Samples, e.TestBaseRate);
                string verdict = b.Test.Samples == 0 ? "no data" : p < 0.05 ? "significant" : "not significant";
                bucketTable.Row(e.Metric, b.Bin.ToString(),
                    b.Test.Samples == 0 ? "—" : EvidenceSupport.Rate((double)b.Test.Successes / b.Test.Samples),
                    b.Test.Samples == 0 ? "—" : $"{b.Test.Low * 100:0.0}–{b.Test.High * 100:0.0}%",
                    b.Test.Samples.ToString(), b.Test.Samples == 0 ? "—" : p.ToString("0.000"), verdict);
                scoredBuckets.Add(new { e.Metric, b.Bin, TestUp = b.Test.Successes, TestN = b.Test.Samples, b.Test.Low, b.Test.High, P = p, Verdict = verdict });
            }

        var data = new
        {
            Symbol = symbol, From = from, To = to, args.Horizon, Sessions = sessions.Count, TrainSessions = trainSessions,
            TestSessions = scored - trainSessions, IgnitionBars = ignitionBars,
            Fits = fits.Select(f => new
            {
                Day = f.Day, f.Fit.Events, f.Fit.Params.Mu, f.Fit.Params.Alpha, f.Fit.Params.Beta,
                Branching = f.Fit.Params.BranchingRatio, f.Fit.LikelihoodRatio,
            }),
            TestBaseRate = evaluations[0].TestBaseRate,
            Buckets = scoredBuckets,
        };
        return OperationResult.Ok(data,
            $"{symbol} Hawkes {from:yyyy-MM-dd}..{to:yyyy-MM-dd}  {sessions.Count} sessions (1 warm-up, {trainSessions} train, {scored - trainSessions} test)  ignition bars: {ignitionBars}\n" +
            "n = branching ratio, the share of trades triggered by earlier trades rather than outside arrivals.\n" +
            fitTable.Render() +
            $"\nup = forward return > 0 after {args.Horizon} min; held-out base rate {EvidenceSupport.Rate(evaluations[0].TestBaseRate)}\n" +
            bucketTable.Render() +
            "\nEach session's detector baseline is the previous session's fit; bucket edges come from training sessions only. " +
            "p is two-sided exact binomial against the held-out base rate, uncorrected for the number of buckets shown.");
    }

    /// <summary>Exact two-sided binomial p-value of k successes in n trials against rate p0 (doubling the smaller tail).</summary>
    internal static double TwoSidedP(int k, int n, double p0)
    {
        if (n == 0 || double.IsNaN(p0)) return 1;
        double upper = EvidenceStats.BinomialUpperTail(k, n, p0);
        double lower = 1 - EvidenceStats.BinomialUpperTail(k + 1, n, p0);
        return Math.Min(1, 2 * Math.Min(upper, lower));
    }
}
