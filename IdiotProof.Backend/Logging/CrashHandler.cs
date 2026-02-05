using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IdiotProof.Backend.Logging
{
    /// <summary>
    /// Handles crash dumps, session logging, and console output capture.
    /// </summary>
    public static class CrashHandler
    {
        // P/Invoke for console close handler (Windows only)
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);
        private delegate bool ConsoleCtrlHandler(int ctrlType);
        private static ConsoleCtrlHandler? _consoleCtrlHandler;

        // Control signal types
        private const int CTRL_C_EVENT = 0;
        private const int CTRL_BREAK_EVENT = 1;
        private const int CTRL_CLOSE_EVENT = 2;
        private const int CTRL_LOGOFF_EVENT = 5;
        private const int CTRL_SHUTDOWN_EVENT = 6;

        // Console output capture for crash dumps and session logs
        private static readonly StringBuilder _consoleLog = new();
        private static readonly object _logLock = new();
        private const int MaxConsoleLogSize = 15 * 1024 * 1024; // 15 MB max buffer
        private const int TrimToSize = 10 * 1024 * 1024;        // Trim to 10 MB when exceeded

        /// <summary>
        /// Sets up crash handling, console close handling, and console output capture.
        /// </summary>
        public static void Setup()
        {
            // Redirect console output to capture it
            var originalOut = Console.Out;
            var capturingWriter = new CapturingTextWriter(originalOut, _consoleLog, _logLock, MaxConsoleLogSize, TrimToSize);
            Console.SetOut(capturingWriter);

            // Handle unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                WriteCrashDump(e.ExceptionObject as Exception, "UnhandledException");
            };

            // Handle task exceptions
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                WriteCrashDump(e.Exception, "UnobservedTaskException");
                e.SetObserved();
            };

            // Handle process exit (normal termination)
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                WriteSessionLog("ProcessExit");
            };

            // Handle console close button (X), Ctrl+C, logoff, shutdown (Windows only)
            if (OperatingSystem.IsWindows())
            {
                _consoleCtrlHandler = new ConsoleCtrlHandler(OnConsoleCtrlEvent);
                SetConsoleCtrlHandler(_consoleCtrlHandler, true);
            }

            // Handle Ctrl+C via .NET (cross-platform)
            Console.CancelKeyPress += (sender, e) =>
            {
                WriteSessionLog("Ctrl+C");
                // Don't cancel - let the app terminate
            };
        }

        /// <summary>
        /// Handles Windows console control events (close button, Ctrl+C, logoff, shutdown).
        /// </summary>
        private static bool OnConsoleCtrlEvent(int ctrlType)
        {
            var source = ctrlType switch
            {
                CTRL_C_EVENT => "Ctrl+C",
                CTRL_BREAK_EVENT => "Ctrl+Break",
                CTRL_CLOSE_EVENT => "Console Close (X button)",
                CTRL_LOGOFF_EVENT => "User Logoff",
                CTRL_SHUTDOWN_EVENT => "System Shutdown",
                _ => $"Unknown ({ctrlType})"
            };

            WriteSessionLog(source);

            // Return false to allow the default handler to terminate the process
            // We have ~5 seconds to complete before Windows forcefully terminates
            return false;
        }

        /// <summary>
        /// Ensures the logs folder exists and returns the full path.
        /// Returns: <ProjectRoot>\Logs\
        /// </summary>
        private static string EnsureLogsFolder()
        {
            return LogPaths.GetLogsFolder();
        }

        /// <summary>
        /// Writes session log to a timestamped file in the logs folder.
        /// </summary>
        public static void WriteSessionLog(string exitReason)
        {
            try
            {
                var logsPath = EnsureLogsFolder();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var filename = Path.Combine(logsPath, $"session_{timestamp}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("═══════════════════════════════════════════════════════════════════");
                sb.AppendLine("                    IDIOTPROOF SESSION LOG");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════");
                sb.AppendLine($"Session End: {DateTime.Now:yyyy-MM-dd hh:mm:ss tt}");
                sb.AppendLine($"Exit Reason: {exitReason}");
                sb.AppendLine();
                sb.AppendLine("═══════════════════════════════════════════════════════════════════");
                sb.AppendLine("                         CONSOLE OUTPUT");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════");

                lock (_logLock)
                {
                    sb.Append(_consoleLog);
                }

                File.WriteAllText(filename, sb.ToString());

                // Write to stderr (bypasses captured stdout)
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Session log saved to: {filename}");
            }
            catch
            {
                // Fail gracefully - logging should never crash the app
            }
        }

        /// <summary>
        /// Writes crash dump to a timestamped file in the logs folder.
        /// </summary>
        public static void WriteCrashDump(Exception? exception, string source)
        {
            try
            {
                var logsPath = EnsureLogsFolder();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var filename = Path.Combine(logsPath, $"crash_{timestamp}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("═══════════════════════════════════════════════════════════════════");
                sb.AppendLine("                    IDIOTPROOF CRASH DUMP");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════");
                sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd hh:mm:ss tt}");
                sb.AppendLine($"Source: {source}");
                sb.AppendLine();

                if (exception != null)
                {
                    sb.AppendLine("═══════════════════════════════════════════════════════════════════");
                    sb.AppendLine("                         EXCEPTION DETAILS");
                    sb.AppendLine("═══════════════════════════════════════════════════════════════════");
                    sb.AppendLine($"Type: {exception.GetType().FullName}");
                    sb.AppendLine($"Message: {exception.Message}");
                    sb.AppendLine();
                    sb.AppendLine("Stack Trace:");
                    sb.AppendLine(exception.StackTrace);

                    // Include inner exceptions
                    var inner = exception.InnerException;
                    int depth = 1;
                    while (inner != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"─── Inner Exception {depth} ───");
                        sb.AppendLine($"Type: {inner.GetType().FullName}");
                        sb.AppendLine($"Message: {inner.Message}");
                        sb.AppendLine("Stack Trace:");
                        sb.AppendLine(inner.StackTrace);
                        inner = inner.InnerException;
                        depth++;
                    }
                }

                sb.AppendLine();
                sb.AppendLine("═══════════════════════════════════════════════════════════════════");
                sb.AppendLine("                         CONSOLE OUTPUT");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════");

                lock (_logLock)
                {
                    sb.Append(_consoleLog);
                }

                File.WriteAllText(filename, sb.ToString());

                // Also write to original console
                Console.Error.WriteLine();
                Console.Error.WriteLine($"*** CRASH DUMP SAVED TO: {filename} ***");
            }
            catch
            {
                // Last resort - don't throw from crash handler
            }
        }
    }
}


