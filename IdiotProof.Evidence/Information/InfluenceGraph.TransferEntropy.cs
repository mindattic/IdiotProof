using IdiotProof.Evidence.Information;
using IdiotProof.Evidence.Search;

namespace IdiotProof.Evidence;

public sealed partial class InfluenceGraph
{
    /// <summary>
    /// Builds lag-1 edges from transfer entropy instead of correlation. Every ordered pair is
    /// tested against shuffled surrogates; an edge s→t exists only when its Benjamini-Hochberg
    /// q-value across all pairs is below <paramref name="alpha"/> and its empirical surrogate p is
    /// too, so testing many pairs doesn't manufacture leaders. Weight is the bias-corrected TE in
    /// bits, signed by the lag-1 correlation (an inverse leader still pushes the other way). Catches
    /// nonlinear leads a correlation edge would miss; for a purely nonlinear link the sign carries
    /// no directional information, only the magnitude does.
    /// </summary>
    public static InfluenceGraph FromTransferEntropy(IReadOnlyDictionary<string, double[]> returns,
        int bins = 3, int k = 1, double alpha = 0.05, int surrogates = 100, int seed = 1337)
    {
        if (alpha is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        var symbols = returns.Keys.OrderBy(s => s, StringComparer.Ordinal).ToList();
        int n = symbols.Count;
        var tested = new List<(int A, int B, TransferEntropyResult Result, double Corr)>();
        for (int a = 0; a < n; a++)
        for (int b = 0; b < n; b++)
        {
            if (a == b) continue;
            var ra = returns[symbols[a]];
            var rb = returns[symbols[b]];
            int len = Math.Min(ra.Length, rb.Length);
            if (len < 10) continue;
            var x = ra.Take(len).ToArray();
            var y = rb.Take(len).ToArray();
            var result = TransferEntropy.Test(x, y, bins, k, surrogates, seed + a * 7919 + b);
            double corr = EvidenceStats.Correlation(x.Take(len - 1).ToArray(), y.Skip(1).ToArray());
            tested.Add((a, b, result, corr));
        }

        var w = new double[n, n];
        var q = MultipleTesting.BenjaminiHochberg(tested.Select(t => t.Result.ParametricPValue).ToList());
        for (int i = 0; i < tested.Count; i++)
        {
            var (a, b, result, corr) = tested[i];
            if (q[i] >= alpha || result.PValue >= alpha || result.Effective <= 0) continue;
            w[a, b] = corr < 0 ? -result.Effective : result.Effective;
        }
        return new InfluenceGraph(symbols, w, 1);
    }
}
