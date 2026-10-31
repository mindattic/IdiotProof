using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketPrevCloseArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. AAPL MSFT.")]
    public string[] Symbols { get; init; } = [];
}

/// <summary>
/// The previous regular session's close (Alpaca snapshot <c>prevDailyBar</c>) next to the latest
/// trade, with the change since that close. One aligned row per symbol.
/// </summary>
[Operation("market prev-close", Summary = "Previous session's close (and full daily bar) for one or more stocks, with the move since.", Risk = OperationRisk.Read)]
[Example("idiotproof market prev-close AAPL")]
[Example("idiotproof market prev-close SPY QQQ IWM")]
public sealed class MarketPrevCloseOperation : IOperation<MarketPrevCloseArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketPrevCloseArgs args, OperationContext ctx)
    {
        var symbols = MarketData.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol, e.g. `idiotproof market prev-close AAPL`.");

        await using var api = await MarketData.OpenAsync(ctx);
        var root = await api.GetDataAsync("/v2/stocks/snapshots",
            MarketData.Q(("symbols", string.Join(',', symbols)), ("feed", MarketData.Feed)), ctx.CancellationToken);
        var snaps = MarketData.BySymbol(root, null);

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Symbol").Column("Session").Column("Prev close", Align.Right).Column("Open", Align.Right).Column("High", Align.Right)
            .Column("Low", Align.Right).Column("Volume", Align.Right).Column("Last", Align.Right).Column("Since close", Align.Right).Column("%", Align.Right);
        var data = new List<object>();
        var missing = new List<string>();
        foreach (var s in symbols)
        {
            if (!snaps.TryGetValue(s, out var snap) || snap.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                missing.Add(s);
                continue;
            }
            var prev = MarketData.Obj(snap, "prevDailyBar");
            var day = MarketData.Time(prev, "t");
            decimal? close = MarketData.Dec(prev, "c"), last = MarketData.Dec(MarketData.Obj(snap, "latestTrade"), "p");
            decimal? change = last - close;
            table.Row(s, day is { } d ? EasternClock.FormatDate(d) : "-", MarketData.Px(close), MarketData.Px(MarketData.Dec(prev, "o")),
                MarketData.Px(MarketData.Dec(prev, "h")), MarketData.Px(MarketData.Dec(prev, "l")), MarketData.Compact(MarketData.Dec(prev, "v")),
                MarketData.Px(last), MarketData.Signed(change), MarketData.Pct(change, close));
            data.Add(new
            {
                symbol = s,
                session = day is { } dd ? EasternClock.FormatDate(dd) : null,
                prevClose = close, open = MarketData.Dec(prev, "o"), high = MarketData.Dec(prev, "h"), low = MarketData.Dec(prev, "l"),
                volume = MarketData.Dec(prev, "v"), vwap = MarketData.Dec(prev, "vw"),
                last, change, changePercent = change is { } c && close is > 0 ? c / close * 100 : null,
            });
        }
        var human = table.Render() + (missing.Count > 0 ? $"\n\nNo data for: {string.Join(", ", missing)}" : "");
        return OperationResult.Ok(new { feed = MarketData.Feed, closes = data, missing }, human);
    }
}
