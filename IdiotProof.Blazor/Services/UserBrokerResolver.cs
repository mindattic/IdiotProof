using System.Collections.Concurrent;
using IdiotProof.Blazor.Data;
using IdiotProof.Brokers;
using Microsoft.Extensions.Configuration;

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
    IConfiguration configuration,
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
            SetCache(cacheKey, new CacheEntry(sandbox, "global", DateTime.UtcNow), userId);
            return sandbox;
        }

        var keys = await userKeys.GetOrCreateAsync(userId, ct);

        // Strategy-level mode: "live" forces isPaper=false; anything else is paper.
        // Paper and Live are separate Alpaca accounts with separate key pairs.
        var isPaper = mode != "live";

        // MindAttic.Vault's Brokers bucket (alpaca-paper/alpaca-live in
        // %APPDATA%\MindAttic\Brokers\providers.json) overrides DB-stored
        // UserApiKeys when present — the documented architecture rule, wired
        // in here for the first time. Falls back to the DB-decrypted pair
        // when Vault doesn't have this mode's keys.
        var (vaultKeyId, vaultSecret) = AlpacaVaultKeys.Resolve(configuration, isPaper);
        var usingVault = !string.IsNullOrWhiteSpace(vaultKeyId) && !string.IsNullOrWhiteSpace(vaultSecret);
        var (keyId, secret) = usingVault
            ? (vaultKeyId!, vaultSecret!)
            : isPaper
                ? (keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey)
                : (keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey);

        // Vault supplies the CREDENTIAL PAIR, not the opt-in — a user who has
        // never flipped "Route my orders to this Alpaca account" (DefaultBroker
        // stays "Sandbox") must not be silently upgraded to a real account just
        // because a Brokers/providers.json happens to exist on the machine
        // (IP-LAW-3: Sandbox is the always-safe default).
        var hasUsableKeys = string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(secret);

        if (!hasUsableKeys)
        {
            if (string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase))
                logger.LogWarning(
                    "User {UserId} prefers Alpaca but has no usable {Mode} keys — routing to the global default ({Broker}).",
                    userId, isPaper ? "paper" : "live", globalRouter.GetActiveBroker().BrokerType);
            var global = globalRouter.GetActiveBroker();
            SetCache(cacheKey, new CacheEntry(global, "global", DateTime.UtcNow), userId);
            return global;
        }

        var fingerprint = $"{keyId}|{secret!.Length}|{isPaper}";
        if (hit is not null && hit.Fingerprint == fingerprint)
        {
            cache[cacheKey] = hit with { CachedUtc = DateTime.UtcNow };
            return hit.Client;
        }

        var client = new AlpacaBrokerClient(keyId!, secret!, isPaper);
        SetCache(cacheKey, new CacheEntry(client, fingerprint, DateTime.UtcNow), userId);
        logger.LogInformation(
            "User {UserId} orders route to their own Alpaca ({Mode}) via strategy mode {StrategyMode}.",
            userId, isPaper ? "paper" : "LIVE", strategyBrokerMode);

        return client;
    }

    /// <summary>
    /// Atomically swaps in <paramref name="newEntry"/> and disposes whatever it
    /// actually replaced. Using the dictionary's own update-value-factory (rather
    /// than a `hit` snapshot read before an earlier `await`) means two concurrent
    /// resolves for the same key can never both "win" without the loser's client
    /// being disposed — a plain read-then-write here previously let a
    /// freshly-constructed client be silently orphaned under a race.
    /// </summary>
    private void SetCache((Guid, string) key, CacheEntry newEntry, Guid userId) =>
        cache.AddOrUpdate(key, newEntry, (_, old) =>
        {
            DisposeReplacedClient(old, userId);
            return newEntry;
        });

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
