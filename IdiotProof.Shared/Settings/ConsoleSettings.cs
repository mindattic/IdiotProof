// ============================================================================
// ConsoleSettings - Settings specific to the Console project
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Settings;

/// <summary>
/// Settings specific to the Console project.
/// </summary>
public class ConsoleSettings : AppSettings
{
    /// <summary>
    /// Console display settings.
    /// </summary>
    [JsonPropertyName("display")]
    public ConsoleDisplaySettings Display { get; set; } = new();

    /// <summary>
    /// Script parser settings.
    /// </summary>
    [JsonPropertyName("parser")]
    public ParserSettings Parser { get; set; } = new();
}

/// <summary>
/// Console display settings.
/// </summary>
public class ConsoleDisplaySettings
{
    /// <summary>
    /// Whether to use colored output.
    /// </summary>
    [JsonPropertyName("useColors")]
    public bool UseColors { get; set; } = true;

    /// <summary>
    /// Whether to show verbose output.
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// Clear console on startup.
    /// </summary>
    [JsonPropertyName("clearOnStartup")]
    public bool ClearOnStartup { get; set; } = true;

    /// <summary>
    /// Show timestamps in output.
    /// </summary>
    [JsonPropertyName("showTimestamps")]
    public bool ShowTimestamps { get; set; } = true;
}

/// <summary>
/// Script parser settings.
/// </summary>
public class ParserSettings
{
    /// <summary>
    /// Whether to allow legacy syntax.
    /// </summary>
    [JsonPropertyName("allowLegacySyntax")]
    public bool AllowLegacySyntax { get; set; } = true;

    /// <summary>
    /// Whether symbol names are case-sensitive.
    /// </summary>
    [JsonPropertyName("caseSensitiveSymbols")]
    public bool CaseSensitiveSymbols { get; set; } = false;

    /// <summary>
    /// Default quantity if not specified.
    /// </summary>
    [JsonPropertyName("defaultQuantity")]
    public int DefaultQuantity { get; set; } = 1;
}
