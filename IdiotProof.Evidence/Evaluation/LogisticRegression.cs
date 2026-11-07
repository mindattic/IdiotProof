namespace IdiotProof.Evidence.Evaluation;

/// <summary>
/// A fitted L2-regularized logistic model on standardized features. A NaN feature value scores as the
/// training mean (zero after standardization), so a missing reading neither helps nor hurts.
/// </summary>
public sealed class LogisticModel
{
    internal LogisticModel(IReadOnlyList<string> features, double[] means, double[] stds, double[] weights, double bias, int epochs, double finalLoss)
    {
        Features = features;
        Means = means;
        Stds = stds;
        Weights = weights;
        Bias = bias;
        Epochs = epochs;
        FinalLoss = finalLoss;
    }

    public IReadOnlyList<string> Features { get; }
    public IReadOnlyList<double> Means { get; }
    public IReadOnlyList<double> Stds { get; }
    public IReadOnlyList<double> Weights { get; }
    public double Bias { get; }
    public int Epochs { get; }
    public double FinalLoss { get; }

    public double Predict(IReadOnlyList<double> x)
    {
        double z = Bias;
        for (int j = 0; j < Weights.Count; j++)
        {
            double v = j < x.Count ? x[j] : double.NaN;
            double s = double.IsNaN(v) ? 0 : (v - Means[j]) / Stds[j];
            z += Weights[j] * s;
        }
        return 1.0 / (1.0 + Math.Exp(-z));
    }
}

/// <summary>
/// Full-batch gradient descent for logistic regression with an L2 penalty on the weights (not the
/// bias). Features are standardized on the training data; the model remembers the means and
/// standard deviations so a scored row is transformed the same way. Deterministic.
/// </summary>
public static class LogisticRegression
{
    public static LogisticModel Fit(IReadOnlyList<string> featureNames, IReadOnlyList<double[]> x, IReadOnlyList<bool> y,
        double l2 = 1.0, int epochs = 500, double learningRate = 0.1)
    {
        int n = x.Count, d = featureNames.Count;
        if (n != y.Count) throw new ArgumentException("x and y differ in length.");
        var means = new double[d];
        var stds = new double[d];
        for (int j = 0; j < d; j++)
        {
            double sum = 0, sumSq = 0;
            int count = 0;
            foreach (var row in x)
            {
                double v = j < row.Length ? row[j] : double.NaN;
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                sum += v; sumSq += v * v; count++;
            }
            means[j] = count == 0 ? 0 : sum / count;
            double var = count < 2 ? 0 : Math.Max(0, sumSq / count - means[j] * means[j]);
            stds[j] = var > 1e-18 ? Math.Sqrt(var) : 1;
        }

        var z = new double[n][];
        for (int i = 0; i < n; i++)
        {
            z[i] = new double[d];
            for (int j = 0; j < d; j++)
            {
                double v = j < x[i].Length ? x[i][j] : double.NaN;
                z[i][j] = double.IsNaN(v) || double.IsInfinity(v) ? 0 : (v - means[j]) / stds[j];
            }
        }

        var w = new double[d];
        double b = 0, loss = double.NaN;
        var grad = new double[d];
        int ran = 0;
        for (int epoch = 0; epoch < epochs; epoch++)
        {
            Array.Clear(grad);
            double gb = 0;
            loss = 0;
            for (int i = 0; i < n; i++)
            {
                double s = b;
                for (int j = 0; j < d; j++) s += w[j] * z[i][j];
                double p = 1.0 / (1.0 + Math.Exp(-s));
                double target = y[i] ? 1 : 0;
                double err = p - target;
                for (int j = 0; j < d; j++) grad[j] += err * z[i][j];
                gb += err;
                double pc = Math.Clamp(p, 1e-12, 1 - 1e-12);
                loss -= y[i] ? Math.Log(pc) : Math.Log(1 - pc);
            }
            double step = learningRate;
            double norm = 0;
            for (int j = 0; j < d; j++)
            {
                double g = grad[j] / Math.Max(1, n) + l2 * w[j] / Math.Max(1, n);
                w[j] -= step * g;
                norm += g * g;
            }
            b -= step * gb / Math.Max(1, n);
            ran = epoch + 1;
            if (Math.Sqrt(norm) < 1e-7) break;
        }
        return new LogisticModel(featureNames, means, stds, w, b, ran, n == 0 ? double.NaN : loss / n);
    }
}

