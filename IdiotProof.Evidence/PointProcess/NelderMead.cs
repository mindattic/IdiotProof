namespace IdiotProof.Evidence.PointProcess;

/// <summary>Derivative-free simplex minimizer (Nelder-Mead with standard reflection/expansion/contraction/shrink).</summary>
public static class NelderMead
{
    public static (double[] Point, double Value, int Evaluations) Minimize(Func<double[], double> f, double[] start,
        double step = 0.5, int maxEvaluations = 2000, double tolerance = 1e-9)
    {
        int d = start.Length;
        var simplex = new double[d + 1][];
        var values = new double[d + 1];
        simplex[0] = (double[])start.Clone();
        values[0] = Safe(f, simplex[0]);
        int evals = 1;
        for (int i = 0; i < d; i++)
        {
            var p = (double[])start.Clone();
            p[i] += step;
            simplex[i + 1] = p;
            values[i + 1] = Safe(f, p);
            evals++;
        }

        while (evals < maxEvaluations)
        {
            var order = Enumerable.Range(0, d + 1).OrderBy(i => values[i]).ToArray();
            simplex = order.Select(i => simplex[i]).ToArray();
            values = order.Select(i => values[i]).ToArray();
            if (Math.Abs(values[d] - values[0]) <= tolerance * (Math.Abs(values[0]) + tolerance)) break;

            var centroid = new double[d];
            for (int i = 0; i < d; i++)
                for (int j = 0; j < d; j++) centroid[j] += simplex[i][j] / d;

            var reflected = Combine(centroid, simplex[d], -1);
            double fr = Safe(f, reflected); evals++;
            if (fr < values[0])
            {
                var expanded = Combine(centroid, simplex[d], -2);
                double fe = Safe(f, expanded); evals++;
                if (fe < fr) { simplex[d] = expanded; values[d] = fe; }
                else { simplex[d] = reflected; values[d] = fr; }
            }
            else if (fr < values[d - 1])
            {
                simplex[d] = reflected; values[d] = fr;
            }
            else
            {
                bool outside = fr < values[d];
                var contracted = Combine(centroid, outside ? reflected : simplex[d], 0.5);
                double fc = Safe(f, contracted); evals++;
                if (fc < (outside ? fr : values[d]))
                {
                    simplex[d] = contracted; values[d] = fc;
                }
                else
                {
                    for (int i = 1; i <= d; i++)
                    {
                        simplex[i] = Combine(simplex[0], simplex[i], 0.5);
                        values[i] = Safe(f, simplex[i]); evals++;
                    }
                }
            }
        }
        int best = Array.IndexOf(values, values.Min());
        return (simplex[best], values[best], evals);
    }

    /// <summary>c + t·(p − c): t = −1 reflects p through c, −2 expands, 0.5 contracts.</summary>
    private static double[] Combine(double[] c, double[] p, double t)
    {
        var r = new double[c.Length];
        for (int i = 0; i < c.Length; i++) r[i] = c[i] + t * (p[i] - c[i]);
        return r;
    }

    private static double Safe(Func<double[], double> f, double[] x)
    {
        double v = f(x);
        return double.IsFinite(v) ? v : double.MaxValue;
    }
}
