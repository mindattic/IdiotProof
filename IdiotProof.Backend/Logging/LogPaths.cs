// ============================================================================
// LogPaths - Centralized log folder path management
// ============================================================================
//
// All logs are stored in: MyDocuments\IdiotProof\Logs\
// This keeps logs with other IdiotProof data (Settings, Strategies).
//
// ============================================================================

namespace IdiotProof.Backend.Logging;

/// <summary>
/// Provides centralized path management for all log files.
/// </summary>
public static class LogPaths
{
    private const string IdiotProofFolder = "IdiotProof";
    private const string LogsFolder = "Logs";

    /// <summary>
    /// Gets the base IdiotProof folder in MyDocuments.
    /// </summary>
    public static string GetBaseFolder()
    {
        var myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(myDocuments, IdiotProofFolder);
    }

    /// <summary>
    /// Gets the logs folder path and ensures it exists.
    /// Returns: MyDocuments\IdiotProof\Logs\
    /// </summary>
    public static string GetLogsFolder()
    {
        var logsPath = Path.Combine(GetBaseFolder(), LogsFolder);
        Directory.CreateDirectory(logsPath);
        return logsPath;
    }
}
