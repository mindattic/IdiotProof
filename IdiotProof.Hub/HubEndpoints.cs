using System.Diagnostics;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Ledger;
using IdiotProof.Operations;
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
                HubConsole.Line($"*** Shutdown requested by `idiotproof hub stop` (PID {Environment.ProcessId}) ***");
                lifetime.StopApplication();
                return Results.Accepted();
            })
            .AddEndpointFilter(async (ctx, next) =>
                HubKey.Matches(settings.Key, ctx.HttpContext.Request.Headers[HubWire.KeyHeader].FirstOrDefault())
                    ? await next(ctx)
                    : Results.Json(new { error = "hub_key_required", message = $"Missing or wrong {HubWire.KeyHeader}." }, statusCode: 401));

        app.MapPost(HubWire.OperationPath, RunOperationAsync)
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
        CancellationToken ct)
    {
        var started = Stopwatch.StartNew();

        // The wire contract is OperationJson's (snake_case, kebab-case enums), shared with every client.
        HubOperationRequest? request;
        try { request = await JsonSerializer.DeserializeAsync<HubOperationRequest>(http.Body, OperationJson.Options, ct); }
        catch (JsonException ex) { return Results.Json(new { error = "bad_request", message = ex.Message }, statusCode: 400); }
        if (request is null) return Results.Json(new { error = "bad_request", message = "Empty request." }, statusCode: 400);

        // Which operation is this? The CLI sends argv (name first), the MCP server a name + JSON.
        string name;
        IReadOnlyList<string>? rest = null;
        if (request.Argv is { Count: > 0 } argv)
        {
            if (!dispatcher.Catalog.TryResolve(argv, out var descriptor, out var tail))
                return Respond(await dispatcher.DispatchArgvAsync(argv, Invocation(request, null, null), ct), string.Join(' ', argv.Take(2)), request.Output);
            name = descriptor!.Name;
            rest = tail;
        }
        else if (!string.IsNullOrWhiteSpace(request.Operation))
        {
            name = dispatcher.Catalog.Find(request.Operation)?.Name ?? request.Operation;
        }
        else
        {
            return Results.Json(new { error = "usage", message = "Send argv or operation." }, statusCode: 400);
        }

        var caller = request.Caller.ToString().ToLowerInvariant();
        HubConsole.CommandIn(caller, name, rest is not null
            ? string.Join(' ', SecretRedactor.RedactArgv(rest))
            : SecretRedactor.RedactJson(request.Args?.GetRawText()) is { } json && json != "{}" ? json : "");

        // Owner: the caller's explicit choice, else the Hub's default, else the lone user.
        var owner = await OwnerResolver.ResolveAsync(dbFactory, request.OwnerId ?? settings.DefaultOwner, ct);
        var info = Invocation(request, owner.OwnerId, owner.Error);

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
        return Respond(result, name, request.Output);
    }

    private static OperationInvocation Invocation(HubOperationRequest request, Guid? ownerId, string? ownerError) => new()
    {
        Caller = request.Caller,
        Output = request.Output,
        OwnerId = ownerId,
        OwnerError = ownerError,
        ConfirmationCode = request.ConfirmationCode,
        Color = request.Color,
        Width = request.Width,
    };

    private static IResult Respond(OperationResult result, string name, OutputMode output) =>
        Results.Json(new HubOperationResponse
        {
            Operation = name,
            Status = result.Status.ToString(),
            ExitCode = result.ExitCode,
            Text = result.Render(output),
            Envelope = JsonSerializer.SerializeToElement(result.ToEnvelope(), OperationJson.Options),
        }, OperationJson.Options);

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
