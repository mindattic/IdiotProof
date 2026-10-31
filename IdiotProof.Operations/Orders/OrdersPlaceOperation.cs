using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Trading;

namespace IdiotProof.Operations.Orders;

/// <summary>Builds the pieces every order-placing operation in the <c>orders</c> family shares.</summary>
internal static class OrderFlow
{
    private static readonly HashSet<string> Intents = ["buy_to_open", "buy_to_close", "sell_to_open", "sell_to_close"];

    /// <summary>
    /// A share count as the model wants it: whole counts stay in the int <see cref="OrderRequest.Quantity"/>
    /// (so the wire sends an integer), anything else goes to <see cref="OrderRequest.FractionalQuantity"/>.
    /// </summary>
    public static (int Whole, decimal? Fractional) Size(decimal? qty)
    {
        if (qty is not { } q) return (0, null);
        if (q <= 0) throw OperationException.Usage("The quantity must be positive.");
        return q == decimal.Truncate(q) && q <= int.MaxValue ? ((int)q, null) : (0, q);
    }

    /// <summary>"sell-to-close" or "sell_to_close" → "sell_to_close"; null stays null; anything else is a usage error.</summary>
    public static string? PositionIntent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var wire = text.Trim().ToLowerInvariant().Replace('-', '_');
        return Intents.Contains(wire) ? wire : throw OperationException.Usage("--intent is buy-to-open, buy-to-close, sell-to-open or sell-to-close.");
    }

    /// <summary>
    /// Entry or exit: an explicit *_to_close intent is an exit and *_to_open an entry; without one a
    /// buy opens (a long) and a sell closes (a long), matching <c>trade buy</c> / <c>trade sell</c>.
    /// </summary>
    public static OrderPurpose Purpose(OrderSide side, string? intent) => intent switch
    {
        { } i when i.EndsWith("_to_close", StringComparison.Ordinal) => OrderPurpose.Exit,
        { } => OrderPurpose.Entry,
        null => side == OrderSide.Buy ? OrderPurpose.Entry : OrderPurpose.Exit,
    };

    /// <summary>Time in force as the model stores it (upper case), checked against Alpaca's six.</summary>
    public static string Tif(string? text)
    {
        var tif = (text ?? "day").Trim().ToUpperInvariant();
        return tif is "DAY" or "GTC" or "OPG" or "CLS" or "IOC" or "FOK" ? tif : throw OperationException.Usage("--tif is day, gtc, opg, cls, ioc or fok.");
    }

    public static string Symbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? throw OperationException.Usage("Name the symbol, e.g. NVDA.") : symbol.Trim().ToUpperInvariant();

    /// <summary>The last price from the Hub's feed for sizing and risk, when it has one (never fatal).</summary>
    public static async Task<decimal?> ReferencePriceAsync(string symbol, decimal? given, OperationContext ctx)
    {
        if (given is not null) return given;
        if (ctx.Services.GetService(typeof(IMarketDataFeed)) is not IMarketDataFeed feed) return null;
        try { return (await feed.GetLatestPriceAsync(symbol, ctx.CancellationToken))?.Price; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    public static string DefaultNote(OperationContext ctx, string what) => $"{ctx.Caller.ToString().ToLowerInvariant()} {what}";
}

/// <summary>Every field of an Alpaca order, for <c>orders place</c>.</summary>
public sealed record OrdersPlaceArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Argument(1, Name = "side", Description = "buy or sell.")]
    public OrderSide Side { get; init; }

    [Argument(2, Name = "qty", Description = "Shares; fractions allowed (0.25, DAY only). Leave out and pass --notional to size by dollars.", Required = false)]
    public decimal? Quantity { get; init; }

    [Option("notional", Description = "Size by dollar amount instead of shares (DAY only).")]
    public decimal? Notional { get; init; }

    [Option("type", Description = "market, limit, stop, stop-limit or trailing-stop. Default: worked out from the prices given (none = market).")]
    public OrderType? Type { get; init; }

    [Option("limit", Description = "Limit price (limit and stop-limit orders).", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("stop-price", Description = "Stop trigger price (stop and stop-limit orders).")]
    public decimal? StopPrice { get; init; }

    [Option("trail-percent", Description = "Trailing stop distance in percent.")]
    public decimal? TrailPercent { get; init; }

    [Option("trail-price", Description = "Trailing stop distance in dollars.")]
    public decimal? TrailPrice { get; init; }

    [Option("tif", Description = "Time in force: day (default), gtc, opg (at the open), cls (at the close), ioc or fok.")]
    public string TimeInForce { get; init; } = "day";

    [Option("ext", Description = "Extended hours (premarket/after-hours); limit + day only.")]
    public bool ExtendedHours { get; init; }

    [Option("client-id", Description = "Your own order id (Alpaca client_order_id, up to 128 characters).")]
    public string? ClientId { get; init; }

    [Option("intent", Description = "Position intent: buy-to-open, buy-to-close, sell-to-open or sell-to-close.")]
    public string? Intent { get; init; }

    [Option("stop", Description = "Planned protective stop for an entry (the Risk Guardian refuses entries without one).", Short = 's')]
    public decimal? Stop { get; init; }

    [Option("target", Description = "Planned take-profit (for the reward:risk check).", Short = 't')]
    public decimal? Target { get; init; }

    [Option("mode", Description = "Where it routes: sandbox (simulator, the default), paper, or live (needs confirmation).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

[Operation("orders place", Summary = "Place any single Alpaca order: market/limit/stop/stop-limit/trailing, every TIF, fractional or notional, client id, position intent. Through the gateway; live needs a code.", Risk = OperationRisk.Money)]
[Example("idiotproof orders place NVDA buy 10 --limit 131.20 --stop 127.50 --tif gtc --mode paper")]
[Example("idiotproof orders place AAPL buy 0.5 --stop 220 --mode paper", Description = "Fractional market buy (DAY only).")]
[Example("idiotproof orders place TSLA sell 5 --type trailing-stop --trail-percent 3 --tif gtc --mode paper", Description = "Trailing stop exit.")]
[Example("idiotproof orders place NVDA sell 10 --type stop-limit --stop-price 120 --limit 119.5 --client-id exit-nvda-1 --mode live")]
public sealed class OrdersPlaceOperation : IOperation<OrdersPlaceArgs>
{
    public async Task<OperationResult> ExecuteAsync(OrdersPlaceArgs args, OperationContext ctx) =>
        await TradeFlow.SubmitOrConfirmAsync(await BuildAsync(args, ctx), ctx);

    internal static async Task<OrderIntent> BuildAsync(OrdersPlaceArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var symbol = OrderFlow.Symbol(args.Symbol);
        if (args.Quantity is null && args.Notional is null) throw OperationException.Usage("Give a quantity or --notional.");
        if (args.Quantity is not null && args.Notional is not null) throw OperationException.Usage("Give a quantity or --notional, not both.");
        if (args.Notional is <= 0) throw OperationException.Usage("--notional must be positive.");
        var (whole, fractional) = OrderFlow.Size(args.Quantity);
        var intent = OrderFlow.PositionIntent(args.Intent);

        var type = args.Type ?? (args.TrailPercent is not null || args.TrailPrice is not null ? OrderType.TrailingStop
            : args.StopPrice is not null && args.Limit is not null ? OrderType.StopLimit
            : args.StopPrice is not null ? OrderType.Stop
            : args.Limit is not null ? OrderType.Limit
            : OrderType.Market);
        // Only the prices the type uses travel; a stray one is a mistake worth saying out loud.
        if (args.Limit is not null && type is not (OrderType.Limit or OrderType.StopLimit)) throw OperationException.Usage($"--limit doesn't apply to a {Wire(type)} order.");
        if (args.StopPrice is not null && type is not (OrderType.Stop or OrderType.StopLimit)) throw OperationException.Usage($"--stop-price doesn't apply to a {Wire(type)} order.");
        if ((args.TrailPercent ?? args.TrailPrice) is not null && type != OrderType.TrailingStop) throw OperationException.Usage($"--trail-percent/--trail-price don't apply to a {Wire(type)} order.");

        return new OrderIntent
        {
            OwnerUserId = owner,
            Mode = args.Mode,
            Purpose = OrderFlow.Purpose(args.Side, intent),
            Origin = OrderOrigin.Manual,
            Order = new OrderRequest
            {
                Symbol = symbol,
                Quantity = whole,
                FractionalQuantity = fractional,
                Notional = args.Notional,
                Side = args.Side,
                Type = type,
                LimitPrice = args.Limit,
                StopPrice = args.StopPrice,
                TrailPercent = args.TrailPercent,
                TrailPrice = args.TrailPrice,
                TimeInForce = OrderFlow.Tif(args.TimeInForce),
                ExtendedHours = args.ExtendedHours,
                ClientOrderId = string.IsNullOrWhiteSpace(args.ClientId) ? null : args.ClientId.Trim(),
                PositionIntent = intent,
            },
            ReferencePrice = await OrderFlow.ReferencePriceAsync(symbol, args.Limit ?? args.StopPrice, ctx),
            StopLossPrice = args.Stop,
            TakeProfitPrice = args.Target,
            Note = args.Note ?? OrderFlow.DefaultNote(ctx, "orders place"),
        };
    }

    private static string Wire(OrderType type) => OperationParameter.EnumWireName(type);
}
