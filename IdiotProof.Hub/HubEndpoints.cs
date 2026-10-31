using System.Diagnostics;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Ledger;
using IdiotProof.Operations;
using IdiotProof.Operations.Hosting;
using IdiotProof.Operations.Streaming;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Hub;

/// <summary>Hub-wide settings resolved once at startup.</summary>
public sealed record HubSettings(string Key, bool MonitorHosted, Guid? DefaultOwner, DateTimeOffset StartedUtc)
{
    /// <summary><c>IDIOTPROOF_HUB_MONITOR=0</c> runs the Hub without the trading loop (dev, tests, a second box).</summary>
    public const string MonitorEnvVar = "IDIOTPROOF_HUB_MONITOR";

    /// <summary>Owner used when a call names none (<c>IDIOTPROOF_OWNER</c> or config <c>Hub:DefaultOwner</c>).</summary>
    public const string OwnerEnvVar = "IDIOTPROOF_OWNER";

    public static HubSettings Resolve(IConfiguration configuration) => new(
        HubKey.LoadOrCreate(),
        Environment.GetEnvironmentVariable(MonitorEnvVar) != "0",
        Guid.TryParse(Environment.GetEnvironmentVariable(OwnerEnvVar) ?? configuration["Hub:DefaultOwner"], out var owner) ? owner : null,
        DateTimeOffset.UtcNow);

    public static string Version =>
        typeof(HubSettings).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}

/// <summary>The Hub's HTTP surface (<see cref="HubWire"/>): health, catalog and the one operation endpoint.</summary>
public static class HubEndpoints
{
    public static void MapHubEndpoints(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<HubSettings>();

        // Unauthenticated reads: liveness and the public shape of the API, nothing about any user.
        app.MapGet(HubWire.HealthPath, (OperationCatalog catalog, IdiotProofDatabase db) => Results.Json(new HubHealth
        {
            Status = "ok",
            Version = HubSettings.Version,
            StartedEt = TimeZoneInfo.ConvertTime(settings.StartedUtc, IdiotProof.Scripting.MarketTime.Eastern),
            MonitorHosted = settings.MonitorHosted,
            Database = DescribeDatabase(db.ConnectionString),
            Operations = catalog.Operations.Count,
            ProcessId = Environment.ProcessId,
        }, OperationJson.Options));

        app.MapGet("/api/version", () => Results.Text(HubSettings.Version));

        app.MapGet(HubWire.CatalogPath, (OperationCatalog catalog) =>
            Results.Content(catalog.ToJson(), "application/json"));

        // `idiotproof hub stop`: the window belongs to the terminal host, so a close request can't
        // reach the Hub; this asks it to shut down the same way Ctrl+C in its window would.
        app.MapPost(HubWire.ShutdownPath, (IHostApplicationLifetime lifetime) =>
            {
                HubConsole.Line($"*** Shutdown requested by `idiotproof hub stop` (PID {Environment.ProcessId}): draining operations, then stopping the Monitor ***");
                lifetime.StopApplication();
                return Results.Accepted();
            })
            .AddEndpointFilter(async (ctx, next) =>
                HubKey.Matches(settings.Key, ctx.HttpContext.Request.Headers[HubWire.KeyHeader].FirstOrDefault())
                    ? await next(ctx)
                    : Results.Json(new { error = "hub_key_required", message = $"Missing or wrong {HubWire.KeyHeader}." }, statusCode: 401));

        // Tail of the Hub's log (IP-T78): ?source=window (default) is the window mirror
        // (IDIOTPROOF_HUB_LOG), ?source=db the MindAttic.Log database. Key-guarded: commands and
        // Monitor warnings are in there.
        app.MapGet(HubLogs.LogsPath, (int? lines, string? source) => HubLogs.Tail(lines, source))
            .AddEndpointFilter(async (ctx, next) =>
                HubKey.Matches(settings.Key, ctx.HttpContext.Request.Headers[HubWire.KeyHeader].FirstOrDefault())
                    ? await next(ctx)
                    : Results.Json(new { error = "hub_key_required", message = $"Missing or wrong {HubWire.KeyHeader}." }, statusCode: 401));

        // Streaming twin of /api/op (IP-T324): same body, answered as server-sent events.
        app.MapPost(HubWire.StreamPath, RunStreamAsync)
            .AddEndpointFilter(async (ctx, next) =>
            {
                var inFlight = ctx.HttpContext.RequestServices.GetRequiredService<InFlightOperations>();
                if (!inFlight.TryEnter())
                    return Results.Json(new { error = "shutting_down", message = "The Hub is shutting down; retry once it is back." }, statusCode: 503);
                try { return await next(ctx); }
                finally { inFlight.Exit(); }
            })
            .AddEndpointFilter(async (ctx, next) =>
                HubKey.Matches(settings.Key, ctx.HttpContext.Request.Headers[HubWire.KeyHeader].FirstOrDefault())
                    ? await next(ctx)
                    : Results.Json(new { error = "hub_key_required", message = $"Missing or wrong {HubWire.KeyHeader}." }, statusCode: 401));

        app.MapPost(HubWire.OperationPath, RunOperationAsync)
            .AddEndpointFilter(async (ctx, next) =>
            {
                // In-flight tracking for graceful shutdown (IP-T79): once draining, refuse new calls.
                var inFlight = ctx.HttpContext.RequestServices.GetRequiredService<InFlightOperations>();
                if (!inFlight.TryEnter())
                    return Results.Json(new { error = "shutting_down", message = "The Hub is shutting down; retry once it is back." }, statusCode: 503);
                try { return await next(ctx); }
                finally { inFlight.Exit(); }
            })
            .AddEndpointFilter(async (ctx, next) =>
                HubKey.Matches(settings.Key, ctx.HttpContext.Request.Headers[HubWire.KeyHeader].FirstOrDefault())
                    ? await next(ctx)
                    : Results.Json(new { error = "hub_key_required", message = $"Missing or wrong {HubWire.KeyHeader}." }, statusCode: 401));
    }

