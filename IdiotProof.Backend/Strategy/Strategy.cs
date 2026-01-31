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
using IdiotProof.Backend.Enums;

namespace IdiotProof.Backend.Models
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

        /// <summary>Trading session for display purposes (null = custom times or Always).</summary>
        public TradingSession? Session { get; init; }

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
        /// Writes the strategy as a single-line summary with colors.
        /// Format: SYMBOL | Session | Price: $X.XX | Qty: X | BuyIn: $X.XX | TakeProfit: $X.XX-$Y.YY
        /// </summary>
        /// <param name="currentStep">Current step index (0-based).</param>
        /// <param name="entryFilled">Whether entry order was filled.</param>
        /// <param name="takeProfitFilled">Whether take profit was filled.</param>
        /// <param name="currentPrice">Current stock price.</param>
        /// <param name="entryPrice">Entry fill price (for percent change calculation).</param>
        /// <param name="takeProfitTarget">Take profit target price.</param>
        public void WriteProgress(int currentStep, bool entryFilled, bool takeProfitFilled, double currentPrice, double entryPrice, double takeProfitTarget)
        {
            // Symbol
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"    {Symbol}");
            Console.ResetColor();

            Console.Write(" | ");

            // Duration
            var sessionStr = GetSessionDisplayString();
            Console.Write("Duration: ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(sessionStr);
            Console.ResetColor();

            Console.Write(" | ");

            // Price
            Console.Write("Price: ");
            if (currentPrice > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"${currentPrice:F2}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("...");
                Console.ResetColor();
            }

            Console.Write(" | ");

            // Quantity
            Console.Write("Qty: ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{Order.Quantity}");
            Console.ResetColor();

            Console.Write(" | ");

            // BuyIn (total cost)
            var entryConditionPrice = GetEntryConditionPrice();
            Console.Write("BuyIn: ");
            if (entryConditionPrice.HasValue)
            {
                double totalCost = entryConditionPrice.Value * Order.Quantity;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"${totalCost:N0}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("N/A");
                Console.ResetColor();
            }

            Console.Write(" | ");

            // TakeProfit (total exit value)
            Console.Write("TakeProfit: ");
            if (Order.EnableTakeProfit)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                if (Order.AdxTakeProfit != null)
                {
                    double lowExit = Order.AdxTakeProfit.ConservativeTarget * Order.Quantity;
                    double highExit = Order.AdxTakeProfit.AggressiveTarget * Order.Quantity;
                    Console.Write($"${lowExit:N0}-${highExit:N0}");
                }
                else if (Order.TakeProfitPrice.HasValue)
                {
                    double exitValue = Order.TakeProfitPrice.Value * Order.Quantity;
                    Console.Write($"${exitValue:N0}");
                }
                else
                {
                    Console.Write($"+${Order.TakeProfitOffset:F2}");
                }
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("None");
                Console.ResetColor();
            }

            // P/L if filled
            if (entryFilled && entryPrice > 0 && currentPrice > 0)
            {
                double change = currentPrice - entryPrice;
                double pctChange = (change / entryPrice) * 100;

                Console.Write(" | ");
                if (change >= 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"+${change:F2} (+{pctChange:F1}%)");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"-${Math.Abs(change):F2} ({pctChange:F1}%)");
                }
                Console.ResetColor();
            }

            Console.WriteLine();

            // Display all strategy steps/conditions
            for (int i = 0; i < Conditions.Count; i++)
            {
                Console.Write("    ");
                Console.WriteLine($"{i + 1}.) {Conditions[i].Name}");
            }

            // Order step - build components
            var tpStr = "";
            if (Order.EnableTakeProfit)
            {
                if (Order.AdxTakeProfit != null)
                    tpStr = $", TakeProfit=${Order.AdxTakeProfit.ConservativeTarget:F2}-${Order.AdxTakeProfit.AggressiveTarget:F2}";
                else if (Order.TakeProfitPrice.HasValue)
                    tpStr = $", TakeProfit=${Order.TakeProfitPrice:F2}";
                else
                    tpStr = $", TakeProfit=+${Order.TakeProfitOffset:F2}";
            }

            var slStr = "";
            if (Order.EnableTrailingStopLoss)
                slStr = $", TrailingStopLoss={Order.TrailingStopLossPercent * 100:F0}%";
            else if (Order.EnableStopLoss)
                slStr = Order.StopLossPrice.HasValue
                    ? $", StopLoss=${Order.StopLossPrice:F2}"
                    : $", StopLoss=-${Order.StopLossOffset:F2}";

            // Calculate PotentialLoss if StopLoss or TrailingStopLoss is enabled
            var potentialLoss = "";
            if (Order.EnableTrailingStopLoss || Order.EnableStopLoss)
            {
                var estimatedEntryPrice = entryPrice > 0 ? entryPrice : GetEntryConditionPrice();
                if (estimatedEntryPrice.HasValue && estimatedEntryPrice.Value > 0)
                {
                    double maxLoss;
                    if (Order.EnableTrailingStopLoss)
                    {
                        maxLoss = estimatedEntryPrice.Value * Order.TrailingStopLossPercent * Order.Quantity;
                    }
                    else if (Order.StopLossPrice.HasValue)
                    {
                        maxLoss = (estimatedEntryPrice.Value - Order.StopLossPrice.Value) * Order.Quantity;
                    }
                    else
                    {
                        maxLoss = Order.StopLossOffset * Order.Quantity;
                    }
                    potentialLoss = $", PotentialLoss=${maxLoss:N0}";
                }
            }

            Console.Write("    ");
            Console.WriteLine($"{Conditions.Count + 1}.) {Order.Side} {Order.Quantity} @ {Order.Type}{tpStr}{slStr}{potentialLoss}");

            // Close position time if set
            if (Order.ClosePositionTime.HasValue)
            {
                Console.Write("    ");
                var closeStr = Order.ClosePositionOnlyIfProfitable ? " (if profitable)" : "";
                Console.WriteLine($"{Conditions.Count + 2}.) Close @ {Order.ClosePositionTime.Value:h:mm tt} ET{closeStr}");
            }
        }

        /// <summary>
        /// Gets the entry condition price from the first price-based condition.
        /// </summary>
        private double? GetEntryConditionPrice()
        {
            foreach (var condition in Conditions)
            {
                // Check if the condition name contains a price (e.g., "Price > 2.40", "Pullback <= 4.15")
                var name = condition.Name;
                if (name.Contains("Price >") || name.Contains("Price >=") || 
                    name.Contains("Breakout") || name.Contains("Pullback"))
                {
                    // Extract the number from the condition name
                    var parts = name.Split(' ');
                    foreach (var part in parts)
                    {
                        if (double.TryParse(part, out var price))
                        {
                            return price;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Gets a display string for the session/time window.
        /// </summary>
        private string GetSessionDisplayString()
        {
            if (Session.HasValue)
            {
                return Session.Value switch
                {
                    TradingSession.Active => "Active",
                    TradingSession.PreMarket => "PreMarket",
                    TradingSession.RTH => "RTH",
                    TradingSession.AfterHours => "AfterHours",
                    TradingSession.Extended => "Extended",
                    TradingSession.PreMarketEndEarly => "PreMarketEndEarly",
                    TradingSession.PreMarketStartLate => "PreMarketStartLate",
                    TradingSession.RTHEndEarly => "RTHEndEarly",
                    TradingSession.RTHStartLate => "RTHStartLate",
                    TradingSession.AfterHoursEndEarly => "AfterHoursEndEarly",
                    _ => Session.Value.ToString()
                };
            }

            if (StartTime.HasValue && EndTime.HasValue)
            {
                return $"{StartTime.Value:h\\:mm}-{EndTime.Value:h\\:mm}";
            }

            if (StartTime.HasValue)
            {
                return $"From {StartTime.Value:h\\:mm}";
            }

            if (EndTime.HasValue)
            {
                return $"Until {EndTime.Value:h\\:mm}";
            }

            return "Active";
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
