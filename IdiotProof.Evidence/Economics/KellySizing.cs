namespace IdiotProof.Evidence.Economics;

/// <summary>Advisory sizing for a repeated bet; never an order quantity.</summary>
public sealed record KellyAdvice(
    int Wins, int Losses, double PayoffRatio,
    double EmpiricalWinRate, double PosteriorWinRate, (double Low, double High) WinRateInterval,
    double PlugInFraction, double PosteriorFraction, double ConservativeFraction, double RecommendedFraction)
{
    public const string Disclaimer =
        "Advisory only: Kelly fractions never change order quantities. The Risk Guardian and the 1-share default policy stay authoritative.";
}

/// <summary>
/// Kelly sizing for a win/lose bet paying b:1. Plug-in Kelly uses the raw win rate; the posterior
/// version maximizes expected log growth averaged over a Beta posterior on the win rate. Because
/// the growth objective is linear in p, that optimum is exactly Kelly at the posterior mean, which
/// is pulled toward the prior and so shrinks the bet when the sample is small.
/// </summary>
public static class KellySizing
{
    /// <summary>f* = p - (1-p)/b, floored at 0 (no edge, no bet).</summary>
    public static double PlugIn(double winRate, double payoffRatio)
    {
        if (payoffRatio <= 0 || double.IsNaN(winRate) || double.IsNaN(payoffRatio)) return 0;
        return Math.Max(0, winRate - (1 - winRate) / payoffRatio);
    }

    /// <summary>Expected log growth per bet at fraction f: p·ln(1+b·f) + (1-p)·ln(1-f).</summary>
    public static double Growth(double winRate, double payoffRatio, double fraction)
    {
        if (fraction >= 1) return double.NegativeInfinity;
        return winRate * Math.Log(1 + payoffRatio * fraction) + (1 - winRate) * Math.Log(1 - fraction);
    }

    /// <summary>Mean win over mean absolute loss from realized per-trade returns (NaN when either side is empty).</summary>
    public static double PayoffRatio(IEnumerable<double> returns)
    {
        double winSum = 0, lossSum = 0;
        int wins = 0, losses = 0;
        foreach (var r in returns)
        {
            if (r > 0) { winSum += r; wins++; }
            else if (r < 0) { lossSum -= r; losses++; }
        }
        return wins == 0 || losses == 0 ? double.NaN : (winSum / wins) / (lossSum / losses);
    }

    /// <param name="fraction">Fraction of the posterior Kelly to recommend (0.5 = half Kelly).</param>
    /// <param name="credibleTail">Lower posterior quantile used for the conservative fraction.</param>
    public static KellyAdvice Advise(int wins, int losses, double payoffRatio,
        double priorAlpha = 1, double priorBeta = 1, double fraction = 0.5, double credibleTail = 0.05)
    {
        if (wins < 0 || losses < 0) throw new ArgumentOutOfRangeException(nameof(wins));
        int n = wins + losses;
        double a = priorAlpha + wins, b = priorBeta + losses;
        double empirical = n == 0 ? double.NaN : (double)wins / n;
        double posteriorMean = a / (a + b);
        double low = BetaDistribution.Quantile(credibleTail, a, b);
        double high = BetaDistribution.Quantile(1 - credibleTail, a, b);

        double plugIn = n == 0 ? 0 : PlugIn(empirical, payoffRatio);
        double posterior = PlugIn(posteriorMean, payoffRatio);
        double conservative = PlugIn(low, payoffRatio);
        return new KellyAdvice(wins, losses, payoffRatio, empirical, posteriorMean, (low, high),
            plugIn, posterior, conservative, Math.Clamp(fraction, 0, 1) * posterior);
    }
}

/// <summary>Beta distribution CDF (regularized incomplete beta) and quantile.</summary>
public static class BetaDistribution
{
    public static double Cdf(double x, double a, double b)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double lnFront = EvidenceStats.LogGamma(a + b) - EvidenceStats.LogGamma(a) - EvidenceStats.LogGamma(b)
            + a * Math.Log(x) + b * Math.Log(1 - x);
        double front = Math.Exp(lnFront);
        return x < (a + 1) / (a + b + 2)
            ? front * ContinuedFraction(x, a, b) / a
            : 1 - front * ContinuedFraction(1 - x, b, a) / b;
    }

    public static double Quantile(double p, double a, double b)
    {
        if (p <= 0) return 0;
        if (p >= 1) return 1;
        double lo = 0, hi = 1;
        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2;
            if (Cdf(mid, a, b) < p) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    // Lentz's method for the incomplete-beta continued fraction.
    private static double ContinuedFraction(double x, double a, double b)
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
            double delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < eps) break;
        }
        return h;
    }
}
