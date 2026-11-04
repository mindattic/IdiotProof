using IdiotProof.Core.Evidence;
using IdiotProof.Models;
using IdiotProof.Operations.Evidence;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>IP-T423: a walk-forward reports which data tiers it ran on and refuses to mix SIP with IEX.</summary>
public class EvidenceFeedProvenanceTests
{
    private static List<Candle> Stamped(IEnumerable<Candle> bars, string source) =>
        bars.Select(c => new Candle
        {
            Symbol = c.Symbol, StartUtc = c.StartUtc, EndUtc = c.EndUtc,
            Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume, Note = source,
        }).ToList();

    [Test]
    public async Task MixedSipAndIex_IsRefused_UnlessAllowed()
    {
        var days = Weekdays(8);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany((d, i) => Stamped(Session("SAW", d, Sawtooth), i < 4 ? "Alpaca:sip" : "Alpaca:iex")).ToList();
        var args = new EvidenceWalkForwardArgs { Symbols = ["saw"], From = days[3], To = days[7], Window = 10, Horizon = 2, Cost = 0 };

        var refused = await new EvidenceWalkForwardOperation().ExecuteAsync(args, Context(store));
        var allowed = await new EvidenceWalkForwardOperation().ExecuteAsync(args with { AllowMixedFeeds = true }, Context(store));

        Assert.That(refused.IsOk, Is.False);
        Assert.That(refused.Message, Does.Contain("Alpaca:sip").And.Contain("Alpaca:iex").And.Contain("--allow-mixed-feeds"));
        Assert.That(allowed.IsOk, Is.True, allowed.Message);
    }

    [Test]
    public async Task OneTier_ReportsItsBarCounts()
    {
        var days = Weekdays(6);
        var store = new FakeBarStore();
        store.Minute["SAW"] = days.SelectMany(d => Stamped(Session("SAW", d, Sawtooth), "Alpaca:sip")).ToList();

        var report = await new EvidenceWalkForward(store).RunAsync(
            new Dictionary<string, SortedSet<DateOnly>> { ["SAW"] = new(days.Skip(3)) },
            new WalkForwardSettings { Window = 10, Horizon = 2, MinTrainDays = 2 }, CancellationToken.None);

        Assert.That(report.Feeds!.IsMixed, Is.False);
        Assert.That(report.Feeds.Sip, Is.EqualTo(6 * 390));
        Assert.That(report.Feeds.Unknown, Is.Zero);
    }

    [Test]
    public void Tiers_AreReadFromTheStampedSource()
    {
        Assert.That(BarProvenance.TierOf("Alpaca:sip"), Is.EqualTo("sip"));
        Assert.That(BarProvenance.TierOf("Alpaca:IEX"), Is.EqualTo("iex"));
        Assert.That(BarProvenance.TierOf("Alpaca"), Is.EqualTo("unknown"), "rows cached before IP-T423 have no tier");
        Assert.That(BarProvenance.TierOf("Mock-daily"), Is.EqualTo("mock"));
        Assert.That(BarProvenance.SourceOf(new Candle(), "Alpaca"), Is.EqualTo("Alpaca"));
        Assert.That(BarProvenance.SourceOf(new Candle { Note = "Alpaca:iex" }, "Alpaca"), Is.EqualTo("Alpaca:iex"));
    }
}
