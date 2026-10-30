using System.Globalization;
using IdiotProof.Blazor.Data;
using IdiotProof.Brokers;
using Microsoft.Extensions.Configuration;

namespace IdiotProof.Blazor.Services;

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

public sealed class AccountSummaryService(UserKeyService userKeys, IConfiguration configuration)
{
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

        // MindAttic.Vault's Brokers bucket (alpaca-paper/alpaca-live) overrides
        // DB-stored UserApiKeys when present — same rule as UserBrokerResolver,
        // so this summary reflects the account that's really being traded on.
        // A Vault pair is used only when BOTH its fields are present — never
        // mix a Vault key with a DB-decrypted secret (or vice versa).
        var (paperKeyId, paperSecret, paperStoredOrVaulted) = EffectivePair(
            AlpacaVaultKeys.Resolve(configuration, isPaper: true), keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey, paperStored, optedIntoAlpaca);
        var (liveKeyId, liveSecret, liveStoredOrVaulted) = EffectivePair(
            AlpacaVaultKeys.Resolve(configuration, isPaper: false), keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey, liveStored, optedIntoAlpaca);

        var paperTask = FetchAsync(paperKeyId, paperSecret, paperStoredOrVaulted, isPaper: true, ct);
        var liveTask = FetchAsync(liveKeyId, liveSecret, liveStoredOrVaulted, isPaper: false, ct);
        await Task.WhenAll(paperTask, liveTask);
        return (paperTask.Result, liveTask.Result);
    }

    private static (string? KeyId, string? Secret, bool Stored) EffectivePair(
        (string? KeyId, string? Secret) vault, string? dbKeyId, string? dbSecret, bool dbStored, bool optedIntoAlpaca)
    {
        if (!optedIntoAlpaca) return (null, null, false);

        var vaultUsable = !string.IsNullOrWhiteSpace(vault.KeyId) && !string.IsNullOrWhiteSpace(vault.Secret);
        return vaultUsable
            ? (vault.KeyId, vault.Secret, true)
            : (dbKeyId, dbSecret, dbStored);
    }

    private static async Task<AccountSnapshot> FetchAsync(string? keyId, string? secret, bool keyStored, bool isPaper, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
        {
            // A key IS stored (ciphertext present in SQL, or a Vault entry
            // exists) but came back empty — a DataProtection key-ring
            // mismatch, not "never configured". Reporting this as "not
            // configured" would send the user to re-type a key that isn't
            // actually missing.
            var error = keyStored
                ? "Key is stored but could not be decrypted (DataProtection key-ring mismatch) — see server logs, or re-enter it on the API Keys page."
                : null;
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
