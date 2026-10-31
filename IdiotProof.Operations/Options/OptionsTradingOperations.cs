using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Trading;
using IdiotProof.Shared.Options;

namespace IdiotProof.Operations.Options;

/// <summary>Arguments for a single-leg options order: one OCC contract, a contract count, an optional limit.</summary>
public sealed record OptionsOrderArgs
{
    [Argument(0, Name = "occ", Description = "OCC contract symbol, e.g. AAPL261219C00150000.")]
    public string Occ { get; init; } = "";

    [Argument(1, Name = "contracts", Description = "Whole contracts (each is usually 100 shares).")]
    public int Contracts { get; init; }

    [Option("limit", Description = "Limit price per share (premium). Without it the order is a market order.", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("mode", Description = "Where it routes: sandbox (simulator, the default), paper, or live (needs confirmation).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

/// <summary>Arguments for selling contracts: sell to close by default, or write them with --open.</summary>
public sealed record OptionsSellArgs
{
    [Argument(0, Name = "occ", Description = "OCC contract symbol, e.g. AAPL261219C00150000.")]
    public string Occ { get; init; } = "";

    [Argument(1, Name = "contracts", Description = "Whole contracts (each is usually 100 shares).")]
    public int Contracts { get; init; }

    [Option("limit", Description = "Limit price per share (premium). Without it the order is a market order.", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("mode", Description = "Where it routes: sandbox (simulator, the default), paper, or live (needs confirmation).", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("open", Description = "Write (sell to open, level 1+: covered call / cash-secured put) instead of selling contracts you hold.")]
    public bool Open { get; init; }

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

}

/// <summary>
/// Single-leg options tickets: builds the <see cref="OrderIntent"/> (origin Options, whole contracts,
/// DAY, OCC symbol + contract attached), checks the account's options approval level against the
/// Alpaca <c>position_intent</c> on the broker the gateway will route to, then hands it to
/// <see cref="TradeFlow.SubmitOrConfirmAsync"/> — so Live waits for a confirmation code like any order.
/// </summary>
public static class OptionsTradeFlow
{
    /// <summary>Decodes the OCC symbol or throws a usage error that shows the expected layout.</summary>
    public static OptionContract ParseContract(string occ) =>
        OptionContract.ParseOcc(occ)
        ?? throw OperationException.Usage($"'{occ}' is not an OCC option symbol: ROOT + YYMMDD + C|P + strike×1000 (8 digits), e.g. AAPL261219C00150000.");

    public static OrderIntent BuildIntent(OperationContext ctx, OptionContract contract, int contracts, OrderSide side,
        string positionIntent, decimal? limit, BrokerMode mode, string? note)
    {
        var owner = ctx.RequireOwner();
        if (contracts <= 0) throw OperationException.Usage("The contract count must be a positive whole number.");
        if (limit is <= 0) throw OperationException.Usage("--limit must be a positive premium.");
        return new OrderIntent
        {
            OwnerUserId = owner,
            Mode = mode,
            Purpose = positionIntent.EndsWith("_to_open", StringComparison.Ordinal) ? OrderPurpose.Entry : OrderPurpose.Exit,
            Origin = OrderOrigin.Options,
            Order = new OrderRequest
            {
                Symbol = contract.OccSymbol,
                Quantity = contracts,
                Side = side,
                Type = limit is null ? OrderType.Market : OrderType.Limit,
                LimitPrice = limit,
                TimeInForce = "DAY",
                PositionIntent = positionIntent,
                AssetClass = AssetClass.Option,
                Option = contract,
            },
            ReferencePrice = limit,
            Note = note ?? $"{ctx.Caller.ToString().ToLowerInvariant()} options {positionIntent} {contract.DisplayName}",
        };
    }

    /// <summary>The broker the gateway will route this owner + mode to, or null when none resolves (the gateway reports why).</summary>
    public static async Task<IBrokerClient?> ResolveAsync(OperationContext ctx, BrokerMode mode)
    {
        try { return await ctx.Get<IOrderRouting>().ResolveAsync(ctx.RequireOwner(), mode, ctx.CancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException and not OperationException) { return null; }
    }

    /// <summary>
    /// Why the routed account's options level forbids this intent, or null when it may proceed.
    /// Fails closed: a level that can't be read blocks the ticket.
    /// </summary>
    public static async Task<string?> LevelBlockerAsync(OperationContext ctx, IBrokerClient? client, string positionIntent)
    {
        if (client is null) return null; // no route: the gateway's route gate refuses it with the reason
        if (!client.SupportsOptions) return "The broker this routes to does not trade options.";
        int level;
        try { level = (await client.GetOptionsAccountAsync(ctx.CancellationToken)).TradingLevel; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "Couldn't read the account's options approval level: " + ex.Message;
        }
        return OptionsTradingLevel.Blocker(level, positionIntent);
    }

    /// <summary>Level gate, then the transaction gateway (preview / Live code / place / audit).</summary>
    public static async Task<OperationResult> SubmitAsync(OrderIntent intent, OperationContext ctx, IBrokerClient? client = null)
    {
        client ??= await ResolveAsync(ctx, intent.Mode);
        var blocker = await LevelBlockerAsync(ctx, client, intent.Order.PositionIntent!);
        if (blocker is not null)
            return OperationResult.Blocked(blocker, new { Order = intent.Describe(), intent.Order.PositionIntent });
        return await TradeFlow.SubmitOrConfirmAsync(intent, ctx);
    }
}

[Operation("options buy", Summary = "Buy option contracts to open (level 2+). Sandbox by default; live needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof options buy AAPL261219C00150000 2 --limit 3.20 --mode paper", Description = "Paper limit buy of 2 calls.")]
[Example("idiotproof options buy SPY261120P00560000 1 --mode live", Description = "Live market buy; prints a confirmation code first.")]
public sealed class OptionsBuyOperation : IOperation<OptionsOrderArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsOrderArgs args, OperationContext ctx)
    {
        var contract = OptionsTradeFlow.ParseContract(args.Occ);
        var intent = OptionsTradeFlow.BuildIntent(ctx, contract, args.Contracts, OrderSide.Buy, "buy_to_open", args.Limit, args.Mode, args.Note);
        return await OptionsTradeFlow.SubmitAsync(intent, ctx);
    }
}

[Operation("options sell", Summary = "Sell option contracts you hold (sell to close), or write them with --open (level 1+).", Risk = OperationRisk.Money)]
[Example("idiotproof options sell AAPL261219C00150000 2 --limit 4.10 --mode paper", Description = "Take profit on 2 calls you hold.")]
[Example("idiotproof options sell AAPL261219C00170000 1 --open --limit 1.05 --mode paper", Description = "Write a covered call.")]
public sealed class OptionsSellOperation : IOperation<OptionsSellArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsSellArgs args, OperationContext ctx)
    {
        var contract = OptionsTradeFlow.ParseContract(args.Occ);
        var intent = OptionsTradeFlow.BuildIntent(ctx, contract, args.Contracts, OrderSide.Sell,
            args.Open ? "sell_to_open" : "sell_to_close", args.Limit, args.Mode, args.Note);
        return await OptionsTradeFlow.SubmitAsync(intent, ctx);
    }
}
