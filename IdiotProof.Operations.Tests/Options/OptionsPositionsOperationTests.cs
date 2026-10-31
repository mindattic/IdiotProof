using IdiotProof.Operations.Account;
using IdiotProof.Operations.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options positions (IP-T197): only us_option rows, decoded from the OCC symbol, with DTE in ET.</summary>
public class OptionsPositionsOperationTests
{
    private const string Positions = """
        [
          {"symbol":"AAPL","asset_class":"us_equity","qty":"10","avg_entry_price":"200","current_price":"250","market_value":"2500","unrealized_pl":"500"},
          {"symbol":"AAPL261120C00250000","asset_class":"us_option","side":"long","qty":"2","avg_entry_price":"4.00","current_price":"5.20",
           "cost_basis":"800","market_value":"1040","unrealized_pl":"240","unrealized_plpc":"0.3"}
        ]
        """;

    [Test]
    public async Task ListsOnlyOptionContracts_DecodedWithDte()
    {
        var paper = new OptionsDataAlpaca().On("/v2/positions", Positions);
        var live = new OptionsDataAlpaca().On("/v2/positions", "[]");
        var result = await new OptionsPositionsOperation().ExecuteAsync(new AccountScopeArgs(), OptionsDataKit.Context(paper, live));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("AAPL261120C00250000").And.Contain("2026-11-20").And.Contain("+$240.00").And.Contain("+30.00%"));
        Assert.That(result.Human, Does.Contain("Live account: no open option positions."));
        Assert.That(result.Human, Does.Not.Contain("$2,500.00"), "equities are not option positions");
        var row = OptionsDataKit.Data(result)[0].GetProperty("positions")[0];
        Assert.That(row.GetProperty("dte").GetInt32(), Is.EqualTo(21));
        Assert.That(row.GetProperty("underlying").GetString(), Is.EqualTo("AAPL"));
        Assert.That(row.GetProperty("strike").GetDecimal(), Is.EqualTo(250m));
        Assert.That(live.Urls.Single(), Does.StartWith("https://api.alpaca.markets/v2/positions"));
    }
}
