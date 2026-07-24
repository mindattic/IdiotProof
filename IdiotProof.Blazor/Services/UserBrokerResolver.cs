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
/// Per-user, per-strategy broker routing for the Monitor (IP-A9). Each strategy
/// declares its own BrokerMode ("Paper" | "Live" | "Sandbox") which overrides the
/// global UserApiKeys.AlpacaIsPaper flag. "Paper" and "Live" use the user's own
/// Alpaca account with isPaper forced accordingly; "Sandbox" always routes to the
/// global sandbox fallback (IP-LAW-3).
///
/// Clients are cached per (userId, mode) for <see cref="CacheTtl"/> and rebuilt
/// when the decrypted key fingerprint changes, so key rotations in the UI take
/// effect within a few minutes without restarting the console.
/// </summary>
public sealed class UserBrokerResolver(
    UserKeyService userKeys,
    BrokerRouter globalRouter,
    ILogger<UserBrokerResolver> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Cache keyed by (userId, mode) so Paper and Live strategies for the same user
    // each get their own AlpacaBrokerClient with the correct isPaper flag.
    private readonly ConcurrentDictionary<(Guid, string), CacheEntry> cache = new();

    private sealed record CacheEntry(IBrokerClient Client, string Fingerprint, DateTime CachedUtc);

    /// <summary>The pure routing rule. Testable with a plain <see cref="UserApiKeys"/>.</summary>
    /// <param name="isPaper">Which key pair must be present — Paper and Live are separate
    /// Alpaca accounts, so a user can have one configured without the other.</param>
    public static BrokerChoice Choose(UserApiKeys keys, bool isPaper = true) =>
        string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase)
        && HasPair(keys, isPaper)
            ? BrokerChoice.UserAlpaca
            : BrokerChoice.GlobalDefault;

    private static bool HasPair(UserApiKeys keys, bool isPaper) => isPaper
        ? !string.IsNullOrWhiteSpace(keys.AlpacaApiKeyId) && !string.IsNullOrWhiteSpace(keys.AlpacaApiSecretKey)
        : !string.IsNullOrWhiteSpace(keys.AlpacaLiveApiKeyId) && !string.IsNullOrWhiteSpace(keys.AlpacaLiveApiSecretKey);

    /// <summary>
    /// Resolves the broker for <paramref name="userId"/> honouring the per-strategy
    /// <paramref name="strategyBrokerMode"/>: "Paper" forces isPaper=true,
    /// "Live" forces isPaper=false, "Sandbox" routes to the global default.
    /// </summary>
    public async Task<IBrokerClient> ResolveAsync(
        Guid userId,
        string strategyBrokerMode = "Paper",
        CancellationToken ct = default)
    {
        var mode = strategyBrokerMode.ToLowerInvariant(); // normalise to "paper"|"live"|"sandbox"
        var cacheKey = (userId, mode);

        if (cache.TryGetValue(cacheKey, out var hit) && DateTime.UtcNow - hit.CachedUtc < CacheTtl)
            return hit.Client;

        // Sandbox mode: always route to global default (IP-LAW-3).
        if (mode == "sandbox")
        {
            var sandbox = globalRouter.GetActiveBroker();
            cache[cacheKey] = new CacheEntry(sandbox, "global", DateTime.UtcNow);
            DisposeReplacedClient(hit, userId);
            return sandbox;
        }

        var keys = await userKeys.GetOrCreateAsync(userId, ct);

        // Strategy-level mode: "live" forces isPaper=false; anything else is paper.
        // Paper and Live are separate Alpaca accounts with separate key pairs.
        var isPaper = mode != "live";
        var choice = Choose(keys, isPaper);

        if (choice == BrokerChoice.GlobalDefault)
        {
            if (string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase))
                logger.LogWarning(
                    "User {UserId} prefers Alpaca but has no usable {Mode} keys — routing to the global default ({Broker}).",
                    userId, isPaper ? "paper" : "live", globalRouter.GetActiveBroker().BrokerType);
            var global = globalRouter.GetActiveBroker();
            cache[cacheKey] = new CacheEntry(global, "global", DateTime.UtcNow);
            DisposeReplacedClient(hit, userId);
            return global;
        }

        var (keyId, secret) = isPaper
            ? (keys.AlpacaApiKeyId!, keys.AlpacaApiSecretKey!)
            : (keys.AlpacaLiveApiKeyId!, keys.AlpacaLiveApiSecretKey!);

        var fingerprint = $"{keyId}|{secret.Length}|{isPaper}";
        if (hit is not null && hit.Fingerprint == fingerprint)
        {
            cache[cacheKey] = hit with { CachedUtc = DateTime.UtcNow };
            return hit.Client;
        }

        var client = new AlpacaBrokerClient(keyId, secret, isPaper);
        cache[cacheKey] = new CacheEntry(client, fingerprint, DateTime.UtcNow);
        logger.LogInformation(
            "User {UserId} orders route to their own Alpaca ({Mode}) via strategy mode {StrategyMode}.",
            userId, isPaper ? "paper" : "LIVE", strategyBrokerMode);

        DisposeReplacedClient(hit, userId);

        return client;
    }

    /// <summary>
    /// Disposes a cache entry's client when it is being replaced. The global
    /// router's brokers are never disposed here (they're shared, and not
    /// entered into the cache as disposable-owned entries in the first place
    /// — only per-user Alpaca clients carry a non-"global" fingerprint).
    /// </summary>
    private void DisposeReplacedClient(CacheEntry? replaced, Guid userId)
    {
        if (replaced is null || replaced.Fingerprint == "global") return;
        if (replaced.Client is IAsyncDisposable disposable)
            _ = disposable.DisposeAsync().AsTask().ContinueWith(
                t => logger.LogWarning(t.Exception, "Failed disposing replaced broker client for user {UserId}.", userId),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }
}
