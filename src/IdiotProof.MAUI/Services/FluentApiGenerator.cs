using IdiotProof.MAUI.Models;

namespace IdiotProof.MAUI.Services;

/// <summary>
/// Walks an ordered list of ScriptSegments and produces fluent API code.
/// </summary>
public static class FluentApiGenerator
{
    public static string Generate(ScriptStrategy strategy)
    {
        if (strategy.Segments.Count == 0)
            return "// Empty strategy — add segments from the toolbox";

        var lines = new List<string> { "Stock" };

        foreach (var seg in strategy.Segments)
            lines.Add($"    {SegmentToCode(seg)}");

        lines.Add("    .Build();");
        return string.Join("\n", lines);
    }

    private static string SegmentToCode(ScriptSegment seg)
    {
        var key = seg.SegmentKey;
        var p = seg.Parameters;

        return key switch
        {
            // Start
            "Ticker" => $".Ticker(\"{Val(p, "symbol")}\")",

            // Session
            "SessionDuration" => $".SessionDuration(TradingSession.{Val(p, "session")})",
            "SessionStart" => $".Start({FormatTime(Val(p, "time"))})",
            "SessionEnd" => $".End({FormatTime(Val(p, "time"))})",

            // Price Conditions
            "Breakout" => $".Breakout({Num(p, "level")})",
            "Pullback" => $".Pullback({Num(p, "level")})",
            "IsPriceAbove" => $".IsPriceAbove({Num(p, "level")})",
            "IsPriceBelow" => $".IsPriceBelow({Num(p, "level")})",
            "IsPriceBetween" => $".IsPriceBetween({Num(p, "low")}, {Num(p, "high")})",
            "HoldsAbove" => $".HoldsAbove({Num(p, "level")})",
            "HoldsBelow" => $".HoldsBelow({Num(p, "level")})",
            "IsNear" => $".IsNear({Num(p, "level")}, {Num(p, "tolerance")})",
            "GapUp" => $".IsGapUp({Num(p, "percent")})",
            "GapDown" => $".IsGapDown({Num(p, "percent")})",

            // VWAP
            "IsAboveVwap" => ".IsAboveVwap()",
            "IsBelowVwap" => ".IsBelowVwap()",
            "IsCloseAboveVwap" => ".IsCloseAboveVwap()",
            "IsVwapRejection" => ".IsVwapRejection()",

            // Indicators
            "IsEmaAbove" => $".IsEmaAbove({Int(p, "period")})",
            "IsEmaBelow" => $".IsEmaBelow({Int(p, "period")})",
            "IsEmaBetween" => $".IsEmaBetween({Int(p, "lower")}, {Int(p, "upper")})",
            "IsEmaTurningUp" => $".IsEmaTurningUp({Int(p, "period")})",
            "IsRsiOversold" => $".IsRsiOversold({Num(p, "threshold")})",
            "IsRsiOverbought" => $".IsRsiOverbought({Num(p, "threshold")})",
            "IsAdxAbove" => $".IsAdxAbove({Num(p, "threshold")})",
            "IsMacdBullish" => ".IsMacdBullish()",
            "IsMacdBearish" => ".IsMacdBearish()",
            "IsDiPositive" => ".IsDiPositive()",
            "IsDiNegative" => ".IsDiNegative()",
            "IsVolumeAbove" => $".IsVolumeAbove({Num(p, "multiplier")})",
            "IsMomentum" => BuildMomentum(p),
            "IsRoc" => BuildRoc(p),
            "IsHigherLows" => $".IsHigherLows({Int(p, "lookback")})",
            "IsLowerHighs" => $".IsLowerHighs({Int(p, "lookback")})",

            // Orders
            "Long" or "Buy" => BuildOrder(key, p),
            "Short" or "Sell" => BuildOrder(key, p),
            "Order" => BuildFullOrder(p),
            "Close" => BuildClose(p),

            // Risk Management
            "StopLoss" => $".StopLoss({Num(p, "price")})",
            "TakeProfit" => $".TakeProfit({Num(p, "price")})",
            "TakeProfitRange" => $".TakeProfit({Num(p, "low")}, {Num(p, "high")})",
            "TrailingStopLoss" => $".TrailingStopLoss({Num(p, "percent")})",
            "TrailingStopLossAtr" => $".TrailingStopLossAtr({Num(p, "multiplier")}, {Int(p, "period")})",
            "AdaptiveOrder" => ".AdaptiveOrder()",

            // Exit
            "ExitTime" or "ClosePosition" => BuildExitTime(p),
            "ExitStrategy" => BuildExitTime(p),
            "IsProfitable" => ".IsProfitable()",

            // Config
            "TimeInForce" => $".TimeInForce(TimeInForce.{Val(p, "tif")})",
            "OutsideRTH" => $".OutsideRTH({Bool(p, "allow")})",
            "AllOrNone" => $".AllOrNone({Bool(p, "enabled")})",
            "OrderType" => $".OrderType(OrderType.{Val(p, "type")})",
            "Repeat" => $".Repeat({Bool(p, "enabled")})",
            "AutonomousTrading" => ".AutonomousTrading()",
            "Name" => $".Name(\"{Val(p, "name")}\")",
            "Description" => $".Description(\"{Val(p, "text")}\")",
            "Author" => $".Author(\"{Val(p, "name")}\")",

            _ => $".{key}() // unknown"
        };
    }

