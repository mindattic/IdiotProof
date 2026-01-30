// ============================================================================
// Price Conditions - Various price-based strategy conditions
// ============================================================================

using System;

namespace IdiotProof.Models
{
    /// <summary>
    /// Condition: Price >= specified level (breakout above resistance).
    /// </summary>
    public sealed class BreakoutCondition : IStrategyCondition
    {
        public double Level { get; }
        public string Name => $"Breakout >= {Level:F2}";

        public BreakoutCondition(double level)
        {
            Level = level;
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            return currentPrice >= Level;
        }
    }

    /// <summary>
    /// Condition: Price &lt;= specified level (pullback to support).
    /// </summary>
    public sealed class PullbackCondition : IStrategyCondition
    {
        public double Level { get; }
        public string Name => $"Pullback <= {Level:F2}";

        public PullbackCondition(double level)
        {
            Level = level;
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            return currentPrice <= Level;
        }
    }

    /// <summary>
    /// Condition: Price >= VWAP (above volume-weighted average price).
    /// </summary>
    public sealed class AboveVwapCondition : IStrategyCondition
    {
        public double Buffer { get; }
        public string Name => Buffer > 0 ? $"Price >= VWAP + {Buffer:F2}" : "Price >= VWAP";

        public AboveVwapCondition(double buffer = 0)
        {
            Buffer = buffer;
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            return vwap > 0 && currentPrice >= (vwap + Buffer);
        }
    }

    /// <summary>
    /// Condition: Price &lt;= VWAP (below volume-weighted average price).
    /// </summary>
    public sealed class BelowVwapCondition : IStrategyCondition
    {
        public double Buffer { get; }
        public string Name => Buffer > 0 ? $"Price <= VWAP - {Buffer:F2}" : "Price <= VWAP";

        public BelowVwapCondition(double buffer = 0)
        {
            Buffer = buffer;
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            return vwap > 0 && currentPrice <= (vwap - Buffer);
        }
    }

    /// <summary>
    /// Condition: Price >= specified level.
    /// </summary>
    public sealed class PriceAtOrAboveCondition : IStrategyCondition
    {
        public double Level { get; }
        public string Name => $"Price >= {Level:F2}";

        public PriceAtOrAboveCondition(double level)
        {
            Level = level;
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            return currentPrice >= Level;
        }
    }

    /// <summary>
    /// Condition: Price &lt; specified level.
    /// </summary>
    public sealed class PriceBelowCondition : IStrategyCondition
    {
        public double Level { get; }
        public string Name => $"Price < {Level:F2}";

        public PriceBelowCondition(double level)
        {
            Level = level;
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            return currentPrice < Level;
        }
    }

    /// <summary>
    /// Custom condition using a delegate.
    /// </summary>
    public sealed class CustomCondition : IStrategyCondition
    {
        private readonly Func<double, double, bool> _evaluator;
        public string Name { get; }

        public CustomCondition(string name, Func<double, double, bool> evaluator)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            return _evaluator(currentPrice, vwap);
        }
    }
}
