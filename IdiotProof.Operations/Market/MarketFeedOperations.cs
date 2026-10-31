using IdiotProof.Core.Hosting;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Operations.Rendering;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Market;

/// <summary>The Hub's market data feed as <c>market feed status</c> reports it.</summary>
public sealed record MarketFeedState(
    string Active, bool Synthetic, bool Switchable, string[] Registered, string? AlpacaTier, string MarketOpsTier,
    string? StartupFeed, string? StartupTier, Dictionary<string, int> ActiveStrategiesByMode);

/// <summary>Reads (and, for the switch, unwraps) the Hub's one <see cref="IMarketDataFeed"/>.</summary>
internal static class MarketFeeds
{
    public static async Task<MarketFeedState> StateAsync(OperationContext ctx)
    {
        var feed = ctx.Get<IMarketDataFeed>();
        var switchable = feed as SwitchableMarketDataFeed;
        var alpaca = MarketFeeds.Alpaca(feed);
        var active = await ctx.Get<StrategyRepository>().GetActiveAsync(ctx.CancellationToken);
        var byMode = active
            .GroupBy(s => OrderIntent.ParseMode(s.BrokerMode).ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        return new MarketFeedState(
            feed.Resolve()?.FeedName ?? feed.FeedName,
            feed.IsSynthetic(),
            switchable is not null,
            switchable is null ? [feed.FeedName] : [.. switchable.Feeds.Keys.Order(StringComparer.Ordinal)],
            alpaca?.FeedTier,
            MarketData.Feed,
            Environment.GetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar),
            Environment.GetEnvironmentVariable(TradingServiceRegistration.AlpacaFeedEnvVar),
            byMode);
    }

    /// <summary>The Alpaca feed behind the Hub's feed (registered in the switch, or the feed itself), if any.</summary>
    public static AlpacaDataFeed? Alpaca(IMarketDataFeed feed) =>
        feed is SwitchableMarketDataFeed s ? s.Feeds.Values.OfType<AlpacaDataFeed>().FirstOrDefault() : feed as AlpacaDataFeed;

    public static string Render(MarketFeedState s)
    {
        var block = new KeyValueBlock()
            .Add("Active feed", s.Active + (s.Synthetic ? "  (SYNTHETIC: only Sandbox strategies can enter, IP-LAW-9)" : ""))
            .Add("Alpaca tier", s.AlpacaTier, "- (no Alpaca keys: the Hub runs on Mock)")
            .Add("market ops tier", s.MarketOpsTier)
            .Add("Switchable", s.Switchable ? "yes: " + string.Join(", ", s.Registered) : "no (no Alpaca keys resolved at startup)")
            .Add("Startup choice", $"{TradingServiceRegistration.FeedEnvVar}={s.StartupFeed ?? "(auto)"}  {TradingServiceRegistration.AlpacaFeedEnvVar}={s.StartupTier ?? "(sip)"}")
            .Add("Active strategies", s.ActiveStrategiesByMode.Count == 0 ? "none"
                : string.Join(", ", s.ActiveStrategiesByMode.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Value} {kv.Key}")));
        return block.Render();
    }
}

/// <summary>
/// Which market data the Hub is running on: Alpaca (SIP or IEX tier) or the synthetic Mock feed,
/// whether it can be switched, the startup knobs and how many strategies the Monitor is evaluating
/// per broker mode (Mock data only ever drives Sandbox entries, IP-LAW-9).
/// </summary>
[Operation("market feed status", Summary = "The Hub's market data feed: Alpaca SIP/IEX or Mock, whether it can switch, and active strategies per mode.", Risk = OperationRisk.Read)]
[Example("idiotproof market feed status")]
public sealed class MarketFeedStatusOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var state = await MarketFeeds.StateAsync(ctx);
        return OperationResult.Ok(state, MarketFeeds.Render(state));
    }
}

