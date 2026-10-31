using System.Globalization;
using IdiotProof.Operations;
using IdiotProof.Operations.Hosting;
using MindAttic.Log;
using MindAttic.Log.Extensions;
using Microsoft.Data.Sqlite;
using Serilog;

namespace IdiotProof.Hub;

/// <summary>
/// The Hub's durable log (IP-T78). Everything its loggers emit (warnings and up from the Monitor
/// and the framework, plus one Information entry per CLI/MCP command under
/// <see cref="CommandsCategory"/>) goes to its own MindAttic.Log database,
/// <c>%APPDATA%\MindAttic\Logging\IdiotProof.Hub\MindAttic.Log.db</c>, next to the visible window
/// and its <c>IDIOTPROOF_HUB_LOG</c> mirror (HubConsole). <c>GET /api/logs</c> tails either one.
/// </summary>
public static class HubLogs
{
    /// <summary>Key-guarded: <c>GET /api/logs?lines=200&amp;source=window|db</c>.</summary>
    public const string LogsPath = "/api/logs";

    /// <summary>Logger category of the per-command entries.</summary>
    public const string CommandsCategory = "IdiotProof.Hub.Commands";

    private static readonly string[] Levels = ["VRB", "DBG", "INF", "WRN", "ERR", "FTL"];

    /// <summary>The database path for this user.</summary>
    public static string DatabasePath =>
        HubHosting.LogDatabasePath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>
    /// Adds the MindAttic.Log sink to the Hub's logging. The Hub's console filters (warnings and up
    /// for IdiotProof/Microsoft) apply to it too; the command entries are let through at Information
    /// for this sink only, so the window does not print them twice (HubConsole already echoes them).
    /// </summary>
    public static void AddHubLogDatabase(this ILoggingBuilder logging)
    {
        try
        {
            var path = DatabasePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteToMindAtticLog(new MindAtticLogOptions { Application = HubHosting.LogApplication, DbPath = path })
                .CreateLogger();
            logging.AddSerilog(logger, dispose: true);
            logging.AddFilter<Serilog.Extensions.Logging.SerilogLoggerProvider>("IdiotProof.Hub", LogLevel.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The durable log is an aid, never a reason the Hub can't start.
            Console.Error.WriteLine($"[hub] MindAttic.Log unavailable ({ex.Message}); logging to the window only.");
        }
    }

    /// <summary>The <see cref="LogsPath"/> handler.</summary>
    public static IResult Tail(int? lines, string? source)
    {
        var n = Math.Clamp(lines ?? 200, 1, LogTail.MaxLines);
        if (string.Equals(source, "db", StringComparison.OrdinalIgnoreCase))
        {
            var path = DatabasePath;
            if (!File.Exists(path)) return Results.Json(new { source = "db", path, lines = Array.Empty<string>() }, OperationJson.Options);
            try
            {
                return Results.Json(new { source = "db", path, lines = ReadDatabase(path, n) }, OperationJson.Options);
            }
            catch (SqliteException ex)
            {
                return Results.Json(new { error = "log_unreadable", message = ex.Message }, statusCode: 500);
            }
        }

        var window = Environment.GetEnvironmentVariable(HubConsole.LogEnvVar);
        if (string.IsNullOrWhiteSpace(window))
            return Results.Json(new { error = "no_window_log", message = $"This Hub was started without {HubConsole.LogEnvVar}; use ?source=db." }, statusCode: 404);
        return Results.Json(new { source = "window", path = window, lines = LogTail.ReadLastLines(window, n) }, OperationJson.Options);
    }

    /// <summary>The newest <paramref name="lines"/> rows, oldest first, as <c>[2026-10-30 9:41 PM ET] WRN Category: message</c>.</summary>
    internal static IReadOnlyList<string> ReadDatabase(string path, int lines)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TimestampUtc, Level, Category, Message, Exception FROM MindAttic_Log ORDER BY Id DESC LIMIT $n;";
        cmd.Parameters.AddWithValue("$n", lines);
        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            var when = DateTime.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc)
                ? EasternClock.Format(utc) : reader.GetString(0);
            var level = reader.GetInt32(1) is var l && l >= 0 && l < Levels.Length ? Levels[l] : l.ToString(CultureInfo.InvariantCulture);
            var category = reader.IsDBNull(2) ? "" : reader.GetString(2) + ": ";
            var line = $"[{when}] {level} {category}{reader.GetString(3)}";
            if (!reader.IsDBNull(4)) line += " | " + reader.GetString(4).ReplaceLineEndings(" ");
            result.Add(line);
        }
        result.Reverse();
        return result;
    }
}
