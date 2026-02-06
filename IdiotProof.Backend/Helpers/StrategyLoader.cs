// ============================================================================
// StrategyLoader - Loads strategies from IdiotScript files
// ============================================================================
//
// This class converts strategy definitions into executable TradingStrategy
// objects. The backend reads strategies retrieved from the frontend, which
// loads them from .idiot files.
//
// DATA FLOW: .idiot files → Frontend → Backend
//
// Strategies are stored as .IDIOT files (IdiotScript format):
//   Strategies/
//     GME.idiot
//     SMCI.idiot
//
// IDIOTSCRIPT FORMAT (fluent chained syntax):
// Each .idiot file contains a strategy with commands evaluated sequentially:
//
//   Ticker(GME).Name("GME HOD Breakout").Session(IS.PREMARKET).Qty(150)
//     .Entry(26.00).Breakout(26.15).Pullback().AboveVwap()
//     .TakeProfit(27.00).TrailingStopLoss(5%).ClosePosition(IS.BELL)
//
// EXECUTION ORDER:
//   [CONFIG] → [ENTRY CONDITIONS] → ✅ BUY → [EXIT CONDITIONS]
//
//   CONFIG:           Ticker, Name, Session, Qty
//   ENTRY CONDITIONS: Entry, Breakout, Pullback, AboveVwap (sequential!)
//   EXIT CONDITIONS:  TakeProfit, TrailingStopLoss, ClosePosition
//
// USAGE IN Program.cs:
//   var strategies = StrategyLoader.LoadFromFile();
//   
//   // Or use a hybrid approach:
//   var strategies = new List<TradingStrategy>();
//   strategies.AddRange(StrategyLoader.LoadFromFile()); // From .idiot files
//   strategies.Add(Stock.Ticker("AAPL")...);            // Hardcoded
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Logging;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using IdiotProof.Shared.Constants;
using IdiotProof.Shared.Helpers;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Scripting;
using IdiotProof.Shared.Settings;
using System.Text.Json;

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// Loads strategy definitions from IdiotScript files and converts them to TradingStrategy objects.
    /// </summary>
    public static class StrategyLoader
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
            SessionLogger?.LogEvent("LOADER", message);
        }

        /// <summary>
        /// Logs a message with color to both console and session log file.
        /// </summary>
        private static void Log(string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"{TimeStamp.NowBracketed} {message}");
            Console.ForegroundColor = originalColor;
            SessionLogger?.LogEvent("LOADER", message);
        }

        /// <summary>
        /// Converts a StrategyDefinition (from Shared project) to a TradingStrategy.
        /// </summary>
        /// <param name="definition">The strategy definition to convert.</param>
        /// <returns>The converted TradingStrategy, or null if conversion fails.</returns>
        public static TradingStrategy? ConvertDefinition(StrategyDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Symbol) || definition.Segments.Count == 0)
                return null;

            return ConvertToTradingStrategy(definition);
        }

        /// <summary>
        /// Gets the default folder for strategy files.
        /// </summary>
        public static string GetDefaultFolder()
        {
            return SettingsManager.GetStrategiesFolder();
        }

        /// <summary>
        /// Gets the date folder for a specific date.
        /// </summary>
        public static string GetDateFolder(DateOnly date, string? baseFolder = null)
        {
            baseFolder ??= GetDefaultFolder();
            return Path.Combine(baseFolder, date.ToString("yyyy-MM-dd"));
        }

        /// <summary>
        /// Loads all enabled strategies from the main Strategies folder and converts them to TradingStrategy objects.
        /// </summary>
        /// <returns>List of TradingStrategy objects ready for execution.</returns>
        public static List<TradingStrategy> LoadFromFile()
        {
            var strategiesFolder = GetDefaultFolder();

            if (!Directory.Exists(strategiesFolder))
            {
                Log($"No strategy folder found at {strategiesFolder}");
                return [];
            }

            var strategies = new List<TradingStrategy>();

            var idiotFiles = Directory.GetFiles(strategiesFolder, "*.idiot");
            Log($"Found {idiotFiles.Length} IdiotScript files in {strategiesFolder}");

            foreach (var file in idiotFiles)
            {
                try
                {
                    var script = File.ReadAllText(file);

                    if (!IdiotScriptParser.TryParse(script, out var definition, out var error))
                    {
                        Log($"Error parsing IdiotScript {Path.GetFileName(file)}: {error}");
                        continue;
                    }

                    if (definition == null)
                        continue;

                    if (!definition.Enabled)
                    {
                        Log($"Skipping disabled strategy: {definition.Name}");
                        continue;
                    }

                    var strategy = ConvertDefinition(definition);
                    if (strategy != null)
                    {
                        strategies.Add(strategy);
                        Log($"Loaded IdiotScript: {definition.Name} ({definition.Symbol})");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error loading IdiotScript from {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            Log($"Loaded {strategies.Count} enabled strategies from {strategiesFolder}");
            return strategies;
        }

        /// <summary>
        /// Converts a StrategyDefinition into a fluent API TradingStrategy.
        /// </summary>
        private static TradingStrategy? ConvertToTradingStrategy(StrategyDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.Symbol) || definition.Segments.Count == 0)
                return null;

            // Start building the strategy
            var builder = Stock.Ticker(definition.Symbol)
                .WithId(definition.Id)
                .WithName(definition.Name)
                .Enabled(definition.Enabled);

            // Track if we've added an order (Long/Short/Close)
            StrategyBuilder? orderBuilder = null;

            foreach (var segment in definition.Segments.OrderBy(s => s.Order))
            {
                // If we already have an order builder, apply post-order segments
                if (orderBuilder != null)
                {
                    orderBuilder = ApplyPostOrderSegment(orderBuilder, segment);
                }
                else
                {
                    // Apply pre-order segments to Stock builder
                    var result = ApplySegment(builder, segment);
                    if (result is StrategyBuilder sb)
                    {
                        orderBuilder = sb;
                    }
                    else if (result is Stock s)
                    {
                        builder = s;
                    }
                }
            }

            // Build the final strategy
            return orderBuilder?.Build();
        }

        /// <summary>
        /// Applies a segment to the Stock builder (pre-order segments).
        /// </summary>
        private static object ApplySegment(Stock builder, StrategySegment segment)
        {
            var segmentType = segment.Type.ToString();

            return segmentType switch
            {
                "Ticker" => builder, // Already applied via Stock.Ticker()

                // Session configuration - all handled via TimeFrame
                "SessionDuration" or "TimeFrame" or "Session" => ApplySessionDuration(builder, segment),
                "Start" => builder.Start(GetTime(segment, "Time")),
                "End" => builder, // End is handled as post-order segment (terminal)

                // Price conditions
                "Breakout" => builder.Breakout(GetDouble(segment, "Level")),
                "Pullback" => builder.Pullback(GetDouble(segment, "Level")),
                "IsPriceAbove" => builder.IsPriceAbove(GetDouble(segment, "Level")),
                "IsPriceBelow" => builder.IsPriceBelow(GetDouble(segment, "Level")),
                "GapUp" => builder.GapUp(GetDouble(segment, "Percentage", 5)),
                "GapDown" => builder.GapDown(GetDouble(segment, "Percentage", 5)),

                // VWAP conditions
                "IsAboveVwap" => builder.IsAboveVwap(GetDouble(segment, "Buffer", 0)),
                "IsBelowVwap" => builder.IsBelowVwap(GetDouble(segment, "Buffer", 0)),

                // Indicator conditions - handle parameter name variations from parser
                "IsRsi" => ApplyRsiCondition(builder, segment),
                "IsMacd" => builder.IsMacd(GetEnum<MacdState>(segment, "State")),
                "IsAdx" => ApplyAdxCondition(builder, segment),
                "IsDI" => ApplyDiCondition(builder, segment),

                // EMA conditions - use actual EMA condition classes with callback hooks
                "IsEmaAbove" => builder.WithCondition(
                    new Strategy.EmaAboveCondition(GetInt(segment, "Period"))),
                "IsEmaBelow" => builder.WithCondition(
                    new Strategy.EmaBelowCondition(GetInt(segment, "Period"))),
                "IsEmaBetween" => builder.WithCondition(
                    new Strategy.EmaBetweenCondition(GetInt(segment, "LowerPeriod"), GetInt(segment, "UpperPeriod"))),
                "IsEmaTurningUp" => builder.WithCondition(
                    new Strategy.EmaTurningUpCondition(GetInt(segment, "Period"))),

                // Momentum conditions - unified IsMomentum with Condition parameter (Above/Below)
                // Parser always produces SegmentType.IsMomentum, ApplyMomentumCondition routes to correct class
                "IsMomentum" => ApplyMomentumCondition(builder, segment),

                // Rate of Change conditions - unified IsRoc with Condition parameter (Above/Below)
                // Parser always produces SegmentType.IsRoc, ApplyRocCondition routes to correct class
                "IsRoc" => ApplyRocCondition(builder, segment),

                // Pattern conditions
                "IsHigherLows" => builder.WithCondition(
                    new Strategy.HigherLowsCondition(GetInt(segment, "LookbackBars", 3))),

                // Volume conditions
                "IsVolumeAbove" => builder.WithCondition(
                    new Strategy.VolumeAboveCondition(GetDouble(segment, "Multiplier", 1.5))),

                // VWAP pattern conditions
                "IsCloseAboveVwap" => builder.WithCondition(
                    new Strategy.CloseAboveVwapCondition()),
                "IsVwapRejection" => builder.WithCondition(
                    new Strategy.VwapRejectionCondition()),

                // Order actions - using single-responsibility methods
                // Order segment with direction parameter
                "Order" => ApplyOrderSegment(builder, segment),
                "Long" => builder.Long()
                    .Quantity(GetInt(segment, "Quantity", 1))
                    .PriceType(GetEnum<Price>(segment, "PriceType"))
                    .OrderType(GetEnum<OrderType>(segment, "OrderType")),
                "Short" => builder.Short()
                    .Quantity(GetInt(segment, "Quantity", 1))
                    .PriceType(GetEnum<Price>(segment, "PriceType"))
                    .OrderType(GetEnum<OrderType>(segment, "OrderType")),
                "Close" or "CloseLong" => builder.CloseLong()
                    .Quantity(GetInt(segment, "Quantity", 1))
                    .PriceType(GetEnum<Price>(segment, "PriceType"))
                    .OrderType(GetEnum<OrderType>(segment, "OrderType")),
                "CloseShort" => builder.CloseShort()
                    .Quantity(GetInt(segment, "Quantity", 1))
                    .PriceType(GetEnum<Price>(segment, "PriceType"))
                    .OrderType(GetEnum<OrderType>(segment, "OrderType")),

                _ => LogUnknownSegmentAndReturn(builder, segmentType, "pre-order")
            };
        }

        /// <summary>
        /// Applies a segment to the StrategyBuilder (post-order segments).
        /// </summary>
        private static StrategyBuilder ApplyPostOrderSegment(StrategyBuilder builder, StrategySegment segment)
        {
            var segmentType = segment.Type.ToString();

            return segmentType switch
            {
                // Risk management
                "TakeProfit" => builder.TakeProfit(GetDouble(segment, "Price")),
                "TakeProfitRange" => builder.AdxTakeProfit(
                    AdxTakeProfitConfig.FromRange(GetDouble(segment, "LowPrice"), GetDouble(segment, "HighPrice"))),
                "StopLoss" => builder.StopLoss(GetDouble(segment, "Price")),
                "TrailingStopLoss" => builder.TrailingStopLoss(GetDouble(segment, "Percentage", 0.10)),
                "TrailingStopLossAtr" => builder.TrailingStopLoss(new AtrStopLossConfig
                {
                    Multiplier = GetDouble(segment, "Multiplier", 2.0),
                    Period = GetInt(segment, "Period", 14)
                }),
                "AdaptiveOrder" => builder.AdaptiveOrder(GetString(segment, "Mode", "Balanced")),
                "AutonomousTrading" => builder.AutonomousTrading(GetString(segment, "Mode", "Balanced")),

                // Position management - single responsibility pattern
                "ExitStrategy" => builder.ExitStrategy(GetTime(segment, "Time")),
                "IsProfitable" => builder.IsProfitable(),

                // Order configuration
                "TimeInForce" => builder.TimeInForce(GetEnum<TimeInForce>(segment, "Type")),
                "OutsideRTH" => builder
                    .OutsideRTH(GetBool(segment, "Allow", true))
                    .TakeProfitOutsideRTH(GetBool(segment, "TakeProfit", true)),
                "AllOrNone" => builder.AllOrNone(GetBool(segment, "AllOrNone", true)),
                "OrderType" => builder, // Order type is handled in Buy/Sell segments

                // Execution behavior
                "Repeat" => builder.Repeat(GetBool(segment, "Enabled", true)),

                _ => LogUnknownSegmentAndReturn(builder, segmentType, "post-order")
            };
        }

        /// <summary>
        /// Logs a warning for unknown segment types and returns the builder unchanged.
        /// This prevents silent failures where conditions are dropped without notice.
        /// </summary>
        private static T LogUnknownSegmentAndReturn<T>(T builder, string segmentType, string context)
        {
            Log($"WARN: Unknown {context} segment type '{segmentType}' - condition DROPPED!", ConsoleColor.Yellow);
            return builder;
        }

        /// <summary>
        /// Applies SessionDuration segment.
        /// </summary>
        private static Stock ApplySessionDuration(Stock builder, StrategySegment segment)
        {
            var sessionStr = GetString(segment, "Session");
            if (Enum.TryParse<TradingSession>(sessionStr, out var session))
            {
                return builder.TimeFrame(session);
            }
            return builder;
        }

        /// <summary>
        /// Applies Order segment with direction parameter.
        /// </summary>
        private static StrategyBuilder ApplyOrderSegment(Stock builder, StrategySegment segment)
        {
            var direction = GetString(segment, "Direction", "Long");
            var quantity = GetInt(segment, "Quantity", 1);
            var priceType = GetEnum<Price>(segment, "PriceType");
            var orderType = GetEnum<OrderType>(segment, "OrderType");

            // Check if direction indicates Short
            if (direction.Equals("Short", StringComparison.OrdinalIgnoreCase) ||
                direction.Equals("IS.SHORT", StringComparison.OrdinalIgnoreCase))
            {
                return builder.Short()
                    .Quantity(quantity)
                    .PriceType(priceType)
                    .OrderType(orderType);
            }

            // Default to Long
            return builder.Long()
                .Quantity(quantity)
                .PriceType(priceType)
                .OrderType(orderType);
        }

        /// <summary>
        /// Applies IsMomentum segment by reading the Condition parameter (Above/Below).
        /// </summary>
        private static Stock ApplyMomentumCondition(Stock builder, StrategySegment segment)
        {
            var condition = GetString(segment, "Condition", "Above");
            var threshold = GetDouble(segment, "Threshold", 0);

            if (condition.Equals("Below", StringComparison.OrdinalIgnoreCase))
            {
                return builder.WithCondition(new Strategy.MomentumBelowCondition(threshold));
            }

            return builder.WithCondition(new Strategy.MomentumAboveCondition(threshold));
        }

        /// <summary>
        /// Applies IsRoc segment by reading the Condition parameter (Above/Below).
        /// </summary>
        private static Stock ApplyRocCondition(Stock builder, StrategySegment segment)
        {
            var condition = GetString(segment, "Condition", "Above");
            var threshold = GetDouble(segment, "Threshold", 0);

            if (condition.Equals("Below", StringComparison.OrdinalIgnoreCase))
            {
                return builder.WithCondition(new Strategy.RocBelowCondition(threshold));
            }

            return builder.WithCondition(new Strategy.RocAboveCondition(threshold));
        }

        /// <summary>
        /// Applies IsRsi segment by reading parameters with fallback for different naming conventions.
        /// </summary>
        private static Stock ApplyRsiCondition(Stock builder, StrategySegment segment)
        {
            var conditionStr = GetString(segment, "Condition");
            var stateStr = GetString(segment, "State");
            var threshold = GetDouble(segment, "Value", GetDouble(segment, "Threshold", 0));

            RsiState state;
            if (!string.IsNullOrEmpty(conditionStr))
            {
                state = conditionStr.Equals("Below", StringComparison.OrdinalIgnoreCase)
                    ? RsiState.Oversold
                    : RsiState.Overbought;
            }
            else if (!string.IsNullOrEmpty(stateStr) && Enum.TryParse<RsiState>(stateStr, out var parsedState))
            {
                state = parsedState;
            }
            else
            {
                state = RsiState.Oversold;
            }

            return builder.IsRsi(state, threshold > 0 ? threshold : null);
        }

        /// <summary>
        /// Applies IsAdx segment by reading parameters with fallback for different naming conventions.
        /// </summary>
        private static Stock ApplyAdxCondition(Stock builder, StrategySegment segment)
        {
            var conditionStr = GetString(segment, "Condition");
            var comparisonStr = GetString(segment, "Comparison");
            var threshold = GetDouble(segment, "Value", GetDouble(segment, "Threshold", 25));

            Comparison comparison;
            if (!string.IsNullOrEmpty(conditionStr))
            {
                comparison = conditionStr.Equals("Below", StringComparison.OrdinalIgnoreCase)
                    ? Comparison.Lte
                    : Comparison.Gte;
            }
            else if (!string.IsNullOrEmpty(comparisonStr) && Enum.TryParse<Comparison>(comparisonStr, out var parsedComparison))
            {
                comparison = parsedComparison;
            }
            else
            {
                comparison = Comparison.Gte;
            }

            return builder.IsAdx(comparison, threshold);
        }

        /// <summary>
        /// Applies IsDI segment by reading parameters with fallback for different naming conventions.
        /// </summary>
        private static Stock ApplyDiCondition(Stock builder, StrategySegment segment)
        {
            var direction = GetEnum<DiDirection>(segment, "Direction");
            var minDifference = GetDouble(segment, "Threshold", GetDouble(segment, "MinDifference", 0));

            return builder.IsDI(direction, minDifference);
        }

        // ========================================================================
        // HELPER METHODS - Extract parameter values from StrategySegment
        // ========================================================================

        private static string GetString(StrategySegment segment, string name, string defaultValue = "")
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (param?.Value == null)
                return defaultValue;

            if (param.Value is JsonElement jsonElement)
                return jsonElement.GetString() ?? defaultValue;

            return param.Value.ToString() ?? defaultValue;
        }

        private static double GetDouble(StrategySegment segment, string name, double defaultValue = 0)
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (param?.Value == null)
                return defaultValue;

            if (param.Value is JsonElement jsonElement)
            {
                if (jsonElement.TryGetDouble(out var d))
                    return d;
                return defaultValue;
            }
            
            if (double.TryParse(param.Value.ToString(), out var result))
                return result;
            
            return defaultValue;
        }

        private static int GetInt(StrategySegment segment, string name, int defaultValue = 0)
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (param?.Value == null)
                return defaultValue;

            if (param.Value is JsonElement jsonElement)
            {
                if (jsonElement.TryGetInt32(out var i))
                    return i;
                return defaultValue;
            }
            
            if (int.TryParse(param.Value.ToString(), out var result))
                return result;
            
            return defaultValue;
        }

        private static bool GetBool(StrategySegment segment, string name, bool defaultValue = false)
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (param?.Value == null)
                return defaultValue;

            if (param.Value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.True)
                    return true;
                if (jsonElement.ValueKind == JsonValueKind.False)
                    return false;
                return defaultValue;
            }
            
            if (bool.TryParse(param.Value.ToString(), out var result))
                return result;
            
            return defaultValue;
        }

        private static T GetEnum<T>(StrategySegment segment, string name) where T : struct, Enum
        {
            var strValue = GetString(segment, name);
            if (Enum.TryParse<T>(strValue, out var result))
                return result;
            return default;
        }

        private static TimeOnly GetTime(StrategySegment segment, string name)
        {
            var value = GetString(segment, name);
            if (TimeOnly.TryParse(value, out var time))
                return time;
            return new TimeOnly(9, 20); // Default to 9:20 AM ET
        }

        // ========================================================================
        // IDIOTSCRIPT SAVE/EXPORT METHODS
        // ========================================================================

        /// <summary>
        /// Saves a StrategyDefinition as an IdiotScript file.
        /// </summary>
        public static async Task<string> SaveAsIdiotScriptAsync(
            StrategyDefinition strategy,
            DateOnly date,
            string? baseFolder = null)
        {
            return await IdiotScriptFileManager.SaveStrategyAsync(strategy, date, baseFolder);
        }

        /// <summary>
        /// Saves multiple strategies as IdiotScript files.
        /// </summary>
        public static async Task SaveAllAsIdiotScriptAsync(
            IEnumerable<StrategyDefinition> strategies,
            DateOnly date,
            string? baseFolder = null)
        {
            await IdiotScriptFileManager.SaveStrategiesAsync(strategies, date, baseFolder);
        }

        /// <summary>
        /// Exports a strategy to IdiotScript text.
        /// </summary>
        public static string ExportToIdiotScript(StrategyDefinition strategy)
        {
            return IdiotScriptFileManager.ExportToScript(strategy);
        }

        /// <summary>
        /// Imports a strategy from IdiotScript text.
        /// </summary>
        public static StrategyDefinition? ImportFromIdiotScript(string script)
        {
            return IdiotScriptFileManager.ImportFromScript(script);
        }
    }
}


