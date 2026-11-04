namespace IdiotProof.Evidence.Factors;

/// <summary>Eigenvalues (largest first) and their unit eigenvectors as matrix columns.</summary>
public sealed record EigenResult(double[] Values, double[,] Vectors)
{
    public int Size => Values.Length;

    public double[] Vector(int k)
    {
        var v = new double[Size];
        for (int i = 0; i < Size; i++) v[i] = Vectors[i, k];
        return v;
    }
}

/// <summary>Cyclic Jacobi eigendecomposition of a real symmetric matrix; exact to rounding, fine for a few hundred symbols.</summary>
public static class SymmetricEigen
{
    public static EigenResult Decompose(double[,] matrix, double tolerance = 1e-12, int maxSweeps = 100)
    {
        int n = matrix.GetLength(0);
        if (n != matrix.GetLength(1)) throw new ArgumentException("Matrix must be square.", nameof(matrix));
        var a = (double[,])matrix.Clone();
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1;

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0, scale = 0;
            for (int p = 0; p < n; p++)
            for (int q = 0; q < n; q++)
            {
                if (p == q) scale += a[p, p] * a[p, p];
                else off += a[p, q] * a[p, q];
            }
            if (off <= tolerance * tolerance * Math.Max(1, scale)) break;

            for (int p = 0; p < n - 1; p++)
            for (int q = p + 1; q < n; q++)
            {
                double apq = a[p, q];
                if (Math.Abs(apq) < 1e-300) continue;
                double theta = (a[q, q] - a[p, p]) / (2 * apq);
                double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                for (int k = 0; k < n; k++)
                {
                    double akp = a[k, p], akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }
                for (int k = 0; k < n; k++)
                {
                    double apk = a[p, k], aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }
                for (int k = 0; k < n; k++)
                {
                    double vkp = v[k, p], vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq;
                    v[k, q] = s * vkp + c * vkq;
                }
            }
        }

        var order = Enumerable.Range(0, n).OrderByDescending(i => a[i, i]).ToArray();
        var values = new double[n];
        var vectors = new double[n, n];
        for (int k = 0; k < n; k++)
        {
            values[k] = a[order[k], order[k]];
            for (int i = 0; i < n; i++) vectors[i, k] = v[i, order[k]];
        }
        return new EigenResult(values, vectors);
    }

    /// <summary>V · diag(values) · Vᵀ.</summary>
    public static double[,] Reconstruct(double[] values, double[,] vectors)
    {
        int n = values.Length;
        var m = new double[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            double sum = 0;
            for (int k = 0; k < n; k++) sum += vectors[i, k] * values[k] * vectors[j, k];
            m[i, j] = sum;
        }
        return m;
    }
}
