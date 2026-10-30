using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Catalogs;
using IdiotProof.Engine;
using IdiotProof.Engine.Storage;
using IdiotProof.Engine.Workspace;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Web;
using MindAttic.Legion;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.DependencyInjection;

namespace IdiotProof.Core.Hosting;

/// <summary>How a host differs when it registers the shared graph.</summary>
public sealed class CoreHostOptions
{
    /// <summary>
    /// Production posture for MindAttic.Authentication: with no durable key ring configured the
    /// web host fails closed rather than minting a throwaway ring.
    /// </summary>
    public bool IsProduction { get; init; }

    /// <summary>
    /// Console hosts (Hub, Monitor) pin the per-user dev ring at
    /// <c>%APPDATA%\MindAttic\DataProtection\IdiotProof</c> when no production ring is configured,
    /// so they decrypt the same <c>UserApiKeys</c> rows the web app wrote.
    /// </summary>
    public bool PinDevKeyRing { get; init; }

    /// <summary>E2E seam (Development only): LegionClient talks to <see cref="FakeLlmHandler"/>.</summary>
    public bool UseFakeLlm { get; init; }
}

/// <summary>
/// The one service graph every IdiotProof front door registers (HOUSE-LAW-6): the web app, the
/// Hub, the Monitor and the research scanner all call <see cref="AddIdiotProofCore"/>, so a
/// strategy, key or order written by one is read by the others with zero translation.
/// Host-only pieces (Razor components, SignalR pushers, the hosted Monitor worker, the owner
/// source) stay in each host, which also supplies <see cref="IConfiguration"/> and, for the auth
/// stack's policy cache, routing.
/// </summary>
public static class CoreServiceRegistration
{
    public const string LocalDbConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=IdiotProof;Trusted_Connection=True;TrustServerCertificate=True;";

    /// <summary>Env var <c>ConnectionStrings__IdiotProof</c> → configuration → LocalDB (same for every host and for design time).</summary>
    public static string ResolveConnectionString(IConfiguration configuration) =>
        Environment.GetEnvironmentVariable("ConnectionStrings__IdiotProof")
        ?? configuration.GetConnectionString("IdiotProof")
        ?? LocalDbConnectionString;

    /// <summary>
    /// Surfaces the MindAttic Vault buckets (LLM, Brokers, … plus Security for the auth pepper)
    /// into configuration. Malformed bucket files are skipped by the provider, never fatal.
    /// </summary>
    public static IConfigurationBuilder AddIdiotProofVault(this IConfigurationBuilder configuration) =>
        configuration.AddMindAtticVaultFiles(src => src.Buckets = [.. src.Buckets, "Security"]);

