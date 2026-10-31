using System.Net;
using IdiotProof.Core.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Identity;
using IdiotProof.Engine.Storage;
using IdiotProof.Hub;
using IdiotProof.Operations;
using IdiotProof.Operations.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.EntityFrameworkCore;

// ── IdiotProof.Hub ───────────────────────────────────────────────────────────
//
// The one long-running IdiotProof process. It hosts the Monitor — the only evaluator and order
// placer (IP-LAW-10), still guarded by its SQL leader lease — and serves every operation to the
// front doors over loopback HTTP (docs/agent/IDIOTPROOF_PROTOCOL.md):
//
//   idiotproof <noun> <verb> --flags   (IdiotProof.Cli)  ─┐
//   mcp__idiotproof__<noun>_<verb>     (IdiotProof.Mcp)  ─┼─► POST /api/op ─► OperationDispatcher
//   web pages                          (IdiotProof.Blazor)┘
//
// The Monitor brings its scanners with it (IP-T75): PremarketFadeScanner (alert-only, 9-10 AM ET)
// and BeBexDecayScanner (arms/disarms the BE strategy, 4:30-5 PM ET) run inside the Monitor's tick,
// so they run exactly when the Monitor does — IDIOTPROOF_HUB_MONITOR=0 turns all three off, and the
// pause flag (`idiotproof monitor pause`) stops them with the tick. AutoGapperScanner has no
// schedule (its AutoGapperEnabled auto-arm was removed); it stays on demand, tuned by
// IDIOTPROOF_AUTOGAPPER_MINGAP / _MAX / _BROKER (paper by default).
//
// Env knobs:
//   IDIOTPROOF_HUB_PORT     loopback port (default 5910)
//   IDIOTPROOF_HUB_MONITOR  0 = serve operations without running the trading loop
//   IDIOTPROOF_OWNER        default owner GUID when a call names none
//   IDIOTPROOF_HUB_KEY      shared key override (default: Vault Tokens bucket "idiotproof-hub")
//   plus the Monitor's IDIOTPROOF_FEED / _BROKER / _ALPACA_FEED / _MONITOR_INTERVAL / _STREAMING
//
// Run:  dotnet run --project IdiotProof.Hub       Stop: Ctrl+C or `idiotproof hub stop` (graceful:
//       drains running operations, stops the Monitor loop, releases the leader lease — IP-T79)

// Windows Service hosting (IP-T76): the same binary runs as a service, installed by
// `idiotproof hub install-service` / tools\install-hub-service.ps1 to run as the USER (LocalDB and
// the Data Protection ring are per-user). A service starts in System32 with no visible window, so
// it roots itself at its exe folder and mirrors its would-be window to Logs\hub-service.log.
var isService = WindowsServiceHelpers.IsWindowsService();
if (HubHosting.WindowLogPath(isService, Environment.GetEnvironmentVariable(HubConsole.LogEnvVar),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) is { } windowLog)
    Environment.SetEnvironmentVariable(HubConsole.LogEnvVar, windowLog);

// One Hub per port (IP-T77): say who already has it and exit before touching the database.
var port = HubWire.ResolvePort();
if (HubInstanceGuard.IsPortInUse(port))
{
    var running = await HubInstanceGuard.ProbeAsync(port, TimeSpan.FromSeconds(3));
    Console.Error.WriteLine(HubInstanceGuard.PortTakenMessage(port, running));
    return HubInstanceGuard.PortTakenExitCode;
}

// The visible, money-green window (IP-T331): paint, title and log tee before anything prints.
HubConsole.Start();

Console.WriteLine();
Console.WriteLine(IdiotProof.Shared.Branding.AsciiBanner);
Console.WriteLine();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = HubHosting.ContentRoot(isService, AppContext.BaseDirectory),
});
builder.Services.AddWindowsService(o => o.ServiceName = HubHosting.ServiceName);

// Loopback only: other machines can never reach the Hub; the shared key covers local processes.
builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port));

// ── The shared IdiotProof graph (HOUSE-LAW-6) — same as the web app and the Monitor ──
builder.Configuration.AddIdiotProofVault();
var storage = new WebStorageProvider();
storage.EnsureDirectories();
builder.Services.AddIdiotProofCore(builder.Configuration, storage, new CoreHostOptions { PinDevKeyRing = true });
builder.Services.AddIdiotProofTrading();

var settings = HubSettings.Resolve(builder.Configuration);
builder.Services.AddSingleton(settings);
if (settings.MonitorHosted)
    builder.Services.AddIdiotProofMonitor();

// Graceful shutdown (IP-T79). Hosted services stop in reverse registration order, so the drain
// (registered after the Monitor) runs first: new /api/op calls get 503, running ones finish; then
// the Monitor's loop ends its current tick and releases the SQL leader lease explicitly.
builder.Services.AddSingleton<InFlightOperations>();
builder.Services.AddHostedService(sp => new OperationDrainService(sp.GetRequiredService<InFlightOperations>(), HubConsole.Line));
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(60));

// ── Operations: every CLI command and MCP tool, reflected from [Operation] types ──
builder.Services.AddIdiotProofOperations(typeof(Program).Assembly);
builder.Services.AddIdiotProofStreaming();
// Scoped services that ask "who is this for?" (the owner's Claude key, …) see the operation's owner.
builder.Services.AddScoped<IOwnerContext, OperationOwnerContext>();
builder.Services.AddScoped<IClaudeKeyContext, OwnerClaudeKeyContext>();

builder.Logging.ClearProviders();
// No ANSI colors: they'd paint over the green window and litter the log file.
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = null; o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled; });
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("IdiotProof", LogLevel.Warning);
// …and durably to the Hub's own MindAttic.Log database, with one entry per command (IP-T78).
builder.Logging.AddHubLogDatabase();

var app = builder.Build();

// Schema first — same as every other host — so operations never meet a stale database.
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

app.MapHubEndpoints();

HubConsole.Banner(port, app.Services.GetRequiredService<OperationCatalog>().Operations.Count, settings.MonitorHosted);

try
{
    await app.RunAsync();
}
catch (IOException ex) when (ex.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse }
                             || ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Port {port} is already in use — another Hub is probably running (`idiotproof hub status`). Set {HubWire.PortEnvVar} to use a different port.");
    return 2;
}
return 0;
