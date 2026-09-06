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
        if (Enum.TryParse<BrokerType>(typeName.Trim(), ignoreCase: true, out var type))
            activeBroker = type;
    }

    public IBrokerClient GetActiveBroker()
    {
        if (brokers.TryGetValue(activeBroker, out var active))
            return active;
        // Only fall back to Sandbox when Sandbox itself is the active broker.
        // If Live or Paper is active but not registered, throw loudly — silent
        // fallback would route live-intended orders to paper with no indication.
        if (activeBroker != BrokerType.Sandbox)
            throw new InvalidOperationException(
                $"Active broker {activeBroker} is not registered. Register it before activating.");
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
        return Enum.TryParse<BrokerType>(typeName.Trim(), ignoreCase: true, out var type)
            ? GetBroker(type)
            : GetActiveBroker();
    }

    public IEnumerable<IBrokerClient> GetAll() => brokers.Values;

    // Convenience delegates to the active broker
    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        => GetActiveBroker().PlaceOrderAsync(request, ct);

    public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default)
        => GetActiveBroker().GetPositionsAsync(ct);

    public bool SupportsOptions => GetActiveBroker().SupportsOptions;

    public Task<int> GetOptionTradingLevelAsync(CancellationToken ct = default)
        => GetActiveBroker().GetOptionTradingLevelAsync(ct);

    public Task<IReadOnlyList<OptionContract>> GetOptionChainAsync(string underlyingSymbol, DateOnly? expiration = null, CancellationToken ct = default)
        => GetActiveBroker().GetOptionChainAsync(underlyingSymbol, expiration, ct);

    public Task<IReadOnlyList<OptionQuote>> GetOptionQuotesAsync(string underlyingSymbol, DateOnly? expiration = null, CancellationToken ct = default)
        => GetActiveBroker().GetOptionQuotesAsync(underlyingSymbol, expiration, ct);
}
