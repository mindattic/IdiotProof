using System.Security.Cryptography;
using MindAttic.Vault.Credentials;

namespace IdiotProof.Operations;

/// <summary>
/// The shared secret the CLI and MCP server present on every Hub call (<see cref="HubWire.KeyHeader"/>).
/// Loopback-only binding keeps other machines out; the key keeps other local processes (a browser
/// page doing a fetch to 127.0.0.1, another user's session) from driving trades. It lives in the
/// per-user MindAttic Vault Tokens bucket, is generated on the Hub's first start, and can be
/// overridden with <see cref="HubWire.KeyEnvVar"/> (tests, services running as another account).
/// </summary>
public static class HubKey
{
    /// <summary>Reads the key, creating and storing one when none exists yet.</summary>
    public static string LoadOrCreate()
    {
        var fromEnv = Environment.GetEnvironmentVariable(HubWire.KeyEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        var store = TokenStore.ForBucket("Tokens");
        var existing = store.Get(HubWire.KeyTokenName);
        if (!string.IsNullOrWhiteSpace(existing)) return existing.Trim();

        var created = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        store.Set(HubWire.KeyTokenName, created);
        return created;
    }

    /// <summary>The key a client should send: env override, else the Vault entry (null when the Hub never ran).</summary>
    public static string? ForClient()
    {
        var fromEnv = Environment.GetEnvironmentVariable(HubWire.KeyEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();
        return TokenStore.ForBucket("Tokens").Get(HubWire.KeyTokenName)?.Trim();
    }

    /// <summary>Constant-time comparison so the key can't be guessed byte by byte from timings.</summary>
    public static bool Matches(string expected, string? presented) =>
        presented is not null &&
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(presented.Trim()));
}
