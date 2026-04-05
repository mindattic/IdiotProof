using IdiotProof.Models;

namespace IdiotProof.Strategies;

/// <summary>
/// Discovers and registers available strategies.
/// </summary>
public sealed class StrategyRegistry
{
    private readonly Dictionary<string, IStrategy> strategies = new(StringComparer.OrdinalIgnoreCase);

    public StrategyRegistry()
    {
        // Register built-in strategies
        Register(new ItiStrategy());
        Register(new LowHighStrategy());
    }

    public void Register(IStrategy strategy)
    {
        strategies[strategy.Name] = strategy;
    }

    public IStrategy? Get(string name)
    {
        return strategies.TryGetValue(name, out var s) ? s : null;
    }

    public IReadOnlyList<IStrategy> GetAll() => strategies.Values.ToList();

    public IReadOnlyList<string> GetNames() => strategies.Keys.ToList();
}