    private static async Task<IResult> RunOperationAsync(
        HttpRequest http,
        OperationDispatcher dispatcher,
        IDbContextFactory<AppDbContext> dbFactory,
        CommandLedgerRepository ledger,
        HubSettings settings,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var (request, error) = await ReadRequestAsync(http, ct);
        if (error is not null) return error;
        var (name, result) = await ExecuteAsync(request!, null, dispatcher, dbFactory, ledger, settings, loggers, ct);
        return Respond(result, name, request!.Output);
    }

    /// <summary>
    /// <c>POST /api/stream</c> (IP-T324): runs the operation with an <see cref="SseStreamSink"/> so a
    /// streaming operation's lines reach the caller as they happen, then sends the usual response
    /// as the final <c>result</c> event. A closed connection (Ctrl+C) cancels the operation.
    /// </summary>
    private static async Task<IResult> RunStreamAsync(
        HttpContext http,
        OperationDispatcher dispatcher,
        IDbContextFactory<AppDbContext> dbFactory,
        CommandLedgerRepository ledger,
        HubSettings settings,
        ILoggerFactory loggers)
    {
        var ct = http.RequestAborted;
        var (request, error) = await ReadRequestAsync(http.Request, ct);
        if (error is not null) return error;

        http.Response.StatusCode = 200;
        http.Response.ContentType = "text/event-stream; charset=utf-8";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await http.Response.Body.FlushAsync(ct);

        var sink = new SseStreamSink(http.Response.Body);
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var beating = Task.Run(async () =>
        {
            try { while (await heartbeat.WaitForNextTickAsync(ct)) await sink.WriteHeartbeatAsync(ct); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
        }, CancellationToken.None);

        var (name, result) = await ExecuteAsync(request!, sink, dispatcher, dbFactory, ledger, settings, loggers, ct);
        heartbeat.Dispose();
        await beating;
        if (!ct.IsCancellationRequested)
        {
            try
            {
                await sink.WriteEventAsync(HubWire.StreamResultEvent,
                    JsonSerializer.Serialize(BuildResponse(result, name, request!.Output), OperationJson.Options), ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException) { /* the caller left */ }
        }
        return Results.Empty;
    }

    private static async Task<(HubOperationRequest? Request, IResult? Error)> ReadRequestAsync(HttpRequest http, CancellationToken ct)
    {
        // The wire contract is OperationJson's (snake_case, kebab-case enums), shared with every client.
        HubOperationRequest? request;
        try { request = await JsonSerializer.DeserializeAsync<HubOperationRequest>(http.Body, OperationJson.Options, ct); }
        catch (JsonException ex) { return (null, Results.Json(new { error = "bad_request", message = ex.Message }, statusCode: 400)); }
        if (request is null) return (null, Results.Json(new { error = "bad_request", message = "Empty request." }, statusCode: 400));
        if (request.Argv is not { Count: > 0 } && string.IsNullOrWhiteSpace(request.Operation))
            return (null, Results.Json(new { error = "usage", message = "Send argv or operation." }, statusCode: 400));
        return (request, null);
    }

    /// <summary>Resolve, log, run and ledger one call; <paramref name="sink"/> is set only for a streaming call.</summary>
    private static async Task<(string Name, OperationResult Result)> ExecuteAsync(
        HubOperationRequest request,
        IStreamSink? sink,
        OperationDispatcher dispatcher,
        IDbContextFactory<AppDbContext> dbFactory,
        CommandLedgerRepository ledger,
        HubSettings settings,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var started = Stopwatch.StartNew();

        // Which operation is this? The CLI sends argv (name first), the MCP server a name + JSON.
        string name;
        IReadOnlyList<string>? rest = null;
        if (request.Argv is { Count: > 0 } argv)
        {
            if (!dispatcher.Catalog.TryResolve(argv, out var descriptor, out var tail))
                return (string.Join(' ', argv.Take(2)), await dispatcher.DispatchArgvAsync(argv, Invocation(request, null, null, sink), ct));
            name = descriptor!.Name;
            rest = tail;
        }
        else
        {
            name = dispatcher.Catalog.Find(request.Operation!)?.Name ?? request.Operation!;
        }

        var caller = request.Caller.ToString().ToLowerInvariant();
        HubConsole.CommandIn(caller, name, rest is not null
            ? string.Join(' ', SecretRedactor.RedactArgv(rest))
            : SecretRedactor.RedactJson(request.Args?.GetRawText()) is { } json && json != "{}" ? json : "");

        // Owner: the caller's explicit choice, else the Hub's default, else the lone user.
        var owner = await OwnerResolver.ResolveAsync(dbFactory, request.OwnerId ?? settings.DefaultOwner, ct);
        var info = Invocation(request, owner.OwnerId, owner.Error, sink);

        var result = rest is not null
            ? await dispatcher.DispatchAsync(name, rest, info, ct)
            : await dispatcher.DispatchAsync(name, request.Args ?? EmptyObject, info, ct);

        await ledger.AppendAsync(new CommandLedgerEntry
        {
            OwnerUserId = owner.OwnerId,
            Operation = name.Length > 64 ? name[..64] : name,
            Caller = caller,
            ArgsJson = rest is not null
                ? JsonSerializer.Serialize(SecretRedactor.RedactArgv(rest))
                : request.Args?.GetRawText(),
            Status = result.Status.ToString(),
            ExitCode = result.ExitCode,
            Message = result.Message,
            DurationMs = (int)started.ElapsedMilliseconds,
        }, CancellationToken.None);

        HubConsole.CommandOut(caller, name, result.Status.ToString().ToLowerInvariant(), result.ExitCode,
            started.ElapsedMilliseconds, result.IsOk ? null : result.Message);
        loggers.CreateLogger(HubLogs.CommandsCategory).LogInformation(
            "{Caller} {Operation} -> {Status} ({ExitCode}) in {DurationMs}ms{Detail}",
            caller, name, result.Status, result.ExitCode, started.ElapsedMilliseconds, result.IsOk ? "" : ": " + result.Message);
        return (name, result);
    }

    private static OperationInvocation Invocation(HubOperationRequest request, Guid? ownerId, string? ownerError, IStreamSink? sink = null) => new()
    {
        Sink = sink,
        Caller = request.Caller,
        Output = request.Output,
        OwnerId = ownerId,
        OwnerError = ownerError,
        ConfirmationCode = request.ConfirmationCode,
        Color = request.Color,
        Width = request.Width,
    };

    private static IResult Respond(OperationResult result, string name, OutputMode output) =>
        Results.Json(BuildResponse(result, name, output), OperationJson.Options);

    private static HubOperationResponse BuildResponse(OperationResult result, string name, OutputMode output) => new()
    {
        Operation = name,
        Status = result.Status.ToString(),
        ExitCode = result.ExitCode,
        Text = result.Render(output),
        Envelope = JsonSerializer.SerializeToElement(result.ToEnvelope(), OperationJson.Options),
    };

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>"(localdb)\MSSQLLocalDB / IdiotProof" — server and database, never credentials.</summary>
    internal static string DescribeDatabase(string connectionString)
    {
        try
        {
            var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return $"{b.DataSource} / {b.InitialCatalog}";
        }
        catch (ArgumentException)
        {
            return "(unparseable connection string)";
        }
    }
}
