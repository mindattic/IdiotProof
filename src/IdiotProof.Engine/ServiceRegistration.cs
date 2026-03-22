using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Engine.Workspace;
using IdiotProof.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Engine;

/// <summary>
/// DI registration for all engine services. Both Web and Desktop call this.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddIdiotProofEngine(this IServiceCollection services, IStorageProvider storageProvider)
    {
        // Storage
        services.AddSingleton(storageProvider);

        // Settings
        var settings = AppSettings.Load(storageProvider);
        services.AddSingleton(settings);

        // Strategies
        services.AddSingleton<StrategyRegistry>();

        // Brokers
        services.AddSingleton<SandboxBrokerClient>();
        services.AddSingleton<BrokerRouter>(sp =>
        {
            var router = new BrokerRouter();
            router.Register(sp.GetRequiredService<SandboxBrokerClient>());

            // Register Alpaca if configured
            if (!string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId))
            {
                var alpaca = new AlpacaBrokerClient(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, settings.AlpacaIsPaper);
                router.Register(alpaca);
            }

            // Register IBKR
            var ibkrPort = settings.IbkrUsePaper ? settings.IbkrPaperPort : settings.IbkrLivePort;
            var ibkr = new IbkrBrokerClient(settings.IbkrHost, ibkrPort, settings.IbkrClientId);
            router.Register(ibkr);

            return router;
        });

        // Data Feeds
        services.AddSingleton<SwitchableMarketDataFeed>(sp =>
        {
            var feed = new SwitchableMarketDataFeed(settings.DefaultDataFeed);

            if (!string.IsNullOrWhiteSpace(settings.PolygonApiKey))
                feed.Register(new PolygonDataFeed(settings.PolygonApiKey));

            return feed;
        });

        // Workspace Manager
        services.AddSingleton<WorkspaceManager>(sp =>
        {
            var manager = new WorkspaceManager(storageProvider);
            manager.LoadAll();
            return manager;
        });

        return services;
    }
}
