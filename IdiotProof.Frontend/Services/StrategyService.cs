// ============================================================================
// StrategyService - Implementation of strategy persistence
// ============================================================================
//
// Strategies are saved as INDIVIDUAL files in a date-based folder structure.
// Supports both IdiotScript (.idiot) and legacy JSON (.json) formats.
//
// PREFERRED FORMAT: IdiotScript (.idiot)
//   Strategies/
//     2025-01-15/
//       VIVS_Breakout.idiot
//       CATX_VWAP_Scalp.idiot
//     2025-01-16/
//       ...
//
// LEGACY FORMAT: JSON (.json) - still supported for backwards compatibility
//
// IDIOTSCRIPT FORMAT:
// Each .idiot file is a plain text file containing IdiotScript:
//   SYM(VIVS); QTY(100); SESSION(~.PREMARKET);
//   BREAKOUT(2.50) > PULLBACK(2.40) > VWAP+;
//   TP(2.80); TSL(~.MODERATE); CLOSE(~.BELL)
//
// File naming uses Windows-compliant names with duplicate handling (a-z suffix).
// ============================================================================

using IdiotProof.Shared.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdiotProof.Frontend.Services
{
    /// <summary>
    /// Service for loading, saving, and managing strategies as individual JSON files.
    /// Each strategy is saved as its own file in a date-based folder structure.
    /// </summary>
    public partial class StrategyService : IStrategyService
    {
        private readonly string _strategiesFolder;
        private readonly JsonSerializerOptions _jsonOptions;

        // Characters invalid in Windows filenames
        private static readonly char[] InvalidFileChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

        public string StrategiesFolder => _strategiesFolder;

        public StrategyService()
        {
            // Store strategies in AppData folder
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _strategiesFolder = Path.Combine(appData, "IdiotProof", "Strategies");

            Directory.CreateDirectory(_strategiesFolder);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters =
                {
                    new System.Text.Json.Serialization.JsonStringEnumConverter()
                }
            };
        }

        public async Task<StrategyCollection> GetCollectionAsync(DateOnly date)
        {
            var dateFolder = GetDateFolder(date);
            var collection = new StrategyCollection
            {
                Date = date,
                Strategies = [],
                Version = 1,
                LastModified = DateTime.UtcNow
            };

            if (!Directory.Exists(dateFolder))
                return collection;

            try
            {
                var files = Directory.GetFiles(dateFolder, "*.json");

                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var strategy = JsonSerializer.Deserialize<StrategyDefinition>(json, _jsonOptions);
                        if (strategy != null)
                        {
                            collection.Strategies.Add(strategy);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading strategy from {file}: {ex.Message}");
                    }
                }

                collection.Strategies = collection.Strategies.OrderBy(s => s.Name).ToList();
                return collection;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading strategies for {date}: {ex.Message}");
                return collection;
            }
        }

        public async Task SaveCollectionAsync(StrategyCollection collection)
        {
            var dateFolder = GetDateFolder(collection.Date);
            Directory.CreateDirectory(dateFolder);

            // Get existing files to track which ones to delete
            var existingFiles = Directory.Exists(dateFolder) 
                ? new HashSet<string>(Directory.GetFiles(dateFolder, "*.json"))
                : [];
            var savedFiles = new HashSet<string>();

            foreach (var strategy in collection.Strategies)
            {
                var filePath = await SaveStrategyAsync(strategy, collection.Date);
                savedFiles.Add(filePath);
            }

            // Delete files that are no longer in the collection
            foreach (var file in existingFiles.Except(savedFiles))
            {
                try
                {
                    File.Delete(file);
                }
                catch { }
            }
        }

        /// <summary>
        /// Saves an individual strategy to its own JSON file.
        /// </summary>
        public async Task<string> SaveStrategyAsync(StrategyDefinition strategy, DateOnly date)
        {
            var dateFolder = GetDateFolder(date);
            Directory.CreateDirectory(dateFolder);

            strategy.ModifiedAt = DateTime.UtcNow;

            var fileName = GetSafeFileName(strategy.Name, strategy.Id, dateFolder);
            var filePath = Path.Combine(dateFolder, fileName);

            var json = JsonSerializer.Serialize(strategy, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);

            return filePath;
        }

        /// <summary>
        /// Deletes a strategy file.
        /// </summary>
        public Task DeleteStrategyAsync(StrategyDefinition strategy, DateOnly date)
        {
            var dateFolder = GetDateFolder(date);

            if (!Directory.Exists(dateFolder))
                return Task.CompletedTask;

            // Find and delete the file with matching ID
            var files = Directory.GetFiles(dateFolder, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var existing = JsonSerializer.Deserialize<StrategyDefinition>(json, _jsonOptions);
                    if (existing?.Id == strategy.Id)
                    {
                        File.Delete(file);
                        break;
                    }
                }
                catch { }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Renames a strategy and its file.
        /// </summary>
        public async Task RenameStrategyAsync(StrategyDefinition strategy, string newName, DateOnly date)
        {
            // Delete old file
            await DeleteStrategyAsync(strategy, date);

            // Update name and save with new filename
            strategy.Name = newName;
            strategy.ModifiedAt = DateTime.UtcNow;
            await SaveStrategyAsync(strategy, date);
        }

        /// <summary>
        /// Clones a strategy with a new name.
        /// </summary>
        public async Task<StrategyDefinition> CloneStrategyAsync(StrategyDefinition strategy, DateOnly date)
        {
            var clone = new StrategyDefinition
            {
                Id = Guid.NewGuid(),
                Name = GetUniqueName(strategy.Name, date),
                Description = strategy.Description,
                Symbol = strategy.Symbol,
                Enabled = false, // Disabled by default
                Segments = strategy.Segments.Select(s => s.Clone()).ToList(),
                Author = strategy.Author,
                Tags = strategy.Tags.ToList(),
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            await SaveStrategyAsync(clone, date);
            return clone;
        }

        public Task<List<DateOnly>> GetAvailableDatesAsync()
        {
            var dates = new List<DateOnly>();

            if (!Directory.Exists(_strategiesFolder))
                return Task.FromResult(dates);

            var directories = Directory.GetDirectories(_strategiesFolder);

            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                if (DateOnly.TryParse(dirName, out var date))
                {
                    // Include dates that have strategy files (.idiot or .json)
                    if (Directory.GetFiles(dir, "*.idiot").Length > 0 ||
                        Directory.GetFiles(dir, "*.json").Length > 0)
                    {
                        dates.Add(date);
                    }
                }
            }

            return Task.FromResult(dates.OrderByDescending(d => d).ToList());
        }

        public string ExportToCode(StrategyDefinition strategy)
        {
            return strategy.ToFluentCode();
        }

        /// <summary>
        /// Exports a strategy to IdiotScript format.
        /// </summary>
        public string ExportToIdiotScript(StrategyDefinition strategy)
        {
            return Shared.Scripting.IdiotScriptSerializer.Serialize(strategy);
        }

        /// <summary>
        /// Exports a strategy to formatted IdiotScript.
        /// </summary>
        public string ExportToFormattedIdiotScript(StrategyDefinition strategy)
        {
            return Shared.Scripting.IdiotScriptSerializer.SerializeFormatted(strategy);
        }

        /// <summary>
        /// Imports a strategy from IdiotScript.
        /// </summary>
        public StrategyDefinition? ImportFromIdiotScript(string script)
        {
            return Shared.Scripting.IdiotScriptFileManager.ImportFromScript(script);
        }

        /// <summary>
        /// Imports a strategy from IdiotScript with error details.
        /// </summary>
        public (StrategyDefinition? Strategy, string? Error) ImportFromIdiotScriptWithError(string script)
        {
            return Shared.Scripting.IdiotScriptFileManager.ImportFromScriptWithError(script);
        }

        private string GetDateFolder(DateOnly date)
        {
            return Path.Combine(_strategiesFolder, date.ToString("yyyy-MM-dd"));
        }

        /// <summary>
        /// Creates a Windows-compliant filename from the strategy name.
        /// Handles duplicates by appending a-z suffix.
        /// </summary>
        private string GetSafeFileName(string strategyName, Guid strategyId, string folder)
        {
            // Remove/replace invalid characters
            var safeName = SanitizeFileName(strategyName);

            // Ensure we have something
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Strategy";

            // Limit length (leave room for suffix and extension)
            if (safeName.Length > 100)
                safeName = safeName[..100];

            var basePath = Path.Combine(folder, $"{safeName}.json");

            // Check if this file already belongs to this strategy
            if (File.Exists(basePath))
            {
                try
                {
                    var json = File.ReadAllText(basePath);
                    var existing = JsonSerializer.Deserialize<StrategyDefinition>(json, _jsonOptions);
                    if (existing?.Id == strategyId)
                        return $"{safeName}.json"; // Same strategy, keep same name
                }
                catch { }
            }
            else
            {
                return $"{safeName}.json"; // File doesn't exist, use base name
            }

            // File exists with different ID - append suffix a-z
            for (char suffix = 'a'; suffix <= 'z'; suffix++)
            {
                var newName = $"{safeName}_{suffix}.json";
                var newPath = Path.Combine(folder, newName);

                if (!File.Exists(newPath))
                    return newName;

                // Check if this suffixed file belongs to our strategy
                try
                {
                    var json = File.ReadAllText(newPath);
                    var existing = JsonSerializer.Deserialize<StrategyDefinition>(json, _jsonOptions);
                    if (existing?.Id == strategyId)
                        return newName;
                }
                catch { }
            }

            // Fallback: use GUID
            return $"{safeName}_{strategyId:N}.json";
        }

        /// <summary>
        /// Sanitizes a string for use as a Windows filename.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            // Replace invalid chars with underscores
            var result = name;
            foreach (var c in InvalidFileChars)
            {
                result = result.Replace(c, '_');
            }

            // Replace multiple spaces/underscores with single underscore
            result = MultipleUnderscoresRegex().Replace(result, "_");
            result = result.Trim('_', ' ');

            return result;
        }

        [GeneratedRegex(@"[_\s]+")]
        private static partial Regex MultipleUnderscoresRegex();

        /// <summary>
        /// Gets a unique name for a cloned strategy.
        /// </summary>
        private string GetUniqueName(string baseName, DateOnly date)
        {
            var dateFolder = GetDateFolder(date);
            var copyName = $"{baseName} (Copy)";

            if (!Directory.Exists(dateFolder))
                return copyName;

            // Check existing files for duplicate names
            var files = Directory.GetFiles(dateFolder, "*.json");
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var strategy = JsonSerializer.Deserialize<StrategyDefinition>(json, _jsonOptions);
                    if (strategy != null)
                        existingNames.Add(strategy.Name);
                }
                catch { }
            }

            if (!existingNames.Contains(copyName))
                return copyName;

            // Try numbered copies
            for (int i = 2; i <= 100; i++)
            {
                var numberedName = $"{baseName} (Copy {i})";
                if (!existingNames.Contains(numberedName))
                    return numberedName;
            }

            // Fallback
            return $"{baseName} (Copy {Guid.NewGuid():N}[..8])";
        }
    }
}
