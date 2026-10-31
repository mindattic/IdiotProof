using System.Net;
using IdiotProof.Operations.Reference;

namespace IdiotProof.Operations.Tests.Reference;

/// <summary>assets bonds / market bond-prices (IP-T312), including the fail-closed path.</summary>
public class FixedIncomeOperationsTests
{
    [Test]
    public async Task Bonds_ListsTreasuriesByMaturity_WithFilters()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/assets/fixed_income/us_treasuries", """
            {"us_treasuries":[
              {"cusip":"91282CJL5","isin":"US91282CJL54","bond_status":"outstanding","tradable":true,"subtype":"note","maturity_date":"2028-11-15","description_short":"T 4.5 11/15/28","coupon":4.5,"close_price":100.12,"close_yield_to_maturity":4.43},
              {"cusip":"912797KA4","isin":"US912797KA41","bond_status":"outstanding","tradable":true,"subtype":"bill","maturity_date":"2026-12-24","description_short":"B 0 12/24/26","coupon":0,"close_price":99.31,"close_yield_to_maturity":4.81}
            ]}
            """);

        var result = await new AssetsBondsOperation().ExecuteAsync(new AssetsBondsArgs { Subtype = "bill", Isins = ["US912797KA41"] }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Requests.Single().Uri.Query, Does.Contain("subtype=bill").And.Contain("isins=US912797KA41"));
        var human = result.Human!;
        Assert.That(human.IndexOf("912797KA4", StringComparison.Ordinal), Is.LessThan(human.IndexOf("91282CJL5", StringComparison.Ordinal)), "sorted by maturity");
        Assert.That(human, Does.Contain("4.810%").And.Contain("Showing 2 of 2 matching treasuries"));
    }

    [Test]
    public async Task Bonds_FailsClosedWithAlpacasMessage_WhenTheAccountLacksFixedIncome()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/assets/fixed_income/us_corporates",
            """{"code":40410000,"message":"endpoint not found"}""", HttpStatusCode.NotFound);

        var result = await new AssetsBondsOperation().ExecuteAsync(new AssetsBondsArgs { Type = BondType.Corporates }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("endpoint not found").And.Contain("404"));
    }

    [Test]
    public async Task BondPrices_HitsTheDataHost_AndNamesMissingIsins()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v1beta1/fixed_income/latest/prices", """
            {"prices":{"US912797KA41":{"t":"2026-10-30T19:59:00Z","p":99.312,"ytm":4.812,"ytw":4.812}}}
            """);

        var result = await new MarketBondPricesOperation().ExecuteAsync(new BondPricesArgs { Isins = ["us912797ka41,US0000000000"] }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        var uri = alpaca.Requests.Single().Uri;
        Assert.That(uri.Host, Is.EqualTo("data.alpaca.markets"));
        Assert.That(uri.Query, Does.Contain("isins=US912797KA41%2CUS0000000000"));
        Assert.That(result.Human, Does.Contain("99.312").And.Contain("3:59 PM ET").And.Contain("No price for: US0000000000"));
    }
}
