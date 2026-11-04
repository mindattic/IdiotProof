using IdiotProof.Evidence.Information;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Operations.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Evidence;

public enum EvidenceUniverse { None, Penny }

public enum EvidenceModel { Analog, Leaders }

public sealed record EvidenceWalkForwardArgs
{
    [Argument(0, Name = "symbols", Description = "Tickers to test, e.g. SNDL MULN (omit when using --universe).", Required = false)]
    public string[] Symbols { get; init; } = [];

    [Option("universe", Description = "penny: each day's point-in-time under-$5 universe (prior-day close, dollar-volume floor), drawn from active AND delisted Alpaca equities.")]
    public EvidenceUniverse Universe { get; init; } = EvidenceUniverse.None;

    [Option("from", Description = "First ET day to evaluate (yyyy-MM-dd).", Required = true)]
    public DateOnly From { get; init; }

    [Option("to", Description = "Last ET day to evaluate (yyyy-MM-dd, default: yesterday ET).")]
    public DateOnly? To { get; init; }

    [Option("model", Description = "analog: LSH look-alike windows vote on direction. leaders: follow the influence-weighted move of the symbol's lag-1 leaders, discovered among --leaders from prior sessions only.")]
    public EvidenceModel Model { get; init; } = EvidenceModel.Analog;

    [Option("leaders", Description = "With --model leaders, the symbols leaders are discovered among (default SPY,QQQ,IWM).")]
    public string Leaders { get; init; } = "SPY,QQQ,IWM";

    [Option("edge", Description = "With --model leaders, how leader edges are measured: correlation (default) or te (transfer entropy vs shuffled surrogates).")]
    public EvidenceEdge Edge { get; init; } = EvidenceEdge.Correlation;

    [Option("clean", Description = "With --model leaders and correlation edges: none (default) or rmt, keeping only lag-1 correlations above the random-matrix noise floor 2.5/sqrt(T).")]
    public EvidenceClean Clean { get; init; } = EvidenceClean.None;

    [Option("leader-hops", Description = "With --model leaders, most links between a leader and the symbol (default 2).")]
    public int LeaderHops { get; init; } = 2;

    [Option("leader-min-influence", Description = "With --model leaders, least |influence| a leader needs to count (default 0.05).")]
    public double LeaderMinInfluence { get; init; } = 0.05;

    [Option("window", Description = "Bars per pattern window (default 20).")]
    public int Window { get; init; } = 20;

    [Option("horizon", Description = "Bars ahead the call is scored on; calls are spaced this far apart so outcomes never overlap (default 5).")]
    public int Horizon { get; init; } = 5;

    [Option("train-days", Description = "Prior sessions each day's model trains on (default 20).")]
    public int TrainDays { get; init; } = 20;

    [Option("min-similarity", Description = "Least cosine similarity for a look-alike to count (default 0.8).")]
    public double MinSimilarity { get; init; } = 0.8;

    [Option("min-neighbors", Description = "Fewest look-alikes needed to make a call (default 8).")]
    public int MinNeighbors { get; init; } = 8;

    [Option("min-agreement", Description = "Smoothed share of look-alikes that must agree to call (default 0.6).")]
    public double MinAgreement { get; init; } = 0.6;

    [Option("cost", Description = "Flat round-trip cost per call as a fraction (e.g. 0.003). Omit it to model each symbol-day's cost from its prior sessions: Corwin-Schultz spread + square-root impact for --shares.")]
    public double? Cost { get; init; }

    [Option("shares", Description = "Shares per call for the modeled impact cost (default 1, the default order size).")]
    public double Shares { get; init; } = 1;

    [Option("volume", Description = "Include the volume shape in the pattern vector.")]
    public bool Volume { get; init; }

    [Option("session", Description = "rth (default) or all (adds pre/after-hours bars).")]
    public EvidenceSession Session { get; init; } = EvidenceSession.Rth;

    [Option("max-symbols-per-day", Description = "With --universe, keep each day's most liquid N members (default 30).")]
    public int MaxSymbolsPerDay { get; init; } = 30;

    [Option("max-candidates", Description = "With --universe, most candidate symbols screened (default 4000; inactive first, then active).")]
    public int MaxCandidates { get; init; } = 4000;
}

