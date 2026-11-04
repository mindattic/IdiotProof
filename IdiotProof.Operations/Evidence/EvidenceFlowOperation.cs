using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Microstructure;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Evidence;

public enum FlowSigning { Tick, LeeReady, Bulk }

public sealed record EvidenceFlowArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. SNDL.")]
    public string Symbol { get; init; } = "";

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 10 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: yesterday ET).")]
    public DateOnly? To { get; init; }

    [Option("horizon", Description = "Minute bars ahead to measure (default 5).")]
    public int Horizon { get; init; } = 5;

    [Option("bins", Description = "Quantile buckets per metric (default 5).")]
    public int Bins { get; init; } = 5;

    [Option("signing", Description = "tick (default: tick rule on trades), lee-ready (trades vs quote midpoint; fetches quotes) or bulk (bar-only bulk classification).")]
    public FlowSigning Signing { get; init; } = FlowSigning.Tick;

    [Option("train", Description = "Share of sessions used to learn bucket edges (default 0.67); the rest is the held-out test.")]
    public double Train { get; init; } = 0.67;

    [Option("vpin-window", Description = "VPIN buckets averaged (default 50).")]
    public int VpinWindow { get; init; } = 50;

    [Option("lambda-window", Description = "Bars per Kyle's lambda regression (default 30).")]
    public int LambdaWindow { get; init; } = 30;
}

