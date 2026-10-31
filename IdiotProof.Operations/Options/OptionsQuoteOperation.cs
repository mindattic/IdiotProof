using IdiotProof.Operations.Rendering;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

/// <summary>Arguments for a read of one option contract by OCC symbol.</summary>
public sealed record OptionsOccArgs
{
    [Argument(0, Name = "occ", Description = "OCC option symbol: ROOT + YYMMDD + C|P + strike x 1000 (8 digits), e.g. AAPL261120C00250000.")]
    public string Occ { get; init; } = "";

    [Option("feed", Description = "Options data feed: indicative (default, free) or opra (paid).")]
    public OptionsFeed Feed { get; init; } = OptionsFeed.Indicative;
}

/// <summary>
/// The latest quote for one contract from its snapshot (<c>GET /v1beta1/options/snapshots</c>):
/// bid/ask with sizes, mid, spread, last trade, and Alpaca's IV and delta when it sent them.
/// </summary>
[Operation("options quote", Summary = "Latest bid/ask, mid, spread and last trade for one option contract (OCC symbol).", Risk = OperationRisk.Read)]
[Example("idiotproof options quote AAPL261120C00250000")]
[Example("idiotproof options quote SPY261218P00600000 --feed opra")]
public sealed class OptionsQuoteOperation : IOperation<OptionsOccArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsOccArgs args, OperationContext ctx)
    {
        var contract = RequireOcc(args.Occ);
        var occ = contract.OccSymbol;
        await using var api = await DataApiAsync(ctx);
        var snap = await RequireSnapshotAsync(api, occ, args.Feed, ctx.CancellationToken);
        var q = ParseSnapshot(occ, snap);
        var quote = Obj(snap, "latestQuote");
        var trade = Obj(snap, "latestTrade");
        decimal? bidSize = quote is { } qq ? Dec(qq, "bs") : null, askSize = quote is { } qa ? Dec(qa, "as") : null;
        decimal? spread = q.Bid > 0 && q.Ask > 0 ? q.Ask - q.Bid : null;
        var mid = q.Mid > 0 ? q.Mid : (decimal?)null;
        DateTime? tradeTime = trade is { } tt ? Time(tt, "t") : null;
        decimal? tradeSize = trade is { } ts ? Dec(ts, "s") : null;

        var block = new KeyValueBlock { Ansi = Ansi.For(ctx) }
            .Add("Contract", $"{occ}  ({contract.DisplayName}, {DaysToExpiry(contract, ctx.UtcNow)} DTE)")
            .Add("Bid", $"{Money(q.Bid)} x {Fmt.Number(bidSize ?? 0)}")
            .Add("Ask", $"{Money(q.Ask)} x {Fmt.Number(askSize ?? 0)}")
            .Add("Mid", mid is { } m ? $"{Money(m)}  ({Fmt.Money(m * contract.Multiplier)} per contract)" : "-")
            .Add("Spread", spread is { } sp && mid is { } md && md > 0 ? $"{Money(sp)}  ({Fmt.Percent(sp / md, 1)} of mid)" : "-")
            .Add("Last trade", q.LastTrade is { } lt ? $"{Money(lt)} x {Fmt.Number(tradeSize ?? 0)} at {Time(tradeTime)}" : "-")
            .Add("Quoted at", Time(q.TimestampUtc))
            .Add("IV", Iv(q.ImpliedVolatility))
            .Add("Delta", Greek(q.Greeks?.Delta))
            .Add("Feed", FeedName(args.Feed));

        var data = new
        {
            occ,
            underlying = contract.UnderlyingSymbol,
            type = contract.Right.ToString().ToLowerInvariant(),
            strike = contract.Strike,
            expiration = Date(contract.Expiration),
            dte = DaysToExpiry(contract, ctx.UtcNow),
            bid = q.Bid, bidSize, ask = q.Ask, askSize, mid, spread,
            last = q.LastTrade, lastSize = tradeSize, lastTimeUtc = tradeTime,
            quoteTimeUtc = q.TimestampUtc > DateTime.MinValue ? q.TimestampUtc : (DateTime?)null,
            impliedVolatility = q.ImpliedVolatility,
            delta = q.Greeks?.Delta,
            feed = FeedName(args.Feed),
        };
        return OperationResult.Ok(data, block.Render());
    }
}
