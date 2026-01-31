// ============================================================================
// StrategySegment - Represents a single segment in a strategy chain
// ============================================================================

using IdiotProof.Shared.Enums;
using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Models
{
    /// <summary>
    /// Represents a single draggable segment in the strategy builder.
    /// </summary>
    public class StrategySegment
    {
        /// <summary>
        /// Unique identifier for this segment instance.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The type of segment.
        /// </summary>
        public required SegmentType Type { get; set; }

        /// <summary>
        /// The category this segment belongs to.
        /// </summary>
        public required SegmentCategory Category { get; set; }

        /// <summary>
        /// Display name for the segment.
        /// </summary>
        public required string DisplayName { get; set; }

        /// <summary>
        /// Short description of what this segment does.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Parameters for this segment.
        /// </summary>
        public List<SegmentParameter> Parameters { get; set; } = [];

        /// <summary>
        /// Order position in the strategy chain.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Icon name for display (Material icon name).
        /// </summary>
        public string? Icon { get; set; }

        /// <summary>
        /// Color for the segment card (hex color).
        /// </summary>
        public string? Color { get; set; }

        /// <summary>
        /// Whether this segment is valid (all required parameters filled).
        /// </summary>
        [JsonIgnore]
        public bool IsValid => Parameters.Where(p => p.IsRequired).All(p => p.Value != null);

        /// <summary>
        /// Gets the fluent API method call string for this segment.
        /// </summary>
        public string ToFluentCode()
        {
            var paramValues = Parameters
                .Where(p => p.Value != null)
                .Select(p => FormatParameterValue(p))
                .ToArray();

            var methodName = Type.ToString();
            
            if (paramValues.Length == 0)
                return $".{methodName}()";

            return $".{methodName}({string.Join(", ", paramValues)})";
        }

        private static string FormatParameterValue(SegmentParameter param)
        {
            return param.Type switch
            {
                ParameterType.String => $"\"{param.Value}\"",
                ParameterType.Boolean => param.Value?.ToString()?.ToLower() ?? "false",
                ParameterType.Enum => $"{param.EnumTypeName}.{param.Value}",
                ParameterType.Time => $"new TimeOnly({((TimeOnly)param.Value!).Hour}, {((TimeOnly)param.Value!).Minute})",
                ParameterType.Percentage => FormatPercentage(param.Value),
                ParameterType.Price or ParameterType.Double => FormatDouble(param.Value),
                ParameterType.Integer => param.Value?.ToString() ?? "0",
                _ => param.Value?.ToString() ?? ""
            };
        }

        private static string FormatDouble(object? value)
        {
            if (value is double d)
                return d.ToString("F2");
            return value?.ToString() ?? "0";
        }

        private static string FormatPercentage(object? value)
        {
            if (value is double d)
            {
                // Convert common percentages to Percent.X format
                return d switch
                {
                    0.05 => "Percent.Five",
                    0.10 => "Percent.Ten",
                    0.15 => "Percent.Fifteen",
                    0.20 => "Percent.Twenty",
                    0.25 => "Percent.TwentyFive",
                    _ => $"Percent.Custom({d * 100})"
                };
            }
            return value?.ToString() ?? "0";
        }

        /// <summary>
        /// Creates a deep copy of this segment.
        /// </summary>
        public StrategySegment Clone()
        {
            return new StrategySegment
            {
                Id = Guid.NewGuid(),
                Type = Type,
                Category = Category,
                DisplayName = DisplayName,
                Description = Description,
                Parameters = Parameters.Select(p => new SegmentParameter
                {
                    Name = p.Name,
                    Label = p.Label,
                    Type = p.Type,
                    DefaultValue = p.DefaultValue,
                    Value = p.DefaultValue,
                    IsRequired = p.IsRequired,
                    MinValue = p.MinValue,
                    MaxValue = p.MaxValue,
                    Step = p.Step,
                    Options = p.Options?.ToList(),
                    EnumTypeName = p.EnumTypeName,
                    HelpText = p.HelpText,
                    Placeholder = p.Placeholder
                }).ToList(),
                Order = Order,
                Icon = Icon,
                Color = Color
            };
        }
    }
}
