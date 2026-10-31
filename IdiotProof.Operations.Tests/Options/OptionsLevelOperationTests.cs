using IdiotProof.Operations.Account;
using IdiotProof.Operations.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options level (IP-T188): the plural options_* account fields, per account, an unreachable account as a row.</summary>
public class OptionsLevelOperationTests
{
    private const string PaperAccount = """
        {"account_number":"PA123","status":"ACTIVE","options_trading_level":"3","options_approved_level":"3","options_buying_power":"25000.50","trading_blocked":false}
        """;

    [Test]
    public async Task ReadsEffectiveAndApprovedLevel_AndShowsAMissingLiveAccountAsARow()
    {
        var paper = new OptionsDataAlpaca().On("/v2/account", PaperAccount);
        var result = await new OptionsLevelOperation().ExecuteAsync(new AccountScopeArgs(), OptionsDataKit.Context(paper));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("level 2 + spreads").And.Contain("$25,000.50").And.Contain("No live Alpaca keys configured."));
        var data = OptionsDataKit.Data(result);
        Assert.That(data[0].GetProperty("trading_level").GetInt32(), Is.EqualTo(3));
        Assert.That(data[0].GetProperty("options_buying_power").GetDecimal(), Is.EqualTo(25000.50m));
        Assert.That(data[1].GetProperty("account").GetString(), Is.EqualTo("live"));
        Assert.That(paper.Urls.Single(), Does.StartWith("https://paper-api.alpaca.markets/v2/account"));
    }

    [Test]
    public async Task FallsBackToTheSingularFieldNames()
    {
        var paper = new OptionsDataAlpaca().On("/v2/account", """{"status":"ACTIVE","option_trading_level":"1"}""");
        var result = await new OptionsLevelOperation().ExecuteAsync(new AccountScopeArgs { Mode = AccountScope.Paper }, OptionsDataKit.Context(paper));

        Assert.That(OptionsDataKit.Data(result)[0].GetProperty("trading_level").GetInt32(), Is.EqualTo(1));
        Assert.That(result.Human, Does.Contain("covered calls, cash-secured puts"));
    }
}
