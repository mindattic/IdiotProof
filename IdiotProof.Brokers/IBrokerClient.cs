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
}
