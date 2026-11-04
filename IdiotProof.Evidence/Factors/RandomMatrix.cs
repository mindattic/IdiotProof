namespace IdiotProof.Evidence.Factors;

/// <summary>A correlation matrix with its noise removed, and how many eigen-directions were judged real.</summary>
public sealed record CleanedCorrelation(double[,] Matrix, int SignalCount, double UpperBound, double[] Eigenvalues);

/// <summary>
/// Random-matrix theory for correlation matrices. With N series and T observations of pure noise,
/// the eigenvalues of the sample correlation matrix fall inside the Marchenko-Pastur band
/// [(1-√(N/T))², (1+√(N/T))²]. Eigenvalues above the band are structure (the market, sectors);
/// those inside it are indistinguishable from chance, so cleaning replaces them with their mean.
/// </summary>
public static class RandomMatrix
{
    public static double MarchenkoPasturUpper(int series, int observations)
    {
        if (series < 1 || observations < 1) throw new ArgumentOutOfRangeException(nameof(observations));
        double q = (double)series / observations;
        return (1 + Math.Sqrt(q)) * (1 + Math.Sqrt(q));
    }

    public static double MarchenkoPasturLower(int series, int observations)
    {
        double q = (double)series / observations;
        double r = 1 - Math.Sqrt(q);
        return r * r;
    }

    /// <summary>Elementwise noise floor of a sample correlation (lagged or not): ~z standard errors of a zero correlation.</summary>
    public static double CorrelationNoiseFloor(int observations, double z = 2.5) => observations <= 0 ? 1 : z / Math.Sqrt(observations);

    /// <summary>Pearson correlation matrix of equal-length series (truncated to the shortest).</summary>
    public static double[,] Correlation(IReadOnlyList<double[]> series)
    {
        int n = series.Count;
        var c = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            c[i, i] = 1;
            for (int j = i + 1; j < n; j++) c[i, j] = c[j, i] = EvidenceStats.Correlation(series[i], series[j]);
        }
        return c;
    }

    /// <summary>
    /// Eigenvalue clipping: eigenvalues at or below the Marchenko-Pastur upper edge are replaced by
    /// their mean (so the trace is preserved), the matrix is rebuilt and rescaled to a unit diagonal.
    /// This is a shrinkage estimator: market- and sector-wide structure survives intact, while a
    /// tight cluster of only two or three names keeps its link but at a shrunk strength.
    /// </summary>
    public static CleanedCorrelation Clean(double[,] correlation, int observations)
    {
        int n = correlation.GetLength(0);
        var eig = SymmetricEigen.Decompose(correlation);
        double upper = MarchenkoPasturUpper(n, observations);
        var values = (double[])eig.Values.Clone();
        int signal = values.Count(v => v > upper);
        if (signal < n)
        {
            double noiseMean = values.Skip(signal).Average();
            for (int k = signal; k < n; k++) values[k] = noiseMean;
        }
        var rebuilt = SymmetricEigen.Reconstruct(values, eig.Vectors);
        var cleaned = new double[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            double d = Math.Sqrt(rebuilt[i, i] * rebuilt[j, j]);
            cleaned[i, j] = i == j ? 1 : d > 0 ? rebuilt[i, j] / d : 0;
        }
        return new CleanedCorrelation(cleaned, signal, upper, eig.Values);
    }
}
