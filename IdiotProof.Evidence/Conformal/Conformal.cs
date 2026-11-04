namespace IdiotProof.Evidence.Conformal;

/// <summary>A prediction interval for a forward return; infinite bounds mean "not enough calibration yet".</summary>
public readonly record struct ConformalInterval(double Low, double High)
{
    public bool IsFinite => !double.IsInfinity(Low) && !double.IsInfinity(High);
    public double Width => High - Low;
    public bool Contains(double y) => y >= Low && y <= High;

    /// <summary>Up when the whole interval is above zero, Down when below, otherwise abstain.</summary>
    public Direction Call() => IsFinite && Low > 0 ? Direction.Up : IsFinite && High < 0 ? Direction.Down : Direction.Flat;

    public static ConformalInterval Unbounded => new(double.NegativeInfinity, double.PositiveInfinity);
}

public static class ConformalMath
{
    /// <summary>
    /// The split-conformal score threshold: the ⌈(n+1)(1-α)⌉-th smallest of n calibration scores.
    /// With exchangeable data, forecast ± this threshold covers the next outcome with probability at
    /// least 1-α. Too few scores for that rank gives +∞ (an honest "can't promise coverage yet").
    /// </summary>
    public static double Quantile(IReadOnlyList<double> scores, double alpha)
    {
        int n = scores.Count;
        if (alpha <= 0 || n == 0) return double.PositiveInfinity;
        if (alpha >= 1) return 0;
        int k = (int)Math.Ceiling((n + 1) * (1 - alpha));
        if (k > n) return double.PositiveInfinity;
        if (k < 1) return 0;
        var sorted = scores.ToArray();
        Array.Sort(sorted);
        return sorted[k - 1];
    }
}

/// <summary>Split conformal on a growing calibration set of absolute residuals.</summary>
public sealed class SplitConformal(double alpha)
{
    private readonly List<double> scores = new();
    public double Alpha { get; } = alpha;
    public int Count => scores.Count;

    public void Add(double forecast, double outcome) => scores.Add(Math.Abs(outcome - forecast));

    public ConformalInterval Interval(double forecast)
    {
        double q = ConformalMath.Quantile(scores, Alpha);
        return double.IsPositiveInfinity(q) ? ConformalInterval.Unbounded : new ConformalInterval(forecast - q, forecast + q);
    }
}

/// <summary>
/// Adaptive Conformal Inference (Gibbs &amp; Candès 2021): after each outcome the working level moves
/// α_t ← α_t + γ(α − err_t), so a run of misses widens the next intervals and a run of easy hits
/// narrows them. Whatever the drift, the long-run miss rate over any stretch of T outcomes stays
/// within (1 + 2γ)/(γT) of α.
/// </summary>
public sealed class AdaptiveConformal(double alpha, double gamma)
{
    private readonly List<double> scores = new();
    public double Alpha { get; } = alpha;
    public double Gamma { get; } = gamma;
    public double AlphaT { get; private set; } = alpha;
    public int Count => scores.Count;

    public ConformalInterval Interval(double forecast)
    {
        if (scores.Count == 0) return ConformalInterval.Unbounded;
        if (AlphaT >= 1) return new ConformalInterval(forecast, forecast);
        double q = ConformalMath.Quantile(scores, AlphaT);
        return double.IsPositiveInfinity(q) ? ConformalInterval.Unbounded : new ConformalInterval(forecast - q, forecast + q);
    }

    /// <summary>Learns from an outcome, given the interval that was issued for it before it was known.</summary>
    public void Update(ConformalInterval issued, double forecast, double outcome)
    {
        double err = issued.Contains(outcome) ? 0 : 1;
        AlphaT += Gamma * (Alpha - err);
        scores.Add(Math.Abs(outcome - forecast));
    }
}
