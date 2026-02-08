// Minimal stub interface - conditions are no longer used with AutonomousTrading
// This exists only to keep TradingStrategy compiling during transition

namespace IdiotProof.Strategy {
    /// <summary>
    /// Minimal stub interface for strategy conditions.
    /// Not used with AutonomousTrading - the market score system handles all entry/exit decisions.
    /// </summary>
    public interface IStrategyCondition
    {
        /// <summary>Gets the condition name for display.</summary>
        string Name { get; }

        /// <summary>Evaluates whether this condition is met.</summary>
        bool Evaluate(double price, double vwap);
    }
}
