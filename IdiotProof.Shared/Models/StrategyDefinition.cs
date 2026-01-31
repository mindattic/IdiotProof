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
        /// Validates the strategy is complete and well-formed.
        /// Uses the comprehensive validation from Shared.Validation.
        /// </summary>
        public Validation.ValidationResult Validate()
        {
            return Validation.StrategyValidator.ValidateStrategy(this);
        }
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
