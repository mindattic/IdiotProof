using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

public class EvidenceKalmanOperationTests
{
    [Test]
    public async Task ScoresSlopeAndSurprise_OnHeldOutSessions()
    {
        var days = Weekdays(6);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth)).ToList();

        var result = await new EvidenceKalmanOperation().ExecuteAsync(new EvidenceKalmanArgs
        {
            Symbols = ["SAW"], From = days[0], To = days[^1], Horizon = 2,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("slope").And.Contain("innovation-z"));
        Assert.That(result.Human, Does.Not.Contain("pair-residual-z"), "no leader, no pair feature");
    }

    [Test]
    public async Task APlantedRepeatingPattern_IsSignificant_RandomWalkIsNot()
    {
        var days = Weekdays(20);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Session("SAW", d, Sawtooth)).ToList();
        store.Minute["RW"] = days.SelectMany((d, k) => Session("RW", d, RandomWalk(100 + k))).ToList();

        var result = await new EvidenceKalmanOperation().ExecuteAsync(new EvidenceKalmanArgs
        {
            Symbols = ["SAW", "RW"], From = days[0], To = days[^1], Horizon = 2,
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.That(lines.Any(l => l.StartsWith("SAW") && IsSignificant(l)), Is.True, result.Human);
        Assert.That(lines.Any(l => l.StartsWith("RW") && IsSignificant(l)), Is.False, result.Human);
    }

    [Test]
    public async Task WithALeader_AddsThePairResidualFeature()
    {
        var days = Weekdays(4);
        var store = new FakeBarStore();
        store.Minute["FOL"] = days.SelectMany((d, k) => Session("FOL", d, RandomWalk(10 + k))).ToList();
        store.Minute["LEAD"] = days.SelectMany((d, k) => Session("LEAD", d, RandomWalk(20 + k))).ToList();

        var result = await new EvidenceKalmanOperation().ExecuteAsync(new EvidenceKalmanArgs
        {
            Symbols = ["FOL"], Leader = "LEAD", From = days[0], To = days[^1],
        }, Context(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("pair-residual-z"));
    }

    [Test]
    public async Task BidAskBounce_LooksSignificantWithCloseEntry_ButNotWithNextOpenEntry()
    {
        var days = Weekdays(20);
        var store = new FakeBarStore();
        var rng = new Random(42);
        var bars = new List<IdiotProof.Models.Candle>();
        foreach (var d in days)
        {
            var open = new DateTime(d.Year, d.Month, d.Day, 14, 30, 0, DateTimeKind.Utc);
            for (int i = 0; i < 390; i++)
            {
                // A $2.00 mid that never moves; prints land on the bid or the ask at random.
                decimal o = rng.Next(2) == 0 ? 1.99m : 2.01m, c = rng.Next(2) == 0 ? 1.99m : 2.01m;
                bars.Add(new IdiotProof.Models.Candle
                {
                    Symbol = "BNC", StartUtc = open.AddMinutes(i), EndUtc = open.AddMinutes(i + 1),
                    Open = o, Close = c, High = 2.01m, Low = 1.99m, Volume = 1000,
                });
            }
        }
        store.Minute["BNC"] = bars;

        var close = await new EvidenceKalmanOperation().ExecuteAsync(new EvidenceKalmanArgs
        {
            Symbols = ["BNC"], From = days[0], To = days[^1], Horizon = 3, Entry = KalmanEntry.Close,
        }, Context(store));
        var next = await new EvidenceKalmanOperation().ExecuteAsync(new EvidenceKalmanArgs
        {
            Symbols = ["BNC"], From = days[0], To = days[^1], Horizon = 3,
        }, Context(store));

        Assert.That(close.Human!.Split('\n').Any(l => l.Contains("innovation-z") && IsSignificant(l)), Is.True, close.Human);
        Assert.That(next.Human!.Split('\n').Any(l => l.Contains("innovation-z") && IsSignificant(l)), Is.False, next.Human);
        Assert.That(next.Human, Does.Contain("entry next open"));
    }

    [Test]
    public async Task TooFewSessions_IsReportedNotThrown()
    {
        var days = Weekdays(1);
        var store = new FakeBarStore();
        store.Minute["ONE"] = Session("ONE", days[0], Sawtooth).ToList();
        var result = await new EvidenceKalmanOperation().ExecuteAsync(new EvidenceKalmanArgs { Symbols = ["ONE"], From = days[0], To = days[0] }, Context(store));
        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Does.Contain("need at least 2 sessions"));
    }

    /// <summary>A table row whose verdict column says significant (q below alpha), not "not significant".</summary>
    private static bool IsSignificant(string line) => line.TrimEnd().EndsWith(" significant") && !line.Contains("not significant");
}
