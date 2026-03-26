using IdiotProof.MAUI.Models;

namespace IdiotProof.MAUI.Services;

/// <summary>
/// Master catalog of every available strategy segment, organized by category.
/// Each template defines the segment's parameters and their input types.
/// Call Create("key") to get a fresh clone ready for the grid.
/// </summary>
public static class SegmentCatalog
{
    // ── Category definitions ──────────────────────────────────────
    public static readonly CatalogCategory[] Categories =
    [
        new("Start",       "#4CAF50", ["Ticker"]),
        new("Session",     "#9C27B0", ["SessionDuration", "SessionStart", "SessionEnd"]),
        new("Price",       "#2196F3", ["Breakout", "Pullback", "IsPriceAbove", "IsPriceBelow", "IsPriceBetween", "HoldsAbove", "HoldsBelow", "IsNear", "GapUp", "GapDown"]),
        new("VWAP",        "#00BCD4", ["IsAboveVwap", "IsBelowVwap", "IsCloseAboveVwap", "IsVwapRejection"]),
        new("Indicators",  "#FF9800", ["IsEmaAbove", "IsEmaBelow", "IsEmaBetween", "IsEmaTurningUp", "IsRsiOversold", "IsRsiOverbought", "IsAdxAbove", "IsMacdBullish", "IsMacdBearish", "IsDiPositive", "IsDiNegative", "IsVolumeAbove", "IsMomentum", "IsRoc", "IsHigherLows", "IsLowerHighs"]),
        new("Order",       "#4CAF50", ["Long", "Short", "Buy", "Sell", "Order", "Close"]),
        new("Risk",        "#FFC107", ["StopLoss", "TakeProfit", "TakeProfitRange", "TrailingStopLoss", "TrailingStopLossAtr", "AdaptiveOrder"]),
        new("Exit",        "#F44336", ["ExitTime", "ClosePosition", "ExitStrategy", "IsProfitable"]),
        new("Config",      "#607D8B", ["TimeInForce", "OutsideRTH", "AllOrNone", "OrderType", "Repeat", "AutonomousTrading", "Name", "Description", "Author"]),
    ];

    // ── Template registry ─────────────────────────────────────────
    private static readonly Dictionary<string, Func<ScriptSegment>> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Start ─────────────────────────────────────────────────
        ["Ticker"] = () => Seg("Ticker", ".Ticker()", "Start", "#4CAF50", "$",
            Param("symbol", "Symbol", ParamKind.String, required: true, placeholder: "NVDA")),

        // ── Session ───────────────────────────────────────────────
        ["SessionDuration"] = () => Seg("SessionDuration", ".SessionDuration()", "Session", "#9C27B0", "clock",
            EnumParam("session", "Trading Session", "TradingSession",
                ["Premarket", "RTH", "AfterHours", "Extended"], defaultVal: "RTH")),

        ["SessionStart"] = () => Seg("SessionStart", ".Start()", "Session", "#9C27B0", "play",
            Param("time", "Start Time", ParamKind.Time, required: true, helpText: "Session start (Eastern)")),

        ["SessionEnd"] = () => Seg("SessionEnd", ".End()", "Session", "#9C27B0", "stop",
            Param("time", "End Time", ParamKind.Time, required: true, helpText: "Session end (Eastern)")),

        // ── Price Conditions ──────────────────────────────────────
        ["Breakout"] = () => Seg("Breakout", ".Breakout()", "Price", "#2196F3", "/^",
            PriceParam("level", "Breakout Level", helpText: "Price must reach or exceed this level")),

        ["Pullback"] = () => Seg("Pullback", ".Pullback()", "Price", "#2196F3", "\\v",
            PriceParam("level", "Pullback Level", helpText: "Price must drop to or below this level")),

        ["IsPriceAbove"] = () => Seg("IsPriceAbove", ".IsPriceAbove()", "Price", "#2196F3", "^^",
            PriceParam("level", "Price Level", helpText: "Price >= this level")),

        ["IsPriceBelow"] = () => Seg("IsPriceBelow", ".IsPriceBelow()", "Price", "#2196F3", "vv",
            PriceParam("level", "Price Level", helpText: "Price < this level")),

        ["IsPriceBetween"] = () => Seg("IsPriceBetween", ".IsPriceBetween()", "Price", "#2196F3", "<>",
            PriceParam("low", "Low Price", helpText: "Lower bound of price range"),
            PriceParam("high", "High Price", helpText: "Upper bound of price range")),

