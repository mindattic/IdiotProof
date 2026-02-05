// ============================================================================
// IdiotScriptFileManager - File I/O for .IDIOT strategy files
// ============================================================================
//
// Handles reading and writing strategy files in IdiotScript format.
// Files use the .IDIOT extension and are stored as plain text.
//
// FOLDER STRUCTURE (in solution root directory):
// Strategies/
//   nvda.idiot
//   nvda-b.idiot
//   my-best-strategy-yet.idiot
//   2025-01-15/
//     AAPL_Breakout.idiot
//     PLTR_VWAP_Scalp.idiot
// Settings/
//   Backend/settings.json
//   Console/settings.json
//   Frontend/settings.json
//
// FILE FORMAT:
// Each .idiot file contains a single strategy in IdiotScript format.
// The file is a plain text file that can be edited in any text editor.
//
// ============================================================================

using IdiotProof.Shared.Models;
using IdiotProof.Shared.Settings;

namespace IdiotProof.Shared.Scripting;

/// <summary>
/// Manages reading and writing .IDIOT strategy files.
/// </summary>
public static class IdiotScriptFileManager
{
    /// <summary>File extension for IdiotScript files (without dot).</summary>
    public const string FileExtension = "idiot";

    /// <summary>File extension for IdiotScript files (with dot).</summary>
    public const string FileExtensionWithDot = ".idiot";

    /// <summary>File search pattern for .idiot files.</summary>
    public const string SearchPattern = "*.idiot";

    /// <summary>Supported file extensions for loading strategies.</summary>
    public static readonly string[] SupportedExtensions = [".idiot", ".txt"];

    // ========================================================================
    // FOLDER MANAGEMENT
    // ========================================================================

    /// <summary>
    /// Gets the base IdiotProof folder in MyDocuments.
    /// </summary>
    public static string GetBaseFolder() => SettingsManager.GetBaseFolder();

    /// <summary>
    /// Gets the default strategies folder path.
    /// </summary>
    public static string GetDefaultFolder() => SettingsManager.GetStrategiesFolder();

    /// <summary>
    /// Gets the settings folder path for a specific project.
    /// </summary>
    public static string GetSettingsFolder(string projectName) => SettingsManager.GetProjectSettingsFolder(projectName);

    /// <summary>
    /// Gets the date-based folder path for a specific date.
    /// </summary>
    public static string GetDateFolder(DateOnly date, string? baseFolder = null)
    {
        baseFolder ??= GetDefaultFolder();
        return Path.Combine(baseFolder, date.ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Ensures a folder exists, creating it if necessary.
    /// </summary>
    public static void EnsureFolderExists(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
    }

    /// <summary>
    /// Ensures all required folders exist.
    /// </summary>
    public static void EnsureAllFoldersExist(string? projectName = null)
    {
        SettingsManager.EnsureFoldersExist(projectName);
    }

    // ========================================================================
    // FILE WRITING
    // ========================================================================

    /// <summary>
    /// Saves a strategy to an .idiot file.
    /// </summary>
    /// <param name="strategy">The strategy to save.</param>
    /// <param name="date">The date folder to save to.</param>
    /// <param name="baseFolder">Optional base folder (defaults to MyDocuments\IdiotProof\strategies).</param>
    /// <returns>The full path to the saved file.</returns>
    public static async Task<string> SaveStrategyAsync(
        StrategyDefinition strategy,
        DateOnly date,
        string? baseFolder = null)
    {
        var folder = GetDateFolder(date, baseFolder);
        EnsureFolderExists(folder);

        var script = IdiotScriptSerializer.SerializeFormatted(strategy);
        var fileName = GetSafeFileName(strategy.Name, strategy.Symbol);
        var filePath = Path.Combine(folder, fileName);

        await File.WriteAllTextAsync(filePath, script);
        return filePath;
    }

    /// <summary>
    /// Saves a strategy to a specific file path.
    /// </summary>
    public static async Task SaveToFileAsync(StrategyDefinition strategy, string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(folder))
            EnsureFolderExists(folder);

        var script = IdiotScriptSerializer.SerializeFormatted(strategy);
        await File.WriteAllTextAsync(filePath, script);
    }

    /// <summary>
    /// Saves multiple strategies to a date folder.
    /// </summary>
    public static async Task SaveStrategiesAsync(
        IEnumerable<StrategyDefinition> strategies,
        DateOnly date,
        string? baseFolder = null)
    {
        var folder = GetDateFolder(date, baseFolder);
        EnsureFolderExists(folder);

        // Clear existing files
        foreach (var existingFile in Directory.GetFiles(folder, SearchPattern))
        {
            File.Delete(existingFile);
        }

        // Save new files
        foreach (var strategy in strategies)
        {
            await SaveStrategyAsync(strategy, date, baseFolder);
        }
    }

    // ========================================================================
    // FILE READING
    // ========================================================================

    /// <summary>
    /// Loads a strategy from an .idiot file.
    /// </summary>
    /// <param name="filePath">Path to the .idiot file.</param>
    /// <returns>The parsed strategy, or null if parsing fails.</returns>
    public static async Task<StrategyDefinition?> LoadStrategyAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var script = await File.ReadAllTextAsync(filePath);
        
        if (IdiotScriptParser.TryParse(script, out var strategy, out _))
            return strategy;

        return null;
    }

