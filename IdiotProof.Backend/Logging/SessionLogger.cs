// ============================================================================
// SessionLogger - Periodic session state logging with crash recovery
// ============================================================================
//
// LOGGING BEHAVIOR:
//   - Overwrites session state log every 20 minutes
//   - Writes final log on:
//     * Normal session close
//     * Application crash (via AppDomain.UnhandledException)
//     * Strategy session time end
//   - Maintains rolling session summary
//
// LOG FILES:
//   MyDocuments\IdiotProof\Logs\
//   ├── session_state.log      (current state, overwritten every 20 min)
//   ├── session_YYYY-MM-DD_HHmm_final.log  (final logs)
//   └── session_YYYY-MM-DD_HHmm_crash.log  (crash logs)
//
// ============================================================================

using System.Text;
using IdiotProof.Shared.Helpers;

namespace IdiotProof.Backend.Logging;

/// <summary>
/// Logs session state periodically and on close/crash.
/// </summary>
public sealed class SessionLogger : IDisposable
{
    private readonly string _sessionId;
    private readonly DateTime _sessionStart;
    private readonly Timer _periodicTimer;
    private readonly object _lock = new();
    private readonly List<StrategyLogEntry> _strategyEntries = [];
    private readonly StringBuilder _eventLog = new();
    private bool _disposed;
    private string? _currentStateFilePath;

    private const int LogIntervalMinutes = 20;

    /// <summary>
    /// Creates a new session logger and registers for crash handling.
    /// </summary>
    public SessionLogger()
    {
        _sessionId = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        _sessionStart = DateTime.Now;
        _currentStateFilePath = GetStateFilePath();

        // Register for unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // Start periodic logging timer (every 20 minutes)
        _periodicTimer = new Timer(
            callback: _ => WritePeriodicLog(),
            state: null,
            dueTime: TimeSpan.FromMinutes(LogIntervalMinutes),
            period: TimeSpan.FromMinutes(LogIntervalMinutes)
        );

        LogEvent("SESSION", $"Session started: {_sessionId}");
        WritePeriodicLog(); // Write initial state
    }

    /// <summary>
    /// Registers a strategy for tracking.
    /// </summary>
    public void RegisterStrategy(string symbol, string name, bool enabled)
    {
        lock (_lock)
        {
            _strategyEntries.Add(new StrategyLogEntry
            {
                Symbol = symbol,
                Name = name,
                Enabled = enabled,
                Status = "Registered",
                RegisteredAt = DateTime.Now
            });
        }
        LogEvent("STRATEGY", $"Registered: {symbol} - {name} (Enabled={enabled})");
    }

    /// <summary>
    /// Updates a strategy's status.
    /// </summary>
    public void UpdateStrategyStatus(string symbol, string status, string? details = null)
    {
        lock (_lock)
        {
            var entry = _strategyEntries.FirstOrDefault(e => e.Symbol == symbol);
            if (entry != null)
            {
                entry.Status = status;
                entry.LastUpdate = DateTime.Now;
                entry.Details = details;
            }
        }
        LogEvent("STATUS", $"{symbol}: {status}" + (details != null ? $" - {details}" : ""));
    }

    /// <summary>
    /// Records an order event.
    /// </summary>
    public void LogOrder(string symbol, string action, int quantity, double price, string? orderId = null)
    {
        lock (_lock)
        {
            var entry = _strategyEntries.FirstOrDefault(e => e.Symbol == symbol);
            if (entry != null)
            {
                entry.Orders.Add(new OrderLogEntry
                {
                    Timestamp = DateTime.Now,
                    Action = action,
                    Quantity = quantity,
                    Price = price,
                    OrderId = orderId
                });
            }
        }
        LogEvent("ORDER", $"{symbol}: {action} {quantity} @ ${price:F2}" + (orderId != null ? $" (ID={orderId})" : ""));
    }

