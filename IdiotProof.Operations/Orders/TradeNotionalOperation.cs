using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Trading;

namespace IdiotProof.Operations.Orders;

public sealed record TradeNotionalArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. AAPL.")]
    public string Symbol { get; init; } = "";

    [Argument(1, Name = "amount", Description = "Dollars to buy (or sell) — Alpaca works out the fractional shares.")]
    public decimal Amount { get; init; }

    [Option("side", Description = "buy (default, opens/adds to a long) or sell (trims a long by dollar value).")]
    public OrderSide Side { get; init; } = OrderSide.Buy;

    [Option("stop", Description = "Protective stop price for a buy; the Risk Guardian refuses entries without one.", Short = 's')]
    public decimal? Stop { get; init; }

    [Option("target", Description = "Take-profit price (for the reward:risk check).", Short = 't')]
    public decimal? Target { get; init; }

    [Option("mode", Description = "Where it routes: sandbox (simulator, the default), paper, or live (needs confirmation).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

[Operation("trade notional", Summary = "Buy (or sell) a dollar amount of a stock: a fractional, DAY, market order sized in dollars. Live needs a code.", Risk = OperationRisk.Money)]
[Example("idiotproof trade notional AAPL 500 --stop 220 --mode paper")]
[Example("idiotproof trade notional AAPL 250 --side sell --mode live", Description = "Trim $250 of a long; prints a confirmation code first.")]
public sealed class TradeNotionalOperation : IOperation<TradeNotionalArgs>
{
    public async Task<OperationResult> ExecuteAsync(TradeNotionalArgs args, OperationContext ctx)
    {
        if (args.Amount <= 0) return OperationResult.Usage("The dollar amount must be positive.");
        var trade = new TradeArgs
        {
            Symbol = args.Symbol,
            Dollars = args.Amount,
            Stop = args.Stop,
            Target = args.Target,
            Mode = args.Mode,
            TimeInForce = "day",
            Note = args.Note ?? OrderFlow.DefaultNote(ctx, "trade notional"),
        };
        var action = args.Side == OrderSide.Buy ? TradeAction.Buy : TradeAction.Sell;
        return await TradeFlow.SubmitOrConfirmAsync(await TradeFlow.BuildIntentAsync(action, trade, ctx), ctx);
    }
}
