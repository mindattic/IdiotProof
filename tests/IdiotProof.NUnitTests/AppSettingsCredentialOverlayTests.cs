using IdiotProof.Engine.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using MindAttic.Vault.Credentials;

namespace IdiotProof.NUnitTests;

/// <summary>
/// Minimal flat-key IConfiguration for tests. We avoid taking on the
/// Microsoft.Extensions.Configuration.Memory NuGet (which doesn't have a
/// version-aligned 10.x release yet) by hand-rolling exactly the surface
/// the AppSettings overlay touches: indexer-based key lookup. No support
/// for sub-sections, change tokens, or providers — none of which the
/// overlay code uses.
/// </summary>
internal sealed class FlatKeyConfiguration : IConfiguration
{
    private readonly IDictionary<string, string?> values;

    public FlatKeyConfiguration(IDictionary<string, string?> values) => this.values = values;

    public string? this[string key]
    {
        get => values.TryGetValue(key, out var v) ? v : null;
        set => values[key] = value;
    }

    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();
    public IChangeToken GetReloadToken() => throw new NotSupportedException();
}

/// <summary>
/// Coverage for the three AppSettings overlay paths that front MindAttic.Vault:
///   • OverlayFromMindAtticCredentials — file-store read for the LLM keyring
///   • OverlayFromBrokerCredentials    — file-store read for the broker keyring
///   • OverlayFromConfiguration        — IConfiguration overlay (User Secrets,
///                                        App Service Application Settings,
///                                        Azure Key Vault references)
///
/// The file-store tests redirect Vault to a temporary directory via the
/// MINDATTIC_LLM_CREDENTIALS / MINDATTIC_BROKER_CREDENTIALS env vars so they
/// never touch the developer's real %APPDATA%. The fresh-construct contract
/// (env-var change between calls is honoured at runtime) is verified
/// explicitly because that is the reason AppSettings does NOT use
/// LlmCredentialStore.Default / BrokerCredentialStore.Default.
/// </summary>
[TestFixture]
public class AppSettingsCredentialOverlayTests
{
    private string llmDir = "";
    private string brokerDir = "";
    private string? originalLlmDirEnv;
    private string? originalBrokerDirEnv;

