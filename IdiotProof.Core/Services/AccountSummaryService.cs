using System.Globalization;
using IdiotProof.Core.Data;
using IdiotProof.Brokers;
using IdiotProof.Models;

namespace IdiotProof.Core.Services;

/// <summary>
/// A snapshot of one Alpaca account (Paper or Live): cash/portfolio value plus
/// the shares and dollar value actually held. Fetched straight from the
/// broker rather than derived from Strategy rows, since the account can hold
/// positions IdiotProof didn't open (a manual trade, another tool) that
/// per-strategy PositionQty never reflects.
/// </summary>
public sealed record AccountSnapshot(
    bool IsPaper, bool Configured, string? Error,
    decimal? Cash, decimal? PortfolioValue,
    int PositionCount, decimal TotalShares, decimal TotalMarketValue);

/// <summary>
/// One Alpaca account in full: every <c>/v2/account</c> field as Alpaca sent it (equity,
/// last_equity, cash, buying_power, daytrade_count, ...) plus each open position. Read-only.
/// </summary>
public sealed record AccountDetail(
    bool IsPaper, bool Configured, string? Error,
    IReadOnlyDictionary<string, string> Account, IReadOnlyList<Position> Positions)
{
    /// <summary>A field parsed as a decimal (invariant culture), or null when absent/unparseable.</summary>
    public decimal? Number(string field) =>
        Account.TryGetValue(field, out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
}

public sealed class AccountSummaryService(UserKeyService userKeys)
{
    /// <summary>
    /// The full Paper or Live account (fields + positions), behind the same consent gate and
    /// Vault-only key rule as <see cref="GetSummaryAsync"/>. Never throws for a broker or key
    /// problem; the reason comes back in <see cref="AccountDetail.Error"/>.
    /// </summary>
    public async Task<AccountDetail> GetDetailAsync(Guid userId, bool isPaper, CancellationToken ct = default)
    {
        var empty = new Dictionary<string, string>();
        UserApiKeys keys;
        bool paperStored, liveStored;
        try
        {
            keys = await userKeys.GetOrCreateAsync(userId, ct);
            (paperStored, liveStored) = await userKeys.GetKeyPresenceAsync(userId, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new AccountDetail(isPaper, false, $"Could not load account keys: {ex.Message}", empty, []);
        }

        var optedIntoAlpaca = string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase);
        var (keyId, secret, stored) = isPaper
            ? EffectivePair(keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey, paperStored, optedIntoAlpaca)
            : EffectivePair(keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey, liveStored, optedIntoAlpaca);

        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
        {
            var error = !optedIntoAlpaca
                ? "Alpaca routing is off for this user (still on Sandbox) — opt in on the API Keys page."
                : $"No {(isPaper ? "paper" : "live")} Alpaca keys in Vault (`idiotproof keys set alpaca-{(isPaper ? "paper" : "live")}` or the API Keys page).";
            return new AccountDetail(isPaper, stored, error, empty, []);
        }

        await using var client = new AlpacaBrokerClient(keyId, secret, isPaper);
        try
        {
            var accountTask = client.GetAccountAsync(ct);
            var positionsTask = client.GetPositionsAsync(ct);
            await Task.WhenAll(accountTask, positionsTask);
            var account = accountTask.Result;
            return account.TryGetValue("error", out var err)
                ? new AccountDetail(isPaper, true, err, empty, [])
                : new AccountDetail(isPaper, true, null, account, positionsTask.Result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new AccountDetail(isPaper, true, ex.Message, empty, []);
        }
    }

    public async Task<(AccountSnapshot Paper, AccountSnapshot Live)> GetSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        UserApiKeys keys;
        bool paperStored, liveStored;
        try
        {
            keys = await userKeys.GetOrCreateAsync(userId, ct);
            (paperStored, liveStored) = await userKeys.GetKeyPresenceAsync(userId, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // A transient DB blip here must degrade to an inline error, not
            // throw unhandled — this is a summary widget, not the reason the
            // whole Strategies page (table, bulk actions, everything) should
            // go down via the page-wide ErrorBoundary.
            var error = new AccountSnapshot(true, false, $"Could not load account info: {ex.Message}", null, null, 0, 0m, 0m);
            return (error, error with { IsPaper = false });
        }

        // Same consent gate as UserBrokerResolver (the thing that actually
        // places orders): a key existing — in Vault or the DB — is not by
        // itself permission to show/trade it. The user must have opted in via
        // "Route my orders to this Alpaca account" on the API Keys page.
        // Without this, the summary could show a real account balance for a
        // user who never enabled Alpaca routing (still on Sandbox), which is
        // both misleading and would disagree with what's actually trading.
        var optedIntoAlpaca = string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase);

        // The pairs come from this user's own MindAttic.Vault Brokers entries (IP-T330), the
        // same ones UserBrokerResolver trades on, so the summary shows the account really traded.
        var (paperKeyId, paperSecret, paperStoredOrVaulted) = EffectivePair(
            keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey, paperStored, optedIntoAlpaca);
        var (liveKeyId, liveSecret, liveStoredOrVaulted) = EffectivePair(
            keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey, liveStored, optedIntoAlpaca);

        var paperTask = FetchAsync(paperKeyId, paperSecret, paperStoredOrVaulted, isPaper: true, ct);
        var liveTask = FetchAsync(liveKeyId, liveSecret, liveStoredOrVaulted, isPaper: false, ct);
        await Task.WhenAll(paperTask, liveTask);
        return (paperTask.Result, liveTask.Result);
    }

    private static (string? KeyId, string? Secret, bool Stored) EffectivePair(
        string? keyId, string? secret, bool stored, bool optedIntoAlpaca) =>
        optedIntoAlpaca ? (keyId, secret, stored) : (null, null, false);

    private static async Task<AccountSnapshot> FetchAsync(string? keyId, string? secret, bool keyStored, bool isPaper, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
        {
            // Not configured (or not opted in): no error, just an empty card.
            string? error = null;
            return new AccountSnapshot(isPaper, keyStored, error, null, null, 0, 0m, 0m);
        }

        await using var client = new AlpacaBrokerClient(keyId, secret, isPaper);
        try
        {
            var accountTask = client.GetAccountAsync(ct);
            var positionsTask = client.GetPositionsAsync(ct);
            await Task.WhenAll(accountTask, positionsTask);
            var account = accountTask.Result;
            var positions = positionsTask.Result;

            if (account.TryGetValue("error", out var err))
                return new AccountSnapshot(isPaper, true, err, null, null, 0, 0m, 0m);

            var totalShares = positions.Sum(p => Math.Abs(p.Quantity));
            var totalMarketValue = positions.Sum(p => p.MarketValue);

            return new AccountSnapshot(isPaper, true, null,
                TryDecimal(account, "cash"), TryDecimal(account, "portfolio_value"),
                positions.Count, totalShares, totalMarketValue);
        }
        catch (Exception ex)
        {
            return new AccountSnapshot(isPaper, true, ex.Message, null, null, 0, 0m, 0m);
        }
    }

    private static decimal? TryDecimal(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v : null;
}
