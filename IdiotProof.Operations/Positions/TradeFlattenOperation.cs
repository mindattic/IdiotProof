using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;

namespace IdiotProof.Operations.Positions;

public sealed record TradeFlattenArgs
{
    [Option("symbol", Description = "Close the whole broker position in this symbol.")]
    public string? Symbol { get; init; }

    [Option("strategy", Description = "Close what this strategy books (id, id prefix, stamp id or symbol), capped at what the broker holds.")]
    public string? Strategy { get; init; }

    [Option("all", Description = "Close every position in the account.")]
    public bool All { get; init; }

    [Option("mode", Description = "paper or live (live needs a confirmation code). Default: the strategy's mode with --strategy, else paper.", Short = 'm')]
    public BrokerMode? Mode { get; init; }

    [Option("preview", Description = "Run every gate and list the exits without sending them.")]
    public bool Preview { get; init; }
}

[Operation("trade flatten", Summary = "Flatten a symbol, a strategy's position, or the whole account with market exits through the TransactionGateway. Live needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof trade flatten --symbol HIHO --mode paper")]
[Example("idiotproof trade flatten --strategy IP-P4AY-UXZU", Description = "Uses the strategy's own mode.")]
[Example("idiotproof trade flatten --all --mode live --preview", Description = "List every Live exit without sending anything.")]
public sealed class TradeFlattenOperation : IOperation<TradeFlattenArgs>
{
    public async Task<OperationResult> ExecuteAsync(TradeFlattenArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var chosen = (string.IsNullOrWhiteSpace(args.Symbol) ? 0 : 1) + (string.IsNullOrWhiteSpace(args.Strategy) ? 0 : 1) + (args.All ? 1 : 0);
        if (chosen != 1) throw OperationException.Usage("Say what to flatten: exactly one of --symbol <ticker>, --strategy <id> or --all.");
        var caller = ctx.Caller.ToString().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(args.Strategy))
        {
            var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
            var mode = args.Mode ?? OrderIntent.ParseMode(s.BrokerMode);
            if (s.PositionQty == 0)
                return OperationResult.Ok(new { s.Id, closed = 0 }, $"{s.Symbol} '{s.Title}' books no position; nothing to flatten.");
            var held = await BrokerPositions.GetAsync(ctx, owner, mode, s.Symbol);
            if (held is null)
                return OperationResult.Ok(new { s.Id, closed = 0, booked = s.PositionQty },
                    $"{s.Symbol} '{s.Title}' books {s.PositionQty} shares but the {mode} account holds none — nothing to sell. See `idiotproof positions reconcile`.");
            var plan = PositionExits.Plan(owner, mode, held, s.PositionQty, s.Id, $"{caller} flatten strategy {StrategyRefs.ShortGuid(s.Id)} {s.Symbol}");
            return await PositionExits.RunAsync([plan], mode, args.Preview, $"flatten strategy {StrategyRefs.ShortGuid(s.Id)} {s.Symbol} · {mode}", ctx);
        }

        var accountMode = args.Mode ?? BrokerMode.Paper;
        if (!string.IsNullOrWhiteSpace(args.Symbol))
        {
            var symbol = args.Symbol.Trim().ToUpperInvariant();
            var held = await BrokerPositions.GetAsync(ctx, owner, accountMode, symbol);
            if (held is null)
                return OperationResult.Ok(new { symbol, closed = 0 }, $"The {accountMode} account holds no {symbol}; already flat.");
            var plan = PositionExits.Plan(owner, accountMode, held, null, null, $"{caller} flatten {symbol}");
            return await PositionExits.RunAsync([plan], accountMode, args.Preview, $"flatten {symbol} · {accountMode}", ctx);
        }

        var positions = await BrokerPositions.ListAsync(ctx, owner, accountMode);
        var plans = positions.OrderBy(p => p.Symbol, StringComparer.Ordinal)
            .Select(p => PositionExits.Plan(owner, accountMode, p, null, null, $"{caller} flatten all"))
            .ToList();
        return await PositionExits.RunAsync(plans, accountMode, args.Preview, $"flatten all · {accountMode}", ctx);
    }
}
