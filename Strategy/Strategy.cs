// ============================================================================
// Strategy - Container for multi-step trading strategy
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace IdiotProof.Models
{
    /// <summary>
    /// Represents a complete trading strategy with symbol, conditions, and order action.
    /// </summary>
    public sealed class TradingStrategy
    {
        /// <summary>Stock symbol to trade.</summary>
        public required string Symbol { get; init; }

        /// <summary>Exchange (default: SMART).</summary>
        public string Exchange { get; init; } = "SMART";

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
