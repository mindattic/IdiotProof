namespace IdiotProof.Evidence.Tests;

public class EvidenceStatsTests
{
    [Test]
    public void BinomialUpperTail_MatchesExactSmallCases()
    {
        Assert.That(EvidenceStats.BinomialUpperTail(10, 10), Is.EqualTo(1.0 / 1024).Within(1e-12));
        Assert.That(EvidenceStats.BinomialUpperTail(0, 10), Is.EqualTo(1.0));
        // P(X >= 8 | 10, 0.5) = (45 + 10 + 1) / 1024
        Assert.That(EvidenceStats.BinomialUpperTail(8, 10), Is.EqualTo(56.0 / 1024).Within(1e-12));
        Assert.That(EvidenceStats.BinomialUpperTail(11, 10), Is.EqualTo(0.0));
    }

    [Test]
    public void BinomialUpperTail_FiftyOnePercentOfTenThousand_IsSignificant_ButOfOneHundred_IsNot()
    {
        Assert.That(EvidenceStats.BinomialUpperTail(5100, 10000), Is.LessThan(0.05));
        Assert.That(EvidenceStats.BinomialUpperTail(51, 100), Is.GreaterThan(0.3));
    }

    [Test]
    public void WilsonInterval_MatchesPublishedValue()
    {
        // 81 of 263: Wilson 95% interval ≈ (0.2553, 0.3662).
        var (lo, hi) = EvidenceStats.WilsonInterval(81, 263);
        Assert.That(lo, Is.EqualTo(0.2553).Within(5e-4));
        Assert.That(hi, Is.EqualTo(0.3662).Within(5e-4));
    }

    [Test]
    public void WilsonInterval_StaysInsideUnitRange_AtExtremes()
    {
        var (lo0, hi0) = EvidenceStats.WilsonInterval(0, 5);
        var (lo5, hi5) = EvidenceStats.WilsonInterval(5, 5);
        Assert.That(lo0, Is.EqualTo(0).Within(1e-12));
        Assert.That(hi0, Is.LessThan(1));
        Assert.That(hi5, Is.EqualTo(1).Within(1e-12));
        Assert.That(lo5, Is.GreaterThan(0));
    }

    [Test]
    public void LogGamma_MatchesFactorials()
    {
        Assert.That(Math.Exp(EvidenceStats.LogGamma(6)), Is.EqualTo(120).Within(1e-9));
        Assert.That(Math.Exp(EvidenceStats.LogGamma(0.5)), Is.EqualTo(Math.Sqrt(Math.PI)).Within(1e-12));
    }

    [Test]
    public void BrierAndLogLoss_CoinFlipBaselines()
    {
        var p = new[] { 0.5, 0.5, 0.5, 0.5 };
        var o = new[] { true, false, true, false };
        Assert.That(EvidenceStats.BrierScore(p, o), Is.EqualTo(0.25).Within(1e-12));
        Assert.That(EvidenceStats.LogLoss(p, o), Is.EqualTo(Math.Log(2)).Within(1e-12));
    }

    [Test]
    public void Correlation_PerfectAndInverse()
    {
        var a = new double[] { 1, 2, 3, 4, 5 };
        var b = new double[] { 2, 4, 6, 8, 10 };
        var c = new double[] { 5, 4, 3, 2, 1 };
        Assert.That(EvidenceStats.Correlation(a, b), Is.EqualTo(1).Within(1e-12));
        Assert.That(EvidenceStats.Correlation(a, c), Is.EqualTo(-1).Within(1e-12));
    }

    [Test]
    public void Scorecard_CountsHitsIgnoresAbstentions_AndReportsSignificanceHonestly()
    {
        var card = new DirectionalScorecard();
        card.Record(Direction.Up, 0.01);
        card.Record(Direction.Up, -0.01);
        card.Record(Direction.Down, -0.02);
        card.Record(Direction.Flat, 0.05);
        Assert.That(card.Calls, Is.EqualTo(3));
        Assert.That(card.Hits, Is.EqualTo(2));
        Assert.That(card.Abstentions, Is.EqualTo(1));
        Assert.That(card.Coverage, Is.EqualTo(0.75).Within(1e-12));
        Assert.That(card.MeanEdge, Is.EqualTo((0.01 - 0.01 + 0.02) / 3).Within(1e-12));
        Assert.That(card.IsSignificant(), Is.False, "2 of 3 is not evidence of anything");
    }
}
