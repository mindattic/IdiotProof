using System.Text.Json;
using Microsoft.Extensions.Configuration;
using IdiotProof.Engine.Storage;
using IdiotProof.Models;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;

namespace IdiotProof.Engine.Settings;

/// <summary>
/// Global application settings. Instance-based, registered in DI.
/// </summary>
public sealed class AppSettings
{
    // Alpaca
    public string AlpacaApiKeyId { get; set; } = "";
    public string AlpacaApiSecretKey { get; set; } = "";
    public bool AlpacaIsPaper { get; set; } = true;

    // Defaults
    public string DefaultBroker { get; set; } = "Sandbox";
    public string DefaultDataFeed { get; set; } = "Alpaca";
    public string Timezone { get; set; } = "Central Standard Time";

    // Display
    public bool ShowConnectionMessages { get; set; } = true;

    // Auth (SHA-256 of admin password — empty means first-run setup)
    public string AdminPasswordHash { get; set; } = "";

    // AI / LLM Voting
    public string ClaudeApiKey { get; set; } = "";
    public bool LlmVotingEnabled { get; set; } = false;
    public decimal LlmConsensusThreshold { get; set; } = 0.66m; // 66% agreement required
    public int StrategyEvaluationIntervalSeconds { get; set; } = 30;
    public int MaxConcurrentEvaluations { get; set; } = 4;
    public string LlmVoterModel { get; set; } = "claude-sonnet-5";

    // ── Auto-gapper generator (on-demand only — operator CLI `auto-gapper`) ──
    // There is no scheduled trigger; a standardized auto-generation flow is
    // future work (USER_STORIES Epic S). These tune the on-demand scan.
    /// <summary>Minimum gap over previous close (percent) to qualify.</summary>
    public double AutoGapperMinGapPercent { get; set; } = 15;
    /// <summary>Discovery price floor (skip sub-dollar illiquid junk).</summary>
    public double AutoGapperMinPrice { get; set; } = 1;
    /// <summary>Max strategies to arm per run (ranked by conviction).</summary>
    public int AutoGapperMaxCount { get; set; } = 5;
    /// <summary>
    /// Broker routing for armed strategies: "paper" (forced paper, the safe
    /// default), "route" (respect the user's normal routing), or "live".
    /// </summary>
    public string AutoGapperBrokerMode { get; set; } = "paper";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load(IStorageProvider storage)
    {
        storage.EnsureDirectories();
        var path = Path.Combine(storage.SettingsPath, "app-settings.json");
        if (!File.Exists(path)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Silently returning default AppSettings would reset AdminPasswordHash to ""
            // (the first-run sentinel) and allow unauthenticated setup on a corrupt file.
            // Rethrow so the host can abort startup with a visible, actionable error.
            throw;
        }
    }

