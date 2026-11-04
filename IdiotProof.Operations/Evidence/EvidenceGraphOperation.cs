using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceGraphArgs
{
    [Argument(0, Name = "symbols", Description = "Two or more tickers, e.g. SNDL TLRY CGC ACB.")]
    public string[] Symbols { get; init; } = [];

    [Option("source", Description = "Symbol whose shock is propagated (default: the first symbol).")]
    public string? Source { get; init; }

    [Option("lag", Description = "0 = moves together (default); 1 = the source's bar predicts the other's next bar.")]
    public int Lag { get; init; }

    [Option("edge", Description = "correlation (default) or te: transfer entropy beating shuffled surrogates (directional, implies --lag 1).")]
    public EvidenceEdge Edge { get; init; } = EvidenceEdge.Correlation;

    [Option("alpha", Description = "With --edge te, the surrogate-test significance an edge must reach (default 0.05).")]
    public double Alpha { get; init; } = 0.05;

    [Option("clean", Description = "none (default) or rmt: keep only correlation the data can tell from chance (lag 0: Marchenko-Pastur eigenvalue clipping; lag 1: the 2.5/sqrt(T) noise floor). Correlation edges only; --edge te already tests each edge against shuffled surrogates.")]
    public EvidenceClean Clean { get; init; } = EvidenceClean.None;

    [Option("min-corr", Description = "Least |correlation| that makes an edge (default 0.2).")]
    public double MinCorr { get; init; } = 0.2;

    [Option("beta", Description = "Per-hop decay of influence, 0-1 exclusive (default 0.5).")]
    public double Beta { get; init; } = 0.5;

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 5 days before --to for minute, 120 for day).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: today ET).")]
    public DateOnly? To { get; init; }

    [Option("timeframe", Description = "minute (default) or day.")]
    public EvidenceTimeframe Timeframe { get; init; } = EvidenceTimeframe.Minute;

    [Option("session", Description = "rth (default) or all.")]
    public EvidenceSession Session { get; init; } = EvidenceSession.Rth;
}

