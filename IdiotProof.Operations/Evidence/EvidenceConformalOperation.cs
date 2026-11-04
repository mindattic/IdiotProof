using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Conformal;
using IdiotProof.Evidence.StateSpace;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

public enum ConformalModel { Analog, Kalman }

public sealed record EvidenceConformalArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. SNDL MULN.")]
    public string[] Symbols { get; init; } = [];

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 40 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: yesterday ET).")]
    public DateOnly? To { get; init; }

    [Option("model", Description = "analog: the mean forward return of LSH look-alike windows from prior sessions. kalman: the local-trend filter's slope × horizon, fitted on the warm-up sessions.")]
    public ConformalModel Model { get; init; } = ConformalModel.Analog;

    [Option("alpha", Description = "Miss rate the intervals target (default 0.1 = 90% intervals).")]
    public double Alpha { get; init; } = 0.1;

    [Option("gamma", Description = "Adaptive conformal learning rate (default 0.005).")]
    public double Gamma { get; init; } = 0.005;

    [Option("horizon", Description = "Minute bars ahead (default 15).")]
    public int Horizon { get; init; } = 15;

    [Option("window", Description = "Bars per pattern window / filter burn-in (default 30).")]
    public int Window { get; init; } = 30;

    [Option("train-days", Description = "Analog: prior sessions searched for look-alikes (default 20).")]
    public int TrainDays { get; init; } = 20;

    [Option("warmup-days", Description = "Sessions before the first forecast; kalman fits its noise variances on them (default 5).")]
    public int WarmupDays { get; init; } = 5;

    [Option("min-similarity", Description = "Analog: least cosine similarity of a look-alike (default 0.8).")]
    public double MinSimilarity { get; init; } = 0.8;

    [Option("entry", Description = "next-open (default: fill at the next bar's open, immune to bid-ask bounce) or close (fill at the signal bar's close; a diagnostic).")]
    public EntryTiming Entry { get; init; } = EntryTiming.NextOpen;
}

