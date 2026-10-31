using IdiotProof.Operations.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options chain (IP-T189): explicit expiration window, nearest expiry, joined snapshots, centred on spot.</summary>
public class OptionsChainOperationTests
{
    private static string Contract(string occ, string type, string strike, string expiry) =>
        $$"""{"id":"{{Guid.NewGuid()}}","symbol":"{{occ}}","underlying_symbol":"AAPL","type":"{{type}}","strike_price":"{{strike}}","expiration_date":"{{expiry}}","open_interest":"1500","size":"100"}""";

    private static readonly string Contracts = "{\"option_contracts\":[" + string.Join(",",
        Contract("AAPL261120C00240000", "call", "240", "2026-11-20"),
        Contract("AAPL261120P00240000", "put", "240", "2026-11-20"),
        Contract("AAPL261120C00250000", "call", "250", "2026-11-20"),
        Contract("AAPL261120P00250000", "put", "250", "2026-11-20"),
        Contract("AAPL261120C00260000", "call", "260", "2026-11-20"),
        Contract("AAPL261120P00260000", "put", "260", "2026-11-20"),
        Contract("AAPL261106C00250000", "call", "250", "2026-11-06")) + "],\"next_page_token\":null}";

    private const string ChainSnapshots = """
        {"snapshots":{
          "AAPL261106C00250000":{"latestQuote":{"bp":3.10,"ap":3.30},"impliedVolatility":0.28,"greeks":{"delta":0.51,"gamma":0.04,"theta":-0.2,"vega":0.15,"rho":0.03}},
          "AAPL261120P00250000":{"latestQuote":{"bp":4.90,"ap":5.10},"impliedVolatility":0.31,"greeks":{"delta":-0.47,"gamma":0.03,"theta":-0.1,"vega":0.28,"rho":-0.08}}
        },"next_page_token":null}
        """;

    private static OptionsDataAlpaca Alpaca() => new OptionsDataAlpaca()
        .On("/v2/options/contracts", Contracts)
        .On("/v1beta1/options/snapshots/AAPL", ChainSnapshots)
        .On("/v2/stocks/AAPL/trades/latest", OptionsDataKit.Spot);

    [Test]
    public async Task SendsTheHorizon_PicksTheNearestExpiry_AndJoinsQuotes()
    {
        var alpaca = Alpaca();
        var result = await new OptionsChainOperation().ExecuteAsync(new OptionsChainArgs { Symbol = "aapl" }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        var contractsUrl = alpaca.Urls.First(u => u.Contains("/v2/options/contracts"));
        Assert.That(contractsUrl, Does.Contain("expiration_date_gte=2026-10-30").And.Contain("expiration_date_lte=2029-11-01").And.Contain("underlying_symbols=AAPL"));
        Assert.That(alpaca.Urls.First(u => u.Contains("/snapshots/AAPL")), Does.Contain("expiration_date=2026-11-06"));
        Assert.That(result.Human, Does.StartWith("AAPL options expiring 2026-11-06 (7 DTE)   spot $250.00"));
        Assert.That(result.Human, Does.Contain("$3.10").And.Contain("28.0%").And.Contain("Expirations (2): 11/06/26, 11/20/26"));
    }

    [Test]
    public async Task ChosenExpiry_WindowedAroundSpot()
    {
        var result = await new OptionsChainOperation().ExecuteAsync(
            new OptionsChainArgs { Symbol = "AAPL", Expiry = new DateOnly(2026, 11, 20), Strikes = 1 }, OptionsDataKit.Context(Alpaca()));

        Assert.That(result.IsOk, Is.True, result.Message);
        var data = OptionsDataKit.Data(result);
        var strikes = data.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("strike").GetDecimal()).ToList();
        Assert.That(strikes, Is.EqualTo(new[] { 240m, 250m }));
        Assert.That(data.GetProperty("total_strikes").GetInt32(), Is.EqualTo(3));
        Assert.That(result.Human, Does.Contain("250 <").And.Contain("-0.47").And.Contain("Showing 2 of 3 strikes"));
    }

    [Test]
    public void Window_KeepsPerSideStrikesAroundSpot()
    {
        var rows = Enumerable.Range(1, 10).Select(i => new ChainRow(i * 10m, null, null)).ToList();
        Assert.That(OptionsChainOperation.Window(rows, 45m, 2).Select(r => r.Strike), Is.EqualTo(new[] { 30m, 40m, 50m, 60m }));
        Assert.That(OptionsChainOperation.Window(rows, 45m, 0), Has.Count.EqualTo(10));
    }
}
