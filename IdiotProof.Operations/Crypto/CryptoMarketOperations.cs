using System.Globalization;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Crypto;

/// <summary>Arguments for the crypto reads that take just a pair.</summary>
public sealed record CryptoPairArgs
{
    [Argument(0, Name = "pair", Description = "Crypto pair, e.g. BTC/USD (BTC-USD and BTCUSD work too).")]
    public string Pair { get; init; } = "";
}

public sealed record CryptoBarsArgs
{
    [Argument(0, Name = "pair", Description = "Crypto pair, e.g. BTC/USD.")]
    public string Pair { get; init; } = "";

    [Option("timeframe", Description = "Bar size in Alpaca's form: 1Min, 5Min, 15Min, 1Hour, 4Hour, 1Day, 1Week (default 1Hour).")]
    public string Timeframe { get; init; } = "1Hour";

    [Option("hours", Description = "How far back to look, in hours (default 24).")]
    public int Hours { get; init; } = 24;

    [Option("limit", Description = "Show at most this many bars, the most recent (default 48).")]
    public int Limit { get; init; } = 48;
}

public sealed record CryptoTicksArgs
{
    [Argument(0, Name = "pair", Description = "Crypto pair, e.g. BTC/USD.")]
    public string Pair { get; init; } = "";

    [Option("minutes", Description = "How far back to look, in minutes (default 5).")]
    public int Minutes { get; init; } = 5;

    [Option("limit", Description = "Show at most this many, the most recent (default 30).")]
    public int Limit { get; init; } = 30;
}

public sealed record CryptoOrderbookArgs
{
    [Argument(0, Name = "pair", Description = "Crypto pair, e.g. BTC/USD.")]
    public string Pair { get; init; } = "";

    [Option("depth", Description = "Price levels per side (default 10).")]
    public int Depth { get; init; } = 10;
}

/// <summary>The Market Data client every crypto read uses: the owner's paper keys, else the Hub's.</summary>
internal static class CryptoReads
{
    public static Task<AlpacaApi> DataAsync(OperationContext ctx) =>
        ctx.Get<AlpacaApiFactory>().ForDataAsync(ctx.OwnerId, ctx.CancellationToken);

    public static void CheckLimit(int limit, int max = 1000)
    {
        if (limit < 1 || limit > max) throw OperationException.Usage($"--limit must be 1-{max}.");
    }
}

[Operation("crypto bars", Summary = "OHLCV bars for a crypto pair from Alpaca's US crypto feed (times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof crypto bars BTC/USD")]
[Example("idiotproof crypto bars ETH/USD --timeframe 5Min --hours 2")]
public sealed class CryptoBarsOperation : IOperation<CryptoBarsArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoBarsArgs args, OperationContext ctx)
    {
        var pair = CryptoData.RequirePair(args.Pair, "idiotproof crypto bars BTC/USD");
        if (args.Hours is < 1 or > 24 * 366) return OperationResult.Usage("--hours must be 1-8784.");
        CryptoReads.CheckLimit(args.Limit, 10_000);

        await using var api = await CryptoReads.DataAsync(ctx);
        var bars = await CryptoData.PagedAsync(api, "bars", pair, new()
        {
            ["timeframe"] = args.Timeframe.Trim(),
            ["start"] = CryptoData.Rfc3339(ctx.UtcNow.AddHours(-args.Hours)),
            ["end"] = CryptoData.Rfc3339(ctx.UtcNow),
            ["limit"] = "10000",
        }, 100_000, ctx.CancellationToken);
        if (bars.Count == 0) return OperationResult.Ok(new { pair, bars = Array.Empty<object>() }, $"No {args.Timeframe} bars for {pair} in the last {args.Hours}h.");

        var shown = bars.TakeLast(args.Limit).Select(b => new
        {
            time = CryptoData.Time(b),
            open = CryptoData.Num(b, "o"), high = CryptoData.Num(b, "h"), low = CryptoData.Num(b, "l"), close = CryptoData.Num(b, "c"),
            volume = CryptoData.Num(b, "v"), trades = CryptoData.Num(b, "n"), vwap = CryptoData.Num(b, "vw"),
        }).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Time (ET)").Column("Open", Align.Right).Column("High", Align.Right).Column("Low", Align.Right)
            .Column("Close", Align.Right).Column("Volume", Align.Right).Column("Trades", Align.Right).Column("VWAP", Align.Right);
        foreach (var b in shown)
            table.Row(Fmt.Et(b.time), CryptoData.Price(b.open), CryptoData.Price(b.high), CryptoData.Price(b.low), CryptoData.Price(b.close),
                CryptoData.Size(b.volume), b.trades is { } n ? n.ToString("0", CultureInfo.InvariantCulture) : "-", CryptoData.Price(b.vwap));

        var first = shown[0];
        var last = shown[^1];
        var change = last.close - first.open;
        var header = $"{pair} {args.Timeframe} bars, {shown.Count} shown of {bars.Count}"
            + (change is { } c && first.open is { } o && o != 0 ? $"  ({(c >= 0 ? "+" : "")}{CryptoData.Price(c)} / {Fmt.Percent(c / o, 2, signed: true)})" : "");
        return OperationResult.Ok(new { pair, timeframe = args.Timeframe, bars = shown }, header + "\n" + table.Render());
    }
}

