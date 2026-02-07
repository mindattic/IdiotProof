// ============================================================================
// StrategyValidator - Validates trading strategies before execution
// ============================================================================
//
// VALIDATION RULES:
// 1. Symbol must not be empty
// 2. At least one condition required before order
// 3. Quantity must be positive (or zero for testing)
// 4. Take profit must be above entry for buy orders
// 5. Stop loss must be below entry for buy orders
// 6. Time window must be valid (start < end)
// 7. Risk parameters must be within acceptable ranges
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Logging;
using IdiotProof.Backend.Strategy;
using IdiotProof.Shared.Helpers;

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// Validates trading strategies before execution.
    /// </summary>
    public static class StrategyValidator
    {
        /// <summary>
        /// Shared session logger instance (set from Program.cs).
        /// </summary>
        public static SessionLogger? SessionLogger { get; set; }

        /// <summary>
        /// Logs a message to both console and session log file.
        /// </summary>
        private static void Log(string message)
        {
            Console.WriteLine($"{TimeStamp.NowBracketed} {message}");
            SessionLogger?.LogEvent("VALIDATOR", message);
        }

        /// <summary>
        /// Validates a list of strategies and returns validation results.
        /// </summary>
        /// <param name="strategies">The strategies to validate.</param>
        /// <returns>A validation result with any errors found.</returns>
        public static StrategyValidationResult ValidateAll(IEnumerable<TradingStrategy> strategies)
        {
            var result = new StrategyValidationResult();
            var strategyList = strategies.ToList();

            if (strategyList.Count == 0)
            {
                result.AddError("No strategies provided");
                return result;
            }

            foreach (var strategy in strategyList)
            {
                ValidateStrategy(strategy, result);
            }

            // Check for duplicate symbols with overlapping time windows
            var symbolGroups = strategyList
                .Where(s => s.Enabled)
                .GroupBy(s => s.Symbol);

            foreach (var group in symbolGroups.Where(g => g.Count() > 1))
            {
                var strategies2 = group.ToList();
                for (int i = 0; i < strategies2.Count; i++)
                {
                    for (int j = i + 1; j < strategies2.Count; j++)
                    {
                        if (TimeWindowsOverlap(strategies2[i], strategies2[j]))
                        {
                            result.AddWarning($"[{group.Key}] Multiple strategies with overlapping time windows detected");
                            break;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Validates a single strategy.
        /// </summary>
        /// <param name="strategy">The strategy to validate.</param>
        /// <returns>A validation result.</returns>
        public static StrategyValidationResult Validate(TradingStrategy strategy)
        {
            var result = new StrategyValidationResult();
            ValidateStrategy(strategy, result);
            return result;
        }

        private static void ValidateStrategy(TradingStrategy strategy, StrategyValidationResult result)
        {
            var symbol = strategy.Symbol ?? "UNKNOWN";
            var prefix = $"[{symbol}]";

            // Symbol validation
            if (string.IsNullOrWhiteSpace(strategy.Symbol))
            {
                result.AddError($"{prefix} Symbol is required");
            }

            // Condition validation - AutonomousTrading handles its own entry/exit decisions
            if ((strategy.Conditions == null || strategy.Conditions.Count == 0) && 
                !strategy.Order.UseAutonomousTrading)
            {
                result.AddError($"{prefix} At least one condition is required before the order (or use AutonomousTrading)");
            }

            // Order validation
            var order = strategy.Order;
            if (order == null)
            {
                result.AddError($"{prefix} Order configuration is required");
                return;
            }

            // Quantity validation
            if (order.Quantity < 0)
            {
                result.AddError($"{prefix} Quantity cannot be negative");
            }
            else if (order.Quantity == 0)
            {
                result.AddWarning($"{prefix} Quantity is 0 - no shares will be traded");
            }

            // Buy order specific validations
            if (order.Side == OrderSide.Buy)
            {
                // Take profit validation
                if (order.EnableTakeProfit && order.TakeProfitPrice.HasValue)
                {
                    // We can't validate against entry price since it's dynamic,
                    // but we can check for obviously wrong values
                    if (order.TakeProfitPrice.Value <= 0)
                    {
                        result.AddError($"{prefix} Take profit price must be positive");
                    }
                }

                // ADX take profit validation
                if (order.AdxTakeProfit != null)
                {
                    if (order.AdxTakeProfit.ConservativeTarget <= 0)
                    {
                        result.AddError($"{prefix} ADX conservative target must be positive");
                    }
                    if (order.AdxTakeProfit.AggressiveTarget <= 0)
                    {
                        result.AddError($"{prefix} ADX aggressive target must be positive");
                    }
                    if (order.AdxTakeProfit.ConservativeTarget >= order.AdxTakeProfit.AggressiveTarget)
                    {
                        result.AddWarning($"{prefix} ADX conservative target should be less than aggressive target");
                    }
                }

                // Stop loss validation
                if (order.EnableStopLoss && order.StopLossPrice.HasValue)
                {
                    if (order.StopLossPrice.Value <= 0)
                    {
                        result.AddError($"{prefix} Stop loss price must be positive");
                    }
                }
            }

            // Trailing stop loss validation
            if (order.EnableTrailingStopLoss)
            {
                if (order.TrailingStopLossPercent <= 0 || order.TrailingStopLossPercent > 1)
                {
                    result.AddError($"{prefix} Trailing stop loss percent must be between 0 and 100%");
                }
                if (order.TrailingStopLossPercent < 0.01)
                {
                    result.AddWarning($"{prefix} Trailing stop loss is very tight ({order.TrailingStopLossPercent * 100:F1}%)");
                }
                if (order.TrailingStopLossPercent > 0.50)
                {
                    result.AddWarning($"{prefix} Trailing stop loss is very loose ({order.TrailingStopLossPercent * 100:F0}%)");
                }
            }

            // ATR stop loss validation
            if (order.AtrStopLoss != null)
            {
                if (order.AtrStopLoss.Multiplier <= 0)
                {
                    result.AddError($"{prefix} ATR multiplier must be positive");
                }
                if (order.AtrStopLoss.Period < 1)
                {
                    result.AddError($"{prefix} ATR period must be at least 1");
                }
            }

            // Time window validation
            if (strategy.StartTime.HasValue && strategy.EndTime.HasValue)
            {
                if (strategy.StartTime.Value >= strategy.EndTime.Value)
                {
                    result.AddError($"{prefix} Start time must be before end time");
                }
            }

            // Risk management check
            if (!order.EnableTakeProfit && !order.EnableStopLoss && !order.EnableTrailingStopLoss)
            {
                result.AddWarning($"{prefix} No exit strategy configured (no TP, SL, or trailing stop)");
            }

            // AllOrNone warning
            if (order.AllOrNone && order.Quantity > 100)
            {
                result.AddWarning($"{prefix} AllOrNone with large quantity ({order.Quantity}) may not fill");
            }
        }

        private static bool TimeWindowsOverlap(TradingStrategy a, TradingStrategy b)
        {
            // If either has no time window, assume they overlap
            if (!a.StartTime.HasValue || !a.EndTime.HasValue ||
                !b.StartTime.HasValue || !b.EndTime.HasValue)
            {
                return true;
            }

            // Check for overlap: a.start < b.end AND b.start < a.end
            return a.StartTime.Value < b.EndTime.Value && b.StartTime.Value < a.EndTime.Value;
        }
    }

    /// <summary>
    /// Result of strategy validation.
    /// </summary>
    public sealed class StrategyValidationResult
    {
        private readonly List<string> _errors = [];
        private readonly List<string> _warnings = [];

        /// <summary>
        /// Gets whether validation passed (no errors).
        /// </summary>
        public bool IsValid => _errors.Count == 0;

        /// <summary>
        /// Gets the list of errors.
        /// </summary>
        public IReadOnlyList<string> Errors => _errors;

        /// <summary>
        /// Gets the list of warnings.
        /// </summary>
        public IReadOnlyList<string> Warnings => _warnings;

        internal void AddError(string message)
        {
            _errors.Add(message);
        }

        internal void AddWarning(string message)
        {
            _warnings.Add(message);
        }

        /// <summary>
        /// Prints validation results to console.
        /// </summary>
        public void PrintResults()
        {
            if (IsValid && _warnings.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{TimeStamp.NowBracketed} All strategies validated successfully");
                StrategyValidator.SessionLogger?.LogEvent("VALIDATOR", "All strategies validated successfully");
                Console.ResetColor();
                return;
            }

            if (_errors.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{TimeStamp.NowBracketed} Validation failed with {_errors.Count} error(s):");
                StrategyValidator.SessionLogger?.LogEvent("VALIDATOR", $"Validation failed with {_errors.Count} error(s)");
                foreach (var error in _errors)
                {
                    Console.WriteLine($"{TimeStamp.NowBracketed}   ERROR: {error}");
                    StrategyValidator.SessionLogger?.LogEvent("VALIDATOR", $"ERROR: {error}");
                }
                Console.ResetColor();
            }

            if (_warnings.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{TimeStamp.NowBracketed} {_warnings.Count} warning(s):");
                StrategyValidator.SessionLogger?.LogEvent("VALIDATOR", $"{_warnings.Count} warning(s)");
                foreach (var warning in _warnings)
                {
                    Console.WriteLine($"{TimeStamp.NowBracketed}   WARN: {warning}");
                    StrategyValidator.SessionLogger?.LogEvent("VALIDATOR", $"WARN: {warning}");
                }
                Console.ResetColor();
            }
        }
    }
}


