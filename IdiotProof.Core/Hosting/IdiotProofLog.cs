using System.Data.Common;
using MindAttic.Log;
using MindAttic.Log.Extensions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace IdiotProof.Core.Hosting;

/// <summary>
/// MindAttic.Log 3.0.0 for every IdiotProof entry point other than the Hub (IP-T16/IP-T23; the
/// Hub wires its own in HubLogs, IP-T78). Each entry point writes its own standalone
/// <c>MindAttic.Log.db</c> at the path Vault assigns its application id,
/// <c>%APPDATA%\MindAttic\Logging\&lt;application&gt;\MindAttic.Log.db</c> (the
/// <c>VaultPaths.LoggingDbPath</c> layout), tagged with a <c>Component</c> property; tests and
/// operators point it elsewhere with <see cref="DbPathEnvVar"/>.
/// <para>
/// <see cref="Start"/> records the application and hooks the unhandled-exception events
/// (AppDomain, unobserved tasks); the database is opened lazily on the first entry, so a CLI run
/// that logs nothing never touches it. A log that can't be opened is an aid lost, never a reason
/// the process fails: every write is a no-op then.
/// </para>
/// </summary>
public static class IdiotProofLog
{
    /// <summary>Overrides the database path for every entry point in this process (tests use a temp file).</summary>
    public const string DbPathEnvVar = "IDIOTPROOF_LOG_DB";

    /// <summary>Application id of the web app.</summary>
    public const string Blazor = "IdiotProof.Blazor";

    /// <summary>Application id of the Monitor exe (its operator subcommands; the Monitor itself runs in the Hub).</summary>
    public const string Monitor = "IdiotProof.Monitor";

    /// <summary>Application id of the one-pass research scanner.</summary>
    public const string ResearchScanner = "IdiotProof.ResearchScanner";

    /// <summary>Application id of the <c>idiotproof</c> CLI (errors only).</summary>
    public const string Cli = "IdiotProof.Cli";

    /// <summary>Application id of the stdio MCP server (never stdout: that is the MCP wire).</summary>
    public const string Mcp = "IdiotProof.Mcp";

    private static readonly object gate = new();
    private static string? application;
    private static string? explicitPath;
    private static Logger? logger;
    private static bool opened;
    private static bool handlersHooked;

    /// <summary>The application id passed to <see cref="Start"/>, or null before it.</summary>
    public static string? Application => application;

    /// <summary>The database this process writes, or null before <see cref="Start"/>.</summary>
    public static string? CurrentPath => application is null ? null : ResolvePath(application, explicitPath);

    /// <summary>Vault's per-application log database: <c>%APPDATA%\MindAttic\Logging\&lt;application&gt;\MindAttic.Log.db</c>.</summary>
    public static string DatabasePath(string application, string? roamingAppData = null) =>
        Path.Combine(roamingAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MindAttic", "Logging", application, "MindAttic.Log.db");

    /// <summary>
    /// Call once at the entry point. <paramref name="dbPath"/> (else <see cref="DbPathEnvVar"/>, else
    /// <see cref="DatabasePath"/>) is where entries go. Calling it again switches to the new target.
    /// </summary>
    public static void Start(string application, string? dbPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        lock (gate)
        {
            CloseLocked();
            IdiotProofLog.application = application;
            explicitPath = dbPath;
            if (handlersHooked) return;
            handlersHooked = true;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write(LogEventLevel.Fatal, e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "Unhandled",
                "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
            if (e.IsTerminating) Stop();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Write(LogEventLevel.Error, e.Exception, "UnobservedTask", "Unobserved task exception");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    /// <summary>The Serilog logger behind the database, opening it if needed; null when not started or unavailable.</summary>
    public static Serilog.ILogger? Logger
    {
        get
        {
            lock (gate) return OpenLocked();
        }
    }

    /// <summary>Writes one entry under <paramref name="category"/> (the Category column). Never throws.</summary>
    public static void Write(LogEventLevel level, Exception? exception, string category, string template, params object?[] values)
    {
        try
        {
            Logger?.ForContext(Constants.SourceContextPropertyName, category).Write(level, exception, template, values);
        }
        catch (Exception)
        {
            // Logging is never a reason the caller fails.
        }
    }

    /// <summary>Writes an Error entry under <paramref name="category"/>.</summary>
    public static void Error(string category, Exception? exception, string template, params object?[] values) =>
        Write(LogEventLevel.Error, exception, category, template, values);

    /// <summary>Writes an Information entry under <paramref name="category"/>.</summary>
    public static void Information(string category, string template, params object?[] values) =>
        Write(LogEventLevel.Information, null, category, template, values);

    /// <summary>
    /// Routes this host's <c>ILogger</c> output into the database: <paramref name="minimum"/> and up
    /// for app categories, warnings and up for Microsoft/System (so EF's SQL chatter stays out).
    /// Requires <see cref="Start"/>; a no-op when the database can't be opened.
    /// </summary>
    public static ILoggingBuilder AddIdiotProofLog(this ILoggingBuilder logging, LogLevel minimum = LogLevel.Information)
    {
        if (Logger is not { } serilog) return logging;
        logging.AddSerilog(serilog, dispose: false);
        logging.AddFilter<SerilogLoggerProvider>(null, minimum);
        logging.AddFilter<SerilogLoggerProvider>("Microsoft", minimum > LogLevel.Warning ? minimum : LogLevel.Warning);
        logging.AddFilter<SerilogLoggerProvider>("System", minimum > LogLevel.Warning ? minimum : LogLevel.Warning);
        return logging;
    }

    /// <summary>Flushes and closes the database (called on process exit; safe to call more than once).</summary>
    public static void Stop()
    {
        lock (gate) CloseLocked();
    }

    /// <summary>Tests: flush, close and forget the application, so stray events in the test host log nowhere.</summary>
    internal static void ResetForTests()
    {
        lock (gate)
        {
            CloseLocked();
            application = null;
            explicitPath = null;
        }
    }

    private static Logger? OpenLocked()
    {
        if (logger is not null || opened || application is null) return logger;
        opened = true;
        var path = ResolvePath(application, explicitPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.WithProperty("Component", Component(application))
                .WriteToMindAtticLog(new MindAtticLogOptions { Application = application, DbPath = path, MinimumLevel = MindAttic.Log.Models.LogSeverity.Verbose })
                .CreateLogger();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or DbException)
        {
            // stderr only: stdout is the MCP wire and the CLI's output.
            try { Console.Error.WriteLine($"[{application}] MindAttic.Log unavailable ({ex.Message}); not logging to {path}."); }
            catch (IOException) { }
        }
        return logger;
    }

    private static void CloseLocked()
    {
        var current = logger;
        logger = null;
        opened = false;
        current?.Dispose();
    }

    private static string ResolvePath(string application, string? dbPath) =>
        !string.IsNullOrWhiteSpace(dbPath) ? dbPath
        : Environment.GetEnvironmentVariable(DbPathEnvVar) is { Length: > 0 } fromEnv ? fromEnv
        : DatabasePath(application);

    private static string Component(string application) =>
        application.StartsWith("IdiotProof.", StringComparison.Ordinal) ? application["IdiotProof.".Length..] : application;
}