/// <summary>
/// Conformal prediction intervals around a forward-return forecast, run strictly online per symbol:
/// each interval is built only from outcomes that had resolved by its call time. Reports empirical
/// coverage against the nominal 1-α for split conformal and Adaptive Conformal Inference, per month,
/// and scores the abstain rule "call only when the adaptive interval excludes zero" with the
/// day-clustered significance tests.
/// </summary>
[Operation("evidence conformal", Summary = "Split and adaptive conformal intervals around analog or Kalman forward-return forecasts: coverage vs nominal per month, and an abstain-unless-the-interval-excludes-zero rule scored with day-clustered significance.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence conformal SNDL MULN --from 2026-08-03 --to 2026-10-30")]
[Example("idiotproof evidence conformal SNDL --model kalman --alpha 0.2 --horizon 10")]
public sealed class EvidenceConformalOperation : IOperation<EvidenceConformalArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceConformalArgs args, OperationContext ctx)
    {
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol.");
        if (args.Alpha is <= 0 or >= 1) return OperationResult.Usage("--alpha must be between 0 and 1.");
        if (args.Gamma is <= 0 or >= 1) return OperationResult.Usage("--gamma must be between 0 and 1.");
        if (args.Horizon < 1 || args.Window < 3 || args.TrainDays < 1 || args.WarmupDays < 1)
            return OperationResult.Usage("--horizon, --train-days, --warmup-days >= 1; --window >= 3.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx).AddDays(-1);
        var from = args.From ?? to.AddDays(-40);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var bars = await ctx.Get<IHistoricalBarStore>().GetBarsForSymbolsAsync(symbols, from, to, BarTimeframe.Minute1, ct: ctx.CancellationToken);
        var results = new List<ConformalResult>();
        var notes = new List<string>();
        var perSymbol = new List<object>();
        foreach (var symbol in symbols)
        {
            var sessions = (bars.TryGetValue(symbol, out var raw) ? EvidenceSupport.Filter(raw, EvidenceTimeframe.Minute, EvidenceSession.Rth) : [])
                .GroupBy(EvidenceSupport.DateEt).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
            if (sessions.Count <= args.WarmupDays)
            {
                notes.Add($"{symbol}: {sessions.Count} session(s), need more than --warmup-days {args.WarmupDays}.");
                continue;
            }
            var points = args.Model == ConformalModel.Kalman ? KalmanPoints(sessions, args) : AnalogPoints(sessions, args);
            var run = ConformalRun.Run(points, args.Alpha, args.Gamma);
            results.AddRange(run);
            var s = ConformalSummary.Of(run);
            perSymbol.Add(new { Symbol = symbol, s.Points, SplitCoverage = R(s.SplitCoverage), AdaptiveCoverage = R(s.AdaptiveCoverage), Calls = s.Abstain.Calls, HitRate = R(s.Abstain.HitRate) });
        }

        if (results.Count == 0)
            return OperationResult.Ok(new { Points = 0, Notes = notes }, "No forecasts: " + (notes.Count > 0 ? string.Join(" ", notes) : "no bars in range."));

        var overall = ConformalSummary.Of(results);
        var clustered = ClusteredSignificance.Evaluate(overall.DayTallies);
        var months = results.GroupBy(r => $"{r.Point.Day:yyyy-MM}").OrderBy(g => g.Key)
            .Select(g => (Month: g.Key, Summary: ConformalSummary.Of(g.ToList()))).ToList();

        double nominal = 1 - args.Alpha;
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("month").Column("points", Align.Right).Column("split cov", Align.Right).Column("ACI cov", Align.Right)
            .Column("ACI width", Align.Right).Column("calls", Align.Right).Column("hit rate", Align.Right);
        foreach (var (month, s) in months)
            table.Row(month, s.Points.ToString(), EvidenceSupport.Rate(s.SplitCoverage), EvidenceSupport.Rate(s.AdaptiveCoverage),
                EvidenceSupport.Pct(s.AdaptiveMeanWidth, 3), s.Abstain.Calls.ToString(), EvidenceSupport.Rate(s.Abstain.HitRate));

        var a = overall.Abstain;
        var (lo, hi) = a.HitRateInterval;
        var block = new KeyValueBlock()
            .Add("forecasts", $"{overall.Points} ({args.Model.ToString().ToLowerInvariant()}, horizon {args.Horizon} min, {(args.Entry == EntryTiming.NextOpen ? "next-open" : "close")} entry), nominal coverage {EvidenceSupport.Rate(nominal)}")
            .Add("split conformal", $"coverage {EvidenceSupport.Rate(overall.SplitCoverage)} (unbounded {overall.SplitUnbounded}), mean width {EvidenceSupport.Pct(overall.SplitMeanWidth, 3)}")
            .Add("adaptive (ACI)", $"coverage {EvidenceSupport.Rate(overall.AdaptiveCoverage)} (unbounded {overall.AdaptiveUnbounded}), mean width {EvidenceSupport.Pct(overall.AdaptiveMeanWidth, 3)}, γ {args.Gamma}")
            .Add("abstain rule", a.Calls == 0
                ? "no call: no adaptive interval excluded zero"
                : $"{a.Hits}/{a.Calls} hits ({EvidenceSupport.Rate(a.HitRate)}, 95% CI {EvidenceSupport.Rate(lo)}–{EvidenceSupport.Rate(hi)}), coverage {EvidenceSupport.Rate(a.Coverage)}, edge {EvidenceSupport.Pct(a.MeanEdge, 3)}/call before cost")
            .Add("day-clustered", a.Calls == 0 ? "—" : $"{clustered.Days} days, bootstrap p {P(clustered.BootstrapPValue)}, t-test p {P(clustered.TPValue)} vs 50% — {(clustered.IsSignificant() ? "significant" : "not significant")}");

        var human = block.Render() + "\n" + table.Render() +
            "\nEach interval is built only from outcomes resolved by its call time (per symbol). Coverage counts an unbounded interval as covering, as the guarantees do. " +
            "The abstain rule calls up/down only when the ACI interval lies entirely above/below zero; a verdict needs both day-clustered tests below 0.05." +
            (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "");

        return OperationResult.Ok(new
        {
            From = from, To = to, Model = args.Model.ToString().ToLowerInvariant(), args.Alpha, args.Gamma, args.Horizon, Entry = args.Entry.ToString(),
            Points = overall.Points, Nominal = nominal,
            SplitCoverage = R(overall.SplitCoverage), SplitUnbounded = overall.SplitUnbounded, SplitMeanWidth = R(overall.SplitMeanWidth),
            AdaptiveCoverage = R(overall.AdaptiveCoverage), AdaptiveUnbounded = overall.AdaptiveUnbounded, AdaptiveMeanWidth = R(overall.AdaptiveMeanWidth),
            Abstain = new { a.Calls, a.Hits, HitRate = R(a.HitRate), Coverage = R(a.Coverage), MeanEdge = R(a.MeanEdge) },
            Clustered = new { clustered.Days, BootstrapP = R(clustered.BootstrapPValue), TP = R(clustered.TPValue), Significant = clustered.IsSignificant() },
            Months = months.Select(m => new
            {
                m.Month, m.Summary.Points, SplitCoverage = R(m.Summary.SplitCoverage), AdaptiveCoverage = R(m.Summary.AdaptiveCoverage),
                AdaptiveMeanWidth = R(m.Summary.AdaptiveMeanWidth), m.Summary.Abstain.Calls, HitRate = R(m.Summary.Abstain.HitRate),
            }).ToList(),
            Symbols = perSymbol, Notes = notes,
        }, human);
    }

    /// <summary>Analog forecasts: for session D, a model trained only on the prior sessions; calls spaced by the horizon.</summary>
    internal static List<ConformalPoint> AnalogPoints(IReadOnlyList<List<Candle>> sessions, EvidenceConformalArgs args)
    {
        var points = new List<ConformalPoint>();
        for (int d = args.WarmupDays; d < sessions.Count; d++)
        {
            var prior = sessions.Skip(Math.Max(0, d - args.TrainDays)).Take(d - Math.Max(0, d - args.TrainDays)).ToList();
            var training = prior.SelectMany(s => s).ToList();
            var model = new PatternAnalogPredictor(args.Window, args.Horizon, entry: args.Entry);
            int start = 0;
            foreach (var s in prior)
            {
                model.Train(training, start, start + s.Count, labelLimit: start + s.Count);
                start += s.Count;
            }
            if (model.Count == 0) continue;

            var tail = training.Skip(Math.Max(0, training.Count - (args.Window - 1))).ToList();
            var test = tail.Concat(sessions[d]).ToList();
            var day = EvidenceSupport.DateEt(sessions[d][0]);
            for (int end = Math.Max(tail.Count, args.Window - 1); end + args.Horizon < test.Count; end += args.Horizon)
            {
                var vote = model.Predict(test, end, minSimilarity: args.MinSimilarity, minNeighbors: 1);
                if (vote.Neighbors == 0) continue;
                double outcome = ForwardReturnLabeler.ForwardReturn(test, end, args.Horizon, args.Entry);
                if (double.IsNaN(outcome)) continue;
                points.Add(new ConformalPoint(day, test[end].EndUtc, test[end + args.Horizon].EndUtc, vote.MeanForwardReturn, outcome));
            }
        }
        return points;
    }

    /// <summary>Kalman forecasts: noise variances fitted on the warm-up sessions only; forecast = exp(slope × horizon) − 1.</summary>
    internal static List<ConformalPoint> KalmanPoints(IReadOnlyList<List<Candle>> sessions, EvidenceConformalArgs args)
    {
        var parameters = KalmanFit.FitLocalTrend(sessions.Take(args.WarmupDays)
            .Select(s => (IReadOnlyList<double>)s.Select(c => Math.Log((double)c.Close)).ToList()).ToList());
        var points = new List<ConformalPoint>();
        for (int d = args.WarmupDays; d < sessions.Count; d++)
        {
            var session = sessions[d];
            var day = EvidenceSupport.DateEt(session[0]);
            var filter = parameters.CreateFilter();
            for (int i = 0; i < session.Count; i++)
            {
                var step = filter.Step(Math.Log((double)session[i].Close));
                if (i < args.Window - 1 || (i - (args.Window - 1)) % args.Horizon != 0 || i + args.Horizon >= session.Count) continue;
                double outcome = ForwardReturnLabeler.ForwardReturn(session, i, args.Horizon, args.Entry);
                if (double.IsNaN(outcome)) continue;
                points.Add(new ConformalPoint(day, session[i].EndUtc, session[i + args.Horizon].EndUtc, Math.Exp(step.Slope * args.Horizon) - 1, outcome));
            }
        }
        return points;
    }

    private static string P(double p) => double.IsNaN(p) ? "—" : p < 0.001 ? "<0.001" : p.ToString("0.000");
    private static double R(double v) => EvidenceSupport.Round(v);
}
