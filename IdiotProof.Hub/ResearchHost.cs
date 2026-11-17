using IdiotProof.Core.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Engine.Storage;
using IdiotProof.Operations;
using IdiotProof.Operations.Hosting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Hub;

/// <summary>
/// The gapper study's own service graph inside the Hub (IP-T515). The study runs on its guarded research database
/// (<c>IdiotProof_Research_GAP</c>, never the trading database), so its operations (<c>research gappers ...</c>) get
/// their DI scope from this container: the same Core services, wired to the research connection string, and
/// migrated once on first use. Everything else keeps the Hub's trading graph. This replaces the separate
/// tools/GapperStudy harness, so the Hub is the one process that opens a database or calls Alpaca.
/// The study folder comes from <c>GAPPER_STUDY_DIR</c> (run.ps1 sets it on deploy) or the repo above the Hub.
/// </summary>
public sealed class ResearchHost(IConfiguration configuration, ILoggerFactory loggers) : IOperationScopeRouter, IAsyncDisposable
{
    public const string Prefix = "research gappers";

    private readonly object gate = new();
    private ServiceProvider? provider;

    public IServiceScopeFactory? ScopesFor(OperationDescriptor descriptor) =>
        descriptor.Name.StartsWith(Prefix, StringComparison.Ordinal) ? Provider().GetRequiredService<IServiceScopeFactory>() : null;

    private ServiceProvider Provider()
    {
        lock (gate)
        {
            if (provider is not null) return provider;
            var paths = GapperStudyPaths.Resolve();
            var parameters = GapperStudyParams.Load(paths);
            var researchDb = parameters.DataTiers.ResearchDb;
            if (!researchDb.Contains(parameters.DataTiers.ResearchDbGuardPattern, StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing to run: '{researchDb}' is not a research database name.");
            // Server and credentials come from ConnectionStrings__IdiotProofResearch / ConnectionStrings:IdiotProofResearch
            // (LocalDB when unset); the study names the database (IP-T542).
            var connection = ResearchDatabase.ConnectionStringFor(researchDb, configuration);
            // Wakes a paused serverless database before anything else touches it. On Azure SQL Database a research
            // database that does not exist stops here with a message to provision it: Migrate below would otherwise
            // run CREATE DATABASE on the server at a default service tier. On a local server Migrate creates it as before.
            SqlConnect.DatabaseExistsAsync(connection, "research", CancellationToken.None).GetAwaiter().GetResult();
            Directory.CreateDirectory(paths.RunsDir);
            Directory.CreateDirectory(paths.DataDir);

            var storage = new WebStorageProvider();
            storage.EnsureDirectories();
            var services = new ServiceCollection();
            services.AddSingleton(loggers);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            services.AddSingleton(configuration);
            services.AddIdiotProofCore(configuration, storage, new CoreHostOptions { PinDevKeyRing = true, ConnectionString = connection });
            services.AddIdiotProofTrading();
            services.AddIdiotProofOperations(typeof(ResearchHost).Assembly);
            services.AddScoped<IOwnerContext, OperationOwnerContext>();
            services.AddScoped<IClaudeKeyContext, OwnerClaudeKeyContext>();
            var built = services.BuildServiceProvider();

            // Migrate the research database once (the harness did this before every stage).
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)).Options;
            using (var db = new AppDbContext(options))
            {
                if (!db.Database.GetConnectionString()!.Contains(researchDb, StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to migrate a non-research database.");
                db.Database.Migrate();
            }
            return provider = built;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
    }
}
