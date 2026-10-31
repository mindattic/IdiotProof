using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Settings;
using IdiotProof.Operations.Tests.Keys;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Settings;

/// <summary>`settings list|get|set` (IP-T250) over an in-memory SettingsKv, validated by SettingsMetadata.</summary>
public class SettingsOperationsTests
{
    private KeysTestKit kit = null!;

    [SetUp]
    public void SetUp()
    {
        kit = new KeysTestKit();
        kit.Services.AddSingleton<SettingsRepository>();
    }

    [TearDown]
    public void TearDown() => kit.Dispose();

    private SettingsRepository Repo => kit.Provider.GetRequiredService<SettingsRepository>();

    [Test]
    public async Task Set_ValidatesAndNormalizes_ThenGetAndListShowIt()
    {
        var set = await new SettingsSetOperation().ExecuteAsync(new SettingsSetArgs { Key = "options.riskfreerate", Value = " 0.045 " }, kit.Context());

        Assert.That(set.Status, Is.EqualTo(OperationStatus.Ok), set.Message);
        Assert.That(await Repo.GetAsync(OptionsTradingService.RiskFreeRateKey), Is.EqualTo("0.045"));
        Assert.That(set.Human, Does.Contain("(default 0.04) -> 0.045"));

        var get = await new SettingsGetOperation().ExecuteAsync(new SettingsGetArgs { Key = "Options.RiskFreeRate" }, kit.Context());
        Assert.That(get.Human, Does.Contain("0.045").And.Contain("0..0.25"));

        var list = await new SettingsListOperation().ExecuteAsync(new SettingsListArgs(), kit.Context());
        Assert.That(list.Human, Does.Contain("Options.RiskFreeRate").And.Contain("decay.be-bex.lookbackdays").And.Contain("(default)"));
        Assert.That(list.Human, Does.Not.Contain("decay.be-bex.latest"), "internal keys hide without --all");
    }

    [Test]
    public async Task OutOfRange_WrongType_Unknown_AndInternal_AreRefused()
    {
        var range = await new SettingsSetOperation().ExecuteAsync(new SettingsSetArgs { Key = "Options.RiskFreeRate", Value = "4" }, kit.Context());
        var type = await new SettingsSetOperation().ExecuteAsync(new SettingsSetArgs { Key = "decay.be-bex.lookbackdays", Value = "twenty" }, kit.Context());
        var unknown = await new SettingsSetOperation().ExecuteAsync(new SettingsSetArgs { Key = "engine.turbo", Value = "1" }, kit.Context());
        var internalKey = await new SettingsSetOperation().ExecuteAsync(new SettingsSetArgs { Key = "decay.be-bex.disarmed.abc", Value = "true" }, kit.Context());

        Assert.Multiple(() =>
        {
            Assert.That(range.Status, Is.EqualTo(OperationStatus.UsageError));
            Assert.That(range.Message, Does.Contain("between 0 and 0.25"));
            Assert.That(type.Status, Is.EqualTo(OperationStatus.UsageError));
            Assert.That(unknown.Status, Is.EqualTo(OperationStatus.UsageError));
            Assert.That(internalKey.Status, Is.EqualTo(OperationStatus.Blocked));
        });
        Assert.That(await Repo.GetAllAsync(), Is.Empty, "nothing was written");
    }

    [Test]
    public async Task ListAll_ShowsInternalFamilyMembers_AndUnregisteredKeys()
    {
        await Repo.SetAsync("decay.be-bex.disarmed.1234", "true");
        await Repo.SetAsync("legacy.thing", "x");

        var list = await new SettingsListOperation().ExecuteAsync(new SettingsListArgs { All = true }, kit.Context());

        Assert.That(list.Human, Does.Contain("decay.be-bex.disarmed.1234").And.Contain("legacy.thing").And.Contain("unregistered key"));
    }
}
