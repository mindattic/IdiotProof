using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Abstraction for any broker connection (IBKR, Alpaca, Sandbox, etc.)
/// </summary>
public interface IBrokerClient
{
    BrokerType BrokerType { get; }
    bool IsConnected { get; }
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);
    Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default);
}
