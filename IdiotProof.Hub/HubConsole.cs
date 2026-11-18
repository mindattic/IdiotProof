using System.Text;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Hub;

/// <summary>
/// The Hub's own visible window (IP-T331), the way Prose.Hub does it: a money-green console so
/// it's instantly recognizable among other terminals, the PID in the title so an agent can be
/// pointed at "the one in the title bar", and every CLI/MCP command echoed as it runs
/// (<c>&gt;&gt;&gt;</c> in, <c>&lt;&lt;&lt;</c> out with status and timing) so a human can watch
/// it working. When <see cref="LogEnvVar"/> names a file, everything the window shows is also
/// written there, which is what <c>idiotproof hub logs</c> tails.
///
/// The screen only prints while the window is on screen (IP-T577): hidden in the tray, nothing is
/// drawn, so a busy Hub spends nothing on a terminal nobody is looking at; the log file still gets
/// every line. Warnings and errors are colored on screen to stand out (IP-T578); the file is plain.
/// </summary>
public static class HubConsole
{
    /// <summary>File the window's output is mirrored to (set by <c>idiotproof hub start</c>).</summary>
    public const string LogEnvVar = "IDIOTPROOF_HUB_LOG";

    /// <summary>The tray icon's knob (IP-T574): <c>0</c> = no icon, <c>shown</c> = icon with the window visible at start.</summary>
    public const string TrayEnvVar = "IDIOTPROOF_HUB_TRAY";

    /// <summary>How many recent lines <see cref="RecentLines"/> keeps (context for an error ticket).</summary>
    private const int RecentKept = 40;

    private static long commandCount;
    private static volatile string? lastCommand;

    /// <summary>How many commands have come in since the Hub started (the tray icon shows it).</summary>
    public static long CommandCount => Interlocked.Read(ref commandCount);

    /// <summary>The latest command and when it came in, e.g. <c>account summary (9:41:07 PM)</c>; null before the first.</summary>
    public static string? LastCommand => lastCommand;

    /// <summary>Whether the window is on screen. The tray icon sets it; with no icon the window is always shown.</summary>
    public static Func<bool> WindowVisible { get; set; } = () => true;

    /// <summary><c>3d 4h</c>, <c>2h 15m</c> or <c>7m</c>.</summary>
    public static string Span(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h" : span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{(int)span.TotalMinutes}m";

    private static TextWriter screen = Console.Out;
    private static TextWriter? file;
    private static readonly Lock gate = new();
    private static readonly Queue<string> recent = new();

    /// <summary>
    /// Call first thing in Program.cs: paints the window green, titles it, and routes Console.Out
    /// and Console.Error to the screen (while visible) and the log file. Every cosmetic step is
    /// guarded: with no real console buffer (piped, a service host) they throw, and cosmetics must
    /// never stop the Hub.
    /// </summary>
    public static void Start()
    {
        try
        {
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Clear(); // repaints the whole buffer, not just text written from here on
        }
        catch (IOException) { }
        catch (PlatformNotSupportedException) { }

        try { Console.Title = $"IdiotProof Hub - PID: {Environment.ProcessId}"; }
        catch (IOException) { }
        catch (PlatformNotSupportedException) { }

        screen = Console.Out;
        var screenError = Console.Error;
        if (Environment.GetEnvironmentVariable(LogEnvVar) is { Length: > 0 } path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                file = TextWriter.Synchronized(new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true });
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[hub] could not open log {path}: {ex.Message}");
            }
        }
        Console.SetOut(new HubWriter(screen));
        Console.SetError(new HubWriter(screenError));

