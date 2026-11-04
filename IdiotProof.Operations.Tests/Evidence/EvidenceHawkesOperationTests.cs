using IdiotProof.Evidence.Microstructure;
using IdiotProof.Operations.Evidence;
using Microsoft.Extensions.DependencyInjection;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>evidence hawkes (IP-T406).</summary>
public class EvidenceHawkesOperationTests
{
    private sealed class FakeTicks(Func<DateOnly, IReadOnlyList<TradePrint>> trades) : ITickStore
    {
        public List<DateOnly> TradeDays { get; } = new();

        public Task<IReadOnlyList<TradePrint>> GetTradesAsync(string symbol, DateOnly dayEt, CancellationToken ct)
        {
            TradeDays.Add(dayEt);
            return Task.FromResult(trades(dayEt));
        }

        public Task<IReadOnlyList<QuoteTick>> GetQuotesAsync(string symbol, DateOnly dayEt, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<QuoteTick>>([]);
    }

    private static OperationContext WithTicks(FakeBarStore store, ITickStore ticks)
    {
        var ctx = Context(store);
        var services = new ServiceCollection();
        services.AddSingleton<IdiotProof.Core.Evidence.IHistoricalBarStore>(store);
        services.AddSingleton(ticks);
        return new OperationContext { Services = services.BuildServiceProvider(), Clock = ctx.Clock };
    }

    /// <summary>A print every 2 s, plus clustered bursts at minutes 40-44 and 80-84.</summary>
    private static IReadOnlyList<TradePrint> SteadyWithBursts(DateOnly day)
    {
        var open = new DateTime(day.Year, day.Month, day.Day, 14, 30, 0, DateTimeKind.Utc);
        var rng = new Random(day.DayNumber);
        var list = new List<TradePrint>();
        for (int s = 0; s < 120 * 60; s += 2) list.Add(new TradePrint(open.AddSeconds(s), 3.0, 100));
        foreach (int startMinute in new[] { 40, 80 })
            for (int s = startMinute * 60; s < (startMinute + 5) * 60; s += 15)
                for (int k = 0; k < 25; k++) list.Add(new TradePrint(open.AddSeconds(s + rng.NextDouble() * 2), 3.0, 100));
        return list.OrderBy(t => t.TimeUtc).ToList();
    }

    [Test]
    public async Task Hawkes_FitsEverySession_WarmsUpOnTheFirst_AndScoresTheDetector()
    {
        var days = Weekdays(4);
        var store = new FakeBarStore();
        store.Minute["BRST"] = days.SelectMany(d => Session("BRST", d, Sawtooth, minutes: 120)).ToList();
        var ticks = new FakeTicks(SteadyWithBursts);

        var result = await new EvidenceHawkesOperation().ExecuteAsync(new EvidenceHawkesArgs
        {
            Symbol = "BRST", From = days[0], To = days[3], Window = 5,
        }, WithTicks(store, ticks));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(ticks.TradeDays, Is.EqualTo(days));
        Assert.That(result.Human, Does.Contain("4 sessions (1 warm-up, 2 train, 1 test)"));
        Assert.That(result.Human, Does.Contain("warm-up").And.Contain("intensity-ratio").And.Contain("branching").And.Contain("ignition"));
        Assert.That(result.Human, Does.Contain("significant"), "every bucket carries a verdict");
        Assert.That(result.Human, Does.Not.Contain("ignition bars: 0"), "the planted bursts must register against the prior session's baseline");
    }

    [Test]
    public async Task Hawkes_NeedsThreeSessions()
    {
        var days = Weekdays(2);
        var store = new FakeBarStore();
        store.Minute["BRST"] = days.SelectMany(d => Session("BRST", d, Sawtooth, minutes: 60)).ToList();
        var result = await new EvidenceHawkesOperation().ExecuteAsync(new EvidenceHawkesArgs { Symbol = "BRST", From = days[0], To = days[1] },
            WithTicks(store, new FakeTicks(SteadyWithBursts)));
        Assert.That(result.Human, Does.Contain("need at least 3 sessions"));
    }

    [Test]
    public void TwoSidedP_MatchesExactBinomial()
    {
        // 8 of 10 at p0 = 0.5: upper tail 56/1024, doubled.
        Assert.That(EvidenceHawkesOperation.TwoSidedP(8, 10, 0.5), Is.EqualTo(112.0 / 1024).Within(1e-12));
        Assert.That(EvidenceHawkesOperation.TwoSidedP(5, 10, 0.5), Is.EqualTo(1.0));
        Assert.That(EvidenceHawkesOperation.TwoSidedP(0, 0, 0.5), Is.EqualTo(1.0));
    }
}
