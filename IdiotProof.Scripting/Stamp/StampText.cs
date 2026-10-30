using System.Globalization;

namespace IdiotProof.Scripting.Stamp;

/// <summary>
/// Plain-English wording for conditions and levels on a stamp. Scripting
/// cannot see IdiotProof.Core's DslGlossary, so this is the stamp's own
/// compact phrasing — one short clause per condition, numbers always
/// InvariantCulture so a comma-decimal host can never print "$4,50". The
/// exact DSL lives in the armored payload; this is the human view.
/// </summary>
internal static class StampText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Num(double v) => v.ToString("0.####", Inv);
    public static string Pct(double v) => v.ToString("0.##", Inv) + "%";
    public static string Money(double v) => "$" + v.ToString(v >= 1000 ? "#,0.00" : "0.00##", Inv);
    public static string Money(decimal v) => "$" + v.ToString("#,0.##", Inv);
    public static string Time(TimeSpan t) => t.ToString(@"hh\:mm", Inv);

    public static string Size(StrategyDefinition def) =>
        def.NotionalAmount is { } n ? $"{Money(n)} notional"
        : def.Quantity == 1 ? "1 share"
        : def.Quantity > 0 ? $"{def.Quantity.ToString("#,0", Inv)} shares"
        : "workspace default size";

    public static string Session(Models.TradingSession s) => s switch
    {
        Models.TradingSession.Premarket => "Premarket (04:00-09:30 ET)",
        Models.TradingSession.RTH => "Regular hours (09:30-16:00 ET)",
        Models.TradingSession.AfterHours => "After hours (16:00-20:00 ET)",
        Models.TradingSession.Extended => "Extended (04:00-20:00 ET)",
        _ => s.ToString(),
    };

    /// <summary>One clause for a condition; composed conditions read as boolean prose.</summary>
    public static string Describe(ICondition c) => c switch
    {
        AndCondition a => $"{Group(a.Left, a)} AND {Group(a.Right, a)}",
        OrCondition o => $"{Group(o.Left, o)} OR {Group(o.Right, o)}",
        NotCondition n => $"NOT {Group(n.Inner, n)}",
        IndicatorCondition i => Indicator(i),
        PatternCondition p => Pattern(p),
        PriceCondition pc => $"price at or above {Money(pc.Price)}",
        PriceLevelCondition pl => pl.Type switch
        {
            PriceLevelType.HoldsAbove => $"holds above {Money(pl.Level)}",
            PriceLevelType.HoldsBelow => $"holds below {Money(pl.Level)}",
            PriceLevelType.Near => $"within {Pct(pl.TolerancePercent)} of {Money(pl.Level)}",
            PriceLevelType.BreaksAbove => $"breaks above {Money(pl.Level)}",
            PriceLevelType.BreaksBelow => $"breaks below {Money(pl.Level)}",
            _ => c.ToScript(),
        },
        TimeWindowCondition tw => $"entry window {Time(tw.StartEt)}-{Time(tw.EndEt)} ET",
        PriceBandCondition pb => $"price between {Money(pb.Min)} and {Money(pb.Max)}",
        GapBandCondition gb => $"gap between {Pct(gb.MinPercent)} and {Pct(gb.MaxPercent)}",
        _ => c.ToScript(),
    };

    // Parenthesize a child only when it is a different boolean operator than
    // its parent, so "A AND B AND C" stays flat and "(A OR B) AND C" is exact.
    private static string Group(ICondition child, ICondition parent)
    {
        var text = Describe(child);
        var isOp = child is AndCondition or OrCondition;
        return isOp && child.GetType() != parent.GetType() || parent is NotCondition && isOp ? $"({text})" : text;
    }

    private static string Indicator(IndicatorCondition i)
    {
        var p = i.Parameter;
        var p2 = i.Parameter2;
        string P(double fallback) => Num(p ?? fallback);
        return i.Type switch
        {
            IndicatorType.VwapAbove => "price above VWAP",
            IndicatorType.VwapBelow => "price below VWAP",
            IndicatorType.VwapReclaim => "reclaims VWAP (crosses up)",
            IndicatorType.VwapLoss => "loses VWAP (crosses down)",
            IndicatorType.EmaAbove => $"price above EMA {P(0)}",
            IndicatorType.EmaBelow => $"price below EMA {P(0)}",
            IndicatorType.BetweenEma => $"price between EMA {P(0)} and EMA {Num(p2 ?? 0)}",
            IndicatorType.EmaStack => $"EMA {P(0)} stacked above EMA {Num(p2 ?? 0)}",
            IndicatorType.ReclaimEma => $"reclaims EMA {P(0)} (crosses up)",
            IndicatorType.DiPositive => "+DI above -DI (bull trend)",
            IndicatorType.DiNegative => "-DI above +DI (bear trend)",
            IndicatorType.AdxAbove => $"ADX at least {P(20)} (trending)",
            IndicatorType.RsiOversold => $"RSI at or below {P(30)} (oversold)",
            IndicatorType.RsiOverbought => $"RSI at or above {P(70)} (overbought)",
            IndicatorType.RsiBullishDivergence => "bullish RSI divergence",
            IndicatorType.RsiBearishDivergence => "bearish RSI divergence",
            IndicatorType.HigherLow => "higher low printed",
            IndicatorType.LowerHigh => "lower high printed",
            IndicatorType.MacdBullish => "MACD above its signal line",
            IndicatorType.MacdBearish => "MACD below its signal line",
            IndicatorType.GapUp => $"gapped up at least {Pct(p ?? 3)}",
            IndicatorType.GapDown => $"gapped down at least {Pct(p ?? 3)}",
            IndicatorType.VolumeAbove => $"volume at least {P(1.5)}x average",
            IndicatorType.AtSupport => $"within {Pct(p ?? 0.5)} of swing-low support",
            IndicatorType.AtResistance => $"within {Pct(p ?? 0.5)} of swing-high resistance",
            _ => i.ToScript(),
        };
    }

    private static string Pattern(PatternCondition p) => p.Type switch
    {
        PatternType.Breakout => p.Level is { } b ? $"breakout through {Money(b)}" : "breakout (no level: never latches)",
        PatternType.Pullback => p.Level is { } s ? $"pullback to {Money(s)} support" : "pullback off the window high",
        PatternType.BullishEngulfing => "bullish engulfing candle",
        PatternType.BearishEngulfing => "bearish engulfing candle",
        PatternType.Hammer => "hammer candle",
        PatternType.ShootingStar => "shooting star candle",
        PatternType.Doji => "doji candle",
        _ => p.ToScript(),
    };

    /// <summary>What a conditional branch changes, as a short " · "-joined list.</summary>
    public static string Overrides(StrategyOverrides o, string sep)
    {
        var parts = new List<string>();
        if (o.Direction is { } d) parts.Add(d == Models.TradeDirection.Short ? "SHORT" : "LONG");
        foreach (var c in o.EntryConditions) parts.Add("also " + Describe(c));
        if (o.TakeProfitTargets.Count > 0)
            parts.Add("targets " + string.Join(" / ", o.TakeProfitTargets.Select(t => $"{t.Label} {Money(t.Price)}")));
        else if (o.TakeProfitPrice is { } tp) parts.Add($"target {Money(tp)}");
        if (o.StopLossPrice is { } sp) parts.Add($"stop {Money(sp)}");
        if (o.StopLossPercent is { } sc) parts.Add($"stop {Pct(sc)}");
        if (o.TrailingStopPercent is { } ts) parts.Add($"trail {Pct(ts)}");
        return parts.Count > 0 ? string.Join(sep, parts) : "no change (base plan)";
    }
}
