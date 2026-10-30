using System.Net;
using IdiotProof.Blazor.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Identity;
using IdiotProof.Engine.Storage;
using IdiotProof.Hub;
using IdiotProof.Operations;
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
// Env knobs:
//   IDIOTPROOF_HUB_PORT     loopback port (default 5910)
//   IDIOTPROOF_HUB_MONITOR  0 = serve operations without running the trading loop
//   IDIOTPROOF_OWNER        default owner GUID when a call names none
//   IDIOTPROOF_HUB_KEY      shared key override (default: Vault Tokens bucket "idiotproof-hub")
//   plus the Monitor's IDIOTPROOF_FEED / _BROKER / _ALPACA_FEED / _MONITOR_INTERVAL / _STREAMING
//
// Run:  dotnet run --project IdiotProof.Hub       Stop: Ctrl+C (graceful)

Console.WriteLine();
Console.WriteLine(IdiotProof.Shared.Branding.AsciiBanner);
Console.WriteLine();

var builder = WebApplication.CreateBuilder(args);

// Windows Service hosting: the same binary runs as a service (installed to run as the USER —
// LocalDB and the Data Protection ring are per-user); interactively it no-ops.
builder.Services.AddWindowsService(o => o.ServiceName = "IdiotProof.Hub");

// Loopback only: other machines can never reach the Hub; the shared key covers local processes.
var port = HubWire.ResolvePort();
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

// ── Operations: every CLI command and MCP tool, reflected from [Operation] types ──
builder.Services.AddIdiotProofOperations(typeof(Program).Assembly);
// Scoped services that ask "who is this for?" (the owner's Claude key, …) see the operation's owner.
builder.Services.AddScoped<IOwnerContext, OperationOwnerContext>();
builder.Services.AddScoped<IClaudeKeyContext, OwnerClaudeKeyContext>();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = null; });
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("IdiotProof", LogLevel.Warning);

var app = builder.Build();

// Schema first — same as every other host — so operations never meet a stale database.
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

app.MapHubEndpoints();

Console.WriteLine($"  Hub listening on http://127.0.0.1:{port}  ·  {app.Services.GetRequiredService<OperationCatalog>().Operations.Count} operations"
    + (settings.MonitorHosted ? "  ·  Monitor hosted" : "  ·  Monitor NOT hosted (IDIOTPROOF_HUB_MONITOR=0)"));

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
