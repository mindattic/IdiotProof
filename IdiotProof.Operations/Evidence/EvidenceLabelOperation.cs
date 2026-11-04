using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceLabelArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. SNDL.")]
    public string Symbol { get; init; } = "";

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 20 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: yesterday ET).")]
    public DateOnly? To { get; init; }

    [Option("horizon", Description = "Bars ahead to measure (default 5).")]
    public int Horizon { get; init; } = 5;

    [Option("timeframe", Description = "minute (default) or day.")]
    public EvidenceTimeframe Timeframe { get; init; } = EvidenceTimeframe.Minute;

    [Option("dead-band", Description = "Absolute return counted as flat, as a fraction (default 0.001).")]
    public double DeadBand { get; init; } = 0.001;

    [Option("session", Description = "rth (default) or all.")]
    public EvidenceSession Session { get; init; } = EvidenceSession.Rth;
}

/// <summary>The measured distribution of what happened h bars after each bar: the base rate any pattern must beat.</summary>
[Operation("evidence label", Summary = "Forward-return distribution for a symbol (up/down/flat rates, quantiles, excursions): the base rate a pattern must beat.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence label SNDL --from 2026-10-01 --to 2026-10-31 --horizon 10")]
[Example("idiotproof evidence label AAPL --timeframe day --from 2026-01-02 --horizon 5")]
public sealed class EvidenceLabelOperation : IOperation<EvidenceLabelArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceLabelArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol.");
        if (args.Horizon < 1) return OperationResult.Usage("--horizon must be >= 1.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx).AddDays(-1);
        var from = args.From ?? to.AddDays(-20);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var raw = await ctx.Get<IHistoricalBarStore>().GetBarsAsync(symbol, from, to, EvidenceSupport.Store(args.Timeframe), ctx.CancellationToken);
        var bars = EvidenceSupport.Filter(raw, args.Timeframe, args.Session);
        // Minute labels stay inside one session: a label crossing the overnight gap measures the gap, not the pattern.
        var labels = args.Timeframe == EvidenceTimeframe.Day
            ? ForwardReturnLabeler.Label(bars, args.Horizon, args.DeadBand)
            : bars.GroupBy(EvidenceSupport.DateEt).SelectMany(g => ForwardReturnLabeler.Label(g.ToList(), args.Horizon, args.DeadBand)).ToList();
        if (labels.Count == 0)
            return OperationResult.Ok(new { Symbol = symbol, Labels = 0 }, $"{symbol}: no labelable bars between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.");

        var rets = labels.Select(l => l.ForwardReturn).OrderBy(r => r).ToList();
        int up = labels.Count(l => l.Direction == Direction.Up), down = labels.Count(l => l.Direction == Direction.Down);
        int flat = labels.Count - up - down;
        double Q(double q) => rets[Math.Clamp((int)Math.Round(q * (rets.Count - 1)), 0, rets.Count - 1)];
        var upCi = EvidenceStats.WilsonInterval(up, up + down);

        var data = new
        {
            Symbol = symbol, From = from, To = to, args.Horizon, Timeframe = args.Timeframe.ToString().ToLowerInvariant(),
            Labels = labels.Count, Up = up, Down = down, Flat = flat,
            UpShareOfDecided = EvidenceSupport.Round(up + down == 0 ? double.NaN : (double)up / (up + down)),
            UpCiLow = EvidenceSupport.Round(upCi.Low), UpCiHigh = EvidenceSupport.Round(upCi.High),
            Mean = EvidenceSupport.Round(EvidenceStats.Mean(rets)), StdDev = EvidenceSupport.Round(EvidenceStats.StdDev(rets)),
            P05 = Q(0.05), P25 = Q(0.25), Median = Q(0.5), P75 = Q(0.75), P95 = Q(0.95),
            MeanMaxAdverse = EvidenceSupport.Round(labels.Average(l => l.MaxAdverse)),
            MeanMaxFavorable = EvidenceSupport.Round(labels.Average(l => l.MaxFavorable)),
        };
        var block = new KeyValueBlock()
            .Add("bars", $"{labels.Count} labeled, {args.Horizon} {data.Timeframe} bar(s) ahead, {from:yyyy-MM-dd} .. {to:yyyy-MM-dd} ET")
            .Add("direction", $"up {up} · down {down} · flat {flat} (up share of decided {EvidenceSupport.Rate((double)up / Math.Max(1, up + down))}, 95% CI {EvidenceSupport.Rate(upCi.Low)}–{EvidenceSupport.Rate(upCi.High)})")
            .Add("return", $"mean {EvidenceSupport.Pct(data.Mean, 3)}, sd {EvidenceSupport.Pct(data.StdDev, 3)}")
            .Add("quantiles", $"5% {EvidenceSupport.Pct(data.P05)} · 25% {EvidenceSupport.Pct(data.P25)} · 50% {EvidenceSupport.Pct(data.Median)} · 75% {EvidenceSupport.Pct(data.P75)} · 95% {EvidenceSupport.Pct(data.P95)}")
            .Add("excursion", $"average worst {EvidenceSupport.Pct(data.MeanMaxAdverse)}, average best {EvidenceSupport.Pct(data.MeanMaxFavorable)} within the horizon");
        return OperationResult.Ok(data, $"{symbol}\n{block.Render()}");
    }
}
