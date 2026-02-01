// ============================================================================
// StrategyLoader - Loads strategies from JSON files for the backend service
// ============================================================================
//
// This class bridges the MAUI frontend's JSON strategy files with the
// backend's fluent API TradingStrategy objects.
//
// Strategies are stored as individual JSON files in date-based folders:
//   Strategies/
//     2025-01-15/
//       VIVS_Breakout.json
//       CATX_VWAP_Scalp.json
//     2025-01-16/
//       ...
//
// USAGE IN Program.cs:
//   // Load strategies from JSON files instead of hardcoding them:
//   var strategies = StrategyLoader.LoadFromJson();
//   
//   // Or use a hybrid approach:
//   var strategies = new List<TradingStrategy>();
//   strategies.AddRange(StrategyLoader.LoadFromJson()); // From UI
//   strategies.Add(Stock.Ticker("AAPL")...);            // Hardcoded
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Strategy;
using System.Text.Json;

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// JSON model for loading individual strategy files from the MAUI frontend.
    /// </summary>
    public class JsonStrategyDefinition
    {
        public Guid Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public required string Symbol { get; set; }
        public bool Enabled { get; set; } = true;
        public List<JsonStrategySegment> Segments { get; set; } = [];
        public string? Author { get; set; }
    }

    public class JsonStrategySegment
    {
        public Guid Id { get; set; }
        public required string Type { get; set; }
        public required string Category { get; set; }
        public required string DisplayName { get; set; }
        public List<JsonSegmentParameter> Parameters { get; set; } = [];
        public int Order { get; set; }
    }

    public class JsonSegmentParameter
    {
        public required string Name { get; set; }
        public required string Type { get; set; }
        public object? Value { get; set; }
    }

    /// <summary>
    /// Loads strategy definitions from JSON files and converts them to TradingStrategy objects.
    /// </summary>
    public static class StrategyLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        /// <summary>
        /// Converts a StrategyDefinition (from Shared project) to a TradingStrategy.
        /// </summary>
        /// <param name="definition">The strategy definition to convert.</param>
        /// <returns>The converted TradingStrategy, or null if conversion fails.</returns>
        public static TradingStrategy? ConvertDefinition(IdiotProof.Shared.Models.StrategyDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Symbol) || definition.Segments.Count == 0)
                return null;

            // Convert Shared.StrategySegment to local JsonStrategySegment
            var jsonDef = new JsonStrategyDefinition
            {
                Id = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                Symbol = definition.Symbol,
                Enabled = definition.Enabled,
                Author = definition.Author,
                Segments = definition.Segments.Select(s => new JsonStrategySegment
                {
                    Id = s.Id,
                    Type = s.Type.ToString(),
                    Category = s.Category.ToString(),
                    DisplayName = s.DisplayName,
                    Order = s.Order,
                    Parameters = s.Parameters.Select(p => new JsonSegmentParameter
                    {
                        Name = p.Name,
                        Type = p.Type.ToString(),
                        Value = p.Value
                    }).ToList()
                }).ToList()
            };

            return ConvertToTradingStrategy(jsonDef);
        }

        /// <summary>
        /// Gets the default folder for strategy files.
        /// </summary>
        public static string GetDefaultFolder()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "IdiotProof", "Strategies");
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
        /// Loads all enabled strategies from today's JSON file and converts them to TradingStrategy objects.
        /// </summary>
        /// <returns>List of TradingStrategy objects ready for execution.</returns>
        public static List<TradingStrategy> LoadFromJson()
        {
            return LoadFromJson(DateOnly.FromDateTime(DateTime.Today));
        }

        /// <summary>
        /// Loads all enabled strategies from the specified date's folder.
        /// Each strategy is stored as an individual JSON file.
        /// </summary>
        /// <param name="date">The date to load strategies for.</param>
        /// <returns>List of TradingStrategy objects ready for execution.</returns>
        public static List<TradingStrategy> LoadFromJson(DateOnly date)
        {
            var dateFolder = GetDateFolder(date);

            if (!Directory.Exists(dateFolder))
            {
                Console.WriteLine($"No strategy folder found for {date:yyyy-MM-dd}");
                return [];
            }

            var strategies = new List<TradingStrategy>();
            var files = Directory.GetFiles(dateFolder, "*.json");

            Console.WriteLine($"Found {files.Length} strategy files in {dateFolder}");

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var definition = JsonSerializer.Deserialize<JsonStrategyDefinition>(json, JsonOptions);

                    if (definition == null)
                        continue;

                    if (!definition.Enabled)
                    {
                        Console.WriteLine($"Skipping disabled strategy: {definition.Name}");
                        continue;
                    }

                    var strategy = ConvertToTradingStrategy(definition);
                    if (strategy != null)
                    {
                        strategies.Add(strategy);
                        Console.WriteLine($"Loaded strategy: {definition.Name} ({definition.Symbol})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading strategy from {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            Console.WriteLine($"Loaded {strategies.Count} enabled strategies from {dateFolder}");
            return strategies;
        }

        /// <summary>
        /// Converts a JsonStrategyDefinition into a fluent API TradingStrategy.
        /// </summary>
        private static TradingStrategy? ConvertToTradingStrategy(JsonStrategyDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.Symbol) || definition.Segments.Count == 0)
                return null;

            // Start building the strategy
            var builder = Stock.Ticker(definition.Symbol)
                .Enabled(definition.Enabled);

            // Track if we've added an order (Buy/Sell/Close)
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
        private static object ApplySegment(Stock builder, JsonStrategySegment segment)
        {
            return segment.Type switch
            {
                "Ticker" => builder, // Already applied via Stock.Ticker()

                "SessionDuration" => ApplySessionDuration(builder, segment),

                "Breakout" => builder.Breakout(GetDouble(segment, "Level")),
                "Pullback" => builder.Pullback(GetDouble(segment, "Level")),
                "IsPriceAbove" => builder.IsPriceAbove(GetDouble(segment, "Level")),
                "IsPriceBelow" => builder.IsPriceBelow(GetDouble(segment, "Level")),

                "IsAboveVwap" => builder.IsAboveVwap(GetDouble(segment, "Buffer", 0)),
                "IsBelowVwap" => builder.IsBelowVwap(GetDouble(segment, "Buffer", 0)),

                "IsRsi" => builder.IsRsi(
                    GetEnum<RsiState>(segment, "State"),
                    GetNullableDouble(segment, "Threshold")),
                "IsMacd" => builder.IsMacd(GetEnum<MacdState>(segment, "State")),
                "IsAdx" => builder.IsAdx(
                    GetEnum<Comparison>(segment, "Comparison"),
                    GetDouble(segment, "Threshold", 25)),
                "IsDI" => builder.IsDI(
                    GetEnum<DiDirection>(segment, "Direction"),
                    GetDouble(segment, "MinDifference", 0)),

                // EMA conditions - use custom conditions since Backend doesn't have EMA methods
                "IsEmaAbove" => builder.When(
                    $"Price >= EMA({GetInt(segment, "Period")})",
                    (price, vwap) => true), // EMA conditions pass through - evaluated by indicator service
                "IsEmaBelow" => builder.When(
                    $"Price <= EMA({GetInt(segment, "Period")})",
                    (price, vwap) => true), // EMA conditions pass through - evaluated by indicator service
                "IsEmaBetween" => builder.When(
                    $"Price between EMA({GetInt(segment, "LowerPeriod")}) and EMA({GetInt(segment, "UpperPeriod")})",
                    (price, vwap) => true), // EMA conditions pass through - evaluated by indicator service

                "Buy" => builder.Buy(
                    GetInt(segment, "Quantity", 1),
                    GetEnum<Price>(segment, "PriceType"),
                    GetEnum<OrderType>(segment, "OrderType")),
                "Sell" => builder.Sell(
                    GetInt(segment, "Quantity", 1),
                    GetEnum<Price>(segment, "PriceType"),
                    GetEnum<OrderType>(segment, "OrderType")),
                "Close" => builder.Close(
                    GetInt(segment, "Quantity", 1),
                    GetEnum<OrderSide>(segment, "PositionSide")),

                _ => builder
            };
        }

        /// <summary>
        /// Applies a segment to the StrategyBuilder (post-order segments).
        /// </summary>
        private static StrategyBuilder ApplyPostOrderSegment(StrategyBuilder builder, JsonStrategySegment segment)
        {
            return segment.Type switch
            {
                "TakeProfit" => builder.TakeProfit(GetDouble(segment, "Price")),
                "TakeProfitRange" => builder.TakeProfit(
                    GetDouble(segment, "LowPrice"),
                    GetDouble(segment, "HighPrice")),
                "StopLoss" => builder.StopLoss(GetDouble(segment, "Price")),
                "TrailingStopLoss" => builder.TrailingStopLoss(GetDouble(segment, "Percentage", 0.10)),
                "ClosePosition" => builder.ClosePosition(
                    GetTime(segment, "Time"),
                    GetBool(segment, "OnlyIfProfitable", true)),
                "TimeInForce" => builder.TimeInForce(GetEnum<TimeInForce>(segment, "Type")),
                "OutsideRTH" => builder.OutsideRTH(
                    GetBool(segment, "Allow", true),
                    GetBool(segment, "TakeProfit", true)),
                "AllOrNone" => builder.AllOrNone(GetBool(segment, "AllOrNone", true)),
                _ => builder
            };
        }

        /// <summary>
        /// Applies SessionDuration segment.
        /// </summary>
        private static Stock ApplySessionDuration(Stock builder, JsonStrategySegment segment)
        {
            var sessionStr = GetString(segment, "Session");
            if (Enum.TryParse<TradingSession>(sessionStr, out var session))
            {
                return builder.TimeFrame(session);
            }
            return builder;
        }

        // Helper methods to extract parameter values from JSON
        private static string GetString(JsonStrategySegment segment, string name, string defaultValue = "")
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name == name);
            if (param?.Value == null)
                return defaultValue;

            if (param.Value is JsonElement jsonElement)
                return jsonElement.GetString() ?? defaultValue;
            
            return param.Value.ToString() ?? defaultValue;
        }

        private static double GetDouble(JsonStrategySegment segment, string name, double defaultValue = 0)
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name == name);
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

        private static double? GetNullableDouble(JsonStrategySegment segment, string name)
        {
            var value = GetDouble(segment, name, 0);
            return value == 0 ? null : value;
        }

        private static int GetInt(JsonStrategySegment segment, string name, int defaultValue = 0)
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name == name);
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

        private static bool GetBool(JsonStrategySegment segment, string name, bool defaultValue = false)
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name == name);
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

        private static T GetEnum<T>(JsonStrategySegment segment, string name) where T : struct, Enum
        {
            var strValue = GetString(segment, name);
            if (Enum.TryParse<T>(strValue, out var result))
                return result;
            return default;
        }

        private static TimeOnly GetTime(JsonStrategySegment segment, string name)
        {
            var value = GetString(segment, name);
            if (TimeOnly.TryParse(value, out var time))
                return time;
            return new TimeOnly(9, 20); // Default to 9:20 AM ET
        }
    }
}
