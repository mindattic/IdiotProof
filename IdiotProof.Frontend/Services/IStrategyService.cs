// ============================================================================
// IStrategyService - Interface for strategy persistence
// ============================================================================

using IdiotProof.Shared.Models;

namespace IdiotProof.Frontend.Services
{
    /// <summary>
    /// Service for loading, saving, and managing strategies.
    /// Supports both IdiotScript (.idiot) and JSON (.json) formats.
    /// </summary>
    public interface IStrategyService
    {
        /// <summary>
        /// Gets the collection for the specified date.
        /// </summary>
        Task<StrategyCollection> GetCollectionAsync(DateOnly date);

        /// <summary>
        /// Saves the collection for the specified date.
        /// </summary>
        Task SaveCollectionAsync(StrategyCollection collection);

        /// <summary>
        /// Saves an individual strategy to its own file.
        /// </summary>
        Task<string> SaveStrategyAsync(StrategyDefinition strategy, DateOnly date);

        /// <summary>
        /// Deletes a strategy file.
        /// </summary>
        Task DeleteStrategyAsync(StrategyDefinition strategy, DateOnly date);

        /// <summary>
        /// Renames a strategy and updates its file.
        /// </summary>
        Task RenameStrategyAsync(StrategyDefinition strategy, string newName, DateOnly date);

        /// <summary>
        /// Clones a strategy with a new name.
        /// </summary>
        Task<StrategyDefinition> CloneStrategyAsync(StrategyDefinition strategy, DateOnly date);

        /// <summary>
        /// Gets all available dates that have strategy files.
        /// </summary>
        Task<List<DateOnly>> GetAvailableDatesAsync();

        /// <summary>
        /// Exports a strategy to fluent API code.
        /// </summary>
        string ExportToCode(StrategyDefinition strategy);

        /// <summary>
        /// Exports a strategy to IdiotScript format.
        /// </summary>
        string ExportToIdiotScript(StrategyDefinition strategy);

        /// <summary>
        /// Exports a strategy to formatted IdiotScript.
        /// </summary>
        string ExportToFormattedIdiotScript(StrategyDefinition strategy);

        /// <summary>
        /// Imports a strategy from IdiotScript.
        /// </summary>
        StrategyDefinition? ImportFromIdiotScript(string script);

        /// <summary>
        /// Imports a strategy from IdiotScript with error details.
        /// </summary>
        (StrategyDefinition? Strategy, string? Error) ImportFromIdiotScriptWithError(string script);

        /// <summary>
        /// Gets the path where strategies are stored.
        /// </summary>
        string StrategiesFolder { get; }
    }
}
