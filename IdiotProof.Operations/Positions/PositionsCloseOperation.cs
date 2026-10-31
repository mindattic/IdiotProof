using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Positions;

public sealed record PositionsCloseArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker or OCC option symbol of the position to close, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Option("qty", Description = "Close only this many shares/contracts (default: all of it).")]
    public decimal? Quantity { get; init; }

    [Option("percentage", Description = "Close this percent of the position (0-100) instead of all of it.")]
    public decimal? Percentage { get; init; }

    [Option("mode", Description = "Which Alpaca account: paper (default) or live (needs a confirmation code).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("preview", Description = "Run every gate and show the exit without sending it.")]
    public bool Preview { get; init; }
}

[Operation("positions close", Summary = "Close (or trim) one Alpaca position as a market exit through the TransactionGateway. Live needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof positions close HIHO --mode paper")]
[Example("idiotproof positions close NVDA --percentage 50 --mode paper", Description = "Sell half.")]
[Example("idiotproof positions close TSLA --qty 5 --mode live", Description = "Prints a confirmation code first.")]
public sealed class PositionsCloseOperation : IOperation<PositionsCloseArgs>
{
    public async Task<OperationResult> ExecuteAsync(PositionsCloseArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (string.IsNullOrWhiteSpace(args.Symbol)) throw OperationException.Usage("Name the position, e.g. `idiotproof positions close NVDA`.");
        if (args.Quantity is not null && args.Percentage is not null) throw OperationException.Usage("Give --qty or --percentage, not both.");
        if (args.Quantity is <= 0) throw OperationException.Usage("--qty must be positive.");
        if (args.Percentage is <= 0 or > 100) throw OperationException.Usage("--percentage must be above 0 and at most 100.");
        var symbol = args.Symbol.Trim().ToUpperInvariant();

        var position = await BrokerPositions.GetAsync(ctx, owner, args.Mode, symbol);
        if (position is null)
            return OperationResult.Ok(new { symbol, mode = args.Mode.ToString(), closed = 0 }, $"The {args.Mode} account holds no {symbol}; nothing to close.");

        var held = Math.Abs(position.Quantity);
        decimal? want = args.Quantity ?? (args.Percentage is { } pct ? held * pct / 100m : null);
        if (want > held) throw OperationException.Usage($"The {args.Mode} account holds {Fmt.Number(held)} {symbol}; --qty {Fmt.Number(want.Value)} is more than that.");

        var what = args.Percentage is { } p ? $"{Fmt.Number(p)}% of" : args.Quantity is { } q ? $"{Fmt.Number(q)} of" : "all of";
        var plan = PositionExits.Plan(owner, args.Mode, position, want, null, $"{ctx.Caller.ToString().ToLowerInvariant()} positions close {what} {symbol}");
        return await PositionExits.RunAsync([plan], args.Mode, args.Preview, $"close {what} {symbol} · {args.Mode}", ctx);
    }
}