        ["HoldsAbove"] = () => Seg("HoldsAbove", ".HoldsAbove()", "Price", "#2196F3", "^^",
            PriceParam("level", "Hold Level", helpText: "Price must sustain above this level")),

        ["HoldsBelow"] = () => Seg("HoldsBelow", ".HoldsBelow()", "Price", "#2196F3", "vv",
            PriceParam("level", "Hold Level", helpText: "Price must sustain below this level")),

        ["IsNear"] = () => Seg("IsNear", ".IsNear()", "Price", "#2196F3", "~=",
            PriceParam("level", "Price Level", helpText: "Target price"),
            Param("tolerance", "Tolerance %", ParamKind.Double, required: true, min: 0.1, max: 10, step: 0.1, defaultVal: 1.0, helpText: "Percentage tolerance")),

        ["GapUp"] = () => Seg("GapUp", ".IsGapUp()", "Price", "#2196F3", "G+",
            Param("percent", "Gap %", ParamKind.Double, required: true, min: 0.1, max: 100, step: 0.5, defaultVal: 3.0, helpText: "Minimum gap up percentage")),

        ["GapDown"] = () => Seg("GapDown", ".IsGapDown()", "Price", "#2196F3", "G-",
            Param("percent", "Gap %", ParamKind.Double, required: true, min: 0.1, max: 100, step: 0.5, defaultVal: 3.0, helpText: "Minimum gap down percentage")),

        // ── VWAP ──────────────────────────────────────────────────
        ["IsAboveVwap"] = () => Seg("IsAboveVwap", ".IsAboveVwap()", "VWAP", "#00BCD4", "V+"),

        ["IsBelowVwap"] = () => Seg("IsBelowVwap", ".IsBelowVwap()", "VWAP", "#00BCD4", "V-"),

        ["IsCloseAboveVwap"] = () => Seg("IsCloseAboveVwap", ".IsCloseAboveVwap()", "VWAP", "#00BCD4", "VC"),

        ["IsVwapRejection"] = () => Seg("IsVwapRejection", ".IsVwapRejection()", "VWAP", "#00BCD4", "VR"),

        // ── Indicators ────────────────────────────────────────────
        ["IsEmaAbove"] = () => Seg("IsEmaAbove", ".IsEmaAbove()", "Indicators", "#FF9800", "E+",
            Param("period", "EMA Period", ParamKind.Integer, required: true, min: 1, max: 200, step: 1, defaultVal: 9, helpText: "EMA lookback period")),

        ["IsEmaBelow"] = () => Seg("IsEmaBelow", ".IsEmaBelow()", "Indicators", "#FF9800", "E-",
            Param("period", "EMA Period", ParamKind.Integer, required: true, min: 1, max: 200, step: 1, defaultVal: 9, helpText: "EMA lookback period")),

        ["IsEmaBetween"] = () => Seg("IsEmaBetween", ".IsEmaBetween()", "Indicators", "#FF9800", "E~",
            Param("lower", "Lower EMA", ParamKind.Integer, required: true, min: 1, max: 200, step: 1, defaultVal: 9, helpText: "Shorter EMA period"),
            Param("upper", "Upper EMA", ParamKind.Integer, required: true, min: 1, max: 200, step: 1, defaultVal: 21, helpText: "Longer EMA period")),

        ["IsEmaTurningUp"] = () => Seg("IsEmaTurningUp", ".IsEmaTurningUp()", "Indicators", "#FF9800", "E^",
            Param("period", "EMA Period", ParamKind.Integer, required: true, min: 1, max: 200, step: 1, defaultVal: 9, helpText: "EMA slope turning positive")),

        ["IsRsiOversold"] = () => Seg("IsRsiOversold", ".IsRsiOversold()", "Indicators", "#FF9800", "R-",
            Param("threshold", "RSI Threshold", ParamKind.Double, required: true, min: 0, max: 100, step: 1, defaultVal: 30.0, helpText: "RSI <= this value is oversold")),

        ["IsRsiOverbought"] = () => Seg("IsRsiOverbought", ".IsRsiOverbought()", "Indicators", "#FF9800", "R+",
            Param("threshold", "RSI Threshold", ParamKind.Double, required: true, min: 0, max: 100, step: 1, defaultVal: 70.0, helpText: "RSI >= this value is overbought")),

