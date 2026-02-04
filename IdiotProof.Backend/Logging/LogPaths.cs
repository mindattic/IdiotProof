// ============================================================================
// LogPaths - Centralized log folder path management
// ============================================================================
//
// All logs are stored in: <ProjectRoot>\Logs\
// This keeps logs in the application directory for easy access.
//
// ============================================================================

namespace IdiotProof.Backend.Logging;

/// <summary>
/// Provides centralized path management for all log files.
/// </summary>
public static class LogPaths
{
    private const string LogsFolder = "Logs";

    /// <summary>
    /// Gets the base application folder (project root or executable directory).
    /// </summary>
    public static string GetBaseFolder()
    {
        // Use the directory where the executable is located
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Gets the logs folder path and ensures it exists.
    /// Returns: <ProjectRoot>\Logs\
    /// </summary>
    public static string GetLogsFolder()
    {
        // Try to find the project root by looking for solution file or go up from bin folder
        var baseDir = GetProjectRoot();
        var logsPath = Path.Combine(baseDir, LogsFolder);
        Directory.CreateDirectory(logsPath);
        return logsPath;
    }

    /// <summary>
    /// Gets the project root directory by navigating up from the executable location.
    /// </summary>
    private static string GetProjectRoot()
    {
        var currentDir = AppContext.BaseDirectory;

        // Navigate up from bin/Debug/net10.0 or similar to find project root
        // Look for a .sln file or Strategies folder as markers
        var dir = new DirectoryInfo(currentDir);
        while (dir != null)
        {
            // Check if this looks like the project root (has .sln file or Strategies folder)
            if (dir.GetFiles("*.sln").Length > 0 ||
                Directory.Exists(Path.Combine(dir.FullName, "Strategies")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        // Fallback to executable directory if project root not found
        return currentDir;
    }
}
