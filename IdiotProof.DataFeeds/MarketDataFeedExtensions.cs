namespace IdiotProof.DataFeeds;

/// <summary>
/// What a feed really is behind a <see cref="SwitchableMarketDataFeed"/>. Every "is this synthetic
/// data?" check (IP-LAW-9) goes through <see cref="IsSynthetic"/>, so switching the Hub's feed to
/// Mock at runtime can never slip past a guard that only tested the registered type.
/// </summary>
public static class MarketDataFeedExtensions
{
    /// <summary>The concrete feed serving requests: unwraps a <see cref="SwitchableMarketDataFeed"/>.</summary>
    public static IMarketDataFeed? Resolve(this IMarketDataFeed feed) =>
        feed is SwitchableMarketDataFeed s ? s.Active : feed;

    /// <summary>True when prices are made up: the Mock feed, directly or as the switch's active feed.</summary>
    public static bool IsSynthetic(this IMarketDataFeed feed) =>
        feed is MockDataFeed
        || string.Equals(feed.FeedName, "Mock", StringComparison.OrdinalIgnoreCase)
        || feed.Resolve() is MockDataFeed;
}
