using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Engine.Settings;

namespace IdiotProof.Operations.Streaming;

/// <summary>An Alpaca key pair for a websocket's auth message. Never printed.</summary>
public sealed record AlpacaStreamKeys(string KeyId, string Secret)
{
    public override string ToString() => "AlpacaStreamKeys(redacted)";
}

/// <summary>
/// The key pairs the Hub's Alpaca websockets authenticate with, behind the same rules as
/// <see cref="AlpacaApiFactory"/>: the trade-updates stream of an account needs the owner's opt-in
/// (DefaultBroker = alpaca) and that account's own Vault pair; market data streams use the owner's
/// paper pair when usable, else the Hub's own. Throws <see cref="AlpacaAccessException"/> with the
/// reason. Tests subclass it and return fixed keys (pass null! for the constructor dependencies).
/// </summary>
public class AlpacaStreamCredentials(UserKeyService userKeys, AppSettings settings)
{
    public virtual async Task<AlpacaStreamKeys> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default)
    {
        if (mode == BrokerMode.Sandbox)
            throw new AlpacaAccessException("Sandbox is IdiotProof's simulator, not an Alpaca account; use --mode paper or --mode live.");
        var isPaper = mode == BrokerMode.Paper;
        var keys = await userKeys.GetOrCreateAsync(ownerUserId, ct);
        if (!string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase))
            throw new AlpacaAccessException("Alpaca routing is off for this user (still on Sandbox). Opt in first (API Keys page, or `idiotproof keys opt-in alpaca`).");
        var (keyId, secret) = isPaper ? (keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey) : (keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey);
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
            throw new AlpacaAccessException(
                $"No {(isPaper ? "paper" : "live")} Alpaca keys in Vault. Set them with `idiotproof keys set alpaca-{(isPaper ? "paper" : "live")}` or on the API Keys page.");
        return new AlpacaStreamKeys(keyId, secret);
    }

    public virtual async Task<AlpacaStreamKeys> ForDataAsync(Guid? ownerUserId, CancellationToken ct = default)
    {
        if (ownerUserId is { } owner)
        {
            try { return await ForOwnerAsync(owner, BrokerMode.Paper, ct); }
            catch (AlpacaAccessException) { }
        }
        if (string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(settings.AlpacaApiSecretKey))
            throw new AlpacaAccessException("No Alpaca keys resolved for market data (owner or Hub settings).");
        return new AlpacaStreamKeys(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey);
    }
}