    /// <summary>
    /// Records a fill event.
    /// </summary>
    public void LogFill(string symbol, string action, int quantity, double price, double? pnl = null)
    {
        lock (_lock)
        {
            var entry = _strategyEntries.FirstOrDefault(e => e.Symbol == symbol);
            if (entry != null)
            {
                entry.Fills.Add(new FillLogEntry
                {
                    Timestamp = DateTime.Now,
                    Action = action,
                    Quantity = quantity,
                    Price = price,
                    PnL = pnl
                });

                if (pnl.HasValue)
                {
                    entry.TotalPnL += pnl.Value;
                }
            }
        }
        var pnlStr = pnl.HasValue ? $" P&L=${pnl:F2}" : "";
        LogEvent("FILL", $"{symbol}: {action} {quantity} @ ${price:F2}{pnlStr}");
    }

    /// <summary>
    /// Logs a general event.
    /// </summary>
    public void LogEvent(string category, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] [{category}] {message}";

        lock (_lock)
        {
            _eventLog.AppendLine(line);

            // Keep event log from growing too large (keep last 1000 lines)
            var lines = _eventLog.ToString().Split('\n');
            if (lines.Length > 1000)
            {
                _eventLog.Clear();
                foreach (var l in lines.TakeLast(800))
                {
                    _eventLog.AppendLine(l.TrimEnd());
                }
            }
        }
    }

    /// <summary>
    /// Writes the final session log (call on normal close or session end).
    /// </summary>
    public void WriteFinalLog(string reason = "Session End")
    {
        var filePath = GetFinalFilePath();
        WriteLog(filePath, reason, isFinal: true);
        LogEvent("SESSION", $"Final log written: {reason}");
    }

    /// <summary>
    /// Forces an immediate state write.
    /// </summary>
    public void FlushState()
    {
        WritePeriodicLog();
    }

    private void WritePeriodicLog()
    {
        if (_disposed) return;

        try
        {
            _currentStateFilePath = GetStateFilePath();
            WriteLog(_currentStateFilePath, "Periodic Update", isFinal: false);
        }
        catch (Exception ex)
        {
            // Don't let logging errors crash the app
            Console.WriteLine($"{TimeStamp.NowBracketed} [SessionLogger] Error writing periodic log: {ex.Message}");
        }
    }

    private void WriteLog(string filePath, string reason, bool isFinal)
    {
        var sb = new StringBuilder();

        lock (_lock)
        {
            var runtime = DateTime.Now - _sessionStart;

            // Header
            sb.AppendLine("╔════════════════════════════════════════════════════════════════════════╗");
            sb.AppendLine($"║  SESSION LOG: {_sessionId,-56} ║");
            sb.AppendLine($"║  Reason: {reason,-61} ║");
            sb.AppendLine($"║  Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss,-55} ║");
            sb.AppendLine($"║  Runtime: {runtime:hh\\:mm\\:ss,-60} ║");
            sb.AppendLine("╠════════════════════════════════════════════════════════════════════════╣");

            // Strategy Summary
            sb.AppendLine("║  STRATEGY SUMMARY                                                       ║");
            sb.AppendLine("╠════════════════════════════════════════════════════════════════════════╣");

            if (_strategyEntries.Count == 0)
            {
                sb.AppendLine("║  No strategies registered                                               ║");
            }
            else
            {
                foreach (var entry in _strategyEntries)
                {
                    var enabledStr = entry.Enabled ? "*" : "o";
                    var statusLine = $"  [{enabledStr}] {entry.Symbol,-8} {entry.Name,-25} {entry.Status,-15}";
                    sb.AppendLine($"║{statusLine,-71} ║");

                    if (entry.Orders.Count > 0 || entry.Fills.Count > 0)
                    {
                        sb.AppendLine($"║      Orders: {entry.Orders.Count,-5} Fills: {entry.Fills.Count,-5} P&L: ${entry.TotalPnL,10:F2}           ║");
                    }
                }
            }

            // Order Details (if final)
            if (isFinal && _strategyEntries.Any(e => e.Orders.Count > 0 || e.Fills.Count > 0))
            {
                sb.AppendLine("╠════════════════════════════════════════════════════════════════════════╣");
                sb.AppendLine("║  ORDER DETAILS                                                          ║");
                sb.AppendLine("╠════════════════════════════════════════════════════════════════════════╣");

                foreach (var entry in _strategyEntries.Where(e => e.Orders.Count > 0 || e.Fills.Count > 0))
                {
                    sb.AppendLine($"║  {entry.Symbol} - {entry.Name,-50}       ║");
                    foreach (var order in entry.Orders)
                    {
                        var orderLine = $"    {order.Timestamp:HH:mm:ss} ORDER {order.Action,-5} {order.Quantity,5} @ ${order.Price:F2}";
                        sb.AppendLine($"║{orderLine,-71} ║");
                    }
                    foreach (var fill in entry.Fills)
                    {
                        var pnlStr = fill.PnL.HasValue ? $" P&L=${fill.PnL.Value:F2}" : "";
                        var fillLine = $"    {fill.Timestamp:HH:mm:ss} FILL  {fill.Action,-5} {fill.Quantity,5} @ ${fill.Price:F2}{pnlStr}";
                        sb.AppendLine($"║{fillLine,-71} ║");
                    }
                }
            }

            // Recent Events (last 50)
            sb.AppendLine("╠════════════════════════════════════════════════════════════════════════╣");
            sb.AppendLine("║  RECENT EVENTS                                                          ║");
            sb.AppendLine("╠════════════════════════════════════════════════════════════════════════╣");

            var events = _eventLog.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var recentEvents = events.TakeLast(50);
            foreach (var evt in recentEvents)
            {
                var truncated = evt.Length > 70 ? evt[..67] + "..." : evt;
                sb.AppendLine($"║  {truncated,-70} ║");
            }

            sb.AppendLine("╚════════════════════════════════════════════════════════════════════════╝");
        }

        // Write to file (overwrite for periodic, create new for final)
        var logsPath = LogPaths.GetLogsFolder();
        Directory.CreateDirectory(logsPath);
        File.WriteAllText(filePath, sb.ToString());
    }

    private string GetStateFilePath()
    {
        return Path.Combine(LogPaths.GetLogsFolder(), "session_state.log");
    }

    private string GetFinalFilePath()
    {
        return Path.Combine(LogPaths.GetLogsFolder(), $"session_{_sessionId}_final.log");
    }

    private string GetCrashFilePath()
    {
        return Path.Combine(LogPaths.GetLogsFolder(), $"session_{_sessionId}_crash.log");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            LogEvent("CRASH", $"Unhandled exception: {ex?.Message ?? "Unknown"}");
            if (ex != null)
            {
                LogEvent("CRASH", $"Stack trace: {ex.StackTrace}");
            }

            var crashPath = GetCrashFilePath();
            WriteLog(crashPath, $"CRASH: {ex?.Message ?? "Unknown"}", isFinal: true);
        }
        catch
        {
            // Last resort - try to write minimal crash info
            try
            {
                var crashPath = GetCrashFilePath();
                File.WriteAllText(crashPath, $"CRASH at {DateTime.Now}: {e.ExceptionObject}");
            }
            catch { }
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            WriteFinalLog("Process Exit");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _periodicTimer.Dispose();

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        WriteFinalLog("Disposed");
    }

    // Internal classes for tracking
    private sealed class StrategyLogEntry
    {
        public required string Symbol { get; init; }
        public required string Name { get; init; }
        public bool Enabled { get; init; }
        public string Status { get; set; } = "Unknown";
        public string? Details { get; set; }
        public DateTime RegisteredAt { get; init; }
        public DateTime? LastUpdate { get; set; }
        public List<OrderLogEntry> Orders { get; } = [];
        public List<FillLogEntry> Fills { get; } = [];
        public double TotalPnL { get; set; }
    }

    private sealed class OrderLogEntry
    {
        public DateTime Timestamp { get; init; }
        public required string Action { get; init; }
        public int Quantity { get; init; }
        public double Price { get; init; }
        public string? OrderId { get; init; }
    }

    private sealed class FillLogEntry
    {
        public DateTime Timestamp { get; init; }
        public required string Action { get; init; }
        public int Quantity { get; init; }
        public double Price { get; init; }
        public double? PnL { get; init; }
    }
}
