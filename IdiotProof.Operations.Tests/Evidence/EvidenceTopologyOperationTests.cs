using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

public class EvidenceTopologyOperationTests
{
    [Test]
    public async Task Topology_OnRandomWalkDays_ReportsNoSignal()
    {
        var days = Weekdays(8);
        var store = new FakeBarStore();
        var rng = new Random(12);
        var bars = new List<IdiotProof.Models.Candle>();
        foreach (var d in days)
        {
            double p = 3;
            var closes = new double[390];
            for (int i = 0; i < closes.Length; i++) { p *= 1 + 0.003 * (rng.NextDouble() * 2 - 1); closes[i] = p; }
            bars.AddRange(Session("RWLK", d, i => closes[i]));
        }
        store.Minute["RWLK"] = bars;

        var result = await new EvidenceTopologyOperation().ExecuteAsync(new EvidenceTopologyArgs
        {
            Symbols = ["RWLK"], From = days[0], To = days[^1], Window = 30, Horizon = 10, MinSamples = 10,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("none is a signal"));
        Assert.That(result.Human, Does.Contain("h1-total").And.Contain("h0-total"));
    }

    [Test]
    public async Task Topology_RejectsAWindowTooShortForTheEmbedding()
    {
        var result = await new EvidenceTopologyOperation().ExecuteAsync(new EvidenceTopologyArgs
        {
            Symbols = ["X"], Window = 10, Dimension = 4, Delay = 3,
        }, Context(new FakeBarStore()));
        Assert.That(result.IsOk, Is.False);
    }

    [Test]
    public async Task Topology_NeedsTwoTradingDays()
    {
        var days = Weekdays(1);
        var store = new FakeBarStore();
        store.Minute["ONE"] = Session("ONE", days[0], i => 3 + 0.01 * Math.Sin(i)).ToList();
        var result = await new EvidenceTopologyOperation().ExecuteAsync(new EvidenceTopologyArgs
        {
            Symbols = ["ONE"], From = days[0], To = days[0],
        }, Context(store));
        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Does.Contain("at least 2 trading days"));
    }
}
