using System.Collections.Concurrent;
using IdiotProof.Blazor.Data;
using IdiotProof.Brokers;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Which broker a user's orders route to. Pure decision, separated from the
/// cached resolver so it is unit-testable without Data Protection or SQL.
/// </summary>
public enum BrokerChoice
{
    /// <summary>User opted into Alpaca and supplied both keys — use THEIR account.</summary>
    UserAlpaca,

    /// <summary>No usable per-user broker config — fall through to the host's global router (Sandbox default, IP-LAW-3).</summary>
    GlobalDefault
}

/// <summary>
/// Per-user broker routing for the Monitor (IP-A9). Every user's orders go to
/// THEIR broker account: a user who configured Alpaca keys on the API Keys
/// page (encrypted <see cref="UserApiKeys"/>, shared Data Protection key ring)
/// trades their own account with their own paper/live flag; everyone else
/// falls through to the host's global <see cref="BrokerRouter"/> — whose
/// default is Sandbox (IP-LAW-3), so a missing or undecryptable key can never
/// silently route one user's order into another user's account.
///
/// Clients are cached per user for <see cref="CacheTtl"/> and rebuilt when the
/// decrypted key fingerprint changes, so key rotations in the UI take effect
/// within a few minutes without restarting the console.
/// </summary>
public sealed class UserBrokerResolver(
    UserKeyService userKeys,
    BrokerRouter globalRouter,
    ILogger<UserBrokerResolver> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, CacheEntry> cache = new();

    private sealed record CacheEntry(IBrokerClient Client, string Fingerprint, DateTime CachedUtc);

    /// <summary>The pure routing rule. Testable with a plain <see cref="UserApiKeys"/>.</summary>
    public static BrokerChoice Choose(UserApiKeys keys) =>
        string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(keys.AlpacaApiKeyId)
        && !string.IsNullOrWhiteSpace(keys.AlpacaApiSecretKey)
            ? BrokerChoice.UserAlpaca
            : BrokerChoice.GlobalDefault;

    public async Task<IBrokerClient> ResolveAsync(Guid userId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(userId, out var hit) && DateTime.UtcNow - hit.CachedUtc < CacheTtl)
            return hit.Client;

        var keys = await userKeys.GetOrCreateAsync(userId, ct);
        var choice = Choose(keys);

        if (choice == BrokerChoice.GlobalDefault)
        {
            if (string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase))
                logger.LogWarning("User {UserId} prefers Alpaca but has no usable keys — routing to the global default ({Broker}).",
                    userId, globalRouter.GetActiveBroker().BrokerType);
            var global = globalRouter.GetActiveBroker();
            cache[userId] = new CacheEntry(global, "global", DateTime.UtcNow);
            return global;
        }

        // Rebuild only when the key material actually changed.
        var fingerprint = $"{keys.AlpacaApiKeyId}|{keys.AlpacaApiSecretKey!.Length}|{keys.AlpacaIsPaper}";
        if (hit is not null && hit.Fingerprint == fingerprint)
        {
            cache[userId] = hit with { CachedUtc = DateTime.UtcNow };
            return hit.Client;
        }

        var client = new AlpacaBrokerClient(keys.AlpacaApiKeyId!, keys.AlpacaApiSecretKey!, keys.AlpacaIsPaper);
        cache[userId] = new CacheEntry(client, fingerprint, DateTime.UtcNow);
        logger.LogInformation("User {UserId} orders route to their own Alpaca ({Mode}).",
            userId, keys.AlpacaIsPaper ? "paper" : "LIVE");
        return client;
    }
}