        ["IsAdxAbove"] = () => Seg("IsAdxAbove", ".IsAdxAbove()", "Indicators", "#FF9800", "AX",
            Param("threshold", "ADX Threshold", ParamKind.Double, required: true, min: 0, max: 100, step: 1, defaultVal: 25.0, helpText: "ADX >= this = strong trend")),

        ["IsMacdBullish"] = () => Seg("IsMacdBullish", ".IsMacdBullish()", "Indicators", "#FF9800", "M+"),

        ["IsMacdBearish"] = () => Seg("IsMacdBearish", ".IsMacdBearish()", "Indicators", "#FF9800", "M-"),

        ["IsDiPositive"] = () => Seg("IsDiPositive", ".IsDiPositive()", "Indicators", "#FF9800", "D+"),

        ["IsDiNegative"] = () => Seg("IsDiNegative", ".IsDiNegative()", "Indicators", "#FF9800", "D-"),

        ["IsVolumeAbove"] = () => Seg("IsVolumeAbove", ".IsVolumeAbove()", "Indicators", "#FF9800", "VA",
            Param("multiplier", "Volume Multiplier", ParamKind.Double, required: true, min: 0.1, max: 50, step: 0.1, defaultVal: 1.5, helpText: "Volume >= X times average")),

        ["IsMomentum"] = () => Seg("IsMomentum", ".IsMomentum()", "Indicators", "#FF9800", "MO",
            EnumParam("direction", "Direction", "MomentumDirection", ["Above", "Below"], defaultVal: "Above"),
            Param("threshold", "Threshold", ParamKind.Double, required: true, min: -100, max: 100, step: 0.5, defaultVal: 0.0, helpText: "Momentum threshold value")),

        ["IsRoc"] = () => Seg("IsRoc", ".IsRoc()", "Indicators", "#FF9800", "RC",
            EnumParam("direction", "Direction", "RocDirection", ["Above", "Below"], defaultVal: "Above"),
            Param("percent", "ROC %", ParamKind.Double, required: true, min: -100, max: 100, step: 0.5, defaultVal: 1.0, helpText: "Rate of Change percentage threshold"),
            Param("period", "Lookback", ParamKind.Integer, required: true, min: 1, max: 50, step: 1, defaultVal: 10, helpText: "Number of bars for ROC calculation")),

        ["IsHigherLows"] = () => Seg("IsHigherLows", ".IsHigherLows()", "Indicators", "#FF9800", "HL",
            Param("lookback", "Lookback Bars", ParamKind.Integer, required: true, min: 2, max: 50, step: 1, defaultVal: 3, helpText: "Number of bars to check for higher lows pattern")),

        ["IsLowerHighs"] = () => Seg("IsLowerHighs", ".IsLowerHighs()", "Indicators", "#FF9800", "LH",
            Param("lookback", "Lookback Bars", ParamKind.Integer, required: true, min: 2, max: 50, step: 1, defaultVal: 3, helpText: "Number of bars to check for lower highs pattern")),

        // ── Orders ────────────────────────────────────────────────
        ["Long"] = () => Seg("Long", ".Long()", "Order", "#4CAF50", "L",
            Param("quantity", "Shares", ParamKind.Integer, required: true, min: 1, step: 1, defaultVal: 100, helpText: "Number of shares"),
            EnumParam("priceType", "Price Type", "PriceType", ["Current", "Bid", "Ask", "VWAP"], defaultVal: "Current")),

        ["Buy"] = () => Seg("Buy", ".Buy()", "Order", "#4CAF50", "B",
            Param("quantity", "Shares", ParamKind.Integer, required: true, min: 1, step: 1, defaultVal: 100, helpText: "Number of shares to buy"),
            EnumParam("priceType", "Price Type", "PriceType", ["Current", "Bid", "Ask", "VWAP"], defaultVal: "Current")),

        ["Short"] = () => Seg("Short", ".Short()", "Order", "#F44336", "S",
            Param("quantity", "Shares", ParamKind.Integer, required: true, min: 1, step: 1, defaultVal: 100, helpText: "Number of shares"),
            EnumParam("priceType", "Price Type", "PriceType", ["Current", "Bid", "Ask", "VWAP"], defaultVal: "Current")),

