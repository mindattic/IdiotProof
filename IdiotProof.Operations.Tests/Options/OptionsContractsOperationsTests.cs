using IdiotProof.Operations.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options contracts / options contract (IP-T309): every catalog filter on the wire, paging, one contract by symbol or id.</summary>
public class OptionsContractsOperationsTests
{
    private const string Contract = """
        {"id":"6e58f870-fe73-4583-81e4-b9a37892c36f","symbol":"AAPL261120C00250000","name":"AAPL Nov 20 2026 250 Call","status":"active","tradable":true,
         "expiration_date":"2026-11-20","root_symbol":"AAPL","underlying_symbol":"AAPL","type":"call","style":"american","strike_price":"250",
         "size":"100","open_interest":"4321","open_interest_date":"2026-10-29","close_price":"5.05","close_price_date":"2026-10-29"}
        """;

    [Test]
    public async Task SendsEveryFilter_AndFollowsPages()
    {
        var alpaca = new OptionsDataAlpaca()
            .On("page_token=p2", "{\"option_contracts\":[" + Contract.Replace("C00250000", "C00255000").Replace("\"250\"", "\"255\"") + "],\"next_page_token\":null}")
            .On("/v2/options/contracts", "{\"option_contracts\":[" + Contract + "],\"next_page_token\":\"p2\"}");
        var result = await new OptionsContractsOperation().ExecuteAsync(new OptionsContractsArgs
        {
            Underlying = "aapl",
            Type = OptionsContractType.Call,
            StrikeMin = 240m,
            StrikeMax = 260m,
            Horizon = 30,
        }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Urls[0], Does.Contain("underlying_symbols=AAPL").And.Contain("status=active").And.Contain("type=call")
            .And.Contain("strike_price_gte=240").And.Contain("strike_price_lte=260")
            .And.Contain("expiration_date_gte=2026-10-30").And.Contain("expiration_date_lte=2026-11-29"));
        Assert.That(alpaca.Urls, Has.Count.EqualTo(2));
        Assert.That(result.Human, Does.Contain("AAPL261120C00250000").And.Contain("AAPL261120C00255000").And.Contain("4,321").And.Contain("2 contracts."));
        Assert.That(OptionsDataKit.Data(result).GetProperty("contracts")[0].GetProperty("style").GetString(), Is.EqualTo("american"));
    }

    [Test]
    public async Task OneContract_BySymbol()
    {
        var alpaca = new OptionsDataAlpaca().On("/v2/options/contracts/AAPL261120C00250000", Contract);
        var result = await new OptionsContractOperation().ExecuteAsync(new OptionsContractArgs { Contract = "aapl261120c00250000" }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("AAPL Nov 20 2026 250 Call").And.Contain("4,321 (as of 2026-10-29)").And.Contain("100 shares"));
        Assert.That(OptionsDataKit.Data(result).GetProperty("id").GetString(), Is.EqualTo("6e58f870-fe73-4583-81e4-b9a37892c36f"));
    }
}
