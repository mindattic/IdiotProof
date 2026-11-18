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
//   mcp__idiotproof__<noun>_<verb>     (IdiotProof.Mcp)  ─┴─► POST /api/op ─► OperationDispatcher
//
// There is no web UI (IP-T504 removed IdiotProof.Blazor and the Monitor shim); the Hub is the only
// process that talks to Alpaca.
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
//   IDIOTPROOF_HUB_TRAY     0 = no tray icon, the window stays as it is; shown = tray icon, window starts visible
//                           (default: tray icon, window hidden until asked for, IP-T574)
//   IDIOTPROOF_HUB_KEY      shared key override (default: Vault Tokens bucket "idiotproof-hub")
//   plus the Monitor's IDIOTPROOF_FEED / _BROKER / _ALPACA_FEED / _MONITOR_INTERVAL / _STREAMING
//
// Run:  dotnet run --project IdiotProof.Hub       Stop: Ctrl+C or `idiotproof hub stop` (graceful:
//       drains running operations, stops the Monitor loop, releases the leader lease — IP-T79)

// Windows Service hosting (IP-T76): the same binary runs as a service, installed by
// `idiotproof hub install-service` / tools\install-hub-service.ps1 to run as the USER (LocalDB and
// the Data Protection ring are per-user). A service starts in System32 with no visible window, so
// it roots itself at its exe folder and mirrors its would-be window to Logs\hub-service.log.
// Operation output promises culture-invariant numbers, but several renderers format with the current
// culture: on a de-DE host they printed "0,5" and "1.234". Pin the Hub (every operation runs here).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

// A thread-pool floor (IP-T520): long research operations (scan, radar, study stages) can hold pool threads,
// and with the default floor the pool grows slowly enough that even /api/health took ~9 s, so the CLI
// reported the Hub "not running" and scheduled jobs missed it.
ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
ThreadPool.SetMinThreads(Math.Max(minWorkers, 64), Math.Max(minIo, 64));

// LAMP (IP-T347): `--write-settings-manifest` imports today's app-settings.json into MindAttic Vault, writes the
// manifest Vault.Desktop reads, and exits (run.ps1 calls it after a deploy; moved here from the web app, IP-T504).
if (args.Contains(MindAttic.Vault.Settings.Manifest.SettingsManifestCommand.Switch, StringComparer.OrdinalIgnoreCase))
{
    IdiotProof.Engine.Settings.AppSettings.Load(new WebStorageProvider());
    Console.WriteLine(IdiotProof.Engine.Settings.IdiotProofSettings.StoreFactory().Directory);
    return 0;
}

var isService = WindowsServiceHelpers.IsWindowsService();

// The tray icon (IP-T574) needs a console window it can hide; Windows Terminal's can't be, so there the Hub
// starts itself again in a classic one and this copy ends.
if (!isService && HubTray.RelaunchedInOwnWindow(HubConsole.TrayEnvVar, args)) return 0;
if (HubHosting.WindowLogPath(isService, Environment.GetEnvironmentVariable(HubConsole.LogEnvVar),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) is { } windowLog)
    Environment.SetEnvironmentVariable(HubConsole.LogEnvVar, windowLog);

// The visible, money-green window (IP-T331): paint, title and log tee before anything prints,
// including the port-taken message below, which `idiotproof hub start` reads from the log.
HubConsole.Start();
// Off the taskbar from here on: the tray icon brings the window back on a click (IP-T574).
using var tray = isService ? null : HubTray.Start("IdiotProof Hub", HubConsole.TrayEnvVar);

// One Hub per port (IP-T77): say who already has it and exit before touching the database.
var port = HubWire.ResolvePort();
if (HubInstanceGuard.IsPortInUse(port))
{
    var running = await HubInstanceGuard.ProbeAsync(port, TimeSpan.FromSeconds(3));
    Console.Error.WriteLine(HubInstanceGuard.PortTakenMessage(port, running));
    return HubInstanceGuard.PortTakenExitCode;
}

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
// Scheduled research and arming steps are MindAttic.Scheduler jobs (HOUSE-LAW-11, IP-T518): each one is an
// `idiotproof ...` CLI call, so it still runs here, through the dispatcher, the gateway and the ledger.
// The gapper study's research database graph (IP-T515): `research gappers ...` operations run there.
builder.Services.AddSingleton<ResearchHost>();
builder.Services.AddSingleton<IOperationScopeRouter>(sp => sp.GetRequiredService<ResearchHost>());
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

if (tray is not null)
{
    var started = DateTime.UtcNow;
    tray.Info = () =>
    [
        $"http://127.0.0.1:{port}  -  PID {Environment.ProcessId}",
        settings.MonitorHosted ? "Monitor hosted: the trading loop is live" : "Monitor not hosted",
        $"Up {HubConsole.Span(DateTime.UtcNow - started)}  -  {HubConsole.CommandCount} commands",
        HubConsole.LastCommand is { } last ? $"Last: {last}" : "No command yet",
    ];
    tray.Actions =
    [
        new HubTrayAction("Stop the Hub", app.Lifetime.StopApplication,
            settings.MonitorHosted ? "Stop the IdiotProof Hub? The trading loop stops with it." : "Stop the IdiotProof Hub?"),
    ];
}

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
