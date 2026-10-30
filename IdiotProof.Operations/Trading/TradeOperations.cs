using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Trading;

/// <summary>The four ways to trade one stock: open or close, long or short.</summary>
public enum TradeAction { Buy, Sell, Short, Cover }

/// <summary>Arguments shared by every manual equity order (buy, sell, short, cover, preview).</summary>
public sealed record TradeArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Argument(1, Name = "qty", Description = "Shares. Leave out and pass --dollars to size by amount.", Required = false)]
    public int? Quantity { get; init; }

    [Option("dollars", Description = "Size by dollar amount instead of shares (fractional, regular hours only).")]
    public decimal? Dollars { get; init; }

    [Option("limit", Description = "Limit price. Without it the order is a market order.", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("stop", Description = "Protective stop price for an entry; the Risk Guardian refuses entries without one.", Short = 's')]
    public decimal? Stop { get; init; }

    [Option("target", Description = "Take-profit price (for the reward:risk check).", Short = 't')]
    public decimal? Target { get; init; }

    [Option("mode", Description = "Where it routes: sandbox (simulator, the default), paper, or live (needs confirmation).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("ext", Description = "Extended hours (premarket/after-hours); requires --limit, DAY.")]
    public bool ExtendedHours { get; init; }

    [Option("tif", Description = "Time in force: day (default) or gtc.")]
    public string TimeInForce { get; init; } = "day";

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

/// <summary>
/// Builds intents from <see cref="TradeArgs"/> and turns gateway outcomes into operation results.
/// Shared by every trading operation so the CLI, MCP and web all trade identically.
/// </summary>
public static class TradeFlow
{
    public static async Task<OrderIntent> BuildIntentAsync(TradeAction action, TradeArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (string.IsNullOrWhiteSpace(args.Symbol)) throw OperationException.Usage("Name the symbol, e.g. `idiotproof trade buy NVDA 10 --stop 120`.");
        if (args.Quantity is null && args.Dollars is null) throw OperationException.Usage("Give a share count or --dollars.");
        if (args.Quantity is not null && args.Dollars is not null) throw OperationException.Usage("Give a share count or --dollars, not both.");
        if (args.Quantity is <= 0) throw OperationException.Usage("The share count must be positive.");
        if (args.Dollars is not null && action is TradeAction.Short or TradeAction.Cover)
            throw OperationException.Usage("Short sales are sized in shares, not dollars.");

        var symbol = args.Symbol.Trim().ToUpperInvariant();
        decimal? reference = args.Limit;
        if (reference is null && ctx.Services.GetService(typeof(IMarketDataFeed)) is IMarketDataFeed feed)
        {
            try { reference = (await feed.GetLatestPriceAsync(symbol, ctx.CancellationToken))?.Price; }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* no quote → the guardian asks for --limit */ }
        }

        var (side, purpose, intent) = action switch
        {
            TradeAction.Buy => (OrderSide.Buy, OrderPurpose.Entry, (string?)null),
            TradeAction.Sell => (OrderSide.Sell, OrderPurpose.Exit, "sell_to_close"),
            TradeAction.Short => (OrderSide.Sell, OrderPurpose.Entry, "sell_to_open"),
            _ => (OrderSide.Buy, OrderPurpose.Exit, "buy_to_close"),
        };

        return new OrderIntent
        {
            OwnerUserId = owner,
            Mode = args.Mode,
            Purpose = purpose,
            Origin = OrderOrigin.Manual,
            Order = new OrderRequest
            {
                Symbol = symbol,
                Quantity = args.Quantity ?? 0,
                Notional = args.Dollars,
                Side = side,
                Type = args.Limit is null ? OrderType.Market : OrderType.Limit,
                LimitPrice = args.Limit,
                TimeInForce = args.TimeInForce.Trim().ToUpperInvariant(),
                ExtendedHours = args.ExtendedHours,
                PositionIntent = intent,
            },
            ReferencePrice = reference,
            StopLossPrice = args.Stop,
            TakeProfitPrice = args.Target,
            Note = args.Note ?? $"{ctx.Caller.ToString().ToLowerInvariant()} {action.ToString().ToLowerInvariant()}",
        };
    }

    /// <summary>
    /// Submits, or — when the call carries a confirmation code — confirms the previewed order.
    /// The re-sent arguments must describe the same order the code was issued for.
    /// </summary>
    public static async Task<OperationResult> SubmitOrConfirmAsync(OrderIntent intent, OperationContext ctx)
    {
        var gateway = ctx.Get<TransactionGateway>();
        var caller = ctx.Caller.ToString().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            try
            {
                var confirmed = await gateway.ConfirmAsync(ctx.ConfirmationCode, intent.OwnerUserId, caller, intent.Hash(), ctx.CancellationToken);
                return ToResult(confirmed!, ctx);
            }
            catch (ConfirmationRefusedException ex)
            {
                return OperationResult.Blocked(ex.Message, new { failure = ex.Failure.ToString() });
            }
        }
        return ToResult(await gateway.SubmitAsync(intent, new GatewayOptions { Caller = caller }, ctx.CancellationToken), ctx);
    }

    public static OperationResult ToResult(GatewayOutcome outcome, OperationContext ctx)
    {
        var data = new
        {
            Status = outcome.Status.ToString(),
            Order = outcome.Intent.Describe(),
            outcome.RoutedTo,
            outcome.Message,
            BrokerOrderId = outcome.Order?.BrokerOrderId,
            outcome.EstimatedCost,
            outcome.WorstCaseLoss,
            Gates = outcome.Verdicts.Select(v => new { v.Gate, v.Passed, v.Detail }),
        };
        // Ok results carry their message in the text; the renderer appends the message itself for
        // blocked/failed/confirmation results, so those get only the preview block.
        var preview = outcome.Preview();
        return outcome.Status switch
        {
            GatewayStatus.Placed or GatewayStatus.Previewed => OperationResult.Ok(data, preview + "\n\n" + outcome.Message),
            GatewayStatus.ConfirmationRequired => OperationResult.NeedsConfirmation(
                outcome.ConfirmationCode!, preview, outcome.ConfirmationExpiresUtc!.Value, data) with { Human = preview },
            GatewayStatus.Blocked => OperationResult.Blocked(outcome.Message, data) with { Human = preview },
            _ => OperationResult.Fail(outcome.Message, data) with { Human = preview },
        };
    }
}

[Operation("trade buy", Summary = "Buy shares (open or add to a long). Sandbox by default; live needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof trade buy NVDA 10 --limit 131.20 --stop 127.50", Description = "Sandbox limit buy with a protective stop.")]
[Example("idiotproof trade buy AAPL --dollars 500 --stop 220 --mode paper")]
[Example("idiotproof trade buy GAPT 100 --limit 4.12 --stop 3.90 --ext --mode live", Description = "Premarket live buy; prints a confirmation code first.")]
public sealed class TradeBuyOperation : IOperation<TradeArgs>
{
    public async Task<OperationResult> ExecuteAsync(TradeArgs args, OperationContext ctx) =>
        await TradeFlow.SubmitOrConfirmAsync(await TradeFlow.BuildIntentAsync(TradeAction.Buy, args, ctx), ctx);
}

[Operation("trade sell", Summary = "Sell shares you hold (close or trim a long). Exits always clear the gates.", Risk = OperationRisk.Money)]
[Example("idiotproof trade sell NVDA 10 --mode paper")]
public sealed class TradeSellOperation : IOperation<TradeArgs>
{
    public async Task<OperationResult> ExecuteAsync(TradeArgs args, OperationContext ctx) =>
        await TradeFlow.SubmitOrConfirmAsync(await TradeFlow.BuildIntentAsync(TradeAction.Sell, args, ctx), ctx);
}

[Operation("trade short", Summary = "Sell short (open a short). Needs a stop above entry; margin + shortable name at the broker.", Risk = OperationRisk.Money)]
[Example("idiotproof trade short TSLA 5 --limit 240 --stop 246")]
public sealed class TradeShortOperation : IOperation<TradeArgs>
{
    public async Task<OperationResult> ExecuteAsync(TradeArgs args, OperationContext ctx) =>
        await TradeFlow.SubmitOrConfirmAsync(await TradeFlow.BuildIntentAsync(TradeAction.Short, args, ctx), ctx);
}

[Operation("trade cover", Summary = "Buy to cover a short.", Risk = OperationRisk.Money)]
[Example("idiotproof trade cover TSLA 5 --mode paper")]
public sealed class TradeCoverOperation : IOperation<TradeArgs>
{
    public async Task<OperationResult> ExecuteAsync(TradeArgs args, OperationContext ctx) =>
        await TradeFlow.SubmitOrConfirmAsync(await TradeFlow.BuildIntentAsync(TradeAction.Cover, args, ctx), ctx);
}

/// <summary>Arguments for a dry run: the order plus which way it goes.</summary>
public sealed record TradePreviewArgs
{
    [Argument(0, Name = "action", Description = "buy, sell, short or cover.")]
    public TradeAction Action { get; init; }

    [Argument(1, Name = "symbol", Description = "Ticker.")]
    public string Symbol { get; init; } = "";

    [Argument(2, Name = "qty", Description = "Shares (or use --dollars).", Required = false)]
    public int? Quantity { get; init; }

    [Option("dollars", Description = "Size by dollar amount.")]
    public decimal? Dollars { get; init; }

    [Option("limit", Description = "Limit price.", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("stop", Description = "Protective stop price.", Short = 's')]
    public decimal? Stop { get; init; }

    [Option("target", Description = "Take-profit price.", Short = 't')]
    public decimal? Target { get; init; }

    [Option("mode", Description = "sandbox (default), paper or live.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("ext", Description = "Extended hours.")]
    public bool ExtendedHours { get; init; }

    public TradeArgs ToTradeArgs() => new()
    {
        Symbol = Symbol, Quantity = Quantity, Dollars = Dollars, Limit = Limit, Stop = Stop,
        Target = Target, Mode = Mode, ExtendedHours = ExtendedHours,
    };
}

[Operation("trade preview", Summary = "Run every gate on an order without placing it (no code issued).", Risk = OperationRisk.Read)]
[Example("idiotproof trade preview buy NVDA 10 --limit 131 --stop 127 --mode live")]
public sealed class TradePreviewOperation : IOperation<TradePreviewArgs>
{
    public async Task<OperationResult> ExecuteAsync(TradePreviewArgs args, OperationContext ctx)
    {
        var intent = await TradeFlow.BuildIntentAsync(args.Action, args.ToTradeArgs(), ctx);
        var outcome = await ctx.Get<TransactionGateway>().PreviewAsync(intent, new GatewayOptions { Caller = ctx.Caller.ToString().ToLowerInvariant() }, ctx.CancellationToken);
        return TradeFlow.ToResult(outcome, ctx);
    }
}

public sealed record TradeConfirmArgs
{
    [Argument(0, Name = "code", Description = "The six-character code a Live preview returned.")]
    public string Code { get; init; } = "";
}

[Operation("trade confirm", Summary = "Execute a pending Live order by its confirmation code (exactly what was previewed, once).", Risk = OperationRisk.Money)]
[Example("idiotproof trade confirm K7Q2ZP")]
public sealed class TradeConfirmOperation : IOperation<TradeConfirmArgs>
{
    public async Task<OperationResult> ExecuteAsync(TradeConfirmArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        try
        {
            var outcome = await ctx.Get<TransactionGateway>().ConfirmAsync(args.Code, owner, ctx.Caller.ToString().ToLowerInvariant(), null, ctx.CancellationToken);
            return outcome is null
                ? OperationResult.Usage("That code confirms a strategy activation, not an order — use `idiotproof strategy mode <id> live --confirm <code>`.")
                : TradeFlow.ToResult(outcome, ctx);
        }
        catch (ConfirmationRefusedException ex)
        {
            return OperationResult.Blocked(ex.Message, new { failure = ex.Failure.ToString() });
        }
    }
}

[Operation("trade pending", Summary = "List Live confirmations still waiting for a yes.", Risk = OperationRisk.Read)]
[Example("idiotproof trade pending")]
public sealed class TradePendingOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var rows = await ctx.Get<ConfirmationStore>().ListActiveAsync(owner, ctx.CancellationToken);
        if (rows.Count == 0) return OperationResult.Ok(Array.Empty<object>(), "No Live confirmations are pending.");

        var table = new TextTable().Column("code").Column("kind").Column("expires (ET)").Column("what");
        foreach (var r in rows)
            table.Row(r.Code, r.Kind, Fmt.EtTime(r.ExpiresUtc), (r.Preview ?? "").Split('\n')[0]);
        return OperationResult.Ok(
            rows.Select(r => new { r.Code, r.Kind, ExpiresUtc = r.ExpiresUtc, Preview = r.Preview }),
            table.Render());
    }
}