        ["Sell"] = () => Seg("Sell", ".Sell()", "Order", "#F44336", "S",
            Param("quantity", "Shares", ParamKind.Integer, required: true, min: 1, step: 1, defaultVal: 100, helpText: "Number of shares to sell"),
            EnumParam("priceType", "Price Type", "PriceType", ["Current", "Bid", "Ask", "VWAP"], defaultVal: "Current")),

        ["Order"] = () => Seg("Order", ".Order()", "Order", "#FF9800", "O",
            EnumParam("direction", "Direction", "Direction", ["Long", "Short"], defaultVal: "Long"),
            Param("quantity", "Shares", ParamKind.Integer, required: true, min: 1, step: 1, defaultVal: 100),
            EnumParam("priceType", "Price Type", "PriceType", ["Current", "Bid", "Ask", "VWAP", "Limit"], defaultVal: "Current"),
            EnumParam("orderType", "Order Type", "OrderType", ["Market", "Limit", "Stop", "StopLimit"], defaultVal: "Limit")),

        ["Close"] = () => Seg("Close", ".Close()", "Order", "#F44336", "X",
            Param("quantity", "Shares", ParamKind.Integer, required: false, min: 1, step: 1, helpText: "Leave empty to close all")),

        // ── Risk Management ───────────────────────────────────────
        ["StopLoss"] = () => Seg("StopLoss", ".StopLoss()", "Risk", "#FFC107", "SL",
            PriceParam("price", "Stop Price", helpText: "Exit if price drops to this level")),

        ["TakeProfit"] = () => Seg("TakeProfit", ".TakeProfit()", "Risk", "#FFC107", "TP",
            PriceParam("price", "Target Price", helpText: "Take profit at this price")),

        ["TakeProfitRange"] = () => Seg("TakeProfitRange", ".TakeProfit(low, high)", "Risk", "#FFC107", "TPR",
            PriceParam("low", "Conservative Target", helpText: "Take profit when trend is weak"),
            PriceParam("high", "Aggressive Target", helpText: "Take profit when trend is strong")),

        ["TrailingStopLoss"] = () => Seg("TrailingStopLoss", ".TrailingStopLoss()", "Risk", "#FFC107", "TSL",
            Param("percent", "Trail %", ParamKind.Percentage, required: true, min: 0.5, max: 50, step: 0.5, defaultVal: 5.0, helpText: "Percentage below peak price")),

        ["TrailingStopLossAtr"] = () => Seg("TrailingStopLossAtr", ".TrailingStopLossAtr()", "Risk", "#FFC107", "ATR",
            Param("multiplier", "ATR Multiplier", ParamKind.Double, required: true, min: 0.5, max: 10, step: 0.5, defaultVal: 2.0, helpText: "Multiplier on ATR for stop distance"),
            Param("period", "ATR Period", ParamKind.Integer, required: true, min: 1, max: 50, step: 1, defaultVal: 14, helpText: "ATR lookback bars")),

        ["AdaptiveOrder"] = () => Seg("AdaptiveOrder", ".AdaptiveOrder()", "Risk", "#FFC107", "AO"),

        // ── Exit ──────────────────────────────────────────────────
        ["ExitTime"] = () => Seg("ExitTime", ".ClosePosition()", "Exit", "#F44336", "ET",
            Param("time", "Exit Time", ParamKind.Time, required: true, helpText: "Close position at this time (Eastern)"),
            Param("onlyIfProfitable", "Only If Profitable", ParamKind.Boolean, required: false, defaultVal: true, helpText: "Only close if position is profitable")),

        ["ClosePosition"] = () => Seg("ClosePosition", ".ClosePosition()", "Exit", "#F44336", "CP",
            Param("time", "Exit Time", ParamKind.Time, required: true, helpText: "Close position at this time (Eastern)"),
            Param("onlyIfProfitable", "Only If Profitable", ParamKind.Boolean, required: false, defaultVal: true)),

        ["ExitStrategy"] = () => Seg("ExitStrategy", ".ExitStrategy()", "Exit", "#F44336", "ES",
            Param("time", "Exit Time", ParamKind.Time, required: true, helpText: "Time-based exit (Eastern)"),
            Param("onlyIfProfitable", "Only If Profitable", ParamKind.Boolean, required: false, defaultVal: false, helpText: "Only exit if position is profitable")),

        ["IsProfitable"] = () => Seg("IsProfitable", ".IsProfitable()", "Exit", "#F44336", "$$"),

