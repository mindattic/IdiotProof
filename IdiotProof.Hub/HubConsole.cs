using System.Text;

namespace IdiotProof.Hub;

/// <summary>
/// The Hub's own visible window (IP-T331), the way Prose.Hub does it: a money-green console so
/// it's instantly recognizable among other terminals, the PID in the title so an agent can be
/// pointed at "the one in the title bar", and every CLI/MCP command echoed as it runs
/// (<c>&gt;&gt;&gt;</c> in, <c>&lt;&lt;&lt;</c> out with status and timing) so a human can watch
/// it working. When <see cref="LogEnvVar"/> names a file, everything the window shows is also
/// written there, which is what <c>idiotproof hub logs</c> tails.
/// </summary>
public static class HubConsole
{
    /// <summary>File the window's output is mirrored to (set by <c>idiotproof hub start</c>).</summary>
    public const string LogEnvVar = "IDIOTPROOF_HUB_LOG";

    private static TextWriter output = Console.Out;
    private static readonly Lock gate = new();

    /// <summary>
    /// Call first thing in Program.cs: paints the window green, titles it, and tees Console.Out
    /// and Console.Error into the log file. Every cosmetic step is guarded: with no real console
    /// buffer (piped, a service host) they throw, and cosmetics must never stop the Hub.
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

        if (Environment.GetEnvironmentVariable(LogEnvVar) is { Length: > 0 } path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                Console.SetOut(new TeeWriter(Console.Out, file));
                Console.SetError(new TeeWriter(Console.Error, file));
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[hub] could not open log {path}: {ex.Message}");
            }
        }
        output = Console.Out;
    }

    /// <summary>The startup banner under the logo: where it listens and that commands echo below.</summary>
    public static void Banner(int port, int operations, bool monitorHosted)
    {
        Line("");
        Line("========================================================================");
        Line($" IdiotProof Hub is running  -  http://127.0.0.1:{port}  -  PID {Environment.ProcessId}");
        Line($" {operations} operations  -  Monitor {(monitorHosted ? "HOSTED: the trading loop is live" : "not hosted (IDIOTPROOF_HUB_MONITOR=0)")}");
        Line(" Every CLI and MCP command is echoed below as it runs. Times are ET.");
        Line("========================================================================");
        Line("");
    }

    /// <summary><c>[9:41:07 PM] &gt;&gt;&gt; cli  account summary  --mode live</c></summary>
    public static void CommandIn(string caller, string operation, string detail) =>
        Line($"[{Now()}] >>> {caller,-4} {operation}{(detail.Length > 0 ? "  " + Clip(detail, 160) : "")}");

    /// <summary><c>[9:41:08 PM] &lt;&lt;&lt; cli  account summary  ok  412ms</c></summary>
    public static void CommandOut(string caller, string operation, string status, int exitCode, long elapsedMs, string? message) =>
        Line($"[{Now()}] <<< {caller,-4} {operation}  {status}{(exitCode != 0 ? $" ({exitCode})" : "")}  {elapsedMs}ms"
             + (string.IsNullOrWhiteSpace(message) ? "" : "  " + Clip(message.ReplaceLineEndings(" "), 200)));

    public static void Line(string text)
    {
        lock (gate) output.WriteLine(text);
    }

    private static string Now() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IdiotProof.Scripting.MarketTime.Eastern)
            .ToString("h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "...";

    /// <summary>Writes to the window and the log file at once.</summary>
    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => first.Encoding;

        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(string? value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void WriteLine(string? value)
        {
            first.WriteLine(value);
            second.WriteLine(value);
        }

        public override void Flush()
        {
            first.Flush();
            second.Flush();
        }
    }
}
