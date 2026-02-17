// ============================================================================
// Settings Metadata - Defines UI rendering and validation for app settings
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Settings;

/// <summary>
/// Defines how a setting should be rendered and validated in the UI.
/// </summary>
public class SettingDefinition
{
    /// <summary>Unique key for the setting (e.g., "Port", "UseStopLoss")</summary>
    public required string Key { get; init; }
    
    /// <summary>Display name shown in UI</summary>
    public required string DisplayName { get; init; }
    
    /// <summary>Category for grouping in UI</summary>
    public required string Category { get; init; }
    
    /// <summary>Description/tooltip text</summary>
    public string? Description { get; init; }
    
    /// <summary>The control type to render</summary>
    public SettingControlType ControlType { get; init; } = SettingControlType.Text;
    
    /// <summary>Data type of the value</summary>
    public SettingDataType DataType { get; init; } = SettingDataType.String;
    
    /// <summary>Current value (serialized as object for JSON)</summary>
    public object? Value { get; set; }
    
    /// <summary>Default value</summary>
    public object? DefaultValue { get; init; }
    
    /// <summary>Minimum value (for numeric types)</summary>
    public double? Min { get; init; }
    
    /// <summary>Maximum value (for numeric types)</summary>
    public double? Max { get; init; }
    
    /// <summary>Step increment (for numeric types)</summary>
    public double? Step { get; init; }
    
    /// <summary>Available options (for select/dropdown)</summary>
    public List<SelectOption>? Options { get; init; }
    
    /// <summary>Whether this setting is read-only (const)</summary>
    public bool IsReadOnly { get; init; }
    
    /// <summary>Whether this setting requires app restart</summary>
    public bool RequiresRestart { get; init; }
    
    /// <summary>Unit label (e.g., "seconds", "%", "$")</summary>
    public string? Unit { get; init; }
    
    /// <summary>Validation regex pattern</summary>
    public string? ValidationPattern { get; init; }
    
    /// <summary>Order within category for display</summary>
    public int Order { get; init; }
}

public class SelectOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SettingControlType
{
    Text,           // Single-line text input
    TextArea,       // Multi-line text input
    Number,         // Numeric input with optional min/max/step
    Toggle,         // Boolean on/off switch
    Checkbox,       // Boolean checkbox
    Select,         // Dropdown select
    Radio,          // Radio button group
    Slider,         // Range slider
    Color,          // Color picker
    Date,           // Date picker
    Time,           // Time picker
    DateTime,       // Date and time picker
    TimeSpan,       // Duration picker
    Password,       // Password input (masked)
    Url,            // URL input with validation
    Email,          // Email input with validation
    IpAddress,      // IP address input
    Port,           // Port number (1-65535)
    Percent,        // Percentage (0-100)
    Currency,       // Currency amount
    ReadOnly        // Display only, not editable
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SettingDataType
{
    String,
    Int,
    Double,
    Bool,
    TimeSpan,
    DateTime,
    Enum
}

/// <summary>
/// Container for all settings organized by category
/// </summary>
public class SettingsBundle
{
    public required string AppVersion { get; init; }
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public required List<SettingDefinition> Settings { get; init; }
    
    public IEnumerable<string> Categories => Settings.Select(s => s.Category).Distinct().OrderBy(c => c);
    
    public IEnumerable<SettingDefinition> GetByCategory(string category) =>
        Settings.Where(s => s.Category == category).OrderBy(s => s.Order);
}
