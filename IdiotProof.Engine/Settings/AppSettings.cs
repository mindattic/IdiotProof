using System.Text.Json;
using IdiotProof.Engine.Storage;
using IdiotProof.Models;
using MindAttic.Legion;

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

    // Polygon
    public string PolygonApiKey { get; set; } = "";

    // Defaults
    public string DefaultBroker { get; set; } = "Sandbox";
    public string DefaultDataFeed { get; set; } = "Polygon";
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
    public string LlmVoterModel { get; set; } = "claude-sonnet-4-6";

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
        catch
        {
            return new AppSettings();
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

        var polygonKey = Environment.GetEnvironmentVariable("PolygonApiKey");
        if (!string.IsNullOrWhiteSpace(polygonKey)) PolygonApiKey = polygonKey;

        var claudeKey = Environment.GetEnvironmentVariable("ClaudeApiKey");
        if (!string.IsNullOrWhiteSpace(claudeKey)) ClaudeApiKey = claudeKey;
    }

    /// <summary>
    /// Overlays LLM credentials from the shared MindAttic.Legion credential store at
    /// <c>%APPDATA%/MindAttic/LLM/providers.json</c>. This is the canonical first stop
    /// for LLM credentials across all MindAttic applications — call it LAST in the
    /// overlay chain so it takes precedence over both disk config and env vars.
    /// </summary>
    public void OverlayFromMindAtticCredentials()
    {
        var claudeKey = MindAtticCredentialStore.GetKey("claude");
        if (!string.IsNullOrWhiteSpace(claudeKey)) ClaudeApiKey = claudeKey;
    }

    /// <summary>
    /// Overlays Alpaca credentials from the shared MindAttic broker keyring at
    /// <c>%APPDATA%/MindAttic/Brokers/providers.json</c>. Picks <c>alpaca-paper</c>
    /// or <c>alpaca-live</c> based on <see cref="AlpacaIsPaper"/>. Mirrors how
    /// <see cref="OverlayFromMindAtticCredentials"/> resolves LLM keys.
    /// </summary>
    public void OverlayFromBrokerCredentials()
    {
        var providerId = AlpacaIsPaper ? "alpaca-paper" : "alpaca-live";
        var creds = BrokerCredentialStore.Get(providerId);
        if (creds is null) return;

        AlpacaApiKeyId = creds.ApiKey;
        AlpacaApiSecretKey = creds.Secret;
    }
}
