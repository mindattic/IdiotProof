// ============================================================================
// Strategy Condition Interface
// ============================================================================

namespace IdiotProof.Backend.Strategy
{
    /// <summary>
    /// Represents a condition that must be met before proceeding to the next step.
    /// </summary>
    public interface IStrategyCondition
    {
        /// <summary>
        /// Gets the name/description of this condition.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Evaluates whether the condition is met.
        /// </summary>
        /// <param name="currentPrice">The current stock price.</param>
        /// <param name="vwap">The current VWAP value.</param>
        /// <returns>True if the condition is satisfied.</returns>
        bool Evaluate(double currentPrice, double vwap);
    }
}
