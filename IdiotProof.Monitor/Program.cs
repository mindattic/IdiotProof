using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
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

// ── IdiotProof.Monitor ───────────────────────────────────────────────────────
//
// Barebones .NET console host that runs 24/7 and evaluates every IsActive=true
// strategy stored in the IdiotProof SQL database. Speaks the same DslStrategy
// adapter the Blazor app uses — anything authored in the Describe / Script /
// Guided tabs is automatically picked up here.
//
// Responsibilities:
//   • Load all active strategies from SQL on a fixed cadence (default 30s).
//   • Pull recent candles per ticker from a shared market-data feed.
//   • For each strategy: build an IndicatorSnapshot, run the DslStrategy
//     adapter, and log condition-by-condition pass/fail progress.
//   • Stamp LastFiredUtc + FireCount when a TradeSignal lands.
//
// On every full-pass the candidate signal walks two gates before fire:
//   1. Legion high-tier voter panel from legion.json — claude / openai /
//      gemini / deepseek vote, claude judges. Reject = no fire.
//   2. RiskGuardian validates the implied trade (stop placement, max loss
//      per trade / day, account risk %, R:R sanity). Block = no fire.
// Both gates write AuditLog entries with reasons. Actual broker order
// placement still lives in the Blazor host; future work pushes approved
// signals back via SignalR for execution.
//
// Run:   dotnet run --project IdiotProof.Monitor
// Stop:  Ctrl+C (graceful shutdown via IHostApplicationLifetime)

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddHostedService<MonitorWorker>();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

var host = builder.Build();
await host.RunAsync();
