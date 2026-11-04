using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Evidence;
using IdiotProof.Operations;
using IdiotProof.Operations.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// IP-T432 research harness: every evidence tool once on real data, per the protocol fixed in
// docs/research/evidence-sweep-2026-11.md. Research databases only, read-only market data, no orders.
//   dotnet run --project tools/EvidenceSweep -- main     (IdiotProof_Research_T375: bars + copied replay trades)
//   dotnet run --project tools/EvidenceSweep -- events   (IdiotProof_Research_T407: copied research claims)
//   dotnet run --project tools/EvidenceSweep -- main topology   (only steps whose name starts with "topology"; one tool per process keeps memory bounded)

var mode = args.FirstOrDefault() ?? "main";
var only = args.Length > 1 ? args[1] : null;
var researchDb = mode == "events" ? "IdiotProof_Research_T407" : "IdiotProof_Research_T375";
var outDir = Environment.GetEnvironmentVariable("EVIDENCE_OUT")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "research", "evidence-sweep-2026-11"));
Directory.CreateDirectory(outDir);

Environment.SetEnvironmentVariable("ConnectionStrings__IdiotProof",
    $@"Server=(localdb)\MSSQLLocalDB;Database={researchDb};Trusted_Connection=True;TrustServerCertificate=True;Command Timeout=300;");
Environment.SetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar, "alpaca");

var configuration = new ConfigurationBuilder().AddIdiotProofVault().Build();
var resolved = CoreServiceRegistration.ResolveConnectionString(configuration);
if (!resolved.Contains(researchDb, StringComparison.Ordinal))
    throw new InvalidOperationException("Refusing to run: the connection string is not a research database.");

var storage = new WebStorageProvider();
storage.EnsureDirectories();
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IConfiguration>(configuration);
services.AddIdiotProofCore(configuration, storage, new CoreHostOptions { PinDevKeyRing = true });
services.AddIdiotProofTrading();
using var root = services.BuildServiceProvider();

var migrateOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(resolved)
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
    .Options;
