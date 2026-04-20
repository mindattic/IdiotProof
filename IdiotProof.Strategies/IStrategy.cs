using IdiotProof.Models;

namespace IdiotProof.Strategies;

/// <summary>
/// Context provided to strategy evaluation.
/// </summary>
public sealed class StrategyContext
{
    public TimeZoneInfo Timezone { get; init; } = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    public DateTime EvaluationTimeUtc { get; init; } = DateTime.UtcNow;
    public Dictionary<string, object> Parameters { get; init; } = new();
}

/// <summary>
/// Unified interface for all trading strategies.
/// </summary>
public interface IStrategy
{
    string Name { get; }
    string Description { get; }
    StrategyType Type { get; }

    /// <summary>
    /// Evaluate candles and return zero or more trade signals.
    /// </summary>
    IReadOnlyList<TradeSignal> Evaluate(string symbol, IReadOnlyList<Candle> candles, StrategyContext context);
}
