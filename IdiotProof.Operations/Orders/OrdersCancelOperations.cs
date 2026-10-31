using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Trading;

namespace IdiotProof.Operations.Orders;

public sealed record OrdersCancelArgs
{
    [Argument(0, Name = "id", Description = "The order id (from `orders list`).")]
    public string Id { get; init; } = "";

    [Option("mode", Description = "The account the order is in: paper (default), live (needs confirmation) or sandbox.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

[Operation("orders cancel", Summary = "Cancel one working order through the gateway (audited; live needs a confirmation code).", Risk = OperationRisk.Money)]
[Example("idiotproof orders cancel 61e69015-8549-4bfd-b9c3-01e75843f47d")]
[Example("idiotproof orders cancel 61e69015-8549-4bfd-b9c3-01e75843f47d --mode live")]
public sealed class OrdersCancelOperation : IOperation<OrdersCancelArgs>
{
    public async Task<OperationResult> ExecuteAsync(OrdersCancelArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var id = args.Id.Trim();
        if (id.Length == 0) return OperationResult.Usage("Name the order id.");

        // Read the order first (Alpaca accounts only) so the preview names what is being cancelled
        // and a finished order gets a plain answer instead of Alpaca's 422.
        var symbol = "";
        if (args.Mode != BrokerMode.Sandbox)
        {
            var scope = args.Mode == BrokerMode.Live ? AccountScope.Live : AccountScope.Paper;
            var (_, order, why) = await OrderViews.FindAsync(ctx, scope, id, null);
            if (order is null) return OperationResult.Fail($"No order {id} in the {args.Mode.ToString().ToLowerInvariant()} account ({why}).");
            if (!order.IsWorking) return OperationResult.Fail($"Order {id} is {order.Status}; only working orders can be cancelled.");
            symbol = order.Symbol;
        }

        var intent = new OrderIntent
        {
            OwnerUserId = owner,
            Mode = args.Mode,
            Purpose = OrderPurpose.Exit,
            Origin = OrderOrigin.Manual,
            Action = OrderAction.Cancel,
            TargetOrderId = id,
            Order = new OrderRequest { Symbol = symbol },
            Note = args.Note ?? OrderFlow.DefaultNote(ctx, "orders cancel"),
        };
        return await TradeFlow.SubmitOrConfirmAsync(intent, ctx);
    }
}

public sealed record OrdersCancelAllArgs
{
    [Option("symbol", Description = "Only this symbol's open orders. Without it, every open order in the account.")]
    public string? Symbol { get; init; }

    [Option("mode", Description = "The account: paper (default), live (needs confirmation) or sandbox.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

[Operation("orders cancel-all", Summary = "Cancel every open order in an account, or only one symbol's, through the gateway (live needs a confirmation code).", Risk = OperationRisk.Money)]
[Example("idiotproof orders cancel-all --mode paper")]
[Example("idiotproof orders cancel-all --symbol NVDA --mode live")]
public sealed class OrdersCancelAllOperation : IOperation<OrdersCancelAllArgs>
{
    public async Task<OperationResult> ExecuteAsync(OrdersCancelAllArgs args, OperationContext ctx)
    {
        var intent = new OrderIntent
        {
            OwnerUserId = ctx.RequireOwner(),
            Mode = args.Mode,
            Purpose = OrderPurpose.Exit,
            Origin = OrderOrigin.Manual,
            Action = OrderAction.CancelAll,
            Order = new OrderRequest { Symbol = string.IsNullOrWhiteSpace(args.Symbol) ? "*" : args.Symbol.Trim().ToUpperInvariant() },
            Note = args.Note ?? OrderFlow.DefaultNote(ctx, "orders cancel-all"),
        };
        return await TradeFlow.SubmitOrConfirmAsync(intent, ctx);
    }
}
