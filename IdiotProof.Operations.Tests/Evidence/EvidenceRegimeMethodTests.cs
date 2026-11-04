using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>evidence regime --method bocpd|hmm (IP-T402).</summary>
public class EvidenceRegimeMethodTests
{
    /// <summary>Calm 1-minute returns, then a burst of volatility from <paramref name="switchAt"/>.</summary>
    private static double[] CalmThenWild(int n, int switchAt, int seed)
    {
        var rng = new Random(seed);
        var p = new double[n];
        double price = 3;
        for (int i = 0; i < n; i++)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double z = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            price *= Math.Exp((i < switchAt ? 0.0005 : 0.006) * z);
            p[i] = price;
        }
        return p;
    }

    [Test]
    public async Task Bocpd_FlagsTheVolatilityBurst_AtItsStart()
    {
        var days = Weekdays(1);
        var store = new FakeBarStore();
        var path = CalmThenWild(390, 300, 3);
        store.Minute["BURST"] = Session("BURST", days[0], i => path[i]).ToList();

        var result = await new EvidenceRegimeOperation().ExecuteAsync(new EvidenceRegimeArgs
        {
            Symbols = ["BURST"], From = days[0], To = days[0], Method = RegimeMethod.Bocpd,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        var line = result.Human!.Split('\n').Single(l => l.StartsWith("BURST"));
        // 300 minutes after the 9:30 open = 14:30 ET; the estimated start may sit a bar or two off.
        Assert.That(line, Does.Match($@"{days[0]:yyyy-MM-dd} 14:(2[89]|3[0-2])"), line);
    }

    [Test]
    public async Task Hmm_TrainsOnEarlierSessions_AndReportsTheVolatileStateNow()
    {
        var days = Weekdays(3);
        var store = new FakeBarStore();
        var earlier1 = CalmThenWild(390, 200, 4);
        var earlier2 = CalmThenWild(390, 150, 5);
        var today = CalmThenWild(390, 300, 6);
        store.Minute["HMM"] = Session("HMM", days[0], i => earlier1[i])
            .Concat(Session("HMM", days[1], i => earlier2[i]))
            .Concat(Session("HMM", days[2], i => today[i])).ToList();

        var result = await new EvidenceRegimeOperation().ExecuteAsync(new EvidenceRegimeArgs
        {
            Symbols = ["HMM"], From = days[0], To = days[2], Method = RegimeMethod.Hmm,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        var line = result.Human!.Split('\n').Single(l => l.StartsWith("HMM"));
        Assert.That(line, Does.Contain("volatile"), line);
        // 780 bars in the two earlier sessions give 779 returns; today's bars are never trained on.
        Assert.That(line, Does.Contain(" 779 "), "trained on the two earlier sessions' returns: " + line);
    }

    [Test]
    public async Task RejectsABadThresholdOrStateCount()
    {
        var store = new FakeBarStore();
        var bad = await new EvidenceRegimeOperation().ExecuteAsync(new EvidenceRegimeArgs { Symbols = ["X"], Method = RegimeMethod.Bocpd, Threshold = 1.5 }, Context(store));
        Assert.That(bad.IsOk, Is.False);
        var badStates = await new EvidenceRegimeOperation().ExecuteAsync(new EvidenceRegimeArgs { Symbols = ["X"], Method = RegimeMethod.Hmm, States = 7 }, Context(store));
        Assert.That(badStates.IsOk, Is.False);
    }
}
