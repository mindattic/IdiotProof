using IdiotProof.Core.Catalysts;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: the pure catalyst score and its breakdown.</summary>
[TestFixture]
public sealed class CatalystScorerTests
{
    private static readonly CatalystScoringWeights W = new()
    {
        TypePriors = new(StringComparer.OrdinalIgnoreCase) { ["Fda"] = 70, ["Other"] = 15 },
        Magnitude = new(StringComparer.OrdinalIgnoreCase) { ["High"] = 1.0, ["Medium"] = 0.7, ["Low"] = 0.4, ["Unknown"] = 0.6 },
        SourceWeights = new(StringComparer.OrdinalIgnoreCase) { ["1"] = 1.0, ["2"] = 0.9, ["3"] = 0.7 },
        NoveltyBonusMax = 15,
        NoveltyWindowDays = 90,
        FreshnessHalfLifeMinutes = 240,
        FreshnessFloor = 0.05,
    };

    [Test]
    public void ShippedWeights_Load()
    {
        var w = CatalystCatalogs.ScoringWeights;
        Assert.That(w.TypePriors, Does.ContainKey("Fda"));
        Assert.That(w.FreshnessHalfLifeMinutes, Is.GreaterThan(0));
        Assert.That(w.NoveltyWindowDays, Is.GreaterThan(0));
    }

    [Test]
    public void Freshness_DecaysMonotonically_AndHalvesAtTheHalfLife()
    {
        double last = double.MaxValue;
        foreach (var age in new double[] { 0, 1, 10, 60, 240, 480, 1440, 10000, 100000 })
        {
            var f = CatalystScorer.Freshness(W, age);
            Assert.That(f, Is.LessThanOrEqualTo(last), $"age {age}");
            Assert.That(f, Is.InRange(W.FreshnessFloor, 1.0));
            last = f;
        }
        Assert.That(CatalystScorer.Freshness(W, 0), Is.EqualTo(1.0));
        Assert.That(CatalystScorer.Freshness(W, 240), Is.EqualTo(0.5).Within(1e-9));
        Assert.That(CatalystScorer.Freshness(W, -5), Is.EqualTo(1.0), "a clock skew never scores above fresh");
    }

    [Test]
    public void NoveltyBonus_ShrinksWithEarlierEvents()
    {
        Assert.That(CatalystScorer.NoveltyBonus(W, 0), Is.EqualTo(15));
        Assert.That(CatalystScorer.NoveltyBonus(W, 2), Is.EqualTo(5));
        Assert.That(CatalystScorer.NoveltyBonus(W, 1), Is.GreaterThan(CatalystScorer.NoveltyBonus(W, 2)));
    }

    [Test]
    public void Explain_ComponentsRecombineToTheScore()
    {
        var b = CatalystScorer.Explain(W, CatalystPreClass.Fda, 1, "Medium", 2, 3, 120);
        var expected = Math.Clamp((70 * 0.7 * 0.9 + 15.0 / 4) * Math.Pow(2, -0.5), 0, 100);
        Assert.Multiple(() =>
        {
            Assert.That(b.TypePrior, Is.EqualTo(70));
            Assert.That(b.Magnitude, Is.EqualTo(0.7));
            Assert.That(b.SourceWeight, Is.EqualTo(0.9));
            Assert.That(b.NoveltyBonus, Is.EqualTo(3.75));
            Assert.That(b.Freshness, Is.EqualTo(Math.Pow(2, -0.5)).Within(1e-12));
            Assert.That(b.Score, Is.EqualTo(expected).Within(1e-9));
            Assert.That(CatalystScorer.Combine(b.TypePrior, b.Magnitude, b.SourceWeight, b.NoveltyBonus, b.Freshness), Is.EqualTo(b.Score));
            Assert.That(b.Direction, Is.EqualTo(1));
            Assert.That(CatalystScorer.Score(W, CatalystPreClass.Fda, 1, "Medium", 2, 3, 120), Is.EqualTo((b.Score, 1)));
        });
    }

    [Test]
    public void UnknownMagnitude_UsesTheUnknownWeight_AndUnknownTypeFallsBackToOther()
    {
        var b = CatalystScorer.Explain(W, CatalystPreClass.Earnings, 0, null, 1, 0, 0);
        Assert.That(b.MagnitudeLabel, Is.EqualTo("Unknown"));
        Assert.That(b.Magnitude, Is.EqualTo(0.6));
        Assert.That(b.TypePrior, Is.EqualTo(15), "Earnings is absent from these weights, so the Other prior applies");
    }

    [Test]
    public void Score_IsClampedTo100_AndDirectionIsASign()
    {
        var hot = new CatalystScoringWeights { TypePriors = new() { ["Fda"] = 500 }, Magnitude = new() { ["High"] = 1 }, SourceWeights = new() { ["1"] = 1 } };
        var (score, direction) = CatalystScorer.Score(hot, CatalystPreClass.Fda, 7, "High", 1, 0, 0);
        Assert.That((score, direction), Is.EqualTo((100d, 1)));
        Assert.That(CatalystScorer.Score(hot, CatalystPreClass.Fda, -3, "High", 1, 0, 0).Direction, Is.EqualTo(-1));
    }

    [Test]
    public void Describe_ShowsEveryTerm()
    {
        var text = CatalystScorer.Explain(W, CatalystPreClass.Fda, -1, "High", 1, 0, 0).Describe();
        Assert.That(text, Does.Contain("70").And.Contain("[Fda]").And.Contain("tier 1").And.Contain("novelty 0").And.Contain("direction -1"));
    }
}
