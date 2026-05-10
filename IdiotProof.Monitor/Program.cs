using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MindAttic.Legion;

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
// LLM voting (the Legion high-tier voter panel from legion.json) now runs
// on every full-pass — signals get cross-verified by Claude/OpenAI/Gemini/
// DeepSeek before LastFiredUtc is stamped. Risk Guardian + actual broker
// order placement still live in the Blazor host; future work pushes the
// approved signal back via SignalR for execution.
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
// MindAttic LLM keyring → MindAttic broker keyring) so the Monitor reads the
// same configuration without re-implementing the load path.
var storage = new WebStorageProvider();
storage.EnsureDirectories();
var settings = AppSettings.Load(storage);
settings.OverlayFromEnvironment();
settings.OverlayFromMindAtticCredentials();
settings.OverlayFromBrokerCredentials();
builder.Services.AddSingleton<IStorageProvider>(storage);
builder.Services.AddSingleton(settings);

// MindAttic.Legion — the universal LLM gateway. AddLegionClient registers the
// HttpClient + the LegionClient itself; LlmVotingService uses it to fan out
// prompts across the high-tier voter panel declared in legion.json.
builder.Services.AddLegionClient();
builder.Services.AddSingleton<IdiotProof.Blazor.Services.LlmVotingService>();

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
