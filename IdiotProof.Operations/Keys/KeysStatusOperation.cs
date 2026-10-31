using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Keys;

/// <summary>Shared bits of the keys family: which secret is which, and how a key id is shown.</summary>
internal static class KeyNames
{
    /// <summary>A key id as its first four characters; secrets themselves are never shown at all.</summary>
    public static string Prefix(string? keyId) =>
        string.IsNullOrEmpty(keyId) ? "-" : keyId.Length <= 8 ? "***" : keyId[..4] + "...";

    /// <summary>"alpaca-paper" / "alpaca-live" to paper (true) / live (false); null for anything else.</summary>
    public static bool? AlpacaTarget(string name) => name.Trim().ToLowerInvariant() switch
    {
        "alpaca-paper" or "paper" => true,
        "alpaca-live" or "live" => false,
        _ => null,
    };
}



/// <summary>
/// Which account secrets resolve for the owner and from where (IP-T246): each Alpaca pair, the
/// Claude key and the OAuth tokens, with the Vault entry that holds it, whether a legacy SQL column
/// still carries an old encrypted copy, the Alpaca routing opt-in and the host fallbacks. Shows
/// key-id prefixes only; never a secret.
/// </summary>
[Operation("keys status", Summary = "Which Alpaca/Claude keys resolve for you and from which Vault entry (never shows a secret).", Risk = OperationRisk.Read)]
[Example("idiotproof keys status")]
public sealed class KeysStatusOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var service = ctx.Get<UserKeyService>();
        var vault = service.Vault;
        var keys = await service.GetOrCreateAsync(owner, ctx.CancellationToken);
        var legacy = await service.GetLegacyDbSecretColumnsAsync(owner, ctx.CancellationToken);

        var rows = new[]
        {
            Row("alpaca-paper", "Brokers/" + vault.AlpacaEntry(owner, true), keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey is not null,
                legacy.Contains(nameof(UserApiKeys.AlpacaApiKeyId)) || legacy.Contains(nameof(UserApiKeys.AlpacaApiSecretKey))),
            Row("alpaca-live", "Brokers/" + vault.AlpacaEntry(owner, false), keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey is not null,
                legacy.Contains(nameof(UserApiKeys.AlpacaLiveApiKeyId)) || legacy.Contains(nameof(UserApiKeys.AlpacaLiveApiSecretKey))),
            Row("claude", "Tokens/" + AccountSecretVault.TokenEntryName(owner, AccountToken.Claude), keys.ClaudeApiKey, keys.ClaudeApiKey is not null,
                legacy.Contains(nameof(UserApiKeys.ClaudeApiKey))),
            Row("alpaca-oauth-access", "Tokens/" + AccountSecretVault.TokenEntryName(owner, AccountToken.AlpacaOAuthAccess), null, keys.AlpacaOAuthAccessToken is not null,
                legacy.Contains(nameof(UserApiKeys.AlpacaOAuthAccessToken))),
            Row("alpaca-oauth-refresh", "Tokens/" + AccountSecretVault.TokenEntryName(owner, AccountToken.AlpacaOAuthRefresh), null, keys.AlpacaOAuthRefreshToken is not null,
                legacy.Contains(nameof(UserApiKeys.AlpacaOAuthRefreshToken))),
        };

        var host = ctx.Services.GetService<AppSettings>();
        var optedIn = string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase);
        var data = new
        {
            owner,
            vault_owner = vault.Owner,
            is_vault_owner = vault.Owner == owner,
            alpaca_routing = optedIn ? "alpaca" : "sandbox",
            claude_model = keys.ClaudeModel,
            llm_voting = keys.LlmVotingEnabled,
            secrets = rows,
            legacy_sql_columns = legacy,
            host_alpaca_keys = host is null ? (bool?)null : !string.IsNullOrWhiteSpace(host.AlpacaApiKeyId),
            host_claude_key = host is null ? (bool?)null : !string.IsNullOrWhiteSpace(host.ClaudeApiKey),
        };

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("secret").Column("in vault").Column("key id").Column("vault entry", maxWidth: 64).Column("legacy sql");
        foreach (var r in rows)
            table.Row(r.Secret, r.InVault ? "yes" : "no", r.KeyId, r.VaultEntry, r.LegacySql ? "YES" : "-");

        var block = new KeyValueBlock()
            .Add("Alpaca routing", optedIn ? "alpaca (your orders go to your Alpaca account)" : "sandbox (opt in with `idiotproof keys opt-in alpaca`)")
            .Add("Vault owner", vault.Owner is null ? "unknown (every user gets per-user entries)" : vault.Owner == owner ? "you (canonical alpaca-paper/alpaca-live)" : vault.Owner.Value.ToString())
            .Add("Claude", $"{keys.ClaudeModel}, voting {(keys.LlmVotingEnabled ? "on" : "off")}")
            .Add("Host fallbacks", host is null ? null : $"Alpaca data keys {(data.host_alpaca_keys == true ? "set" : "missing")}, Claude key {(data.host_claude_key == true ? "set" : "missing")}");

        var lines = new List<string> { block.Render(), "", table.Render() };
        if (legacy.Count > 0)
            lines.Add($"\nSQL still holds {legacy.Count} encrypted secret column(s) nothing reads any more. Move them: `idiotproof keys migrate-to-vault` (dry run), then `--apply`.");
        lines.Add($"Vault files: {vault.BrokersFile} and {vault.TokensFile}");
        return OperationResult.Ok(data, string.Join("\n", lines));
    }

    private static SecretStatus Row(string secret, string entry, string? keyId, bool inVault, bool legacySql) =>
        new(secret, entry, inVault, inVault ? KeyNames.Prefix(keyId) : "-", legacySql);

    /// <summary>One secret's status (JSON: secret, vault_entry, in_vault, key_id, legacy_sql).</summary>
    internal sealed record SecretStatus(string Secret, string VaultEntry, bool InVault, string KeyId, bool LegacySql);
}
