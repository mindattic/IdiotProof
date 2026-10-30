using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Engine.Settings;

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

    public static IServiceCollection AddIdiotProofTrading(this IServiceCollection services)
    {
        services.AddSingleton<IMarketDataFeed>(sp => CreateFeed(sp.GetRequiredService<AppSettings>()));
        services.AddSingleton(sp => CreateRouter(sp.GetRequiredService<AppSettings>()));
        services.AddSingleton<UserBrokerResolver>();
        return services;
    }

    /// <summary>
    /// Market data: Alpaca whenever keys resolved through the settings chain (env → Vault broker
    /// keyring → configuration), Mock otherwise. Forced with <see cref="FeedEnvVar"/>. Mock data
    /// never drives a real order (IP-LAW-9); the Monitor enforces that at the order seam.
    /// </summary>
    public static IMarketDataFeed CreateFeed(AppSettings settings)
    {
        var choice = Environment.GetEnvironmentVariable(FeedEnvVar)?.ToLowerInvariant();
        if (choice == "mock" || (!HasAlpacaKeys(settings) && choice != "alpaca"))
            return new MockDataFeed();
        var tier = Environment.GetEnvironmentVariable(AlpacaFeedEnvVar) ?? "sip";
        return new AlpacaDataFeed(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, tier);
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
