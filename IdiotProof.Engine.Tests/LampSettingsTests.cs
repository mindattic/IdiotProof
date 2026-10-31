using System.Reflection;
using System.Text.Json;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using Microsoft.Extensions.Configuration;

namespace IdiotProof.Engine.Tests;

/// <summary>
/// IP-T347 (LAMP): IdiotProof's app-level settings come from MindAttic Vault. One test per setting proves the
/// value edited in Vault is the one AppSettings.Load hands the app, plus the import, save and manifest paths.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class LampSettingsTests
{
    private string dir = "";
    private string vaultDir = "";
    private WebStorageProvider storage = null!;
    private Func<JsonSettingsStore<IdiotProofSettings>> originalFactory = null!;

    private JsonSettingsStore<IdiotProofSettings> Vault => new(vaultDir);

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "ip-lamp-" + Guid.NewGuid().ToString("N"));
        vaultDir = Path.Combine(dir, "vault");
        storage = new WebStorageProvider(Path.Combine(dir, "app"));
        originalFactory = IdiotProofSettings.StoreFactory;
        IdiotProofSettings.StoreFactory = () => Vault;
    }

    [TearDown]
    public void TearDown()
    {
        IdiotProofSettings.StoreFactory = originalFactory;
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Saves a Vault file with one setting changed, keeps app-settings.json at defaults, and loads.</summary>
    private AppSettings LoadWithVault(Action<IdiotProofSettings> edit)
    {
        new AppSettings().Save(storage);            // app-settings.json + Vault at defaults
        var v = Vault.LoadOrSeed();
        edit(v);
        Vault.Save(v);
        return AppSettings.Load(storage);
    }

    [Test] public void AlpacaIsPaper_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.AlpacaIsPaper = false).AlpacaIsPaper, Is.False);

    [Test] public void DefaultBroker_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.DefaultBroker = "alpaca").DefaultBroker, Is.EqualTo("alpaca"));

    [Test] public void DefaultDataFeed_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.DefaultDataFeed = "Polygon").DefaultDataFeed, Is.EqualTo("Polygon"));

    [Test] public void Timezone_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.Timezone = "Eastern Standard Time").Timezone, Is.EqualTo("Eastern Standard Time"));

    [Test] public void ShowConnectionMessages_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.ShowConnectionMessages = false).ShowConnectionMessages, Is.False);

    [Test] public void LlmVotingEnabled_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.LlmVotingEnabled = true).LlmVotingEnabled, Is.True);

    [Test] public void LlmConsensusThreshold_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.LlmConsensusThreshold = 0.8m).LlmConsensusThreshold, Is.EqualTo(0.8m));

    [Test] public void StrategyEvaluationIntervalSeconds_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.StrategyEvaluationIntervalSeconds = 90).StrategyEvaluationIntervalSeconds, Is.EqualTo(90));

    [Test] public void MaxConcurrentEvaluations_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.MaxConcurrentEvaluations = 9).MaxConcurrentEvaluations, Is.EqualTo(9));

    [Test] public void LlmVoterModel_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.LlmVoterModel = "gpt-test").LlmVoterModel, Is.EqualTo("gpt-test"));

    [Test] public void AutoGapperMinGapPercent_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.AutoGapperMinGapPercent = 22.5).AutoGapperMinGapPercent, Is.EqualTo(22.5));

    [Test] public void AutoGapperMinPrice_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.AutoGapperMinPrice = 3.25).AutoGapperMinPrice, Is.EqualTo(3.25));

    [Test] public void AutoGapperMaxCount_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.AutoGapperMaxCount = 12).AutoGapperMaxCount, Is.EqualTo(12));

    [Test] public void AutoGapperBrokerMode_ComesFromVault() =>
        Assert.That(LoadWithVault(v => v.AutoGapperBrokerMode = "live").AutoGapperBrokerMode, Is.EqualTo("live"));

    [Test]
    public void EverySettingHasItsOwnTest()
    {
        var tests = typeof(LampSettingsTests).GetMethods().Select(m => m.Name).ToHashSet();
        var settings = typeof(IdiotProofSettings).GetProperties()
            .Where(p => p.GetCustomAttribute<SettingAttribute>() is not null).Select(p => p.Name).ToList();
        Assert.That(settings, Has.Count.EqualTo(14));
        Assert.That(settings.Where(s => !tests.Contains(s + "_ComesFromVault")), Is.Empty);
    }

    [Test]
    public void FirstLoad_ImportsTodaysValuesFromAppSettingsJson()
    {
        storage.EnsureDirectories();
        File.WriteAllText(Path.Combine(storage.SettingsPath, "app-settings.json"),
            """{ "alpacaIsPaper": false, "defaultBroker": "alpaca", "autoGapperMaxCount": 7 }""");
        Assert.That(Vault.Exists(), Is.False);

        var loaded = AppSettings.Load(storage);

        Assert.That(Vault.Exists(), Is.True);
        var v = Vault.LoadOrSeed();
        Assert.Multiple(() =>
        {
            Assert.That(v.AlpacaIsPaper, Is.False);
            Assert.That(v.DefaultBroker, Is.EqualTo("alpaca"));
            Assert.That(v.AutoGapperMaxCount, Is.EqualTo(7));
            Assert.That(loaded.DefaultBroker, Is.EqualTo("alpaca"));
        });
    }

    [Test]
    public void Save_WritesTheVaultFile()
    {
        var s = new AppSettings { DefaultBroker = "alpaca", LlmVotingEnabled = true };
        s.Save(storage);
        var v = Vault.LoadOrSeed();
        Assert.That(v.DefaultBroker, Is.EqualTo("alpaca"));
        Assert.That(v.LlmVotingEnabled, Is.True);
    }

    [Test]
    public void SecretsStayOutOfTheVaultFile()
    {
        new AppSettings { AlpacaApiKeyId = "id-not-for-vault", AlpacaApiSecretKey = "secret-not-for-vault" }.Save(storage);
        var json = File.ReadAllText(Vault.FilePath);
        Assert.That(json, Does.Not.Contain("not-for-vault"));
        Assert.That(json, Does.Not.Contain("adminPassword").IgnoreCase);
    }

    [Test]
    public void AppServiceConfiguration_WinsOverTheVaultFile()
    {
        var s = LoadWithVault(v => v.DefaultBroker = "Sandbox");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MindAttic:Settings:IdiotProof:DefaultBroker"] = "alpaca",
                ["MindAttic:Settings:IdiotProof:AutoGapperMaxCount"] = "11",
                ["MindAttic:Settings:IdiotProof:LlmConsensusThreshold"] = "not-a-number",
            })
            .Build();

        s.OverlayFromConfiguration(config);

        Assert.Multiple(() =>
        {
            Assert.That(s.DefaultBroker, Is.EqualTo("alpaca"));
            Assert.That(s.AutoGapperMaxCount, Is.EqualTo(11));
            Assert.That(s.LlmConsensusThreshold, Is.EqualTo(0.66m), "a malformed value keeps the Vault value");
        });
    }

    [Test]
    public void Manifest_DescribesIdiotProofWithEverySetting()
    {
        var manifest = SettingsManifestBuilder.Build<IdiotProofSettings>();
        var json = JsonSerializer.Serialize(manifest);
        Assert.That(json, Does.Contain("IdiotProof"));
        foreach (var p in typeof(IdiotProofSettings).GetProperties().Where(p => p.GetCustomAttribute<SettingAttribute>() is not null))
            Assert.That(json, Does.Contain(p.Name).IgnoreCase, p.Name);
    }
}