    [SetUp]
    public void SetUp()
    {
        llmDir    = Path.Combine(Path.GetTempPath(), "idiotproof-vault-llm-"    + Guid.NewGuid().ToString("N"));
        brokerDir = Path.Combine(Path.GetTempPath(), "idiotproof-vault-broker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(llmDir);
        Directory.CreateDirectory(brokerDir);

        originalLlmDirEnv    = Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar);
        originalBrokerDirEnv = Environment.GetEnvironmentVariable(BrokerCredentialStore.DirectoryEnvVar);
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar,    llmDir);
        Environment.SetEnvironmentVariable(BrokerCredentialStore.DirectoryEnvVar, brokerDir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar,    originalLlmDirEnv);
        Environment.SetEnvironmentVariable(BrokerCredentialStore.DirectoryEnvVar, originalBrokerDirEnv);
        TryDelete(llmDir);
        TryDelete(brokerDir);
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best effort — temp cleanup. */ }
    }

    private static void WriteProvidersJson(string dir, string contents) =>
        File.WriteAllText(Path.Combine(dir, "providers.json"), contents);

    // ──────────────────────────────────────────────────────────────────────────
    // OverlayFromMindAtticCredentials — Vault LLM keyring
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void OverlayFromMindAtticCredentials_PopulatesClaudeKey_FromVaultProvidersJson()
    {
        WriteProvidersJson(llmDir, """
            { "claude": { "type": "anthropic", "apiKey": "sk-ant-vault-llm-redirect" } }
            """);

        var settings = new AppSettings();
        settings.OverlayFromMindAtticCredentials();

        Assert.That(settings.ClaudeApiKey, Is.EqualTo("sk-ant-vault-llm-redirect"));
    }

    [Test]
    public void OverlayFromMindAtticCredentials_LeavesKeyUnchanged_WhenDirectoryEmpty()
    {
        // Empty bucket directory — no providers.json, no per-provider .key files.
        var settings = new AppSettings { ClaudeApiKey = "preexisting-from-disk-config" };
        settings.OverlayFromMindAtticCredentials();

        Assert.That(settings.ClaudeApiKey, Is.EqualTo("preexisting-from-disk-config"));
    }

    [Test]
    public void OverlayFromMindAtticCredentials_HonorsRuntimeEnvVarRedirect_BetweenCalls()
    {
        // Verifies the fresh-construct pattern: AppSettings must NOT cache the
        // store at type-load (LlmCredentialStore.Default does that), otherwise
        // tests that swap MINDATTIC_LLM_CREDENTIALS partway through can't see
        // the new directory.
        WriteProvidersJson(llmDir, """
            { "claude": { "type": "anthropic", "apiKey": "first-dir-key" } }
            """);

        var settings = new AppSettings();
        settings.OverlayFromMindAtticCredentials();
        Assert.That(settings.ClaudeApiKey, Is.EqualTo("first-dir-key"));

        var secondDir = Path.Combine(Path.GetTempPath(), "idiotproof-vault-llm-2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(secondDir);
        try
        {
            WriteProvidersJson(secondDir, """
                { "claude": { "type": "anthropic", "apiKey": "second-dir-key" } }
                """);
            Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, secondDir);

            settings.OverlayFromMindAtticCredentials();
            Assert.That(settings.ClaudeApiKey, Is.EqualTo("second-dir-key"),
                "Overlay must construct a fresh LlmCredentialStore each call so MINDATTIC_LLM_CREDENTIALS changes are honoured.");
        }
        finally
        {
            TryDelete(secondDir);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // OverlayFromBrokerCredentials — Vault broker keyring
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void OverlayFromBrokerCredentials_SelectsPaperProvider_WhenIsPaperTrue()
    {
        WriteProvidersJson(brokerDir, """
            {
              "alpaca-paper": { "type": "alpaca", "apiKey": "PK-PAPER", "secret": "SECRET-PAPER" },
              "alpaca-live":  { "type": "alpaca", "apiKey": "AK-LIVE",  "secret": "SECRET-LIVE"  }
            }
            """);

        var settings = new AppSettings { AlpacaIsPaper = true };
        settings.OverlayFromBrokerCredentials();

        Assert.That(settings.AlpacaApiKeyId,     Is.EqualTo("PK-PAPER"));
        Assert.That(settings.AlpacaApiSecretKey, Is.EqualTo("SECRET-PAPER"));
    }

    [Test]
    public void OverlayFromBrokerCredentials_SelectsLiveProvider_WhenIsPaperFalse()
    {
        WriteProvidersJson(brokerDir, """
            {
              "alpaca-paper": { "type": "alpaca", "apiKey": "PK-PAPER", "secret": "SECRET-PAPER" },
              "alpaca-live":  { "type": "alpaca", "apiKey": "AK-LIVE",  "secret": "SECRET-LIVE"  }
            }
            """);

        var settings = new AppSettings { AlpacaIsPaper = false };
        settings.OverlayFromBrokerCredentials();

        Assert.That(settings.AlpacaApiKeyId,     Is.EqualTo("AK-LIVE"));
        Assert.That(settings.AlpacaApiSecretKey, Is.EqualTo("SECRET-LIVE"));
    }

    [Test]
    public void OverlayFromBrokerCredentials_LeavesValuesUnchanged_WhenProviderMissing()
    {
        // alpaca-live is missing — switching to live mode without keys must not
        // wipe out whatever was already on the settings (e.g. from disk config).
        WriteProvidersJson(brokerDir, """
            { "alpaca-paper": { "type": "alpaca", "apiKey": "PK-PAPER", "secret": "SECRET-PAPER" } }
            """);

        var settings = new AppSettings
        {
            AlpacaIsPaper      = false,
            AlpacaApiKeyId     = "preexisting-key",
            AlpacaApiSecretKey = "preexisting-secret"
        };
        settings.OverlayFromBrokerCredentials();

        Assert.That(settings.AlpacaApiKeyId,     Is.EqualTo("preexisting-key"));
        Assert.That(settings.AlpacaApiSecretKey, Is.EqualTo("preexisting-secret"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // OverlayFromConfiguration — IConfiguration / Vault standard schema
    // ──────────────────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new FlatKeyConfiguration(values);

    [Test]
    public void OverlayFromConfiguration_PopulatesClaudeKey_FromMindAtticVaultLlmSchema()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MindAttic:Vault:LLM:claude:apiKey"] = "sk-ant-from-iconfiguration"
        });

        var settings = new AppSettings();
        settings.OverlayFromConfiguration(config);

        Assert.That(settings.ClaudeApiKey, Is.EqualTo("sk-ant-from-iconfiguration"));
    }

    [Test]
    public void OverlayFromConfiguration_SelectsPaperBrokerCreds_WhenIsPaperTrue()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MindAttic:Vault:Brokers:alpaca-paper:apiKey"] = "PK-PAPER-CFG",
            ["MindAttic:Vault:Brokers:alpaca-paper:secret"] = "SECRET-PAPER-CFG",
            ["MindAttic:Vault:Brokers:alpaca-live:apiKey"]  = "AK-LIVE-CFG",
            ["MindAttic:Vault:Brokers:alpaca-live:secret"]  = "SECRET-LIVE-CFG"
        });

        var settings = new AppSettings { AlpacaIsPaper = true };
        settings.OverlayFromConfiguration(config);

        Assert.That(settings.AlpacaApiKeyId,     Is.EqualTo("PK-PAPER-CFG"));
        Assert.That(settings.AlpacaApiSecretKey, Is.EqualTo("SECRET-PAPER-CFG"));
    }

    [Test]
    public void OverlayFromConfiguration_SelectsLiveBrokerCreds_WhenIsPaperFalse()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MindAttic:Vault:Brokers:alpaca-paper:apiKey"] = "PK-PAPER-CFG",
            ["MindAttic:Vault:Brokers:alpaca-paper:secret"] = "SECRET-PAPER-CFG",
            ["MindAttic:Vault:Brokers:alpaca-live:apiKey"]  = "AK-LIVE-CFG",
            ["MindAttic:Vault:Brokers:alpaca-live:secret"]  = "SECRET-LIVE-CFG"
        });

        var settings = new AppSettings { AlpacaIsPaper = false };
        settings.OverlayFromConfiguration(config);

        Assert.That(settings.AlpacaApiKeyId,     Is.EqualTo("AK-LIVE-CFG"));
        Assert.That(settings.AlpacaApiSecretKey, Is.EqualTo("SECRET-LIVE-CFG"));
    }

    [Test]
    public void OverlayFromConfiguration_LeavesValuesUnchanged_WhenSectionMissing()
    {
        // Empty configuration — must NOT clobber whatever was overlaid by the
        // file-store steps that ran earlier in the chain.
        var config = BuildConfig(new Dictionary<string, string?>());

        var settings = new AppSettings
        {
            ClaudeApiKey       = "from-file-store",
            AlpacaApiKeyId     = "from-file-store-id",
            AlpacaApiSecretKey = "from-file-store-secret"
        };
        settings.OverlayFromConfiguration(config);

        Assert.That(settings.ClaudeApiKey,       Is.EqualTo("from-file-store"));
        Assert.That(settings.AlpacaApiKeyId,     Is.EqualTo("from-file-store-id"));
        Assert.That(settings.AlpacaApiSecretKey, Is.EqualTo("from-file-store-secret"));
    }

    [Test]
    public void OverlayFromConfiguration_WinsOverPreviousOverlays_WhenBothSet()
    {
        // Documents the "later wins" contract: the IConfiguration overlay runs
        // last and must override anything the file-store overlays produced.
        WriteProvidersJson(llmDir, """
            { "claude": { "type": "anthropic", "apiKey": "from-vault-file" } }
            """);

        var settings = new AppSettings();
        settings.OverlayFromMindAtticCredentials();
        Assume.That(settings.ClaudeApiKey, Is.EqualTo("from-vault-file"));

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MindAttic:Vault:LLM:claude:apiKey"] = "from-iconfiguration-overrides"
        });
        settings.OverlayFromConfiguration(config);

        Assert.That(settings.ClaudeApiKey, Is.EqualTo("from-iconfiguration-overrides"));
    }
}
