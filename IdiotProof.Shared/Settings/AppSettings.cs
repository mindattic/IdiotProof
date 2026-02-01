// ============================================================================
// AppSettings - Base application settings model
// ============================================================================
//
// This class contains settings that are common across all IdiotProof projects.
// Each project can extend this with project-specific settings.
//
// FOLDER STRUCTURE (in MyDocuments\IdiotProof):
// Settings/
//   Backend/settings.json
//   Backend.UnitTests/settings.json
//   Console/settings.json
//   Console.UnitTests/settings.json
//   Frontend/settings.json
//   Shared/settings.json
//
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Settings;

/// <summary>
/// Base application settings common to all IdiotProof projects.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Version of the settings schema for migration purposes.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// When the settings were last modified.
    /// </summary>
    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The project name these settings belong to.
    /// </summary>
    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// User preferences.
    /// </summary>
    [JsonPropertyName("preferences")]
    public UserPreferences Preferences { get; set; } = new();

    /// <summary>
    /// Logging configuration.
    /// </summary>
    [JsonPropertyName("logging")]
    public LoggingSettings Logging { get; set; } = new();
}

/// <summary>
/// User preferences for the application.
/// </summary>
public class UserPreferences
{
    /// <summary>
    /// Whether to show confirmation dialogs before destructive actions.
    /// </summary>
    [JsonPropertyName("confirmDestructiveActions")]
    public bool ConfirmDestructiveActions { get; set; } = true;

    /// <summary>
    /// Default strategy folder name pattern.
    /// </summary>
    [JsonPropertyName("defaultFolderPattern")]
    public string DefaultFolderPattern { get; set; } = "yyyy-MM-dd";

    /// <summary>
    /// Auto-save strategies on changes.
    /// </summary>
    [JsonPropertyName("autoSave")]
    public bool AutoSave { get; set; } = true;

    /// <summary>
    /// Auto-save interval in seconds.
    /// </summary>
    [JsonPropertyName("autoSaveIntervalSeconds")]
    public int AutoSaveIntervalSeconds { get; set; } = 30;
}

/// <summary>
/// Logging configuration settings.
/// </summary>
public class LoggingSettings
{
    /// <summary>
    /// Whether logging is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum log level (Trace, Debug, Information, Warning, Error, Critical).
    /// </summary>
    [JsonPropertyName("minimumLevel")]
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>
    /// Whether to log to file.
    /// </summary>
    [JsonPropertyName("logToFile")]
    public bool LogToFile { get; set; } = true;

    /// <summary>
    /// Maximum log file size in MB before rotation.
    /// </summary>
    [JsonPropertyName("maxFileSizeMb")]
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>
    /// Number of log files to retain.
    /// </summary>
    [JsonPropertyName("retainedFileCount")]
    public int RetainedFileCount { get; set; } = 5;
}
