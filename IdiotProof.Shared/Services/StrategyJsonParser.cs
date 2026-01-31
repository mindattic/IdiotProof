// ============================================================================
// StrategyJsonParser - Parses JSON strategy files into StrategyDefinition objects
// ============================================================================
//
// Strategies are stored as individual JSON files in date-based folders:
//   Strategies/
//     2025-01-15/
//       VIVS_Breakout.json
//       CATX_VWAP_Scalp.json
//     2025-01-16/
//       ...
// ============================================================================

using IdiotProof.Shared.Models;
using IdiotProof.Shared.Enums;
using System.Text.Json;

namespace IdiotProof.Shared.Services
{
    /// <summary>
    /// Parses JSON strategy files and loads StrategyDefinition objects.
    /// Used by both the frontend and backend to work with strategy data.
    /// </summary>
    public static class StrategyJsonParser
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        /// <summary>
        /// Gets the default folder for strategy files.
        /// </summary>
        public static string GetDefaultFolder()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "IdiotProof", "Strategies");
        }

        /// <summary>
        /// Gets the date folder for a specific date.
        /// </summary>
        /// <param name="date">The date.</param>
        /// <param name="baseFolder">The base folder for strategy files.</param>
        /// <returns>The full folder path.</returns>
        public static string GetDateFolder(DateOnly date, string? baseFolder = null)
        {
            baseFolder ??= GetDefaultFolder();
            return Path.Combine(baseFolder, date.ToString("yyyy-MM-dd"));
        }

        /// <summary>
        /// Loads a strategy from a JSON file.
        /// </summary>
        /// <param name="filePath">Path to the JSON file.</param>
        /// <returns>The parsed strategy definition.</returns>
        public static StrategyDefinition? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<StrategyDefinition>(json, JsonOptions);
        }

        /// <summary>
        /// Loads a strategy from a JSON string.
        /// </summary>
        /// <param name="json">The JSON content.</param>
        /// <returns>The parsed strategy definition.</returns>
        public static StrategyDefinition? LoadFromJson(string json)
        {
            return JsonSerializer.Deserialize<StrategyDefinition>(json, JsonOptions);
        }

        /// <summary>
        /// Loads all strategies for a specific date.
        /// </summary>
        /// <param name="date">The date to load strategies for.</param>
        /// <param name="baseFolder">Optional base folder override.</param>
        /// <returns>List of strategy definitions for the date.</returns>
        public static List<StrategyDefinition> LoadStrategiesForDate(DateOnly date, string? baseFolder = null)
        {
            var dateFolder = GetDateFolder(date, baseFolder);
            var strategies = new List<StrategyDefinition>();

            if (!Directory.Exists(dateFolder))
                return strategies;

            var files = Directory.GetFiles(dateFolder, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var strategy = LoadFromFile(file);
                    if (strategy != null)
                        strategies.Add(strategy);
                }
                catch
                {
                    // Skip invalid files
                }
            }

            return strategies.OrderBy(s => s.Name).ToList();
        }

        /// <summary>
        /// Loads strategies for today's date.
        /// </summary>
        /// <param name="baseFolder">Optional base folder override.</param>
        /// <returns>List of strategy definitions for today.</returns>
        public static List<StrategyDefinition> LoadTodaysStrategies(string? baseFolder = null)
        {
            return LoadStrategiesForDate(DateOnly.FromDateTime(DateTime.Today), baseFolder);
        }

        /// <summary>
        /// Loads all enabled strategies for today.
        /// </summary>
        /// <param name="baseFolder">Optional base folder override.</param>
        /// <returns>List of enabled strategy definitions.</returns>
        public static List<StrategyDefinition> LoadEnabledStrategies(string? baseFolder = null)
        {
            return LoadTodaysStrategies(baseFolder)
                .Where(s => s.Enabled)
                .ToList();
        }

        /// <summary>
        /// Saves a strategy to a JSON file.
        /// </summary>
        /// <param name="strategy">The strategy to save.</param>
        /// <param name="filePath">The path to save to.</param>
        public static void SaveToFile(StrategyDefinition strategy, string filePath)
        {
            var json = JsonSerializer.Serialize(strategy, JsonOptions);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Gets a parameter value from a segment.
        /// </summary>
        public static T? GetParameterValue<T>(StrategySegment segment, string parameterName)
        {
            var param = segment.Parameters.FirstOrDefault(p => p.Name == parameterName);
            if (param?.Value == null)
                return default;

            try
            {
                if (param.Value is JsonElement jsonElement)
                {
                    // Handle JSON element conversion
                    if (typeof(T) == typeof(string))
                        return (T)(object)jsonElement.GetString()!;
                    if (typeof(T) == typeof(int))
                        return (T)(object)jsonElement.GetInt32();
                    if (typeof(T) == typeof(double))
                        return (T)(object)jsonElement.GetDouble();
                    if (typeof(T) == typeof(bool))
                        return (T)(object)jsonElement.GetBoolean();
                    if (typeof(T).IsEnum)
                        return (T)Enum.Parse(typeof(T), jsonElement.GetString()!);
                }

                // Direct conversion
                return (T)Convert.ChangeType(param.Value, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// Validates a strategy definition.
        /// </summary>
        public static ValidationResult Validate(StrategyDefinition strategy)
        {
            return strategy.Validate();
        }

        /// <summary>
        /// Gets all available dates that have strategy files.
        /// </summary>
        public static List<DateOnly> GetAvailableDates(string? baseFolder = null)
        {
            baseFolder ??= GetDefaultFolder();
            var dates = new List<DateOnly>();

            if (!Directory.Exists(baseFolder))
                return dates;

            var directories = Directory.GetDirectories(baseFolder);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                if (DateOnly.TryParse(dirName, out var date))
                {
                    // Only include dates that have strategy files
                    if (Directory.GetFiles(dir, "*.json").Length > 0)
                        dates.Add(date);
                }
            }

            return dates.OrderByDescending(d => d).ToList();
        }
    }
}
