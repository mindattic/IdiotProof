using IdiotProof.Core.Data;
using IdiotProof.Operations.Research;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research universe list / refresh (IP-T233) over EF InMemory and a scripted Alpaca.</summary>
public class ResearchUniverseOperationsTests
{
    private const string Assets = """
        [{"symbol":"NVDA","exchange":"NASDAQ","tradable":true},
         {"symbol":"IBM","exchange":"NYSE","tradable":true},
         {"symbol":"OTCX","exchange":"OTC","tradable":true},
         {"symbol":"HALT","exchange":"NYSE","tradable":false}]
        """;

    private const string Trades = """{"trades":{"NVDA":{"p":120.5},"IBM":{"p":210.25}}}""";

    [Test]
    public async Task Refresh_LoadsTradableNasdaqNyse_WithPrices()
    {
        var kit = new ResearchKit();
        kit.Http.On("GET /v2/assets", Assets).On("GET /v2/stocks/trades/latest", Trades);

        var result = await new ResearchUniverseRefreshOperation().ExecuteAsync(new ResearchUniverseRefreshArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("2 tickers (2 new), 2 with a last price"));
        await using var db = await kit.Db.CreateDbContextAsync();
        Assert.That(await db.TrackedTickers.Select(t => t.Symbol).OrderBy(s => s).ToListAsync(), Is.EqualTo(new[] { "IBM", "NVDA" }));
        Assert.That(kit.Http.Requests.All(r => r.Method == HttpMethod.Get), Is.True, "read-only on Alpaca");
    }

    [Test]
    public async Task Refresh_WhenFresh_DoesNothingUnlessForced()
    {
        var kit = new ResearchKit();
        await kit.AddAsync(new TrackedTicker { Symbol = "NVDA", Exchange = "NASDAQ", IsTradable = true, LastRefreshedUtc = DateTime.UtcNow.AddHours(-1) });

        var result = await new ResearchUniverseRefreshOperation().ExecuteAsync(new ResearchUniverseRefreshArgs(), kit.Context());

        Assert.That(result.Human, Does.Contain("nothing to do"));
        Assert.That(kit.Http.Requests, Is.Empty);
    }

    [Test]
    public async Task Refresh_WithoutAHostKey_IsBlocked()
    {
        var kit = new ResearchKit();
        kit.Settings.AlpacaApiKeyId = "";
        var result = await new ResearchUniverseRefreshOperation().ExecuteAsync(new ResearchUniverseRefreshArgs { Force = true }, kit.Context());
        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
    }

    [Test]
    public async Task List_FiltersByExchangePrefixAndPrice()
    {
        var kit = new ResearchKit();
        await kit.AddAsync(
            new TrackedTicker { Symbol = "NVDA", Exchange = "NASDAQ", LastPrice = 120m },
            new TrackedTicker { Symbol = "NVAX", Exchange = "NASDAQ", LastPrice = 4m },
            new TrackedTicker { Symbol = "IBM", Exchange = "NYSE", LastPrice = 210m });

        var cheap = await new ResearchUniverseListOperation().ExecuteAsync(new ResearchUniverseListArgs { MaxPrice = 5 }, kit.Context());
        var nv = await new ResearchUniverseListOperation().ExecuteAsync(new ResearchUniverseListArgs { Prefix = "nv", Exchange = "nasdaq" }, kit.Context());

        Assert.That(cheap.Human, Does.Contain("NVAX").And.Not.Contain("NVDA").And.Not.Contain("IBM"));
        Assert.That(nv.Human, Does.Contain("NVAX").And.Contain("NVDA").And.Not.Contain("IBM").And.Contain("3 tracked"));
    }
}
