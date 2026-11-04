namespace IdiotProof.Evidence;

/// <summary>
/// The "invisible strings" between symbols. Edges are measured return correlations (lag 0:
/// they move together; lag 1: this one's move precedes that one's). A shock at one node spreads
/// by a Katz-style diffusion, Σ βᵏ Pᵏ, where P is the signed, row-normalized edge matrix, so a
/// direct neighbor feels it at strength β, a neighbor-of-neighbor at most β², and so on: the pull
/// weakens with every hop.
/// </summary>
public sealed class InfluenceGraph
{
    private readonly double[,] weights;
    public IReadOnlyList<string> Symbols { get; }
    public int Lag { get; }

    private InfluenceGraph(IReadOnlyList<string> symbols, double[,] weights, int lag)
    {
        Symbols = symbols;
        this.weights = weights;
        Lag = lag;
    }

    /// <summary>The measured edge from <paramref name="from"/> to <paramref name="to"/> (0 when below the threshold).</summary>
    public double Edge(string from, string to) => weights[IndexOf(from), IndexOf(to)];

    /// <summary>
    /// Builds edges from aligned per-bar returns. With lag L, edge s→t is corr(r_s[i], r_t[i+L]),
    /// i.e. how well s's move predicts t's move L bars later. Edges below |minAbsCorrelation| are dropped.
    /// </summary>
    public static InfluenceGraph FromReturns(IReadOnlyDictionary<string, double[]> returns, int lag = 0, double minAbsCorrelation = 0.2)
    {
        if (lag < 0) throw new ArgumentOutOfRangeException(nameof(lag));
        var symbols = returns.Keys.OrderBy(s => s, StringComparer.Ordinal).ToList();
        int n = symbols.Count;
        var w = new double[n, n];
        for (int a = 0; a < n; a++)
        for (int b = 0; b < n; b++)
        {
            if (a == b) continue;
            var ra = returns[symbols[a]];
            var rb = returns[symbols[b]];
            int len = Math.Min(ra.Length, rb.Length) - lag;
            if (len < 3) continue;
            var x = new double[len];
            var y = new double[len];
            Array.Copy(ra, 0, x, 0, len);
            Array.Copy(rb, lag, y, 0, len);
            double c = EvidenceStats.Correlation(x, y);
            if (Math.Abs(c) >= minAbsCorrelation) w[a, b] = c;
        }
        return new InfluenceGraph(symbols, w, lag);
    }

    /// <summary>Builds a graph from explicit edges; useful for sector maps and tests.</summary>
    public static InfluenceGraph FromEdges(IReadOnlyList<string> symbols, IEnumerable<(string From, string To, double Weight)> edges)
    {
        var list = symbols.ToList();
        var w = new double[list.Count, list.Count];
        foreach (var (from, to, weight) in edges) w[list.IndexOf(from), list.IndexOf(to)] = weight;
        return new InfluenceGraph(list, w, 0);
    }

    /// <summary>
    /// Influence of a unit shock at <paramref name="source"/> on every other node: Σ_{k=1..maxHops} βᵏ (Pᵏ)[source, ·].
    /// Each row of P has absolute sum 1, so total influence is bounded by β/(1-β).
    /// </summary>
    public IReadOnlyDictionary<string, double> Influence(string source, double beta = 0.5, int maxHops = 6)
    {
        if (beta <= 0 || beta >= 1) throw new ArgumentOutOfRangeException(nameof(beta));
        int n = Symbols.Count;
        var p = RowNormalized();
        var current = new double[n];
        current[IndexOf(source)] = 1;
        var total = new double[n];
        double scale = 1;
        for (int hop = 1; hop <= maxHops; hop++)
        {
            var next = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (current[i] == 0) continue;
                for (int j = 0; j < n; j++) next[j] += current[i] * p[i, j];
            }
            scale *= beta;
            for (int j = 0; j < n; j++) total[j] += scale * next[j];
            current = next;
        }
        var result = new Dictionary<string, double>(n);
        for (int j = 0; j < n; j++)
            if (Symbols[j] != source) result[Symbols[j]] = total[j];
        return result;
    }

    /// <summary>Shortest hop count between two symbols over non-zero edges (-1 when unreachable).</summary>
    public int HopDistance(string from, string to)
    {
        int s = IndexOf(from), t = IndexOf(to), n = Symbols.Count;
        var dist = Enumerable.Repeat(-1, n).ToArray();
        var queue = new Queue<int>();
        dist[s] = 0;
        queue.Enqueue(s);
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            for (int v = 0; v < n; v++)
                if (weights[u, v] != 0 && dist[v] < 0) { dist[v] = dist[u] + 1; queue.Enqueue(v); }
        }
        return dist[t];
    }

    private double[,] RowNormalized()
    {
        int n = Symbols.Count;
        var p = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            double abs = 0;
            for (int j = 0; j < n; j++) abs += Math.Abs(weights[i, j]);
            if (abs <= 0) continue;
            for (int j = 0; j < n; j++) p[i, j] = weights[i, j] / abs;
        }
        return p;
    }

    private int IndexOf(string symbol)
    {
        for (int i = 0; i < Symbols.Count; i++) if (Symbols[i] == symbol) return i;
        throw new KeyNotFoundException(symbol);
    }
}
