using IdiotProof.Core.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Engine.Storage;
using IdiotProof.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using IdiotProof.Core.Monitoring;
// ── IdiotProof.Monitor ───────────────────────────────────────────────────────
//
// SHIM (IP-T81): the Monitor now runs inside the IdiotProof Hub (IdiotProof.Hub hosts
// MonitorWorker with its leader lease and scanners). Started with no subcommand, this exe starts
// the Hub in this console and exits with its exit code, so start.bat, shortcuts and habits keep
// working. The operator subcommands below still run here and exit.
//
// What the Hub-hosted Monitor does (unchanged): the unified always-on evaluator + executor
// (IP-LAW-10). Runs 24/7,
// re-reads every IsActive=true SQL strategy each tick (UI edits apply live),
// pulls real market data (Alpaca REST + websocket streaming when keyed; Mock
// fallback), walks the three gates (conditions → LLM voter panel →
// RiskGuardian), places entry orders through BrokerRouter (Sandbox is the
// always-safe default, IP-LAW-3; premarket = limit + extended_hours), then
// manages open positions to their exit (sell-by / stops / take-profit /
// peak-giveback momentum rollover) and feeds realized P&L back into the
// RiskGuardian daily circuit breaker.
//
// Env knobs:
//   IDIOTPROOF_MONITOR_INTERVAL  tick cadence (default 1s)
//   IDIOTPROOF_FEED              alpaca | mock   (default: alpaca when keyed)
//   IDIOTPROOF_BROKER            alpaca | sandbox (default sandbox — IP-LAW-3)
//   IDIOTPROOF_ALPACA_FEED       sip | iex data tier (default sip; iex for the free tier)
//   IDIOTPROOF_STREAMING         0 disables the websocket stream
//
// Run:   dotnet run --project IdiotProof.Monitor     (= starts the Hub)
// Stop:  Ctrl+C (the Hub shuts down gracefully: drain, stop the loop, release the lease)

// Crash dump — write unhandled exceptions to crash.log next to the exe so the
// event survives even if the DB / logger is what caused the crash.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        var crashLog = Path.Combine(AppContext.BaseDirectory, "crash.log");
        File.AppendAllText(crashLog,
            $"[{DateTime.UtcNow:u}] CRASH (isTerminating={e.IsTerminating})\n{e.ExceptionObject}\n---\n");
    }
    catch { /* best-effort; if the file write fails there is nothing left to do */ }
};

// No subcommand: be the shim. Nothing below (host, migrations) is needed to start the Hub.
if (args.Length == 0 || !MonitorCli.IsCommand(args[0]))
    return await HubShim.RunAsync(args);

// Subcommands that now have an operation run it in the Hub (IP-T138); --local keeps the old path.
if (MonitorForwarding.Translate(args, out var forwardUsage) is { } forward)
    return await MonitorForwarding.RunAsync(forward, Console.Out, Console.Error);
if (forwardUsage is not null)
{
    Console.Error.WriteLine(forwardUsage);
    return 2;
}

// Brand wordmark on startup (shared source of truth — IdiotProof.Shared.Branding).
Console.WriteLine();
Console.WriteLine(IdiotProof.Shared.Branding.AsciiBanner);
try
{
    var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
    var buildUtc = System.IO.File.GetLastWriteTimeUtc(loc);
    var buildEt = TimeZoneInfo.ConvertTimeFromUtc(buildUtc, IdiotProof.Scripting.MarketTime.Eastern);
    Console.WriteLine($"  Build: {buildEt:yyyy-MM-dd h:mm tt} ET");
}
catch { /* best-effort */ }
Console.WriteLine();

var builder = Host.CreateApplicationBuilder(args);

// Windows Service hosting: `sc.exe create IdiotProof.Monitor binPath=...` runs
// this same binary as a service; interactively it no-ops. Lifetime events
// (stop/shutdown) map to the host's graceful cancellation.
builder.Services.AddWindowsService(o => o.ServiceName = "IdiotProof.Monitor");

// The shared IdiotProof graph (HOUSE-LAW-6) — the same SQL database (env
// ConnectionStrings__IdiotProof → configuration → LocalDB), Vault buckets (incl. Security for
// the create-account auth path), AppSettings overlay chain, Legion voter panel and services the
// web app registers. The Data Protection ring is the web app's ring (Blob + Key Vault, else
// DataProtection:KeyRingPath, else the pinned %APPDATA% dev ring) so this process can decrypt the
// per-user API keys the UI wrote. Monitor stays on-box while the web app may move to Azure, so
// it MUST follow the same ring choice — see CoreServiceRegistration.SharedKeyRing.
builder.Configuration.AddIdiotProofVault();
var storage = new WebStorageProvider();
storage.EnsureDirectories();
builder.Services.AddIdiotProofCore(builder.Configuration, storage, new CoreHostOptions { PinDevKeyRing = true });

// Market data + the one BrokerRouter (Sandbox always registered and the default, IP-LAW-3;
// Alpaca joins when keyed) + per-strategy routing.
// Knobs: IDIOTPROOF_FEED=mock|alpaca, IDIOTPROOF_BROKER=alpaca|sandbox, IDIOTPROOF_ALPACA_FEED=sip|iex.
builder.Services.AddIdiotProofTrading();

// The tick loop, its scanners and the SQL leader lease (one trading Monitor per database).
builder.Services.AddIdiotProofMonitor();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = null;
});
// Suppress EF Core SQL chatter and all framework noise. Only warnings,
// errors, and critical events reach the console. Console.WriteLine calls
// (fill blocks, self-ping) bypass the logger and are always visible.
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("IdiotProof", LogLevel.Warning);

var host = builder.Build();

// Apply pending EF migrations before touching the DB — same as the Blazor host.
// Without this, a build whose model is ahead of the database (new columns/tables)
// silently breaks every UserApiKeys read — including the BYO-key money path
// (status / test-order / replay). Idempotent; safe for both CLI and worker.
using (var migScope = host.Services.CreateScope())
{
    var dbf = migScope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>();
    using var migDb = dbf.CreateDbContext();
    migDb.Database.Migrate();
}

// Operator CLI subcommands (status / set-keys / create-strategies) run against
// the built DI and EXIT — they never start the trading worker (RunAsync). This
// is the "visualize/configure from the CLI" surface.
return await MonitorCli.RunAsync(host.Services, args);