/// <summary>AUC of two scores compared on the same rows, with a day-block bootstrap interval for their difference.</summary>
public sealed record AucComparison(double AucA, double AucB, double Difference, double Low, double High, double PValueTwoSided, int Days, int Rows, int Positives, int Draws);

/// <summary>Area under the ROC curve by the Mann-Whitney statistic (ties count half), and a DeLong-style bootstrap over day blocks for comparing two scorers.</summary>
public static class Auc
{
    public static double Compute(IReadOnlyList<double> scores, IReadOnlyList<bool> labels)
    {
        if (scores.Count != labels.Count) throw new ArgumentException("scores and labels differ in length.");
        int n = scores.Count;
        if (n == 0) return double.NaN;
        var order = Enumerable.Range(0, n).OrderBy(i => scores[i]).ToArray();
        var rank = new double[n];
        int pos = 0;
        while (pos < n)
        {
            int end = pos;
            while (end + 1 < n && scores[order[end + 1]] == scores[order[pos]]) end++;
            double avg = (pos + end) / 2.0 + 1;
            for (int k = pos; k <= end; k++) rank[order[k]] = avg;
            pos = end + 1;
        }
        long positives = 0;
        double rankSum = 0;
        for (int i = 0; i < n; i++)
            if (labels[i]) { positives++; rankSum += rank[i]; }
        long negatives = n - positives;
        if (positives == 0 || negatives == 0) return double.NaN;
        return (rankSum - positives * (positives + 1) / 2.0) / ((double)positives * negatives);
    }

    /// <summary>
    /// AUC(A) - AUC(B) with a bootstrap that resamples whole days, so rows that share a market day
    /// are resampled together (the DeLong variance treats rows as independent, which gapper days are not).
    /// </summary>
    public static AucComparison CompareByDay(IReadOnlyList<double> scoresA, IReadOnlyList<double> scoresB, IReadOnlyList<bool> labels, IReadOnlyList<DateOnly> days,
        int draws = 10000, int seed = 1337, double ciLevel = 0.95, int minDays = 5)
    {
        int n = labels.Count;
        if (scoresA.Count != n || scoresB.Count != n || days.Count != n) throw new ArgumentException("All inputs must have the same length.");
        double aucA = Compute(scoresA, labels), aucB = Compute(scoresB, labels);
        double diff = aucA - aucB;
        var byDay = Enumerable.Range(0, n).GroupBy(i => days[i]).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
        int positives = labels.Count(l => l);
        if (byDay.Length < minDays || double.IsNaN(diff) || draws < 1)
            return new AucComparison(aucA, aucB, diff, double.NaN, double.NaN, 1, byDay.Length, n, positives, 0);

        var rng = new Random(seed);
        var sa = new List<double>(n);
        var sb = new List<double>(n);
        var lb = new List<bool>(n);
        var boot = new List<double>(draws);
        for (int b = 0; b < draws; b++)
        {
            sa.Clear(); sb.Clear(); lb.Clear();
            for (int k = 0; k < byDay.Length; k++)
                foreach (var i in byDay[rng.Next(byDay.Length)]) { sa.Add(scoresA[i]); sb.Add(scoresB[i]); lb.Add(labels[i]); }
            double d = Compute(sa, lb) - Compute(sb, lb);
            if (!double.IsNaN(d)) boot.Add(d);
        }
        if (boot.Count == 0) return new AucComparison(aucA, aucB, diff, double.NaN, double.NaN, 1, byDay.Length, n, positives, 0);
        boot.Sort();
        double tail = (1 - ciLevel) / 2;
        double low = boot[(int)Math.Floor(tail * (boot.Count - 1))];
        double high = boot[(int)Math.Ceiling((1 - tail) * (boot.Count - 1))];
        int below = boot.Count(v => v <= 0), above = boot.Count(v => v >= 0);
        double p = Math.Min(1, 2.0 * (1 + Math.Min(below, above)) / (boot.Count + 1));
        return new AucComparison(aucA, aucB, diff, low, high, p, byDay.Length, n, positives, boot.Count);
    }
}
