namespace IdiotProof.Evidence;

/// <summary>How one trading day's calls scored, pooled across every symbol called that day.</summary>
public readonly record struct DayTally(DateOnly Day, int Calls, int Hits);

public sealed record ClusteredResult(
    int Days,
    int Calls,
    int Hits,
    double PooledHitRate,
    double DayMeanHitRate,
    double TStatistic,
    double TPValue,
    double BootstrapPValue,
    double BootstrapLow,
    double BootstrapHigh,
    double NullRate)
{
    /// <summary>Both day-level tests reject the null at <paramref name="alpha"/>.</summary>
    public bool IsSignificant(double alpha = 0.05) => BootstrapPValue < alpha && TPValue < alpha;
}

/// <summary>
/// Significance that treats the trading day, not the call, as the unit of evidence. Calls made on
/// the same day across many symbols ride the same market move, so a naive binomial test counts one
/// lucky day as hundreds of independent wins. Here the day is resampled whole (block bootstrap) and
/// the day-level hit rates are t-tested, so one day — however many calls it holds — is one sample.
/// </summary>
public static class ClusteredSignificance
{
    /// <param name="nullRate">The hit rate to beat (0.5 = a coin; 0.51 = the 51% bar).</param>
    /// <param name="minDays">Fewer called days than this proves nothing: both p-values are reported as 1.</param>
    public static ClusteredResult Evaluate(IReadOnlyList<DayTally> tallies, double nullRate = 0.5,
        int resamples = 10000, int seed = 1337, int minDays = 5)
    {
        var days = tallies.Where(t => t.Calls > 0).ToArray();
        int calls = days.Sum(d => d.Calls), hits = days.Sum(d => d.Hits);
        double pooled = calls == 0 ? double.NaN : (double)hits / calls;
        var rates = days.Select(d => (double)d.Hits / d.Calls).ToArray();
        double dayMean = rates.Length == 0 ? double.NaN : rates.Average();

        if (days.Length < minDays)
            return new ClusteredResult(days.Length, calls, hits, pooled, dayMean, double.NaN, 1, 1, 0, 1, nullRate);

        // Day-level one-sided t-test of mean daily hit rate > nullRate.
        double sd = EvidenceStats.StdDev(rates);
        double t, tp;
        if (sd <= 0)
        {
            t = dayMean > nullRate ? double.PositiveInfinity : dayMean < nullRate ? double.NegativeInfinity : 0;
            tp = dayMean > nullRate ? 0 : 1;
        }
        else
        {
            t = (dayMean - nullRate) / (sd / Math.Sqrt(rates.Length));
            tp = StudentTUpperTail(t, rates.Length - 1);
        }

        // Block bootstrap over whole days, null-centered: how often would resampling move the pooled
        // rate at least as far above the estimate as the estimate sits above the null.
        var rng = new Random(seed);
        var boot = new double[resamples];
        int extreme = 0;
        double gap = pooled - nullRate;
        for (int b = 0; b < resamples; b++)
        {
            int h = 0, c = 0;
            for (int i = 0; i < days.Length; i++)
            {
                var d = days[rng.Next(days.Length)];
                h += d.Hits;
                c += d.Calls;
            }
            double r = (double)h / c;
            boot[b] = r;
            if (r - pooled >= gap) extreme++;
        }
        Array.Sort(boot);
        double bp = (1.0 + extreme) / (resamples + 1.0);
        double lo = boot[(int)Math.Floor(0.025 * (resamples - 1))];
        double hi = boot[(int)Math.Ceiling(0.975 * (resamples - 1))];
        return new ClusteredResult(days.Length, calls, hits, pooled, dayMean, t, tp, bp, lo, hi, nullRate);
    }

    /// <summary>P(T &gt;= t) for Student's t with <paramref name="df"/> degrees of freedom.</summary>
    public static double StudentTUpperTail(double t, double df)
    {
        if (double.IsPositiveInfinity(t)) return 0;
        if (double.IsNegativeInfinity(t)) return 1;
        double x = df / (df + t * t);
        double half = 0.5 * RegularizedIncompleteBeta(x, df / 2, 0.5);
        return t >= 0 ? half : 1 - half;
    }

    /// <summary>I_x(a, b) by Lentz's continued fraction (Numerical Recipes betacf).</summary>
    public static double RegularizedIncompleteBeta(double x, double a, double b)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double lnFront = EvidenceStats.LogGamma(a + b) - EvidenceStats.LogGamma(a) - EvidenceStats.LogGamma(b)
                         + a * Math.Log(x) + b * Math.Log(1 - x);
        if (x < (a + 1) / (a + b + 2))
            return Math.Exp(lnFront) * BetaContinuedFraction(x, a, b) / a;
        return 1 - Math.Exp(lnFront) * BetaContinuedFraction(1 - x, b, a) / b;
    }

    private static double BetaContinuedFraction(double x, double a, double b)
    {
        const double tiny = 1e-300, eps = 1e-15;
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < tiny) d = tiny;
        d = 1 / d;
        double h = d;
        for (int m = 1; m <= 300; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny;
            c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d; h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny;
            c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < eps) break;
        }
        return h;
    }
}
