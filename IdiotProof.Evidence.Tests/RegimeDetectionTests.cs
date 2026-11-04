using IdiotProof.Evidence.Adaptation;
using IdiotProof.Evidence.Regimes;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class RegimeDetectionTests
{
    private static double[] Gaussian(int n, double mean, double sd, Random rng)
    {
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = mean + sd * Synthetic.Gaussian(rng);
        return x;
    }

    private static double[] Concat(params double[][] parts) => parts.SelectMany(p => p).ToArray();

    [Test]
    public void Bocpd_FlagsAPlantedVarianceShift_WithinAFewBars()
    {
        var rng = new Random(21);
        var xs = Concat(Gaussian(300, 0, 1, rng), Gaussian(200, 0, 4, rng));
        var events = Bocpd.Detect(xs, NormalGammaPrior.FromSample(xs.Take(50).ToList()));
        var hit = events.FirstOrDefault(e => e.DetectedAt >= 300);
        Assert.That(events.Any(e => e.DetectedAt >= 300), Is.True, "the shift was never flagged");
        Assert.That(hit.DetectedAt - 300, Is.LessThanOrEqualTo(15), $"flagged at {hit.DetectedAt}");
        Assert.That(hit.StartsAt, Is.InRange(295, 310), "the estimated start should sit near the true switch");
        Assert.That(events.Count(e => e.DetectedAt < 300), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void Bocpd_FlagsAPlantedMeanShift_WithinAFewBars()
    {
        var rng = new Random(22);
        var xs = Concat(Gaussian(300, 0, 1, rng), Gaussian(200, 3, 1, rng));
        var events = Bocpd.Detect(xs, NormalGammaPrior.FromSample(xs.Take(50).ToList()));
        var after = events.Where(e => e.DetectedAt >= 300).ToList();
        Assert.That(after, Is.Not.Empty);
        Assert.That(after[0].DetectedAt - 300, Is.LessThanOrEqualTo(10));
    }

    [Test]
    public void Bocpd_StaysQuietOnStationaryNoise()
    {
        var rng = new Random(23);
        var xs = Gaussian(2000, 0, 1, rng);
        var events = Bocpd.Detect(xs, NormalGammaPrior.FromSample(xs.Take(50).ToList()));
        // The documented cost of catching volatility decreases: ~1–1.5 false alarms per 1,000 bars.
        Assert.That(events.Count, Is.LessThanOrEqualTo(3), $"{events.Count} false alarms in 2000 stationary bars");
        var abruptOnly = Bocpd.Detect(xs, NormalGammaPrior.FromSample(xs.Take(50).ToList()), minMapDrop: int.MaxValue);
        Assert.That(abruptOnly.Count, Is.LessThanOrEqualTo(1), $"{abruptOnly.Count} abrupt-only false alarms");
    }

    [Test]
    public void Bocpd_RunLengthPosterior_IsADistribution_AndGrowsWithoutChange()
    {
        var rng = new Random(24);
        var xs = Gaussian(100, 0, 1, rng);
        var model = new Bocpd(NormalGammaPrior.FromSample(xs.Take(20).ToList()));
        foreach (var x in xs) model.Update(x);
        Assert.That(model.RunLengthPosterior.Sum(), Is.EqualTo(1).Within(1e-9));
        Assert.That(model.MapRunLength, Is.GreaterThan(50));
    }

    [Test]
    public void Bocpd_IsCausal_LaterDataNeverChangesAnEarlierDetection()
    {
        var rng = new Random(25);
        var head = Concat(Gaussian(200, 0, 1, rng), Gaussian(100, 0, 5, rng));
        var tailA = Gaussian(200, 0, 1, new Random(1));
        var tailB = Gaussian(200, 10, 9, new Random(2));
        var prior = NormalGammaPrior.FromSample(head.Take(50).ToList());
        var a = Bocpd.Detect(Concat(head, tailA), prior).Where(e => e.DetectedAt < head.Length).ToList();
        var b = Bocpd.Detect(Concat(head, tailB), prior).Where(e => e.DetectedAt < head.Length).ToList();
        Assert.That(b, Is.EqualTo(a));
    }

    [Test]
    public void Hmm_RecoversPlantedStates()
    {
        var rng = new Random(31);
        var xs = new List<double>();
        int state = 0;
        double[] sd = [0.5, 2.0];
        for (int t = 0; t < 4000; t++)
        {
            if (rng.NextDouble() < 0.02) state = 1 - state;
            xs.Add(sd[state] * Synthetic.Gaussian(rng));
        }
        var hmm = GaussianHmm.Fit(xs);
        Assert.That(hmm.States[0].StdDev, Is.EqualTo(0.5).Within(0.08));
        Assert.That(hmm.States[1].StdDev, Is.EqualTo(2.0).Within(0.3));
        Assert.That(hmm.Persistence(0), Is.GreaterThan(0.93));
        Assert.That(hmm.Persistence(1), Is.GreaterThan(0.93));
    }

    [Test]
    public void Hmm_FilterIsForwardOnly_FutureBarsNeverChangeAPastRow()
    {
        var rng = new Random(32);
        var train = Concat(Gaussian(500, 0, 0.5, rng), Gaussian(500, 0, 2, rng));
        var hmm = GaussianHmm.Fit(train);
        var head = Gaussian(100, 0, 0.5, rng);
        var a = hmm.Filter(Concat(head, Gaussian(50, 0, 0.5, new Random(1))));
        var b = hmm.Filter(Concat(head, Gaussian(50, 0, 6, new Random(2))));
        for (int t = 0; t < head.Length; t++) Assert.That(b[t], Is.EqualTo(a[t]));
    }

    [Test]
    public void Hmm_DetectsAVolatilitySwitch()
    {
        var rng = new Random(33);
        var train = Concat(Gaussian(600, 0, 0.5, rng), Gaussian(600, 0, 2, rng), Gaussian(600, 0, 0.5, rng));
        var hmm = GaussianHmm.Fit(train);
        var live = Concat(Gaussian(200, 0, 0.5, rng), Gaussian(200, 0, 2, rng));
        var switches = hmm.DetectSwitches(live);
        Assert.That(switches.Any(e => e.DetectedAt >= 200 && e.DetectedAt <= 220), Is.True,
            "switches at: " + string.Join(",", switches.Select(e => e.DetectedAt)));
    }

    [Test]
    public void Comparison_ReportsDelayAndFalseAlarms_ForEveryDetector()
    {
        var rng = new Random(41);
        var training = Concat(Gaussian(400, 0, 1, rng), Gaussian(400, 0, 3, rng));
        var series = Concat(Gaussian(400, 0, 1, rng), Gaussian(300, 0, 3, rng), Gaussian(300, 0, 1, rng));
        var scores = RegimeDetectorComparison.Compare(training, series, [400, 700], maxDelay: 300);
        Assert.That(scores.Select(s => s.Method), Is.EquivalentTo(new[] { "bocpd", "hmm", "vr" }));
        var bocpd = scores.Single(s => s.Method == "bocpd");
        Assert.That(bocpd.Detected, Is.EqualTo(2), bocpd.ToString());
        Assert.That(bocpd.MeanDelay, Is.LessThanOrEqualTo(15), bocpd.ToString());
        // The variance ratio measures serial dependence, not volatility; a pure volatility switch is
        // invisible to it, which is the reason BOCPD and the HMM exist in this toolkit.
        var vr = scores.Single(s => s.Method == "vr");
        Assert.That(vr.Observations, Is.EqualTo(series.Length));
        Assert.That(vr.Detected, Is.LessThanOrEqualTo(bocpd.Detected), vr.ToString());
        TestContext.Out.WriteLine(string.Join("\n", scores));
    }

    [Test]
    public void Score_MatchesEventsToSwitches_AndCountsTheRestAsFalseAlarms()
    {
        var events = new List<ChangePoint> { new(50, 50, 1), new(105, 103, 1), new(300, 300, 1) };
        var score = RegimeDetectorComparison.Score("x", events, [100, 400], 500, maxDelay: 20);
        Assert.That(score.Detected, Is.EqualTo(1));
        Assert.That(score.MeanDelay, Is.EqualTo(5));
        Assert.That(score.FalseAlarms, Is.EqualTo(2));
    }

    private static readonly DateTime Open = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static List<Candle> Bars(IReadOnlyList<double> returns)
    {
        var list = new List<Candle>();
        double p = 3;
        for (int i = 0; i <= returns.Count; i++)
        {
            double prev = p;
            if (i > 0) p *= Math.Exp(returns[i - 1]);
            list.Add(new Candle
            {
                Symbol = "T", StartUtc = Open.AddMinutes(i), EndUtc = Open.AddMinutes(i + 1),
                Open = (decimal)prev, Close = (decimal)p, High = (decimal)Math.Max(prev, p), Low = (decimal)Math.Min(prev, p), Volume = 1000,
            });
        }
        return list;
    }

    [Test]
    public void Gate_ResetsEvidenceAfterARegimeChange_AndWaitsForFreshBars()
    {
        var rng = new Random(51);
        var returns = Concat(Gaussian(80, 0, 0.001, rng), Gaussian(6, 0, 0.012, rng));
        var candles = Bars(returns);
        var options = new InformationGateOptions { ChangePointThreshold = 0.5, MinBarsAfterChange = 10 };
        var now = candles[^1].EndUtc;

        var shortly = InformationGate.Check(candles, Open, now, options);
        Assert.That(shortly.Ready, Is.False, shortly.Reason);
        Assert.That(shortly.Reason, Does.Contain("regime changed"));
        Assert.That(shortly.EvidenceStartUtc, Is.Not.Null);
        Assert.That(shortly.EvidenceStartUtc!.Value, Is.GreaterThanOrEqualTo(Open.AddMinutes(75)));

        var more = Bars(Concat(returns, Gaussian(20, 0, 0.012, rng)));
        var later = InformationGate.Check(more, Open, more[^1].EndUtc, options);
        Assert.That(later.Ready, Is.True, later.Reason);
        Assert.That(later.EvidenceStartUtc, Is.EqualTo(shortly.EvidenceStartUtc));
    }

    [Test]
    public void Gate_WithoutAThreshold_BehavesAsBefore_AndStationaryBarsNeverReset()
    {
        var rng = new Random(52);
        var candles = Bars(Gaussian(120, 0, 0.001, rng));
        var now = candles[^1].EndUtc;
        var off = InformationGate.Check(candles, Open, now);
        Assert.That(off.Ready, Is.True);
        Assert.That(off.EvidenceStartUtc, Is.Null);
        var on = InformationGate.Check(candles, Open, now, new InformationGateOptions { ChangePointThreshold = 0.5 });
        Assert.That(on.Ready, Is.True, on.Reason);
        Assert.That(on.EvidenceStartUtc, Is.Null, on.Reason);
    }
}
