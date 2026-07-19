using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Engine.Workspace;
using IdiotProof.Strategies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Engine;

public static class ServiceRegistration
{
    /// <summary>
    /// Backward-compat overload that loads settings without an IConfiguration
    /// overlay. New code should pass <see cref="IConfiguration"/> so the
    /// cloud-native overlay (User Secrets / App Service Application Settings /
    /// Azure Key Vault) wins over the file-based sources.
    /// </summary>
    public static IServiceCollection AddIdiotProofEngine(this IServiceCollection services, IStorageProvider storageProvider) =>
        AddIdiotProofEngine(services, storageProvider, configuration: null);

    /// <summary>
    /// Cloud-native registration. Overlays settings in this order (later wins):
    /// disk → env vars → MindAttic LLM keyring → MindAttic broker keyring →
    /// IConfiguration. Pass <c>builder.Configuration</c> from the host so
    /// User Secrets / App Service / Key Vault values are layered on top.
    /// </summary>
    public static IServiceCollection AddIdiotProofEngine(
        this IServiceCollection services,
        IStorageProvider storageProvider,
        IConfiguration? configuration)
    {
        services.AddSingleton(storageProvider);

        // Load settings from disk, then overlay any secrets from environment variables
        // (Azure App Service injects Key Vault references as env vars with the same names).
        // Then overlay from the shared MindAttic family keyrings — these are the
        // canonical first stop and win over both disk config and env vars:
        //   • LLM keys      → %APPDATA%\MindAttic\LLM\providers.json
        //   • Broker keys   → %APPDATA%\MindAttic\Brokers\providers.json
        // Finally, if an IConfiguration is supplied, layer User Secrets / App
        // Service Application Settings / Azure Key Vault references on top. The
        // configuration overlay always wins so production secrets override
        // anything on disk.
        var settings = AppSettings.Load(storageProvider);
        settings.OverlayFromEnvironment();
        settings.OverlayFromMindAtticCredentials();
        settings.OverlayFromBrokerCredentials();
        if (configuration is not null)
            settings.OverlayFromConfiguration(configuration);
        services.AddSingleton(settings);

        // NOTE (IP-A8): the StrategyRegistry / BrokerRouter / SwitchableMarketDataFeed
        // singletons that used to be registered here were dead DI — nothing in the
        // Blazor host consumed them (audit 2026-07-18). The one live order-placing
        // host, IdiotProof.Monitor, constructs its own BrokerRouter + feed from the
        // same AppSettings chain in its Program.cs. Exactly one construction site
        // exists now; do not re-add rival registrations here.

        // Workspace store — JSON-on-disk by default. The Blazor host overrides
        // this registration with a SQL-backed implementation (SqlWorkspaceStore)
        // before AddIdiotProofEngine runs; CLI and tests keep the JSON path.
        // TryAddSingleton so a pre-registered IWorkspaceStore (from the host)
        // wins; otherwise we install the JSON default.
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
            .TryAddSingleton<IWorkspaceStore>(services, sp => new JsonFileWorkspaceStore(storageProvider));

        // Workspace manager — caches + seeds defaults on top of IWorkspaceStore.
        // No eager LoadAll() here: that call targets the legacy "__global__"
        // bucket, which the SQL-backed store cannot represent (its Load/Save
        // Guid-gate silently no-ops), so the old startup seed was a lie — it
        // fabricated an ephemeral Default tab and persisted nothing. Loading
        // is lazy per user via GetTabsForUser; the CLI/JSON path can still
        // call LoadAll() itself.
        services.AddSingleton<WorkspaceManager>(sp =>
            new WorkspaceManager(sp.GetRequiredService<IWorkspaceStore>()));

        // Audit logger
        services.AddSingleton<AuditLogger>();

        return services;
    }
}
