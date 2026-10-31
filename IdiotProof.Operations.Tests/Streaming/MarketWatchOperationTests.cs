using System.Text.Json;
using IdiotProof.Operations.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Streaming;

/// <summary>market watch (IP-T326): one fake websocket per asset class, merged into one ET tape.</summary>
public class MarketWatchOperationTests
{
    private const string Stocks = AlpacaStreamSession.DataStreamBase + "v2/sip";
    private const string Crypto = AlpacaStreamSession.DataStreamBase + "v1beta3/crypto/us";
    private const string Options = AlpacaStreamSession.DataStreamBase + "v1beta1/opra";
    private const string News = AlpacaStreamSession.DataStreamBase + "v1beta1/news";

    private const string Hello = """[{"T":"success","msg":"connected"}]""";
    private const string Authed = """[{"T":"success","msg":"authenticated"}]""";

    [Test]
    public async Task Stocks_SubscribeTheChannels_AndRenderTradesQuotesStatusesAndLuld()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Stocks] =
        [
            Hello, Authed,
            """[{"T":"subscription","trades":["AAPL"],"quotes":["AAPL"],"statuses":["AAPL"],"lulds":["AAPL"]}]""",
            """[{"T":"t","S":"AAPL","p":187.2,"s":100,"x":"V","t":"2026-10-30T13:31:07.5Z"},{"T":"q","S":"AAPL","bp":187.19,"bs":3,"ap":187.21,"as":2,"t":"2026-10-30T13:31:08Z"}]""",
            """[{"T":"s","S":"AAPL","sc":"H","sm":"Trading Halt","rc":"LUDP","rm":"Volatility Trading Pause","t":"2026-10-30T13:32:00Z"}]""",
            """[{"T":"l","S":"AAPL","u":190.5,"d":180.25,"i":"B","t":"2026-10-30T13:32:01Z"}]""",
        ];

        var result = await new MarketWatchOperation().ExecuteAsync(
            new MarketWatchArgs { Symbols = ["aapl"], Channels = ["trades,quotes,statuses,lulds"], Feed = "sip", Seconds = 1 }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var sent = kit.Sockets.SentText(Stocks).ToList();
        Assert.That(sent[0], Is.EqualTo("""{"action":"auth","key":"data-key","secret":"secret"}"""));
        Assert.That(sent[1], Is.EqualTo("""{"action":"subscribe","trades":["AAPL"],"quotes":["AAPL"],"statuses":["AAPL"],"lulds":["AAPL"]}"""));
        var human = result.Human!;
        Assert.That(human, Does.Contain("subscribed trades AAPL; quotes AAPL; statuses AAPL; lulds AAPL"));
        Assert.That(human, Does.Contain("9:31:07 AM ET  AAPL                 trade    $187.20 x 100 V"));
        Assert.That(human, Does.Contain("quote    bid $187.19 x 3  ask $187.21 x 2"));
        Assert.That(human, Does.Contain("status   H Trading Halt (Volatility Trading Pause)"));
        Assert.That(human, Does.Contain("LULD     up $190.50 down $180.25 indicator B"));
    }

    [Test]
    public async Task MixedSymbols_OpenOneStreamPerClass_OptionsOverMsgPack()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Crypto] = [Hello, Authed, """[{"T":"b","S":"BTC/USD","o":65000,"h":65100,"l":64900,"c":65050.5,"v":1.25,"t":"2026-10-30T13:31:00Z"}]"""];
        kit.Sockets.BinaryScripts[Options] =
        [
            MsgPack.Encode(new object[] { new Dictionary<string, object?> { ["T"] = "success", ["msg"] = "authenticated" } }),
            MsgPack.Encode(new object[] { new Dictionary<string, object?> { ["T"] = "q", ["S"] = "AAPL261120C00200000", ["bp"] = 4.1, ["bs"] = 10, ["ap"] = 4.3, ["as"] = 12 } }),
        ];
        kit.Sockets.Scripts[News] = [Hello, Authed, """[{"T":"n","headline":"Apple beats","symbols":["AAPL"],"source":"benzinga","created_at":"2026-10-30T13:30:00Z"}]"""];

        var result = await new MarketWatchOperation().ExecuteAsync(
            new MarketWatchArgs { Symbols = ["BTC/USD", "AAPL261120C00200000"], Channels = ["bars", "quotes"], News = true, Seconds = 1 }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(kit.Sockets.SentText(Crypto).Last(), Is.EqualTo("""{"action":"subscribe","bars":["BTC/USD"],"quotes":["BTC/USD"]}"""));
        var optionSub = kit.Sockets.Sent.Last(s => s.Uri.ToString() == Options);
        Assert.That(optionSub.Binary, Is.True, "the options feed is msgpack only");
        Assert.That(MsgPack.ToJson(optionSub.Data), Is.EqualTo("""{"action":"subscribe","quotes":["AAPL261120C00200000"]}"""));
        Assert.That(kit.Sockets.Connections.Single(c => c.Uri.ToString() == Options).Headers!["Content-Type"], Is.EqualTo("application/msgpack"));
        Assert.That(kit.Sockets.SentText(News).Last(), Is.EqualTo("""{"action":"subscribe","news":["*"]}"""));
        Assert.That(kit.Sockets.Connections.Select(c => c.Uri.ToString()), Has.None.EqualTo(Stocks), "no stock symbols, no stock socket");

        var human = result.Human!;
        Assert.That(human, Does.Contain("BTC/USD").And.Contain("bar      O $65,000.00 H $65,100.00 L $64,900.00 C $65,050.50 V 1.25"));
        Assert.That(human, Does.Contain("AAPL261120C00200000").And.Contain("bid $4.10 x 10  ask $4.30 x 12"));
        Assert.That(human, Does.Contain("Apple beats [AAPL] · benzinga"));
    }

    [Test]
    public async Task AConnectionLimit_IsExplained_AndShowsInStreamStatusWhileOpen()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Stocks] = [Hello, """[{"T":"error","code":406,"msg":"connection limit exceeded"}]"""];

        var result = await new MarketWatchOperation().ExecuteAsync(new MarketWatchArgs { Symbols = ["AAPL"], Feed = "sip", Seconds = 2 }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("406 connection limit exceeded").And.Contain("--feed iex"));
        Assert.That(kit.Services.GetRequiredService<StreamRegistry>().Health(), Is.Empty, "a finished watch leaves the registry");
    }

    [TestCase(new string[0], false, "Name at least one symbol")]
    [TestCase(new[] { "AAPL" }, false, null)]
    public async Task Usage(string[] symbols, bool news, string? error)
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Stocks] = [Hello, Authed];
        var result = await new MarketWatchOperation().ExecuteAsync(new MarketWatchArgs { Symbols = symbols, News = news, Feed = "sip", Seconds = 1 }, kit.Context());
        if (error is null) Assert.That(result.IsOk, Is.True, result.Message);
        else Assert.That(result.Message, Does.Contain(error));

        var bad = await new MarketWatchOperation().ExecuteAsync(new MarketWatchArgs { Symbols = ["AAPL"], Channels = ["ticks"] }, kit.Context());
        Assert.That(bad.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public void MsgPack_RoundTripsWhatAlpacaSends_IncludingTimestamps()
    {
        // fixmap{ "t": ext(-1, 8 bytes: 0 nanos, seconds 1793367067) }
        var seconds = 1793367067UL;
        var bytes = new byte[] { 0x81, 0xa1, (byte)'t', 0xd7, 0xff }.Concat(BitConverter.GetBytes(seconds).Reverse()).ToArray();
        using var doc = JsonDocument.Parse(MsgPack.ToJson(bytes));
        Assert.That(doc.RootElement.GetProperty("t").GetString(), Does.StartWith(DateTime.UnixEpoch.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss")));

        var encoded = MsgPack.Encode(new Dictionary<string, object?> { ["a"] = 1, ["b"] = -5, ["c"] = 1.5, ["d"] = true, ["e"] = null, ["f"] = new[] { "x", "y" }, ["g"] = 300 });
        Assert.That(MsgPack.ToJson(encoded), Is.EqualTo("""{"a":1,"b":-5,"c":1.5,"d":true,"e":null,"f":["x","y"],"g":300}"""));
    }
}