    public void Save(IStorageProvider storage)
    {
        storage.EnsureDirectories();
        var path = Path.Combine(storage.SettingsPath, "app-settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>
    /// Overlays secrets from environment variables. Azure App Service injects
    /// Key Vault-linked settings as env vars with the same name. Call after Load().
    /// </summary>
    public void OverlayFromEnvironment()
    {
        var alpacaKeyId = Environment.GetEnvironmentVariable("AlpacaApiKeyId");
        if (!string.IsNullOrWhiteSpace(alpacaKeyId)) AlpacaApiKeyId = alpacaKeyId;

        var alpacaSecret = Environment.GetEnvironmentVariable("AlpacaApiSecretKey");
        if (!string.IsNullOrWhiteSpace(alpacaSecret)) AlpacaApiSecretKey = alpacaSecret;

        var claudeKey = Environment.GetEnvironmentVariable("ClaudeApiKey");
        if (!string.IsNullOrWhiteSpace(claudeKey)) ClaudeApiKey = claudeKey;

        // Auto-gapper on-demand tuning knobs (env overrides for the CLI scan).
        var agGap = Environment.GetEnvironmentVariable("IDIOTPROOF_AUTOGAPPER_MINGAP");
        if (double.TryParse(agGap, System.Globalization.CultureInfo.InvariantCulture, out var g)) AutoGapperMinGapPercent = g;
        var agMax = Environment.GetEnvironmentVariable("IDIOTPROOF_AUTOGAPPER_MAX");
        if (int.TryParse(agMax, out var mx)) AutoGapperMaxCount = mx;
        var agBroker = Environment.GetEnvironmentVariable("IDIOTPROOF_AUTOGAPPER_BROKER");
        if (!string.IsNullOrWhiteSpace(agBroker)) AutoGapperBrokerMode = agBroker.ToLowerInvariant();
    }

    /// <summary>
    /// Overlays LLM credentials from the shared MindAttic Vault LLM keyring at
    /// <c>%APPDATA%/MindAttic/LLM/providers.json</c>. This is the canonical
    /// file-store stop for LLM credentials across all MindAttic applications —
    /// call it after disk + env so it takes precedence, but BEFORE
    /// <see cref="OverlayFromConfiguration"/> so production cloud secrets win.
    /// <para>
    /// Constructs a fresh <see cref="LlmCredentialStore"/> per call so the
    /// <c>MINDATTIC_LLM_CREDENTIALS</c> env-var override is re-evaluated each
    /// time, mirroring <see cref="OverlayFromBrokerCredentials"/>.
    /// </para>
    /// </summary>
    public void OverlayFromMindAtticCredentials()
    {
        var store = new LlmCredentialStore(
            Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar)
            ?? VaultPaths.RoamingBucket(LlmCredentialStore.Bucket));
        var claudeKey = store.GetKey("claude");
        if (!string.IsNullOrWhiteSpace(claudeKey)) ClaudeApiKey = claudeKey;
    }

    /// <summary>
    /// Overlays Alpaca credentials from the shared MindAttic broker keyring at
    /// <c>%APPDATA%/MindAttic/Brokers/providers.json</c>. Picks <c>alpaca-paper</c>
    /// or <c>alpaca-live</c> based on <see cref="AlpacaIsPaper"/>. Mirrors how
    /// <see cref="OverlayFromMindAtticCredentials"/> resolves LLM keys.
    /// <para>
    /// Constructs a fresh <see cref="BrokerCredentialStore"/> per call so the
    /// <c>MINDATTIC_BROKER_CREDENTIALS</c> env-var override is re-evaluated
    /// each time (matches the pre-Vault behaviour the test suite depends on).
    /// </para>
    /// </summary>
    public void OverlayFromBrokerCredentials()
    {
        var providerId = AlpacaIsPaper ? "alpaca-paper" : "alpaca-live";
        var store = new BrokerCredentialStore(
            Environment.GetEnvironmentVariable(BrokerCredentialStore.DirectoryEnvVar)
            ?? VaultPaths.RoamingBucket(BrokerCredentialStore.Bucket));
        var creds = store.GetBrokerCreds(providerId);
        if (creds is null) return;

        AlpacaApiKeyId = creds.ApiKey;
        AlpacaApiSecretKey = creds.Secret;
    }

    /// <summary>
    /// Cloud-native overlay (Phase B.2). Layers IConfiguration values
    /// (User Secrets, App Service Application Settings, Azure Key Vault
    /// references) on top of every other credential source. Call AFTER the
    /// other overlays so IConfiguration always wins. Reads from the standard
    /// <see cref="VaultConfigurationKeys.LlmSection"/> and
    /// <see cref="VaultConfigurationKeys.BrokersSection"/> paths.
    /// </summary>
    public void OverlayFromConfiguration(IConfiguration config)
    {
        if (config is null) return;

        var claude = config[VaultConfigurationKeys.ProviderApiKeyPath(
            VaultConfigurationKeys.LlmSection, "claude")];
        if (!string.IsNullOrWhiteSpace(claude)) ClaudeApiKey = claude;

        var providerId = AlpacaIsPaper ? "alpaca-paper" : "alpaca-live";
        var apiKey = config[$"{VaultConfigurationKeys.BrokersSection}:{providerId}:apiKey"];
        var secret = config[$"{VaultConfigurationKeys.BrokersSection}:{providerId}:secret"];
        if (!string.IsNullOrWhiteSpace(apiKey)) AlpacaApiKeyId     = apiKey;
        if (!string.IsNullOrWhiteSpace(secret)) AlpacaApiSecretKey = secret;
    }
}
