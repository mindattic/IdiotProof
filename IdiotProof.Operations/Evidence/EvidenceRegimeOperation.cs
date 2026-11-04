using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Regimes;
using IdiotProof.Models;
using IdiotProof.Scripting;
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

    [Option("method", Description = "vr (default: variance-ratio z-test), bocpd (Bayesian online change points) or hmm (Gaussian hidden Markov regimes, trained on earlier sessions, filtered forward on the last).")]
    public RegimeMethod Method { get; init; } = RegimeMethod.Vr;

    [Option("threshold", Description = "bocpd: probability that a regime began within the last 5 bars needed to flag a change (default 0.5).")]
    public double Threshold { get; init; } = 0.5;

    [Option("states", Description = "hmm: hidden states, 2 or 3 (default 2).")]
    public int States { get; init; } = 2;

    [Option("aggregate", Description = "bocpd/hmm on minute bars: bar multiple to fit on (default: auto, the smallest of 1,2,3,5,10,15,30 minutes at which most bars move and tick rounding is under 10% of return variance; 1 = raw minutes).")]
    public int? Aggregate { get; init; }
}

public enum RegimeMethod { Vr, Bocpd, Hmm }

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
        if (args.Threshold is <= 0 or >= 1) return OperationResult.Usage("--threshold must be between 0 and 1.");
        if (args.States is < 2 or > 3) return OperationResult.Usage("--states must be 2 or 3.");
        if (args.Aggregate is < 1 or > 60) return OperationResult.Usage("--aggregate must be between 1 and 60.");

        var bars = (await ctx.Get<IHistoricalBarStore>().GetSplitAdjustedBarsForSymbolsAsync(symbols, from, to, EvidenceSupport.Store(args.Timeframe), ct: ctx.CancellationToken)).Bars;
        if (args.Method == RegimeMethod.Bocpd) return ChangePoints(args, symbols, bars, from, to, ctx.Width);
        if (args.Method == RegimeMethod.Hmm) return HiddenMarkov(args, symbols, bars, from, to, ctx.Width);
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

    private static OperationResult ChangePoints(EvidenceRegimeArgs args, IReadOnlyList<string> symbols,
        IReadOnlyDictionary<string, IReadOnlyList<Candle>> bars, DateOnly from, DateOnly to, int? width)
    {
        const int window = 5, warmup = 50;
        var rows = new List<object>();
        var table = new TextTable { MaxWidth = width }
            .Column("symbol").Column("bar", Align.Right).Column("bars", Align.Right).Column("changes", Align.Right).Column("last change (ET)")
            .Column("P(new regime)", Align.Right).Column("run length", Align.Right);
        foreach (var s in symbols)
        {
            var (series, k) = Prepare(bars, s, args);
            var returns = LogReturns(series);
            if (returns.Count <= warmup)
            {
                rows.Add(new { Symbol = s, AggregationMinutes = k, Bars = series.Count, Changes = 0, LastChangeUtc = (DateTime?)null, ShortRunProbability = double.NaN, MapRunLength = 0 });
                table.Row(s, $"{k}m", series.Count.ToString(), "—", "too few bars", "—", "—");
                continue;
            }
            // Prior scaled to the first warm-up returns only; detection never reads ahead of the bar it flags.
            double tickFloor = TickNoise.ReturnVarianceFloor(series.Select(c => c.Close).ToList());
            var prior = NormalGammaPrior.FromSample(returns.Take(warmup).ToList(), tickFloor);
            var events = Bocpd.Detect(returns, prior, window: window, threshold: args.Threshold, warmup: warmup, minVariance: tickFloor);
            var model = new Bocpd(prior, minVariance: tickFloor);
            foreach (var x in returns) model.Update(x);
            DateTime? last = events.Count == 0 ? null : series[Math.Min(series.Count - 1, events[^1].StartsAt + 1)].StartUtc;
            double pNow = model.ShortRunProbability(window);
            rows.Add(new
            {
                Symbol = s, AggregationMinutes = k, Bars = series.Count, Changes = events.Count, LastChangeUtc = last,
                ShortRunProbability = EvidenceSupport.Round(pNow), MapRunLength = model.MapRunLength,
            });
            table.Row(s, $"{k}m", series.Count.ToString(), events.Count.ToString(), last is { } t ? EtText(t) : "none", F(pNow), model.MapRunLength.ToString());
        }
        return OperationResult.Ok(new { From = from, To = to, Method = "bocpd", args.Threshold, Symbols = rows },
            $"{table.Render()}\nBOCPD (Adams-MacKay, Normal-Gamma, hazard 1/250) over log returns; a change is flagged when P(regime began within {window} bars) >= {args.Threshold:0.##}. Run length = bars since the most likely regime start.");
    }

    private static OperationResult HiddenMarkov(EvidenceRegimeArgs args, IReadOnlyList<string> symbols,
        IReadOnlyDictionary<string, IReadOnlyList<Candle>> bars, DateOnly from, DateOnly to, int? width)
    {
        var rows = new List<object>();
        var table = new TextTable { MaxWidth = width }
            .Column("symbol").Column("bar", Align.Right).Column("train bars", Align.Right).Column("now").Column("P(state)", Align.Right)
            .Column("states (sd/bar, stay)").Column("switches", Align.Right);
        foreach (var s in symbols)
        {
            var (series, k) = Prepare(bars, s, args);
            // Train on earlier sessions; filter the last one forward. One session only: train on its first 60%.
            int split = 0;
            if (series.Count > 0)
            {
                var lastDay = EvidenceSupport.DateEt(series[^1]);
                split = series.Count(c => EvidenceSupport.DateEt(c) < lastDay);
            }
            if (split < 20 * args.States) split = series.Count * 6 / 10;
            var trainReturns = LogReturns(series.Take(split).ToList());
            var liveReturns = LogReturns(series.Skip(Math.Max(0, split - 1)).ToList());
            // A 2-3 state HMM is not identified on a handful of bars: a quiet name aggregated to long bars may have
            // too few, and the honest answer is "too few bars at this bar size", not a degenerate fit.
            if (trainReturns.Count < MinHmmTrainBars)
            {
                rows.Add(new { Symbol = s, AggregationMinutes = k, TrainBars = trainReturns.Count, Error = $"too few bars at {k}m (need {MinHmmTrainBars})" });
                table.Row(s, $"{k}m", trainReturns.Count.ToString(), $"too few bars at {k}m", "—", "—", "—");
                continue;
            }
            var hmm = GaussianHmm.Fit(trainReturns, args.States, minVariance: TickNoise.ReturnVarianceFloor(series.Take(split).Select(c => c.Close).ToList()));
            var filtered = hmm.Filter(liveReturns);
            var now = filtered.Length == 0 ? hmm.Initial.ToArray() : filtered[^1];
            int state = Array.IndexOf(now, now.Max());
            int switches = hmm.DetectSwitches(liveReturns).Count;
            var stateText = string.Join(" | ", hmm.States.Select((st, k) => $"{Name(k, hmm.StateCount)} {st.StdDev * 100:0.000}% {hmm.Persistence(k):0.00}"));
            rows.Add(new
            {
                Symbol = s, AggregationMinutes = k, TrainBars = trainReturns.Count, State = Name(state, hmm.StateCount), Probability = EvidenceSupport.Round(now[state]), Switches = switches,
                States = hmm.States.Select((st, k) => new
                {
                    Name = Name(k, hmm.StateCount), Mean = EvidenceSupport.Round(st.Mean), StdDev = EvidenceSupport.Round(st.StdDev),
                    Persistence = EvidenceSupport.Round(hmm.Persistence(k)),
                }).ToList(),
            });
            table.Row(s, $"{k}m", trainReturns.Count.ToString(), Name(state, hmm.StateCount), F(now[state]), stateText, switches.ToString());
        }
        return OperationResult.Ok(new { From = from, To = to, Method = "hmm", args.States, Symbols = rows },
            $"{table.Render()}\nGaussian HMM: Baum-Welch on earlier bars, forward filtering only on the latest session; states ordered calm to volatile; stay = P(same state next bar).");
    }

    /// <summary>Fewest training returns the HMM is fit on.</summary>
    internal const int MinHmmTrainBars = 150;

    private static string Name(int k, int count) => count == 2 ? (k == 0 ? "calm" : "volatile") : k switch { 0 => "calm", 1 => "normal", _ => "volatile" };

    /// <summary>
    /// The symbol's filtered bars, aggregated within each session to the chosen multiple (minute bars only).
    /// A sub-$5 name whose minute closes mostly don't move is fit on bars long enough to carry real volatility.
    /// </summary>
    internal static (IReadOnlyList<Candle> Series, int Minutes) Prepare(IReadOnlyDictionary<string, IReadOnlyList<Candle>> bars, string symbol, EvidenceRegimeArgs args)
    {
        var series = bars.TryGetValue(symbol, out var raw) ? EvidenceSupport.Filter(raw, args.Timeframe, args.Session) : [];
        if (args.Timeframe != EvidenceTimeframe.Minute || series.Count == 0) return (series, 1);
        var sessions = series.GroupBy(EvidenceSupport.DateEt).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        int k = args.Aggregate ?? TickNoise.ChooseAggregation(sessions.Select(g => (IReadOnlyList<decimal>)g.Select(c => c.Close).ToList()).ToList());
        if (k <= 1) return (series, 1);
        var aggregated = new List<Candle>();
        foreach (var day in sessions)
            foreach (var chunk in day.Chunk(k))
                aggregated.Add(new Candle
                {
                    Symbol = chunk[0].Symbol, StartUtc = chunk[0].StartUtc, EndUtc = chunk[^1].EndUtc,
                    Open = chunk[0].Open, Close = chunk[^1].Close, High = chunk.Max(c => c.High), Low = chunk.Min(c => c.Low),
                    Volume = chunk.Sum(c => c.Volume), Note = chunk[0].Note,
                });
        return (aggregated, k);
    }

    private static List<double> LogReturns(IReadOnlyList<Candle> series)
    {
        var r = new List<double>(Math.Max(0, series.Count - 1));
        for (int i = 1; i < series.Count; i++)
        {
            double prev = (double)series[i - 1].Close;
            r.Add(prev > 0 ? Math.Log((double)series[i].Close / prev) : 0);
        }
        return r;
    }

    private static string EtText(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern).ToString("yyyy-MM-dd HH:mm");
}