        // The runtime prints an unhandled exception straight to the native stderr, past the writer, so
        // `idiotproof hub start` would never see a startup crash in the log; write it ourselves.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { Line($"Unhandled exception: {e.ExceptionObject}", LogLevel.Critical); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        };
    }

    /// <summary>The startup banner under the logo: where it listens and that commands echo below.</summary>
    public static void Banner(int port, int operations, bool monitorHosted)
    {
        Line("");
        Line("========================================================================");
        Line($" IdiotProof Hub is running  -  http://127.0.0.1:{port}  -  PID {Environment.ProcessId}");
        Line($" {operations} operations  -  Monitor {(monitorHosted ? "HOSTED: the trading loop is live" : "not hosted (IDIOTPROOF_HUB_MONITOR=0)")}");
        Line(" Every CLI and MCP command is echoed below as it runs. Times are ET.");
        Line(" Hidden in the tray, the window prints nothing; the log file keeps every line.");
        Line("========================================================================");
        Line("");
    }

    /// <summary><c>[9:41:07 PM] &gt;&gt;&gt; cli  account summary  --mode live</c></summary>
    public static void CommandIn(string caller, string operation, string detail)
    {
        Interlocked.Increment(ref commandCount);
        lastCommand = $"{Clip(operation, 60)} ({Now()})";
        Line($"[{Now()}] >>> {caller,-4} {operation}{(detail.Length > 0 ? "  " + Clip(detail, 160) : "")}");
    }

    /// <summary><c>[9:41:08 PM] &lt;&lt;&lt; cli  account summary  ok  412ms</c></summary>
    public static void CommandOut(string caller, string operation, string status, int exitCode, long elapsedMs, string? message) =>
        Line($"[{Now()}] <<< {caller,-4} {operation}  {status}{(exitCode != 0 ? $" ({exitCode})" : "")}  {elapsedMs}ms"
             + (string.IsNullOrWhiteSpace(message) ? "" : "  " + Clip(message.ReplaceLineEndings(" "), 200)));

    public static void Line(string text) => Line(text, LogLevel.Information);

    /// <summary>
    /// One line (or block) to the log file always, and to the screen while the window is visible,
    /// colored by <paramref name="level"/>: warnings black on yellow, errors and critical white on red.
    /// </summary>
    public static void Line(string text, LogLevel level)
    {
        lock (gate)
        {
            Remember(text);
            file?.WriteLine(text);
            if (!IsVisible()) return;
            var (fore, back) = ColorsFor(level);
            if (fore is null)
            {
                screen.WriteLine(text);
                return;
            }
            ConsoleColor oldFore = ConsoleColor.White, oldBack = ConsoleColor.DarkGreen;
            try
            {
                oldFore = Console.ForegroundColor;
                oldBack = Console.BackgroundColor;
                Console.ForegroundColor = fore.Value;
                Console.BackgroundColor = back!.Value;
                screen.WriteLine(text);
            }
            catch (IOException) { screen.WriteLine(text); }
            finally
            {
                try { Console.ForegroundColor = oldFore; Console.BackgroundColor = oldBack; }
                catch (IOException) { }
            }
        }
    }

    /// <summary>The last lines written (screen or not), oldest first: the context an error ticket carries.</summary>
    public static IReadOnlyList<string> RecentLines()
    {
        lock (gate) return [.. recent];
    }

    private static (ConsoleColor? Fore, ConsoleColor? Back) ColorsFor(LogLevel level) => level switch
    {
        LogLevel.Warning => (ConsoleColor.Black, ConsoleColor.Yellow),
        LogLevel.Error or LogLevel.Critical => (ConsoleColor.White, ConsoleColor.DarkRed),
        _ => (null, null),
    };

    private static bool IsVisible()
    {
        try { return WindowVisible(); }
        catch { return true; }
    }

    private static void Remember(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            recent.Enqueue(line.TrimEnd('\r'));
            while (recent.Count > RecentKept) recent.Dequeue();
        }
    }

    private static string Now() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IdiotProof.Scripting.MarketTime.Eastern)
            .ToString("h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "...";

    /// <summary>
    /// What Console.Out and Console.Error become: whole lines go through <see cref="Line(string)"/>
    /// (file always, screen while visible); partial writes are buffered until their newline.
    /// </summary>
    private sealed class HubWriter(TextWriter screenWriter) : TextWriter
    {
        private readonly StringBuilder pending = new();
        public override Encoding Encoding => screenWriter.Encoding;

        public override void Write(char value)
        {
            if (value == '\n') { WriteLine(""); return; } // the line so far, blank lines included
            if (value == '\r') return;
            lock (pending) pending.Append(value);
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            foreach (var c in value) Write(c);
        }

        public override void WriteLine(string? value)
        {
            string text;
            lock (pending)
            {
                text = pending.Append(value).ToString();
                pending.Clear();
            }
            Line(text);
        }

        public override void Flush()
        {
            string? text = null;
            lock (pending)
            {
                if (pending.Length > 0) { text = pending.ToString(); pending.Clear(); }
            }
            if (text is not null) Line(text);
        }
    }
}
