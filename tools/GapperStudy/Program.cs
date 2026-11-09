using System.Globalization;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Operations;
using IdiotProof.Operations.Research.Gappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// IP-T442 gapper-study harness: one stage per process, on IdiotProof_Research_GAP only, every number
// from docs/research/gapper-study-2026-11/params.json. Each stage writes runs/<stage>-<yyyyMMdd-HHmm>.txt/.json.
//   dotnet run --project tools/GapperStudy -- build-universe [--from D --to D] [--smoke --symbols A,B,C] [--max-calls N]
//   dotnet run --project tools/GapperStudy -- pull <bars|news|cik|edgar|splits|all> [--smoke] [--max-calls N] [--from D --to D]
//   dotnet run --project tools/GapperStudy -- pull <edgar-dilution|edgar-dilution-text> [--max-calls N] (exploration only, never in `all`; files under D:\IdiotProofData\edgar-dilution, IP-T458)
//   dotnet run --project tools/GapperStudy -- catalysts [--llm-budget N]
//   dotnet run --project tools/GapperStudy -- snapshot <train|validation|holdout> [--smoke] [--from D --to D]
//   dotnet run --project tools/GapperStudy -- label <train|validation|holdout> [--smoke] [--from D --to D]
//   dotnet run --project tools/GapperStudy -- calibrate-spread [--split train --events 500] [--force]
//   dotnet run --project tools/GapperStudy -- test <H1 H2 ...|all> --split <train|validation|holdout> [--smoke]
//   dotnet run --project tools/GapperStudy -- report [--skip-export]
//   dotnet run --project tools/GapperStudy -- explore <build|visibility|models|exits|news|fade|entries|corobs|ssr|float-import|tradeflow-pull|tradeflow|regsho-pull|alarms|exit-routes|fade-short> [--times "08:00,08:30"] [--force] [--from D --to D] (train only, IP-T453)
//   dotnet run --project tools/GapperStudy -- status
//   --user <guid> names the owner whose Alpaca keys are used when the host has none; --db <name> overrides the research DB name.

var stage = args.FirstOrDefault()?.ToLowerInvariant() ?? "status";
var positional = args.Skip(1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
var flags = ParseFlags(args.Skip(1 + positional.Count));

var paths = GapperStudyPaths.Resolve();
var parameters = GapperStudyParams.Load(paths);
var researchDb = flags.GetValueOrDefault("db") ?? parameters.DataTiers.ResearchDb;
if (!researchDb.Contains(parameters.DataTiers.ResearchDbGuardPattern, StringComparison.Ordinal))
    throw new InvalidOperationException($"Refusing to run: '{researchDb}' is not a research database name.");
Directory.CreateDirectory(paths.RunsDir);
Directory.CreateDirectory(paths.DataDir);

Environment.SetEnvironmentVariable("ConnectionStrings__IdiotProof",
    $@"Server=(localdb)\MSSQLLocalDB;Database={researchDb};Trusted_Connection=True;TrustServerCertificate=True;Command Timeout=600;");
Environment.SetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar, "alpaca");
Environment.SetEnvironmentVariable(TradingServiceRegistration.AlpacaFeedEnvVar, parameters.DataTiers.AlpacaFeed);

var configuration = new ConfigurationBuilder().AddIdiotProofVault().Build();
var resolved = CoreServiceRegistration.ResolveConnectionString(configuration);
if (!resolved.Contains(researchDb, StringComparison.Ordinal) || !resolved.Contains(parameters.DataTiers.ResearchDbGuardPattern, StringComparison.Ordinal))
    throw new InvalidOperationException("Refusing to run: the connection string is not a research database.");

var storage = new WebStorageProvider();
storage.EnsureDirectories();
var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }).SetMinimumLevel(LogLevel.Information)
    .AddFilter("Microsoft", LogLevel.Warning).AddFilter("System", LogLevel.Warning));
services.AddSingleton<IConfiguration>(configuration);
services.AddIdiotProofCore(configuration, storage, new CoreHostOptions { PinDevKeyRing = true });
services.AddIdiotProofTrading();
await using var root = services.BuildServiceProvider();

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

Guid? owner = Guid.TryParse(flags.GetValueOrDefault("user"), out var explicitOwner) ? explicitOwner : null;
var settings = root.GetRequiredService<AppSettings>();
if (string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId) && owner is null && stage is "build-universe" or "pull" or "calibrate-spread")
{
    var resolvedOwner = await OwnerResolver.ResolveAsync(root.GetRequiredService<IDbContextFactory<AppDbContext>>(), null);
    owner = resolvedOwner.OwnerId;
    if (owner is null) throw new InvalidOperationException("No Alpaca keys in the host settings and no owner resolved; pass --user <guid>.");
}

using var scope = root.CreateScope();
var ctx = new OperationContext { Services = scope.ServiceProvider, Width = 200, OwnerId = owner, Caller = OperationCaller.Cli };
var started = DateTime.UtcNow;
var stamp = started.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
var name = $"{stage}{(positional.Count > 0 ? "-" + string.Join('-', positional) : "")}{(flags.ContainsKey("smoke") ? "-smoke" : "")}";
Console.WriteLine($"[{GapperStudyServices.Et(started)}] {name} on {researchDb} (study {paths.StudyDir})");

