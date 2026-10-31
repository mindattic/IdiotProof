using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Trading;

namespace IdiotProof.Operations.Positions;

/// <summary>What closing one broker position means: the exit intent, or why it can't be closed here.</summary>
public sealed record ExitPlan(BrokerPosition Position, OrderIntent? Intent, string? Skipped)
{
    /// <summary>Shares/contracts left open after this exit (a fractional remainder, or a partial close).</summary>
    public decimal Remaining => Math.Abs(Position.Quantity) - (Intent?.Order.SizeQuantity ?? 0m);
}

/// <summary>
/// Turns broker positions into exit intents (sell-to-close a long, buy-to-close a short, market,
/// DAY, OrderPurpose.Exit, OrderOrigin.Flatten) and runs a set of them through the
/// <see cref="TransactionGateway"/>: one exit is the ordinary gateway flow (Live → preview + code,
/// re-run with --confirm); several Live exits share ONE code bound to exactly that set, and each is
/// still gated individually when it is placed.
/// </summary>
public static class PositionExits
{
    /// <summary>
    /// The exit for <paramref name="p"/>: <paramref name="quantity"/> (null = all of it), capped at
    /// what is held. Partial closes and shorts are whole shares/contracts; closing ALL of a
    /// fractional long sells the exact fraction (the position exists, so the asset is fractionable).
    /// The reference price is left out on purpose so the intent's hash — what a Live code is bound
    /// to — does not move with the quote.
    /// </summary>
    public static ExitPlan Plan(Guid owner, BrokerMode mode, BrokerPosition p, decimal? quantity, Guid? strategyId, string note)
    {
        if (p.IsCrypto)
            return new ExitPlan(p, null, "crypto is closed with the crypto commands");
        if (p.Quantity == 0)
            return new ExitPlan(p, null, "flat");

        var held = Math.Abs(p.Quantity);
        var amount = Math.Min(quantity ?? held, held);
        var fractional = amount == held && amount != decimal.Floor(amount) && !p.IsShort && !p.IsOption;
        var whole = (int)decimal.Floor(amount);
        if (!fractional && whole < 1)
            return new ExitPlan(p, null, $"less than one whole {(p.IsOption ? "contract" : "share")} ({Fmt.Number(amount)} of {Fmt.Number(held)}); close all of it instead");

        var intent = new OrderIntent
        {
            OwnerUserId = owner,
            Mode = mode,
            Purpose = OrderPurpose.Exit,
            Origin = OrderOrigin.Flatten,
            StrategyId = strategyId,
            Note = note,
            Order = new OrderRequest
            {
                Symbol = p.Option?.OccSymbol ?? p.Symbol.ToUpperInvariant(),
                Quantity = fractional ? 0 : whole,
                FractionalQuantity = fractional ? amount : null,
                Side = p.IsShort ? OrderSide.Buy : OrderSide.Sell,
                Type = OrderType.Market,
                TimeInForce = "DAY",
                PositionIntent = p.IsShort ? "buy_to_close" : "sell_to_close",
                AssetClass = p.IsOption ? AssetClass.Option : AssetClass.Equity,
                Option = p.Option,
            },
        };
        return new ExitPlan(p, intent, null);
    }

    /// <summary>The cancel-every-open-order intent that runs ahead of a close-all's exits.</summary>
    public static OrderIntent CancelAllOrders(Guid owner, BrokerMode mode, string note) => new()
    {
        OwnerUserId = owner,
        Mode = mode,
        Purpose = OrderPurpose.Exit,
        Origin = OrderOrigin.Flatten,
        Action = OrderAction.CancelAll,
        Note = note,
        Order = new OrderRequest { Symbol = "*", TimeInForce = "DAY" },
    };

