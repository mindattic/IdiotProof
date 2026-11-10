using System.Globalization;
using IdiotProof.Core.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Engine.Storage;
using IdiotProof.Operations;
using IdiotProof.Operations.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// IP-T493 "Momentum." claim tracker harness: runs the `momentum import|extract|verify|report` operations in-process
// against the app database and the owner's keys, so they work before the Hub is republished with them.
//   dotnet run --project tools/MomentumTracker -- all [--dir D:\IdiotProofData\momentum] [--since 2026-11-01] [--limit N]
//   dotnet run --project tools/MomentumTracker -- import|extract|verify|report [--force] [--out path] [--user <guid>]
// The only migration it applies is AddMomentumClaimTracker (additive: four new tables); it refuses to start when any
// other migration is pending, so it never moves the app schema further than its own tables.

const string DefaultOwner = "a99504c3-0759-4256-8abb-a1f5c643808c";
const string OwnMigration = "AddMomentumClaimTracker";

var stage = args.FirstOrDefault()?.ToLowerInvariant() ?? "all";
var flags = ParseFlags(args.Skip(1));
var owner = Guid.Parse(flags.GetValueOrDefault("user") ?? DefaultOwner);

var configuration = new ConfigurationBuilder().AddIdiotProofVault().Build();
var connection = CoreServiceRegistration.ResolveConnectionString(configuration);

var storage = new WebStorageProvider();
storage.EnsureDirectories();
var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }).SetMinimumLevel(LogLevel.Information)
    .AddFilter("Microsoft", LogLevel.Warning).AddFilter("System", LogLevel.Warning));
services.AddSingleton<IConfiguration>(configuration);
services.AddIdiotProofCore(configuration, storage, new CoreHostOptions { PinDevKeyRing = true });
services.AddIdiotProofTrading();
await using var root = services.BuildServiceProvider();

await using (var db = await root.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
{
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Count > 0)
    {
        if (pending.Any(m => !m.EndsWith("_" + OwnMigration, StringComparison.Ordinal)))
        {
            Console.WriteLine($"Refusing: pending migrations other than {OwnMigration}: {string.Join(", ", pending)}. Apply those through their own owner first.");
            return 2;
        }
        Console.WriteLine($"Applying {string.Join(", ", pending)} (additive: MomentumVideos, MomentumTranscripts, MomentumClaims, MomentumClaimOutcomes) to {db.Database.GetDbConnection().Database}.");
        await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db).MigrateAsync(pending[^1]);
    }
}

using var scope = root.CreateScope();
var ctx = new OperationContext { Services = scope.ServiceProvider, Width = 200, OwnerId = owner, Caller = OperationCaller.Cli };
var since = flags.TryGetValue("since", out var sv) && DateOnly.TryParseExact(sv, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sd) ? sd : (DateOnly?)null;
var limit = flags.TryGetValue("limit", out var lv) && int.TryParse(lv, CultureInfo.InvariantCulture, out var ln) ? ln : 0;
var dir = flags.GetValueOrDefault("dir");

var steps = stage == "all" ? new[] { "import", "extract", "verify", "report" } : [stage];
int exit = 0;
foreach (var step in steps)
{
    Console.WriteLine($"== momentum {step} ==");
    OperationResult r;
    try
    {
        r = step switch
        {
            "import" => await new MomentumImportOperation().ExecuteAsync(new MomentumImportArgs { Dir = dir }, ctx),
            "extract" => await new MomentumExtractOperation().ExecuteAsync(new MomentumExtractArgs { Since = since, Limit = limit, Dir = dir, Force = flags.ContainsKey("force") }, ctx),
            "verify" => await new MomentumVerifyOperation().ExecuteAsync(new MomentumVerifyArgs { Since = since, Force = flags.ContainsKey("force"), Dir = dir }, ctx),
            "report" => await new MomentumReportOperation().ExecuteAsync(new MomentumReportArgs { Since = since, Out = flags.GetValueOrDefault("out") }, ctx),
            _ => OperationResult.Usage($"Unknown step '{step}'. Use import, extract, verify, report or all."),
        };
    }
    catch (Exception ex)
    {
        Console.WriteLine($"CRASH in {step}: {ex}");
        return 1;
    }
    Console.WriteLine(r.Render(OutputMode.Human));
    if (r.ExitCode != 0) { exit = r.ExitCode; break; }
}
return exit;

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
