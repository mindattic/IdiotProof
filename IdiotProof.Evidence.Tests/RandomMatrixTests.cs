using IdiotProof.Evidence.Factors;

namespace IdiotProof.Evidence.Tests;

public class RandomMatrixTests
{
    [Test]
    public void Jacobi_ReconstructsAKnownMatrix_AndFindsItsEigenvalues()
    {
        var m = new double[,] { { 4, 1, 2 }, { 1, 3, 0 }, { 2, 0, 5 } };
        var eig = SymmetricEigen.Decompose(m);
        var back = SymmetricEigen.Reconstruct(eig.Values, eig.Vectors);
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            Assert.That(back[i, j], Is.EqualTo(m[i, j]).Within(1e-10));
        Assert.That(eig.Values.Sum(), Is.EqualTo(12).Within(1e-10), "trace");
        Assert.That(eig.Values, Is.Ordered.Descending);

        var two = SymmetricEigen.Decompose(new double[,] { { 2, 1 }, { 1, 2 } });
        Assert.That(two.Values[0], Is.EqualTo(3).Within(1e-12));
        Assert.That(two.Values[1], Is.EqualTo(1).Within(1e-12));
    }

    [Test]
    public void Jacobi_EigenvectorsAreOrthonormal()
    {
        var rng = new Random(5);
        int n = 12;
        var a = new double[n, n];
        for (int i = 0; i < n; i++)
        for (int j = i; j < n; j++)
            a[i, j] = a[j, i] = Synthetic.Gaussian(rng);
        var eig = SymmetricEigen.Decompose(a);
        for (int p = 0; p < n; p++)
        for (int q = 0; q < n; q++)
        {
            double dot = 0;
            for (int i = 0; i < n; i++) dot += eig.Vectors[i, p] * eig.Vectors[i, q];
            Assert.That(dot, Is.EqualTo(p == q ? 1 : 0).Within(1e-9));
        }
    }

    [Test]
    public void PureNoise_EigenvaluesStayInsideTheMarchenkoPasturBulk_AndCleaningErasesFakeCorrelation()
    {
        int n = 40, t = 400;
        var series = Enumerable.Range(0, n).Select(i => Synthetic.RandomWalkReturns(t, seed: 100 + i)).ToList();
        var raw = RandomMatrix.Correlation(series);
        var cleaned = RandomMatrix.Clean(raw, t);

        double upper = RandomMatrix.MarchenkoPasturUpper(n, t);
        Assert.That(cleaned.Eigenvalues.Max(), Is.LessThan(upper * 1.1), "largest noise eigenvalue near the MP edge");
        Assert.That(cleaned.Eigenvalues.Min(), Is.GreaterThan(RandomMatrix.MarchenkoPasturLower(n, t) * 0.8));
        Assert.That(cleaned.SignalCount, Is.LessThanOrEqualTo(1));

        Assert.That(MeanAbsOffDiagonal(cleaned.Matrix), Is.LessThan(MeanAbsOffDiagonal(raw) * 0.5),
            "cleaning should shrink chance correlations toward 0");
        for (int i = 0; i < n; i++) Assert.That(cleaned.Matrix[i, i], Is.EqualTo(1).Within(1e-12));
    }

    [Test]
    public void PlantedOneFactorModel_KeepsItsFactorAboveTheEdge_AndSurvivesCleaning()
    {
        int n = 30, t = 500;
        var rng = new Random(21);
        var market = Synthetic.RandomWalkReturns(t, seed: 77);
        var series = new List<double[]>();
        for (int i = 0; i < n; i++)
        {
            var s = new double[t];
            for (int x = 0; x < t; x++) s[x] = 0.6 * market[x] + 0.01 * Synthetic.Gaussian(rng);
            series.Add(s);
        }
        var raw = RandomMatrix.Correlation(series);
        var cleaned = RandomMatrix.Clean(raw, t);
        Assert.That(cleaned.Eigenvalues[0], Is.GreaterThan(cleaned.UpperBound * 5));
        Assert.That(cleaned.SignalCount, Is.EqualTo(1));
        // True pairwise correlation is 0.36/1.36 ≈ 0.26; a market-wide factor survives cleaning intact.
        Assert.That(MeanAbsOffDiagonal(raw), Is.EqualTo(0.26).Within(0.05));
        Assert.That(MeanAbsOffDiagonal(cleaned.Matrix), Is.EqualTo(MeanAbsOffDiagonal(raw)).Within(0.03), "the real common factor is kept");
    }

