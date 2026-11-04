using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceSimilarArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. SNDL.")]
    public string Symbol { get; init; } = "";

    [Option("date", Description = "ET day whose latest window is matched (yyyy-MM-dd, default: today ET).")]
    public DateOnly? Date { get; init; }

    [Option("window", Description = "Bars per pattern window (default 20).")]
    public int Window { get; init; } = 20;

    [Option("horizon", Description = "Bars ahead each look-alike's outcome is measured (default 5).")]
    public int Horizon { get; init; } = 5;

    [Option("entry", Description = "next-open (default: fill at the next bar's open, immune to bid-ask bounce) or close (fill at the signal bar's close; a diagnostic).")]
    public EntryTiming Entry { get; init; } = EntryTiming.NextOpen;

    [Option("train-days", Description = "Prior sessions searched for look-alikes (default 20).")]
    public int TrainDays { get; init; } = 20;

    [Option("k", Description = "Look-alikes to list (default 15).")]
    public int K { get; init; } = 15;

    [Option("min-similarity", Description = "Least cosine similarity (default 0.8).")]
    public double MinSimilarity { get; init; } = 0.8;

    [Option("session", Description = "rth (default) or all.")]
    public EvidenceSession Session { get; init; } = EvidenceSession.Rth;
}

/// <summary>"Has this shape happened before, and what came next?" LSH look-alikes of the latest window, each with its realized outcome.</summary>
[Operation("evidence similar", Summary = "LSH look-alikes of a symbol's latest bar window from earlier sessions, with what each did next.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence similar SNDL")]
[Example("idiotproof evidence similar MULN --date 2026-10-30 --window 30 --horizon 10")]
public sealed class EvidenceSimilarOperation : IOperation<EvidenceSimilarArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceSimilarArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol.");
        if (args.Window < 3 || args.Horizon < 1 || args.TrainDays < 1 || args.K < 1) return OperationResult.Usage("--window >= 3; --horizon, --train-days, --k >= 1.");
        var day = args.Date ?? EvidenceSupport.TodayEt(ctx);

        var raw = await ctx.Get<IHistoricalBarStore>().GetBarsAsync(symbol, day.AddDays(-(args.TrainDays * 3 / 2 + 7)), day, BarTimeframe.Minute1, ctx.CancellationToken);
        var bars = EvidenceSupport.Filter(raw, EvidenceTimeframe.Minute, args.Session);
        var history = bars.Where(c => EvidenceSupport.DateEt(c) < day).ToList();
        var sessions = history.Select(EvidenceSupport.DateEt).Distinct().OrderBy(d => d).TakeLast(args.TrainDays).ToHashSet();
        history = history.Where(c => sessions.Contains(EvidenceSupport.DateEt(c))).ToList();
        var current = bars.Where(c => EvidenceSupport.DateEt(c) <= day).ToList();
        if (current.Count < args.Window)
            return OperationResult.Ok(new { Symbol = symbol, Neighbors = 0 }, $"{symbol}: fewer than {args.Window} bars up to {day:yyyy-MM-dd}.");

        var index = new LshIndex<(DateTime At, double Forward)>(args.Window);
        for (int end = args.Window - 1; end + args.Horizon < history.Count; end++)
        {
            if (EvidenceSupport.DateEt(history[end + args.Horizon]) != EvidenceSupport.DateEt(history[end])) continue;
            double fwd = ForwardReturnLabeler.ForwardReturn(history, end, args.Horizon, args.Entry);
            if (double.IsNaN(fwd)) continue;
            index.Add(ShapeVector.FromWindow(history, end, args.Window), (history[end].EndUtc, fwd));
        }
        var query = ShapeVector.FromWindow(current, current.Count - 1, args.Window);
        var hits = index.Query(query, args.K, args.MinSimilarity);

        int up = hits.Count(h => h.Payload.Forward > 0), down = hits.Count(h => h.Payload.Forward < 0);
        var ci = EvidenceStats.WilsonInterval(up, up + down);
        var rows = hits.Select(h => new { At = h.Payload.At, Similarity = Math.Round(h.Similarity, 4), Forward = Math.Round(h.Payload.Forward, 6) }).ToList();
        var data = new
        {
            Symbol = symbol, AsOf = current[^1].EndUtc, args.Window, args.Horizon, Indexed = index.Count, Neighbors = rows,
            Up = up, Down = down, UpCiLow = EvidenceSupport.Round(ci.Low), UpCiHigh = EvidenceSupport.Round(ci.High),
            MeanForward = EvidenceSupport.Round(hits.Count == 0 ? double.NaN : hits.Average(h => h.Payload.Forward)),
        };
        var head = $"{symbol} window ending {ctx.Et(current[^1].EndUtc)}: {index.Count} past windows indexed over {sessions.Count} session(s)";
        if (hits.Count == 0) return OperationResult.Ok(data, $"{head}\nNo look-alikes at similarity >= {args.MinSimilarity}.");

        var table = new TextTable { MaxWidth = ctx.Width }.Column("when (ET)").Column("sim", Align.Right).Column($"next {args.Horizon}", Align.Right);
        foreach (var r in rows) table.Row(ctx.Et(r.At), r.Similarity.ToString("0.000"), EvidenceSupport.Pct(r.Forward));
        var summary = $"{up} rose, {down} fell (up share 95% CI {EvidenceSupport.Rate(ci.Low)}–{EvidenceSupport.Rate(ci.High)}), mean {EvidenceSupport.Pct(data.MeanForward, 3)}. In-sample anecdote; `evidence walkforward` measures whether this ever predicts.";
        return OperationResult.Ok(data, $"{head}\n{table.Render()}\n{summary}");
    }
}
