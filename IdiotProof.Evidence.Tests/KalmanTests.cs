using IdiotProof.Evidence.StateSpace;

namespace IdiotProof.Evidence.Tests;

public class KalmanTests
{
    [Test]
    public void LocalLevel_ConvergesToAPlantedLevel()
    {
        var rng = new Random(1);
        var f = new LocalLevelFilter(q: 1e-8, r: 0.04);
        KalmanStep last = default;
        for (int i = 0; i < 2000; i++) last = f.Step(5.0 + 0.2 * Synthetic.Gaussian(rng));
        Assert.That(last.Level, Is.EqualTo(5.0).Within(0.03));
    }

    [Test]
    public void LocalTrend_RecoversAPlantedSlope()
    {
        var rng = new Random(2);
        var f = new LocalTrendFilter(qLevel: 1e-6, qSlope: 1e-10, r: 0.01);
        KalmanStep last = default;
        for (int t = 0; t < 1500; t++) last = f.Step(100 + 0.05 * t + 0.1 * Synthetic.Gaussian(rng));
        Assert.That(last.Slope, Is.EqualTo(0.05).Within(0.003));
        Assert.That(last.Level, Is.EqualTo(100 + 0.05 * 1499).Within(0.1));
    }

    [Test]
    public void Fit_RecoversTheSignalToNoiseRatio_WithinTheGridResolution()
    {
        var y = SimulateTrend(4000, qLevel: 1e-4, qSlope: 0, r: 1e-2, seed: 3);
        var fit = KalmanFit.FitLocalTrend(y);
        Assert.That(fit.R, Is.EqualTo(1e-2).Within(0.3e-2));
        double ratio = fit.QLevel / fit.R;
        Assert.That(ratio, Is.InRange(1e-3, 3e-2), $"q/r = {ratio}");
    }

    [Test]
    public void Innovations_AreWhite_UnderTheTrueModel()
    {
        const double ql = 2e-4, qs = 1e-7, r = 1e-3;
        var y = SimulateTrend(5000, ql, qs, r, seed: 4);
        var f = new LocalTrendFilter(ql, qs, r);
        var z = new List<double>();
        for (int t = 0; t < y.Count; t++)
        {
            var st = f.Step(y[t]);
            if (t >= 50) z.Add(st.Z);
        }
        Assert.That(EvidenceStats.Mean(z), Is.EqualTo(0).Within(0.05));
        Assert.That(EvidenceStats.StdDev(z), Is.EqualTo(1).Within(0.05));
        Assert.That(Math.Abs(RegimeStats.Autocorrelation(z, 1)), Is.LessThan(0.05));
    }

    [Test]
    public void Innovations_AreNotWhite_UnderAWrongModel()
    {
        // A strongly trending series filtered as if it had almost no level noise and no slope leaves autocorrelated innovations.
        var y = SimulateTrend(3000, 2e-4, 1e-6, 1e-4, seed: 5);
        var f = new LocalTrendFilter(1e-9, 0, 1e-1);
        var z = new List<double>();
        for (int t = 0; t < y.Count; t++) { var st = f.Step(y[t]); if (t >= 50) z.Add(st.Z); }
        Assert.That(RegimeStats.Autocorrelation(z, 1), Is.GreaterThan(0.3));
    }

    [Test]
    public void HedgeRatio_TracksADriftingBeta()
    {
        var rng = new Random(6);
        var f = new HedgeRatioFilter(delta: 1e-3, r: 1e-4);
        double x = 0, err = 0;
        int counted = 0;
        const int n = 3000;
        for (int t = 0; t < n; t++)
        {
            x += 0.01 * Synthetic.Gaussian(rng);
            double beta = 1.0 + (double)t / n;
            double y = 0.02 + beta * x + 0.01 * Synthetic.Gaussian(rng);
            var st = f.Step(x, y);
            if (t >= n / 2) { err += Math.Abs(st.Beta - beta); counted++; }
        }
        Assert.That(err / counted, Is.LessThan(0.15));
    }

    [Test]
    public void Features_HaveNoLookahead()
    {
        var a = SimulateTrend(300, 1e-4, 1e-8, 1e-4, seed: 7);
        var b = a.ToList();
        for (int i = 200; i < b.Count; i++) b[i] += 5;
        var lead = SimulateTrend(300, 1e-4, 1e-8, 1e-4, seed: 8);
        var p = new LocalTrendParameters(1e-4, 1e-8, 1e-4, 0);
        var fa = KalmanFeatures.Compute(a, p, lead);
        var fb = KalmanFeatures.Compute(b, p, lead);
        for (int i = 0; i < 200; i++)
        {
            Assert.That(fb.Slope[i], Is.EqualTo(fa.Slope[i]).Or.NaN);
            Assert.That(fb.InnovationZ[i], Is.EqualTo(fa.InnovationZ[i]).Or.NaN);
            Assert.That(fb.PairResidualZ[i], Is.EqualTo(fa.PairResidualZ[i]).Or.NaN);
        }
        Assert.That(fb.InnovationZ[200], Is.Not.EqualTo(fa.InnovationZ[200]));
    }

    [Test]
    public void Features_PairResidual_IsNaN_WithoutALeader_AndDefinedWithOne()
    {
        var y = SimulateTrend(200, 1e-4, 1e-8, 1e-4, seed: 9);
        var p = new LocalTrendParameters(1e-4, 1e-8, 1e-4, 0);
        Assert.That(KalmanFeatures.Compute(y, p).PairResidualZ, Is.All.NaN);
        Assert.That(KalmanFeatures.Compute(y, p, y).PairResidualZ.Count(v => !double.IsNaN(v)), Is.GreaterThan(100));
    }

    private static List<double> SimulateTrend(int n, double qLevel, double qSlope, double r, int seed)
    {
        var rng = new Random(seed);
        double level = 1.0, slope = 0;
        var y = new List<double>(n);
        for (int t = 0; t < n; t++)
        {
            level += slope + Math.Sqrt(qLevel) * Synthetic.Gaussian(rng);
            slope += Math.Sqrt(qSlope) * Synthetic.Gaussian(rng);
            y.Add(level + Math.Sqrt(r) * Synthetic.Gaussian(rng));
        }
        return y;
    }
}