[Operation("crypto trades", Summary = "Recent trade prints for a crypto pair (price, size, taker side).", Risk = OperationRisk.Read)]
[Example("idiotproof crypto trades BTC/USD")]
[Example("idiotproof crypto trades SOL/USD --minutes 30 --limit 100")]
public sealed class CryptoTradesOperation : IOperation<CryptoTicksArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoTicksArgs args, OperationContext ctx)
    {
        var pair = CryptoData.RequirePair(args.Pair, "idiotproof crypto trades BTC/USD");
        if (args.Minutes is < 1 or > 1440) return OperationResult.Usage("--minutes must be 1-1440.");
        CryptoReads.CheckLimit(args.Limit);

        await using var api = await CryptoReads.DataAsync(ctx);
        var trades = await CryptoData.PagedAsync(api, "trades", pair, new()
        {
            ["start"] = CryptoData.Rfc3339(ctx.UtcNow.AddMinutes(-args.Minutes)),
            ["end"] = CryptoData.Rfc3339(ctx.UtcNow),
            ["limit"] = "10000",
        }, 50_000, ctx.CancellationToken);
        if (trades.Count == 0) return OperationResult.Ok(new { pair, trades = Array.Empty<object>() }, $"No {pair} trades in the last {args.Minutes} minutes.");

        var shown = trades.TakeLast(args.Limit).Select(t => new
        {
            time = CryptoData.Time(t), price = CryptoData.Num(t, "p"), size = CryptoData.Num(t, "s"),
            takerSide = CryptoData.Str(t, "tks") switch { "B" => "buy", "S" => "sell", var x => x }, id = CryptoData.Str(t, "i"),
        }).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Time (ET)").Column("Price", Align.Right).Column("Size", Align.Right).Column("Taker");
        foreach (var t in shown)
            table.Row(CryptoData.EtSeconds(t.time), CryptoData.Price(t.price), CryptoData.Size(t.size), t.takerSide ?? "-");
        var volume = shown.Sum(t => t.size ?? 0);
        return OperationResult.Ok(new { pair, trades = shown },
            $"{pair}: {shown.Count} of {trades.Count} trades in the last {args.Minutes} min, volume {CryptoData.Size(volume)}\n{table.Render()}");
    }
}

