using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceRegimeArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. SNDL MULN.")]
    public string[] Symbols { get; init; } = [];

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 5 days before --to for minute, 120 for day).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: today ET).")]
    public DateOnly? To { get; init; }

    [Option("timeframe", Description = "minute (default) or day.")]
    public EvidenceTimeframe Timeframe { get; init; } = EvidenceTimeframe.Minute;

    [Option("q", Description = "Variance-ratio aggregation in bars (default 4).")]
    public int Q { get; init; } = 4;

    [Option("session", Description = "rth (default) or all.")]
    public EvidenceSession Session { get; init; } = EvidenceSession.Rth;
}

/// <summary>Trending, mean-reverting or random walk, decided by whether the variance ratio rejects a random walk.</summary>
[Operation("evidence regime", Summary = "Per-symbol regime: Hurst, variance ratio (z-tested vs random walk), efficiency ratio, realized vol, lag-1 autocorrelation.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence regime SNDL MULN")]
[Example("idiotproof evidence regime SPY --timeframe day --from 2026-05-01")]
public sealed class EvidenceRegimeOperation : IOperation<EvidenceRegimeArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceRegimeArgs args, OperationContext ctx)
    {
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol.");
        if (args.Q < 2) return OperationResult.Usage("--q must be >= 2.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx);
        var from = args.From ?? to.AddDays(args.Timeframe == EvidenceTimeframe.Day ? -120 : -5);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var bars = await ctx.Get<IHistoricalBarStore>().GetBarsForSymbolsAsync(symbols, from, to, EvidenceSupport.Store(args.Timeframe), ct: ctx.CancellationToken);
        var rows = new List<object>();
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("symbol").Column("regime").Column("bars", Align.Right).Column("VR", Align.Right).Column("hurst", Align.Right)
            .Column("eff", Align.Right).Column("ac1", Align.Right).Column("vol/bar", Align.Right);
        foreach (var s in symbols)
        {
            var series = bars.TryGetValue(s, out var raw) ? EvidenceSupport.Filter(raw, args.Timeframe, args.Session) : [];
            var r = RegimeStats.Read(series.Select(c => (double)c.Close).ToList(), args.Q);
            var regime = r.Regime switch
            {
                Regime.Trending => "trending",
                Regime.MeanReverting => "mean-reverting",
                Regime.RandomWalk => "random walk",
                _ => "unknown",
            };
            rows.Add(new
            {
                Symbol = s, Regime = regime, Bars = r.Samples, VarianceRatio = EvidenceSupport.Round(r.VarianceRatio), Hurst = EvidenceSupport.Round(r.Hurst),
                EfficiencyRatio = EvidenceSupport.Round(r.EfficiencyRatio), Lag1Autocorrelation = EvidenceSupport.Round(r.Lag1Autocorrelation),
                RealizedVolatility = EvidenceSupport.Round(r.RealizedVolatility),
            });
            table.Row(s, regime, r.Samples.ToString(), F(r.VarianceRatio), F(r.Hurst), F(r.EfficiencyRatio), F(r.Lag1Autocorrelation), EvidenceSupport.Rate(r.RealizedVolatility));
        }
        return OperationResult.Ok(new { From = from, To = to, Timeframe = args.Timeframe.ToString().ToLowerInvariant(), args.Q, Symbols = rows },
            $"{table.Render()}\nregime = Lo-MacKinlay VR({args.Q}) z-test at 95% (trending > 1, mean-reverting < 1); unknown = too few bars.");
    }

    private static string F(double v) => double.IsNaN(v) ? "—" : v.ToString("0.000");
}
