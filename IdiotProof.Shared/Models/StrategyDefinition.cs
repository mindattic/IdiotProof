// ============================================================================
// StrategyDefinition - Complete strategy definition for JSON serialization
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Models
{
    /// <summary>
    /// Represents a complete strategy that can be serialized to/from JSON.
    /// </summary>
    public class StrategyDefinition
    {
        /// <summary>
        /// Unique identifier for this strategy.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// User-friendly name for the strategy.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Optional description of the strategy.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The stock symbol this strategy targets.
        /// </summary>
        public required string Symbol { get; set; }

        /// <summary>
        /// Whether this strategy is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether this strategy repeats after completion.
        /// When true, the strategy resets and can fire again when conditions are met.
        /// When false (default), the strategy fires once and stops.
        /// </summary>
        public bool RepeatEnabled { get; set; } = false;

        /// <summary>
        /// The ordered list of segments that make up this strategy.
        /// </summary>
        public List<StrategySegment> Segments { get; set; } = [];

        /// <summary>
        /// User-provided notes for the overall strategy.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Contributor/author of the strategy.
        /// </summary>
        public string? Author { get; set; }

        /// <summary>
        /// Date/time the strategy was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Date/time the strategy was last modified.
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tags for categorization.
        /// </summary>
        public List<string> Tags { get; set; } = [];

        /// <summary>
        /// The original IdiotScript text (without comments).
        /// Preserved from parsing to avoid re-serialization changes.
        /// </summary>
        [JsonIgnore]
        public string? OriginalScript { get; set; }

        /// <summary>
        /// Generates the fluent API code for this strategy.
        /// </summary>
        public string ToFluentCode()
        {
            var lines = new List<string>
            {
                "Stock",
                $"    .Ticker(\"{Symbol}\")"
            };

            foreach (var segment in Segments.Where(s => s.Type != Enums.SegmentType.Ticker).OrderBy(s => s.Order))
            {
                var code = segment.ToFluentCode();
                lines.Add($"    {code}");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Generates the IdiotScript representation of this strategy.
        /// Returns the original script if available, otherwise re-serializes.
        /// </summary>
        public string ToIdiotScript()
        {
            return OriginalScript ?? Scripting.IdiotScriptSerializer.Serialize(this);
        }

        /// <summary>
        /// Generates an ASCII chart visualization of this strategy.
        /// </summary>
        public string ToAsciiChart()
        {
            return Scripting.IdiotScriptChartGenerator.GenerateChart(this);
        }

        /// <summary>
        /// Generates ASCII chart as comment lines (prefixed with #).
        /// </summary>
        public string ToAsciiChartComments()
        {
            return Scripting.IdiotScriptChartGenerator.GenerateChartComments(this);
        }

        /// <summary>
        /// Validates the strategy is complete and well-formed.
        /// Uses the comprehensive validation from Shared.Validation.
        /// </summary>
        public Validation.ValidationResult Validate()
        {
            return Validation.StrategyValidator.ValidateStrategy(this);
        }

        /// <summary>
        /// Gets the calculated statistics for this strategy.
        /// </summary>
        public StrategyStats GetStats()
        {
            var stats = new StrategyStats();

            // Get Order segment (unified type)
            var orderSegment = Segments.FirstOrDefault(s => s.Type == Enums.SegmentType.Order);
            if (orderSegment != null)
            {
                var qtyParam = orderSegment.Parameters.FirstOrDefault(p => p.Name.Equals("Quantity", StringComparison.OrdinalIgnoreCase));
                var limitPriceParam = orderSegment.Parameters.FirstOrDefault(p => p.Name.Equals("LimitPrice", StringComparison.OrdinalIgnoreCase));

                if (qtyParam?.Value != null)
                    stats.Quantity = Convert.ToInt32(qtyParam.Value);

                // If there's an explicit limit price, use it
                if (limitPriceParam?.Value != null)
                    stats.Price = Convert.ToDouble(limitPriceParam.Value);
            }

            // If no explicit price, try to get entry price from price conditions
            if (stats.Price == 0)
            {
                // Check IsPriceAbove (typical for buy entries)
                var priceAboveSegment = Segments.FirstOrDefault(s => s.Type == Enums.SegmentType.IsPriceAbove);
                if (priceAboveSegment != null)
                {
                    var levelParam = priceAboveSegment.Parameters.FirstOrDefault(p => p.Name.Equals("Level", StringComparison.OrdinalIgnoreCase));
                    if (levelParam?.Value != null)
                        stats.Price = Convert.ToDouble(levelParam.Value);
                }

                // Check Breakout as alternative entry signal
                if (stats.Price == 0)
                {
                    var breakoutSegment = Segments.FirstOrDefault(s => s.Type == Enums.SegmentType.Breakout);
                    if (breakoutSegment != null)
                    {
                        var levelParam = breakoutSegment.Parameters.FirstOrDefault(p => p.Name.Equals("Level", StringComparison.OrdinalIgnoreCase));
                        if (levelParam?.Value != null)
                            stats.Price = Convert.ToDouble(levelParam.Value);
                    }
                }
            }

            // Get TakeProfit segment
            var takeProfitSegment = Segments.FirstOrDefault(s => s.Type == Enums.SegmentType.TakeProfit);
            if (takeProfitSegment != null)
            {
                var levelParam = takeProfitSegment.Parameters.FirstOrDefault(p => 
                    p.Name.Equals("Level", StringComparison.OrdinalIgnoreCase) || 
                    p.Name.Equals("Price", StringComparison.OrdinalIgnoreCase));

                if (levelParam?.Value != null)
                    stats.TakeProfit = Convert.ToDouble(levelParam.Value);
            }

            // Get TrailingStopLoss segment
            var trailingStopSegment = Segments.FirstOrDefault(s => s.Type == Enums.SegmentType.TrailingStopLoss);
            if (trailingStopSegment != null)
            {
                var percentParam = trailingStopSegment.Parameters.FirstOrDefault(p => 
                    p.Name.Equals("Percent", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals("Percentage", StringComparison.OrdinalIgnoreCase));

                if (percentParam?.Value != null)
                    stats.TrailingStopLossPercent = Convert.ToDouble(percentParam.Value);
            }

            // Get hard StopLoss segment
            var stopLossSegment = Segments.FirstOrDefault(s => s.Type == Enums.SegmentType.StopLoss);
            if (stopLossSegment != null)
            {
                var levelParam = stopLossSegment.Parameters.FirstOrDefault(p =>
                    p.Name.Equals("Level", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals("Price", StringComparison.OrdinalIgnoreCase));

                if (levelParam?.Value != null)
                    stats.StopLoss = Convert.ToDouble(levelParam.Value);
            }

            // Calculate derived values
            stats.BuyIn = stats.Quantity * stats.Price;

            // Calculate potential loss - use hard stop loss if set, otherwise use trailing stop loss
            if (stats.Price > 0 && stats.StopLoss > 0)
            {
                // Hard stop loss takes precedence as it's a fixed price level
                stats.PotentialLoss = stats.Quantity * (stats.Price - stats.StopLoss);
            }
            else if (stats.Price > 0 && stats.TrailingStopLossPercent > 0)
            {
                var stopPrice = stats.Price * (1 - stats.TrailingStopLossPercent);
                stats.PotentialLoss = stats.Quantity * (stats.Price - stopPrice);
            }

            if (stats.Price > 0 && stats.TakeProfit > 0)
            {
                stats.PotentialGain = stats.Quantity * (stats.TakeProfit - stats.Price);
            }

            return stats;
        }
    }

    /// <summary>
    /// Calculated statistics for a strategy.
    /// </summary>
    public class StrategyStats
    {
        public int Quantity { get; set; }
        public double Price { get; set; }
        public double BuyIn { get; set; }
        public double TakeProfit { get; set; }
        public double StopLoss { get; set; }
        public double TrailingStopLossPercent { get; set; }
        public double PotentialLoss { get; set; }
        public double PotentialGain { get; set; }
    }

    /// <summary>
    /// Collection of strategies for a specific date.
    /// </summary>
    public class StrategyCollection
    {
        /// <summary>
        /// Date this collection is for (strategies are saved by date).
        /// </summary>
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        /// <summary>
        /// List of strategies in this collection.
        /// </summary>
        public List<StrategyDefinition> Strategies { get; set; } = [];

        /// <summary>
        /// Version of the file format.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Last modified timestamp.
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}