/// <summary>
/// Measures whether order flow says anything about the next move: per-minute imbalance, VPIN, Kyle's
/// lambda and absorption, each bucketed on the training sessions and scored on held-out sessions.
/// </summary>
[Operation("evidence flow", Summary = "Order-flow evidence: imbalance, VPIN, Kyle's lambda and absorption bucketed against forward returns, scored on held-out sessions.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence flow SNDL --from 2026-10-20 --to 2026-10-31")]
[Example("idiotproof evidence flow MULN --signing bulk --horizon 10")]
public sealed class EvidenceFlowOperation : IOperation<EvidenceFlowArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceFlowArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol.");
        if (args.Horizon < 1) return OperationResult.Usage("--horizon must be >= 1.");
        if (args.Bins < 2) return OperationResult.Usage("--bins must be >= 2.");
        if (args.Train is <= 0 or >= 1) return OperationResult.Usage("--train must be between 0 and 1.");
        if (args.VpinWindow < 1 || args.LambdaWindow < 3) return OperationResult.Usage("--vpin-window must be >= 1 and --lambda-window >= 3.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx).AddDays(-1);
        var from = args.From ?? to.AddDays(-10);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var raw = await ctx.Get<IHistoricalBarStore>().GetBarsAsync(symbol, from, to, BarTimeframe.Minute1, ctx.CancellationToken);
        var sessions = EvidenceSupport.Filter(raw, EvidenceTimeframe.Minute, EvidenceSession.Rth)
            .GroupBy(EvidenceSupport.DateEt).OrderBy(g => g.Key).Select(g => (Day: g.Key, Bars: g.ToList())).ToList();
        if (sessions.Count < 2)
            return OperationResult.Ok(new { Symbol = symbol, Sessions = sessions.Count }, $"{symbol}: need at least 2 sessions of minute bars, found {sessions.Count}.");

        int trainSessions = Math.Clamp((int)Math.Round(sessions.Count * args.Train), 1, sessions.Count - 1);
        // VPIN bucket size from training sessions only: 1/50th of the median training-session volume.
        var trainVolumes = sessions.Take(trainSessions).Select(s => s.Bars.Sum(b => (double)b.Volume)).OrderBy(v => v).ToList();
        double bucketVolume = Math.Max(1, trainVolumes[trainVolumes.Count / 2] / 50);

        var ticks = args.Signing == FlowSigning.Bulk ? null : ctx.Services.GetService<ITickStore>() ?? new AlpacaTickStore(ctx);
        var imbalance = new List<double>();
        var vpin = new List<double>();
        var lambda = new List<double>();
        var absorption = new List<int>();
        var labels = new List<ForwardLabel>();
        int trainEnd = 0;
        long trades = 0;
        for (int s = 0; s < sessions.Count; s++)
        {
            var bars = sessions[s].Bars;
            if (s == trainSessions) trainEnd = imbalance.Count;
            BarFlow[] flows;
            if (ticks is null) flows = OrderFlow.BulkClassify(bars);
            else
            {
                var prints = await ticks.GetTradesAsync(symbol, sessions[s].Day, ctx.CancellationToken);
                trades += prints.Count;
                var signs = args.Signing == FlowSigning.LeeReady
                    ? TradeSigner.LeeReady(prints, await ticks.GetQuotesAsync(symbol, sessions[s].Day, ctx.CancellationToken))
                    : TradeSigner.TickRule(prints);
                flows = OrderFlow.PerBar(bars, prints, signs);
            }
            int offset = imbalance.Count;
            imbalance.AddRange(flows.Select(f => f.Imbalance));
            vpin.AddRange(FlowMetrics.Vpin(flows, bucketVolume, args.VpinWindow));
            lambda.AddRange(FlowMetrics.RollingKylesLambda(bars, flows, args.LambdaWindow));
            absorption.AddRange(FlowMetrics.Absorption(bars, flows).Select(a => a + 1));
            labels.AddRange(ForwardReturnLabeler.Label(bars, args.Horizon).Select(l => l with { Index = l.Index + offset }));
        }

        var evaluations = new[]
        {
            FlowEvaluator.Evaluate("imbalance", imbalance, labels, args.Horizon, trainEnd, args.Bins),
            FlowEvaluator.Evaluate("vpin", vpin, labels, args.Horizon, trainEnd, args.Bins),
            FlowEvaluator.Evaluate("kyle-lambda", lambda, labels, args.Horizon, trainEnd, args.Bins),
            FlowEvaluator.EvaluateCategories("absorption", absorption, labels, args.Horizon, trainEnd, 3),
        };

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("metric").Column("bucket", Align.Right).Column("train up", Align.Right).Column("n", Align.Right)
            .Column("test up", Align.Right).Column("95% CI", Align.Right).Column("n", Align.Right).Column("lift", Align.Right);
        foreach (var e in evaluations)
            foreach (var b in e.Buckets)
            {
                var lift = e.TestBaseRate > 0 && b.Test.Samples > 0 ? (double)b.Test.Successes / b.Test.Samples / e.TestBaseRate : double.NaN;
                table.Row(e.Metric, b.Bin.ToString(), EvidenceSupport.Rate((double)b.Train.Successes / Math.Max(1, b.Train.Samples)), b.Train.Samples.ToString(),
                    b.Test.Samples == 0 ? "—" : EvidenceSupport.Rate((double)b.Test.Successes / b.Test.Samples),
                    b.Test.Samples == 0 ? "—" : $"{b.Test.Low * 100:0.0}–{b.Test.High * 100:0.0}%", b.Test.Samples.ToString(),
                    double.IsNaN(lift) ? "—" : lift.ToString("0.00"));
            }

        string signing = args.Signing switch { FlowSigning.LeeReady => "Lee-Ready", FlowSigning.Bulk => "bulk volume classification", _ => "tick rule" };
        var data = new
        {
            Symbol = symbol, From = from, To = to, args.Horizon, Signing = signing, Sessions = sessions.Count, TrainSessions = trainSessions,
            Trades = trades, BucketVolume = Math.Round(bucketVolume),
            Metrics = evaluations.Select(e => new
            {
                e.Metric, e.Bins, e.TrainBaseRate, e.TestBaseRate, e.TrainSamples, e.TestSamples,
                Buckets = e.Buckets.Select(b => new
                {
                    b.Bin, TrainUp = b.Train.Successes, TrainN = b.Train.Samples,
                    TestUp = b.Test.Successes, TestN = b.Test.Samples, TestLow = b.Test.Low, TestHigh = b.Test.High,
                }),
            }),
        };
        var testBase = evaluations[0].TestBaseRate;
        return OperationResult.Ok(data,
            $"{symbol} order flow {from:yyyy-MM-dd}..{to:yyyy-MM-dd}  {sessions.Count} sessions ({trainSessions} train / {sessions.Count - trainSessions} test)  signing: {signing}" +
            (ticks is null ? "" : $"  {trades:N0} trades") + $"\nup = forward return > 0 after {args.Horizon} min; held-out base rate {EvidenceSupport.Rate(testBase)}\n" +
            table.Render() +
            "\nBucket edges come from training sessions only; a bucket matters only if its test CI excludes the base rate. Absorption buckets: 0 = buying absorbed, 1 = none, 2 = selling absorbed.");
    }
}
