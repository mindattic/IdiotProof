using System.Text.Json;
using IdiotProof.DataFeeds;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: the SIP socket's <c>statuses</c>/<c>lulds</c> frames parse into halt facts, and owners share one subscription set.</summary>
[TestFixture]
public sealed class AlpacaStreamStatusTests
{
    private const string Frames = """
        [
          {"T":"s","S":"abcd","sc":"H","sm":"Trading Halt","rc":"T1","rm":"Halt News Pending","t":"2026-11-06T13:31:26.034Z","z":"C"},
          {"T":"s","S":"ABCD","sc":"T","sm":"Trading Resumption","rc":"","rm":"","t":"2026-11-06T13:41:00Z","z":"C"},
          {"T":"s","S":"EFGH","sc":"","sm":"Volatility Trading Pause","rc":"","rm":"","t":"2026-11-06T13:42:00Z","z":"C"},
          {"T":"s","S":"IJKL","sc":"5","sm":"Price Indication","rc":"","rm":"","t":"2026-11-06T13:43:00Z","z":"C"},
          {"T":"s","sc":"H","sm":"no symbol"},
          {"T":"l","S":"ABCD","u":3.52,"d":2.88,"i":"B","t":"2026-11-06T13:44:00Z","z":"C"}
        ]
        """;

    private static List<JsonElement> Elements() => JsonDocument.Parse(Frames).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

    [Test]
    public void StatusFrames_ParseIntoHaltFacts()
    {
        var els = Elements();
        var halt = AlpacaStreamingClient.ParseStatus(els[0])!;
        var resume = AlpacaStreamingClient.ParseStatus(els[1])!;
        var pause = AlpacaStreamingClient.ParseStatus(els[2])!;
        var indication = AlpacaStreamingClient.ParseStatus(els[3])!;
        Assert.Multiple(() =>
        {
            Assert.That(halt.Symbol, Is.EqualTo("ABCD"));
            Assert.That(halt.Halted, Is.True);
            Assert.That(halt.TimestampUtc, Is.EqualTo(new DateTime(2026, 11, 6, 13, 31, 26, 34, DateTimeKind.Utc)));
            Assert.That(halt.ReasonMessage, Is.EqualTo("Halt News Pending"));
            Assert.That(resume.Halted, Is.False);
            Assert.That(pause.Halted, Is.True, "a pause reads as halted from the message text");
            Assert.That(indication.Halted, Is.Null, "neither a halt nor a resumption");
            Assert.That(AlpacaStreamingClient.ParseStatus(els[4]), Is.Null);
        });
    }

    [Test]
    public void LuldFrames_ParseIntoBands()
    {
        var band = AlpacaStreamingClient.ParseLuld(Elements()[5])!;
        Assert.That(band.Symbol, Is.EqualTo("ABCD"));
        Assert.That(band.LimitUp, Is.EqualTo(3.52m));
        Assert.That(band.LimitDown, Is.EqualTo(2.88m));
        Assert.That(band.Indicator, Is.EqualTo("B"));
    }

    [Test]
    public async Task Owners_ShareOneSubscriptionSet_AsAUnion()
    {
        await using var client = new AlpacaStreamingClient("key", "secret", "sip");
        await client.SetSymbolsAsync(["aapl", "MSFT"]);
        await client.SetSymbolsAsync("formation", ["msft", "ABCD", "EFGH"]);
        Assert.That(client.SubscribedCount, Is.EqualTo(4));
        Assert.That(client.SymbolsOf(AlpacaStreamingClient.DefaultOwner), Is.EquivalentTo(new[] { "AAPL", "MSFT" }));
        Assert.That(client.SymbolsOf("formation"), Has.Count.EqualTo(3));

        await client.SetSymbolsAsync("formation", []);
        Assert.That(client.SubscribedCount, Is.EqualTo(2), "dropping the formation owner keeps the Monitor's symbols");
        Assert.That(client.SymbolsOf("formation"), Is.Empty);

        await client.SetSymbolsAsync([]);
        Assert.That(client.SubscribedCount, Is.Zero);
    }
}
