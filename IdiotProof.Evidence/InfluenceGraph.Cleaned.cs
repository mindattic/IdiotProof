using IdiotProof.Evidence.Factors;

namespace IdiotProof.Evidence;

public sealed partial class InfluenceGraph
{
    /// <summary>
    /// Builds edges only from correlation the data can tell apart from chance. Lag 0: the
    /// contemporaneous matrix is cleaned by Marchenko-Pastur eigenvalue clipping before edges are
    /// cut. Lag ≥ 1: a lagged matrix has no symmetric spectrum to clip, so each edge must instead
    /// clear the elementwise noise floor of a zero correlation (2.5/√T) as well as
    /// <paramref name="minAbsCorrelation"/>.
    /// </summary>
    public static InfluenceGraph FromCleanedReturns(IReadOnlyDictionary<string, double[]> returns, int lag = 0, double minAbsCorrelation = 0.2)
    {
        if (lag < 0) throw new ArgumentOutOfRangeException(nameof(lag));
        var symbols = returns.Keys.OrderBy(s => s, StringComparer.Ordinal).ToList();
        int n = symbols.Count;
        if (n < 2) return new InfluenceGraph(symbols, new double[n, n], lag);
        int t = symbols.Min(s => returns[s].Length);

        if (lag > 0)
            return FromReturns(returns, lag, Math.Max(minAbsCorrelation, RandomMatrix.CorrelationNoiseFloor(t - lag)));

        var series = symbols.Select(s => returns[s].Take(t).ToArray()).ToList();
        var cleaned = RandomMatrix.Clean(RandomMatrix.Correlation(series), t).Matrix;
        var w = new double[n, n];
        for (int a = 0; a < n; a++)
        for (int b = 0; b < n; b++)
            if (a != b && Math.Abs(cleaned[a, b]) >= minAbsCorrelation) w[a, b] = cleaned[a, b];
        return new InfluenceGraph(symbols, w, lag);
    }
}
