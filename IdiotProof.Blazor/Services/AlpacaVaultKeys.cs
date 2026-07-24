using Microsoft.Extensions.Configuration;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Reads the MindAttic.Vault "Brokers" bucket's alpaca-paper/alpaca-live entries
/// (<c>%APPDATA%\MindAttic\Brokers\providers.json</c>, surfaced into
/// IConfiguration by AddMindAtticVaultFiles — Brokers is one of Vault's default
/// buckets, already wired with zero extra registration in every IdiotProof
/// host). Per this project's own architecture rule, these override DB-stored
/// UserApiKeys when present.
/// </summary>
public static class AlpacaVaultKeys
{
    public static (string? KeyId, string? Secret) Resolve(IConfiguration configuration, bool isPaper)
    {
        var section = configuration.GetSection($"MindAttic:Vault:Brokers:{(isPaper ? "alpaca-paper" : "alpaca-live")}");
        return (section["apiKey"], section["secret"]);
    }
}
