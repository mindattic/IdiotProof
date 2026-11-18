using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Hub;

/// <summary>One error the Hub met: a logged Error/Critical entry or an exception nobody caught.</summary>
public sealed record HubError(LogLevel Level, string Category, EventId EventId, string Message, Exception? Exception, string? Template);

/// <summary>
/// Every error in the Hub files a fix ticket (IP-T579, the user's rule: "every error that gets
/// thrown in a hub needs to automatically file a ticket"), carrying what an agent picking it up
/// needs: the full exception text, the log category and message, the command that was running,
/// the last console lines, the Hub build and the time in ET.
///
/// Guarded so it can never hurt the Hub or flood the queue:
///   • a fingerprint (exception type, message template or message, top stack frames) files once;
///     a repeat adds a note to that ticket at most hourly, or files a new one if it was closed;
///   • at most <see cref="MaxNewPerHour"/> new tickets an hour (the rest are counted in the next);
///   • filing runs on one background worker through the <c>tickets</c> CLI and never throws;
///     its own problems are logged at Warning under <see cref="Category"/>, which is never reported.
/// <c>IDIOTPROOF_HUB_ERROR_TICKETS=0</c> turns it off.
/// </summary>
public static class HubErrorTickets
{
    public const string Category = "IdiotProof.Hub.HubErrorTickets";
    public const string KnobEnvVar = "IDIOTPROOF_HUB_ERROR_TICKETS";
    private const int MaxNewPerHour = 10;
    private const string Repo = "IdiotProof";
    private static readonly TimeSpan NoteEvery = TimeSpan.FromHours(1);

