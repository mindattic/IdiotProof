using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketSnapshotArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. AAPL MSFT.")]
    public string[] Symbols { get; init; } = [];
}

/// <summary>
/// Alpaca snapshots (<c>/v2/stocks/snapshots</c>): latest trade, latest quote, today's daily bar
/// and the previous daily bar for several stocks, as one aligned table.
/// </summary>
[Operation("market snapshot", Summary = "One-line snapshot per stock: last, change vs prior close, bid/ask, today's open/high/low/volume.", Risk = OperationRisk.Read)]
[Example("idiotproof market snapshot AAPL")]
[Example("idiotproof market snapshot SPY QQQ NVDA TSLA")]
public sealed class MarketSnapshotOperation : IOperation<MarketSnapshotArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketSnapshotArgs args, OperationContext ctx)
    {
        var symbols = MarketData.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol, e.g. `idiotproof market snapshot AAPL`.");

        await using var api = await MarketData.OpenAsync(ctx);
        var root = await api.GetDataAsync("/v2/stocks/snapshots",
            MarketData.Q(("symbols", string.Join(',', symbols)), ("feed", MarketData.Feed)), ctx.CancellationToken);
        var snaps = MarketData.BySymbol(root, null);

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Symbol").Column("Last", Align.Right).Column("Chg", Align.Right).Column("Chg %", Align.Right)
            .Column("Bid", Align.Right).Column("Ask", Align.Right).Column("Open", Align.Right).Column("High", Align.Right)
            .Column("Low", Align.Right).Column("Volume", Align.Right).Column("Prev close", Align.Right).Column("As of");
        var data = new List<object>();
        var missing = new List<string>();
        foreach (var s in symbols)
        {
            if (!snaps.TryGetValue(s, out var snap) || snap.ValueKind != JsonValueKind.Object)
            {
                missing.Add(s);
                continue;
            }
            JsonElement trade = MarketData.Obj(snap, "latestTrade"), quote = MarketData.Obj(snap, "latestQuote"),
                day = MarketData.Obj(snap, "dailyBar"), prev = MarketData.Obj(snap, "prevDailyBar"), minute = MarketData.Obj(snap, "minuteBar");
            decimal? last = MarketData.Dec(trade, "p"), prevClose = MarketData.Dec(prev, "c");
            decimal? change = last - prevClose;
            var asOf = MarketData.Time(trade, "t");
            table.Row(s, MarketData.Px(last), MarketData.Signed(change), MarketData.Pct(change, prevClose),
                MarketData.Px(MarketData.Dec(quote, "bp")), MarketData.Px(MarketData.Dec(quote, "ap")),
                MarketData.Px(MarketData.Dec(day, "o")), MarketData.Px(MarketData.Dec(day, "h")), MarketData.Px(MarketData.Dec(day, "l")),
                MarketData.Compact(MarketData.Dec(day, "v")), MarketData.Px(prevClose), MarketData.Et(asOf));
            data.Add(new
            {
                symbol = s,
                latestTrade = new { price = last, size = MarketData.Dec(trade, "s"), exchange = MarketData.Str(trade, "x"), time = MarketData.EtOffset(asOf) },
                latestQuote = new
                {
                    bid = MarketData.Dec(quote, "bp"), bidSize = MarketData.Dec(quote, "bs"),
                    ask = MarketData.Dec(quote, "ap"), askSize = MarketData.Dec(quote, "as"), time = MarketData.EtOffset(MarketData.Time(quote, "t")),
                },
                minuteBar = BarData(minute), dailyBar = BarData(day), prevDailyBar = BarData(prev),
                change, changePercent = change is { } c && prevClose is > 0 ? c / prevClose * 100 : null,
            });
        }
        var human = $"feed {MarketData.Feed}\n" + table.Render() + (missing.Count > 0 ? $"\n\nNo data for: {string.Join(", ", missing)}" : "");
        return OperationResult.Ok(new { feed = MarketData.Feed, snapshots = data, missing }, human);
    }

    internal static object? BarData(JsonElement b) => b.ValueKind != JsonValueKind.Object ? null : new
    {
        time = MarketData.EtOffset(MarketData.Time(b, "t")),
        open = MarketData.Dec(b, "o"), high = MarketData.Dec(b, "h"), low = MarketData.Dec(b, "l"), close = MarketData.Dec(b, "c"),
        volume = MarketData.Dec(b, "v"), trades = MarketData.Dec(b, "n"), vwap = MarketData.Dec(b, "vw"),
    };
}

/// <summary>Which latest-data endpoint <c>market latest</c> reads.</summary>
public enum LatestKind
{
    Bar,
    Trade,
    Quote,
}

public sealed record MarketLatestArgs
{
    [Argument(0, Name = "kind", Description = "bar (latest minute bar), trade or quote.")]
    public LatestKind Kind { get; init; } = LatestKind.Trade;

    [Argument(1, Name = "symbols", Description = "One or more tickers, e.g. AAPL MSFT.")]
    public string[] Symbols { get; init; } = [];
}

