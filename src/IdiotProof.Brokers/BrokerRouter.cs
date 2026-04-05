using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Routes broker operations to the active broker based on configuration.
/// </summary>
public sealed class BrokerRouter
{
    private readonly Dictionary<BrokerType, IBrokerClient> brokers = new();

    public void Register(IBrokerClient client)
    {
        brokers[client.BrokerType] = client;
    }

    public IBrokerClient GetBroker(BrokerType type)
    {
        if (brokers.TryGetValue(type, out var client))
            return client;
        throw new InvalidOperationException($"No broker registered for type: {type}");
    }

    public IBrokerClient GetBroker(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return GetBroker(BrokerType.Ibkr);

        if (Enum.TryParse<BrokerType>(typeName, ignoreCase: true, out var type))
            return GetBroker(type);

        return GetBroker(BrokerType.Ibkr);
    }
}