    [Test]
    public void Residuals_AreUncorrelatedWithTheRemovedFactor_AndKeepIdiosyncraticMoves()
    {
        int n = 10, t = 600;
        var rng = new Random(3);
        var market = Synthetic.RandomWalkReturns(t, seed: 9);
        var idio = Enumerable.Range(0, n).Select(i => Synthetic.RandomWalkReturns(t, seed: 300 + i, sigma: 0.005)).ToArray();
        var series = Enumerable.Range(0, n).Select(i => market.Select((m, x) => (0.5 + 0.1 * i) * m + idio[i][x]).ToArray()).ToList();

        var model = Eigenportfolios.Fit(series);
        Assert.That(model.SignificantFactors, Is.EqualTo(1));
        var factor = model.FactorReturns(0);
        var residuals = model.Residuals(series, 1);

        for (int i = 0; i < n; i++)
        {
            Assert.That(EvidenceStats.Correlation(residuals[i], factor), Is.EqualTo(0).Within(1e-9));
            Assert.That(EvidenceStats.Correlation(residuals[i], idio[i]), Is.GreaterThan(0.6), "residual is the stock's own story");
        }
    }

    [Test]
    public void CleanedGraph_DropsNoiseEdges_KeepsTheRealLink()
    {
        int t = 400;
        var a = Synthetic.RandomWalkReturns(t, seed: 1);
        var rng = new Random(2);
        var b = a.Select(x => 0.9 * x + 0.004 * Synthetic.Gaussian(rng)).ToArray();
        var returns = new Dictionary<string, double[]> { ["A"] = a, ["B"] = b };
        for (int i = 0; i < 20; i++) returns[$"N{i:00}"] = Synthetic.RandomWalkReturns(t, seed: 500 + i);

        var raw = InfluenceGraph.FromReturns(returns, 0, minAbsCorrelation: 0.08);
        var clean = InfluenceGraph.FromCleanedReturns(returns, 0, minAbsCorrelation: 0.08);
        int rawNoise = CountEdges(raw, s => s.StartsWith('N'));
        int cleanNoise = CountEdges(clean, s => s.StartsWith('N'));
        // Clipping is a shrinkage estimator: a two-stock cluster's small eigenvalue is lifted to the
        // noise mean, so the link is kept but shrunk (raw ≈ 0.91). It must stay the strongest edge.
        double link = clean.Edge("A", "B");
        Assert.That(link, Is.GreaterThan(0.2));
        foreach (var x in clean.Symbols)
        foreach (var y in clean.Symbols)
            if (x != y && !(x is "A" or "B" && y is "A" or "B"))
                Assert.That(Math.Abs(clean.Edge(x, y)), Is.LessThan(link), $"{x}->{y}");
        Assert.That(cleanNoise, Is.LessThan(rawNoise), $"noise edges raw {rawNoise}, cleaned {cleanNoise}");
    }

    [Test]
    public void CleanedLaggedGraph_RequiresTheNoiseFloor()
    {
        int t = 100;
        var returns = new Dictionary<string, double[]>
        {
            ["X"] = Synthetic.RandomWalkReturns(t, seed: 40),
            ["Y"] = Synthetic.RandomWalkReturns(t, seed: 41),
        };
        var g = InfluenceGraph.FromCleanedReturns(returns, lag: 1, minAbsCorrelation: 0);
        double floor = RandomMatrix.CorrelationNoiseFloor(t - 1);
        Assert.That(Math.Abs(g.Edge("X", "Y")), Is.EqualTo(0).Or.GreaterThanOrEqualTo(floor));
        Assert.That(Math.Abs(g.Edge("Y", "X")), Is.EqualTo(0).Or.GreaterThanOrEqualTo(floor));
    }

    private static double MeanAbsOffDiagonal(double[,] m)
    {
        int n = m.GetLength(0);
        double sum = 0;
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
            if (i != j) sum += Math.Abs(m[i, j]);
        return sum / (n * (n - 1));
    }

    private static int CountEdges(InfluenceGraph g, Func<string, bool> both)
    {
        int c = 0;
        foreach (var a in g.Symbols)
        foreach (var b in g.Symbols)
            if (a != b && both(a) && both(b) && g.Edge(a, b) != 0) c++;
        return c;
    }
}
