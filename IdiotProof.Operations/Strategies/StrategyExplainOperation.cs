using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

/// <summary>
/// Turns a canon into plain English, deterministically: no LLM, same strategy → same words.
/// Walks the six IdiotScript phases (setup, filters, entry, order, risk, exit) and the branches.
/// </summary>
public static class StrategyNarrator
{
    public static List<(string Phase, List<string> Lines)> Explain(StrategyDefinition d, string? mode = null)
    {
        var sections = new List<(string, List<string>)>();

        var setup = new List<string>
        {
            $"Trades {d.Symbol}{(d.Name is { Length: > 0 } n ? $" (\"{n}\")" : "")} during the {SessionText(d.Session)}." +
            (mode is null ? "" : $" Orders route to {mode}."),
        };
        sections.Add(("Setup", setup));

        var filters = d.EntryConditions.Where(c => c.Phase == StrategyPhase.Filters).Select(Describe).ToList();
        if (d.EntryRollingLowDays is { } lowDays) filters.Add($"price is within {Pct(d.EntryRollingLowBuffer ?? 2.5)} above its {lowDays}-day low (near support)");
        if (d.EntryRollingHighDays is { } highDays) filters.Add($"price is within {Pct(d.EntryRollingHighBuffer ?? 2.5)} below its {highDays}-day high (near the breakout zone)");
        if (filters.Count > 0) sections.Add(("Filters", ["Only considers an entry while " + Join(filters) + "."]));

        var entry = d.EntryConditions.Where(c => c.Phase != StrategyPhase.Filters).Select(Describe).ToList();
        sections.Add(("Entry", [entry.Count == 0
            ? "Has no entry trigger: once active it enters on the first tick it is evaluated."
            : (entry.Count == 1 ? "Enters when " : "Enters when ALL of these are true on the same bar: ") + Join(entry) + "."]));

        var size = d.Quantity > 0 ? $"{d.Quantity} share{(d.Quantity == 1 ? "" : "s")}"
            : d.NotionalAmount is { } amt ? $"{Rendering.Fmt.Money(amt)} worth" : "1 share (no size given)";
        var order = new List<string> { $"{(d.Direction == TradeDirection.Long ? "Buys" : "Sells short")} {size} with a marketable limit order." };
        if (d.ShouldRepeat) order.Add("After it exits it re-arms and can enter again the same day.");
        if (d.IsAutonomous) order.Add("Autonomous: it manages itself without asking.");
        if (d.IsAdaptive) order.Add("Adaptive orders: the limit adjusts to the book.");
        sections.Add(("Order", order));

        var risk = new List<string>();
        if (d.StopLossPrice is { } sp) risk.Add($"Stop loss at {Money(sp)}.");
        else if (d.StopLossPercent is { } spp) risk.Add($"Stop loss {Pct(spp)} {(d.Direction == TradeDirection.Long ? "below" : "above")} the fill.");
        else risk.Add("NO stop loss — the Risk Guardian refuses to let it fire.");
        if (d.TrailingStopPercent is { } tr) risk.Add($"Trailing stop {Pct(tr)} behind the best price since entry.");
        if (d.RollingLowDays is { } rl) risk.Add($"Cuts the loss if price falls to within {Pct(d.RollingLowBuffer ?? 2.5)} of its {rl}-day low.");
        sections.Add(("Risk", risk));

        var exit = new List<string>();
        if (d.TakeProfitTargets.Count > 1)
            exit.Add("Scales out: " + string.Join(", ", d.TakeProfitTargets.Select(t => $"{t.PercentToSell}% at {Money(t.Price)}")) + ".");
        else if (d.TakeProfitPrice is { } tp) exit.Add($"Takes profit at {Money(tp)}.");
        else if (d.TakeProfitTargets.Count == 1) exit.Add($"Takes profit at {Money(d.TakeProfitTargets[0].Price)}.");
        if (d.TakeProfitPercent is { } tpp) exit.Add($"Takes profit {Pct(tpp)} from the fill.");
        if (d.PeakGivebackPercent is { } pg)
            exit.Add($"Sells if price gives back {Pct(pg)} of its run from entry to the peak" + (d.PeakGivebackArmTime is { } arm ? $", from {Time(arm)} ET on." : "."));
        if (d.ExitAtPriorHigh) exit.Add("Sells into the high of day that formed before the entry.");
        if (d.RollingHighDays is { } rh) exit.Add($"Sells as price reaches within {Pct(d.RollingHighBuffer ?? 2.5)} of its {rh}-day high.");
        if (d.ExitTime is { } et) exit.Add($"Sells whatever is still held at {Time(et)} ET.");
        if (exit.Count == 0) exit.Add("Has no profit exit: only the stop closes the position.");
        sections.Add(("Exit", exit));

