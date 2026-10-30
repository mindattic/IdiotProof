using System.Collections.Concurrent;
using IdiotProof.Blazor.Data;
using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Shared.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IdiotProof.Blazor.Services;

/// <summary>Which account the Options page is pointed at. Sandbox is the always-safe default (IP-LAW-3).</summary>
public enum OptionsBrokerMode { Sandbox, Paper, Live }

/// <summary>Where the page's broker actually resolved to, and why, so the UI can say so plainly.</summary>
public sealed record OptionsBrokerResolution(IBrokerClient Client, OptionsBrokerMode Mode, bool IsAlpaca, string? FallbackReason);

/// <summary>
/// Host-side plumbing for the manual Options section (Phase 1). Resolves the user's broker
/// for the chosen mode under the SAME consent rules as <see cref="AccountSummaryService"/> /
/// <see cref="UserBrokerResolver"/> (opted into Alpaca + a full key pair, Vault first), gets
/// the live underlying price, reads recent bullish research claims for the sell signal, and
/// holds the Black-Scholes risk-free rate setting. Scoped: one instance per circuit, clients
/// cached for the session and disposed with it.
/// </summary>
public sealed class OptionsTradingService(
    UserKeyService userKeys,
    IConfiguration configuration,
    AppSettings appSettings,
    IDbContextFactory<AppDbContext> dbFactory,
    SettingsRepository settings,
    ILogger<OptionsTradingService> logger) : IAsyncDisposable
{
    public const string RiskFreeRateKey = "Options.RiskFreeRate";
    public const decimal DefaultRiskFreeRate = 0.04m;

    private readonly SandboxBrokerClient sandbox = new();
    private readonly ConcurrentDictionary<OptionsBrokerMode, AlpacaBrokerClient> alpacaClients = new();
    private AlpacaDataFeed? feed;

    public SandboxBrokerClient Sandbox => sandbox;

    public async Task<OptionsBrokerResolution> ResolveBrokerAsync(Guid userId, OptionsBrokerMode mode, CancellationToken ct = default)
    {
        if (mode == OptionsBrokerMode.Sandbox)
            return new OptionsBrokerResolution(sandbox, mode, false, null);

        if (alpacaClients.TryGetValue(mode, out var cached))
            return new OptionsBrokerResolution(cached, mode, true, null);

        var isPaper = mode == OptionsBrokerMode.Paper;
        var (keyId, secret, reason) = await ResolveAlpacaPairAsync(userId, isPaper, ct);
        if (keyId is null || secret is null)
        {
            logger.LogInformation("Options page: user {UserId} asked for {Mode} but {Reason} — using Sandbox.", userId, mode, reason);
            return new OptionsBrokerResolution(sandbox, OptionsBrokerMode.Sandbox, false, reason);
        }

        var client = alpacaClients.GetOrAdd(mode, _ => new AlpacaBrokerClient(keyId, secret, isPaper));
        return new OptionsBrokerResolution(client, mode, true, null);
    }

    /// <summary>
    /// Same gate as the rest of the app: a key existing (Vault or DB) is not permission to
    /// trade it — the user must have flipped "Route my orders to this Alpaca account".
    /// </summary>
    private async Task<(string? KeyId, string? Secret, string? Reason)> ResolveAlpacaPairAsync(Guid userId, bool isPaper, CancellationToken ct)
    {
        var keys = await userKeys.GetOrCreateAsync(userId, ct);
        if (!string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase))
            return (null, null, "Alpaca routing isn't enabled on the API Keys page");

        var (vaultKeyId, vaultSecret) = AlpacaVaultKeys.Resolve(configuration, isPaper);
        if (!string.IsNullOrWhiteSpace(vaultKeyId) && !string.IsNullOrWhiteSpace(vaultSecret))
            return (vaultKeyId, vaultSecret, null);

        var (id, sec) = isPaper
            ? (keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey)
            : (keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey);
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sec)
            ? (null, null, $"no {(isPaper ? "paper" : "live")} Alpaca key pair is configured")
            : (id, sec, null);
    }

    /// <summary>
    /// Live underlying price. Alpaca data (user's paper keys, else the host's global keys)
    /// when available; otherwise the Sandbox's deterministic reference price, labelled as such.
    /// </summary>
    public async Task<(decimal? Price, string Source)> GetUnderlyingPriceAsync(Guid userId, string symbol, CancellationToken ct = default)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        try
        {
            var f = await GetFeedAsync(userId, ct);
            if (f is not null)
            {
                var latest = await f.GetLatestPriceAsync(symbol, ct);
                if (latest is { Price: > 0m })
                {
                    sandbox.SetReferencePrice(symbol, latest.Price); // keep the synthetic chain honest
                    return (latest.Price, "Alpaca");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Options page: underlying price lookup failed for {Symbol}", symbol);
        }
        return (sandbox.GetReferencePrice(symbol), "Sandbox");
    }

    private async Task<AlpacaDataFeed?> GetFeedAsync(Guid userId, CancellationToken ct)
    {
        if (feed is not null) return feed;

        // Key priority mirrors /gapper and /backtest: the user's own paper pair, then the host chain.
        var keys = await userKeys.GetOrCreateAsync(userId, ct);
        var useUser = !string.IsNullOrWhiteSpace(keys.AlpacaApiKeyId) && !string.IsNullOrWhiteSpace(keys.AlpacaApiSecretKey);
        var keyId = useUser ? keys.AlpacaApiKeyId! : appSettings.AlpacaApiKeyId;
        var secret = useUser ? keys.AlpacaApiSecretKey! : appSettings.AlpacaApiSecretKey;
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret)) return null;

        feed = new AlpacaDataFeed(keyId, secret);
        return feed;
    }

    /// <summary>Bullish research claims for the underlying since <paramref name="sinceUtc"/>, projected for <see cref="SellSignalEvaluator"/>.</summary>
    public async Task<IReadOnlyList<BullishClaimSummary>> GetRecentBullishClaimsAsync(string ticker, DateTime sinceUtc, CancellationToken ct = default)
    {
        ticker = ticker.Trim().ToUpperInvariant();
        var since = DateOnly.FromDateTime(sinceUtc);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await db.ResearchClaims
                .Where(c => c.Ticker == ticker && c.Sentiment == "Bullish" && c.ArticleDate >= since)
                .OrderByDescending(c => c.SignificanceScore)
                .Take(10)
                .Select(c => new { c.Ticker, c.ArticleDate, c.SignificanceScore, c.ClaimSummary })
                .ToListAsync(ct);

            return rows
                .Select(r => new BullishClaimSummary(r.Ticker, r.ArticleDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), (int)Math.Round(r.SignificanceScore ?? 0), r.ClaimSummary))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Options page: research claim lookup failed for {Ticker}", ticker);
            return [];
        }
    }

    public async Task<decimal> GetRiskFreeRateAsync(CancellationToken ct = default)
    {
        try { return await settings.GetDecimalAsync(RiskFreeRateKey, ct) ?? DefaultRiskFreeRate; }
        catch { return DefaultRiskFreeRate; }
    }

    public Task SetRiskFreeRateAsync(decimal rate, CancellationToken ct = default) =>
        settings.SetAsync(RiskFreeRateKey, rate.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);

    public async ValueTask DisposeAsync()
    {
        foreach (var c in alpacaClients.Values) await c.DisposeAsync();
        alpacaClients.Clear();
        if (feed is IAsyncDisposable d) await d.DisposeAsync();
    }
}
