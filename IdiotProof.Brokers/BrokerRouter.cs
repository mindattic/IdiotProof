using System.Collections.Concurrent;
using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Routes broker operations to the configured active broker.
/// Sandbox is always the safe default — never routes to live brokers
/// unless explicitly activated.
/// </summary>
public sealed class BrokerRouter
{
    // ConcurrentDictionary so paper/live hot-swap doesn't race with order routing.
    private readonly ConcurrentDictionary<BrokerType, IBrokerClient> brokers = new();
    private BrokerType activeBroker = BrokerType.Sandbox;

    public BrokerType ActiveBrokerType => activeBroker;

    public void Register(IBrokerClient client) => brokers[client.BrokerType] = client;

    public void SetActive(BrokerType type) => activeBroker = type;

    public void SetActive(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return;
        if (Enum.TryParse<BrokerType>(typeName, ignoreCase: true, out var type))
            activeBroker = type;
    }

    public IBrokerClient GetActiveBroker()
    {
        if (brokers.TryGetValue(activeBroker, out var active))
            return active;
        // Fallback: always return Sandbox rather than throw
        return brokers.TryGetValue(BrokerType.Sandbox, out var sandbox)
            ? sandbox
            : throw new InvalidOperationException("No broker registered. Sandbox broker must always be registered.");
    }

    public IBrokerClient GetBroker(BrokerType type)
    {
        if (brokers.TryGetValue(type, out var client))
            return client;
        // Safe fallback
        return brokers.TryGetValue(BrokerType.Sandbox, out var sandbox) ? sandbox
            : throw new InvalidOperationException($"No broker for {type} and no Sandbox fallback.");
    }

    public IBrokerClient GetBroker(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return GetActiveBroker();
        return Enum.TryParse<BrokerType>(typeName, ignoreCase: true, out var type)
            ? GetBroker(type)
            : GetActiveBroker();
    }

    public IEnumerable<IBrokerClient> GetAll() => brokers.Values;

    // Convenience delegates to the active broker
    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        => GetActiveBroker().PlaceOrderAsync(request, ct);

    public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default)
        => GetActiveBroker().GetPositionsAsync(ct);
}
