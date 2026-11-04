using IdiotProof.Models;
using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

public class EvidencePoolsOperationTests
{
    /// <summary>Flat at 3.05, a dip piercing 3.00 that closes back above, then back to 3.04.</summary>
    private static Func<int, double> RaidDay() => i => (i % 25) == 12 ? 3.02 : (i % 25) < 12 ? 3.05 : 3.04;

    private static FakeBarStore StoreWithRaids()
    {
        var store = new FakeBarStore();
        var bars = new List<Candle>();
        foreach (var day in Weekdays(2))
            bars.AddRange(Session("SNDL", day, RaidDay(), minutes: 250).Select((c, i) =>
                i % 25 == 12 ? new Candle
                {
                    Symbol = c.Symbol, StartUtc = c.StartUtc, EndUtc = c.EndUtc,
                    Open = 3.05m, High = 3.05m, Low = 2.97m, Close = 3.02m, Volume = c.Volume,
                } : c));
        store.Minute["SNDL"] = bars;
        return store;
    }

    [Test]
    public async Task CountsRaidsPerPoolKind_WithRates()
    {
        var days = Weekdays(2);
        var result = await new EvidencePoolsOperation().ExecuteAsync(
            new EvidencePoolsArgs { Symbols = ["SNDL"], From = days[0], To = days[1], MinSamples = 5 }, Context(StoreWithRaids()));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("RoundNumber"));
        Assert.That(result.Human, Does.Contain("below"));
        Assert.That(result.Human, Does.Contain("raid+rev"));
    }

    [Test]
    public async Task RejectsBadArguments()
    {
        var ctx = Context(new FakeBarStore());
        Assert.That((await new EvidencePoolsOperation().ExecuteAsync(new EvidencePoolsArgs(), ctx)).Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That((await new EvidencePoolsOperation().ExecuteAsync(new EvidencePoolsArgs { Symbols = ["X"], Horizon = 0 }, ctx)).Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task NoBars_SaysSo()
    {
        var result = await new EvidencePoolsOperation().ExecuteAsync(new EvidencePoolsArgs { Symbols = ["NONE"] }, Context(new FakeBarStore()));
        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("No pool approaches"));
    }
}