    private static readonly Channel<(HubError Error, string? LastCommand, IReadOnlyList<string> Recent, DateTime Utc)> queue =
        Channel.CreateBounded<(HubError, string?, IReadOnlyList<string>, DateTime)>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite });
    private static readonly Lock registryGate = new();
    private static readonly List<DateTime> filedThisHour = [];
    private static int skippedForCap;
    private static Task? worker;
    private static ILogger? log;

    private static string RegistryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdiotProof", "hub-error-tickets.json");

    /// <summary>Starts the worker and hooks the exceptions nobody caught. Call once at startup.</summary>
    public static void Start(ILoggerFactory loggers)
    {
        if (IsOff() || worker is not null) return;
        log = loggers.CreateLogger(Category);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(new HubError(LogLevel.Critical, "AppDomain.UnhandledException", default,
                "Unhandled exception (the Hub is going down)", e.ExceptionObject as Exception, null), waitForFiling: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Report(new HubError(LogLevel.Error, "TaskScheduler.UnobservedTaskException", default,
                "A faulted task's exception was never observed", e.Exception, null));
        worker = Task.Run(WorkAsync);
    }

    /// <summary>Queues an error to be filed. Never blocks the caller (except a dying process, briefly).</summary>
    public static void Report(HubError error, bool waitForFiling = false)
    {
        if (IsOff() || worker is null) return;
        var item = (error, HubConsole.LastCommand, HubConsole.RecentLines(), DateTime.UtcNow);
        if (!waitForFiling) { queue.Writer.TryWrite(item); return; }
        // The process is about to end: file this one directly, bounded, so the crash is not lost.
        try { FileOrNoteAsync(item).Wait(TimeSpan.FromSeconds(20)); } catch { }
    }

    private static bool IsOff() => Environment.GetEnvironmentVariable(KnobEnvVar)?.Trim() is "0" or "off" or "false";

    private static async Task WorkAsync()
    {
        await foreach (var item in queue.Reader.ReadAllAsync())
        {
            try { await FileOrNoteAsync(item); }
            catch (Exception ex) { log?.LogWarning(ex, "Could not file a ticket for a Hub error."); }
        }
    }

    private static async Task FileOrNoteAsync((HubError Error, string? LastCommand, IReadOnlyList<string> Recent, DateTime Utc) item)
    {
        var e = item.Error;
        var fingerprint = Fingerprint(e);
        Entry? existing;
        lock (registryGate) existing = Load().GetValueOrDefault(fingerprint);

        var open = existing is null ? false : await IsOpenAsync(existing.TicketId);
        if (open is null) { existing!.Count++; Save(fingerprint, existing); return; } // can't tell: don't risk a duplicate
        if (open is true && existing is not null)
        {
            existing.Count++;
            if (DateTime.UtcNow - existing.LastNotedUtc >= NoteEvery)
            {
                await RunTicketsAsync(["ticket", "note", existing.TicketId,
                    $"Recurred: {existing.Count} times since it was filed; latest {Et(item.Utc)} ET. {Clip(e.Exception?.Message ?? e.Message, 300)}"]);
                existing.LastNotedUtc = DateTime.UtcNow;
            }
            Save(fingerprint, existing);
            return;
        }

        lock (registryGate)
        {
            filedThisHour.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromHours(1));
            if (filedThisHour.Count >= MaxNewPerHour) { skippedForCap++; return; }
            filedThisHour.Add(DateTime.UtcNow);
        }

        var what = e.Exception is { } ex ? $"{ex.GetType().Name}: {Clip(ex.Message, 90)}" : Clip(e.Message, 110);
        var summary = $"Hub error in {ShortCategory(e.Category)}: {what}".ReplaceLineEndings(" ");
        var descriptionFile = Path.Combine(Path.GetTempPath(), $"hub-error-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(descriptionFile, Describe(item, fingerprint, existing));
        try
        {
            var output = await RunTicketsAsync(["ticket", "new", "--type", "fix", "--repo", Repo,
                "--name", "Hub error " + ShortCategory(e.Category), "--summary", Clip(summary, 200), "--description-file", descriptionFile]);
            var id = output?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("IP-T", StringComparison.Ordinal));
            if (id is null) { log?.LogWarning("tickets did not return a ticket id for a Hub error: {Output}", Clip(output ?? "", 300)); return; }
            Save(fingerprint, new Entry { TicketId = id, Count = 1, LastNotedUtc = DateTime.UtcNow });
            HubConsole.Line($"[hub] filed {id} for: {summary}", LogLevel.Warning);
        }
        finally
        {
            try { File.Delete(descriptionFile); } catch { }
        }
    }

    private static string Describe((HubError Error, string? LastCommand, IReadOnlyList<string> Recent, DateTime Utc) item, string fingerprint, Entry? closedBefore)
    {
        var e = item.Error;
        var sb = new StringBuilder();
        sb.AppendLine("Filed automatically by the IdiotProof Hub (IP-T579): every Hub error becomes a fix ticket.");
        if (closedBefore is not null) sb.AppendLine($"\n**Regression:** this error was filed before as {closedBefore.TicketId}, which is closed.");
        sb.AppendLine();
        sb.AppendLine("## Where");
        sb.AppendLine($"- When: {Et(item.Utc)} ET ({item.Utc:O} UTC)");
        sb.AppendLine($"- Level: {e.Level}");
        sb.AppendLine($"- Logger category: `{e.Category}`" + (e.EventId.Id != 0 ? $" (event {e.EventId.Id}{(e.EventId.Name is { } n ? " " + n : "")})" : ""));
        sb.AppendLine($"- Command running last: {item.LastCommand ?? "none since the Hub started"}");
        sb.AppendLine($"- Hub: PID {Environment.ProcessId}, {Environment.ProcessPath}, built {BuildStamp()}");
        sb.AppendLine($"- Fingerprint: `{fingerprint}` (repeats add notes here instead of new tickets)");
        sb.AppendLine();
        sb.AppendLine("## Message");
        sb.AppendLine("```");
        sb.AppendLine(e.Message);
        sb.AppendLine("```");
        if (e.Template is { } t && t != e.Message) sb.AppendLine($"Template: `{t}`");
        if (e.Exception is { } ex)
        {
            sb.AppendLine();
            sb.AppendLine("## Exception (full)");
            sb.AppendLine("```");
            sb.AppendLine(ex.ToString());
            sb.AppendLine("```");
            if (ex.Data.Count > 0)
            {
                sb.AppendLine("Exception data:");
                foreach (System.Collections.DictionaryEntry d in ex.Data) sb.AppendLine($"- {d.Key}: {d.Value}");
            }
        }
        sb.AppendLine();
        sb.AppendLine($"## Last {item.Recent.Count} Hub console lines (oldest first)");
        sb.AppendLine("```");
        foreach (var l in item.Recent) sb.AppendLine(l);
        sb.AppendLine("```");
        int skipped;
        lock (registryGate) { skipped = skippedForCap; skippedForCap = 0; }
        if (skipped > 0) sb.AppendLine($"\n{skipped} other Hub error(s) were not filed in the last hour (cap {MaxNewPerHour}/hour); see the Hub log.");
        return sb.ToString();
    }

    /// <summary>Stable across repeats: the exception type, the message template (or message without digits), and the top 3 stack frames.</summary>
    private static string Fingerprint(HubError e)
    {
        var frames = e.Exception?.StackTrace?.Split('\n').Select(f => f.Trim()).Where(f => f.StartsWith("at ", StringComparison.Ordinal))
            .Select(f => f.Split(" in ")[0]).Take(3) ?? [];
        var message = e.Template ?? new string((e.Exception?.Message ?? e.Message).Where(c => !char.IsDigit(c)).ToArray());
        var key = string.Join("|", [e.Category, e.Exception?.GetType().FullName ?? "", message, .. frames]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
    }

    private static async Task<bool?> IsOpenAsync(string ticketId)
    {
        var output = await RunTicketsAsync(["ticket", "show", ticketId]);
        if (output is null) return null;
        var first = output.Split('\n').FirstOrDefault() ?? "";
        return first.Contains("· open", StringComparison.Ordinal) ? true : first.Contains("· closed", StringComparison.Ordinal) || first.Contains("· wont-do", StringComparison.Ordinal) ? false : null;
    }

    private static async Task<string?> RunTicketsAsync(IReadOnlyList<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo("tickets") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["NO_COLOR"] = "1";
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            var text = System.Text.RegularExpressions.Regex.Replace(await stdout, @"\x1b\[[0-9;]*m", "");
            if (p.ExitCode != 0) { log?.LogWarning("tickets {Args} exited {Code}.", string.Join(' ', args.Take(2)), p.ExitCode); return null; }
            return text;
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Could not run the tickets CLI.");
            return null;
        }
    }

    private sealed class Entry
    {
        public string TicketId { get; set; } = "";
        public int Count { get; set; }
        public DateTime LastNotedUtc { get; set; }
    }

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            return File.Exists(RegistryPath)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(RegistryPath)) ?? []
                : [];
        }
        catch { return []; }
    }

    private static void Save(string fingerprint, Entry entry)
    {
        lock (registryGate)
        {
            try
            {
                var all = Load();
                all[fingerprint] = entry;
                Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
                File.WriteAllText(RegistryPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { log?.LogWarning(ex, "Could not save the Hub error-ticket registry."); }
        }
    }

    private static string ShortCategory(string category) => category.Split('.').LastOrDefault() is { Length: > 0 } last ? last : category;

    private static string Et(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, IdiotProof.Scripting.MarketTime.Eastern).ToString("yyyy-MM-dd h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture);

    private static string BuildStamp()
    {
        try { return Environment.ProcessPath is { } exe ? File.GetLastWriteTimeUtc(exe).ToString("yyyy-MM-dd HH:mm 'UTC'") : "unknown"; }
        catch { return "unknown"; }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "...";
}
