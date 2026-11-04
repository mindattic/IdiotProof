namespace IdiotProof.Evidence.Search;

/// <summary>Normal and Student-t distribution functions used by the multiple-testing corrections.</summary>
public static class Distributions
{
    /// <summary>Standard normal CDF Φ(x), via a complementary error function accurate to ~1e-7.</summary>
    public static double NormalCdf(double x) => 0.5 * Erfc(-x / Math.Sqrt(2));

    /// <summary>Φ⁻¹(p) by Acklam's rational approximation, refined with one Halley step (~1e-12).</summary>
    public static double NormalQuantile(double p)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double pLow = 0.02425;
        double x;
        if (p < pLow)
        {
            double q = Math.Sqrt(-2 * Math.Log(p));
            x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else if (p <= 1 - pLow)
        {
            double q = p - 0.5, r = q * q;
            x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        else
        {
            double q = Math.Sqrt(-2 * Math.Log(1 - p));
            x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        double e = NormalCdf(x) - p;
        double u = e * Math.Sqrt(2 * Math.PI) * Math.Exp(x * x / 2);
        return x - u / (1 + x * u / 2);
    }

    /// <summary>One-sided p-value P(T ≥ t) for Student's t with <paramref name="df"/> degrees of freedom.</summary>
    public static double StudentTUpperTail(double t, double df)
    {
        if (double.IsNaN(t) || df <= 0) return double.NaN;
        double x = df / (df + t * t);
        double tail = 0.5 * RegularizedIncompleteBeta(df / 2, 0.5, x);
        return t >= 0 ? tail : 1 - tail;
    }

    /// <summary>Regularized incomplete beta I_x(a, b) by Lentz's continued fraction.</summary>
    public static double RegularizedIncompleteBeta(double a, double b, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double lnFront = EvidenceStats.LogGamma(a + b) - EvidenceStats.LogGamma(a) - EvidenceStats.LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x);
        if (x > (a + 1) / (a + b + 2)) return 1 - RegularizedIncompleteBeta(b, a, 1 - x);
        return Math.Exp(lnFront) * BetaContinuedFraction(a, b, x) / a;
    }

    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const double tiny = 1e-300, eps = 1e-14;
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

    private static double Erfc(double x)
    {
        double z = Math.Abs(x);
        double t = 1 / (1 + 0.5 * z);
        double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806 +
            t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0 ? r : 2 - r;
    }
}
