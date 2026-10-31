using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketQuoteArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. AAPL MSFT (commas work too).")]
    public string[] Symbols { get; init; } = [];
}

/// <summary>
/// Latest trade and latest NBBO quote for one or more stocks from Alpaca's Market Data API
/// (<c>/v2/stocks/trades/latest</c> + <c>/v2/stocks/quotes/latest</c>) on the configured feed tier,
/// as one aligned table with ET times.
/// </summary>
[Operation("market quote", Summary = "Latest trade and bid/ask for one or more stocks (Alpaca market data, times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market quote AAPL")]
[Example("idiotproof market quote AAPL MSFT NVDA")]
public sealed class MarketQuoteOperation : IOperation<MarketQuoteArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketQuoteArgs args, OperationContext ctx)
    {
        var symbols = MarketData.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol, e.g. `idiotproof market quote AAPL`.");

        await using var api = await MarketData.OpenAsync(ctx);
        var query = MarketData.Q(("symbols", string.Join(',', symbols)), ("feed", MarketData.Feed));
        var tradesTask = api.GetDataAsync("/v2/stocks/trades/latest", query, ctx.CancellationToken);
        var quotesTask = api.GetDataAsync("/v2/stocks/quotes/latest", query, ctx.CancellationToken);
        var trades = MarketData.BySymbol(await tradesTask, "trades");
        var quotes = MarketData.BySymbol(await quotesTask, "quotes");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Symbol").Column("Last", Align.Right).Column("Size", Align.Right).Column("Trade time")
            .Column("Bid", Align.Right).Column("Bid sz", Align.Right).Column("Ask", Align.Right).Column("Ask sz", Align.Right)
            .Column("Spread", Align.Right).Column("Quote time");
        var data = new List<object>();
        foreach (var s in symbols)
        {
            var t = trades.GetValueOrDefault(s);
            var q = quotes.GetValueOrDefault(s);
            decimal? last = MarketData.Dec(t, "p"), bid = MarketData.Dec(q, "bp"), ask = MarketData.Dec(q, "ap");
            decimal? spread = bid is > 0 && ask is > 0 ? ask - bid : null;
            DateTime? tradeTime = MarketData.Time(t, "t"), quoteTime = MarketData.Time(q, "t");
            table.Row(s, MarketData.Px(last), MarketData.Size(MarketData.Dec(t, "s")), MarketData.Et(tradeTime),
                MarketData.Px(bid), MarketData.Size(MarketData.Dec(q, "bs")), MarketData.Px(ask), MarketData.Size(MarketData.Dec(q, "as")),
                MarketData.Px(spread), MarketData.Et(quoteTime));
            data.Add(new
            {
                symbol = s,
                last, lastSize = MarketData.Dec(t, "s"), tradeExchange = MarketData.Str(t, "x"), tradeTime = MarketData.EtOffset(tradeTime),
                bid, bidSize = MarketData.Dec(q, "bs"), bidExchange = MarketData.Str(q, "bx"),
                ask, askSize = MarketData.Dec(q, "as"), askExchange = MarketData.Str(q, "ax"),
                spread, quoteTime = MarketData.EtOffset(quoteTime),
                found = trades.ContainsKey(s) || quotes.ContainsKey(s),
            });
        }
        var missing = symbols.Where(s => !trades.ContainsKey(s) && !quotes.ContainsKey(s)).ToList();
        var human = $"feed {MarketData.Feed}\n" + table.Render() + (missing.Count > 0 ? $"\n\nNo data for: {string.Join(", ", missing)}" : "");
        return OperationResult.Ok(new { feed = MarketData.Feed, quotes = data }, human);
    }
}
