using System.Collections.Concurrent;
using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// In-memory sandbox broker for testing. No external API calls. Orders fill
/// instantly at the limit price (or $0-cost basis for market orders with no
/// reference price) and update the in-memory position book, so a sandbox
/// session behaves like a real fill loop: buy → position appears, sell →
/// position shrinks/disappears.
/// </summary>
public sealed class SandboxBrokerClient : IBrokerClient
{
    private readonly ConcurrentDictionary<string, Position> positions = new(StringComparer.OrdinalIgnoreCase);

    public BrokerType BrokerType => BrokerType.Sandbox;

    /// <summary>Sandbox is always simulated — never real money.</summary>
    public bool IsPaper => true;

    public bool IsConnected => true;

    public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task DisconnectAsync() => Task.CompletedTask;

    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        // Simulate an immediate fill. Fill price = limit price when present;
        // market orders without a reference price fill at 0 (harmless for the
        // position book — quantity tracking is what matters in sandbox).
        var fillPrice = request.LimitPrice ?? request.StopPrice ?? 0m;
        var qty = request.Quantity > 0
            ? request.Quantity
            : fillPrice > 0m && request.Notional is { } notional
                ? Math.Floor(notional / fillPrice)
                : 0m;
        var signedQty = request.Side == OrderSide.Buy ? qty : -qty;

        positions.AddOrUpdate(
            request.Symbol.ToUpperInvariant(),
            _ => new Position
            {
                Symbol = request.Symbol.ToUpperInvariant(),
                Quantity = signedQty,
                AveragePrice = fillPrice,
                MarketValue = signedQty * fillPrice,
            },
            (_, existing) =>
            {
                var newQty = existing.Quantity + signedQty;
                return new Position
                {
                    Symbol = existing.Symbol,
                    Quantity = newQty,
                    // Weighted average only when adding in the same direction;
                    // reductions keep the original basis.
                    AveragePrice = signedQty > 0 && existing.Quantity > 0 && newQty != 0
                        ? (existing.AveragePrice * existing.Quantity + fillPrice * signedQty) / newQty
                        : existing.AveragePrice,
                    MarketValue = newQty * fillPrice,
                };
            });

        // Drop flattened rows so GetPositionsAsync mirrors a real broker.
        if (positions.TryGetValue(request.Symbol.ToUpperInvariant(), out var updated) && updated.Quantity == 0m)
            positions.TryRemove(request.Symbol.ToUpperInvariant(), out _);

        var result = new OrderResult
        {
            BrokerOrderId = $"SANDBOX-{Guid.NewGuid():N}",
            IsSuccess = true,
            Message = $"Sandbox order filled ({request.Side} {(qty > 0 ? qty : request.Notional)} {request.Symbol}@{fillPrice:F2})."
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
