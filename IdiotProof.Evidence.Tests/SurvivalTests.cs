using IdiotProof.Evidence.Survival;

namespace IdiotProof.Evidence.Tests;

public class SurvivalTests
{
    /// <summary>Freireich et al. 6-MP remission times (weeks); censored times end in a + in the literature.</summary>
    private static readonly (double T, bool Event)[] SixMp =
    {
        (6, true), (6, true), (6, true), (6, false), (7, true), (9, false), (10, true), (10, false), (11, false), (13, true), (16, true),
        (17, false), (19, false), (20, false), (22, true), (23, true), (25, false), (32, false), (32, false), (34, false), (35, false),
    };

    [Test]
    public void KaplanMeier_MatchesTheTextbook6MpCurve()
    {
        var km = SurvivalAnalysis.KaplanMeier(SixMp.Select(x => new SurvivalObservation(x.T, x.Event ? 1 : 0)));

        Assert.That(km.Select(s => s.Time), Is.EqualTo(new double[] { 6, 7, 10, 13, 16, 22, 23 }));
        var expected = new[] { 0.857, 0.807, 0.753, 0.690, 0.627, 0.538, 0.448 };
        for (int i = 0; i < expected.Length; i++) Assert.That(km[i].Survival, Is.EqualTo(expected[i]).Within(1e-3), $"step {i}");
        Assert.That(km[0].AtRisk, Is.EqualTo(21));
        Assert.That(km[0].StandardError, Is.EqualTo(0.0764).Within(1e-3), "Greenwood SE at week 6");
        Assert.That(km[^1].StandardError, Is.EqualTo(0.1346).Within(2e-3), "Greenwood SE at week 23");
        Assert.That(SurvivalAnalysis.MedianTime(km), Is.EqualTo(23));
    }

    [Test]
    public void KaplanMeier_CensoringOnlyShrinksTheRiskSet()
    {
        var km = SurvivalAnalysis.KaplanMeier(new[]
        {
            new SurvivalObservation(1, 1), new SurvivalObservation(2, 0), new SurvivalObservation(3, 1),
            new SurvivalObservation(4, 1), new SurvivalObservation(5, 0),
        });
        Assert.That(km.Select(s => s.Survival), Is.EqualTo(new[] { 0.8, 0.8 * 2 / 3, 0.8 * 2 / 3 * 0.5 }).Within(1e-12));
        Assert.That(km[1].AtRisk, Is.EqualTo(3), "the hold censored at t=2 is no longer at risk at t=3");
    }

    [Test]
    public void CumulativeIncidence_PlusSurvival_IsOneAtEveryStep_EvenWithCensoring()
    {
        var rng = new Random(5);
        var obs = Enumerable.Range(0, 300).Select(_ => new SurvivalObservation(rng.Next(1, 60), rng.Next(0, 4))).ToList();

        var cif = SurvivalAnalysis.CumulativeIncidence(obs, 3);
        var km = SurvivalAnalysis.KaplanMeier(obs);

        Assert.That(cif, Is.Not.Empty);
        foreach (var step in cif)
            Assert.That(step.Survival + step.Incidence.Sum(), Is.EqualTo(1).Within(1e-12), $"t={step.Time}");
        Assert.That(cif.Select(s => s.Survival), Is.EqualTo(km.Select(s => s.Survival)).Within(1e-12), "overall survival matches Kaplan-Meier");
        for (int k = 0; k < 3; k++)
            Assert.That(cif.Select(s => s.Incidence[k]), Is.Ordered, "incidence never decreases");
    }

    [Test]
    public void CumulativeIncidence_WithoutCensoring_IsTheEmpiricalShare()
    {
        var obs = new[] { (1, 1), (2, 2), (3, 1), (4, 1), (5, 2) }.Select(x => new SurvivalObservation(x.Item1, x.Item2)).ToList();
        var cif = SurvivalAnalysis.CumulativeIncidence(obs, 2);
        Assert.That(cif[^1].Incidence[0], Is.EqualTo(0.6).Within(1e-12));
        Assert.That(cif[^1].Incidence[1], Is.EqualTo(0.4).Within(1e-12));
        Assert.That(SurvivalAnalysis.FlatteningTime(cif, 1, 0.95), Is.EqualTo(4));
        Assert.That(SurvivalAnalysis.At(cif, 2.5, s => s.Time, s => s.Incidence[0], 0), Is.EqualTo(0.2).Within(1e-12));
    }

    [Test]
    public void Cox_RecoversAPlantedHazardRatio_UnderCensoring()
    {
        var rng = new Random(42);
        var times = new List<double>();
        var events = new List<bool>();
        var x = new List<double[]>();
        for (int i = 0; i < 3000; i++)
        {
            double group = rng.Next(2);
            double noise = rng.NextDouble();
            double rate = 0.05 * Math.Exp(Math.Log(2.0) * group + 0 * noise);
            double t = -Math.Log(1 - rng.NextDouble()) / rate;
            double censor = rng.NextDouble() * 40;
            times.Add(Math.Min(t, censor));
            events.Add(t <= censor);
            x.Add(new[] { group, noise });
        }

        var fit = CoxModel.Fit(times, events, x, new[] { "group", "noise" });

        Assert.That(fit.Converged, Is.True);
        Assert.That(fit.Events, Is.LessThan(3000), "some holds are censored");
        Assert.That(fit.Coefficients[0].HazardRatio, Is.EqualTo(2.0).Within(0.2));
        Assert.That(fit.Coefficients[0].Low, Is.LessThan(2.0));
        Assert.That(fit.Coefficients[0].High, Is.GreaterThan(2.0));
        Assert.That(fit.Coefficients[0].PValue, Is.LessThan(1e-6));
        Assert.That(fit.Coefficients[1].Low, Is.LessThan(1.0));
        Assert.That(fit.Coefficients[1].High, Is.GreaterThan(1.0), "a covariate with no effect has a CI spanning 1");
    }

    [Test]
    public void Cox_HandlesTiedTimes_WithBreslow()
    {
        // Two groups, identical tied times; the group with every event earlier gets HR > 1.
        var times = new double[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 };
        var events = Enumerable.Repeat(true, 10).ToList();
        var x = new[] { 1.0, 1, 1, 0, 1, 0, 0, 0, 1, 0 }.Select(v => new[] { v }).ToList();
        var fit = CoxModel.Fit(times, events, x);
        Assert.That(fit.Converged, Is.True);
        Assert.That(fit.Coefficients[0].HazardRatio, Is.GreaterThan(1));
        Assert.That(double.IsFinite(fit.Coefficients[0].StandardError), Is.True);
    }
}
