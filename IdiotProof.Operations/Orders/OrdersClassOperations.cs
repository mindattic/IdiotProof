using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Trading;

namespace IdiotProof.Operations.Orders;

/// <summary>Arguments for an advanced (bracket / OCO / OTO) order: the parent plus its take-profit and stop-loss children.</summary>
public sealed record OrderClassArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Argument(1, Name = "side", Description = "buy or sell (the parent's side; an OCO's side closes the position: sell exits a long).")]
    public OrderSide Side { get; init; }

    [Argument(2, Name = "qty", Description = "Whole shares.")]
    public int Quantity { get; init; }

    [Option("limit", Description = "Entry limit price (bracket/OTO). Without it the entry is a market order.", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("take-profit", Description = "Take-profit child's limit price.")]
    public decimal? TakeProfit { get; init; }

    [Option("stop-loss", Description = "Stop-loss child's stop price.")]
    public decimal? StopLoss { get; init; }

    [Option("stop-loss-limit", Description = "Make the stop-loss a stop-limit with this limit price.")]
    public decimal? StopLossLimit { get; init; }

    [Option("tif", Description = "Time in force: day (default) or gtc.")]
    public string TimeInForce { get; init; } = "day";

    [Option("client-id", Description = "Your own id for the parent order (Alpaca client_order_id).")]
    public string? ClientId { get; init; }

    [Option("stop", Description = "Planned protective stop for the Risk Guardian when the order has no stop-loss child (OTO with only a take-profit).", Short = 's')]
    public decimal? Stop { get; init; }

    [Option("mode", Description = "Where it routes: sandbox (simulator, the default), paper, or live (needs confirmation).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

/// <summary>Builds a bracket / OCO / OTO intent; the gateway then checks the class rules and places it.</summary>
internal static class OrderClassFlow
{
    public static async Task<OperationResult> RunAsync(OrderClass cls, OrderClassArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var symbol = OrderFlow.Symbol(args.Symbol);
        if (args.Quantity <= 0) throw OperationException.Usage("The quantity must be a positive whole number of shares.");
        var tif = OrderFlow.Tif(args.TimeInForce);
        if (tif is not ("DAY" or "GTC")) throw OperationException.Usage("Bracket, OCO and OTO orders are day or gtc.");
        if (args.StopLossLimit is not null && args.StopLoss is null) throw OperationException.Usage("--stop-loss-limit needs --stop-loss.");
        switch (cls)
        {
            case OrderClass.Bracket when args.TakeProfit is null || args.StopLoss is null:
                throw OperationException.Usage("A bracket needs --take-profit and --stop-loss.");
            case OrderClass.Oco when args.TakeProfit is null || args.StopLoss is null:
                throw OperationException.Usage("An OCO needs --take-profit and --stop-loss.");
            case OrderClass.Oco when args.Limit is not null:
                throw OperationException.Usage("An OCO has no entry; its limit is the --take-profit.");
            case OrderClass.Oto when (args.TakeProfit is null) == (args.StopLoss is null):
                throw OperationException.Usage("An OTO takes exactly one of --take-profit or --stop-loss.");
        }

        var exit = cls == OrderClass.Oco;
        var order = new OrderRequest
        {
            Symbol = symbol,
            Quantity = args.Quantity,
            Side = args.Side,
            Type = exit || args.Limit is not null ? OrderType.Limit : OrderType.Market,
            LimitPrice = exit ? null : args.Limit,
            TimeInForce = tif,
            ClientOrderId = string.IsNullOrWhiteSpace(args.ClientId) ? null : args.ClientId.Trim(),
            OrderClass = cls,
            TakeProfit = args.TakeProfit is { } tp ? new TakeProfitLeg { LimitPrice = tp } : null,
            StopLoss = args.StopLoss is { } sl ? new StopLossLeg { StopPrice = sl, LimitPrice = args.StopLossLimit } : null,
        };
        var intent = new OrderIntent
        {
            OwnerUserId = owner,
            Mode = args.Mode,
            Purpose = exit ? OrderPurpose.Exit : OrderPurpose.Entry,
            Origin = OrderOrigin.Manual,
            Order = order,
            ReferencePrice = await OrderFlow.ReferencePriceAsync(symbol, args.Limit, ctx),
            StopLossPrice = args.Stop,
            Note = args.Note ?? OrderFlow.DefaultNote(ctx, "orders " + cls.ToString().ToLowerInvariant()),
        };
        return await TradeFlow.SubmitOrConfirmAsync(intent, ctx);
    }
}

[Operation("orders bracket", Summary = "Entry plus a take-profit and a stop-loss that cancel each other (Alpaca bracket). The stop-loss is the Risk Guardian's stop.", Risk = OperationRisk.Money)]
[Example("idiotproof orders bracket NVDA buy 10 --limit 131 --take-profit 140 --stop-loss 127 --tif gtc --mode paper")]
[Example("idiotproof orders bracket AAPL buy 5 --take-profit 240 --stop-loss 220 --stop-loss-limit 219.5", Description = "Market entry; the stop-loss is a stop-limit.")]
public sealed class OrdersBracketOperation : IOperation<OrderClassArgs>
{
    public Task<OperationResult> ExecuteAsync(OrderClassArgs args, OperationContext ctx) => OrderClassFlow.RunAsync(OrderClass.Bracket, args, ctx);
}

[Operation("orders oco", Summary = "Exit a held position with a take-profit limit and a stop-loss; whichever fills cancels the other (Alpaca OCO).", Risk = OperationRisk.Money)]
[Example("idiotproof orders oco NVDA sell 10 --take-profit 140 --stop-loss 127 --tif gtc --mode paper")]
public sealed class OrdersOcoOperation : IOperation<OrderClassArgs>
{
    public Task<OperationResult> ExecuteAsync(OrderClassArgs args, OperationContext ctx) => OrderClassFlow.RunAsync(OrderClass.Oco, args, ctx);
}

[Operation("orders oto", Summary = "Entry that, once filled, triggers ONE child: a take-profit or a stop-loss (Alpaca OTO).", Risk = OperationRisk.Money)]
[Example("idiotproof orders oto NVDA buy 10 --limit 131 --stop-loss 127 --mode paper")]
[Example("idiotproof orders oto NVDA buy 10 --limit 131 --take-profit 140 --stop 127", Description = "Take-profit child; --stop is the planned stop for the Risk Guardian.")]
public sealed class OrdersOtoOperation : IOperation<OrderClassArgs>
{
    public Task<OperationResult> ExecuteAsync(OrderClassArgs args, OperationContext ctx) => OrderClassFlow.RunAsync(OrderClass.Oto, args, ctx);
}
