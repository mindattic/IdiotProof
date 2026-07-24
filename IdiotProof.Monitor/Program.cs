using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.AspNetCore.DataProtection;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MindAttic.Legion;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.DependencyInjection;
using MindAttic.Authentication.Web;

// ── IdiotProof.Monitor ───────────────────────────────────────────────────────
//
// The unified always-on evaluator + executor (RFC 0002 / IP-A8). Runs 24/7,
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
//   IDIOTPROOF_MONITOR_INTERVAL  tick cadence (default 5s)
//   IDIOTPROOF_FEED              alpaca | mock   (default: alpaca when keyed)
//   IDIOTPROOF_BROKER            alpaca | sandbox (default sandbox — IP-LAW-3)
//   IDIOTPROOF_ALPACA_FEED       sip | iex data tier (default iex; sip auto-downgrades)
//   IDIOTPROOF_STREAMING         0 disables the websocket stream
//
// Run:   dotnet run --project IdiotProof.Monitor
// Stop:  Ctrl+C (graceful shutdown via IHostApplicationLifetime)

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

// SQL Server — same connection-string priority chain as the Blazor host:
// env var → IConfiguration → LocalDB fallback. Kept identical so the Monitor
// always reads the same database the user is editing.
var connStr =
    Environment.GetEnvironmentVariable("ConnectionStrings__IdiotProof")
    ?? builder.Configuration["ConnectionStrings:IdiotProof"]
    ?? @"Server=(localdb)\MSSQLLocalDB;Database=IdiotProof;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(connStr));

// AppSettings — same overlay chain the Blazor host uses (disk → env vars →
// MindAttic LLM keyring → MindAttic broker keyring → IConfiguration via Vault)
// so the Monitor reads the same configuration without re-implementing the
// load path.
builder.Configuration
    .AddMindAtticVaultFiles();
builder.Services.AddMindAtticVault(builder.Configuration);

// Surface the Security vault bucket (pepper.v1, bootstrap-token, …) into config
// for the auth stack used by the create-account CLI path. AddMindAtticVaultFiles
// surfaces the broker/LLM buckets trading needs; the console also needs Security.
// The file carries a UTF-8 BOM — File.ReadAllText strips it.
var securityBucket = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "MindAttic", "Security", "providers.json");
if (File.Exists(securityBucket))
{
    // Guarded: a malformed/locked Security file must NOT crash the trading
    // process on boot. Auth (create-account) would then fail loudly when used,
    // but the Monitor's evaluation loop — which needs none of this — keeps running.
    try
    {
        using var secDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(securityBucket));
        var secMem = new Dictionary<string, string?>();
        foreach (var p in secDoc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                secMem[$"MindAttic:Vault:Security:{p.Name}"] = p.Value.GetString();
        builder.Configuration.AddInMemoryCollection(secMem);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[startup] Security bucket unreadable ({ex.GetType().Name}: {ex.Message}) — create-account will be unavailable; trading loop unaffected.");
    }
}

var storage = new WebStorageProvider();
storage.EnsureDirectories();
var settings = AppSettings.Load(storage);
settings.OverlayFromEnvironment();
settings.OverlayFromMindAtticCredentials();
settings.OverlayFromBrokerCredentials();
settings.OverlayFromConfiguration(builder.Configuration);
builder.Services.AddSingleton<IStorageProvider>(storage);
builder.Services.AddSingleton(settings);

// MindAttic.Legion — the universal LLM gateway. AddLegionClient registers the
// HttpClient + the LegionClient itself; LlmVotingService uses it to fan out
// prompts across the high-tier voter panel declared in legion.json.
builder.Services.AddLegionClient();
builder.Services.AddSingleton<IdiotProof.Blazor.Services.LlmVotingService>();

// Risk Guardian — final gate after the LLM panel. Per-user instances are
// cached by RiskGuardianService and seeded from UserPreferences risk fields
// (set via the Settings page). Cache preserves the in-memory daily-loss
// tracker across signals so the daily circuit breaker actually trips.
builder.Services.AddSingleton<RiskGuardianService>();

builder.Services.AddSingleton<StrategyRepository>();
builder.Services.AddSingleton<ConditionProgressRepository>();
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<TradeDiaryRepository>();

// Connection string surfaced to the worker for the single-instance leader
// lease (sp_getapplock — see MonitorLeaderLease).
builder.Services.AddSingleton(new MonitorDatabase(connStr));

