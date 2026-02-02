// ============================================================================
// IpcLogger - Logs all IPC communication for debugging/overnight monitoring
// ============================================================================

namespace IdiotProof.Backend.Logging;

/// <summary>
/// Logs IPC communication to a dedicated file for overnight monitoring.
/// Logs are stored in: MyDocuments\IdiotProof\Logs\ipc_YYYY-MM-DD.log
/// </summary>
public static class IpcLogger
{
    private static readonly string LogFilePath;
    private static readonly object _lock = new();
    private static bool _enabled = true;

    static IpcLogger()
    {
        var logsPath = LogPaths.GetLogsFolder();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
        LogFilePath = Path.Combine(logsPath, $"ipc_{timestamp}.log");
    }

    /// <summary>
    /// Enable or disable IPC logging.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Logs an incoming request from a client.
    /// </summary>
    public static void LogRequest(Guid clientId, string messageType, string? payload = null)
    {
        Log($"[REQUEST] Client={clientId:N} Type={messageType}" + (payload != null ? $" Payload={Truncate(payload)}" : ""));
    }

    /// <summary>
    /// Logs an outgoing response to a client.
    /// </summary>
    public static void LogResponse(Guid clientId, string messageType, string? payload = null)
    {
        Log($"[RESPONSE] Client={clientId:N} Type={messageType}" + (payload != null ? $" Payload={Truncate(payload)}" : ""));
    }

    /// <summary>
    /// Logs a broadcast message to all clients.
    /// </summary>
    public static void LogBroadcast(string messageType, int clientCount)
    {
        Log($"[BROADCAST] Type={messageType} Clients={clientCount}");
    }

    /// <summary>
    /// Logs a client connection event.
    /// </summary>
    public static void LogConnection(Guid clientId, bool connected)
    {
        Log($"[CONNECTION] Client={clientId:N} Status={(connected ? "CONNECTED" : "DISCONNECTED")}");
    }

    /// <summary>
    /// Logs a heartbeat sent to clients.
    /// </summary>
    public static void LogHeartbeat(int clientCount)
    {
        Log($"[HEARTBEAT] Clients={clientCount}");
    }

    /// <summary>
    /// Logs an error during IPC communication.
    /// </summary>
    public static void LogError(string context, Exception ex)
    {
        Log($"[ERROR] Context={context} Error={ex.Message}");
    }

    private static void Log(string message)
    {
        if (!_enabled) return;

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{timestamp} {message}";

            lock (_lock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Fail silently - logging should never crash the app
        }
    }

    private static string Truncate(string text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
