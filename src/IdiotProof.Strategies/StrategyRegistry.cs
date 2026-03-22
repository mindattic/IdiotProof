using IdiotProof.Models;

namespace IdiotProof.Strategies;

/// <summary>
/// Discovers and registers available strategies.
/// </summary>
public sealed class StrategyRegistry
{
    private readonly Dictionary<string, IStrategy> _strategies = new(StringComparer.OrdinalIgnoreCase);

    public StrategyRegistry()
    {
        // Register built-in strategies
        Register(new ItiStrategy());
        Register(new LowHighStrategy());
    }

    public void Register(IStrategy strategy)
    {
        _strategies[strategy.Name] = strategy;
    }

    public IStrategy? Get(string name)
    {
        return _strategies.TryGetValue(name, out var s) ? s : null;
    }

    public IReadOnlyList<IStrategy> GetAll() => _strategies.Values.ToList();

    public IReadOnlyList<string> GetNames() => _strategies.Keys.ToList();
}