/// <summary>
/// The honest test of "can it call direction better than a coin": a strictly out-of-sample,
/// walk-forward run with a binomial test against 50%, a Wilson interval, how often the model
/// abstained, and edge before and after costs, next to the always-up base rate it must beat.
/// </summary>
[Operation("evidence walkforward", Summary = "Out-of-sample walk-forward directional test (LSH analogs) with binomial p, Wilson CI, coverage and edge after costs.", Risk = OperationRisk.Read,
    Description = "Each day's model trains only on earlier sessions; calls are spaced by --horizon so outcomes never overlap. Analysis only; arms nothing.")]
[Example("idiotproof evidence walkforward --universe penny --from 2026-08-03 --to 2026-10-30", Description = "Penny-stock (< $5) test across the point-in-time universe.")]
[Example("idiotproof evidence walkforward SNDL --from 2026-10-01 --horizon 10")]
public sealed class EvidenceWalkForwardOperation : IOperation<EvidenceWalkForwardArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceWalkForwardArgs args, OperationContext ctx)
    {
        var to = args.To ?? EvidenceSupport.TodayEt(ctx).AddDays(-1);
        if (to < args.From) return OperationResult.Usage("--to must be on or after --from.");
        if (args.Window < 3 || args.Horizon < 1 || args.TrainDays < 1) return OperationResult.Usage("--window must be >= 3, --horizon and --train-days >= 1.");
        if (args.MinAgreement is <= 0.5 or > 1) return OperationResult.Usage("--min-agreement must be in (0.5, 1].");
        if (args.Cost is < 0) return OperationResult.Usage("--cost must be >= 0.");
        if (args.Clean == EvidenceClean.Rmt && args.Model != EvidenceModel.Leaders)
            return OperationResult.Usage("--clean rmt applies to --model leaders.");
        if (args.Clean == EvidenceClean.Rmt && args.Edge == EvidenceEdge.Te)
            return OperationResult.Usage(EvidenceSupport.CleanWithTeMessage);
        if (args.Shares <= 0) return OperationResult.Usage("--shares must be > 0.");
        if (args.Model == EvidenceModel.Leaders && (string.IsNullOrWhiteSpace(args.Leaders) || args.LeaderHops < 1 || args.LeaderMinInfluence < 0))
            return OperationResult.Usage("--model leaders needs --leaders symbols, --leader-hops >= 1 and --leader-min-influence >= 0.");
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (args.Universe == EvidenceUniverse.None && symbols.Count == 0)
            return OperationResult.Usage("Name symbols or pass --universe penny.");
        if (args.Universe != EvidenceUniverse.None && symbols.Count > 0)
            return OperationResult.Usage("Pass symbols or --universe, not both.");

        var store = ctx.Get<IHistoricalBarStore>();
        var plan = new Dictionary<string, SortedSet<DateOnly>>(StringComparer.Ordinal);
        var notes = new List<string>();
        int universeDays = 0, candidateCount = 0, inactiveCandidates = 0;

        if (args.Universe == EvidenceUniverse.Penny)
        {
            var source = ctx.Services.GetService<IEvidenceCandidateSource>() ?? new AlpacaAssetCandidateSource(ctx);
            var all = await source.GetCandidatesAsync(ctx.CancellationToken);
            var candidates = all.OrderBy(c => c.Active).ThenBy(c => c.Symbol, StringComparer.Ordinal).Take(args.MaxCandidates).ToList();
            candidateCount = candidates.Count;
            inactiveCandidates = candidates.Count(c => !c.Active);
            if (all.Count > candidates.Count)
                notes.Add($"Candidates capped at {candidates.Count} of {all.Count} (all inactive first, then active alphabetically); raise --max-candidates for full coverage.");
            if (inactiveCandidates == 0)
                notes.Add("No delisted/inactive candidates were available: results carry survivorship bias.");

            for (var d = args.From; d <= to; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var members = await store.GetPointInTimeUniverseAsync(d, candidates.Select(c => c.Symbol), ct: ctx.CancellationToken);
                if (members.Count == 0) continue;
                universeDays++;
                foreach (var m in members.OrderByDescending(m => m.AvgDollarVolume).Take(args.MaxSymbolsPerDay))
                {
                    if (!plan.TryGetValue(m.Symbol, out var set)) plan[m.Symbol] = set = new SortedSet<DateOnly>();
                    set.Add(d);
                }
            }
        }
        else
        {
            foreach (var s in symbols)
            {
                var set = new SortedSet<DateOnly>();
                for (var d = args.From; d <= to; d = d.AddDays(1))
                    if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) set.Add(d);
                plan[s] = set;
            }
        }

        var settings = new WalkForwardSettings
        {
            Window = args.Window, Horizon = args.Horizon, TrainDays = args.TrainDays, MinSimilarity = args.MinSimilarity,
            MinNeighbors = args.MinNeighbors, MinAgreement = args.MinAgreement, RoundTripCost = args.Cost ?? 0.003, ModelCosts = args.Cost is null, Shares = args.Shares,
            IncludeVolume = args.Volume, Session = args.Session, Model = args.Model,
            LeaderCandidates = EvidenceSupport.Symbols(args.Leaders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            LeaderLookback = 5, LeaderMaxHops = args.LeaderHops, LeaderMinInfluence = args.LeaderMinInfluence,
            LeaderEdge = args.Edge == EvidenceEdge.Te ? EdgeMeasure.TransferEntropy : EdgeMeasure.Correlation,
            LeaderCleanRmt = args.Clean == EvidenceClean.Rmt,
        };
        var report = await new EvidenceWalkForward(store).RunAsync(plan, settings, ctx.CancellationToken);
        var o = report.Overall;
        string costLabel = args.Cost is { } flat ? EvidenceSupport.Rate(flat) : $"modeled {EvidenceSupport.Pct(o.MeanCost, 3)} mean";
        var (lo, hi) = o.HitRateInterval;
        int decided = report.UpOutcomes + report.DownOutcomes;
        double upBase = decided == 0 ? double.NaN : (double)report.UpOutcomes / decided;
        double bestNaive = double.IsNaN(upBase) ? double.NaN : Math.Max(upBase, 1 - upBase);
        notes.Add("The naive binomial p treats every call as independent; same-day calls share market moves, so the verdict uses the day-clustered tests (block bootstrap over days + t-test on daily hit rates).");

        var tallies = report.DayTallies();
        var clustered = ClusteredSignificance.Evaluate(tallies, 0.5);
        var clustered51 = ClusteredSignificance.Evaluate(tallies, 0.51);
        var months = tallies.GroupBy(t => $"{t.Day:yyyy-MM}").OrderBy(g => g.Key)
            .Select(g =>
            {
                int c = g.Sum(t => t.Calls), h = g.Sum(t => t.Hits);
                return new WalkForwardMonth(g.Key, g.Count(t => t.Calls > 0), c, h, c == 0 ? double.NaN : (double)h / c);
            }).ToList();

        string verdict = o.Calls == 0
            ? "No calls were made; nothing to judge."
            : clustered.IsSignificant() && o.HitRate > 0.5
                ? (o.HitRate >= 0.51
                    ? $"Beats 51% with clustered significance vs a coin (bootstrap p={clustered.BootstrapPValue:G3}, day t-test p={clustered.TPValue:G3})"
                    : $"Beats a coin with clustered significance (bootstrap p={clustered.BootstrapPValue:G3}) but the hit rate is under 51%")
                  + (o.MeanEdgeAfterCost() > 0
                    ? $"; edge stays positive after {costLabel} round-trip cost."
                    : $"; the edge does not survive {costLabel} round-trip cost.")
                : $"Not significant once calls are clustered by day (bootstrap p={clustered.BootstrapPValue:G3}, day t-test p={clustered.TPValue:G3}, naive p={o.PValue:G3}): this run does not show the model calling direction better than chance.";

        var data = new
        {
            Model = args.Model.ToString().ToLowerInvariant(),
            Universe = args.Universe.ToString().ToLowerInvariant(),
            From = args.From, To = to, settings.Window, settings.Horizon, settings.TrainDays, settings.MinSimilarity,
            settings.MinNeighbors, settings.MinAgreement, Cost = args.Cost, ModeledCosts = args.Cost is null, MeanCost = EvidenceSupport.Round(o.MeanCost), Session = args.Session.ToString().ToLowerInvariant(),
            UniverseDays = universeDays, Candidates = candidateCount, InactiveCandidates = inactiveCandidates,
            o.Calls, o.Hits, HitRate = EvidenceSupport.Round(o.HitRate), CiLow = EvidenceSupport.Round(lo), CiHigh = EvidenceSupport.Round(hi),
            PValue = o.PValue, Coverage = EvidenceSupport.Round(o.Coverage), MeanEdge = EvidenceSupport.Round(o.MeanEdge),
            MeanEdgeAfterCost = EvidenceSupport.Round(o.MeanEdgeAfterCost()),
            EvaluationPoints = report.EvaluationPoints, UpBaseRate = EvidenceSupport.Round(upBase), BestNaiveHitRate = EvidenceSupport.Round(bestNaive),
            report.SkippedSymbolDays, Folds = report.Folds.Count,
            ClusteredDays = clustered.Days, DayMeanHitRate = EvidenceSupport.Round(clustered.DayMeanHitRate),
            ClusteredBootstrapP = clustered.BootstrapPValue, ClusteredTP = clustered.TPValue,
            ClusteredCiLow = EvidenceSupport.Round(clustered.BootstrapLow), ClusteredCiHigh = EvidenceSupport.Round(clustered.BootstrapHigh),
            Clustered51BootstrapP = clustered51.BootstrapPValue, Clustered51TP = clustered51.TPValue,
            Months = months, Symbols = report.Symbols, Verdict = verdict, Notes = notes,
        };

        var block = new KeyValueBlock()
            .Add("model", args.Model == EvidenceModel.Leaders
                ? $"leaders: among {string.Join(",", settings.LeaderCandidates)}, within {args.LeaderHops} hops, |influence| >= {args.LeaderMinInfluence}, move over {settings.LeaderLookback} bars, horizon {args.Horizon}, discovered on {args.TrainDays} prior sessions, {(args.Edge == EvidenceEdge.Te ? "transfer-entropy" : args.Clean == EvidenceClean.Rmt ? "RMT-cleaned lag-1 correlation" : "lag-1 correlation")} edges"
                : $"{data.Model}: window {args.Window}, horizon {args.Horizon}, train {args.TrainDays} sessions, sim >= {args.MinSimilarity}, >= {args.MinNeighbors} neighbors, agree >= {args.MinAgreement}")
            .Add("range", $"{args.From:yyyy-MM-dd} .. {to:yyyy-MM-dd} ET ({args.Session.ToString().ToLowerInvariant()})" +
                 (args.Universe == EvidenceUniverse.Penny ? $", penny universe on {universeDays} days from {candidateCount} candidates ({inactiveCandidates} inactive)" : $", {plan.Count} symbol(s)"))
            .Add("calls", $"{o.Hits}/{o.Calls} hits = {EvidenceSupport.Rate(o.HitRate)} (95% CI {EvidenceSupport.Rate(lo)}–{EvidenceSupport.Rate(hi)}), p = {o.PValue:G3} vs 50%")
            .Add("clustered", $"{clustered.Days} called days: bootstrap p = {clustered.BootstrapPValue:G3} (95% {EvidenceSupport.Rate(clustered.BootstrapLow)}–{EvidenceSupport.Rate(clustered.BootstrapHigh)}), day t-test p = {clustered.TPValue:G3}; vs 51%: bootstrap p = {clustered51.BootstrapPValue:G3}, t p = {clustered51.TPValue:G3}")
            .Add("months", string.Join("; ", months.Select(m => $"{m.Month} {m.Hits}/{m.Calls} = {EvidenceSupport.Rate(m.HitRate)} over {m.Days}d")))
            .Add("coverage", $"{EvidenceSupport.Rate(o.Coverage)} of {report.EvaluationPoints} evaluation points called; {report.Folds.Count} symbol-days, {report.SkippedSymbolDays} skipped (no bars or too little history)")
            .Add("base rate", $"up {EvidenceSupport.Rate(upBase)} of decided outcomes; a constant guess would hit {EvidenceSupport.Rate(bestNaive)}")
            .Add("edge", $"{EvidenceSupport.Pct(o.MeanEdge, 3)} per call before cost, {EvidenceSupport.Pct(o.MeanEdgeAfterCost(), 3)} after {costLabel}")
            .Add("verdict", verdict);

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("symbol").Column("days", Align.Right).Column("calls", Align.Right).Column("hit%", Align.Right)
            .Column("p", Align.Right).Column("edge", Align.Right);
        foreach (var s in report.Symbols.Where(s => s.Calls > 0).OrderByDescending(s => s.Calls).Take(25))
            table.Row(s.Symbol, s.Days.ToString(), s.Calls.ToString(), EvidenceSupport.Rate(s.HitRate), s.PValue.ToString("G3"), EvidenceSupport.Pct(s.MeanEdge, 3));

        var human = block.Render() + (report.Symbols.Any(s => s.Calls > 0) ? "\n\n" + table.Render() : "") + "\n\n" + string.Join("\n", notes.Select(n => "note: " + n));
        return OperationResult.Ok(data, human);
    }
}