/// <summary>
/// The latest minute bar, trade or quote for several stocks (<c>/v2/stocks/{bars|trades|quotes}/latest</c>)
/// as one aligned table.
/// </summary>
[Operation("market latest", Summary = "Latest minute bar, trade or quote for one or more stocks (times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market latest bar AAPL MSFT")]
[Example("idiotproof market latest trade NVDA")]
[Example("idiotproof market latest quote SPY QQQ")]
public sealed class MarketLatestOperation : IOperation<MarketLatestArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketLatestArgs args, OperationContext ctx)
    {
        var symbols = MarketData.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol, e.g. `idiotproof market latest trade AAPL`.");

        var plural = args.Kind switch { LatestKind.Bar => "bars", LatestKind.Quote => "quotes", _ => "trades" };
        await using var api = await MarketData.OpenAsync(ctx);
        var root = await api.GetDataAsync($"/v2/stocks/{plural}/latest",
            MarketData.Q(("symbols", string.Join(',', symbols)), ("feed", MarketData.Feed)), ctx.CancellationToken);
        var items = MarketData.BySymbol(root, plural);

        var table = new TextTable { MaxWidth = ctx.Width }.Column("Symbol");
        switch (args.Kind)
        {
            case LatestKind.Bar:
                table.Column("Minute").Column("Open", Align.Right).Column("High", Align.Right).Column("Low", Align.Right)
                    .Column("Close", Align.Right).Column("Volume", Align.Right).Column("Trades", Align.Right).Column("VWAP", Align.Right);
                break;
            case LatestKind.Quote:
                table.Column("Time").Column("Bid", Align.Right).Column("Bid sz", Align.Right).Column("Bx").Column("Ask", Align.Right)
                    .Column("Ask sz", Align.Right).Column("Ax").Column("Spread", Align.Right);
                break;
            default:
                table.Column("Time").Column("Price", Align.Right).Column("Size", Align.Right).Column("Exch").Column("Cond");
                break;
        }

        var data = new List<object>();
        var missing = new List<string>();
        foreach (var s in symbols)
        {
            if (!items.TryGetValue(s, out var e) || e.ValueKind != JsonValueKind.Object)
            {
                missing.Add(s);
                continue;
            }
            var time = MarketData.Time(e, "t");
            switch (args.Kind)
            {
                case LatestKind.Bar:
                    table.Row(s, MarketData.Et(time), MarketData.Px(MarketData.Dec(e, "o")), MarketData.Px(MarketData.Dec(e, "h")),
                        MarketData.Px(MarketData.Dec(e, "l")), MarketData.Px(MarketData.Dec(e, "c")), MarketData.Compact(MarketData.Dec(e, "v")),
                        MarketData.Size(MarketData.Dec(e, "n")), MarketData.Px(MarketData.Dec(e, "vw")));
                    data.Add(new { symbol = s, bar = MarketSnapshotOperation.BarData(e) });
                    break;
                case LatestKind.Quote:
                    decimal? bid = MarketData.Dec(e, "bp"), ask = MarketData.Dec(e, "ap");
                    decimal? spread = bid is > 0 && ask is > 0 ? ask - bid : null;
                    table.Row(s, MarketData.TickTime(time), MarketData.Px(bid), MarketData.Size(MarketData.Dec(e, "bs")), MarketData.Str(e, "bx") ?? "",
                        MarketData.Px(ask), MarketData.Size(MarketData.Dec(e, "as")), MarketData.Str(e, "ax") ?? "", MarketData.Px(spread));
                    data.Add(new
                    {
                        symbol = s, time = MarketData.EtOffset(time), bid, bidSize = MarketData.Dec(e, "bs"), bidExchange = MarketData.Str(e, "bx"),
                        ask, askSize = MarketData.Dec(e, "as"), askExchange = MarketData.Str(e, "ax"), spread, conditions = MarketData.Strings(e, "c"),
                    });
                    break;
                default:
                    table.Row(s, MarketData.TickTime(time), MarketData.Px(MarketData.Dec(e, "p")), MarketData.Size(MarketData.Dec(e, "s")),
                        MarketData.Str(e, "x") ?? "", string.Join(" ", MarketData.Strings(e, "c")));
                    data.Add(new
                    {
                        symbol = s, time = MarketData.EtOffset(time), price = MarketData.Dec(e, "p"), size = MarketData.Dec(e, "s"),
                        exchange = MarketData.Str(e, "x"), conditions = MarketData.Strings(e, "c"), tape = MarketData.Str(e, "z"),
                    });
                    break;
            }
        }
        var kind = OperationParameter.EnumWireName(args.Kind);
        var human = $"Latest {kind}  feed {MarketData.Feed}\n" + table.Render() + (missing.Count > 0 ? $"\n\nNo data for: {string.Join(", ", missing)}" : "");
        return OperationResult.Ok(new { kind, feed = MarketData.Feed, latest = data, missing }, human);
    }
}