await using (var db = new AppDbContext(migrateOptions))
{
    if (!db.Database.GetConnectionString()!.Contains(researchDb, StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing to migrate a non-research database.");
    await db.Database.MigrateAsync();
}
if (string.IsNullOrWhiteSpace(root.GetRequiredService<AppSettings>().AlpacaApiKeyId))
    throw new InvalidOperationException("No Alpaca market-data keys resolved.");

var ctx = new OperationContext { Services = root, Width = 200 };
var summary = new List<object>();

async Task Run(string name, Func<Task<OperationResult>> op)
{
    if (only is not null && !name.StartsWith(only, StringComparison.Ordinal)) return;
    var started = DateTime.UtcNow;
    Console.WriteLine($"[{started:HH:mm:ss}] {name} ...");
    try
    {
        var r = await op();
        var text = $"status: {r.Status}\nmessage: {r.Message}\n\n{r.Human}";
        await File.WriteAllTextAsync(Path.Combine(outDir, name + ".txt"), text);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(outDir, name + ".json"),
                JsonSerializer.Serialize(r.Data, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        }
        catch (Exception ex) { Console.WriteLine($"  (json not written: {ex.Message})"); }
        summary.Add(new { name, status = r.Status.ToString(), seconds = (DateTime.UtcNow - started).TotalSeconds });
        Console.WriteLine($"  {r.Status} in {(DateTime.UtcNow - started).TotalSeconds:F0}s");
    }
    catch (Exception ex)
    {
        await File.WriteAllTextAsync(Path.Combine(outDir, name + ".crash.txt"), ex.ToString());
        summary.Add(new { name, status = "CRASH: " + ex.GetType().Name + ": " + ex.Message, seconds = (DateTime.UtcNow - started).TotalSeconds });
        Console.WriteLine($"  CRASH {ex.GetType().Name}: {ex.Message}");
    }
}

if (mode == "events")
{
    await Run("events", () => new EvidenceEventsOperation().ExecuteAsync(new EvidenceEventsArgs
    {
        From = new DateOnly(2024, 2, 1), To = new DateOnly(2026, 7, 26), Windows = "-1:1,0:1,0:5",
    }, ctx));
}
else
{
    // Sample rule S20, fixed in the protocol before any tool ran: the point-in-time under-$5
    // universe on 2026-08-03 from daily bars dated before that day, ranked by 20-session ADV.
    var asOf = new DateOnly(2026, 8, 3);
    List<(string Symbol, decimal Close, decimal Adv)> universe;
    await using (var db = await root.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
    {
        var rows = await db.HistoricalBars.AsNoTracking()
            .Where(b => b.Timeframe == "1Day" && string.Compare(b.DateEt, "2026-06-01") >= 0 && string.Compare(b.DateEt, "2026-08-03") < 0)
            .Select(b => new { b.Symbol, b.DateEt, b.Close, b.Volume })
            .ToListAsync();
        universe = rows.GroupBy(r => r.Symbol)
            .Select(g =>
            {
                var w = g.OrderBy(r => r.DateEt).TakeLast(20).ToList();
                return (Symbol: g.Key, Close: w[^1].Close, Adv: w.Average(r => r.Close * r.Volume), Sessions: w.Count, Last: w[^1].DateEt);
            })
            .Where(m => m.Sessions >= 5 && m.Last == "2026-07-31" && m.Close >= 0.10m && m.Close < 5m && m.Adv >= 500_000m)
            .OrderByDescending(m => m.Adv).ThenBy(m => m.Symbol, StringComparer.Ordinal)
            .Select(m => (m.Symbol, m.Close, m.Adv))
            .ToList();
    }
    var s20 = universe.Take(20).Select(m => m.Symbol).ToArray();
    var s10 = s20.Take(10).ToArray();
    var s5 = s20.Take(5).ToArray();
    await File.WriteAllTextAsync(Path.Combine(outDir, "sample.json"), JsonSerializer.Serialize(new
    {
        asOf, universeSize = universe.Count,
        s20 = universe.Take(20).Select(m => new { m.Symbol, close = m.Close, adv = Math.Round(m.Adv) }),
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"universe {universe.Count}; S20 = {string.Join(' ', s20)}");

    var from = new DateOnly(2026, 8, 3);
    var to = new DateOnly(2026, 10, 30);
    var octFrom = new DateOnly(2026, 10, 1);

    await Run("survival", () => new EvidenceSurvivalOperation().ExecuteAsync(new EvidenceSurvivalArgs(), ctx));
    await Run("tails", () => new EvidenceTailsOperation().ExecuteAsync(new EvidenceTailsArgs
    {
        Symbols = s20.Append("SPY").Append("IWM").ToArray(), Timeframe = EvidenceTimeframe.Day,
        From = new DateOnly(2025, 11, 3), To = to, Q = 0.1, Train = 0.67, Permutations = 999, Alpha = 0.05,
    }, ctx));
    await Run("regime-bocpd", () => new EvidenceRegimeOperation().ExecuteAsync(new EvidenceRegimeArgs
    {
        Symbols = s20, From = new DateOnly(2026, 10, 26), To = to, Method = RegimeMethod.Bocpd, Threshold = 0.5,
    }, ctx));
    await Run("regime-hmm", () => new EvidenceRegimeOperation().ExecuteAsync(new EvidenceRegimeArgs
    {
        Symbols = s20, From = new DateOnly(2026, 10, 26), To = to, Method = RegimeMethod.Hmm, States = 2,
    }, ctx));
    await Run("topology", () => new EvidenceTopologyOperation().ExecuteAsync(new EvidenceTopologyArgs
    {
        Symbols = s20, From = from, To = to, Window = 40, Dimension = 3, Delay = 1, Horizon = 5, Buckets = 4, Holdout = 0.3,
    }, ctx));
    await Run("pools", () => new EvidencePoolsOperation().ExecuteAsync(new EvidencePoolsArgs
    {
        Symbols = s20, From = from, To = to, Horizon = 10, Proximity = 0.25, MinSamples = 20,
    }, ctx));
    await Run("conformal-analog", () => new EvidenceConformalOperation().ExecuteAsync(new EvidenceConformalArgs
    {
        Symbols = s20, From = from, To = to, Model = ConformalModel.Analog, Alpha = 0.1, Gamma = 0.005, Horizon = 15, Window = 30, Entry = EntryTiming.NextOpen,
    }, ctx));
    await Run("conformal-kalman", () => new EvidenceConformalOperation().ExecuteAsync(new EvidenceConformalArgs
    {
        Symbols = s20, From = from, To = to, Model = ConformalModel.Kalman, Alpha = 0.1, Gamma = 0.005, Horizon = 15, Window = 30, Entry = EntryTiming.NextOpen,
    }, ctx));
    await Run("search", () => new EvidenceSearchOperation().ExecuteAsync(new EvidenceSearchArgs
    {
        Symbols = s10, Script = "Ticker(X).OnVwapReclaim().StopLossPercent({sl}).TrailingStopLoss({trail})",
        Grid = ["sl=2,4", "trail=1,2,3"], From = from, To = to, Holdout = 0.3, Seed = 1337,
    }, ctx));
    foreach (var s in s5)
    {
        await Run($"flow-{s}", () => new EvidenceFlowOperation().ExecuteAsync(new EvidenceFlowArgs
        {
            Symbol = s, From = octFrom, To = to, Horizon = 5, Bins = 5, Signing = FlowSigning.Tick, Train = 0.67,
        }, ctx));
        await Run($"hawkes-{s}", () => new EvidenceHawkesOperation().ExecuteAsync(new EvidenceHawkesArgs
        {
            Symbol = s, From = octFrom, To = to, Horizon = 5, Bins = 5, Train = 0.67, Window = 10, Spike = 3, Branching = 0.7,
        }, ctx));
    }
}

await File.WriteAllTextAsync(Path.Combine(outDir, only is null ? $"summary-{mode}.json" : $"summary-{mode}-{only}.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("done");
