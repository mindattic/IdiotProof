using IdiotProof.Models;

namespace IdiotProof.Evidence;

/// <summary>
/// Turns a window of candles into a scale-free shape: z-normalized closes, optionally followed by
/// z-normalized volume. A $0.80 stock and an $80 stock tracing the same path get the same vector,
/// and cosine similarity between two shapes equals the Pearson correlation of their paths.
/// </summary>
public static class ShapeVector
{
    public static double[] FromWindow(IReadOnlyList<Candle> candles, int end, int length, bool includeVolume = false)
    {
        if (length < 2) throw new ArgumentOutOfRangeException(nameof(length));
        int start = end - length + 1;
        if (start < 0 || end >= candles.Count) throw new ArgumentOutOfRangeException(nameof(end));

        var closes = new double[length];
        for (int i = 0; i < length; i++) closes[i] = (double)candles[start + i].Close;
        var shape = ZNormalize(closes);
        if (!includeVolume) return shape;

        var vols = new double[length];
        for (int i = 0; i < length; i++) vols[i] = Math.Log(1 + (double)candles[start + i].Volume);
        var vshape = ZNormalize(vols);
        var both = new double[length * 2];
        shape.CopyTo(both, 0);
        vshape.CopyTo(both, length);
        return both;
    }

    /// <summary>Subtract the mean, divide by the population std; a flat series becomes all zeros.</summary>
    public static double[] ZNormalize(IReadOnlyList<double> xs)
    {
        int n = xs.Count;
        var z = new double[n];
        if (n == 0) return z;
        double mean = 0;
        foreach (var x in xs) mean += x;
        mean /= n;
        double var = 0;
        foreach (var x in xs) var += (x - mean) * (x - mean);
        double sd = Math.Sqrt(var / n);
        if (sd < 1e-12) return z;
        for (int i = 0; i < n; i++) z[i] = (xs[i] - mean) / sd;
        return z;
    }

    public static double Cosine(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count) throw new ArgumentException("Dimension mismatch.");
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Count; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na <= 0 || nb <= 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}
