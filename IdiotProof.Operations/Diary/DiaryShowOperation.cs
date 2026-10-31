using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Diary;

public sealed record DiaryShowArgs
{
    [Argument(0, Name = "trade", Description = "Diary trade id, or the 8-character prefix `diary list` shows.")]
    public string Trade { get; init; } = "";
}

/// <summary>
/// One diary trade in full: the strategy and account it ran in, entry and exit with order ids,
/// the risk plan that managed it (stop, trail, target, giveback, sell-by), and the outcome in
/// dollars, percent and R.
/// </summary>
[Operation("diary show", Summary = "One diary trade in full: entry, exit, order ids, the risk plan that managed it, P&L and R.", Risk = OperationRisk.Read)]
[Example("idiotproof diary show 0193a7c2")]
public sealed class DiaryShowOperation : IOperation<DiaryShowArgs>
{
    public async Task<OperationResult> ExecuteAsync(DiaryShowArgs args, OperationContext ctx)
    {
        var t = await DiarySupport.ResolveAsync(ctx, args.Trade);
        var r = DiarySupport.ToRow(t);

        var plan = new List<string>();
        if (t.StopLossPrice is { } sl) plan.Add($"stop {Fmt.Money(sl)}");
        else if (t.StopLossPercent is { } slp) plan.Add($"stop {Fmt.PercentPoints(slp)}");
        if (t.TrailingStopPercent is { } tr) plan.Add($"trail {Fmt.PercentPoints(tr)}");
        if (t.TakeProfitPrice is { } tp) plan.Add($"target {Fmt.Money(tp)}");
        if (t.PeakGivebackPercent is { } gb) plan.Add($"giveback {Fmt.PercentPoints(gb)}" + (t.PeakGivebackArmEt is { } arm ? $" after {arm} ET" : ""));
        if (t.SellByEt is { } sb) plan.Add($"sell by {sb} ET");

        var kv = new KeyValueBlock()
            .Add("id", t.Id.ToString())
            .Add("strategy", $"{t.StrategyTitle} ({t.StrategyId.ToString("N")[..8]})")
            .Add("trade", $"{t.Direction} {t.Quantity} {t.Symbol}" + (t.Notional is { } n ? $" (sized {Fmt.Money(n)} notional)" : ""))
            .Add("account", $"{r.Mode} via {t.Broker}" + (r.Mode == "live" ? " — real money" : ""))
            .Add("status", t.Status)
            .Add("entry", $"{Fmt.Money(t.EntryPrice)} at {Fmt.Et(t.EntryUtc)}" + (t.EntryOrderId is { } eo ? $" (order {eo})" : ""))
            .Add("exit", t.ExitUtc is null ? "still open"
                : $"{(t.ExitPrice is { } px ? Fmt.Money(px) : "?")} at {Fmt.Et(t.ExitUtc)} — {t.ExitReason ?? "?"}" + (t.ExitOrderId is { } xo ? $" (order {xo})" : ""))
            .Add("held", r.HoldMinutes is null ? null : DiarySupport.Hold(r.HoldMinutes))
            .Add("risk plan", plan.Count == 0 ? "none recorded" : string.Join(", ", plan))
            .Add("risked", r.InitialRisk is { } risk ? Fmt.Money(risk) : null)
            .Add("P&L", t.RealizedPnL is { } pnl
                ? $"{Fmt.SignedMoney(pnl)}" + (t.ReturnPercent is { } ret ? $" ({Fmt.PercentPoints(ret, 2, signed: true)})" : "")
                  + (r.RMultiple is { } rm ? $", {rm.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture)}R" : "")
                : null)
            .Add("recorded", $"{Fmt.Et(t.CreatedUtc)}; updated {Fmt.Et(t.UpdatedUtc)}");
        return OperationResult.Ok(new { Trade = r, t.PeakGivebackPercent, t.PeakGivebackArmEt, t.SellByEt, t.CreatedUtc, t.UpdatedUtc }, kv.Render());
    }
}