[Operation("crypto quotes", Summary = "Recent best bid/ask quotes for a crypto pair, with the spread.", Risk = OperationRisk.Read)]
[Example("idiotproof crypto quotes BTC/USD")]
public sealed class CryptoQuotesOperation : IOperation<CryptoTicksArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoTicksArgs args, OperationContext ctx)
    {
        var pair = CryptoData.RequirePair(args.Pair, "idiotproof crypto quotes BTC/USD");
        if (args.Minutes is < 1 or > 1440) return OperationResult.Usage("--minutes must be 1-1440.");
        CryptoReads.CheckLimit(args.Limit);

        await using var api = await CryptoReads.DataAsync(ctx);
        var quotes = await CryptoData.PagedAsync(api, "quotes", pair, new()
        {
            ["start"] = CryptoData.Rfc3339(ctx.UtcNow.AddMinutes(-args.Minutes)),
            ["end"] = CryptoData.Rfc3339(ctx.UtcNow),
            ["limit"] = "10000",
        }, 50_000, ctx.CancellationToken);
        if (quotes.Count == 0) return OperationResult.Ok(new { pair, quotes = Array.Empty<object>() }, $"No {pair} quotes in the last {args.Minutes} minutes.");

        var shown = quotes.TakeLast(args.Limit).Select(q => new
        {
            time = CryptoData.Time(q),
            bid = CryptoData.Num(q, "bp"), bidSize = CryptoData.Num(q, "bs"),
            ask = CryptoData.Num(q, "ap"), askSize = CryptoData.Num(q, "as"),
        }).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Time (ET)").Column("Bid size", Align.Right).Column("Bid", Align.Right).Column("Ask", Align.Right)
            .Column("Ask size", Align.Right).Column("Spread", Align.Right);
        foreach (var q in shown)
            table.Row(CryptoData.EtSeconds(q.time), CryptoData.Size(q.bidSize), CryptoData.Price(q.bid), CryptoData.Price(q.ask),
                CryptoData.Size(q.askSize), CryptoData.Price(q.ask - q.bid));
        return OperationResult.Ok(new { pair, quotes = shown }, $"{pair}: {shown.Count} of {quotes.Count} quotes in the last {args.Minutes} min\n{table.Render()}");
    }
}

[Operation("crypto latest", Summary = "The latest trade, quote and minute bar for a crypto pair.", Risk = OperationRisk.Read)]
[Example("idiotproof crypto latest BTC/USD")]
public sealed class CryptoLatestOperation : IOperation<CryptoPairArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoPairArgs args, OperationContext ctx)
    {
        var pair = CryptoData.RequirePair(args.Pair, "idiotproof crypto latest BTC/USD");
        await using var api = await CryptoReads.DataAsync(ctx);
        var q = new Dictionary<string, string?> { ["symbols"] = pair };
        var trade = CryptoData.ForPair(await api.GetDataAsync($"{CryptoData.Root}/latest/trades", q, ctx.CancellationToken), "trades", pair);
        var quote = CryptoData.ForPair(await api.GetDataAsync($"{CryptoData.Root}/latest/quotes", q, ctx.CancellationToken), "quotes", pair);
        var bar = CryptoData.ForPair(await api.GetDataAsync($"{CryptoData.Root}/latest/bars", q, ctx.CancellationToken), "bars", pair);
        if (trade is null && quote is null && bar is null) return OperationResult.Fail($"Alpaca has no crypto data for {pair}. Check the pair (e.g. BTC/USD).");

        var block = new KeyValueBlock().Add("Pair:", pair);
        if (trade is { } t)
            block.Add("Last trade:", $"{CryptoData.Price(CryptoData.Num(t, "p"))} x {CryptoData.Size(CryptoData.Num(t, "s"))}  at {CryptoData.EtSeconds(CryptoData.Time(t))}");
        if (quote is { } qt)
        {
            decimal? bid = CryptoData.Num(qt, "bp"), ask = CryptoData.Num(qt, "ap");
            block.Add("Bid / ask:", $"{CryptoData.Price(bid)} x {CryptoData.Size(CryptoData.Num(qt, "bs"))}  /  {CryptoData.Price(ask)} x {CryptoData.Size(CryptoData.Num(qt, "as"))}");
            block.Add("Spread:", $"{CryptoData.Price(ask - bid)}" + (bid is { } b && b != 0 && ask is { } a ? $"  ({Fmt.Percent((a - b) / b, 3)})" : ""));
        }
        if (bar is { } br)
            block.Add("Minute bar:", $"O {CryptoData.Price(CryptoData.Num(br, "o"))}  H {CryptoData.Price(CryptoData.Num(br, "h"))}  L {CryptoData.Price(CryptoData.Num(br, "l"))}  C {CryptoData.Price(CryptoData.Num(br, "c"))}  V {CryptoData.Size(CryptoData.Num(br, "v"))}  at {Fmt.Et(CryptoData.Time(br))}");

        var data = new
        {
            pair,
            trade = trade is { } td ? new { time = CryptoData.Time(td), price = CryptoData.Num(td, "p"), size = CryptoData.Num(td, "s") } : null,
            quote = quote is { } qd ? new { time = CryptoData.Time(qd), bid = CryptoData.Num(qd, "bp"), bidSize = CryptoData.Num(qd, "bs"), ask = CryptoData.Num(qd, "ap"), askSize = CryptoData.Num(qd, "as") } : null,
            bar = bar is { } bd ? new { time = CryptoData.Time(bd), open = CryptoData.Num(bd, "o"), high = CryptoData.Num(bd, "h"), low = CryptoData.Num(bd, "l"), close = CryptoData.Num(bd, "c"), volume = CryptoData.Num(bd, "v") } : null,
        };
        return OperationResult.Ok(data, block.Render());
    }
}

