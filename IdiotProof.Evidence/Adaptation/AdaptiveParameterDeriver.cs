using IdiotProof.Models;

namespace IdiotProof.Evidence.Adaptation;

/// <summary>Exit parameters measured from one ticker's own recent bars, with the evidence behind them.</summary>
public sealed record DerivedParameters(
    double AtrPercent,
    double StopLossPercent,
    double TrailingStopPercent,
    double TakeProfitPercent,
    int Samples,
    int Horizon,
    double StopQuantile,
    RegimeReading Regime);

/// <summary>
/// Derives stop / trail / target per ticker from what its price actually did (IP-US-S3), not from a
/// house default: over every closed bar, measure the next <c>h</c> bars' maximum adverse excursion,
/// deepest pullback from the running high, and maximum favorable excursion. The stop sits at the
/// <c>q</c>-quantile of adverse excursions (so only 1-q of entries would be shaken out by ordinary
/// noise), the trail at the same quantile of pullbacks, and the target at the median favorable run.
/// </summary>
public static class AdaptiveParameterDeriver
{
    public static DerivedParameters Derive(IReadOnlyList<Candle> closed, int horizon = 10, double stopQuantile = 0.8,
        double minPercent = 0.25)
    {
        if (horizon < 1) throw new ArgumentOutOfRangeException(nameof(horizon));
        var mae = new List<double>();
        var pullback = new List<double>();
        var mfe = new List<double>();
        for (int i = 0; i + horizon < closed.Count; i++)
        {
            double entry = (double)closed[i].Close;
            if (entry <= 0) continue;
            double low = double.MaxValue, high = entry, deepestPullback = 0;
            for (int j = i + 1; j <= i + horizon; j++)
            {
                low = Math.Min(low, (double)closed[j].Low);
                deepestPullback = Math.Max(deepestPullback, (high - (double)closed[j].Low) / high);
                high = Math.Max(high, (double)closed[j].High);
            }
            mae.Add(Math.Max(0, (entry - low) / entry) * 100);
            pullback.Add(deepestPullback * 100);
            mfe.Add(Math.Max(0, (high - entry) / entry) * 100);
        }

        double atrPct = AtrPercent(closed);
        var closes = closed.Select(c => (double)c.Close).ToList();
        var regime = RegimeStats.Read(closes);
        return new DerivedParameters(
            atrPct,
            Math.Max(minPercent, Quantile(mae, stopQuantile)),
            Math.Max(minPercent, Quantile(pullback, stopQuantile)),
            Math.Max(minPercent, Quantile(mfe, 0.5)),
            mae.Count, horizon, stopQuantile, regime);
    }

    /// <summary>
    /// A small grid of variants around the measured parameters: tighter, measured and wider stops,
    /// each with and without the trail. The bandit decides among them on today's evidence.
    /// </summary>
    public static IReadOnlyList<ParameterVariant> VariantsAround(DerivedParameters p)
    {
        var list = new List<ParameterVariant>();
        foreach (var (label, k) in new[] { ("tight", 0.75), ("measured", 1.0), ("wide", 1.5) })
        {
            list.Add(new ParameterVariant($"{label}-stop", Round(p.StopLossPercent * k), null, Round(p.TakeProfitPercent)));
            list.Add(new ParameterVariant($"{label}-stop+trail", Round(p.StopLossPercent * k), Round(p.TrailingStopPercent * k), null));
        }
        return list;
    }

    public static double Quantile(IReadOnlyList<double> xs, double q)
    {
        if (xs.Count == 0) return 0;
        var sorted = xs.OrderBy(x => x).ToArray();
        double pos = q * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    private static double AtrPercent(IReadOnlyList<Candle> c, int period = 14)
    {
        if (c.Count < 2) return 0;
        var trs = new List<double>();
        for (int i = 1; i < c.Count; i++)
        {
            double h = (double)c[i].High, l = (double)c[i].Low, pc = (double)c[i - 1].Close;
            trs.Add(Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc))));
        }
        var recent = trs.Skip(Math.Max(0, trs.Count - period)).ToList();
        double last = (double)c[^1].Close;
        return last <= 0 ? 0 : recent.Average() / last * 100;
    }

    private static double Round(double v) => Math.Round(v, 2);
}
