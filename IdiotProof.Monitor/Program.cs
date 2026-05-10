using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
// Risk Guardian, LLM voting, and broker order placement live in the Blazor host
// — the Monitor only surfaces signals + per-condition progress. A future
// iteration can wire it to push signals back through SignalR to the running web
// app.
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

builder.Services.AddSingleton<StrategyRepository>();
builder.Services.AddHostedService<MonitorWorker>();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

var host = builder.Build();
await host.RunAsync();
