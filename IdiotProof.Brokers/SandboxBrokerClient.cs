using System.Collections.Concurrent;
using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// In-memory sandbox broker for testing. No external API calls.
/// </summary>
public sealed class SandboxBrokerClient : IBrokerClient
{
    private readonly ConcurrentDictionary<string, Position> positions = new();

    public BrokerType BrokerType => BrokerType.Sandbox;
    public bool IsConnected => true;

    public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task DisconnectAsync() => Task.CompletedTask;

    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var result = new OrderResult
        {
            BrokerOrderId = $"SANDBOX-{Guid.NewGuid():N}",
            IsSuccess = true,
            Message = "Sandbox order accepted."
        };
        return Task.FromResult(result);
    }

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        return Task.FromResult(new OrderResult
        {
            BrokerOrderId = orderId,
            IsSuccess = true,
            Message = "Sandbox order cancelled."
        });
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Position> result = this.positions.Values.ToList();
        return Task.FromResult(result);
    }
}
