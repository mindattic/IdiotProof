using System.Net;
using System.Text;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Crypto;
using IdiotProof.Operations.Market;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Crypto;

/// <summary>An Alpaca stand-in: answers each request by the first route whose path prefix matches, and records them.</summary>
internal sealed class CryptoAlpacaHandler : HttpMessageHandler
{
    private readonly List<(string Method, string Prefix, HttpStatusCode Status, string Body)> routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string?> Bodies { get; } = [];

    public CryptoAlpacaHandler On(string pathPrefix, string body, HttpStatusCode status = HttpStatusCode.OK, string method = "GET")
    {
        routes.Add((method, pathPrefix, status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        var path = request.RequestUri!.AbsolutePath;
        var route = routes.FirstOrDefault(r => r.Method == request.Method.Method && path.StartsWith(r.Prefix, StringComparison.Ordinal));
        if (route.Prefix is null) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"message":"no route in test"}""") };
        return new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
    }
}

/// <summary>An <see cref="AlpacaApiFactory"/> that hands back clients over a scripted handler, never the network.</summary>
internal sealed class CryptoAlpacaFactory(HttpMessageHandler handler) : AlpacaApiFactory(null!, null!, null!)
{
    public List<BrokerMode> Modes { get; } = [];

    public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default)
    {
        Modes.Add(mode);
        return Task.FromResult(new AlpacaApi("k", "s", mode == BrokerMode.Paper, handler));
    }

    public override Task<AlpacaApi> ForDataAsync(Guid? ownerUserId = null, CancellationToken ct = default) =>
        Task.FromResult(new AlpacaApi("k", "s", true, handler));

    public static OperationContext Context(HttpMessageHandler handler, Action<IServiceCollection>? more = null, string? confirm = null)
    {
        var services = new ServiceCollection().AddSingleton<AlpacaApiFactory>(new CryptoAlpacaFactory(handler));
        more?.Invoke(services);
        return new OperationContext
        {
            OwnerId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            Services = services.BuildServiceProvider(),
            Clock = new Fixtures.FixedClock(new DateTime(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc)), // 10:00 AM ET
            ConfirmationCode = confirm,
        };
    }
}

/// <summary>The crypto market data reads (IP-T316) against a scripted v1beta3 crypto feed.</summary>
public class CryptoMarketOperationTests
{
    [TestCase("btc/usd", "BTC/USD")]
    [TestCase("ETH-USD", "ETH/USD")]
    [TestCase("SOLUSDT", "SOL/USDT")]
    [TestCase("AAPL", null)]
    [TestCase("A/B/C", null)]
    public void NormalizesPairs(string input, string? expected) =>
        Assert.That(CryptoData.Normalize(input), Is.EqualTo(expected));

    [Test]
    public void ParsesNanosecondTimestamps() =>
        Assert.That(CryptoData.Time(System.Text.Json.JsonDocument.Parse("""{"t":"2026-10-30T13:31:07.123456789Z"}""").RootElement),
            Is.EqualTo(new DateTime(2026, 10, 30, 13, 31, 7, DateTimeKind.Utc).AddTicks(1234567)));

    [Test]
    public async Task Bars_PagesThePairsArray_AndShowsTheMostRecent()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v1beta3/crypto/us/bars", """{"bars":{"BTC/USD":[{"t":"2026-10-30T12:00:00Z","o":70000,"h":70500,"l":69900,"c":70400,"v":1.5,"n":20,"vw":70200}]},"next_page_token":"p2"}""");
        var ctx = CryptoAlpacaFactory.Context(handler);
        // Second page: same route; the handler answers identically, so cap pages by the token on the second reply.
        var result = await new CryptoBarsOperation().ExecuteAsync(new CryptoBarsArgs { Pair = "btc-usd", Limit = 1 }, ctx);

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(handler.Requests[0].RequestUri!.Host, Is.EqualTo("data.alpaca.markets"));
        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("symbols=BTC%2FUSD").And.Contain("timeframe=1Hour"));
        Assert.That(handler.Requests[1].RequestUri!.Query, Does.Contain("page_token=p2"));
        Assert.That(result.Human, Does.Contain("BTC/USD 1Hour bars, 1 shown").And.Contain("70,400.00").And.Contain("8:00 AM ET"));
    }

    [Test]
    public async Task Trades_ShowSecondsInEt_AndTakerSide()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v1beta3/crypto/us/trades", """{"trades":{"ETH/USD":[{"t":"2026-10-30T13:59:01.5Z","p":2500.25,"s":0.0123,"tks":"B","i":7},{"t":"2026-10-30T13:59:30Z","p":2501,"s":0.5,"tks":"S","i":8}]},"next_page_token":null}""");
        var result = await new CryptoTradesOperation().ExecuteAsync(new CryptoTicksArgs { Pair = "ETH/USD" }, CryptoAlpacaFactory.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("9:59:01 AM ET").And.Contain("0.0123").And.Contain("buy").And.Contain("sell").And.Contain("volume 0.5123"));
    }

    [Test]
    public async Task Quotes_ShowTheSpread()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v1beta3/crypto/us/quotes", """{"quotes":{"BTC/USD":[{"t":"2026-10-30T13:59:59Z","bp":70000,"bs":0.2,"ap":70010.5,"as":0.1}]}}""");
        var result = await new CryptoQuotesOperation().ExecuteAsync(new CryptoTicksArgs { Pair = "BTC/USD" }, CryptoAlpacaFactory.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("70,010.50").And.Contain("10.50"));
    }

    [Test]
    public async Task Latest_CombinesTradeQuoteAndBar()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v1beta3/crypto/us/latest/trades", """{"trades":{"BTC/USD":{"t":"2026-10-30T13:59:59Z","p":70005,"s":0.01}}}""")
            .On("/v1beta3/crypto/us/latest/quotes", """{"quotes":{"BTC/USD":{"t":"2026-10-30T13:59:59Z","bp":70000,"bs":1,"ap":70010,"as":2}}}""")
            .On("/v1beta3/crypto/us/latest/bars", """{"bars":{"BTC/USD":{"t":"2026-10-30T13:59:00Z","o":70001,"h":70011,"l":69999,"c":70005,"v":3}}}""");
        var result = await new CryptoLatestOperation().ExecuteAsync(new CryptoPairArgs { Pair = "BTC/USD" }, CryptoAlpacaFactory.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Last trade:").And.Contain("70,005.00 x 0.01").And.Contain("Spread:").And.Contain("Minute bar:"));
        Assert.That(handler.Requests, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Orderbook_SortsBidsDownAndAsksUp_ToDepth()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v1beta3/crypto/us/latest/orderbooks", """{"orderbooks":{"BTC/USD":{"t":"2026-10-30T13:59:59Z","b":[{"p":69990,"s":1},{"p":70000,"s":0.5},{"p":69980,"s":2}],"a":[{"p":70020,"s":1},{"p":70010,"s":0.25}]}}}""");
        var result = await new CryptoOrderbookOperation().ExecuteAsync(new CryptoOrderbookArgs { Pair = "BTC/USD", Depth = 2 }, CryptoAlpacaFactory.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.That(lines[0], Does.Contain("spread 10.00"));
        Assert.That(lines[3], Does.Contain("70,000.00").And.Contain("70,010.00"));
        Assert.That(result.Human, Does.Not.Contain("69,980.00"));
    }

    [Test]
    public async Task Snapshot_ShowsChangeVsPriorDailyClose()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v1beta3/crypto/us/snapshots", """
                {"snapshots":{"BTC/USD":{
                  "latestTrade":{"t":"2026-10-30T13:59:59Z","p":71000,"s":0.1},
                  "latestQuote":{"t":"2026-10-30T13:59:59Z","bp":70990,"bs":1,"ap":71010,"as":1},
                  "minuteBar":{"t":"2026-10-30T13:59:00Z","o":70990,"h":71005,"l":70980,"c":71000,"v":2},
                  "dailyBar":{"t":"2026-10-30T05:00:00Z","o":70000,"h":71200,"l":69800,"c":71000,"v":200},
                  "prevDailyBar":{"t":"2026-10-29T05:00:00Z","o":69000,"h":70500,"l":68800,"c":70000,"v":300}}}}
                """);
        var result = await new CryptoSnapshotOperation().ExecuteAsync(new CryptoPairArgs { Pair = "BTCUSD" }, CryptoAlpacaFactory.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("+1,000.00").And.Contain("+1.43%").And.Contain("Prior daily:"));
    }

    [Test]
    public void RejectsANonPair() =>
        Assert.ThrowsAsync<OperationException>(() =>
            new CryptoSnapshotOperation().ExecuteAsync(new CryptoPairArgs { Pair = "AAPL" }, CryptoAlpacaFactory.Context(new CryptoAlpacaHandler())));

    [Test]
    public async Task MarketChart_DrawsACryptoPairAsOne24hSessionPerDay()
    {
        // 1-minute bars from Thu 10/29 00:00 ET through Fri 10/30 09:59 ET (EDT, UTC-4).
        var bars = new StringBuilder();
        var p = 70000m;
        for (var t = new DateTime(2026, 10, 29, 4, 0, 0, DateTimeKind.Utc); t < new DateTime(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc); t = t.AddMinutes(1))
        {
            var o = p;
            p += (t.Minute % 7 - 3) * 2m;
            if (bars.Length > 0) bars.Append(',');
            bars.Append($$"""{"t":"{{t:yyyy-MM-ddTHH:mm:ssZ}}","o":{{o}},"h":{{Math.Max(o, p) + 5}},"l":{{Math.Min(o, p) - 5}},"c":{{p}},"v":0.5}""");
        }
        var handler = new CryptoAlpacaHandler().On("/v1beta3/crypto/us/bars", $$"""{"bars":{"BTC/USD":[{{bars}}]},"next_page_token":null}""");

        var result = await new MarketChartOperation().ExecuteAsync(new MarketChartArgs { Symbol = "btc/usd", Width = 118 }, CryptoAlpacaFactory.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        var chart = result.Human!.Split("\n\n")[0];
        Assert.That(chart.Split('\n').Select(l => l.Length).Distinct(), Is.EqualTo(new[] { 118 }));
        Assert.That(chart, Does.StartWith("BTC/USD  Thu 10/29 12:00 AM -> Fri 10/30 11:59 PM ET"));
        Assert.That(chart, Does.Contain("<- Thu close"));
        Assert.That(result.Human, Does.Contain("24H").And.Not.Contain("PRE"));
        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("timeframe=1Min"));
    }
}
