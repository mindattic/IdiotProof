// ============================================================================
// Price Conditions - Various price-based strategy conditions
// ============================================================================

using System;
using static IdiotProof.Backend.Strategy.PriceConditionValidation;

namespace IdiotProof.Backend.Strategy
{
    /// <summary>
    /// Condition: Price >= specified level (breakout above resistance).
    /// Semantically identical to <see cref="PriceAtOrAboveCondition"/> but named for resistance breakout context.
    /// </summary>
    public sealed class BreakoutCondition : IStrategyCondition
    {
        public double Level { get; }
        public string Name => $"Breakout >= {Level:F2}";

        public BreakoutCondition(double level)
        {
            ThrowIfInvalidPrice(level, nameof(level));
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
            ThrowIfInvalidPrice(level, nameof(level));
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
            ThrowIfInvalidPrice(buffer, nameof(buffer));
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
            ThrowIfInvalidPrice(buffer, nameof(buffer));
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
            ThrowIfInvalidPrice(level, nameof(level));
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
            ThrowIfInvalidPrice(level, nameof(level));
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

    /// <summary>
    /// Shared validation helper for price condition parameters.
    /// </summary>
    internal static class PriceConditionValidation
    {
        public static void ThrowIfInvalidPrice(double value, string paramName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Value must be a finite number.", paramName);
        }

        public static void ThrowIfInvalidPercentage(double value, string paramName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Value must be a finite number.", paramName);
            if (value < 0 || value > 100)
                throw new ArgumentOutOfRangeException(paramName, "Percentage must be between 0 and 100.");
        }
    }

    // =========================================================================
    // GAP CONDITIONS
    // =========================================================================

    /// <summary>
    /// Condition: Price has gapped up by a specified percentage from previous close.
    /// Gap Up = ((Current Price - Previous Close) / Previous Close) * 100 >= threshold
    /// </summary>
    /// <remarks>
    /// <para><b>Gap Up Detection:</b></para>
    /// <para>A gap up occurs when the current price opens significantly higher than
    /// the previous session's close, indicating strong buying pressure or positive news.</para>
    /// 
    /// <para><b>Requirements:</b></para>
    /// <list type="bullet">
    ///   <item>Previous close must be set via <see cref="SetPreviousClose"/> before evaluation</item>
    ///   <item>Typically used at market open or premarket</item>
    /// </list>
    /// 
    /// <para><b>ASCII Visualization:</b></para>
    /// <code>
    ///                   ┌────┐
    ///                   │    │  Current Price
    ///           Gap Up  │    │
    ///           (5%+)   └────┘
    ///                     ↑
    ///     ─────────────────────────── Gap
    ///                     ↓
    ///     ┌────┐
    ///     │    │  Previous Close
    ///     └────┘
    /// </code>
    /// 
    /// <para><b>Example:</b></para>
    /// <code>
    /// Stock.Ticker("NVDA")
    ///     .GapUp(5)                    // Gapped up 5%+
    ///     .IsAboveVwap()               // Holding above VWAP
    ///     .Long(100, Price.Current)
    ///     .AdaptiveOrder(IS.AGGRESSIVE)
    ///     .Build();
    /// </code>
    /// </remarks>
    public sealed class GapUpCondition : IStrategyCondition
    {
        /// <summary>Gets the gap percentage threshold.</summary>
        public double Percentage { get; }

        /// <summary>Gets the previous close price (set externally).</summary>
        public double PreviousClose { get; private set; }

        /// <summary>Gets whether the previous close has been set.</summary>
        public bool IsPreviousCloseSet => PreviousClose > 0;

        public string Name => $"Gap Up >= {Percentage:F1}%";

        /// <summary>
        /// Creates a gap up condition with the specified percentage threshold.
        /// </summary>
        /// <param name="percentage">The minimum gap percentage (e.g., 5 for 5%).</param>
        public GapUpCondition(double percentage)
        {
            PriceConditionValidation.ThrowIfInvalidPercentage(percentage, nameof(percentage));
            Percentage = percentage;
        }

        /// <summary>
        /// Sets the previous close price for gap calculation.
        /// This should be called by the StrategyRunner with historical data.
        /// </summary>
        /// <param name="previousClose">The previous session's closing price.</param>
        public void SetPreviousClose(double previousClose)
        {
            PriceConditionValidation.ThrowIfInvalidPrice(previousClose, nameof(previousClose));
            PreviousClose = previousClose;
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            // Cannot evaluate without previous close
            if (PreviousClose <= 0)
                return false;

            // Calculate gap percentage: ((current - previous) / previous) * 100
            double gapPercent = ((currentPrice - PreviousClose) / PreviousClose) * 100;
            return gapPercent >= Percentage;
        }
    }

    /// <summary>
    /// Condition: Price has gapped down by a specified percentage from previous close.
    /// Gap Down = ((Previous Close - Current Price) / Previous Close) * 100 >= threshold
    /// </summary>
    /// <remarks>
    /// <para><b>Gap Down Detection:</b></para>
    /// <para>A gap down occurs when the current price opens significantly lower than
    /// the previous session's close, indicating selling pressure or negative news.</para>
    /// 
    /// <para><b>ASCII Visualization:</b></para>
    /// <code>
    ///     ┌────┐
    ///     │    │  Previous Close
    ///     └────┘
    ///                     ↑
    ///     ─────────────────────────── Gap
    ///                     ↓
    ///           Gap Down  ┌────┐
    ///           (5%+)     │    │  Current Price
    ///                     └────┘
    /// </code>
    /// 
    /// <para><b>Example:</b></para>
    /// <code>
    /// Stock.Ticker("NVDA")
    ///     .GapDown(5)                  // Gapped down 5%+
    ///     .IsBelowVwap()               // Holding below VWAP
    ///     .Short(100, Price.Current)
    ///     .Build();
    /// </code>
    /// </remarks>
    public sealed class GapDownCondition : IStrategyCondition
    {
        /// <summary>Gets the gap percentage threshold.</summary>
        public double Percentage { get; }

        /// <summary>Gets the previous close price (set externally).</summary>
        public double PreviousClose { get; private set; }

        /// <summary>Gets whether the previous close has been set.</summary>
        public bool IsPreviousCloseSet => PreviousClose > 0;

        public string Name => $"Gap Down >= {Percentage:F1}%";

        /// <summary>
        /// Creates a gap down condition with the specified percentage threshold.
        /// </summary>
        /// <param name="percentage">The minimum gap percentage (e.g., 5 for 5%).</param>
        public GapDownCondition(double percentage)
        {
            PriceConditionValidation.ThrowIfInvalidPercentage(percentage, nameof(percentage));
            Percentage = percentage;
        }

        /// <summary>
        /// Sets the previous close price for gap calculation.
        /// </summary>
        public void SetPreviousClose(double previousClose)
        {
            PriceConditionValidation.ThrowIfInvalidPrice(previousClose, nameof(previousClose));
            PreviousClose = previousClose;
        }

        public bool Evaluate(double currentPrice, double vwap)
        {
            // Cannot evaluate without previous close
            if (PreviousClose <= 0)
                return false;

            // Calculate gap percentage: ((previous - current) / previous) * 100
            double gapPercent = ((PreviousClose - currentPrice) / PreviousClose) * 100;
            return gapPercent >= Percentage;
        }
    }
}


