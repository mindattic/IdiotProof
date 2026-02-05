// ============================================================================
// LogManager - Centralized logging for IdiotProof
// ============================================================================
//
// All log files are stored in the IdiotProof.Logs folder:
//   {SolutionRoot}\IdiotProof.Logs\
//     Backend\
//       backend_2025-02-05.log
//       trades_2025-02-05.log
//     Console\
//       console_2025-02-05.log
//     Frontend\
//       frontend_2025-02-05.log
//
// ============================================================================

namespace IdiotProof.Logs;

/// <summary>
/// Provides centralized logging functionality for all IdiotProof projects.
/// </summary>
public static class LogManager
{
    private static readonly object _lock = new();
    private static string? _baseLogsFolder;

    /// <summary>
    /// Sets the base logs folder path.
    /// </summary>
    public static void SetBaseFolder(string path)
    {
        _baseLogsFolder = path;
        EnsureFolderExists(path);
    }

    /// <summary>
    /// Gets the base logs folder path.
    /// </summary>
    public static string GetBaseFolder()
    {
        return _baseLogsFolder ?? throw new InvalidOperationException(
            "LogManager.SetBaseFolder() must be called before using LogManager.");
    }

    /// <summary>
    /// Gets the logs folder for a specific project.
    /// </summary>
    public static string GetProjectFolder(string projectName)
    {
        var folder = Path.Combine(GetBaseFolder(), projectName);
        EnsureFolderExists(folder);
        return folder;
    }

    /// <summary>
    /// Gets the full path to a log file for a specific project and date.
    /// </summary>
    public static string GetLogFilePath(string projectName, string logName, DateOnly? date = null)
    {
        date ??= DateOnly.FromDateTime(DateTime.Now);
        var fileName = $"{logName}_{date:yyyy-MM-dd}.log";
        return Path.Combine(GetProjectFolder(projectName), fileName);
    }

    /// <summary>
    /// Writes a log entry to a project's log file.
    /// </summary>
    public static void Log(string projectName, string logName, string message, LogLevel level = LogLevel.Info)
    {
        var filePath = GetLogFilePath(projectName, logName);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{level}] {message}";

        lock (_lock)
        {
            File.AppendAllText(filePath, logEntry + Environment.NewLine);
        }
    }

    /// <summary>
    /// Writes an info log entry.
    /// </summary>
    public static void Info(string projectName, string logName, string message)
        => Log(projectName, logName, message, LogLevel.Info);

    /// <summary>
    /// Writes a warning log entry.
    /// </summary>
    public static void Warn(string projectName, string logName, string message)
        => Log(projectName, logName, message, LogLevel.Warn);

    /// <summary>
    /// Writes an error log entry.
    /// </summary>
    public static void Error(string projectName, string logName, string message)
        => Log(projectName, logName, message, LogLevel.Error);

    /// <summary>
    /// Writes an error log entry with exception details.
    /// </summary>
    public static void Error(string projectName, string logName, string message, Exception ex)
        => Log(projectName, logName, $"{message}: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);

    /// <summary>
    /// Writes a debug log entry.
    /// </summary>
    public static void Debug(string projectName, string logName, string message)
        => Log(projectName, logName, message, LogLevel.Debug);

    /// <summary>
    /// Writes a trade log entry (for Backend trade logging).
    /// </summary>
    public static void Trade(string projectName, string message)
        => Log(projectName, "trades", message, LogLevel.Trade);

    /// <summary>
    /// Ensures a folder exists, creating it if necessary.
    /// </summary>
    private static void EnsureFolderExists(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
    }
}

/// <summary>
/// Log severity levels.
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Trade
}
