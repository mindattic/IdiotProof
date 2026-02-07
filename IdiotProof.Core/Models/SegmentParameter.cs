// ============================================================================
// SegmentParameter - Defines a parameter for a strategy segment
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Core.Models
{
    /// <summary>
    /// Defines a single parameter for a strategy segment.
    /// </summary>
    public class SegmentParameter
    {
        /// <summary>
        /// Name of the parameter (e.g., "symbol", "level", "quantity").
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Display label for the parameter in the UI.
        /// </summary>
        public required string Label { get; set; }

        /// <summary>
        /// Type of the parameter for UI rendering.
        /// </summary>
        public required ParameterType Type { get; set; }

        /// <summary>
        /// Default value for the parameter.
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// Current value of the parameter.
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// Whether this parameter is required.
        /// </summary>
        public bool IsRequired { get; set; } = true;

        /// <summary>
        /// Minimum value (for numeric types).
        /// </summary>
        public double? MinValue { get; set; }

        /// <summary>
        /// Maximum value (for numeric types).
        /// </summary>
        public double? MaxValue { get; set; }

        /// <summary>
        /// Step increment (for numeric types).
        /// </summary>
        public double? Step { get; set; }

        /// <summary>
        /// Available options (for enum/dropdown types).
        /// </summary>
        public List<string>? Options { get; set; }

        /// <summary>
        /// The enum type name (for enum parameters).
        /// </summary>
        public string? EnumTypeName { get; set; }

        /// <summary>
        /// Tooltip/help text for the parameter.
        /// </summary>
        public string? HelpText { get; set; }

        /// <summary>
        /// Placeholder text for text inputs.
        /// </summary>
        public string? Placeholder { get; set; }
    }

    /// <summary>
    /// Types of parameters that determine how they are rendered in the UI.
    /// </summary>
    public enum ParameterType
    {
        /// <summary>Text input</summary>
        String,

        /// <summary>Integer number input</summary>
        Integer,

        /// <summary>Decimal number input</summary>
        Double,

        /// <summary>Checkbox</summary>
        Boolean,

        /// <summary>Dropdown from enum values</summary>
        Enum,

        /// <summary>Time picker</summary>
        Time,

        /// <summary>Percentage slider/input (0.01 to 1.0)</summary>
        Percentage,

        /// <summary>Price input with dollar formatting</summary>
        Price
    }
}


