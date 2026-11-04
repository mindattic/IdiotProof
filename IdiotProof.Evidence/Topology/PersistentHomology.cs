namespace IdiotProof.Evidence.Topology;

/// <summary>A topological feature: born at <paramref name="Birth"/>, gone at <paramref name="Death"/> (∞ when it never dies).</summary>
public readonly record struct PersistenceBar(int Dimension, double Birth, double Death)
{
    public double Persistence => double.IsPositiveInfinity(Death) ? double.PositiveInfinity : Death - Birth;
    public bool IsInfinite => double.IsPositiveInfinity(Death);
}

/// <summary>
/// Persistent homology of the Vietoris-Rips filtration of a small point cloud, in dimensions 0
/// (connected components) and 1 (loops). H0 comes from union-find over edges sorted by length; H1
/// from the standard boundary-matrix column reduction over Z/2 with triangles as columns and edges
/// as rows. Meant for windows of a few dozen points: the full complex has O(n³) triangles.
/// </summary>
public static class PersistentHomology
{
    public const int MaxPoints = 120;

    public static IReadOnlyList<PersistenceBar> Compute(IReadOnlyList<double[]> points, bool includeH1 = true)
    {
        int n = points.Count;
        if (n == 0) return [];
        if (n > MaxPoints) throw new ArgumentException($"At most {MaxPoints} points; the Rips complex grows as n³.", nameof(points));

        var dist = Distances(points);
        var edges = new List<(int A, int B, double Len)>(n * (n - 1) / 2);
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++) edges.Add((a, b, dist[a, b]));
        edges.Sort((x, y) => x.Len != y.Len ? x.Len.CompareTo(y.Len) : x.A != y.A ? x.A.CompareTo(y.A) : x.B.CompareTo(y.B));

        var bars = new List<PersistenceBar>();
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        foreach (var e in edges)
        {
            int ra = Find(e.A), rb = Find(e.B);
            if (ra == rb) continue;
            parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            if (e.Len > 0) bars.Add(new PersistenceBar(0, 0, e.Len));
        }
        bars.Add(new PersistenceBar(0, 0, double.PositiveInfinity));

        if (includeH1 && n >= 3) bars.AddRange(H1(n, dist, edges));
        return bars;
    }

    private static IEnumerable<PersistenceBar> H1(int n, double[,] dist, List<(int A, int B, double Len)> edges)
    {
        var rank = new int[n, n];
        for (int i = 0; i < edges.Count; i++) { rank[edges[i].A, edges[i].B] = i; rank[edges[i].B, edges[i].A] = i; }

        var triangles = new List<(int E0, int E1, int E2, double Value, int MaxRank)>();
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                for (int c = b + 1; c < n; c++)
                {
                    int e0 = rank[a, b], e1 = rank[a, c], e2 = rank[b, c];
                    int max = Math.Max(e0, Math.Max(e1, e2));
                    triangles.Add((e0, e1, e2, edges[max].Len, max));
                }
        triangles.Sort((x, y) => x.Value != y.Value ? x.Value.CompareTo(y.Value) : x.MaxRank.CompareTo(y.MaxRank));

        // Column reduction over Z/2: a column is the sorted set of edge ranks on the triangle's boundary;
        // its pivot (largest rank) pairs the edge that created a loop with the triangle that filled it.
        var pivotOwner = new Dictionary<int, List<int>>();
        foreach (var t in triangles)
        {
            var col = new List<int> { t.E0, t.E1, t.E2 };
            col.Sort();
            while (col.Count > 0 && pivotOwner.TryGetValue(col[^1], out var other)) col = XorSorted(col, other);
            if (col.Count == 0) continue;
            pivotOwner[col[^1]] = col;
            double birth = edges[col[^1]].Len;
            if (t.Value > birth) yield return new PersistenceBar(1, birth, t.Value);
        }
    }

    private static List<int> XorSorted(List<int> a, List<int> b)
    {
        var r = new List<int>(a.Count + b.Count);
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (a[i] == b[j]) { i++; j++; }
            else if (a[i] < b[j]) r.Add(a[i++]);
            else r.Add(b[j++]);
        }
        while (i < a.Count) r.Add(a[i++]);
        while (j < b.Count) r.Add(b[j++]);
        return r;
    }

    private static double[,] Distances(IReadOnlyList<double[]> points)
    {
        int n = points.Count;
        var d = new double[n, n];
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
            {
                double s = 0;
                var pa = points[a];
                var pb = points[b];
                for (int k = 0; k < pa.Length; k++) { double x = pa[k] - pb[k]; s += x * x; }
                d[a, b] = d[b, a] = Math.Sqrt(s);
            }
        return d;
    }
}