public sealed record MarketFeedSwitchArgs
{
    [Argument(0, Name = "feed", Description = "sip (Alpaca consolidated, Algo Trader Plus), iex (Alpaca free tier) or mock (synthetic prices).")]
    public string Feed { get; init; } = "";
}

/// <summary>
/// Switches the Hub's market data without a restart. <c>sip</c>/<c>iex</c> put Alpaca back in
/// front on that tier (the <c>market ...</c> operations follow it too); <c>mock</c> swaps in
/// synthetic prices and is refused while any active strategy routes to Paper or Live, because
/// made-up prices must never drive a real order (IP-LAW-9). The switch lasts until the Hub
/// restarts; the startup choice stays IDIOTPROOF_FEED / IDIOTPROOF_ALPACA_FEED.
/// </summary>
[Operation("market feed switch", Summary = "Switch the Hub's market data to sip, iex or mock (mock refused while Paper/Live strategies are active).", Risk = OperationRisk.Write)]
[Example("idiotproof market feed switch iex")]
[Example("idiotproof market feed switch mock", Description = "Only while every active strategy is Sandbox.")]
public sealed class MarketFeedSwitchOperation : IOperation<MarketFeedSwitchArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketFeedSwitchArgs args, OperationContext ctx)
    {
        var target = args.Feed.Trim().ToLowerInvariant();
        if (target is not ("sip" or "iex" or "mock"))
            return OperationResult.Usage("Switch to sip, iex or mock, e.g. `idiotproof market feed switch iex`.");

        var feed = ctx.Get<IMarketDataFeed>();
        var switchable = feed as SwitchableMarketDataFeed;
        var alpaca = MarketFeeds.Alpaca(feed);

        if (target == "mock")
        {
            if (feed.IsSynthetic())
                return OperationResult.Ok(await MarketFeeds.StateAsync(ctx), "Already on the Mock feed.");
            var real = (await ctx.Get<StrategyRepository>().GetActiveAsync(ctx.CancellationToken))
                .Where(s => OrderIntent.ParseMode(s.BrokerMode) != BrokerMode.Sandbox)
                .ToList();
            if (real.Count > 0)
                return OperationResult.Blocked(
                    $"{real.Count} active strateg{(real.Count == 1 ? "y routes" : "ies route")} to Paper/Live, and Mock prices must never drive a real order (IP-LAW-9). " +
                    "Deactivate them or move them to sandbox first:\n" +
                    string.Join("\n", real.Select(s => $"  {Strategies.StrategyRefs.ShortGuid(s.Id)}  {s.Symbol,-6} {OrderIntent.ParseMode(s.BrokerMode),-7} {s.Title}")),
                    new { activeRealStrategies = real.Select(s => new { s.Id, s.Symbol, s.Title, Mode = OrderIntent.ParseMode(s.BrokerMode).ToString() }) });
            if (switchable is null || !switchable.Feeds.ContainsKey("Mock"))
                return OperationResult.Fail("This Hub's feed can't be switched at runtime; start it with IDIOTPROOF_FEED=mock instead.");
            switchable.SetActiveFeed("Mock");
        }
        else
        {
            if (alpaca is null)
                return OperationResult.Fail("No Alpaca keys resolved when the Hub started, so it can only run on Mock. Add keys (`idiotproof keys`) and restart the Hub.");
            alpaca.SetTier(target);
            switchable?.SetActiveFeed(alpaca.FeedName);
            // The market ... operations read the tier from the same knob the Hub started with.
            Environment.SetEnvironmentVariable(MarketData.FeedEnvVar, target);
        }

        var state = await MarketFeeds.StateAsync(ctx);
        var said = target == "mock"
            ? "Switched to the Mock feed: synthetic prices, Sandbox entries only."
            : $"Switched to Alpaca {target.ToUpperInvariant()}.";
        return OperationResult.Ok(state, said + " Lasts until the Hub restarts.\n\n" + MarketFeeds.Render(state));
    }
}
