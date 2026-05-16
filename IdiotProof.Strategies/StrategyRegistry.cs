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
        // No built-in named strategies. All strategies are user-authored
        // IdiotScript saved to the Strategies table; DslStrategy executes them
        // at runtime via the parser. Re-add Register(new XStrategy()) here only
        // if a hard-coded baseline strategy is ever needed again.
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