// Data Protection — SAME app name + key ring as the Blazor host so this
// process can decrypt the per-user API keys the UI wrote (UserApiKeys rows).
// Monitor stays on-box while Blazor moves to Azure (per user decision), so
// this MUST follow whichever ring Blazor is actually using — same
// AzureBlobUri/KeyVaultKeyUri vs. KeyRingPath choice as
// IdiotProof.Blazor/Program.cs, or the two processes decrypt with different
// keys and every UserApiKeys row becomes unreadable to one of them.
// Do NOT also call builder.Services.AddDataProtection() here: ASP.NET Core
// only keeps ONE effective ApplicationDiscriminator/XmlRepository — whichever
// Configure<DataProtectionOptions> action runs last wins, silently, based on
// registration order. AddMindAtticAuthentication below registers its own
// DataProtection builder (app name "MindAttic.Auth:{AppName}"); a second,
// differently-named registration here previously only "worked" because it
// happened to run first and get overridden — reordering either block would
// have silently flipped the discriminator and made every UserApiKeys row
// undecryptable to this process. opts.ConfigureDataProtection below is the
// single source of truth for both the app name AND the key-ring choice.
var dpBlobUri  = builder.Configuration["DataProtection:AzureBlobUri"];
var dpKvKeyUri = builder.Configuration["DataProtection:KeyVaultKeyUri"];
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MindAttic", "DataProtection", "IdiotProof");

builder.Services.AddSingleton<UserKeyService>();
builder.Services.AddSingleton<UserBrokerResolver>();
builder.Services.AddSingleton<EmailDomainBlocklistService>();

// Auth stack (same as the Blazor host) so the operator CLI can create an
// account through the REAL Argon2id+pepper path (create-account). Service
// registration only — no web middleware runs in the console.
builder.Services.AddMindAtticAuthentication<AppDbContext>(
    builder.Configuration,
    opts =>
    {
        opts.AppName = "IdiotProof";
        opts.IsProduction = false;
        opts.ConfigureDataProtection = dp =>
        {
            if (!string.IsNullOrWhiteSpace(dpBlobUri) && !string.IsNullOrWhiteSpace(dpKvKeyUri))
            {
                var dpCredential = new Azure.Identity.DefaultAzureCredential();
                dp.PersistKeysToAzureBlobStorage(new Uri(dpBlobUri), dpCredential)
                  .ProtectKeysWithAzureKeyVault(new Uri(dpKvKeyUri), dpCredential);
            }
            else
            {
                dp.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
            }
        };
    });

// Market data — Alpaca whenever keys resolved through the settings chain
// (env → Vault broker keyring → IConfiguration), Mock otherwise. Force with
// IDIOTPROOF_FEED=mock|alpaca.
builder.Services.AddSingleton<IdiotProof.DataFeeds.IMarketDataFeed>(_ =>
{
    var choice = Environment.GetEnvironmentVariable("IDIOTPROOF_FEED")?.ToLowerInvariant();
    var hasKeys = !string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId)
               && !string.IsNullOrWhiteSpace(settings.AlpacaApiSecretKey);
    if (choice == "mock" || (!hasKeys && choice != "alpaca"))
        return new IdiotProof.DataFeeds.MockDataFeed();
    // Real-time SIP by default (Algo Trader Plus, IP-A29); IDIOTPROOF_ALPACA_FEED=iex to override.
    var tier = Environment.GetEnvironmentVariable("IDIOTPROOF_ALPACA_FEED") ?? "sip";
    return new IdiotProof.DataFeeds.AlpacaDataFeed(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, tier);
});

// Broker — Sandbox always registered and active by default (IP-LAW-3).
// Alpaca joins the router when keys exist; IDIOTPROOF_BROKER=alpaca opts in
// to routing real orders (paper vs live follows AlpacaIsPaper from settings).
builder.Services.AddSingleton(_ =>
{
    var router = new IdiotProof.Brokers.BrokerRouter();
    router.Register(new IdiotProof.Brokers.SandboxBrokerClient());
    var hasKeys = !string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId)
               && !string.IsNullOrWhiteSpace(settings.AlpacaApiSecretKey);
    if (hasKeys)
        router.Register(new IdiotProof.Brokers.AlpacaBrokerClient(
            settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, settings.AlpacaIsPaper));
    router.SetActive(Environment.GetEnvironmentVariable("IDIOTPROOF_BROKER")); // null/garbage → stays Sandbox
    return router;
});

// On-demand gapper generator — resolved by the `auto-gapper` operator CLI only
// (no scheduled trigger; standardized auto-generation is future work, Epic S).
builder.Services.AddSingleton<AutoGapperScanner>();
builder.Services.AddSingleton<IdiotProof.Blazor.Services.LiveBarRepository>();

// Premarket blow-off/fade detector — triggered automatically by MonitorWorker's
// tick loop (Mon-Fri, 9:00-10:00 AM ET, re-scanned every 5 min), detection-only
// (no strategy created, no order placed).
builder.Services.AddSingleton<EmailSmsAlertSender>();
builder.Services.AddSingleton<PremarketFadeScanner>();

builder.Services.AddHostedService<MonitorWorker>();

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
if (args.Length > 0 && MonitorCli.IsCommand(args[0]))
    return await MonitorCli.RunAsync(host.Services, args);

await host.RunAsync();
return 0;