        var branches = new List<string>();
        foreach (var block in d.ConditionalBlocks)
            for (var i = 0; i < block.Branches.Count; i++)
            {
                var branch = block.Branches[i];
                var lead = branch.Condition is null ? (i == 0 ? "Always" : "Otherwise") : i == 0 ? "If " + Describe(branch.Condition) : "Else if " + Describe(branch.Condition);
                branches.Add(lead + ": " + Overrides(branch.Overrides));
            }
        if (branches.Count > 0) sections.Add(("Branches", branches));
        return sections;
    }

    public static string Render(StrategyDefinition d, string? mode = null)
    {
        var sb = new StringBuilder();
        foreach (var (phase, lines) in Explain(d, mode))
        {
            sb.Append(phase.PadRight(9));
            sb.AppendLine(lines[0]);
            foreach (var l in lines.Skip(1)) sb.Append(' ', 9).AppendLine(l);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>One condition in words.</summary>
    public static string Describe(ICondition c) => c switch
    {
        AndCondition a => $"({Describe(a.Left)} and {Describe(a.Right)})",
        OrCondition o => $"({Describe(o.Left)} or {Describe(o.Right)})",
        NotCondition n => $"not ({Describe(n.Inner)})",
        PriceCondition p => $"price is at or above {Money(p.Price)}",
        PriceLevelCondition l => l.Type switch
        {
            PriceLevelType.HoldsAbove => $"price holds above {Money(l.Level)}",
            PriceLevelType.HoldsBelow => $"price holds below {Money(l.Level)}",
            PriceLevelType.Near => $"price is within {Pct(l.TolerancePercent)} of {Money(l.Level)}",
            PriceLevelType.BreaksAbove => $"price breaks above {Money(l.Level)}",
            PriceLevelType.BreaksBelow => $"price breaks below {Money(l.Level)}",
            _ => Humanize(c.ToScript()),
        },
        PatternCondition pc => pc.Type switch
        {
            PatternType.Breakout => pc.Level is { } lvl ? $"price has broken out above {Money(lvl)}" : "a breakout has happened",
            PatternType.Pullback => pc.Level is { } lvl ? $"price has pulled back toward {Money(lvl)}" : "price has pulled back after the breakout",
            PatternType.BullishEngulfing => "the last candle is a bullish engulfing",
            PatternType.BearishEngulfing => "the last candle is a bearish engulfing",
            PatternType.Hammer => "the last candle is a hammer",
            PatternType.ShootingStar => "the last candle is a shooting star",
            PatternType.Doji => "the last candle is a doji",
            _ => Humanize(c.ToScript()),
        },
        IndicatorCondition i => Indicator(i),
        TimeWindowCondition tw => $"the time is between {Time(tw.StartEt)} and {Time(tw.EndEt)} ET",
        PriceBandCondition pb => $"price is between {Money(pb.Min)} and {Money(pb.Max)}",
        GapBandCondition gb => $"the gap from yesterday's close is between {Pct(gb.MinPercent)} and {Pct(gb.MaxPercent)}",
        _ => Humanize(c.ToScript()),
    };

    private static string Indicator(IndicatorCondition i)
    {
        var p1 = i.Parameter is { } a ? Num(a) : null;
        var p2 = i.Parameter2 is { } b ? Num(b) : null;
        return i.Type switch
        {
            IndicatorType.VwapAbove => "price is above VWAP",
            IndicatorType.VwapBelow => "price is below VWAP",
            IndicatorType.VwapReclaim => "price reclaims VWAP (crosses back above it)",
            IndicatorType.VwapLoss => "price loses VWAP (crosses below it)",
            IndicatorType.EmaAbove => $"price is above the {p1 ?? "?"} EMA",
            IndicatorType.EmaBelow => $"price is below the {p1 ?? "?"} EMA",
            IndicatorType.BetweenEma => $"price is between the {p1 ?? "?"} and {p2 ?? "?"} EMAs",
            IndicatorType.EmaStack => $"the {p1 ?? "?"} EMA is above the {p2 ?? "?"} EMA (an uptrend stack)",
            IndicatorType.ReclaimEma => $"price reclaims the {p1 ?? "?"} EMA",
            IndicatorType.DiPositive => "+DI is above -DI (buyers in control)",
            IndicatorType.DiNegative => "-DI is above +DI (sellers in control)",
            IndicatorType.AdxAbove => $"ADX is above {p1 ?? "20"} (a trending market)",
            IndicatorType.RsiOversold => $"RSI is below {p1 ?? "30"} (oversold)",
            IndicatorType.RsiOverbought => $"RSI is above {p1 ?? "70"} (overbought)",
            IndicatorType.RsiBullishDivergence => "RSI shows a bullish divergence",
            IndicatorType.RsiBearishDivergence => "RSI shows a bearish divergence",
            IndicatorType.HigherLow => "price has made a higher low",
            IndicatorType.LowerHigh => "price has made a lower high",
            IndicatorType.MacdBullish => "MACD is bullish (above its signal line)",
            IndicatorType.MacdBearish => "MACD is bearish (below its signal line)",
            IndicatorType.GapUp => $"it gapped up at least {(i.Parameter is { } g ? Pct(g) : "3%")}",
            IndicatorType.GapDown => $"it gapped down at least {(i.Parameter is { } g2 ? Pct(g2) : "3%")}",
            IndicatorType.VolumeAbove => $"volume is at least {p1 ?? "?"}× its average",
            IndicatorType.AtSupport => "price is at support",
            IndicatorType.AtResistance => "price is at resistance",
            _ => Humanize(i.ToScript()),
        };
    }

    private static string Overrides(StrategyOverrides o)
    {
        var parts = new List<string>();
        if (o.Direction is { } dir) parts.Add(dir == TradeDirection.Long ? "go long" : "go short");
        if (o.EntryConditions.Count > 0) parts.Add("also require " + Join(o.EntryConditions.Select(Describe).ToList()));
        if (o.StopLossPrice is { } sp) parts.Add($"stop at {Money(sp)}");
        if (o.StopLossPercent is { } spp) parts.Add($"stop {Pct(spp)} from the fill");
        if (o.TrailingStopPercent is { } tr) parts.Add($"trail {Pct(tr)}");
        if (o.TakeProfitPrice is { } tp) parts.Add($"take profit at {Money(tp)}");
        if (o.TakeProfitTargets.Count > 0) parts.Add("scale out " + string.Join(", ", o.TakeProfitTargets.Select(t => $"{t.PercentToSell}% at {Money(t.Price)}")));
        return parts.Count == 0 ? "use the base settings." : string.Join(", ", parts) + ".";
    }

    /// <summary>"IsAboveVwap()" → "is above vwap" for anything without a hand-written phrase.</summary>
    private static string Humanize(string script) =>
        Regex.Replace(Regex.Replace(script, @"\(\)$", ""), "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();

    private static string Join(List<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };

    private static string SessionText(TradingSession s) => s switch
    {
        TradingSession.Premarket => "premarket session (4:00–9:30 AM ET)",
        TradingSession.RTH => "regular session (9:30 AM–4:00 PM ET)",
        TradingSession.AfterHours => "after-hours session (4:00–8:00 PM ET)",
        _ => "extended day (4:00 AM–8:00 PM ET)",
    };

    private static string Money(double v) => Rendering.Fmt.Money((decimal)v);

    private static string Pct(double v) => Num(v) + "%";

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Time(TimeSpan t) => new DateTime(2000, 1, 1).Add(t).ToString("h:mm tt", CultureInfo.InvariantCulture);
}

[Operation("strategy explain", Summary = "Explain a strategy in plain English, phase by phase — generated from its canon, no LLM, the same words every time.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy explain NVDA")]
public sealed class StrategyExplainOperation : IOperation<StrategyRefArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyRefArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var def = StrategyRefs.RequireDefinition(s);
        var mode = OrderIntent.ParseMode(s.BrokerMode).ToString();
        var sections = StrategyNarrator.Explain(def, mode);
        var state = s.IsActive ? "It is ACTIVE — the Monitor evaluates it every tick." : "It is inactive.";
        if (s.PositionQty > 0) state += $" It holds {s.PositionQty} shares now.";
        return OperationResult.Ok(new
        {
            s.Id, Stamp = def.ShortId(), s.Symbol, s.Title, Mode = mode, s.IsActive,
            Sections = sections.Select(x => new { Phase = x.Phase, Lines = x.Lines }).ToList(),
        }, $"{s.Symbol} '{s.Title}' ({def.ShortId()})\n\n{StrategyNarrator.Render(def, mode)}\n\n{state}");
    }
}
