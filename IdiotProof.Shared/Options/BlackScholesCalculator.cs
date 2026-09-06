using IdiotProof.Models;

namespace IdiotProof.Shared.Options;

/// <summary>
/// Black-Scholes(-Merton) theoretical pricing and an implied-volatility solver.
/// <para>
/// <c>C = S·N(d1) − K·e^(−rT)·N(d2)</c>, <c>P = K·e^(−rT)·N(−d2) − S·N(−d1)</c>,
/// <c>d1 = [ln(S/K) + (r + σ²/2)T] / (σ√T)</c>, <c>d2 = d1 − σ√T</c>.
/// </para>
/// <para>
/// Used as an independent cross-check next to the broker's own IV/Greeks, and as the FALLBACK
/// when the broker omits them. Known simplifications, by design (v1):
/// <list type="bullet">
///   <item>European exercise assumed — US equity options are American. Early-exercise value is
///   ignored; immaterial for non-dividend names, material for deep-ITM calls near an ex-dividend date.</item>
///   <item>No dividend yield term (q = 0).</item>
///   <item>Constant volatility and rate over the life of the contract.</item>
/// </list>
/// Inputs are <c>double</c> — this is model math, not money; callers round for display.
/// </para>
/// </summary>
public static class BlackScholesCalculator
{
    private const double SqrtTwoPi = 2.5066282746310002;

    /// <summary>Theoretical price per share. T in years. Returns intrinsic when T ≤ 0 or σ ≤ 0.</summary>
    public static double TheoreticalPrice(double s, double k, double t, double r, double sigma, OptionRight right)
    {
        if (s <= 0 || k <= 0) return 0;
        if (t <= 0 || sigma <= 0)
            return right == OptionRight.Call ? Math.Max(0, s - k) : Math.Max(0, k - s);

        var (d1, d2) = D1D2(s, k, t, r, sigma);
        var discountedStrike = k * Math.Exp(-r * t);
        return right == OptionRight.Call
            ? s * NormalCdf(d1) - discountedStrike * NormalCdf(d2)
            : discountedStrike * NormalCdf(-d2) - s * NormalCdf(-d1);
    }

    /// <summary>∂Price/∂σ per share (same for calls and puts). Zero when T ≤ 0 or σ ≤ 0.</summary>
    public static double Vega(double s, double k, double t, double r, double sigma)
    {
        if (s <= 0 || k <= 0 || t <= 0 || sigma <= 0) return 0;
        var (d1, _) = D1D2(s, k, t, r, sigma);
        return s * NormalPdf(d1) * Math.Sqrt(t);
    }

    /// <summary>Δ: call in (0,1), put in (−1,0).</summary>
    public static double Delta(double s, double k, double t, double r, double sigma, OptionRight right)
    {
        if (s <= 0 || k <= 0 || t <= 0 || sigma <= 0)
        {
            var itm = right == OptionRight.Call ? s > k : s < k;
            return itm ? (right == OptionRight.Call ? 1 : -1) : 0;
        }
        var (d1, _) = D1D2(s, k, t, r, sigma);
        return right == OptionRight.Call ? NormalCdf(d1) : NormalCdf(d1) - 1;
    }

    /// <summary>
    /// Solve for σ such that <see cref="TheoreticalPrice"/> equals <paramref name="marketPrice"/>.
    /// Newton-Raphson seeded by the Brenner-Subrahmanyam approximation, falling back to bisection
    /// when vega collapses (near expiry / deep ITM-OTM). Returns null when no solution exists —
    /// price below intrinsic, non-positive T, or the market price is unattainable at any σ.
    /// </summary>
    public static double? ImpliedVolatility(double s, double k, double t, double r, double marketPrice, OptionRight right)
    {
        if (s <= 0 || k <= 0 || t <= 0 || marketPrice <= 0) return null;

        var intrinsicNow = right == OptionRight.Call ? Math.Max(0, s - k * Math.Exp(-r * t)) : Math.Max(0, k * Math.Exp(-r * t) - s);
        if (marketPrice < intrinsicNow - 1e-9) return null;

        // Upper bound: the price can never exceed S (call) or discounted K (put).
        var maxPrice = right == OptionRight.Call ? s : k * Math.Exp(-r * t);
        if (marketPrice >= maxPrice) return null;

        const double lo = 1e-4, hi = 5.0; // 0.01% .. 500% annualised
        const double tolerance = 1e-8;

        // Brenner–Subrahmanyam seed: σ ≈ √(2π/T) · (C/S)
        var sigma = Math.Clamp(Math.Sqrt(2 * Math.PI / t) * marketPrice / s, 0.05, 2.0);

        for (var i = 0; i < 50; i++)
        {
            var price = TheoreticalPrice(s, k, t, r, sigma, right);
            var diff = price - marketPrice;
            if (Math.Abs(diff) < tolerance) return sigma;

            var vega = Vega(s, k, t, r, sigma);
            if (vega < 1e-10) break; // flat — hand off to bisection

            var next = sigma - diff / vega;
            if (next <= lo || next >= hi || double.IsNaN(next)) break;
            sigma = next;
        }

        return Bisect(s, k, t, r, marketPrice, right, lo, hi, tolerance);
    }

    private static double? Bisect(double s, double k, double t, double r, double target, OptionRight right, double lo, double hi, double tol)
    {
        var fLo = TheoreticalPrice(s, k, t, r, lo, right) - target;
        var fHi = TheoreticalPrice(s, k, t, r, hi, right) - target;
        if (fLo > 0 || fHi < 0) return null; // target not bracketed

        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            var fMid = TheoreticalPrice(s, k, t, r, mid, right) - target;
            if (Math.Abs(fMid) < tol || (hi - lo) / 2 < 1e-10) return mid;
            if (fMid > 0) hi = mid; else lo = mid;
        }
        return (lo + hi) / 2;
    }

    private static (double d1, double d2) D1D2(double s, double k, double t, double r, double sigma)
    {
        var sqrtT = Math.Sqrt(t);
        var d1 = (Math.Log(s / k) + (r + sigma * sigma / 2) * t) / (sigma * sqrtT);
        return (d1, d1 - sigma * sqrtT);
    }

    /// <summary>Standard normal CDF via the complementary error function (Abramowitz–Stegun 7.1.26, |ε| &lt; 1.5e-7).</summary>
    public static double NormalCdf(double x)
    {
        // Φ(x) = ½·erfc(−x/√2); erfc via A&S rational approximation.
        var z = Math.Abs(x) / Math.Sqrt(2);
        var tt = 1.0 / (1.0 + 0.3275911 * z);
        var poly = tt * (0.254829592 + tt * (-0.284496736 + tt * (1.421413741 + tt * (-1.453152027 + tt * 1.061405429))));
        var erf = 1.0 - poly * Math.Exp(-z * z);
        return x >= 0 ? 0.5 * (1.0 + erf) : 0.5 * (1.0 - erf);
    }

    public static double NormalPdf(double x) => Math.Exp(-0.5 * x * x) / SqrtTwoPi;

    /// <summary>Calendar-day fraction of a year, floored at a small positive value so 0DTE still prices.</summary>
    public static double YearsUntil(DateOnly expiration, DateTime nowUtc)
    {
        // Options expire at 4:00 PM ET on expiration day; approximate with end-of-day UTC-4/5.
        var expiryUtc = expiration.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc);
        var years = (expiryUtc - nowUtc).TotalDays / 365.0;
        return Math.Max(years, 1.0 / (365.0 * 24 * 4)); // ≥ 15 minutes
    }
}
