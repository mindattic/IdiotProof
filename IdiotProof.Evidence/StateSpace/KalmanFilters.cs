namespace IdiotProof.Evidence.StateSpace;

/// <summary>One filtered step: the state after seeing y, the one-step-ahead innovation and its variance.</summary>
public readonly record struct KalmanStep(double Level, double Slope, double Innovation, double InnovationVariance)
{
    /// <summary>Innovation in standard deviations; ~N(0,1) when the model is right.</summary>
    public double Z => InnovationVariance > 0 ? Innovation / Math.Sqrt(InnovationVariance) : 0;
}

/// <summary>
/// Local level model: x_t = x_{t-1} + w (var Q), y_t = x_t + v (var R). The filter only ever uses
/// observations up to t, so every output is causal.
/// </summary>
public sealed class LocalLevelFilter(double q, double r)
{
    private double level;
    private double p;
    private bool started;

    public double Q { get; } = q;
    public double R { get; } = r;

    public KalmanStep Step(double y)
    {
        if (!started)
        {
            level = y;
            p = R;
            started = true;
            return new KalmanStep(level, 0, 0, 0);
        }
        p += Q;
        double e = y - level;
        double s = p + R;
        double k = p / s;
        level += k * e;
        p *= 1 - k;
        return new KalmanStep(level, 0, e, s);
    }
}

/// <summary>
/// Local linear trend: level_t = level_{t-1} + slope_{t-1} + w1, slope_t = slope_{t-1} + w2, y_t = level_t + v.
/// Q = diag(qLevel, qSlope), observation noise R.
/// </summary>
public sealed class LocalTrendFilter(double qLevel, double qSlope, double r)
{
    private double level, slope;
    private double p00, p01, p11;
    private int seen;

    public double QLevel { get; } = qLevel;
    public double QSlope { get; } = qSlope;
    public double R { get; } = r;

    public KalmanStep Step(double y)
    {
        if (seen == 0)
        {
            level = y;
            slope = 0;
            p00 = R;
            p01 = 0;
            p11 = Math.Max(R, 1e-12);
            seen++;
            return new KalmanStep(level, slope, 0, 0);
        }
        seen++;

        // Predict: x = F x, P = F P F' + Q with F = [[1,1],[0,1]].
        level += slope;
        double n00 = p00 + 2 * p01 + p11 + QLevel;
        double n01 = p01 + p11;
        double n11 = p11 + QSlope;

        double e = y - level;
        double s = n00 + R;
        double k0 = n00 / s, k1 = n01 / s;
        level += k0 * e;
        slope += k1 * e;
        p00 = (1 - k0) * n00;
        p01 = (1 - k0) * n01;
        p11 = n11 - k1 * n01;
        return new KalmanStep(level, slope, e, s);
    }
}

/// <summary>A filtered hedge-ratio step for y = alpha + beta·x.</summary>
public readonly record struct HedgeStep(double Alpha, double Beta, double Innovation, double InnovationVariance)
{
    public double Z => InnovationVariance > 0 ? Innovation / Math.Sqrt(InnovationVariance) : 0;
}

/// <summary>
/// Regression Kalman filter with a random-walk [alpha, beta] state: tracks a hedge ratio that drifts
/// through the day. <paramref name="delta"/> sets how fast beta may move (state noise = delta/(1-delta)·I),
/// <paramref name="r"/> is the observation noise.
/// </summary>
public sealed class HedgeRatioFilter(double delta, double r)
{
    private double alpha, beta;
    private double p00 = 1, p01, p11 = 1;
    private readonly double w = delta / (1 - delta);

    public double R { get; } = r;

    public HedgeStep Step(double x, double y)
    {
        double n00 = p00 + w, n01 = p01, n11 = p11 + w;
        double yhat = alpha + beta * x;
        double e = y - yhat;
        // H = [1, x]: S = H P H' + R, K = P H' / S.
        double ph0 = n00 + n01 * x;
        double ph1 = n01 + n11 * x;
        double s = ph0 + ph1 * x + R;
        double k0 = ph0 / s, k1 = ph1 / s;
        alpha += k0 * e;
        beta += k1 * e;
        p00 = n00 - k0 * ph0;
        p01 = n01 - k0 * ph1;
        p11 = n11 - k1 * ph1;
        return new HedgeStep(alpha, beta, e, s);
    }
}

/// <summary>Noise variances for a local linear trend model, fitted by maximum likelihood.</summary>
public sealed record LocalTrendParameters(double QLevel, double QSlope, double R, double LogLikelihood)
{
    public LocalTrendFilter CreateFilter() => new(QLevel, QSlope, R);
}

/// <summary>
/// Fits the noise variances on earlier data by maximum likelihood over a grid of signal-to-noise
/// ratios, with the observation variance concentrated out exactly (every variance scales with R).
/// </summary>
public static class KalmanFit
{
    private static readonly double[] LevelRatios = Grid(-4, 2);
    private static readonly double[] SlopeRatios = [0, .. Grid(-9, -2)];

    public static LocalTrendParameters FitLocalTrend(IReadOnlyList<IReadOnlyList<double>> segments)
    {
        LocalTrendParameters? best = null;
        foreach (var ql in LevelRatios)
        foreach (var qs in SlopeRatios)
        {
            double sumNorm = 0, sumLogS = 0;
            int n = 0;
            foreach (var seg in segments)
            {
                var f = new LocalTrendFilter(ql, qs, 1);
                for (int t = 0; t < seg.Count; t++)
                {
                    var st = f.Step(seg[t]);
                    if (t < 2 || st.InnovationVariance <= 0) continue;
                    sumNorm += st.Innovation * st.Innovation / st.InnovationVariance;
                    sumLogS += Math.Log(st.InnovationVariance);
                    n++;
                }
            }
            if (n < 10) continue;
            double r = sumNorm / n;
            if (r <= 0) continue;
            double ll = -0.5 * (n * Math.Log(2 * Math.PI * r) + sumLogS + n);
            if (best is null || ll > best.LogLikelihood) best = new LocalTrendParameters(ql * r, qs * r, r, ll);
        }
        return best ?? new LocalTrendParameters(1e-8, 0, 1e-6, double.NaN);
    }

    public static LocalTrendParameters FitLocalTrend(IReadOnlyList<double> series) => FitLocalTrend([series]);

    private static double[] Grid(int fromExp, int toExp)
    {
        var g = new List<double>();
        for (int e = fromExp; e <= toExp; e++) { g.Add(Math.Pow(10, e)); g.Add(3 * Math.Pow(10, e)); }
        return g.ToArray();
    }
}