[Operation("crypto orderbook", Summary = "The latest order book for a crypto pair: bids and asks side by side.", Risk = OperationRisk.Read)]
[Example("idiotproof crypto orderbook BTC/USD")]
[Example("idiotproof crypto orderbook ETH/USD --depth 20")]
public sealed class CryptoOrderbookOperation : IOperation<CryptoOrderbookArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoOrderbookArgs args, OperationContext ctx)
    {
        var pair = CryptoData.RequirePair(args.Pair, "idiotproof crypto orderbook BTC/USD");
        if (args.Depth is < 1 or > 200) return OperationResult.Usage("--depth must be 1-200.");
        await using var api = await CryptoReads.DataAsync(ctx);
        var root = await api.GetDataAsync($"{CryptoData.Root}/latest/orderbooks", new Dictionary<string, string?> { ["symbols"] = pair }, ctx.CancellationToken);
        if (CryptoData.ForPair(root, "orderbooks", pair) is not { } book) return OperationResult.Fail($"Alpaca has no order book for {pair}.");

        static List<(decimal? Price, decimal? Size)> Side(JsonElement book, string side, bool descending)
        {
            if (!book.TryGetProperty(side, out var levels) || levels.ValueKind != JsonValueKind.Array) return [];
            var list = levels.EnumerateArray().Select(l => (CryptoData.Num(l, "p"), CryptoData.Num(l, "s")));
            return [.. descending ? list.OrderByDescending(l => l.Item1) : list.OrderBy(l => l.Item1)];
        }
        var bids = Side(book, "b", descending: true).Take(args.Depth).ToList();
        var asks = Side(book, "a", descending: false).Take(args.Depth).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Bid size", Align.Right).Column("Bid", Align.Right).Column("Ask", Align.Right).Column("Ask size", Align.Right);
        for (int i = 0; i < Math.Max(bids.Count, asks.Count); i++)
        {
            var bid = i < bids.Count ? bids[i] : default;
            var ask = i < asks.Count ? asks[i] : default;
            table.Row(i < bids.Count ? CryptoData.Size(bid.Size) : "", i < bids.Count ? CryptoData.Price(bid.Price) : "",
                i < asks.Count ? CryptoData.Price(ask.Price) : "", i < asks.Count ? CryptoData.Size(ask.Size) : "");
        }

        var time = CryptoData.Time(book);
        decimal? bestBid = bids.Count > 0 ? bids[0].Price : null, bestAsk = asks.Count > 0 ? asks[0].Price : null;
        var header = $"{pair} order book at {CryptoData.EtSeconds(time)}  spread {CryptoData.Price(bestAsk - bestBid)}"
            + $"  depth bid {CryptoData.Size(bids.Sum(b => b.Size ?? 0))} / ask {CryptoData.Size(asks.Sum(a => a.Size ?? 0))}";
        var data = new
        {
            pair, time, spread = bestAsk - bestBid,
            bids = bids.Select(b => new { price = b.Price, size = b.Size }),
            asks = asks.Select(a => new { price = a.Price, size = a.Size }),
        };
        return OperationResult.Ok(data, header + "\n" + table.Render());
    }
}

