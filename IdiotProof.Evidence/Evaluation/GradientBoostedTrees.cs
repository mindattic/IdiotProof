namespace IdiotProof.Evidence.Evaluation;

/// <summary>Settings of <see cref="GradientBoostedTrees"/>.</summary>
public sealed record BoostingOptions
{
    public int Trees { get; init; } = 200;
    public int MaxDepth { get; init; } = 4;
    public double LearningRate { get; init; } = 0.05;
    /// <summary>L2 penalty on leaf values.</summary>
    public double Lambda { get; init; } = 1.0;
    /// <summary>Least summed hessian (weighted p(1-p)) a child must hold.</summary>
    public double MinChildHessian { get; init; } = 1.0;
    /// <summary>Most bins per feature (quantile edges of the training values).</summary>
    public int MaxBins { get; init; } = 64;
}

/// <summary>A fitted boosted-tree classifier: predicts P(y = 1).</summary>
public sealed class BoostedTreeModel
{
    internal readonly record struct Node(int Feature, float Threshold, bool MissingLeft, int Left, int Right, double Value)
    {
        public bool IsLeaf => Feature < 0;
    }

    internal BoostedTreeModel(IReadOnlyList<string> features, double bias, List<Node[]> trees, double[] gain)
    {
        Features = features;
        Bias = bias;
        Trees = trees;
        Gain = gain;
    }

    public IReadOnlyList<string> Features { get; }
    public double Bias { get; }
    internal List<Node[]> Trees { get; }
    /// <summary>Total split gain per feature: how much each feature reduced the loss.</summary>
    public IReadOnlyList<double> Gain { get; }
    public int TreeCount => Trees.Count;

    public double Margin(ReadOnlySpan<float> x)
    {
        double f = Bias;
        foreach (var tree in Trees)
        {
            int i = 0;
            while (!tree[i].IsLeaf)
            {
                var n = tree[i];
                float v = x[n.Feature];
                i = float.IsNaN(v) ? (n.MissingLeft ? n.Left : n.Right) : v <= n.Threshold ? n.Left : n.Right;
            }
            f += tree[i].Value;
        }
        return f;
    }

    public double Predict(ReadOnlySpan<float> x) => 1.0 / (1.0 + Math.Exp(-Margin(x)));

