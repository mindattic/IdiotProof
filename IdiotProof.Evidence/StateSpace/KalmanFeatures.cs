namespace IdiotProof.Evidence.StateSpace;

/// <summary>Per-bar Kalman features; NaN where a bar has no defined value (first bars of a session, missing leader).</summary>
public sealed record KalmanFeatureSet(double[] Level, double[] Slope, double[] InnovationZ, double[] PairResidualZ, double[] HedgeRatio);

/// <summary>
/// Runs the filters bar by bar over one session of log closes. Value i reads only bars 0..i, so a
/// feature can be evaluated against bar i's forward return without seeing anything after it.
/// </summary>
public static class KalmanFeatures
{
    public const int Warmup = 5;

    /// <param name="logCloses">Log closes of the session.</param>
    /// <param name="leaderLogCloses">The leader's log closes aligned bar for bar (NaN where missing), or null.</param>
    public static KalmanFeatureSet Compute(IReadOnlyList<double> logCloses, LocalTrendParameters parameters,
        IReadOnlyList<double>? leaderLogCloses = null, double hedgeDelta = 1e-4, double hedgeR = 1e-6)
    {
        int n = logCloses.Count;
        var level = Nans(n);
        var slope = Nans(n);
        var z = Nans(n);
        var pair = Nans(n);
        var hedge = Nans(n);

        var trend = parameters.CreateFilter();
        for (int i = 0; i < n; i++)
        {
            var st = trend.Step(logCloses[i]);
            if (i < Warmup) continue;
            level[i] = st.Level;
            slope[i] = st.Slope;
            z[i] = st.Z;
        }

        if (leaderLogCloses is not null)
        {
            if (leaderLogCloses.Count != n) throw new ArgumentException("Leader series must align with the follower.", nameof(leaderLogCloses));
            var hf = new HedgeRatioFilter(hedgeDelta, hedgeR);
            int steps = 0;
            double x0 = double.NaN, y0 = double.NaN;
            for (int i = 0; i < n; i++)
            {
                double x = leaderLogCloses[i];
                if (double.IsNaN(x)) continue;
                // Measure both legs from their first shared bar so alpha and beta aren't collinear on raw log levels.
                if (double.IsNaN(x0)) { x0 = x; y0 = logCloses[i]; }
                var st = hf.Step(x - x0, logCloses[i] - y0);
                if (++steps <= Warmup * 4) continue;
                pair[i] = st.Z;
                hedge[i] = st.Beta;
            }
        }
        return new KalmanFeatureSet(level, slope, z, pair, hedge);
    }

    private static double[] Nans(int n)
    {
        var a = new double[n];
        Array.Fill(a, double.NaN);
        return a;
    }
}
