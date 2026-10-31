using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Core.Hosting;
using IdiotProof.Engine.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ── IdiotProof.ResearchScanner ──────────────────────────────────────────────
//
// One scan pass, then exit. Fired on a recurring cadence by a MindAttic.Scheduler
// (WatchDog) job (M.SCHD-T8), or on demand by `idiotproof research scan` — NOT a
// daemon, and NOT part of IdiotProof.Monitor's real-time trading loop. It
// sweeps EDGAR/Alpaca/Federal-Register for market-moving events across the
// tracked ticker universe, scores their significance, and writes everything
// to the shared SQL database; IdiotProof.Blazor's /research page just reads
// what this process already computed.
//
// Env knobs:
//   IDIOTPROOF_RESEARCHSCAN_BATCHSIZE   tickers to sweep per pass, beyond the
//                                       watchlist (default 300)
//   IDIOTPROOF_RESEARCHSCAN_DAYSBACK    lookback window per source per pass
//                                       (default 2 — passes are frequent, this
//                                       is just overlap/safety margin)
//   IDIOTPROOF_RESEARCHSCAN_REGULATORY_HOURS  min hours between regulatory-
//                                       scan cadence (default 24 — rule
//                                       filings are infrequent)
//
// Run once by hand:  dotnet run --project IdiotProof.ResearchScanner
var builder = Host.CreateApplicationBuilder(args);

// The shared IdiotProof graph (HOUSE-LAW-6): the same SQL database, Vault buckets, AppSettings
// overlay chain, Legion client and research services as the web app and the Monitor.
builder.Configuration.AddIdiotProofVault();
var storage = new WebStorageProvider();
storage.EnsureDirectories();
builder.Services.AddIdiotProofCore(builder.Configuration, storage, new CoreHostOptions { PinDevKeyRing = true });

using var host = builder.Build();

var logDir = Path.Combine(storage.LogsPath, "research-scan");
Directory.CreateDirectory(logDir);
var logFile = Path.Combine(logDir, $"{DateTime.UtcNow:yyyyMMdd}.log");

// The pass itself (and saving its ScanRun row, even on failure) lives in IdiotProof.Core so the
// Hub's `research scan` operation runs the very same pass on demand.
ScanRun scanRun;
await using (var scope = host.Services.CreateAsyncScope())
    scanRun = await scope.ServiceProvider.GetRequiredService<IResearchScanPass>()
        .RunAsync(new ScanPassOptions(), CancellationToken.None);

var summary = $"[{DateTime.UtcNow:u}] scan run {scanRun.Id}: " +
              $"{scanRun.TickersScanned}/{scanRun.UniverseSize} tickers, " +
              $"{scanRun.ClaimsFound} claims, {scanRun.ErrorCount} errors, " +
              $"{(scanRun.CompletedUtc!.Value - scanRun.StartedUtc).TotalSeconds:0.0}s";
File.AppendAllText(logFile, summary + Environment.NewLine);
Console.WriteLine(summary);
