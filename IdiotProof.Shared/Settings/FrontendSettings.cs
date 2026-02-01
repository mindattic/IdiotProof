// ============================================================================
// FrontendSettings - Settings specific to the Frontend project
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Settings;

/// <summary>
/// Settings specific to the Frontend (MAUI) project.
/// </summary>
public class FrontendSettings : AppSettings
{
    /// <summary>
    /// UI theme settings.
    /// </summary>
    [JsonPropertyName("theme")]
    public ThemeSettings Theme { get; set; } = new();

    /// <summary>
    /// Strategy editor settings.
    /// </summary>
    [JsonPropertyName("editor")]
    public EditorSettings Editor { get; set; } = new();

    /// <summary>
    /// Backend connection settings.
    /// </summary>
    [JsonPropertyName("backendConnection")]
    public BackendConnectionSettings BackendConnection { get; set; } = new();
}

/// <summary>
/// UI theme settings.
/// </summary>
public class ThemeSettings
{
    /// <summary>
    /// Theme mode: "Light", "Dark", or "System".
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "System";

    /// <summary>
    /// Accent color for UI elements.
    /// </summary>
    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#0078D4";

    /// <summary>
    /// Font size multiplier (1.0 = 100%).
    /// </summary>
    [JsonPropertyName("fontScale")]
    public double FontScale { get; set; } = 1.0;
}

/// <summary>
/// Strategy editor settings.
/// </summary>
public class EditorSettings
{
    /// <summary>
    /// Show line numbers in script editor.
    /// </summary>
    [JsonPropertyName("showLineNumbers")]
    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>
    /// Enable syntax highlighting.
    /// </summary>
    [JsonPropertyName("syntaxHighlighting")]
    public bool SyntaxHighlighting { get; set; } = true;

    /// <summary>
    /// Enable auto-complete suggestions.
    /// </summary>
    [JsonPropertyName("autoComplete")]
    public bool AutoComplete { get; set; } = true;

    /// <summary>
    /// Font family for the editor.
    /// </summary>
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Cascadia Code";

    /// <summary>
    /// Font size in points.
    /// </summary>
    [JsonPropertyName("fontSize")]
    public int FontSize { get; set; } = 14;
}

/// <summary>
/// Backend connection settings for the frontend.
/// </summary>
public class BackendConnectionSettings
{
    /// <summary>
    /// Auto-connect to backend on startup.
    /// </summary>
    [JsonPropertyName("autoConnect")]
    public bool AutoConnect { get; set; } = true;

    /// <summary>
    /// Reconnect interval in seconds if connection is lost.
    /// </summary>
    [JsonPropertyName("reconnectIntervalSeconds")]
    public int ReconnectIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum reconnection attempts.
    /// </summary>
    [JsonPropertyName("maxReconnectAttempts")]
    public int MaxReconnectAttempts { get; set; } = 10;
}
