using IdiotProof.Core.Trading;

namespace IdiotProof.Operations.Positions;

public sealed record PositionsCloseAllArgs
{
    [Option("cancel-orders", Description = "Cancel every open order first (frees shares held by working sells/stops).")]
    public bool CancelOrders { get; init; }

    [Option("mode", Description = "Which Alpaca account: paper (default) or live (one confirmation code covers the whole set).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("preview", Description = "Run every gate and list everything that would close, without sending anything.")]
    public bool Preview { get; init; }
}

[Operation("positions close-all", Summary = "Close every Alpaca position in one account with market exits through the TransactionGateway; optionally cancel open orders first. Live needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof positions close-all --preview", Description = "List what would close in paper; send nothing.")]
[Example("idiotproof positions close-all --cancel-orders --mode paper")]
[Example("idiotproof positions close-all --cancel-orders --mode live", Description = "Prints the full list and one confirmation code.")]
public sealed class PositionsCloseAllOperation : IOperation<PositionsCloseAllArgs>
{
    public async Task<OperationResult> ExecuteAsync(PositionsCloseAllArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var caller = ctx.Caller.ToString().ToLowerInvariant();
        var positions = await BrokerPositions.ListAsync(ctx, owner, args.Mode);
        var plans = positions.OrderBy(p => p.Symbol, StringComparer.Ordinal)
            .Select(p => PositionExits.Plan(owner, args.Mode, p, null, null, $"{caller} positions close-all"))
            .ToList();
        var before = args.CancelOrders ? new[] { PositionExits.CancelAllOrders(owner, args.Mode, $"{caller} positions close-all --cancel-orders") } : [];
        return await PositionExits.RunAsync(plans, args.Mode, args.Preview,
            $"close all{(args.CancelOrders ? " + cancel orders" : "")} · {args.Mode}", ctx, before);
    }
}
