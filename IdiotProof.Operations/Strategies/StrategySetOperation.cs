using System.Globalization;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Strategies;

public sealed record StrategySetArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("stop", Description = "Stop-loss price (replaces a percent stop).")]
    public decimal? Stop { get; init; }

    [Option("stop-percent", Description = "Stop-loss percent from the fill (replaces a price stop).")]
    public decimal? StopPercent { get; init; }

    [Option("target", Description = "Take-profit price (replaces a percent target and any scale-out ladder).")]
    public decimal? Target { get; init; }

    [Option("target-percent", Description = "Take-profit percent from the fill (replaces a price target).")]
    public decimal? TargetPercent { get; init; }

    [Option("trailing", Description = "Trailing-stop percent; 0 removes it.")]
    public decimal? Trailing { get; init; }

    [Option("giveback", Description = "Peak-giveback exit percent; 0 removes it.")]
    public decimal? Giveback { get; init; }

    [Option("qty", Description = "Share quantity per entry (replaces a notional size).")]
    public int? Qty { get; init; }

    [Option("notional", Description = "Dollar size per entry (replaces a share quantity).")]
    public decimal? Notional { get; init; }

    [Option("sell-by", Description = "Sell anything still held at this ET time (HH:mm); \"none\" removes it.")]
    public string? SellBy { get; init; }

    [Option("session", Description = "Trading session: premarket, rth, afterhours or extended.")]
    public TradingSession? Session { get; init; }
}

[Operation("strategy set", Summary = "Edit a strategy's stop, target, trailing, size, sell-by time or session; the canon is rewritten and re-validated.", Risk = OperationRisk.Money,
    Description = "Only the named fields change. The result must still validate (a stop, stop below target on a long, ...). A Live strategy needs a confirmation code bound to exactly these changes.")]
[Example("idiotproof strategy set NVDA --stop 168.5 --target 195")]
[Example("idiotproof strategy set NVDA --qty 2 --sell-by 15:45")]
public sealed class StrategySetOperation : IOperation<StrategySetArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategySetArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var original = StrategyRefs.RequireDefinition(s);
        var def = StrategyDrafts.Copy(original);
        var changes = Apply(def, args);
        if (changes.Count == 0)
            return OperationResult.Usage("Name at least one field to change: --stop, --stop-percent, --target, --target-percent, --trailing, --giveback, --qty, --notional, --sell-by or --session.");

        var v = StrategyDrafts.Validate(def);
        if (v.HasErrors)
            return OperationResult.Fail("That edit leaves the strategy invalid; nothing changed:\n" + v.Render(ctx.Width), new { Changed = false, v.Findings });
        if (v.StampId == original.ShortId())
            return OperationResult.Ok(new { s.Id, Changed = false }, $"{s.Symbol} '{s.Title}' already has those settings.");

        var summary = string.Join(", ", changes);
        if (OrderIntent.ParseMode(s.BrokerMode) == BrokerMode.Live)
        {
            var gate = await LiveGate.RequireAsync(ctx, s, "set " + summary,
                $"EDIT {s.Symbol} '{s.Title}' · Live — {summary}" + (s.IsActive ? " (active: the Monitor uses it on its next tick)." : "."));
            if (gate is not null) return gate;
        }

        s.ScriptText = def.ToScript();
        var result = await ctx.Get<StrategyRepository>().UpdateAsync(s, v.Canon, ctx.CancellationToken);
        if (result != StrategyMutation.Ok) return OperationResult.Fail($"Not saved: {result}.");

        var holding = s.PositionQty > 0
            ? $"\nNote: the open {s.PositionQty}-share position keeps the exit settings captured at its entry; these apply from the next entry."
            : "";
        return OperationResult.Ok(new { s.Id, Changed = true, Changes = changes, Before = original.ShortId(), After = v.StampId, v.Findings },
            $"{s.Symbol} '{s.Title}': {summary}.\nstamp {original.ShortId()} → {v.StampId}" + holding +
            (v.Findings.Count == 0 ? "" : "\n\n" + v.Render(ctx.Width)));
    }

    /// <summary>Applies the named fields to <paramref name="def"/>; returns a line per change.</summary>
    internal static List<string> Apply(StrategyDefinition def, StrategySetArgs a)
    {
        var changes = new List<string>();
        if (a.Stop is not null && a.StopPercent is not null) throw OperationException.Usage("Pass --stop or --stop-percent, not both.");
        if (a.Target is not null && a.TargetPercent is not null) throw OperationException.Usage("Pass --target or --target-percent, not both.");
        if (a.Qty is not null && a.Notional is not null) throw OperationException.Usage("Pass --qty or --notional, not both.");

        if (a.Stop is { } stop) { def.StopLossPrice = (double)stop; def.StopLossPercent = null; changes.Add($"stop {Fmt.Money(stop)}"); }
        if (a.StopPercent is { } sp) { def.StopLossPercent = (double)sp; def.StopLossPrice = null; changes.Add($"stop {sp.ToString(CultureInfo.InvariantCulture)}%"); }
        if (a.Target is { } t) { def.TakeProfitPrice = (double)t; def.TakeProfitPercent = null; def.TakeProfitTargets.Clear(); changes.Add($"target {Fmt.Money(t)}"); }
        if (a.TargetPercent is { } tp) { def.TakeProfitPercent = (double)tp; def.TakeProfitPrice = null; def.TakeProfitTargets.Clear(); changes.Add($"target {tp.ToString(CultureInfo.InvariantCulture)}%"); }
        if (a.Trailing is { } tr) { def.TrailingStopPercent = tr == 0 ? null : (double)tr; changes.Add(tr == 0 ? "no trailing stop" : $"trailing {tr.ToString(CultureInfo.InvariantCulture)}%"); }
        if (a.Giveback is { } g) { def.PeakGivebackPercent = g == 0 ? null : (double)g; changes.Add(g == 0 ? "no peak giveback" : $"giveback {g.ToString(CultureInfo.InvariantCulture)}%"); }
        if (a.Qty is { } q)
        {
            if (q <= 0) throw OperationException.Usage("--qty must be at least 1 share.");
            def.Quantity = q; def.NotionalAmount = null; changes.Add($"qty {q}");
        }
        if (a.Notional is { } n)
        {
            if (n <= 0) throw OperationException.Usage("--notional must be above $0.");
            def.NotionalAmount = n; def.Quantity = 0; changes.Add($"notional {Fmt.Money(n)}");
        }
        if (a.SellBy is { } sb)
        {
            if (sb.Equals("none", StringComparison.OrdinalIgnoreCase)) { def.ExitTime = null; changes.Add("no sell-by time"); }
            else if (TimeSpan.TryParseExact(sb.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var at) && at < TimeSpan.FromHours(24))
            { def.ExitTime = at; changes.Add($"sell by {at:hh\\:mm} ET"); }
            else throw OperationException.Usage($"--sell-by takes an ET time like 15:45, or none (got '{sb}').");
        }
        if (a.Session is { } session) { def.Session = session; changes.Add($"session {session}"); }
        return changes;
    }
}
