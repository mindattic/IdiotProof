using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Engine.Settings;

using IdiotProof.Core.Services;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using Microsoft.EntityFrameworkCore;
namespace IdiotProof.Core.Hosting;

/// <summary>
/// The order-placing half of the graph: the one <see cref="BrokerRouter"/>, the market-data feed
/// and per-strategy broker routing. Registered only by the host that runs the Monitor (today the
/// Monitor console, next the Hub) — the web app never places orders (IP-LAW-10).
/// </summary>
public static class TradingServiceRegistration
{
    /// <summary>Env knob: <c>alpaca</c> | <c>mock</c> (default: Alpaca when keyed, Mock otherwise).</summary>
    public const string FeedEnvVar = "IDIOTPROOF_FEED";

    /// <summary>Env knob: <c>alpaca</c> | <c>sandbox</c> (default Sandbox, IP-LAW-3).</summary>
    public const string BrokerEnvVar = "IDIOTPROOF_BROKER";

    /// <summary>Env knob: Alpaca data tier, <c>sip</c> (default, Algo Trader Plus) or <c>iex</c> (free tier).</summary>
    public const string AlpacaFeedEnvVar = "IDIOTPROOF_ALPACA_FEED";

    /// <summary>Env knob (IP-T454): <c>app</c> keeps bars in the app database's own cache instead of the permanent history database.</summary>
    public const string HistoryStoreEnvVar = "IDIOTPROOF_HISTORY_STORE";

    /// <summary>
    /// Where fetched bars are kept: the permanent history database (falling back to the app cache when
    /// its server does not answer), unless no history database is registered or the env knob says <c>app</c>.
    /// </summary>
    private static IBarCache BarCache(IServiceProvider sp)
    {
        var app = new AppDbBarCache(sp.GetRequiredService<IDbContextFactory<AppDbContext>>());
        var history = sp.GetService<IdiotProof.Core.History.HistoryDatabase>();
        if (history is null || string.Equals(Environment.GetEnvironmentVariable(HistoryStoreEnvVar), "app", StringComparison.OrdinalIgnoreCase)) return app;
        return new IdiotProof.Core.History.FallbackBarCache(new IdiotProof.Core.History.SqlHistoryBarCache(history), app,
            sp.GetService<TimeProvider>() ?? TimeProvider.System, sp.GetService<Microsoft.Extensions.Logging.ILogger<IdiotProof.Core.History.FallbackBarCache>>());
    }

    public static IServiceCollection AddIdiotProofTrading(this IServiceCollection services)
    {
        services.AddSingleton<IMarketDataFeed>(sp => CreateFeed(sp.GetRequiredService<AppSettings>()));
        services.AddSingleton<ISplitSource>(sp => CreateSplitSource(sp.GetRequiredService<AppSettings>()));
        services.AddSingleton<IHistoricalBarStore>(sp => new HistoricalBarStore(
            BarCache(sp), sp.GetRequiredService<IMarketDataFeed>(), splits: sp.GetRequiredService<ISplitSource>()));
        services.AddSingleton(sp => CreateRouter(sp.GetRequiredService<AppSettings>()));
        services.AddSingleton<UserBrokerResolver>();
        // Every order — Monitor, manual, options, flatten — goes through the gateway.
        services.AddSingleton<IdiotProof.Core.Trading.IOrderRouting, IdiotProof.Core.Trading.OrderRouting>();
        services.AddSingleton<IdiotProof.Core.Trading.TransactionGateway>();
        return services;
    }

    /// <summary>
    /// Market data: Alpaca whenever keys resolved through the settings chain (env → Vault broker
    /// keyring → configuration), Mock otherwise. Forced with <see cref="FeedEnvVar"/>. Mock data
    /// never drives a real order (IP-LAW-9); the Monitor enforces that at the order seam.
    /// With keys the feed is a <see cref="SwitchableMarketDataFeed"/> holding both Alpaca and Mock,
    /// so <c>idiotproof market feed switch</c> can change it without a restart; every synthetic-data
    /// guard asks <see cref="MarketDataFeedExtensions.IsSynthetic"/>, which sees through the switch.
    /// </summary>
    /// <summary>Split data for return studies: Alpaca corporate actions when keys exist, none otherwise.</summary>
    public static ISplitSource CreateSplitSource(AppSettings settings) =>
        HasAlpacaKeys(settings)
            ? new AlpacaSplitSource(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey)
            : new NullSplitSource();

    public static IMarketDataFeed CreateFeed(AppSettings settings)
    {
        var choice = Environment.GetEnvironmentVariable(FeedEnvVar)?.ToLowerInvariant();
        if (!HasAlpacaKeys(settings) && choice != "alpaca")
            return new MockDataFeed();
        var tier = Environment.GetEnvironmentVariable(AlpacaFeedEnvVar) ?? "sip";
        var feed = new SwitchableMarketDataFeed(choice == "mock" ? "Mock" : "Alpaca");
        feed.Register(new AlpacaDataFeed(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, tier));
        feed.Register(new MockDataFeed());
        return feed;
    }

    /// <summary>
    /// Broker: Sandbox is always registered and active by default (IP-LAW-3). Alpaca joins when
    /// keys exist; <see cref="BrokerEnvVar"/>=alpaca makes it the global default. Per-strategy
    /// Paper/Live routing to a user's own account is <see cref="UserBrokerResolver"/>'s job.
    /// </summary>
    public static BrokerRouter CreateRouter(AppSettings settings)
    {
        var router = new BrokerRouter();
        router.Register(new SandboxBrokerClient());
        if (HasAlpacaKeys(settings))
            router.Register(new AlpacaBrokerClient(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, settings.AlpacaIsPaper));
        var requested = Environment.GetEnvironmentVariable(BrokerEnvVar);
        router.SetActive(requested); // null/garbage → stays Sandbox
        if (router.GetAll().All(b => b.BrokerType != router.ActiveBrokerType))
        {
            // Asked for a broker that has no keys: the router would throw on every order. Stay on
            // Sandbox (IP-LAW-3) and say so at startup instead of failing each tick.
            Console.Error.WriteLine(
                $"[broker] {BrokerEnvVar}={requested} but no {router.ActiveBrokerType} keys resolved — staying on Sandbox.");
            router.SetActive(BrokerType.Sandbox);
        }
        return router;
    }

    private static bool HasAlpacaKeys(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId) && !string.IsNullOrWhiteSpace(settings.AlpacaApiSecretKey);
}
