using System.Net;
using IdiotProof.Blazor.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Account;

/// <summary>account config and account config set (IP-T299): GET/PATCH /v2/account/configurations, Live behind a code.</summary>
public class AccountConfigOperationTests
{
    private const string Config = """
        {"dtbp_check":"entry","trade_confirm_email":"all","suspend_trade":false,"no_shorting":false,
         "fractional_trading":true,"max_margin_multiplier":"4","max_options_trading_level":3,"pdt_check":"entry",
         "ptp_no_exception_entry":false}
        """;

    private const string NoShorting = """
        {"dtbp_check":"entry","trade_confirm_email":"all","suspend_trade":false,"no_shorting":true,
         "fractional_trading":true,"max_margin_multiplier":"4","max_options_trading_level":3,"pdt_check":"entry",
         "ptp_no_exception_entry":false}
        """;

    private static ScriptedAlpacaFactory Factory()
    {
        var f = new ScriptedAlpacaFactory();
        foreach (var h in new[] { f.Paper, f.Live })
            h.On("GET /v2/account/configurations", Config).On("PATCH /v2/account/configurations", NoShorting);
        return f;
    }

    private ServiceProvider db = null!;

    [OneTimeSetUp]
    public void CreateDb() => db = new ServiceCollection()
        .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"account-config-{Guid.NewGuid():N}"))
        .BuildServiceProvider();

    [OneTimeTearDown]
    public void DisposeDb() => db.Dispose();

    private void WithStore(IServiceCollection s) =>
        s.AddSingleton(new ConfirmationStore(db.GetRequiredService<IDbContextFactory<AppDbContext>>(), TimeProvider.System));

    [Test]
    public async Task Show_PutsBothAccountsSideBySide()
    {
        var result = await new AccountConfigOperation().ExecuteAsync(new AccountScopeArgs(), AccountTestKit.Context(Factory()));

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.That(lines[0], Does.Contain("Paper").And.Contain("Live"));
        Assert.That(lines.Single(l => l.StartsWith("max_options_trading_level")), Does.Contain("3"));
        Assert.That(lines.Single(l => l.StartsWith("fractional_trading")), Does.Contain("true"));
    }

    [Test]
    public async Task SetPaper_PatchesJustThatKey_AsAJsonBoolean()
    {
        var f = Factory();

        var result = await new AccountConfigSetOperation().ExecuteAsync(
            new AccountConfigSetArgs { Setting = "no-shorting", Value = "TRUE" }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Is.EqualTo("paper no_shorting: false -> true."));
        var patch = f.Paper.Requests.Single(r => r.Method == HttpMethod.Patch);
        Assert.That(patch.Body, Is.EqualTo("""{"no_shorting":true}"""));
        Assert.That(f.Live.Requests, Is.Empty);
    }

    [Test]
    public async Task SetOptionsLevel_SendsANumber()
    {
        var f = Factory();
        var result = await new AccountConfigSetOperation().ExecuteAsync(
            new AccountConfigSetArgs { Setting = "max_options_trading_level", Value = "1" }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(f.Paper.Requests.Single(r => r.Method == HttpMethod.Patch).Body, Is.EqualTo("""{"max_options_trading_level":1}"""));
    }

    [Test]
    public async Task SetLive_NeedsACode_ThenTheCodeAppliesIt_Once()
    {
        var f = Factory();
        var args = new AccountConfigSetArgs { Setting = "no_shorting", Value = "true", Mode = BrokerMode.Live };

        var first = await new AccountConfigSetOperation().ExecuteAsync(args, AccountTestKit.Context(f, WithStore));
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(first.Confirmation!.Preview, Does.Contain("SET LIVE account no_shorting: false -> true"));
        Assert.That(f.Live.Requests.Any(r => r.Method == HttpMethod.Patch), Is.False, "nothing changes before the code");

        var wrong = await new AccountConfigSetOperation().ExecuteAsync(args with { Value = "false" },
            AccountTestKit.Context(Factory(), WithStore, confirm: first.Confirmation.Code));
        Assert.That(wrong.IsOk, Is.True, "false is already the value, so no code is consumed");

        var second = await new AccountConfigSetOperation().ExecuteAsync(args, AccountTestKit.Context(f, WithStore, confirm: first.Confirmation.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        Assert.That(f.Live.Requests.Count(r => r.Method == HttpMethod.Patch), Is.EqualTo(1));

        var replay = await new AccountConfigSetOperation().ExecuteAsync(args, AccountTestKit.Context(f, WithStore, confirm: first.Confirmation.Code));
        Assert.That(replay.Status, Is.EqualTo(OperationStatus.Blocked));
    }

    [Test]
    public async Task SetLive_ACodeForADifferentChangeIsRefused()
    {
        var f = Factory();
        var args = new AccountConfigSetArgs { Setting = "no_shorting", Value = "true", Mode = BrokerMode.Live };
        var first = await new AccountConfigSetOperation().ExecuteAsync(args, AccountTestKit.Context(f, WithStore));

        var other = await new AccountConfigSetOperation().ExecuteAsync(args with { Setting = "suspend_trade" },
            AccountTestKit.Context(f, WithStore, confirm: first.Confirmation!.Code));

        Assert.That(other.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(f.Live.Requests.Any(r => r.Method == HttpMethod.Patch), Is.False);
    }

    [TestCase("bogus", "true")]
    [TestCase("dtbp_check", "sometimes")]
    [TestCase("max_margin_multiplier", "3")]
    public async Task BadSettingOrValue_IsUsage(string setting, string value)
    {
        var result = await new AccountConfigSetOperation().ExecuteAsync(
            new AccountConfigSetArgs { Setting = setting, Value = value }, AccountTestKit.Context(Factory()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