    /// <summary>
    /// Previews, places, or (Live) gates the exits behind a confirmation code. <paramref name="what"/>
    /// names the action in the code's preview ("close all · Live"). <paramref name="before"/> runs
    /// first and rides on the same code (close-all's cancel-every-open-order).
    /// </summary>
    public static async Task<OperationResult> RunAsync(IReadOnlyList<ExitPlan> plans, BrokerMode mode, bool preview, string what,
        OperationContext ctx, IReadOnlyList<OrderIntent>? before = null)
    {
        var intents = (before ?? []).Concat(plans.Where(p => p.Intent is not null).Select(p => p.Intent!)).ToList();
        var skipped = plans.Where(p => p.Skipped is not null).Select(p => $"  skipped {p.Position.Symbol}: {p.Skipped}").ToList();
        var footer = skipped.Count == 0 ? "" : "\n" + string.Join("\n", skipped);
        var skippedData = plans.Where(p => p.Skipped is not null).Select(p => new { p.Position.Symbol, Reason = p.Skipped });

        if (intents.Count == 0)
            return OperationResult.Ok(new { closed = 0, skipped = skippedData }, $"Nothing to close in {mode}." + footer);

        var gateway = ctx.Get<TransactionGateway>();
        var caller = ctx.Caller.ToString().ToLowerInvariant();

        if (preview)
        {
            var dry = new List<GatewayOutcome>();
            foreach (var i in intents) dry.Add(await gateway.PreviewAsync(i, new GatewayOptions { Caller = caller }, ctx.CancellationToken));
            return Summarize(dry, plans, "Preview only — nothing was sent.", skippedData, footer, ctx);
        }

        if (intents.Count == 1)
        {
            var single = await TradeFlow.SubmitOrConfirmAsync(intents[0], ctx);
            return footer.Length == 0 ? single : single with { Human = (single.Human ?? "") + footer };
        }

        if (mode == BrokerMode.Live)
        {
            // One code for the whole set; the gateway issues it, binds it to exactly these
            // intents, and re-gates each one when the code comes back.
            if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
            {
                var text = $"{what.ToUpperInvariant()} — {intents.Count} Live exits:\n" + string.Join("\n", intents.Select(i => "  " + i.Describe())) + footer;
                var (dry, pending) = await gateway.RequestBatchConfirmationAsync(intents, what, text, caller, ctx.CancellationToken);
                if (pending is null)
                    return Summarize(dry.ToList(), plans, "A gate refused at least one exit; no code was issued.", skippedData, footer, ctx) with { Status = OperationStatus.Blocked };
                return OperationResult.NeedsConfirmation(pending.Code, text, pending.ExpiresUtc,
                    new { what, exits = intents.Select(i => i.Describe()), skipped = skippedData });
            }

            try
            {
                var placed = await gateway.ConfirmBatchAsync(ctx.ConfirmationCode, intents, what, caller, ctx.CancellationToken);
                return Summarize(placed.ToList(), plans, null, skippedData, footer, ctx);
            }
            catch (ConfirmationRefusedException ex)
            {
                return OperationResult.Blocked(ex.Message
                    + (ex.Failure == ConfirmationFailure.Tampered ? " The positions changed since the code was issued (or it was for something else); run it again without --confirm for a fresh code." : ""),
                    new { failure = ex.Failure.ToString() });
            }
        }

        var outcomes = new List<GatewayOutcome>();
        foreach (var i in intents) outcomes.Add(await gateway.SubmitAsync(i, new GatewayOptions { Caller = caller }, ctx.CancellationToken));
        return Summarize(outcomes, plans, null, skippedData, footer, ctx);
    }

    private static OperationResult Summarize(List<GatewayOutcome> outcomes, IReadOnlyList<ExitPlan> plans, string? note,
        object skipped, string footer, OperationContext ctx)
    {
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Order").Column("Status").Column("Detail", maxWidth: 70);
        foreach (var o in outcomes)
            table.Row(o.Intent.Describe(), o.Status.ToString().ToLowerInvariant(),
                o.Status is GatewayStatus.Blocked or GatewayStatus.Rejected
                    ? string.Join("; ", o.Verdicts.Where(v => !v.Passed).Select(v => v.Detail).DefaultIfEmpty(o.Message))
                    : o.Intent.Action == OrderAction.CancelAll && o.Status == GatewayStatus.Placed ? o.Message
                    : o.Order?.BrokerOrderId is { Length: > 0 } id ? "order " + id : o.RoutedTo);
        var ok = outcomes.Count(o => o.Status is GatewayStatus.Placed or GatewayStatus.Previewed);
        var data = new
        {
            exits = outcomes.Select(o => new
            {
                Order = o.Intent.Describe(), Action = o.Intent.Action.ToString(), o.Intent.Order.Symbol, Quantity = o.Intent.Order.SizeQuantity, Side = o.Intent.Order.Side.ToString(),
                Status = o.Status.ToString(), o.Message, BrokerOrderId = o.Order?.BrokerOrderId, o.RoutedTo,
            }),
            succeeded = ok,
            failed = outcomes.Count - ok,
            skipped,
        };
        var human = table.Render() + footer + (note is null ? "" : "\n" + note)
            + (plans.Any(p => p.Intent is not null && p.Remaining > 0 && p.Skipped is null) ? "\nPartial: some shares stay open (see the order sizes)." : "");
        return ok == outcomes.Count
            ? OperationResult.Ok(data, human + (note is null ? $"\n{ok} exit{(ok == 1 ? "" : "s")} sent." : ""))
            : OperationResult.Fail($"{outcomes.Count - ok} of {outcomes.Count} exits did not go through.", data) with { Human = human };
    }
}
