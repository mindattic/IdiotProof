using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Trading;

namespace IdiotProof.Operations.Orders;

public sealed record OrdersReplaceArgs
{
    [Argument(0, Name = "id", Description = "The working order to change (from `orders list`).")]
    public string Id { get; init; } = "";

    [Option("qty", Description = "New share count (fractions allowed).")]
    public decimal? Quantity { get; init; }

    [Option("limit", Description = "New limit price.", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("stop-price", Description = "New stop trigger price.")]
    public decimal? StopPrice { get; init; }

    [Option("trail", Description = "New trailing distance, in the order's own unit (percent or dollars).")]
    public decimal? Trail { get; init; }

    [Option("tif", Description = "New time in force: day, gtc, opg, cls, ioc or fok.")]
    public string? TimeInForce { get; init; }

    [Option("client-id", Description = "New client order id for the replacement order.")]
    public string? ClientId { get; init; }

    [Option("stop", Description = "Protective stop for the position, needed when --qty raises an entry's size (the Risk Guardian checks the whole new size).")]
    public decimal? Stop { get; init; }

    [Option("mode", Description = "The account the order is in: paper (default) or live (needs confirmation).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

[Operation("orders replace", Summary = "Change a working order's quantity, limit/stop/trail, TIF or client id (Alpaca PATCH; it gets a new id). Through the gateway; live needs a code.", Risk = OperationRisk.Money)]
[Example("idiotproof orders replace 61e69015-8549-4bfd-b9c3-01e75843f47d --limit 131.50")]
[Example("idiotproof orders replace 61e69015-8549-4bfd-b9c3-01e75843f47d --qty 5 --tif gtc --mode live")]
public sealed class OrdersReplaceOperation : IOperation<OrdersReplaceArgs>
{
    public async Task<OperationResult> ExecuteAsync(OrdersReplaceArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var id = args.Id.Trim();
        if (id.Length == 0) return OperationResult.Usage("Name the order id.");
        if (args.Mode == BrokerMode.Sandbox) return OperationResult.Usage("Sandbox orders can't be replaced; cancel and place again.");
        if (args.Quantity is null && args.Limit is null && args.StopPrice is null && args.Trail is null
            && string.IsNullOrWhiteSpace(args.TimeInForce) && string.IsNullOrWhiteSpace(args.ClientId))
            return OperationResult.Usage("Say what to change: --qty, --limit, --stop-price, --trail, --tif or --client-id.");

        var scope = args.Mode == BrokerMode.Live ? AccountScope.Live : AccountScope.Paper;
        var (_, order, why) = await OrderViews.FindAsync(ctx, scope, id, null);
        if (order is null) return OperationResult.Fail($"No order {id} in the {args.Mode.ToString().ToLowerInvariant()} account ({why}).");
        if (!order.IsWorking) return OperationResult.Fail($"Order {id} is {order.Status}; only working orders can be replaced.");
        if (args.Limit is not null && order.Type is not ("limit" or "stop_limit")) return OperationResult.Usage($"Order {id} is a {order.Type} order; it has no limit price.");
        if (args.StopPrice is not null && order.Type is not ("stop" or "stop_limit")) return OperationResult.Usage($"Order {id} is a {order.Type} order; it has no stop price.");
        if (args.Trail is not null && order.Type != "trailing_stop") return OperationResult.Usage($"Order {id} is a {order.Type} order; it has no trail.");

        var (whole, fractional) = OrderFlow.Size(args.Quantity);
        var trailIsPrice = order.TrailPrice is not null && order.TrailPercent is null;
        var intent = new OrderIntent
        {
            OwnerUserId = owner,
            Mode = args.Mode,
            Purpose = PurposeOf(order),
            ReplacedQuantity = order.Qty,
            ReferencePrice = args.Limit ?? order.LimitPrice ?? args.StopPrice ?? order.StopPrice,
            StopLossPrice = args.Stop,
            Origin = OrderOrigin.Manual,
            Action = OrderAction.Replace,
            TargetOrderId = id,
            Order = new OrderRequest
            {
                Symbol = order.Symbol,
                Side = order.Side == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Quantity = whole,
                FractionalQuantity = fractional,
                LimitPrice = args.Limit,
                StopPrice = args.StopPrice,
                TrailPercent = trailIsPrice ? null : args.Trail,
                TrailPrice = trailIsPrice ? args.Trail : null,
                TimeInForce = string.IsNullOrWhiteSpace(args.TimeInForce) ? "" : OrderFlow.Tif(args.TimeInForce),
                ClientOrderId = string.IsNullOrWhiteSpace(args.ClientId) ? null : args.ClientId.Trim(),
            },
            Note = args.Note ?? OrderFlow.DefaultNote(ctx, "orders replace"),
        };
        return await TradeFlow.SubmitOrConfirmAsync(intent, ctx);
    }

    /// <summary>
    /// Whether the working order opens risk (an entry) or reduces it (an exit): its position
    /// intent when Alpaca reports one (<c>sell_to_open</c> is a short entry, <c>buy_to_close</c> a
    /// cover), else its side — a buy opens, a sell closes.
    /// </summary>
    internal static OrderPurpose PurposeOf(OrderView order) => order.PositionIntent switch
    {
        { } pi when pi.EndsWith("_to_open", StringComparison.OrdinalIgnoreCase) => OrderPurpose.Entry,
        { } pi when pi.EndsWith("_to_close", StringComparison.OrdinalIgnoreCase) => OrderPurpose.Exit,
        _ => order.Side == "buy" ? OrderPurpose.Entry : OrderPurpose.Exit,
    };
}