    /// <summary>
    /// Loads all strategies from a date folder.
    /// </summary>
    public static async Task<List<StrategyDefinition>> LoadStrategiesAsync(
        DateOnly date,
        string? baseFolder = null)
    {
        var folder = GetDateFolder(date, baseFolder);
        return await LoadStrategiesFromFolderAsync(folder);
    }

    /// <summary>
    /// Loads all strategies from a specific folder.
    /// </summary>
    public static async Task<List<StrategyDefinition>> LoadStrategiesFromFolderAsync(string folder)
    {
        var strategies = new List<StrategyDefinition>();

        if (!Directory.Exists(folder))
            return strategies;

        // Load files from all supported extensions
        var files = SupportedExtensions
            .SelectMany(ext => Directory.GetFiles(folder, $"*{ext}"))
            .Distinct()
            .ToList();

        foreach (var file in files)
        {
            var strategy = await LoadStrategyAsync(file);
            if (strategy != null)
                strategies.Add(strategy);
        }

        return strategies;
    }

    /// <summary>
    /// Loads a strategy collection from a date folder.
    /// </summary>
    public static async Task<StrategyCollection> LoadCollectionAsync(
        DateOnly date,
        string? baseFolder = null)
    {
        var strategies = await LoadStrategiesAsync(date, baseFolder);

        return new StrategyCollection
        {
            Date = date,
            Strategies = strategies,
            LastModified = DateTime.UtcNow,
            Version = 1
        };
    }

    // ========================================================================
    // FILE OPERATIONS
    // ========================================================================

    /// <summary>
    /// Deletes a strategy file.
    /// </summary>
    public static void DeleteStrategy(StrategyDefinition strategy, DateOnly date, string? baseFolder = null)
    {
        var folder = GetDateFolder(date, baseFolder);
        var fileName = GetSafeFileName(strategy.Name, strategy.Symbol);
        var filePath = Path.Combine(folder, fileName);

        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    /// <summary>
    /// Checks if a strategy file exists.
    /// </summary>
    public static bool StrategyExists(StrategyDefinition strategy, DateOnly date, string? baseFolder = null)
    {
        var folder = GetDateFolder(date, baseFolder);
        var fileName = GetSafeFileName(strategy.Name, strategy.Symbol);
        var filePath = Path.Combine(folder, fileName);

        return File.Exists(filePath);
    }

    /// <summary>
    /// Gets the file path for a strategy.
    /// </summary>
    public static string GetStrategyFilePath(StrategyDefinition strategy, DateOnly date, string? baseFolder = null)
    {
        var folder = GetDateFolder(date, baseFolder);
        var fileName = GetSafeFileName(strategy.Name, strategy.Symbol);
        return Path.Combine(folder, fileName);
    }

    // ========================================================================
    // IMPORT/EXPORT
    // ========================================================================

    /// <summary>
    /// Exports a strategy to IdiotScript text (for clipboard or display).
    /// </summary>
    public static string ExportToScript(StrategyDefinition strategy)
    {
        return IdiotScriptSerializer.Serialize(strategy);
    }

    /// <summary>
    /// Exports a strategy to formatted IdiotScript text (for display or file).
    /// </summary>
    public static string ExportToFormattedScript(StrategyDefinition strategy)
    {
        return IdiotScriptSerializer.SerializeFormatted(strategy);
    }

    /// <summary>
    /// Imports a strategy from IdiotScript text.
    /// </summary>
    public static StrategyDefinition? ImportFromScript(string script)
    {
        if (IdiotScriptParser.TryParse(script, out var strategy, out _))
            return strategy;
        return null;
    }

    /// <summary>
    /// Imports a strategy from IdiotScript text with error details.
    /// </summary>
    public static (StrategyDefinition? Strategy, string? Error) ImportFromScriptWithError(string script)
    {
        if (IdiotScriptParser.TryParse(script, out var strategy, out var error))
            return (strategy, null);
        return (null, error);
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// <summary>
    /// Gets a safe file name for a strategy.
    /// </summary>
    public static string GetSafeFileName(string? name, string symbol)
    {
        // Start with symbol
        var baseName = symbol;

        // Add name if it's meaningful (not just "SYMBOL Strategy")
        if (!string.IsNullOrEmpty(name) && 
            !name.EndsWith("Strategy", StringComparison.OrdinalIgnoreCase))
        {
            // Clean the name
            var cleanName = name
                .Replace(" ", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(":", "")
                .Replace("\"", "")
                .Replace("<", "")
                .Replace(">", "")
                .Replace("|", "")
                .Replace("?", "")
                .Replace("*", "");

            baseName = $"{symbol}_{cleanName}";
        }

        return $"{baseName}{FileExtensionWithDot}";
    }

    /// <summary>
    /// Gets all .idiot files in a folder with their last modified times.
    /// </summary>
    public static IEnumerable<(string FilePath, DateTime LastModified)> GetFilesWithModifiedTime(
        DateOnly date,
        string? baseFolder = null)
    {
        var folder = GetDateFolder(date, baseFolder);

        if (!Directory.Exists(folder))
            yield break;

        foreach (var file in Directory.GetFiles(folder, SearchPattern))
        {
            yield return (file, File.GetLastWriteTimeUtc(file));
        }
    }
}


