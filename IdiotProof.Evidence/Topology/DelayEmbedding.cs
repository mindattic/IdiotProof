namespace IdiotProof.Evidence.Topology;

/// <summary>
/// Takens delay embedding: point i is (x[i], x[i+τ], …, x[i+(d-1)τ]). A periodic signal traces a
/// closed loop in the embedded space, a trend a line, noise a cloud, so the shape of the point cloud
/// carries the dynamics that a single indicator flattens away.
/// </summary>
public static class DelayEmbedding
{
    public static double[][] Embed(IReadOnlyList<double> series, int dimension, int delay)
    {
        if (dimension < 1) throw new ArgumentOutOfRangeException(nameof(dimension));
        if (delay < 1) throw new ArgumentOutOfRangeException(nameof(delay));
        int count = series.Count - (dimension - 1) * delay;
        if (count <= 0) return [];
        var points = new double[count][];
        for (int i = 0; i < count; i++)
        {
            var p = new double[dimension];
            for (int k = 0; k < dimension; k++) p[k] = series[i + k * delay];
            points[i] = p;
        }
        return points;
    }

    /// <summary>Log returns of the closes, z-normalized; element i is the return from close i to close i+1.</summary>
    public static double[] NormalizedLogReturns(IReadOnlyList<double> closes)
    {
        if (closes.Count < 2) return [];
        var r = new double[closes.Count - 1];
        for (int i = 1; i < closes.Count; i++)
            r[i - 1] = closes[i - 1] > 0 && closes[i] > 0 ? Math.Log(closes[i] / closes[i - 1]) : 0;
        return ShapeVector.ZNormalize(r);
    }
}
