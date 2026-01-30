// ============================================================================
// Strategy - Container for multi-step trading strategy
// ============================================================================
//
// BEST PRACTICES:
// 1. Use the Stock fluent builder to create TradingStrategy instances.
// 2. Always define at least one condition before the order action.
// 3. Use StartTime/EndTime to limit when the strategy monitors the market.
// 4. Ensure Order has both TakeProfit and StopLoss configured.
// 5. Use ClosePositionTime for time-based exits (e.g., end of session).
//
// STRATEGY LIFECYCLE:
// 1. Created via Stock.Ticker()...Build()
// 2. Passed to StrategyRunner for execution
// 3. Conditions evaluated sequentially
// 4. Order executed when all conditions met
// 5. Position managed until exit (TP/SL/Time)
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using IdiotProof.Enums;

namespace IdiotProof.Models
{
    /// <summary>
    /// Represents a complete trading strategy with symbol, conditions, and order action.
    /// This is an immutable container created by the <see cref="Stock"/> fluent builder.
    /// </summary>
    /// <remarks>
    /// <para><b>Best Practices:</b></para>
    /// <list type="bullet">
    ///   <item>Use <see cref="Stock"/> fluent builder instead of direct instantiation.</item>
    ///   <item>Conditions are evaluated sequentially in the order defined.</item>
    ///   <item>Configure risk management via <see cref="Order"/> properties.</item>
    ///   <item>Use time bounds (<see cref="StartTime"/>/<see cref="EndTime"/>) for session control.</item>
    /// </list>
    /// </remarks>
    public sealed class TradingStrategy
    {
        /// <summary>Stock symbol to trade.</summary>
        public required string Symbol { get; init; }

        /// <summary>Exchange (default: SMART).</summary>
        public string Exchange { get; init; } = "SMART";

        /// <summary>Primary exchange for routing (used with SMART routing for OTC stocks).</summary>
        public string? PrimaryExchange { get; init; }

        /// <summary>Currency (default: USD).</summary>
        public string Currency { get; init; } = "USD";

        /// <summary>Security type (default: STK).</summary>
        public string SecType { get; init; } = "STK";

        /// <summary>Ordered list of conditions that must be met sequentially.</summary>
        public required IReadOnlyList<IStrategyCondition> Conditions { get; init; }

        /// <summary>The order to execute when all conditions are met.</summary>
        public required OrderAction Order { get; init; }

        /// <summary>Whether this strategy is enabled.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>Time to start monitoring the strategy (null = immediately).</summary>
        public TimeOnly? StartTime { get; init; }

        /// <summary>Time to stop monitoring the strategy (null = no end time).</summary>
        public TimeOnly? EndTime { get; init; }

        /// <summary>Time to close position if still open (null = no auto-close).</summary>
        public TimeOnly? ClosePositionTime { get; init; }

        /// <summary>
        /// Writes the strategy progress to console with colors.
        /// </summary>
        /// <param name="currentStep">Current step index (0-based).</param>
        public void WriteProgress(int currentStep)
        {
            WriteProgress(currentStep, entryFilled: false, takeProfitFilled: false, currentPrice: 0, entryPrice: 0, takeProfitTarget: 0);
        }

        /// <summary>
        /// Writes the strategy progress to console with colors, including monitoring step.
        /// </summary>
        /// <param name="currentStep">Current step index (0-based).</param>
        /// <param name="entryFilled">Whether entry order was filled.</param>
        /// <param name="takeProfitFilled">Whether take profit was filled.</param>
        /// <param name="currentPrice">Current stock price.</param>
        /// <param name="entryPrice">Entry fill price (for percent change calculation).</param>
        /// <param name="takeProfitTarget">Take profit target price.</param>
        public void WriteProgress(int currentStep, bool entryFilled, bool takeProfitFilled, double currentPrice, double entryPrice, double takeProfitTarget)
        {
            // Symbol line with price and percent change
            Console.Write($"  {Symbol}");
            if (currentPrice > 0)
            {
                Console.Write($"  |  ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"${currentPrice:F2}");
                Console.ResetColor();

                if (entryFilled && entryPrice > 0)
                {
                    double change = currentPrice - entryPrice;
                    double pctChange = (change / entryPrice) * 100;

                    Console.Write("  ");
                    if (change >= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write($"+${change:F2} (+{pctChange:F2}%)");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write($"-${Math.Abs(change):F2} ({pctChange:F2}%)");
                    }
                    Console.ResetColor();
                }
            }
            Console.WriteLine(":");

            for (int i = 0; i < Conditions.Count; i++)
            {
                Console.Write("    ");

                if (currentStep > i)
                {
                    // Completed - default color
                    Console.WriteLine($"{i + 1}.) {Conditions[i].Name}");
                }
                else if (currentStep == i)
                {
                    // Current - green
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{i + 1}.) {Conditions[i].Name}");
                    Console.ResetColor();
                }
                else
                {
                    // Pending - gray
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"{i + 1}.) {Conditions[i].Name}");
                    Console.ResetColor();
                }
            }

            // Format order details
            var tpStr = Order.EnableTakeProfit 
                ? (Order.TakeProfitPrice.HasValue ? $", TakeProfit={Order.TakeProfitPrice:F2}" : $", TakeProfit=+{Order.TakeProfitOffset:F2}") 
                : "";

            Console.Write("    ");
            if (entryFilled || currentStep >= Conditions.Count)
            {
                // Order executed - default
                Console.WriteLine($"{Conditions.Count + 1}.) {Order.Side}={Order.Quantity}{tpStr}, {Order.Type}, {Order.TimeInForce}");
            }
            else
            {
                // Order pending - gray
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"{Conditions.Count + 1}.) {Order.Side}={Order.Quantity}{tpStr}, {Order.Type}, {Order.TimeInForce}");
                Console.ResetColor();
            }

            // Monitoring step - shows current price and take profit status
            if (Order.EnableTakeProfit)
            {
                int monitoringStepNumber = Conditions.Count + 2;
                Console.Write("    ");

                if (takeProfitFilled)
                {
                    // Take profit filled - green
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{monitoringStepNumber}.) Take Profit FILLED @ {takeProfitTarget:F2}");
                    Console.ResetColor();
                }
                else if (entryFilled)
                {
                    // Monitoring active - show current price in yellow/cyan
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"{monitoringStepNumber}.) Watching: ${currentPrice:F2} -> Target: ${takeProfitTarget:F2}");
                    Console.ResetColor();
                }
                else
                {
                    // Waiting for entry - gray
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"{monitoringStepNumber}.) Waiting for take profit...");
                    Console.ResetColor();
                }
            }
        }

        /// <summary>
        /// Gets a display-friendly summary of the strategy.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  {Symbol}:");

            for (int i = 0; i < Conditions.Count; i++)
            {
                sb.AppendLine($"    {i + 1}.) {Conditions[i].Name}");
            }

            var tpStr = Order.EnableTakeProfit 
                ? (Order.TakeProfitPrice.HasValue ? $", TakeProfit={Order.TakeProfitPrice:F2}" : $", TakeProfit=+{Order.TakeProfitOffset:F2}") 
                : "";
            sb.Append($"    {Conditions.Count + 1}.) {Order.Side}={Order.Quantity}{tpStr}, {Order.Type}, {Order.TimeInForce}");

            return sb.ToString();
        }
    }
}
