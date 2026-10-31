using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Settings;
using IdiotProof.Operations.Tests.Keys;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Settings;

/// <summary>`prefs get|set` (IP-T251) over in-memory UserPreferences.</summary>
public class PrefsOperationsTests
{
    private KeysTestKit kit = null!;

    [SetUp]
    public void SetUp()
    {
        kit = new KeysTestKit();
        kit.Services.AddSingleton<UserPreferencesService>();
    }

    [TearDown]
    public void TearDown() => kit.Dispose();

    private UserPreferencesService Prefs => kit.Provider.GetRequiredService<UserPreferencesService>();

    [Test]
    public async Task Get_ShowsDefaults_WithTheAccountNumberMasked()
    {
        var p = await Prefs.GetOrCreateAsync(KeysTestKit.Owner);
        p.ActiveAccountId = "PA3PG9SP46WU";
        await Prefs.SaveAsync(p);

        var result = await new PrefsGetOperation().ExecuteAsync(new PrefsGetArgs(), kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.That(result.Human, Does.Contain("risk.max-loss-per-trade").And.Contain("100").And.Contain("***46WU"));
        Assert.That(result.Human, Does.Not.Contain("PA3PG9SP46WU"));
        Assert.That(result.Human, Does.Contain(" ET"));
    }

    [Test]
    public async Task SetRiskLimit_StoresIt_AndReportsSanityAdjustments()
    {
        var ok = await new PrefsSetOperation().ExecuteAsync(new PrefsSetArgs { Field = "risk.max-loss-per-day", Value = "750" }, kit.Context());
        Assert.That(ok.Human, Is.EqualTo("risk.max-loss-per-day: 500 -> 750."));
        Assert.That((await Prefs.GetOrCreateAsync(KeysTestKit.Owner)).RiskMaxLossPerDay, Is.EqualTo(750m));

        // A daily cap below the per-trade cap (100) is raised to it.
        var adjusted = await new PrefsSetOperation().ExecuteAsync(new PrefsSetArgs { Field = "risk.max-loss-per-day", Value = "50" }, kit.Context());
        Assert.That(adjusted.Human, Does.Contain("-> 100 (adjusted"));
    }

    [Test]
    public async Task BadInput_IsAUsageError()
    {
        var unknown = await new PrefsSetOperation().ExecuteAsync(new PrefsSetArgs { Field = "color", Value = "red" }, kit.Context());
        var notNumber = await new PrefsSetOperation().ExecuteAsync(new PrefsSetArgs { Field = "risk.account-balance", Value = "lots" }, kit.Context());
        var badType = await new PrefsSetOperation().ExecuteAsync(new PrefsSetArgs { Field = "account-type", Value = "margin" }, kit.Context());

        Assert.That(new[] { unknown.Status, notNumber.Status, badType.Status }, Is.All.EqualTo(OperationStatus.UsageError));
    }
}
