using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Topology;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceTopologyArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. SNDL MULN.")]
    public string[] Symbols { get; init; } = [];

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 30 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: today ET).")]
    public DateOnly? To { get; init; }

    [Option("window", Description = "Closes per topology read (default 40, max 100).")]
    public int Window { get; init; } = 40;

    [Option("dim", Description = "Delay-embedding dimension (default 3).")]
    public int Dimension { get; init; } = 3;

    [Option("delay", Description = "Delay-embedding lag in bars (default 1).")]
    public int Delay { get; init; } = 1;

    [Option("horizon", Description = "Forward bars for the up/down label; reads are spaced this far apart (default 5).")]
    public int Horizon { get; init; } = 5;

    [Option("buckets", Description = "Quantile buckets per feature, learned on training days (default 4).")]
    public int Buckets { get; init; } = 4;

    [Option("holdout", Description = "Fraction of the latest trading days held out (default 0.3).")]
    public double Holdout { get; init; } = 0.3;

    [Option("min-samples", Description = "Minimum training reads in a bucket before it can be picked (default 30).")]
    public int MinSamples { get; init; } = 30;

    [Option("session", Description = "rth (default) or all.")]
    public EvidenceSession Session { get; init; } = EvidenceSession.Rth;
}

/// <summary>Do loops and clusters in delay-embedded returns predict the next move? Only a holdout-significant answer counts.</summary>
[Operation("evidence topology", Summary = "Persistent-homology features (H0/H1 persistence, landscape norm) of delay-embedded returns, learned on earlier days and re-tested on held-out days with a session-clustered test.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence topology SNDL MULN --from 2026-08-01 --to 2026-10-30")]
[Example("idiotproof evidence topology SPY --window 30 --dim 2 --delay 2 --horizon 10")]
public sealed class EvidenceTopologyOperation : IOperation<EvidenceTopologyArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceTopologyArgs args, OperationContext ctx)
    {
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol.");
        var settings = new TopologySettings(args.Window, args.Dimension, args.Delay);
        if (args.Window < 10 || args.Window > 100) return OperationResult.Usage("--window must be 10..100.");
        if (args.Dimension < 1 || args.Delay < 1) return OperationResult.Usage("--dim and --delay must be >= 1.");
        if (settings.PointsPerWindow < 3) return OperationResult.Usage("--window is too short for that --dim/--delay (fewer than 3 embedded points).");
        if (args.Horizon < 1) return OperationResult.Usage("--horizon must be >= 1.");
        if (args.Buckets < 2) return OperationResult.Usage("--buckets must be >= 2.");
        if (args.Holdout <= 0 || args.Holdout >= 1) return OperationResult.Usage("--holdout must be between 0 and 1.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx);
        var from = args.From ?? to.AddDays(-30);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var bars = await ctx.Get<IHistoricalBarStore>().GetBarsForSymbolsAsync(symbols, from, to, BarTimeframe.Minute1, ct: ctx.CancellationToken);
        var sessions = new List<SessionSeries>();
        foreach (var s in symbols)
        {
            if (!bars.TryGetValue(s, out var raw)) continue;
            foreach (var day in EvidenceSupport.Filter(raw, EvidenceTimeframe.Minute, args.Session).GroupBy(EvidenceSupport.DateEt))
                sessions.Add(new SessionSeries(day.Key, day.Select(c => (double)c.Close).ToList()));
        }
        if (sessions.Select(x => x.Day).Distinct().Count() < 2)
            return OperationResult.Ok(new { From = from, To = to, Sessions = sessions.Count }, "Need at least 2 trading days of bars to hold any out.");

        var result = TopologyEvaluator.Evaluate(sessions, settings, args.Horizon, args.Buckets, args.Holdout, args.MinSamples);

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("feature").Column("bucket").Column("train", Align.Right).Column("holdout", Align.Right)
            .Column("95% CI", Align.Right).Column("vs base", Align.Right).Column("p", Align.Right).Column("day p", Align.Right).Column("verdict");
        foreach (var f in result.Features)
        {
            string dir = f.UpBucket ? "up" : "down";
            table.Row(f.Feature, $"{f.Bucket + 1}/{f.Buckets} {dir}",
                $"{EvidenceSupport.Rate(f.TrainRate)} n={f.TrainSamples}",
                f.HoldoutSamples == 0 ? "—" : $"{f.HoldoutHits}/{f.HoldoutSamples} {EvidenceSupport.Rate(f.HoldoutRate)}",
                f.HoldoutSamples == 0 ? "—" : $"{EvidenceSupport.Rate(f.HoldoutLow)}–{EvidenceSupport.Rate(f.HoldoutHigh)}",
                EvidenceSupport.Rate(f.UpBucket ? f.HoldoutBaseRate : 1 - f.HoldoutBaseRate),
                f.BinomialP.ToString("G3"), Math.Max(f.Clustered.BootstrapPValue, f.Clustered.TPValue).ToString("G3"),
                f.Promoted ? "SIGNAL" : "not significant");
        }

        var promoted = result.Features.Where(f => f.Promoted).Select(f => f.Feature).ToList();
        string verdict = promoted.Count == 0
            ? "No topology feature beat the held-out base rate after correcting for the features tried; none is a signal."
            : $"Holdout-significant: {string.Join(", ", promoted)}.";
        string human = $"{table.Render()}\n" +
            $"train {result.TrainSessions} sessions / {result.TrainSamples} reads, holdout {result.HoldoutSessions} sessions / {result.HoldoutSamples} reads; " +
            $"each feature's most extreme training bucket re-tested on holdout, α = {0.05 / TopologyFeatures.Names.Length:0.####} per feature (0.05 / {TopologyFeatures.Names.Length}), day-clustered.\n{verdict}";

        return OperationResult.Ok(new
        {
            From = from, To = to, Symbols = symbols, args.Window, Dimension = args.Dimension, Delay = args.Delay, args.Horizon,
            result.TrainSessions, result.HoldoutSessions, result.TrainSamples, result.HoldoutSamples,
            TrainBaseRate = EvidenceSupport.Round(result.TrainBaseRate), HoldoutBaseRate = EvidenceSupport.Round(result.HoldoutBaseRate),
            Features = result.Features.Select(f => new
            {
                f.Feature, Bucket = f.Bucket + 1, f.Buckets, Direction = f.UpBucket ? "up" : "down",
                TrainRate = EvidenceSupport.Round(f.TrainRate), f.TrainSamples, TrainLift = EvidenceSupport.Round(f.TrainLift),
                f.HoldoutHits, f.HoldoutSamples, HoldoutRate = EvidenceSupport.Round(f.HoldoutRate),
                HoldoutLow = EvidenceSupport.Round(f.HoldoutLow), HoldoutHigh = EvidenceSupport.Round(f.HoldoutHigh),
                HoldoutLift = EvidenceSupport.Round(f.HoldoutLift), BinomialP = EvidenceSupport.Round(f.BinomialP),
                DayBootstrapP = EvidenceSupport.Round(f.Clustered.BootstrapPValue), DayTP = EvidenceSupport.Round(f.Clustered.TPValue),
                HoldoutDays = f.Clustered.Days, f.Alpha, f.Promoted,
            }),
            Promoted = promoted,
        }, human);
    }
}
