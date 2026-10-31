using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Ledger;
using IdiotProof.Core.Trading;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Keys;

public sealed record KeysSetArgs
{
    [Argument(0, Name = "target", Description = "alpaca-paper, alpaca-live or claude.")]
    public string Target { get; init; } = "";

    [Option("key-id", Description = "The Alpaca API key id (PK... for paper, AK... for live). Not used for claude.")]
    public string? KeyId { get; init; }

    [Option("secret", Description = "The Alpaca secret key, or for claude the Claude API key.", Required = true)]
    public string Secret { get; init; } = "";

    [Option("force", Description = "Store even when the key id prefix looks wrong or Alpaca rejects the pair.")]
    public bool Force { get; init; }
}

/// <summary>
/// Stores a key in the owner's own MindAttic.Vault entry (IP-T247, the port of the Monitor's
/// <c>set-keys</c>): an Alpaca pair in the Brokers bucket (the owner's <c>alpaca-paper|live</c>,
/// anyone else's <c>idiotproof-&lt;id&gt;-alpaca-paper|live</c>), a Claude key in Tokens. Before an
/// Alpaca pair is stored its prefix is checked (PK paper, AK live) and it is proven with a
/// read-only <c>GET /v2/account</c> against the matching host; <c>--force</c> skips both. Never
/// writes SQL, never echoes the secret; routing is still off until <c>keys opt-in alpaca</c>.
/// </summary>
[Operation("keys set", Summary = "Store an Alpaca paper/live key pair (checked, then probed) or your Claude key in MindAttic.Vault.", Risk = OperationRisk.Write)]
[Example("idiotproof keys set alpaca-paper --key-id PK...your-paper-key-id --secret <secret>")]
[Example("idiotproof keys set alpaca-live --key-id AK...your-live-key-id --secret <secret>")]
[Example("idiotproof keys set claude --secret <sk-ant-key>")]
public sealed class KeysSetOperation : IOperation<KeysSetArgs>
{
    public async Task<OperationResult> ExecuteAsync(KeysSetArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var service = ctx.Get<UserKeyService>();
        var secret = args.Secret.Trim();
        if (secret.Length == 0) return OperationResult.Usage("--secret is required.");

        if (string.Equals(args.Target.Trim(), "claude", StringComparison.OrdinalIgnoreCase))
        {
            if (!args.Force && !secret.StartsWith("sk-ant-", StringComparison.Ordinal))
                return OperationResult.Usage("That doesn't look like a Claude API key (expected 'sk-ant-...'). Use --force to store it anyway.");
            service.Vault.SetToken(owner, AccountToken.Claude, secret);
            var entry = AccountSecretVault.TokenEntryName(owner, AccountToken.Claude);
            return OperationResult.Ok(new { target = "claude", vault_entry = "Tokens/" + entry, key = KeyNames.Prefix(secret) },
                $"Stored your Claude key ({KeyNames.Prefix(secret)}) in Vault Tokens/{entry}. LLM voting on your strategies uses it from now on.");
        }

        if (KeyNames.AlpacaTarget(args.Target) is not { } isPaper)
            return OperationResult.Usage($"Unknown target '{args.Target}'. Use alpaca-paper, alpaca-live or claude.");
        var keyId = args.KeyId?.Trim() ?? "";
        if (keyId.Length == 0) return OperationResult.Usage("--key-id is required for an Alpaca pair.");
        var mode = isPaper ? "paper" : "live";

        string? account = null, status = null;
        if (!args.Force)
        {
            var prefix = isPaper ? "PK" : "AK";
            if (!keyId.StartsWith(prefix, StringComparison.Ordinal))
                return OperationResult.Usage($"That key id doesn't look like a {mode.ToUpperInvariant()} key (expected '{prefix}...'). " +
                    $"Use alpaca-{(isPaper ? "live" : "paper")} for the other account, or --force to store it anyway.");

            // Prove the pair before storing it: a typo'd or revoked key would otherwise only fail at trade time.
            try
            {
                await using var api = ctx.Get<AlpacaApiFactory>().ForKeys(keyId, secret, isPaper);
                var acct = await api.GetAsync("/v2/account", ct: ctx.CancellationToken);
                account = Str(acct, "account_number") is { } n ? SecretRedactor.MaskAccountNumber(n) : null;
                status = Str(acct, "status");
            }
            catch (AlpacaApiException ex)
            {
                return OperationResult.Fail($"Alpaca {mode} rejected that pair ({(int)ex.Status} {ex.AlpacaMessage}). Nothing was stored. Use --force to store it anyway.");
            }
        }

        service.Vault.SetAlpaca(owner, isPaper, keyId, secret);
        var vaultEntry = "Brokers/" + service.Vault.AlpacaEntry(owner, isPaper);
        var keys = await service.GetOrCreateAsync(owner, ctx.CancellationToken);
        var optedIn = string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase);

        var lines = new List<string>
        {
            $"Stored your {mode} Alpaca pair ({KeyNames.Prefix(keyId)}) in Vault {vaultEntry}.",
            args.Force ? "Not probed (--force)." : $"Probe ok: account {account ?? "?"}, status {status ?? "?"}.",
        };
        if (!optedIn) lines.Add("Routing is still Sandbox: `idiotproof keys opt-in alpaca` sends your orders to this account.");
        if (!isPaper) lines.Add("These are LIVE keys: real money. Only strategies you switch to Live (with a confirmation code) use them.");
        return OperationResult.Ok(new { target = $"alpaca-{mode}", vault_entry = vaultEntry, key_id = KeyNames.Prefix(keyId), probed = !args.Force, account, status, routing = optedIn ? "alpaca" : "sandbox" },
            string.Join("\n", lines));
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