    /// <summary>Default per-user key ring for console hosts (see <see cref="CoreHostOptions.PinDevKeyRing"/>).</summary>
    public static string DevKeyRingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "DataProtection", "IdiotProof");

    public static IServiceCollection AddIdiotProofCore(
        this IServiceCollection services,
        IConfiguration configuration,
        IStorageProvider storage,
        CoreHostOptions options)
    {
        services.AddMindAtticVault(configuration);

        // ── SQL ──
        var connectionString = ResolveConnectionString(configuration);
        services.AddSingleton(new IdiotProofDatabase(connectionString));
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        // ── Engine: storage, AppSettings overlay chain, workspaces, audit logger ──
        // The SQL workspace store is registered first so the engine's TryAdd keeps it.
        services.AddSingleton<IWorkspaceStore>(sp =>
            new SqlWorkspaceStore(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), storage));
        services.AddIdiotProofEngine(storage, configuration);

        // ── Accounts + the shared Data Protection key ring ──
        services.AddMindAtticAuthentication<AppDbContext>(configuration, opts =>
        {
            opts.AppName = "IdiotProof";
            opts.IsProduction = options.IsProduction;
            var ring = SharedKeyRing(configuration, options.PinDevKeyRing);
            if (ring is not null) opts.ConfigureDataProtection = ring;
        });

        // ── LLM: every call goes through MindAttic.Legion (HOUSE-LAW-4) ──
        services.AddLegionClient();
        if (options.UseFakeLlm)
            services.AddTransient<LegionClient>(_ => new LegionClient(new HttpClient(new FakeLlmHandler()), options: null));
        services.AddSignalVotingPanel();

        // ── Static catalogs ──
        services.AddSingleton<IStaticCatalogSource>(StaticCatalogSource.Default);

        // ── Keys, risk (broker routing is AddIdiotProofTrading: it needs the BrokerRouter) ──
        services.AddSingleton<UserKeyService>();
        services.AddSingleton<UserClaudeKeyResolver>();
        services.AddSingleton<LiveModeElevationService>();
        services.AddSingleton<RiskGuardianService>();
        services.AddSingleton<TradingStateService>();
        services.AddScoped<AccountSummaryService>();
        services.AddScoped<OptionsTradingService>();

        // ── Strategies, gapper, diary, audit, settings ──
        services.AddSingleton<StrategyRepository>();
        services.AddSingleton<ConditionProgressRepository>();
        services.AddSingleton<StrategyScriptGenerator>();
        services.AddSingleton<GapperProfileService>();
        services.AddSingleton<GapperInterpreter>();
        services.AddSingleton<TradeDiaryRepository>();
        services.AddSingleton<AuditLogRepository>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<UserPreferencesService>();
        services.AddSingleton<EmailDomainBlocklistService>();
        services.AddSingleton<LiveBarRepository>();

        // ── Research ──
        services.AddHttpClient();
        services.AddHttpClient("edgar", c =>
        {
            // SEC EDGAR requires a descriptive User-Agent. The contact email MUST be a
            // parenthesized comment: .NET's strict header parser rejects "@" in a bare product
            // token (FormatException), which EdgarService's fail-closed catch used to swallow.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("IdiotProof/1 (research@idiotproof.app)");
            c.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient("usspends", c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<EdgarService>();
        services.AddScoped<Form4Parser>();
        services.AddScoped<UsSpendsService>();
        services.AddScoped<AlpacaNewsService>();
        services.AddScoped<CatalystExtractor>();
        services.AddScoped<ClaimVectorService>();
        services.AddSingleton<ClaimCorrelationService>();
        services.AddScoped<ResearchService>();
        services.AddScoped<TickerUniverseService>();
        services.AddScoped<CorporateActionDetector>();
        services.AddScoped<RegulatoryScanner>();
        services.AddScoped<IndexEventScanner>();
        services.AddScoped<SignificanceScorer>();
        services.AddScoped<OutcomeBackfillService>();

        return services;
    }

    /// <summary>
    /// The one key-ring rule (docs/BIBLE.md §4.4). Azure Blob + Key Vault when both URIs are set,
    /// else a durable <c>DataProtection:KeyRingPath</c>, else (console hosts) the per-user dev ring.
    /// Null leaves the library default: the dev ring in Development, fail-closed in production.
    /// Every host must land on the same ring or <c>UserApiKeys</c> rows become unreadable to one of them.
    /// </summary>
    public static Action<IDataProtectionBuilder>? SharedKeyRing(IConfiguration configuration, bool pinDevKeyRing)
    {
        var blobUri = configuration["DataProtection:AzureBlobUri"];
        var keyVaultKeyUri = configuration["DataProtection:KeyVaultKeyUri"];
        var keyRingPath = configuration["DataProtection:KeyRingPath"];

        if (!string.IsNullOrWhiteSpace(blobUri) && !string.IsNullOrWhiteSpace(keyVaultKeyUri))
            return dp =>
            {
                var credential = new Azure.Identity.DefaultAzureCredential();
                dp.PersistKeysToAzureBlobStorage(new Uri(blobUri), credential)
                  .ProtectKeysWithAzureKeyVault(new Uri(keyVaultKeyUri), credential);
            };
        if (!string.IsNullOrWhiteSpace(keyRingPath))
            return dp => dp.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        if (pinDevKeyRing)
            return dp => dp.PersistKeysToFileSystem(new DirectoryInfo(DevKeyRingPath));
        return null;
    }
}

/// <summary>The resolved SQL connection string (the Monitor's leader lease opens its own connection).</summary>
public sealed record IdiotProofDatabase(string ConnectionString);