/// <summary>The measured "strings" between symbols and how far a shock at one travels, weakening every hop.</summary>
[Operation("evidence graph", Summary = "Correlation graph across symbols (lag 0 or 1) and the hop-decayed influence of a shock at a source symbol.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence graph SNDL TLRY CGC ACB --source TLRY")]
[Example("idiotproof evidence graph SPY IWM MULN --lag 1 --source SPY")]
public sealed class EvidenceGraphOperation : IOperation<EvidenceGraphArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceGraphArgs args, OperationContext ctx)
    {
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (symbols.Count < 2) return OperationResult.Usage("Name at least two symbols.");
        if (args.Lag is < 0 or > 1) return OperationResult.Usage("--lag must be 0 or 1.");
        if (args.Alpha is <= 0 or >= 1) return OperationResult.Usage("--alpha must be between 0 and 1 (exclusive).");
        bool te = args.Edge == EvidenceEdge.Te;
        int lag = te ? 1 : args.Lag;
        bool rmt = args.Clean == EvidenceClean.Rmt;
        if (te && rmt) return OperationResult.Usage(EvidenceSupport.CleanWithTeMessage);
        if (args.Beta is <= 0 or >= 1) return OperationResult.Usage("--beta must be between 0 and 1 (exclusive).");
        var source = (args.Source ?? symbols[0]).Trim().ToUpperInvariant();
        if (!symbols.Contains(source)) return OperationResult.Usage($"--source {source} is not one of the symbols.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx);
        var from = args.From ?? to.AddDays(args.Timeframe == EvidenceTimeframe.Day ? -120 : -5);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var bars = await ctx.Get<IHistoricalBarStore>().GetBarsForSymbolsAsync(symbols, from, to, EvidenceSupport.Store(args.Timeframe), ct: ctx.CancellationToken);
        var closes = symbols.ToDictionary(s => s, s => (bars.TryGetValue(s, out var raw) ? EvidenceSupport.Filter(raw, args.Timeframe, args.Session) : [])
            .GroupBy(c => c.StartUtc).ToDictionary(g => g.Key, g => (double)g.First().Close));

        // Align on bar times every symbol traded; returns between consecutive shared bars, never across sessions.
        var shared = closes.Values.Select(d => d.Keys.AsEnumerable()).Aggregate((a, b) => a.Intersect(b)).OrderBy(t => t).ToList();
        if (shared.Count < 10)
            return OperationResult.Ok(new { Symbols = symbols, SharedBars = shared.Count }, $"Only {shared.Count} bar times shared by every symbol; need at least 10.");
        var returns = new Dictionary<string, double[]>();
        foreach (var s in symbols)
        {
            var list = new List<double>();
            for (int i = 1; i < shared.Count; i++)
            {
                bool sameDay = args.Timeframe == EvidenceTimeframe.Day ||
                               DateOnly.FromDateTime(EasternClock.ToEastern(shared[i])) == DateOnly.FromDateTime(EasternClock.ToEastern(shared[i - 1]));
                list.Add(sameDay ? closes[s][shared[i]] / closes[s][shared[i - 1]] - 1 : 0);
            }
            returns[s] = list.ToArray();
        }

        var graph = te ? InfluenceGraph.FromTransferEntropy(returns, alpha: args.Alpha)
            : rmt ? InfluenceGraph.FromCleanedReturns(returns, lag, args.MinCorr)
            : InfluenceGraph.FromReturns(returns, lag, args.MinCorr);
        var influence = graph.Influence(source, args.Beta);
        var edges = new List<object>();
        var edgeTable = new TextTable { MaxWidth = ctx.Width }.Column("from").Column("to").Column(te ? "te bits" : "corr", Align.Right);
        foreach (var a in graph.Symbols)
        foreach (var b in graph.Symbols)
        {
            if (a == b) continue;
            if (lag == 0 && string.CompareOrdinal(a, b) > 0) continue;
            var w = graph.Edge(a, b);
            if (w == 0) continue;
            edges.Add(new { From = a, To = b, Weight = Math.Round(w, 4) });
            edgeTable.Row(a, b, w.ToString("+0.000;-0.000"));
        }
        var infRows = influence.OrderByDescending(kv => Math.Abs(kv.Value))
            .Select(kv => new { Symbol = kv.Key, Influence = Math.Round(kv.Value, 4), Hops = graph.HopDistance(source, kv.Key) }).ToList();
        var infTable = new TextTable { MaxWidth = ctx.Width }.Column("symbol").Column("influence", Align.Right).Column("hops", Align.Right);
        foreach (var r in infRows) infTable.Row(r.Symbol, r.Influence.ToString("+0.000;-0.000"), r.Hops < 0 ? "—" : r.Hops.ToString());

        var data = new { From = from, To = to, Lag = lag, Edge = te ? "te" : "correlation", Clean = rmt ? "rmt" : "none", args.MinCorr, args.Alpha, args.Beta, Source = source, SharedBars = shared.Count, Edges = edges, Influence = infRows };
        var human = (te ? $"{shared.Count} shared bars, lag 1, transfer entropy p < {args.Alpha} vs shuffled surrogates\n"
                        : $"{shared.Count} shared bars, lag {lag}, |corr| >= {args.MinCorr}" +
                          (rmt ? (lag == 0 ? ", RMT-cleaned (Marchenko-Pastur clipping)" : ", RMT noise floor 2.5/sqrt(T)") : "") + "\n") +
                    (edges.Count == 0 ? "No edges above the threshold.\n" : edgeTable.Render() + "\n") +
                    $"\nShock at {source} (beta {args.Beta} per hop):\n{infTable.Render()}";
        return OperationResult.Ok(data, human);
    }
}
