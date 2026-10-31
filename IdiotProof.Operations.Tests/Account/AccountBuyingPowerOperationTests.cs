using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;

namespace IdiotProof.Operations.Tests.Account;

/// <summary>account buying-power and account pdt (IP-T101): GET /v2/account per account, rendered side by side.</summary>
public class AccountBuyingPowerOperationTests
{
    private const string PaperAccount = """
        {"status":"ACTIVE","equity":"101234.5","cash":"50000","buying_power":"200000","regt_buying_power":"150000",
         "daytrading_buying_power":"400000","non_marginable_buying_power":"50000","options_buying_power":"50000",
         "multiplier":"4","daytrade_count":2,"pattern_day_trader":false,"trading_blocked":false}
        """;

    private const string LiveAccount = """
        {"status":"ACTIVE","equity":"1800.10","cash":"1800.10","buying_power":"1800.10","regt_buying_power":"1800.10",
         "daytrading_buying_power":"0","non_marginable_buying_power":"1800.10","options_buying_power":"1800.10",
         "multiplier":"1","daytrade_count":3,"pattern_day_trader":false,"trading_blocked":false}
        """;

    private static ScriptedAlpacaFactory Factory()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/account", PaperAccount);
        f.Live.On("GET /v2/account", LiveAccount);
        return f;
    }

    [Test]
    public async Task BuyingPower_ShowsEveryKindForBothAccounts()
    {
        var result = await new AccountBuyingPowerOperation().ExecuteAsync(new AccountScopeArgs(), AccountTestKit.Context(Factory()));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Paper").And.Contain("$400,000.00").And.Contain("4x"));
        Assert.That(result.Human, Does.Contain("Live").And.Contain("$1,800.10").And.Contain("1x"));
    }

    [Test]
    public async Task BuyingPower_BothScope_KeepsTheOtherAccountWhenOneIsRefused()
    {
        var f = Factory();
        f.Refused.Add(BrokerMode.Live);

        var result = await new AccountBuyingPowerOperation().ExecuteAsync(new AccountScopeArgs(), AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("$200,000.00").And.Contain("No live Alpaca keys configured."));
    }

    [Test]
    public void BuyingPower_SingleScope_PropagatesTheRefusal()
    {
        var f = Factory();
        f.Refused.Add(BrokerMode.Live);

        Assert.ThrowsAsync<AlpacaAccessException>(() =>
            new AccountBuyingPowerOperation().ExecuteAsync(new AccountScopeArgs { Mode = AccountScope.Live }, AccountTestKit.Context(f)));
    }

    [Test]
    public async Task Pdt_CountsTradesLeftUnderTheFloor_AndUnlimitedAbove()
    {
        var result = await new AccountPdtOperation().ExecuteAsync(new AccountScopeArgs(), AccountTestKit.Context(Factory()));

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.That(lines.Single(l => l.StartsWith("Paper")), Does.Contain("unlimited"));
        Assert.That(lines.Single(l => l.StartsWith("Live")), Does.Contain("one more day trade flags"));
    }

    [TestCase(0, false, 1000, 3)]
    [TestCase(2, false, 1000, 1)]
    [TestCase(5, false, 1000, 0)]
    [TestCase(4, true, 1000, 0)]
    public void Pdt_Standing(int count, bool flagged, decimal equity, int left) =>
        Assert.That(AccountPdtOperation.Standing(count, flagged, equity).Left, Is.EqualTo(left));

    [Test]
    public void Pdt_Standing_AboveTheFloorIsUnlimited() =>
        Assert.That(AccountPdtOperation.Standing(9, true, 30_000m).Left, Is.Null);
}
