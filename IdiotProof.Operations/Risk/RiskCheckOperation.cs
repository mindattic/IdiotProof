using IdiotProof.Blazor.Services;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Risk;

public sealed record RiskCheckArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Option("entry", Description = "Planned entry price.", Required = true)]
    public decimal Entry { get; init; }

    [Option("stop", Description = "Planned protective stop price.", Required = true)]
    public decimal Stop { get; init; }

    [Option("qty", Description = "Planned share count.", Required = true)]
    public int Qty { get; init; }

    [Option("target", Description = "Planned take-profit price, for the reward:risk warning.")]
    public decimal? Target { get; init; }

    [Option("short", Description = "A short entry (stop above entry) instead of a long.")]
    public bool Short { get; init; }
}

/// <summary>
/// Dry-runs a trade setup through the owner's Risk Guardian exactly as the transaction gateway
/// would size it (IP-LAW-2): approved or the block reasons, worst-case and expected loss, the
/// warnings, a reduced quantity when one would pass, and the largest size the limits allow.
/// Nothing is placed and the daily loss counter is untouched.
/// </summary>
[Operation("risk check", Summary = "Dry-run a setup through your Risk Guardian: approved or why not, worst case, and the largest size allowed. Places nothing.", Risk = OperationRisk.Read)]
[Example("idiotproof risk check NVDA --entry 100 --stop 98 --qty 20")]
[Example("idiotproof risk check TSLA --entry 250 --stop 255 --qty 5 --target 240 --short")]
public sealed class RiskCheckOperation : IOperation<RiskCheckArgs>
{
    public async Task<OperationResult> ExecuteAsync(RiskCheckArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol, e.g. `idiotproof risk check NVDA --entry 100 --stop 98 --qty 20`.");

        var setup = new TradeSetup
        {
            Symbol = symbol,
            Direction = args.Short ? TradeDirection.Short : TradeDirection.Long,
            EntryPrice = args.Entry,
            StopLoss = args.Stop,
            TakeProfit = args.Target ?? 0m,
            Quantity = args.Qty,
            RiskDollars = Math.Abs(args.Entry - args.Stop) * Math.Max(args.Qty, 0),
            RewardDollars = args.Target is { } t ? Math.Abs(t - args.Entry) * Math.Max(args.Qty, 0) : 0m,
            Rationale = "risk check (dry run)",
        };
        var guardian = await ctx.Get<RiskGuardianService>().GetForUserAsync(owner, ctx.CancellationToken);
        var r = guardian.ValidateTrade(setup);
        var maxQty = args.Entry > 0 && args.Stop > 0 ? guardian.CalculateMaxQuantity(args.Entry, args.Stop) : 0;

        var block = new KeyValueBlock()
            .Add("Setup", $"{(args.Short ? "SHORT" : "LONG")} {args.Qty} {symbol} @ {Fmt.Money(args.Entry)}, stop {Fmt.Money(args.Stop)}" +
                          (args.Target is { } tp ? $", target {Fmt.Money(tp)}" : ""))
            .Add("Verdict", r.IsApproved ? (r.RequiresConfirmation ? "APPROVED (above the confirmation threshold)" : "APPROVED") : "BLOCKED")
            .Add("Expected loss", Fmt.Money(r.ExpectedLoss) + " if the stop fills")
            .Add("Worst case", Fmt.Money(r.WorstCaseLoss) + " (1.5x slippage through the stop)")
            .Add("Largest size allowed", maxQty > 0 ? $"{maxQty} shares at this entry and stop" : "none at this entry and stop");
        if (r.AdjustedSetup is { } adj) block.Add("Would pass at", $"{adj.Quantity} shares");
        var lines = new List<string> { block.Render() };
        lines.AddRange(r.BlockReasons.Select(b => "  BLOCK: " + b));
        lines.AddRange(r.Warnings.Select(w => "  warn:  " + w));
        lines.Add("Dry run: nothing was placed and today's loss counter is unchanged.");

        var data = new
        {
            symbol, approved = r.IsApproved, requiresConfirmation = r.RequiresConfirmation, blockReasons = r.BlockReasons, warnings = r.Warnings,
            expectedLoss = r.ExpectedLoss, worstCaseLoss = r.WorstCaseLoss, adjustedQuantity = r.AdjustedSetup?.Quantity, maxQuantity = maxQty,
        };
        return OperationResult.Ok(data, string.Join("\n", lines));
    }
}
