namespace IdiotProof.Evidence.Tests;

public class CalibrationTests
{
    private static IEnumerable<CalibrationSample> Many(string template, double predicted, int wins, int losses) =>
        Enumerable.Repeat(new CalibrationSample("exhaustion", template, predicted, true), wins)
            .Concat(Enumerable.Repeat(new CalibrationSample("exhaustion", template, predicted, false), losses));

    [Test]
    public void WellCalibratedTemplate_MatchesItsBuckets_AndBeatsTheBaseRate()
    {
        var samples = Many("a", 0.8, 8, 2).Concat(Many("a", 0.2, 2, 8));
        var g = Calibration.Report(samples, buckets: 5).Single();
        Assert.That(g.Samples, Is.EqualTo(20));
        Assert.That(g.Wins, Is.EqualTo(10));
        Assert.That(g.RealizedRate, Is.EqualTo(0.5).Within(1e-12));
        Assert.That(g.Buckets, Has.Count.EqualTo(2));
        Assert.That(g.Buckets[0].RealizedRate, Is.EqualTo(0.2).Within(1e-12));
        Assert.That(g.Buckets[1].RealizedRate, Is.EqualTo(0.8).Within(1e-12));
        Assert.That(g.Brier, Is.EqualTo(0.16).Within(1e-12));
        Assert.That(g.BaselineBrier, Is.EqualTo(0.25).Within(1e-12));
        Assert.That(g.Brier, Is.LessThan(g.BaselineBrier));
    }

    [Test]
    public void OverconfidentTemplate_ScoresWorseThanTheBaseRate()
    {
        var g = Calibration.Report(Many("hot", 0.9, 5, 5)).Single();
        Assert.That(g.MeanPredicted, Is.EqualTo(0.9).Within(1e-12));
        Assert.That(g.RealizedRate, Is.EqualTo(0.5).Within(1e-12));
        Assert.That(g.Brier, Is.GreaterThan(g.BaselineBrier));
    }

    [Test]
    public void GroupsBySourceAndTemplate_AndPutsProbabilityOneInTheTopBucket()
    {
        var samples = new[]
        {
            new CalibrationSample("adaptation", "tight", 1.0, true),
            new CalibrationSample("exhaustion", "exhaustion-long", 0.0, false),
            new CalibrationSample("exhaustion", "exhaustion-long", double.NaN, true),
        };
        var groups = Calibration.Report(samples, buckets: 4);
        Assert.That(groups.Select(g => (g.Source, g.Template)),
            Is.EqualTo(new[] { ("adaptation", "tight"), ("exhaustion", "exhaustion-long") }));
        Assert.That(groups[0].Buckets.Single().Low, Is.EqualTo(0.75));
        Assert.That(groups[1].Samples, Is.EqualTo(1), "NaN forecasts are dropped, not scored");
    }
}
