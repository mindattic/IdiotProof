// ============================================================================
// LogPaths - Centralized log folder path management
// ============================================================================
//
// All logs are stored in: <SolutionRoot>\IdiotProof.Core\Logs\
// This keeps logs in the Core project directory for easy access.
//
// ============================================================================

using IdiotProof.Shared.Settings;

namespace IdiotProof.Backend.Logging;

/// <summary>
/// Provides centralized path management for all log files.
/// </summary>
public static class LogPaths
{
    /// <summary>
    /// Gets the logs folder path and ensures it exists.
    /// Returns: <SolutionRoot>\IdiotProof.Core\Logs\
    /// </summary>
    public static string GetLogsFolder()
    {
        var logsPath = SettingsManager.GetLogsFolder();
        Directory.CreateDirectory(logsPath);
        return logsPath;
    }
}


