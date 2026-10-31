using System.Globalization;
using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketTicksArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. AAPL.")]
    public string Symbol { get; init; } = "";

    [Option("from", Description = "First ET date to include (default: the most recent ticks).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET date to include (default: now).")]
    public DateOnly? To { get; init; }

    [Option("limit", Description = "Most ticks to show (default 20, max 10000). Without --from these are the most recent.")]
    public int Limit { get; init; } = 20;
}

/// <summary>Fetches one symbol's historical trades or quotes, oldest first, newest window by default.</summary>
internal static class TickReads
{
    public static async Task<(List<JsonElement> Ticks, string? Error)> LoadAsync(MarketTicksArgs args, OperationContext ctx, string kind)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return ([], $"Name a symbol, e.g. `idiotproof market {kind} AAPL`.");
        if (args.Limit is < 1 or > 10_000) return ([], "--limit must be 1-10000.");
        if (MarketData.Range(args.From, args.To, ctx.UtcNow, TimeSpan.FromDays(5)) is not { } range)
            return ([], "--from must be on or before --to, and not in the future.");

        var newestFirst = args.From is null;
        var query = MarketData.Q(
            ("start", MarketData.Rfc3339(range.StartUtc)), ("end", MarketData.Rfc3339(range.EndUtc)),
            ("limit", Math.Min(args.Limit, 10_000).ToString(CultureInfo.InvariantCulture)),
            ("feed", MarketData.Feed), ("sort", newestFirst ? "desc" : "asc"));

        await using var api = await MarketData.OpenAsync(ctx);
        var ticks = new List<JsonElement>();
        await foreach (var t in api.GetDataPagedAsync($"/v2/stocks/{Uri.EscapeDataString(symbol)}/{kind}", kind, query, ct: ctx.CancellationToken))
        {
            ticks.Add(t);
            if (ticks.Count >= args.Limit) break;
        }
        if (newestFirst) ticks.Reverse();
        return (ticks, null);
    }
}

/// <summary>Historical trade prints for one stock (<c>/v2/stocks/{symbol}/trades</c>), times in ET to the millisecond.</summary>
[Operation("market trades", Summary = "Historical trade prints for a stock: price, size, exchange, conditions (times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market trades AAPL")]
[Example("idiotproof market trades TSLA --from 2026-10-29 --to 2026-10-29 --limit 100")]
public sealed class MarketTradesOperation : IOperation<MarketTicksArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketTicksArgs args, OperationContext ctx)
    {
        var (ticks, error) = await TickReads.LoadAsync(args, ctx, "trades");
        if (error is not null) return OperationResult.Usage(error);
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (ticks.Count == 0) return OperationResult.Ok(new { symbol, feed = MarketData.Feed, trades = Array.Empty<object>() }, $"No trades for {symbol} in that window.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Time").Column("Price", Align.Right).Column("Size", Align.Right).Column("Exch").Column("Cond").Column("Tape");
        var data = new List<object>();
        foreach (var t in ticks)
        {
            var time = MarketData.Time(t, "t");
            var conditions = MarketData.Strings(t, "c");
            table.Row(MarketData.TickTime(time), MarketData.Px(MarketData.Dec(t, "p")), MarketData.Size(MarketData.Dec(t, "s")),
                MarketData.Str(t, "x") ?? "", string.Join(" ", conditions), MarketData.Str(t, "z") ?? "");
            data.Add(new
            {
                time = MarketData.EtOffset(time), price = MarketData.Dec(t, "p"), size = MarketData.Dec(t, "s"),
                exchange = MarketData.Str(t, "x"), conditions, tape = MarketData.Str(t, "z"), id = MarketData.Dec(t, "i"),
            });
        }
        return OperationResult.Ok(new { symbol, feed = MarketData.Feed, trades = data },
            $"{symbol} trades  {ticks.Count} shown  feed {MarketData.Feed}\n{table.Render()}");
    }
}

/// <summary>Historical NBBO quotes for one stock (<c>/v2/stocks/{symbol}/quotes</c>), times in ET to the millisecond.</summary>
[Operation("market quotes", Summary = "Historical bid/ask quotes for a stock: sizes, exchanges, spread (times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market quotes AAPL")]
[Example("idiotproof market quotes SPY --from 2026-10-29 --limit 200")]
public sealed class MarketQuotesOperation : IOperation<MarketTicksArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketTicksArgs args, OperationContext ctx)
    {
        var (ticks, error) = await TickReads.LoadAsync(args, ctx, "quotes");
        if (error is not null) return OperationResult.Usage(error);
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (ticks.Count == 0) return OperationResult.Ok(new { symbol, feed = MarketData.Feed, quotes = Array.Empty<object>() }, $"No quotes for {symbol} in that window.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Time").Column("Bid", Align.Right).Column("Bid sz", Align.Right).Column("Bx")
            .Column("Ask", Align.Right).Column("Ask sz", Align.Right).Column("Ax").Column("Spread", Align.Right).Column("Cond");
        var data = new List<object>();
        foreach (var q in ticks)
        {
            var time = MarketData.Time(q, "t");
            decimal? bid = MarketData.Dec(q, "bp"), ask = MarketData.Dec(q, "ap");
            decimal? spread = bid is > 0 && ask is > 0 ? ask - bid : null;
            var conditions = MarketData.Strings(q, "c");
            table.Row(MarketData.TickTime(time), MarketData.Px(bid), MarketData.Size(MarketData.Dec(q, "bs")), MarketData.Str(q, "bx") ?? "",
                MarketData.Px(ask), MarketData.Size(MarketData.Dec(q, "as")), MarketData.Str(q, "ax") ?? "", MarketData.Px(spread), string.Join(" ", conditions));
            data.Add(new
            {
                time = MarketData.EtOffset(time), bid, bidSize = MarketData.Dec(q, "bs"), bidExchange = MarketData.Str(q, "bx"),
                ask, askSize = MarketData.Dec(q, "as"), askExchange = MarketData.Str(q, "ax"), spread, conditions, tape = MarketData.Str(q, "z"),
            });
        }
        return OperationResult.Ok(new { symbol, feed = MarketData.Feed, quotes = data },
            $"{symbol} quotes  {ticks.Count} shown  feed {MarketData.Feed}\n{table.Render()}");
    }
}