        // ── Config ────────────────────────────────────────────────
        ["TimeInForce"] = () => Seg("TimeInForce", ".TimeInForce()", "Config", "#607D8B", "TIF",
            EnumParam("tif", "Time In Force", "TimeInForce",
                ["DAY", "GTC", "IOC", "FOK"], defaultVal: "DAY")),

        ["OutsideRTH"] = () => Seg("OutsideRTH", ".OutsideRTH()", "Config", "#607D8B", "RTH",
            Param("allow", "Allow Outside RTH", ParamKind.Boolean, required: false, defaultVal: true, helpText: "Allow fills during pre/after market")),

        ["AllOrNone"] = () => Seg("AllOrNone", ".AllOrNone()", "Config", "#607D8B", "AON",
            Param("enabled", "All Or None", ParamKind.Boolean, required: false, defaultVal: true, helpText: "Order must fill completely or not at all")),

        ["OrderType"] = () => Seg("OrderType", ".OrderType()", "Config", "#607D8B", "OT",
            EnumParam("type", "Order Type", "OrderType", ["Market", "Limit", "Stop", "StopLimit", "TrailingStop"], defaultVal: "Limit")),

        ["Repeat"] = () => Seg("Repeat", ".Repeat()", "Config", "#607D8B", "RPT",
            Param("enabled", "Repeat", ParamKind.Boolean, required: false, defaultVal: true, helpText: "Strategy resets and fires again after completion")),

        ["AutonomousTrading"] = () => Seg("AutonomousTrading", ".AutonomousTrading()", "Config", "#607D8B", "AI"),

        ["Name"] = () => Seg("Name", ".Name()", "Config", "#607D8B", "N",
            Param("name", "Strategy Name", ParamKind.String, required: true, placeholder: "My Strategy")),

        ["Description"] = () => Seg("Description", ".Description()", "Config", "#607D8B", "D",
            Param("text", "Description", ParamKind.String, required: false, placeholder: "What this strategy does")),

        ["Author"] = () => Seg("Author", ".Author()", "Config", "#607D8B", "A",
            Param("name", "Author", ParamKind.String, required: false, placeholder: "Your name")),
    };

    /// <summary>
    /// Creates a fresh segment instance from a catalog template key.
    /// </summary>
    public static ScriptSegment Create(string key)
    {
        if (!Templates.TryGetValue(key, out var factory))
            throw new ArgumentException($"Unknown segment key: {key}");

        var seg = factory();
        seg.Id = Guid.NewGuid().ToString("N");
        // Set default values as current values
        foreach (var p in seg.Parameters)
            p.Value ??= p.DefaultValue;
        return seg;
    }

    /// <summary>
    /// All available segment keys.
    /// </summary>
    public static IEnumerable<string> AllKeys => Templates.Keys;

    /// <summary>
    /// Check if a key exists in the catalog.
    /// </summary>
    public static bool Exists(string key) => Templates.ContainsKey(key);

    // ── Builder helpers ───────────────────────────────────────────

    private static ScriptSegment Seg(string key, string displayName, string category, string color, string icon, params ScriptParam[] parameters) => new()
    {
        SegmentKey = key,
        DisplayName = displayName,
        Category = category,
        Color = color,
        Icon = icon,
        Parameters = parameters.ToList()
    };

    private static ScriptParam Param(string name, string label, ParamKind kind,
        bool required = true, double? min = null, double? max = null, double? step = null,
        object? defaultVal = null, string? placeholder = null, string? helpText = null) => new()
    {
        Name = name,
        Label = label,
        Kind = kind,
        IsRequired = required,
        Min = min,
        Max = max,
        Step = step,
        DefaultValue = defaultVal,
        Placeholder = placeholder,
        HelpText = helpText
    };

    private static ScriptParam PriceParam(string name, string label, string? helpText = null) => new()
    {
        Name = name,
        Label = label,
        Kind = ParamKind.Price,
        IsRequired = true,
        Min = 0.01,
        Step = 0.01,
        HelpText = helpText
    };

    private static ScriptParam EnumParam(string name, string label, string enumType,
        List<string> options, string? defaultVal = null) => new()
    {
        Name = name,
        Label = label,
        Kind = ParamKind.Enum,
        IsRequired = true,
        EnumType = enumType,
        Options = options,
        DefaultValue = defaultVal
    };
}

/// <summary>
/// A category in the toolbox sidebar.
/// </summary>
public sealed record CatalogCategory(string Name, string Color, string[] SegmentKeys);
