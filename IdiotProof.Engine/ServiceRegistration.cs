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

        // Strategies
        services.AddSingleton<StrategyRegistry>();

        // Brokers — Sandbox is always registered as the safe fallback
        services.AddSingleton<SandboxBrokerClient>();
        services.AddSingleton<BrokerRouter>(sp =>
        {
            var router = new BrokerRouter();
            router.Register(sp.GetRequiredService<SandboxBrokerClient>());

            if (!string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId))
            {
                var alpaca = new AlpacaBrokerClient(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, settings.AlpacaIsPaper);
                router.Register(alpaca);
            }

            // IBKR support is dormant — see IdiotProof.Brokers.Ibkr/README.md to re-enable.

            // Set the configured default broker as active
            router.SetActive(settings.DefaultBroker);

            return router;
        });

        // Data Feeds — Mock is always registered so the engine works without API keys
        services.AddSingleton<SwitchableMarketDataFeed>(sp =>
        {
            var feed = new SwitchableMarketDataFeed("Mock");
            feed.Register(new MockDataFeed());

            if (!string.IsNullOrWhiteSpace(settings.PolygonApiKey))
            {
                feed.Register(new PolygonDataFeed(settings.PolygonApiKey));
                feed.SetActiveFeed("Polygon");
            }

            // Override with configured default if registered
            if (!string.IsNullOrWhiteSpace(settings.DefaultDataFeed) && settings.DefaultDataFeed != "Mock")
            {
                try { feed.SetActiveFeed(settings.DefaultDataFeed); } catch { /* fallback to Mock */ }
            }

            return feed;
        });

        // Workspace store — JSON-on-disk by default. The Blazor host overrides
        // this registration with a SQL-backed implementation (SqlWorkspaceStore)
        // before AddIdiotProofEngine runs; CLI and tests keep the JSON path.
        // TryAddSingleton so a pre-registered IWorkspaceStore (from the host)
        // wins; otherwise we install the JSON default.
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
            .TryAddSingleton<IWorkspaceStore>(services, sp => new JsonFileWorkspaceStore(storageProvider));

        // Workspace manager — caches + seeds defaults on top of IWorkspaceStore.
        services.AddSingleton<WorkspaceManager>(sp =>
        {
            var manager = new WorkspaceManager(sp.GetRequiredService<IWorkspaceStore>());
            manager.LoadAll();
            return manager;
        });

        // Audit logger
        services.AddSingleton<AuditLogger>();

        return services;
    }
}
