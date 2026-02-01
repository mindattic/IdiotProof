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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

    // ========================================================================
    // SETTINGS LOAD/SAVE
    // ========================================================================

    /// <summary>
    /// Loads settings for a project. Creates default settings if file doesn't exist.
    /// </summary>
    /// <typeparam name="T">Settings type (must derive from AppSettings).</typeparam>
    /// <param name="projectName">Name of the project.</param>
    /// <returns>The loaded or default settings.</returns>
    public static T LoadSettings<T>(string projectName) where T : AppSettings, new()
    {
        EnsureFoldersExist(projectName);
        var filePath = GetSettingsFilePath(projectName);

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (settings != null)
                    return settings;
            }
            catch (Exception)
            {
                // If we can't read/parse, create new defaults
            }
        }

        // Create default settings
        var defaultSettings = CreateDefaultSettings<T>(projectName);
        SaveSettings(projectName, defaultSettings);
        return defaultSettings;
    }

    /// <summary>
    /// Loads settings asynchronously for a project. Creates default settings if file doesn't exist.
    /// </summary>
    public static async Task<T> LoadSettingsAsync<T>(string projectName) where T : AppSettings, new()
    {
        EnsureFoldersExist(projectName);
        var filePath = GetSettingsFilePath(projectName);

        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var settings = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (settings != null)
                    return settings;
            }
            catch (Exception)
            {
                // If we can't read/parse, create new defaults
            }
        }

        // Create default settings
        var defaultSettings = CreateDefaultSettings<T>(projectName);
        await SaveSettingsAsync(projectName, defaultSettings);
        return defaultSettings;
    }

    /// <summary>
    /// Saves settings for a project.
    /// </summary>
    public static void SaveSettings<T>(string projectName, T settings) where T : AppSettings
    {
        EnsureFoldersExist(projectName);
        var filePath = GetSettingsFilePath(projectName);

        settings.LastModified = DateTime.UtcNow;
        settings.ProjectName = projectName;

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Saves settings asynchronously for a project.
    /// </summary>
    public static async Task SaveSettingsAsync<T>(string projectName, T settings) where T : AppSettings
    {
        EnsureFoldersExist(projectName);
        var filePath = GetSettingsFilePath(projectName);

        settings.LastModified = DateTime.UtcNow;
        settings.ProjectName = projectName;

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Creates default settings for a project.
    /// </summary>
    private static T CreateDefaultSettings<T>(string projectName) where T : AppSettings, new()
    {
        return new T
        {
            Version = 1,
            ProjectName = projectName,
            LastModified = DateTime.UtcNow
        };
    }

    // ========================================================================
    // SETTINGS MANAGEMENT
    // ========================================================================

    /// <summary>
    /// Checks if settings file exists for a project.
    /// </summary>
    public static bool SettingsExist(string projectName)
    {
        var filePath = GetSettingsFilePath(projectName);
        return File.Exists(filePath);
    }

    /// <summary>
    /// Deletes settings for a project (use with caution).
    /// </summary>
    public static void DeleteSettings(string projectName)
    {
        var filePath = GetSettingsFilePath(projectName);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    /// <summary>
    /// Resets settings to defaults for a project.
    /// </summary>
    public static T ResetSettings<T>(string projectName) where T : AppSettings, new()
    {
        var defaultSettings = CreateDefaultSettings<T>(projectName);
        SaveSettings(projectName, defaultSettings);
        return defaultSettings;
    }

    /// <summary>
    /// Gets all project names that have settings files.
    /// </summary>
    public static IEnumerable<string> GetAllProjectsWithSettings()
    {
        var settingsBaseFolder = Path.Combine(GetBaseFolder(), SettingsFolder);
        
        if (!Directory.Exists(settingsBaseFolder))
            yield break;

        foreach (var dir in Directory.GetDirectories(settingsBaseFolder))
        {
            var projectName = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, SettingsFileName)))
                yield return projectName;
        }
    }
}