    private static string BuildOrder(string key, List<ScriptParam> p)
    {
        var qty = Int(p, "quantity");
        var price = Val(p, "priceType");
        if (string.IsNullOrEmpty(price) || price == "Current")
            return $".{key}({qty})";
        return $".{key}({qty}, Price.{price})";
    }

    private static string BuildFullOrder(List<ScriptParam> p)
    {
        var dir = Val(p, "direction");
        var method = dir == "Short" ? "Short" : "Long";
        var qty = Int(p, "quantity");
        var price = Val(p, "priceType");
        if (string.IsNullOrEmpty(price) || price == "Current")
            return $".{method}({qty})";
        return $".{method}({qty}, Price.{price})";
    }

    private static string BuildClose(List<ScriptParam> p)
    {
        var qty = Val(p, "quantity");
        return string.IsNullOrEmpty(qty) ? ".Close()" : $".Close({qty})";
    }

    private static string BuildMomentum(List<ScriptParam> p)
    {
        var dir = Val(p, "direction");
        var threshold = Num(p, "threshold");
        return dir == "Below"
            ? $".IsMomentumBelow({threshold})"
            : $".IsMomentumAbove({threshold})";
    }

    private static string BuildRoc(List<ScriptParam> p)
    {
        var dir = Val(p, "direction");
        var pct = Num(p, "percent");
        var period = Int(p, "period");
        return dir == "Below"
            ? $".IsRocBelow({pct}, {period})"
            : $".IsRocAbove({pct}, {period})";
    }

    private static string BuildExitTime(List<ScriptParam> p)
    {
        var time = FormatTime(Val(p, "time"));
        var profitable = Val(p, "onlyIfProfitable");
        if (profitable == "True" || profitable == "true")
            return $".ClosePosition({time}, onlyIfProfitable: true)";
        return $".ClosePosition({time})";
    }

    // ── Value helpers ─────────────────────────────────────────────

    private static string Val(List<ScriptParam> p, string name) =>
        p.FirstOrDefault(x => x.Name == name)?.Value?.ToString() ?? "";

    private static string Num(List<ScriptParam> p, string name)
    {
        var raw = Val(p, name);
        return double.TryParse(raw, out var d) ? d.ToString("G") : "0";
    }

    private static string Int(List<ScriptParam> p, string name)
    {
        var raw = Val(p, name);
        return int.TryParse(raw, out var i) ? i.ToString() : "0";
    }

    private static string Bool(List<ScriptParam> p, string name)
    {
        var raw = Val(p, name);
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
    }

    private static string FormatTime(string? val)
    {
        if (string.IsNullOrEmpty(val)) return "new TimeOnly(16, 0)";
        if (TimeOnly.TryParse(val, out var t))
            return $"new TimeOnly({t.Hour}, {t.Minute})";
        return "new TimeOnly(16, 0)";
    }
}
