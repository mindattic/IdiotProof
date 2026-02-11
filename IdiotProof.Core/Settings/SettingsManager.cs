// ============================================================================
// SettingsManager - Manages per-project settings files
// ============================================================================
//
// Handles reading, writing, and auto-creation of settings.json files.
// Each project gets its own settings folder under:
//   {SolutionRoot}\Settings\{ProjectName}\settings.json
//
// Strategies are stored under:
//   {SolutionRoot}\IdiotProof.Core\Scripts\
//
// Logs are stored under:
//   {SolutionRoot}\IdiotProof.Core\Logs\
//
// The solution root is detected by walking up from the executing assembly
// location until a .sln file is found, or falls back to the current directory.
//
// ============================================================================

using IdiotProof.Constants;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Settings;

/// <summary>
/// Manages application settings with per-project isolation.
/// </summary>
public static class SettingsManager
{
    private const string SettingsFileName = "settings.json";

    // Cached base folder path
    private static string? _cachedBaseFolder;

    // ========================================================================
    // PATH HELPERS
    // ========================================================================

    /// <summary>
    /// Gets the base folder (solution root directory).
    /// Detects by walking up from the executing assembly until a .sln file is found.
    /// Falls back to current directory if no solution file is found.
    /// </summary>
    public static string GetBaseFolder()
    {
        if (_cachedBaseFolder != null)
            return _cachedBaseFolder;

        _cachedBaseFolder = FindSolutionRoot() ?? Directory.GetCurrentDirectory();
        return _cachedBaseFolder;
    }

    /// <summary>
    /// Sets the base folder explicitly (useful for testing or custom deployments).
    /// </summary>
    public static void SetBaseFolder(string path)
    {
        _cachedBaseFolder = path;
    }

    /// <summary>
    /// Resets the base folder cache, forcing re-detection on next access.
    /// </summary>
    public static void ResetBaseFolder()
    {
        _cachedBaseFolder = null;
    }

    /// <summary>
    /// Finds the solution root by walking up the directory tree looking for a .sln file.
    /// </summary>
    private static string? FindSolutionRoot()
    {
        // Start from the executing assembly's location
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var startDir = Path.GetDirectoryName(assemblyLocation);

        if (string.IsNullOrEmpty(startDir))
            startDir = Directory.GetCurrentDirectory();

        var currentDir = new DirectoryInfo(startDir);

        // Walk up the directory tree looking for a .sln file
        while (currentDir != null)
        {
            // Check for any .sln file in this directory
            if (currentDir.GetFiles("*.sln").Length > 0)
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        // No solution file found, return null to trigger fallback
        return null;
    }

    /// <summary>
    /// Gets the shared strategies folder path.
    /// Strategies are stored in: {SolutionRoot}\IdiotProof.Core\Scripts\
    /// </summary>
    public static string GetStrategiesFolder()
    {
        return Path.Combine(GetBaseFolder(), _.Folder.Core, _.Folder.Scripts);
    }

    /// <summary>
    /// Gets the shared profiles folder path for learning system data.
    /// Profiles are stored in: {SolutionRoot}\IdiotProof.Core\Profiles\
    /// Learning data for each ticker to improve autonomous trading decisions.
    /// </summary>
    public static string GetProfilesFolder()
    {
        return Path.Combine(GetBaseFolder(), _.Folder.Core, _.Folder.Profiles);
    }

    /// <summary>
    /// Gets the shared data folder path for cached historical data, profiles, etc.
    /// Data is stored in: {SolutionRoot}\IdiotProof.Core\Data\
    /// </summary>
    public static string GetDataFolder()
    {
        return Path.Combine(GetBaseFolder(), _.Folder.Core, _.Folder.Data);
    }

    /// <summary>
    /// Gets the per-ticker data folder path.
    /// Ticker data is stored in: {SolutionRoot}\IdiotProof.Core\Data\{SYMBOL}\
    /// Creates the directory if it doesn't exist.
    /// </summary>
    public static string GetTickerDataFolder(string symbol)
    {
        var folder = Path.Combine(GetDataFolder(), symbol.ToUpperInvariant());
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Gets the shared metadata folder path for ticker analysis data.
    /// Metadata is stored in: {SolutionRoot}\IdiotProof.Core\Metadata\
    /// </summary>
    public static string GetMetadataFolder()
    {
        return Path.Combine(GetBaseFolder(), _.Folder.Core, "Metadata");
    }

    /// <summary>
    /// Gets the shared logs folder path.
    /// </summary>
    public static string GetLogsFolder()
    {
        return Path.Combine(GetBaseFolder(), _.Folder.Core, _.Folder.Logs);
    }

    /// <summary>
    /// Gets the logs folder for a specific project.
    /// </summary>
    public static string GetProjectLogsFolder(string projectName)
    {
        return Path.Combine(GetLogsFolder(), projectName);
    }

    /// <summary>
    /// Gets the settings folder for a specific project.
    /// </summary>
    public static string GetProjectSettingsFolder(string projectName)
    {
        return Path.Combine(GetBaseFolder(), _.Folder.Settings, projectName);
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
        // Create strategies folder (IdiotProof.Core\Scripts)
        var strategiesFolder = GetStrategiesFolder();
        if (!Directory.Exists(strategiesFolder))
            Directory.CreateDirectory(strategiesFolder);

        // Create profiles folder (IdiotProof.Core\Profiles)
        var profilesFolder = GetProfilesFolder();
        if (!Directory.Exists(profilesFolder))
            Directory.CreateDirectory(profilesFolder);

        // Create metadata folder (IdiotProof.Core\Metadata)
        var metadataFolder = GetMetadataFolder();
        if (!Directory.Exists(metadataFolder))
            Directory.CreateDirectory(metadataFolder);

        // Create logs folder (IdiotProof.Core\Logs)
        var logsFolder = GetLogsFolder();
        if (!Directory.Exists(logsFolder))
            Directory.CreateDirectory(logsFolder);

        // Create settings base folder
        var settingsBaseFolder = Path.Combine(GetBaseFolder(), _.Folder.Settings);
        if (!Directory.Exists(settingsBaseFolder))
            Directory.CreateDirectory(settingsBaseFolder);

        // Create project-specific folders if project name provided
        if (!string.IsNullOrEmpty(projectName))
        {
            var projectSettingsFolder = GetProjectSettingsFolder(projectName);
            if (!Directory.Exists(projectSettingsFolder))
                Directory.CreateDirectory(projectSettingsFolder);

            var projectLogsFolder = GetProjectLogsFolder(projectName);
            if (!Directory.Exists(projectLogsFolder))
                Directory.CreateDirectory(projectLogsFolder);
        }
    }
}