    /// <summary>Features ranked by their share of the total gain.</summary>
    public IReadOnlyList<(string Feature, double Share)> Importance()
    {
        double total = Gain.Sum();
        return Features.Select((f, i) => (f, total > 0 ? Gain[i] / total : 0)).OrderByDescending(x => x.Item2).ThenBy(x => x.f, StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// Weighted gradient-boosted decision trees for a binary label (logistic loss), pure and
/// deterministic: histogram splits on per-feature weighted quantile bins, level-wise trees of
/// limited depth, a learned direction for missing values (NaN), L2-penalized leaves. No sampling,
/// so the same inputs give the same model. The gapper exploration's non-linear learner (IP-T453);
/// the frozen study's detectors stay logistic.
/// </summary>
public static class GradientBoostedTrees
{
    private const byte MissingBin = byte.MaxValue;

    /// <summary>Fit on a row-major matrix (<paramref name="x"/>[row * features + feature], NaN = missing).</summary>
    public static BoostedTreeModel Fit(IReadOnlyList<string> features, float[] x, bool[] y, double[] w, BoostingOptions? options = null)
    {
        var o = options ?? new BoostingOptions();
        int d = features.Count, n = y.Length;
        if (x.Length != n * d) throw new ArgumentException($"x holds {x.Length} values, expected {n} rows x {d} features.");
        if (w.Length != n) throw new ArgumentException("w must have one weight per row.");
        if (o.MaxBins is < 2 or > 254) throw new ArgumentOutOfRangeException(nameof(options), "MaxBins must be 2..254.");

        var edges = new float[d][];
        var bins = new byte[n * d];
        Parallel.For(0, d, f =>
        {
            edges[f] = Edges(x, w, n, d, f, o.MaxBins);
            for (int r = 0; r < n; r++) bins[r * d + f] = Bin(x[r * d + f], edges[f]);
        });

        double wSum = 0, wPos = 0;
        for (int r = 0; r < n; r++) { wSum += w[r]; if (y[r]) wPos += w[r]; }
        double rate = Math.Clamp(wSum > 0 ? wPos / wSum : 0.5, 1e-6, 1 - 1e-6);
        double bias = Math.Log(rate / (1 - rate));

        var margin = new double[n];
        Array.Fill(margin, bias);
        var g = new double[n];
        var h = new double[n];
        var gain = new double[d];
        var trees = new List<BoostedTreeModel.Node[]>(o.Trees);
        var rows = Enumerable.Range(0, n).ToArray();

        for (int t = 0; t < o.Trees; t++)
        {
            for (int r = 0; r < n; r++)
            {
                double p = 1.0 / (1.0 + Math.Exp(-margin[r]));
                g[r] = w[r] * (p - (y[r] ? 1 : 0));
                h[r] = w[r] * p * (1 - p);
            }
            var tree = Grow(rows, bins, edges, g, h, d, o, gain);
            trees.Add(tree);
            for (int r = 0; r < n; r++)
            {
                int i = 0;
                while (!tree[i].IsLeaf)
                {
                    var node = tree[i];
                    byte b = bins[r * d + node.Feature];
                    i = b == MissingBin ? (node.MissingLeft ? node.Left : node.Right) : x[r * d + node.Feature] <= node.Threshold ? node.Left : node.Right;
                }
                margin[r] += tree[i].Value;
            }
        }
        return new BoostedTreeModel(features, bias, trees, gain);
    }

    /// <summary>Upper bin edges from weighted quantiles of the non-missing values; duplicates collapse.</summary>
    private static float[] Edges(float[] x, double[] w, int n, int d, int f, int maxBins)
    {
        var values = new List<(float V, double W)>();
        for (int r = 0; r < n; r++)
        {
            float v = x[r * d + f];
            if (!float.IsNaN(v) && w[r] > 0) values.Add((v, w[r]));
        }
        if (values.Count == 0) return [];
        values.Sort((a, b) => a.V.CompareTo(b.V));
        double total = values.Sum(v => v.W), cum = 0;
        var result = new List<float>();
        int next = 1;
        foreach (var (v, wt) in values)
        {
            cum += wt;
            while (next < maxBins && cum >= total * next / maxBins)
            {
                if (result.Count == 0 || v > result[^1]) result.Add(v);
                next++;
            }
        }
        if (result.Count == 0 || values[^1].V > result[^1]) result.Add(values[^1].V);
        return result.ToArray();
    }

    private static byte Bin(float v, float[] edges)
    {
        if (float.IsNaN(v)) return MissingBin;
        int lo = 0, hi = edges.Length - 1;
        if (edges.Length == 0) return 0;
        if (v > edges[hi]) return (byte)hi;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (v <= edges[mid]) hi = mid; else lo = mid + 1;
        }
        return (byte)lo;
    }

    private readonly record struct Split(int Feature, int Bin, bool MissingLeft, double Gain);

    private static BoostedTreeModel.Node[] Grow(int[] allRows, byte[] bins, float[][] edges, double[] g, double[] h, int d, BoostingOptions o, double[] gainTotal)
    {
        var nodes = new List<BoostedTreeModel.Node>();
        var nodeRows = new List<int[]> { allRows };
        nodes.Add(default);
        var frontier = new List<(int Node, int Depth)> { (0, 0) };
        while (frontier.Count > 0)
        {
            var nextFrontier = new List<(int, int)>();
            foreach (var (id, depth) in frontier)
            {
                var rows = nodeRows[id];
                double G = 0, H = 0;
                foreach (var r in rows) { G += g[r]; H += h[r]; }
                double leaf = -G / (H + o.Lambda) * o.LearningRate;
                var split = depth < o.MaxDepth ? BestSplit(rows, bins, edges, g, h, d, G, H, o) : (Split?)null;
                if (split is not { } s)
                {
                    nodes[id] = new BoostedTreeModel.Node(-1, 0, false, -1, -1, leaf);
                    continue;
                }
                gainTotal[s.Feature] += s.Gain;
                var left = new List<int>(); var right = new List<int>();
                foreach (var r in rows)
                {
                    byte b = bins[r * d + s.Feature];
                    bool goLeft = b == MissingBin ? s.MissingLeft : b <= s.Bin;
                    (goLeft ? left : right).Add(r);
                }
                int l = nodes.Count; nodes.Add(default); nodeRows.Add(left.ToArray());
                int rr = nodes.Count; nodes.Add(default); nodeRows.Add(right.ToArray());
                nodes[id] = new BoostedTreeModel.Node(s.Feature, edges[s.Feature][s.Bin], s.MissingLeft, l, rr, 0);
                nodeRows[id] = [];
                nextFrontier.Add((l, depth + 1));
                nextFrontier.Add((rr, depth + 1));
            }
            frontier = nextFrontier;
        }
        return nodes.ToArray();
    }

    private static Split? BestSplit(int[] rows, byte[] bins, float[][] edges, double[] g, double[] h, int d, double G, double H, BoostingOptions o)
    {
        var best = new Split?[d];
        double parent = G * G / (H + o.Lambda);
        Parallel.For(0, d, f =>
        {
            int k = edges[f].Length;
            if (k < 2) return;
            var hg = new double[k]; var hh = new double[k];
            double mg = 0, mh = 0;
            foreach (var r in rows)
            {
                byte b = bins[r * d + f];
                if (b == MissingBin) { mg += g[r]; mh += h[r]; }
                else { hg[b] += g[r]; hh[b] += h[r]; }
            }
            Split? local = null;
            double lg = 0, lh = 0;
            for (int b = 0; b < k - 1; b++)
            {
                lg += hg[b]; lh += hh[b];
                foreach (bool missingLeft in new[] { false, true })
                {
                    double gl = lg + (missingLeft ? mg : 0), hl = lh + (missingLeft ? mh : 0);
                    double gr = G - gl, hr = H - hl;
                    if (hl < o.MinChildHessian || hr < o.MinChildHessian) continue;
                    double gain = gl * gl / (hl + o.Lambda) + gr * gr / (hr + o.Lambda) - parent;
                    if (gain > 1e-12 && (local is null || gain > local.Value.Gain)) local = new Split(f, b, missingLeft, gain);
                }
            }
            best[f] = local;
        });
        Split? winner = null;
        foreach (var s in best)
            if (s is { } c && (winner is null || c.Gain > winner.Value.Gain)) winner = c;
        return winner;
    }
}
