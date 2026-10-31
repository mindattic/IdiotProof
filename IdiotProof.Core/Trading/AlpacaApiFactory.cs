using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Engine.Settings;
using Microsoft.Extensions.Configuration;

namespace IdiotProof.Core.Trading;

/// <summary>Why an owner's Alpaca account can't be reached (no opt-in, no keys, undecryptable keys).</summary>
public sealed class AlpacaAccessException(string message) : Exception(message);

/// <summary>
/// Builds an <see cref="AlpacaApi"/> for an owner's Paper or Live account, behind the same consent
/// gate and key precedence as <see cref="UserBrokerResolver"/>: the owner must have opted into
/// Alpaca routing (DefaultBroker = alpaca, IP-LAW-3), and a complete MindAttic.Vault Brokers pair
/// wins over the DB-stored pair. Market data needs no consent: <see cref="ForDataAsync"/> falls back
/// to the Hub's own keys. Callers dispose the returned client. Tests subclass it and override both
/// methods to hand back an <see cref="AlpacaApi"/> over a scripted <see cref="HttpMessageHandler"/>.
/// </summary>
public class AlpacaApiFactory(UserKeyService userKeys, IConfiguration configuration, AppSettings settings)
{
    /// <summary>The owner's Trading API client for <paramref name="mode"/> (Paper or Live). Throws <see cref="AlpacaAccessException"/> with the reason.</summary>
    public virtual async Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default)
    {
        if (mode == BrokerMode.Sandbox)
            throw new AlpacaAccessException("Sandbox is IdiotProof's simulator, not an Alpaca account; use --mode paper or --mode live.");
        var isPaper = mode == BrokerMode.Paper;
        var keys = await userKeys.GetOrCreateAsync(ownerUserId, ct);
        if (!string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase))
            throw new AlpacaAccessException("Alpaca routing is off for this user (still on Sandbox). Opt in first (API Keys page, or `idiotproof keys opt-in alpaca`).");

        var (vaultKeyId, vaultSecret) = AlpacaVaultKeys.Resolve(configuration, isPaper);
        var (keyId, secret) = !string.IsNullOrWhiteSpace(vaultKeyId) && !string.IsNullOrWhiteSpace(vaultSecret)
            ? (vaultKeyId, vaultSecret)
            : isPaper ? (keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey) : (keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey);
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
        {
            var (paperStored, liveStored) = await userKeys.GetKeyPresenceAsync(ownerUserId, ct);
            throw new AlpacaAccessException((isPaper ? paperStored : liveStored)
                ? "The key is stored but could not be decrypted (DataProtection key-ring mismatch)."
                : $"No {(isPaper ? "paper" : "live")} Alpaca keys configured.");
        }
        return new AlpacaApi(keyId, secret, isPaper);
    }

    /// <summary>
    /// A client for the Market Data API: the owner's paper keys when usable, else the Hub's own
    /// (settings chain) keys. Throws <see cref="AlpacaAccessException"/> when neither exists.
    /// </summary>
    public virtual async Task<AlpacaApi> ForDataAsync(Guid? ownerUserId = null, CancellationToken ct = default)
    {
        if (ownerUserId is { } owner)
        {
            try { return await ForOwnerAsync(owner, BrokerMode.Paper, ct); }
            catch (AlpacaAccessException) { }
        }
        if (string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(settings.AlpacaApiSecretKey))
            throw new AlpacaAccessException("No Alpaca keys resolved for market data (owner or Hub settings).");
        return new AlpacaApi(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, settings.AlpacaIsPaper);
    }
}