OperationResult result;
try
{
    result = stage switch
    {
        "build-universe" => await new GapperBuildUniverseOperation().ExecuteAsync(new GapperBuildUniverseArgs
        {
            From = Date(flags, "from") ?? parameters.Split("train").From,
            To = Date(flags, "to") ?? parameters.Split("holdout").To,
            Smoke = flags.ContainsKey("smoke"), Symbols = flags.GetValueOrDefault("symbols"), MaxCalls = Int(flags, "max-calls"),
        }, ctx),
        "pull" => await new GapperPullOperation().ExecuteAsync(new GapperPullArgs
        {
            Tier = positional.FirstOrDefault() ?? flags.GetValueOrDefault("tier") ?? "all", Smoke = flags.ContainsKey("smoke"),
            From = Date(flags, "from"), To = Date(flags, "to"), MaxCalls = Int(flags, "max-calls"),
        }, ctx),
        "catalysts" => await new GapperCatalystsOperation().ExecuteAsync(new GapperCatalystsArgs { LlmBudget = Int(flags, "llm-budget") }, ctx),
        "snapshot" => await new GapperSnapshotOperation().ExecuteAsync(new GapperSnapshotArgs
        {
            Split = positional.FirstOrDefault() ?? flags.GetValueOrDefault("split") ?? "train", Smoke = flags.ContainsKey("smoke"),
            From = Date(flags, "from"), To = Date(flags, "to"),
        }, ctx),
        "label" => await new GapperLabelOperation().ExecuteAsync(new GapperLabelArgs
        {
            Split = positional.FirstOrDefault() ?? flags.GetValueOrDefault("split") ?? "train", Smoke = flags.ContainsKey("smoke"),
            From = Date(flags, "from"), To = Date(flags, "to"),
        }, ctx),
        "calibrate-spread" => await new GapperCalibrateSpreadOperation().ExecuteAsync(new GapperCalibrateSpreadArgs
        {
            Split = flags.GetValueOrDefault("split"), Events = flags.ContainsKey("events") ? Int(flags, "events") : null, Force = flags.ContainsKey("force"),
        }, ctx),
        "test" => await new GapperTestOperation().ExecuteAsync(new GapperTestArgs
        {
            Hypothesis = positional.Count > 0 ? positional.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToArray() : (flags.GetValueOrDefault("hypothesis") ?? "all").Split(','),
            Split = flags.GetValueOrDefault("split") ?? "train", Smoke = flags.ContainsKey("smoke"),
        }, ctx),
        "report" => await new GapperReportOperation().ExecuteAsync(new GapperReportArgs { SkipExport = flags.ContainsKey("skip-export") }, ctx),
        "explore" => await new GapperExploreOperation().ExecuteAsync(new GapperExploreArgs
        {
            Command = positional.FirstOrDefault() ?? flags.GetValueOrDefault("command") ?? "", Split = flags.GetValueOrDefault("split") ?? "train",
            Times = flags.GetValueOrDefault("times"), Force = flags.ContainsKey("force"), From = Date(flags, "from"), To = Date(flags, "to"),
            Parallelism = flags.ContainsKey("parallelism") ? Int(flags, "parallelism") : 4,
        }, ctx),
        "status" => await new GapperStatusOperation().ExecuteAsync(new GapperStatusArgs(), ctx),
        _ => throw new ArgumentException($"Unknown stage '{stage}'. Use build-universe, pull, catalysts, snapshot, label, calibrate-spread, test, report, explore or status."),
    };
}
catch (Exception ex)
{
    await File.WriteAllTextAsync(Path.Combine(paths.RunsDir, $"{name}-{stamp}.crash.txt"), ex.ToString());
    Console.WriteLine($"CRASH {ex.GetType().Name}: {ex.Message}");
    return 1;
}

var seconds = (DateTime.UtcNow - started).TotalSeconds;
var text = $"stage: {name}\nstarted: {GapperStudyServices.Et(started)}\nseconds: {seconds:F0}\nstatus: {result.Status}\nmessage: {result.Message}\n\n{result.Human}";
await File.WriteAllTextAsync(Path.Combine(paths.RunsDir, $"{name}-{stamp}.txt"), text);
try
{
    await File.WriteAllTextAsync(Path.Combine(paths.RunsDir, $"{name}-{stamp}.json"),
        JsonSerializer.Serialize(new { stage = name, startedUtc = started, seconds, status = result.Status.ToString(), result.Message, data = result.Data },
            new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
}
catch (Exception ex) { Console.WriteLine($"  (json not written: {ex.Message})"); }
Console.WriteLine(result.Render(OutputMode.Human));
Console.WriteLine($"[{GapperStudyServices.Et(DateTime.UtcNow)}] {result.Status} in {seconds:F0}s → {paths.RunsDir}\\{name}-{stamp}.txt");
return result.ExitCode;

static Dictionary<string, string?> ParseFlags(IEnumerable<string> tokens)
{
    var flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var list = tokens.ToList();
    for (int i = 0; i < list.Count; i++)
    {
        if (!list[i].StartsWith("--", StringComparison.Ordinal)) continue;
        var key = list[i][2..];
        var eq = key.IndexOf('=');
        if (eq >= 0) { flags[key[..eq]] = key[(eq + 1)..]; continue; }
        if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal)) { flags[key] = list[++i]; continue; }
        flags[key] = null;
    }
    return flags;
}

static DateOnly? Date(Dictionary<string, string?> flags, string key) =>
    flags.TryGetValue(key, out var v) && DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

static int Int(Dictionary<string, string?> flags, string key) =>
    flags.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
