using System.Text.Json;

namespace IdiotProof.Engine.Settings;

/// <summary>
/// Shared broker-credential store for the MindAttic family. Mirrors the LLM keyring
/// owned by MindAttic.Legion (<c>%APPDATA%\MindAttic\LLM\providers.json</c>) so any
/// MindAttic app that needs to trade reads the same keys without re-entering them.
///
/// Schema (rich, alphabetized — same as MindAtticCredentialStore):
/// <code>
/// {
///   "alpaca-paper": { "type": "alpaca", "apiKey": "PK...", "secret": "...", "baseUrl": "https://paper-api.alpaca.markets" },
///   "alpaca-live":  { "type": "alpaca", "apiKey": "AK...", "secret": "...", "baseUrl": "https://api.alpaca.markets" }
/// }
/// </code>
///
/// Platform paths (via <see cref="Environment.SpecialFolder.ApplicationData"/>):
///   Windows: %APPDATA%\MindAttic\Brokers\
///   macOS:   ~/.config/MindAttic/Brokers/
///   Linux:   ~/.config/MindAttic/Brokers/
///
/// Override for tests via the <c>MINDATTIC_BROKER_CREDENTIALS</c> environment variable.
/// </summary>
public static class BrokerCredentialStore
{
    public const string EnvVarName = "MINDATTIC_BROKER_CREDENTIALS";
    private const string ProvidersJsonFile = "providers.json";

    public static string CredentialDirectory =>
        Environment.GetEnvironmentVariable(EnvVarName)
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MindAttic", "Brokers");

    public static string ProvidersFilePath => Path.Combine(CredentialDirectory, ProvidersJsonFile);

    public sealed record BrokerCreds(string ApiKey, string Secret, string? BaseUrl);

    public static BrokerCreds? Get(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        if (!File.Exists(ProvidersFilePath)) return null;

        try
        {
            var raw = File.ReadAllText(ProvidersFilePath);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(providerId, out var entry)) return null;
            if (entry.ValueKind != JsonValueKind.Object) return null;

            var apiKey = entry.TryGetProperty("apiKey", out var k) && k.ValueKind == JsonValueKind.String
                ? k.GetString() ?? "" : "";
            var secret = entry.TryGetProperty("secret", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() ?? "" : "";
            var baseUrl = entry.TryGetProperty("baseUrl", out var b) && b.ValueKind == JsonValueKind.String
                ? b.GetString() : null;

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
                return null;

            return new BrokerCreds(apiKey.Trim(), secret.Trim(), baseUrl?.Trim());
        }
        catch
        {
            return null;
        }
    }
}
