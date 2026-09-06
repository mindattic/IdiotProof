using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Abstraction for any broker connection (IBKR, Alpaca, Sandbox, etc.)
/// </summary>
public interface IBrokerClient
{
    BrokerType BrokerType { get; }

    /// <summary>
    /// True when this client trades a SIMULATED / paper account (no real
    /// money): Sandbox is always paper; Alpaca reflects its paper-vs-live
    /// endpoint. The trade diary records this per trade so a live fill can
    /// never be mistaken for a paper one. Broker-agnostic on purpose — every
    /// implementation must answer it.
    /// </summary>
    bool IsPaper { get; }

    bool IsConnected { get; }
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);
    Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default);

    // ---- Options (single-leg). Default implementations = "this broker has no options
    // ---- support", so dormant adapters (IBKR) compile untouched.

    /// <summary>
    /// True when this client IMPLEMENTS the options endpoints. It says nothing about
    /// whether the connected account is approved to trade them — check
    /// <see cref="GetOptionTradingLevelAsync"/> for that (Alpaca: 0 = not approved).
    /// </summary>
    bool SupportsOptions => false;

    /// <summary>Account's options approval level (Alpaca 0–3). 0 when unsupported/unapproved.</summary>
    async Task<int> GetOptionTradingLevelAsync(CancellationToken ct = default) =>
        (await GetOptionsAccountAsync(ct).ConfigureAwait(false)).TradingLevel;

    /// <summary>
    /// Effective/approved options level plus options buying power in one call.
    /// Default = "no options" (<see cref="OptionsAccountInfo.None"/>).
    /// </summary>
    Task<OptionsAccountInfo> GetOptionsAccountAsync(CancellationToken ct = default) => Task.FromResult(OptionsAccountInfo.None);

    /// <summary>
    /// Listed contracts for an underlying, optionally narrowed to one expiration.
    /// Catalog data only (strike/expiry/right/OI) — no prices; see <see cref="GetOptionQuotesAsync"/>.
    /// </summary>
    Task<IReadOnlyList<OptionContract>> GetOptionChainAsync(string underlyingSymbol, DateOnly? expiration = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OptionContract>>([]);

    /// <summary>
    /// Live quotes (bid/ask/last, and IV/Greeks when the broker supplies them) for the contracts
    /// of an underlying, optionally narrowed to one expiration. Keyed by OCC symbol via
    /// <see cref="OptionQuote.OccSymbol"/>.
    /// </summary>
    Task<IReadOnlyList<OptionQuote>> GetOptionQuotesAsync(string underlyingSymbol, DateOnly? expiration = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OptionQuote>>([]);
}
