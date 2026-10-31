using IdiotProof.Core.Trading;
using IdiotProof.Operations.Reference;

namespace IdiotProof.Operations.Tests.Reference;

/// <summary>assets list / assets show (IP-T311) against a scripted /v2/assets.</summary>
public class AssetOperationsTests
{
    private const string Assets = """
        [
          {"id":"a1","class":"us_equity","exchange":"NASDAQ","symbol":"MSFT","name":"Microsoft Corporation","status":"active","tradable":true,"marginable":true,"shortable":true,"easy_to_borrow":true,"fractionable":true,"attributes":["has_options"]},
          {"id":"a2","class":"us_equity","exchange":"NASDAQ","symbol":"AAPL","name":"Apple Inc. Common Stock","status":"active","tradable":true,"marginable":true,"shortable":true,"easy_to_borrow":true,"fractionable":true,"attributes":[]},
          {"id":"a3","class":"us_equity","exchange":"NASDAQ","symbol":"APPN","name":"Appian Corp","status":"active","tradable":true,"marginable":true,"shortable":false,"easy_to_borrow":false,"fractionable":false,"attributes":[]},
          {"id":"a4","class":"us_equity","exchange":"NASDAQ","symbol":"ZZZQ","name":"Pineapple Holdings","status":"active","tradable":false,"marginable":false,"shortable":false,"easy_to_borrow":false,"fractionable":false,"attributes":[]}
        ]
        """;

    [Test]
    public async Task List_SendsFilters_SearchesSymbolPrefixOrName_SortsAndCountsTotals()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/assets", Assets);
        var ctx = ReferenceTestKit.Context(alpaca, out var factory);

        var result = await new AssetsListOperation().ExecuteAsync(new AssetsListArgs
        {
            Class = AssetClassFilter.UsEquity, Exchange = "nasdaq", Attributes = ["has_options,ipo"], Search = "app", Limit = 2,
        }, ctx);

        Assert.That(result.IsOk, Is.True, result.Message);
        var uri = alpaca.Requests.Single().Uri;
        Assert.That(uri.Host, Is.EqualTo("paper-api.alpaca.markets"));
        Assert.That(uri.Query, Does.Contain("status=active").And.Contain("asset_class=us_equity").And.Contain("exchange=NASDAQ").And.Contain("attributes=has_options%2Cipo"));
        Assert.That(factory.Modes, Is.EqualTo(new[] { BrokerMode.Paper }));
        // AAPL (name has "Apple"), APPN (prefix), ZZZQ (name "Pineapple") match; limit 2 shows AAPL, APPN.
        Assert.That(result.Human, Does.Contain("AAPL").And.Contain("APPN").And.Not.Contain("ZZZQ").And.Not.Contain("MSFT"));
        Assert.That(result.Human, Does.Contain("Showing 2 of 3 matching assets (4 returned by Alpaca)"));
    }

    [Test]
    public async Task List_ShortableFlagFilters()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/assets", Assets);

        var result = await new AssetsListOperation().ExecuteAsync(new AssetsListArgs { Shortable = true, Status = AssetStatusFilter.All }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.Human, Does.Contain("Showing 2 of 2 matching"));
        Assert.That(alpaca.Requests.Single().Uri.Query, Does.Not.Contain("status="));
    }

    [Test]
    public async Task Show_RendersTheFlagsAndMargin_EscapesCryptoPairs()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/assets/", """
            {"id":"b1","class":"crypto","exchange":"CRYPTO","symbol":"BTC/USD","name":"Bitcoin","status":"active","tradable":true,"marginable":false,
             "maintenance_margin_requirement":100,"margin_requirement_long":"100","margin_requirement_short":"100","shortable":false,"easy_to_borrow":false,
             "fractionable":true,"attributes":[],"min_order_size":"0.0001","min_trade_increment":"0.000000001","price_increment":"1"}
            """);

        var result = await new AssetsShowOperation().ExecuteAsync(new AssetsShowArgs { Symbol = "btc/usd", Mode = AlpacaAccount.Live }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Requests.Single().Uri.AbsoluteUri, Is.EqualTo("https://api.alpaca.markets/v2/assets/BTC%2FUSD"));
        Assert.That(result.Human, Does.Match(@"Easy to borrow: +no").And.Match(@"Maintenance margin: +100%").And.Match(@"Fractionable: +yes"));
    }
}