[Operation("crypto snapshot", Summary = "Snapshot of a crypto pair: latest trade and quote, minute bar, today's and the prior daily bar.", Risk = OperationRisk.Read)]
[Example("idiotproof crypto snapshot BTC/USD")]
public sealed class CryptoSnapshotOperation : IOperation<CryptoPairArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoPairArgs args, OperationContext ctx)
    {
        var pair = CryptoData.RequirePair(args.Pair, "idiotproof crypto snapshot BTC/USD");
        await using var api = await CryptoReads.DataAsync(ctx);
        var root = await api.GetDataAsync($"{CryptoData.Root}/snapshots", new Dictionary<string, string?> { ["symbols"] = pair }, ctx.CancellationToken);
        if (CryptoData.ForPair(root, "snapshots", pair) is not { } snap) return OperationResult.Fail($"Alpaca has no snapshot for {pair}. Check the pair (e.g. BTC/USD).");

        static JsonElement? Part(JsonElement snap, string name) =>
            snap.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
        var trade = Part(snap, "latestTrade");
        var quote = Part(snap, "latestQuote");
        var minute = Part(snap, "minuteBar");
        var daily = Part(snap, "dailyBar");
        var prev = Part(snap, "prevDailyBar");

        var last = trade is { } t ? CryptoData.Num(t, "p") : minute is { } mb ? CryptoData.Num(mb, "c") : null;
        var prevClose = prev is { } p ? CryptoData.Num(p, "c") : null;
        var block = new KeyValueBlock().Add("Pair:", pair);
        if (last is { } l)
            block.Add("Last:", CryptoData.Price(l) + (prevClose is { } pc && pc != 0
                ? $"  {(l - pc >= 0 ? "+" : "")}{CryptoData.Price(l - pc)} ({Fmt.Percent((l - pc) / pc, 2, signed: true)}) vs prior daily close {CryptoData.Price(pc)}"
                : "") + (trade is { } tt ? $"  at {CryptoData.EtSeconds(CryptoData.Time(tt))}" : ""));
        if (quote is { } q)
            block.Add("Bid / ask:", $"{CryptoData.Price(CryptoData.Num(q, "bp"))} x {CryptoData.Size(CryptoData.Num(q, "bs"))}  /  {CryptoData.Price(CryptoData.Num(q, "ap"))} x {CryptoData.Size(CryptoData.Num(q, "as"))}");
        string Bar(JsonElement b) =>
            $"O {CryptoData.Price(CryptoData.Num(b, "o"))}  H {CryptoData.Price(CryptoData.Num(b, "h"))}  L {CryptoData.Price(CryptoData.Num(b, "l"))}  C {CryptoData.Price(CryptoData.Num(b, "c"))}  V {CryptoData.Size(CryptoData.Num(b, "v"))}  ({Fmt.Et(CryptoData.Time(b))})";
        if (minute is { } m) block.Add("Minute bar:", Bar(m));
        if (daily is { } d) block.Add("Daily bar:", Bar(d));
        if (prev is { } pd) block.Add("Prior daily:", Bar(pd));

        object? BarData(JsonElement? b) => b is { } x
            ? new { time = CryptoData.Time(x), open = CryptoData.Num(x, "o"), high = CryptoData.Num(x, "h"), low = CryptoData.Num(x, "l"), close = CryptoData.Num(x, "c"), volume = CryptoData.Num(x, "v") }
            : null;
        var data = new
        {
            pair, last, prevClose,
            change = last - prevClose,
            trade = trade is { } td ? new { time = CryptoData.Time(td), price = CryptoData.Num(td, "p"), size = CryptoData.Num(td, "s") } : null,
            quote = quote is { } qd ? new { time = CryptoData.Time(qd), bid = CryptoData.Num(qd, "bp"), bidSize = CryptoData.Num(qd, "bs"), ask = CryptoData.Num(qd, "ap"), askSize = CryptoData.Num(qd, "as") } : null,
            minuteBar = BarData(minute), dailyBar = BarData(daily), prevDailyBar = BarData(prev),
        };
        return OperationResult.Ok(data, block.Render());
    }
}
