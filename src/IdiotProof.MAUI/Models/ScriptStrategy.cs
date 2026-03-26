using System.Text.Json.Serialization;

namespace IdiotProof.MAUI.Models;

/// <summary>
/// A complete strategy built from an ordered chain of segments.
/// Persisted as JSON, rendered as a script grid, generates fluent API code.
/// </summary>
public sealed class ScriptStrategy
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled Strategy";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ordered chain of segments forming the strategy.
    /// </summary>
    public List<ScriptSegment> Segments { get; set; } = [];
}

/// <summary>
/// A single step in the strategy chain — one fluent API method call.
/// </summary>
public sealed class ScriptSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Key into SegmentCatalog (e.g. "Ticker", "Breakout", "Long").</summary>
    public string SegmentKey { get; set; } = "";

    /// <summary>Display name shown in the grid (e.g. ".Breakout()").</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Category for color coding.</summary>
    public string Category { get; set; } = "";

    /// <summary>Accent color hex.</summary>
    public string Color { get; set; } = "#6366f1";

    /// <summary>Short icon text.</summary>
    public string Icon { get; set; } = "";

    /// <summary>Typed parameters with current values.</summary>
    public List<ScriptParam> Parameters { get; set; } = [];

    /// <summary>Whether all required params are filled.</summary>
    [JsonIgnore]
    public bool IsValid => Parameters
        .Where(p => p.IsRequired)
        .All(p => p.Value is not null && p.Value.ToString() != "");
}

/// <summary>
/// A typed parameter on a script segment.
/// </summary>
public sealed class ScriptParam
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public ParamKind Kind { get; set; }
    public object? Value { get; set; }
    public object? DefaultValue { get; set; }
    public bool IsRequired { get; set; } = true;
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public List<string>? Options { get; set; }
    public string? EnumType { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
}

public enum ParamKind
{
    String,
    Integer,
    Double,
    Boolean,
    Enum,
    Time,
    Price,
    Percentage
}
