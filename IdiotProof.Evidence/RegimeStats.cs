namespace IdiotProof.Evidence;

public enum Regime { Unknown, Trending, MeanReverting, RandomWalk }

/// <summary>A symbol's current market character, measured from its own returns.</summary>
public sealed record RegimeReading(double RealizedVolatility, double Hurst, double VarianceRatio,
    double EfficiencyRatio, double Lag1Autocorrelation, int Samples, Regime Regime);

/// <summary>
/// Measures whether price is trending, mean-reverting or wandering, so a strategy can switch
/// between breakout and fade logic on evidence instead of guesswork.
/// </summary>
public static class RegimeStats
{
    /// <summary>Std of returns (per bar, not annualized).</summary>
    public static double RealizedVolatility(IReadOnlyList<double> returns) => EvidenceStats.StdDev(returns);

    /// <summary>
    /// Hurst exponent by rescaled range: ~0.5 random walk, &gt;0.5 persistent (trends continue),
    /// &lt;0.5 anti-persistent (moves reverse). Slope of log(R/S) against log(window) over
    /// power-of-two windows from <paramref name="minWindow"/> to n/2.
    /// </summary>
    public static double Hurst(IReadOnlyList<double> returns, int minWindow = 8)
    {
        int n = returns.Count;
        var xs = new List<double>();
        var ys = new List<double>();
        for (int w = minWindow; w <= n / 2; w *= 2)
        {
            double rsSum = 0;
            int chunks = 0;
            for (int start = 0; start + w <= n; start += w)
            {
                double mean = 0;
                for (int i = 0; i < w; i++) mean += returns[start + i];
                mean /= w;
                double cum = 0, max = double.MinValue, min = double.MaxValue, ss = 0;
                for (int i = 0; i < w; i++)
                {
                    double d = returns[start + i] - mean;
                    cum += d;
                    max = Math.Max(max, cum);
                    min = Math.Min(min, cum);
                    ss += d * d;
                }
                double s = Math.Sqrt(ss / w);
                if (s <= 0) continue;
                rsSum += (max - min) / s;
                chunks++;
            }
            if (chunks == 0) continue;
            xs.Add(Math.Log(w));
            ys.Add(Math.Log(rsSum / chunks));
        }
        return xs.Count < 2 ? double.NaN : Slope(xs, ys);
    }

    /// <summary>
    /// Lo-MacKinlay variance ratio VR(q) = Var(q-bar returns) / (q·Var(1-bar returns)) using
    /// overlapping sums: ~1 random walk, &gt;1 trending, &lt;1 mean-reverting.
    /// </summary>
    public static double VarianceRatio(IReadOnlyList<double> returns, int q)
    {
        int n = returns.Count;
        if (q < 2 || n < q + 1) return double.NaN;
        double mu = EvidenceStats.Mean(returns);
        double var1 = 0;
        foreach (var r in returns) var1 += (r - mu) * (r - mu);
        var1 /= n - 1;
        if (var1 <= 0) return double.NaN;

        double varQ = 0;
        int m = 0;
        for (int t = q - 1; t < n; t++)
        {
            double sum = 0;
            for (int j = 0; j < q; j++) sum += returns[t - j];
            double d = sum - q * mu;
            varQ += d * d;
            m++;
        }
        varQ /= m - 1;
        return varQ / (q * var1);
    }

    /// <summary>Kaufman efficiency ratio: net move / path length over the series of prices (1 = straight line, ~0 = chop).</summary>
    public static double EfficiencyRatio(IReadOnlyList<double> prices)
    {
        if (prices.Count < 2) return double.NaN;
        double path = 0;
        for (int i = 1; i < prices.Count; i++) path += Math.Abs(prices[i] - prices[i - 1]);
        return path <= 0 ? 0 : Math.Abs(prices[^1] - prices[0]) / path;
    }

    public static double Autocorrelation(IReadOnlyList<double> xs, int lag)
    {
        int n = xs.Count;
        if (lag < 1 || n <= lag + 1) return double.NaN;
        double m = EvidenceStats.Mean(xs), num = 0, den = 0;
        for (int i = 0; i < n; i++) den += (xs[i] - m) * (xs[i] - m);
        for (int i = lag; i < n; i++) num += (xs[i] - m) * (xs[i - lag] - m);
        return den <= 0 ? 0 : num / den;
    }

    /// <summary>
    /// Classifies by the variance ratio's significance under the random-walk null (homoskedastic
    /// Lo-MacKinlay z-statistic), so "trending" means the data rejects a random walk, not a cutoff someone picked.
    /// </summary>
    public static RegimeReading Read(IReadOnlyList<double> prices, int q = 4, double zCritical = 1.96)
    {
        var returns = new double[Math.Max(0, prices.Count - 1)];
        for (int i = 1; i < prices.Count; i++)
            returns[i - 1] = prices[i - 1] > 0 ? prices[i] / prices[i - 1] - 1 : 0;

        double vr = VarianceRatio(returns, q);
        int n = returns.Length;
        var regime = Regime.Unknown;
        if (!double.IsNaN(vr) && n > q * 4)
        {
            double se = Math.Sqrt(2.0 * (2 * q - 1) * (q - 1) / (3.0 * q * n));
            double z = (vr - 1) / se;
            regime = z > zCritical ? Regime.Trending : z < -zCritical ? Regime.MeanReverting : Regime.RandomWalk;
        }
        return new RegimeReading(RealizedVolatility(returns), Hurst(returns), vr, EfficiencyRatio(prices),
            Autocorrelation(returns, 1), n, regime);
    }

    private static double Slope(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        double mx = EvidenceStats.Mean(xs), my = EvidenceStats.Mean(ys), num = 0, den = 0;
        for (int i = 0; i < xs.Count; i++) { num += (xs[i] - mx) * (ys[i] - my); den += (xs[i] - mx) * (xs[i] - mx); }
        return den <= 0 ? double.NaN : num / den;
    }
}
