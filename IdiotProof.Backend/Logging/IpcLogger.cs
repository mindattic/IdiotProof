// ============================================================================
// IpcLogger - Logs all IPC communication for debugging/overnight monitoring
// ============================================================================
//
// IPC events are logged to SessionLogger with category "IPC".
// This consolidates all logs into the session log files.
//
// ============================================================================

namespace IdiotProof.Backend.Logging;

/// <summary>
/// Logs IPC communication to SessionLogger for overnight monitoring.
/// All IPC events use the "IPC" category in the session log.
/// </summary>
public static class IpcLogger
{
    private static bool _enabled = true;

    /// <summary>
    /// Shared session logger instance (set from Program.cs via IpcServer).
    /// </summary>
    public static SessionLogger? SessionLogger { get; set; }

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
    /// Logs an error during IPC communication.
    /// </summary>
    public static void LogError(string context, Exception ex)
    {
        Log($"[ERROR] Context={context} Error={ex.Message}");
    }

    private static void Log(string message)
    {
        if (!_enabled) return;

        SessionLogger?.LogEvent("IPC", message);
    }

    private static string Truncate(string text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
