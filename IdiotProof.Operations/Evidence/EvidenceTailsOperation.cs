using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Dependence;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceTailsArgs
{
    [Argument(0, Name = "symbols", Description = "Two or more tickers, e.g. SNDL MULN SPY.")]
    public string[] Symbols { get; init; } = [];

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 365 days before --to for day, 10 for minute).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: today ET).")]
    public DateOnly? To { get; init; }

    [Option("timeframe", Description = "day (default) or minute.")]
    public EvidenceTimeframe Timeframe { get; init; } = EvidenceTimeframe.Day;

    [Option("q", Description = "Tail quantile: a 'crash' is a return in the bottom q (default 0.1).")]
    public double Q { get; init; } = 0.1;

    [Option("train", Description = "Share of aligned bars used to find tail-linked pairs (default 0.67); the rest is the held-out contagion test.")]
    public double Train { get; init; } = 0.67;

    [Option("permutations", Description = "Permutations for the tail-independence test (default 999).")]
    public int Permutations { get; init; } = 999;

    [Option("alpha", Description = "False-discovery rate for both the pair screen and the holdout test (default 0.05).")]
    public double Alpha { get; init; } = 0.05;

    [Option("seed", Description = "Permutation seed (default 1337).")]
    public int Seed { get; init; } = 1337;
}

