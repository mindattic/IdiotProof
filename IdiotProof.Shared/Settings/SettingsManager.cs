// ============================================================================
// SettingsManager - Manages per-project settings files
// ============================================================================
//
// Handles reading, writing, and auto-creation of settings.json files.
// Each project gets its own settings folder under:
//   MyDocuments\IdiotProof\Settings\{ProjectName}\settings.json
//
// On first run, if no settings.json exists, one is created with defaults.
//
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Settings;

/// <summary>
/// Manages application settings with per-project isolation.
/// </summary>
public static class SettingsManager
{
    private const string SettingsFileName = "settings.json";
    private const string IdiotProofFolder = "IdiotProof";
    private const string SettingsFolder = "Settings";
    private const string StrategiesFolder = "Strategies";


    // ========================================================================
    // PATH HELPERS
    // ========================================================================

    /// <summary>
    /// Gets the base IdiotProof folder in MyDocuments.
    /// </summary>
    public static string GetBaseFolder()
    {
        var myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(myDocuments, IdiotProofFolder);
    }

    /// <summary>
    /// Gets the shared strategies folder path.
    /// </summary>
    public static string GetStrategiesFolder()
    {
        return Path.Combine(GetBaseFolder(), StrategiesFolder);
    }

    /// <summary>
    /// Gets the settings folder for a specific project.
    /// </summary>
    public static string GetProjectSettingsFolder(string projectName)
    {
        return Path.Combine(GetBaseFolder(), SettingsFolder, projectName);
    }

    /// <summary>
    /// Gets the full path to a project's settings.json file.
    /// </summary>
    public static string GetSettingsFilePath(string projectName)
    {
        return Path.Combine(GetProjectSettingsFolder(projectName), SettingsFileName);
    }

    // ========================================================================
    // FOLDER INITIALIZATION
    // ========================================================================

    /// <summary>
    /// Ensures all required folders exist.
    /// Creates the folder structure if it doesn't exist.
    /// </summary>
    public static void EnsureFoldersExist(string? projectName = null)
    {
        // Create base folder
        var baseFolder = GetBaseFolder();
        if (!Directory.Exists(baseFolder))
            Directory.CreateDirectory(baseFolder);

        // Create strategies folder
        var strategiesFolder = GetStrategiesFolder();
        if (!Directory.Exists(strategiesFolder))
            Directory.CreateDirectory(strategiesFolder);

        // Create settings base folder
        var settingsBaseFolder = Path.Combine(baseFolder, SettingsFolder);
        if (!Directory.Exists(settingsBaseFolder))
            Directory.CreateDirectory(settingsBaseFolder);

        // Create project-specific settings folder if project name provided
        if (!string.IsNullOrEmpty(projectName))
        {
            var projectSettingsFolder = GetProjectSettingsFolder(projectName);
            if (!Directory.Exists(projectSettingsFolder))
                Directory.CreateDirectory(projectSettingsFolder);
        }
    }


}

