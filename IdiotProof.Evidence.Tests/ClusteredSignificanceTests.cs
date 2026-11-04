namespace IdiotProof.Evidence.Tests;

public class ClusteredSignificanceTests
{
    private static readonly DateOnly D0 = new(2026, 8, 3);

    [Test]
    public void OnePerfectlyCorrelatedDay_IsNotSignificant_EvenThoughTheNaiveBinomialSaysItIs()
    {
        var tallies = new[] { new DayTally(D0, 37, 37) };
        Assert.That(EvidenceStats.BinomialUpperTail(37, 37), Is.LessThan(1e-10), "naive test is fooled");
        var r = ClusteredSignificance.Evaluate(tallies);
        Assert.That(r.Days, Is.EqualTo(1));
        Assert.That(r.IsSignificant(), Is.False);
        Assert.That(r.BootstrapPValue, Is.EqualTo(1));
        Assert.That(r.TPValue, Is.EqualTo(1));
    }

    [Test]
    public void AFewBigWinningDays_CarryAPooledRate_ButTheDayLevelTestsRefuseIt()
    {
        // 11 days where every call won, 9 where every call lost: 550/1000 pooled.
        var tallies = Enumerable.Range(0, 20)
            .Select(i => new DayTally(D0.AddDays(i), 50, i < 11 ? 50 : 0)).ToArray();
        Assert.That(EvidenceStats.BinomialUpperTail(550, 1000), Is.LessThan(0.01), "naive test is fooled");
        var r = ClusteredSignificance.Evaluate(tallies);
        Assert.That(r.PooledHitRate, Is.EqualTo(0.55).Within(1e-12));
        Assert.That(r.BootstrapPValue, Is.GreaterThan(0.2));
        Assert.That(r.TPValue, Is.GreaterThan(0.2));
        Assert.That(r.IsSignificant(), Is.False);
    }

    [Test]
    public void AConsistentEdgeAcrossManyDays_IsSignificant()
    {
        var rng = new Random(5);
        var tallies = Enumerable.Range(0, 60).Select(i =>
        {
            int calls = 40, hits = 0;
            for (int c = 0; c < calls; c++) if (rng.NextDouble() < 0.6) hits++;
            return new DayTally(D0.AddDays(i), calls, hits);
        }).ToArray();
        var r = ClusteredSignificance.Evaluate(tallies);
        Assert.That(r.IsSignificant(), Is.True, $"boot p={r.BootstrapPValue}, t p={r.TPValue}");
        Assert.That(r.BootstrapLow, Is.GreaterThan(0.5));
    }

    [Test]
    public void ACoinAcrossManyDays_IsNotSignificant()
    {
        var rng = new Random(8);
        var tallies = Enumerable.Range(0, 60).Select(i =>
        {
            int hits = 0;
            for (int c = 0; c < 40; c++) if (rng.NextDouble() < 0.5) hits++;
            return new DayTally(D0.AddDays(i), 40, hits);
        }).ToArray();
        Assert.That(ClusteredSignificance.Evaluate(tallies).IsSignificant(), Is.False);
    }

    [Test]
    public void NullRate_CanBeTheFiftyOnePercentBar()
    {
        var tallies = Enumerable.Range(0, 30).Select(i => new DayTally(D0.AddDays(i), 100, i % 2 == 0 ? 52 : 51)).ToArray();
        var vsCoin = ClusteredSignificance.Evaluate(tallies, nullRate: 0.5);
        var vsBar = ClusteredSignificance.Evaluate(tallies, nullRate: 0.515);
        Assert.That(vsCoin.TPValue, Is.LessThan(vsBar.TPValue));
    }

    [Test]
    public void Bootstrap_IsDeterministicForASeed()
    {
        var tallies = Enumerable.Range(0, 10).Select(i => new DayTally(D0.AddDays(i), 10, 4 + i % 4)).ToArray();
        Assert.That(ClusteredSignificance.Evaluate(tallies, seed: 3).BootstrapPValue,
            Is.EqualTo(ClusteredSignificance.Evaluate(tallies, seed: 3).BootstrapPValue));
    }

    [Test]
    public void StudentT_MatchesTables()
    {
        Assert.That(ClusteredSignificance.StudentTUpperTail(1.812461, 10), Is.EqualTo(0.05).Within(1e-5));
        Assert.That(ClusteredSignificance.StudentTUpperTail(2.0, 10), Is.EqualTo(0.036694).Within(1e-5));
        Assert.That(ClusteredSignificance.StudentTUpperTail(0, 7), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(ClusteredSignificance.StudentTUpperTail(-2.0, 10), Is.EqualTo(1 - 0.036694).Within(1e-5));
    }
}