/// <summary>
/// Which names crash together, and does it help? On the training share of the aligned bars each pair
/// gets Kendall's τ, Pearson correlation, the empirical lower-tail dependence λ_L(q) with a permutation
/// test, and the best of five copulas by AIC. Pairs whose λ_L survives Benjamini-Hochberg are then
/// tested out of sample: after a bottom-q bar in A, does B fall over its next bar (entered at the next
/// open) more often than B's base rate? Only that held-out lift could justify a contagion guard.
/// </summary>
[Operation("evidence tails", Summary = "Tail dependence (copulas): which symbols crash together beyond what correlation shows, and whether a crash in one predicts the next bar of the other out of sample.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence tails SNDL MULN SPY")]
[Example("idiotproof evidence tails SOXL NVDA AMD --q 0.05 --from 2025-11-01")]
public sealed class EvidenceTailsOperation : IOperation<EvidenceTailsArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceTailsArgs args, OperationContext ctx)
    {
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (symbols.Count < 2) return OperationResult.Usage("Name at least two symbols.");
        if (args.Q is <= 0 or >= 0.5) return OperationResult.Usage("--q must be between 0 and 0.5.");
        if (args.Train is <= 0.2 or >= 0.95) return OperationResult.Usage("--train must be between 0.2 and 0.95.");
        if (args.Permutations < 99) return OperationResult.Usage("--permutations must be >= 99.");
        if (args.Alpha is <= 0 or >= 1) return OperationResult.Usage("--alpha must be between 0 and 1.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx);
        var from = args.From ?? to.AddDays(args.Timeframe == EvidenceTimeframe.Day ? -365 : -10);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var adjusted = await ctx.Get<IHistoricalBarStore>().GetSplitAdjustedBarsForSymbolsAsync(symbols, from, to, EvidenceSupport.Store(args.Timeframe), ct: ctx.CancellationToken);
        var series = symbols.ToDictionary(s => s,
            s => adjusted.Bars.TryGetValue(s, out var b) ? EvidenceSupport.Filter(b, args.Timeframe, EvidenceSession.Rth) : (IReadOnlyList<Candle>)[]);

        var screened = new List<PairScreen>();
        for (int i = 0; i < symbols.Count; i++)
            for (int j = i + 1; j < symbols.Count; j++)
                if (AlignBars(series[symbols[i]], series[symbols[j]], args.Timeframe) is { } pair && pair.Returns.Count >= 30)
                    screened.Add(Screen(symbols[i], symbols[j], pair, args));

        if (screened.Count == 0)
            return OperationResult.Ok(new { From = from, To = to, Pairs = 0 }, "Not enough aligned bars for any pair (need 30+ returns).");

        var pairQ = MultipleTesting.BenjaminiHochberg(screened.Select(p => p.Tail.PValue).ToList());
        var links = new List<Contagion>();
        for (int k = 0; k < screened.Count; k++)
        {
            screened[k] = screened[k] with { TailQ = pairQ[k] };
            if (pairQ[k] >= args.Alpha) continue;
            links.Add(Holdout(screened[k].A, screened[k].B, screened[k].Pair, screened[k].Pair.ThresholdA, args.Q, a: true));
            links.Add(Holdout(screened[k].B, screened[k].A, screened[k].Pair, screened[k].Pair.ThresholdB, args.Q, a: false));
        }
        var linkQ = MultipleTesting.BenjaminiHochberg(links.Select(l => l.PValue).ToList());
        for (int k = 0; k < links.Count; k++) links[k] = links[k] with { QValue = linkQ[k] };

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("pair").Column("n", Align.Right).Column("corr", Align.Right).Column("tau", Align.Right)
            .Column($"λL({args.Q:0.##})", Align.Right).Column("q", Align.Right).Column("best copula").Column("implied λL", Align.Right);
        foreach (var p in screened.OrderBy(p => p.TailQ))
            table.Row($"{p.A}/{p.B}", p.TrainCount.ToString(), F(p.Correlation), F(p.Tau), F(p.Tail.Lambda), P(p.TailQ),
                p.Best.Family == CopulaFamily.StudentT ? $"t(ν={p.Best.Nu:0})" : p.Best.Family.ToString().ToLowerInvariant(), F(p.Best.LowerTail));

        var linkTable = new TextTable { MaxWidth = ctx.Width }
            .Column("crash in").Column("then").Column("events", Align.Right).Column("P(down | crash)", Align.Right)
            .Column("base down", Align.Right).Column("q", Align.Right).Column("verdict");
        foreach (var l in links.OrderBy(l => l.QValue))
            linkTable.Row(l.Source, l.Target, l.Events.ToString(), EvidenceSupport.Rate(l.DownAfter), EvidenceSupport.Rate(l.BaseDown), P(l.QValue),
                l.QValue < args.Alpha ? "contagion holds out of sample" : "not significant");

        string tailOnly = string.Join(", ", screened.Where(p => p.TailQ < args.Alpha && Math.Abs(p.Correlation) < 0.3).Select(p => $"{p.A}/{p.B}"));
        var human = $"Tail dependence {from:yyyy-MM-dd}..{to:yyyy-MM-dd} ({args.Timeframe.ToString().ToLowerInvariant()}, split-adjusted: {(adjusted.SplitDataAvailable ? $"{adjusted.TotalSplits} split(s)" : "no split data")})\n"
            + table.Render() + "\n"
            + (links.Count == 0 ? "No pair's lower-tail dependence survived the false-discovery screen, so nothing was tested out of sample.\n"
                                : "Held-out contagion (next bar, entered at the next open):\n" + linkTable.Render() + "\n")
            + (tailOnly.Length > 0 ? $"Tail-linked despite |corr| < 0.3: {tailOnly}.\n" : "")
            + $"λL = share of A's bottom-{args.Q:0.##} bars on which B was also in its bottom {args.Q:0.##} (independence ≈ {args.Q:0.##}); q = Benjamini-Hochberg over {screened.Count} pair(s). "
            + "Pairs are found on the first share of bars and the contagion test runs only on the rest.";

        return OperationResult.Ok(new
        {
            From = from, To = to, Timeframe = args.Timeframe.ToString().ToLowerInvariant(), args.Q, args.Train, args.Alpha,
            SplitDataAvailable = adjusted.SplitDataAvailable, SplitsApplied = adjusted.TotalSplits,
            Pairs = screened.Select(p => new
            {
                p.A, p.B, Train = p.TrainCount, Correlation = R(p.Correlation), Tau = R(p.Tau), LowerTail = R(p.Tail.Lambda),
                TailP = R(p.Tail.PValue), TailQ = R(p.TailQ), BestCopula = p.Best.Family.ToString(), Nu = p.Best.Nu, Parameter = R(p.Best.Parameter),
                ImpliedLowerTail = R(p.Best.LowerTail), ImpliedUpperTail = R(p.Best.UpperTail),
                Fits = p.Fits.Select(f => new { Family = f.Family.ToString(), Aic = R(f.Aic), f.Nu, Parameter = R(f.Parameter) }).ToList(),
            }).ToList(),
            Contagion = links.Select(l => new
            {
                l.Source, l.Target, l.Events, DownAfter = R(l.DownAfter), BaseDown = R(l.BaseDown), PValue = R(l.PValue), QValue = R(l.QValue),
                Significant = l.QValue < args.Alpha,
            }).ToList(),
        }, human);
    }

    internal sealed record AlignedPair(IReadOnlyList<Candle> BarsA, IReadOnlyList<Candle> BarsB, IReadOnlyList<(double A, double B)> Returns,
        int TrainCount, double ThresholdA, double ThresholdB);

    private sealed record PairScreen(string A, string B, AlignedPair Pair, int TrainCount, double Correlation, double Tau,
        TailEstimate Tail, CopulaFit Best, IReadOnlyList<CopulaFit> Fits, double TailQ = double.NaN);

    private sealed record Contagion(string Source, string Target, int Events, double DownAfter, double BaseDown, double PValue, double QValue = double.NaN);

    /// <summary>
    /// Bars of both symbols at the same timestamps; returns between consecutive common bars (minute
    /// returns never span the overnight gap). Index t of Returns is the move into bar t+1.
    /// </summary>
    internal static AlignedPair? AlignBars(IReadOnlyList<Candle> a, IReadOnlyList<Candle> b, EvidenceTimeframe tf)
    {
        object Key(Candle c) => tf == EvidenceTimeframe.Day ? EvidenceSupport.DateEt(c) : c.StartUtc;
        var byKey = b.GroupBy(Key).ToDictionary(g => g.Key, g => g.Last());
        var barsA = new List<Candle>();
        var barsB = new List<Candle>();
        foreach (var c in a.OrderBy(c => c.StartUtc))
            if (byKey.TryGetValue(Key(c), out var other) && c.Close > 0 && other.Close > 0) { barsA.Add(c); barsB.Add(other); }
        if (barsA.Count < 3) return null;

        var returns = new List<(double, double)>();
        for (int t = 1; t < barsA.Count; t++)
        {
            if (tf == EvidenceTimeframe.Minute && EvidenceSupport.DateEt(barsA[t]) != EvidenceSupport.DateEt(barsA[t - 1])) { returns.Add((double.NaN, double.NaN)); continue; }
            returns.Add(((double)(barsA[t].Close / barsA[t - 1].Close) - 1, (double)(barsB[t].Close / barsB[t - 1].Close) - 1));
        }
        return new AlignedPair(barsA, barsB, returns, 0, double.NaN, double.NaN);
    }

    private static PairScreen Screen(string a, string b, AlignedPair pair, EvidenceTailsArgs args)
    {
        int train = (int)(pair.Returns.Count * args.Train);
        var trainPairs = pair.Returns.Take(train).Where(r => !double.IsNaN(r.A)).ToList();
        var xs = trainPairs.Select(r => r.A).ToList();
        var ys = trainPairs.Select(r => r.B).ToList();
        var tail = Copulas.LowerTail(xs, ys, args.Q, args.Permutations, args.Seed);
        var fits = Copulas.Fit(xs, ys);
        var withThresholds = pair with { TrainCount = train, ThresholdA = Quantile(xs, args.Q), ThresholdB = Quantile(ys, args.Q) };
        return new PairScreen(a, b, withThresholds, xs.Count, EvidenceStats.Correlation(xs, ys), Copulas.KendallTau(xs, ys), tail, fits[0], fits);
    }

    /// <summary>
    /// Held-out bars only: after a return below the training threshold in the source, how often does the
    /// target lose over its next bar (entered at that bar's open)? One-sided binomial against the target's
    /// own down-rate on the other held-out bars.
    /// </summary>
    private static Contagion Holdout(string source, string target, AlignedPair pair, double threshold, double q, bool a)
    {
        var targetBars = a ? pair.BarsB : pair.BarsA;
        int events = 0, eventsDown = 0, others = 0, othersDown = 0;
        for (int t = pair.TrainCount; t < pair.Returns.Count; t++)
        {
            var (ra, rb) = pair.Returns[t];
            double src = a ? ra : rb;
            if (double.IsNaN(src)) continue;
            // Returns[t] is the move into bar t+1; the trade is entered at bar t+2's open and exits at its close.
            double next = ForwardReturnLabeler.ForwardReturn(targetBars, t + 1, 1, EntryTiming.NextOpen);
            if (double.IsNaN(next)) continue;
            if (src <= threshold) { events++; if (next < 0) eventsDown++; }
            else { others++; if (next < 0) othersDown++; }
        }
        double baseDown = others == 0 ? double.NaN : (double)othersDown / others;
        double p = events == 0 || double.IsNaN(baseDown) ? 1 : EvidenceStats.BinomialUpperTail(eventsDown, events, Math.Clamp(baseDown, 1e-6, 1 - 1e-6));
        return new Contagion(source, target, events, events == 0 ? double.NaN : (double)eventsDown / events, baseDown, p);
    }

    private static double Quantile(IReadOnlyList<double> xs, double q)
    {
        var sorted = xs.OrderBy(x => x).ToArray();
        return sorted.Length == 0 ? double.NaN : sorted[Math.Clamp((int)Math.Floor(q * sorted.Length), 0, sorted.Length - 1)];
    }

    private static string F(double v) => double.IsNaN(v) ? "—" : v.ToString("0.000");
    private static string P(double p) => double.IsNaN(p) ? "—" : p < 0.001 ? "<0.001" : p.ToString("0.000");
    private static double R(double v) => EvidenceSupport.Round(v);
}
