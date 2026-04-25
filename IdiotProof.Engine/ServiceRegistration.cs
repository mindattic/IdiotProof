using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Engine.Workspace;
using IdiotProof.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Engine;

public static class ServiceRegistration
{
    public static IServiceCollection AddIdiotProofEngine(this IServiceCollection services, IStorageProvider storageProvider)
    {
        services.AddSingleton(storageProvider);

        // Load settings from disk, then overlay any secrets from environment variables
        // (Azure App Service injects Key Vault references as env vars with the same names).
        // Finally overlay from the shared MindAttic LLM credential store — it is the
        // canonical first stop for LLM keys and wins over both disk config and env vars.
        var settings = AppSettings.Load(storageProvider);
        settings.OverlayFromEnvironment();
        settings.OverlayFromMindAtticCredentials();
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

            var ibkrPort = settings.IbkrUsePaper ? settings.IbkrPaperPort : settings.IbkrLivePort;
            var ibkr = new IbkrBrokerClient(settings.IbkrHost, ibkrPort, settings.IbkrClientId);
            router.Register(ibkr);

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

        // Workspace manager
        services.AddSingleton<WorkspaceManager>(sp =>
        {
            var manager = new WorkspaceManager(storageProvider);
            manager.LoadAll();
            return manager;
        });

        // Audit logger
        services.AddSingleton<AuditLogger>();

        return services;
    }
}
