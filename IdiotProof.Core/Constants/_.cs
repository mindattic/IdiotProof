// ============================================================================
// _ (Underscore) - Central source for string constants
// ============================================================================
//
// This static class provides a single source of truth for string constants
// used throughout the codebase. Using constants eliminates case sensitivity
// issues and makes refactoring easier.
//
// USAGE:
//   var param = segment.Parameters.FirstOrDefault(p => p.Name == _.Param.Level);
//   var segment = segments.First(s => s.Type.ToString() == _.Segment.Breakout);
//
// ============================================================================

namespace IdiotProof.Constants;

/// <summary>
/// Central source for string constants used throughout the codebase.
/// </summary>
public static class _
{
    /// <summary>
    /// Segment parameter name constants.
    /// </summary>
    public static class Param
    {
        // Start/Identity
        public const string Symbol = "symbol";
        public const string Name = "name";
        public const string Description = "description";
        public const string Enabled = "enabled";

        // Session
        public const string Session = "session";
        public const string StartTime = "startTime";
        public const string EndTime = "endTime";
        public const string Time = "time";

        // Price Conditions
        public const string Level = "level";
        public const string Percentage = "percentage";

        // VWAP Conditions
        public const string Buffer = "buffer";

        // Indicator Conditions
        public const string State = "state";
        public const string Threshold = "threshold";
        public const string Comparison = "comparison";
        public const string Direction = "direction";
        public const string MinDifference = "minDifference";
        public const string Period = "period";
        public const string LowerPeriod = "lowerPeriod";
        public const string UpperPeriod = "upperPeriod";
        public const string LookbackBars = "lookbackBars";
        public const string Multiplier = "multiplier";
        public const string Condition = "condition";

        // Order
        public const string Quantity = "quantity";
        public const string PriceType = "priceType";
        public const string OrderType = "orderType";
        public const string PositionSide = "positionSide";

        // Risk Management
        public const string Price = "price";
        public const string Percent = "percent";
        public const string LowTarget = "lowTarget";
        public const string HighTarget = "highTarget";
        public const string Mode = "mode";

        // Order Config
        public const string Tif = "tif";
        public const string OutsideRth = "outsideRth";
        public const string TakeProfit = "takeProfit";
        public const string AllOrNone = "allOrNone";
    }

    /// <summary>
    /// Segment type name constants.
    /// </summary>
    public static class Segment
    {
        // Start
        public const string Ticker = "Ticker";

        // Session
        public const string SessionDuration = "SessionDuration";
        public const string Start = "Start";
        public const string End = "End";

        // Price Conditions
        public const string Breakout = "Breakout";
        public const string Pullback = "Pullback";
        public const string IsPriceAbove = "IsPriceAbove";
        public const string IsPriceBelow = "IsPriceBelow";
        public const string GapUp = "GapUp";
        public const string GapDown = "GapDown";

        // VWAP Conditions
        public const string IsAboveVwap = "IsAboveVwap";
        public const string IsBelowVwap = "IsBelowVwap";
        public const string IsCloseAboveVwap = "IsCloseAboveVwap";
        public const string IsVwapRejection = "IsVwapRejection";

        // Indicator Conditions
        public const string IsRsi = "IsRsi";
        public const string IsMacd = "IsMacd";
        public const string IsAdx = "IsAdx";
        public const string IsDI = "IsDI";
        public const string IsEmaAbove = "IsEmaAbove";
        public const string IsEmaBelow = "IsEmaBelow";
        public const string IsEmaBetween = "IsEmaBetween";
        public const string IsEmaTurningUp = "IsEmaTurningUp";
        public const string IsMomentum = "IsMomentum";
        public const string IsRoc = "IsRoc";
        public const string IsHigherLows = "IsHigherLows";
        public const string IsVolumeAbove = "IsVolumeAbove";

        // Orders
        public const string Order = "Order";
        public const string Long = "Long";
        public const string Short = "Short";
        public const string Close = "Close";
        public const string CloseLong = "CloseLong";
        public const string CloseShort = "CloseShort";

        // Risk Management
        public const string TakeProfit = "TakeProfit";
        public const string TakeProfitRange = "TakeProfitRange";
        public const string StopLoss = "StopLoss";
        public const string TrailingStopLoss = "TrailingStopLoss";
        public const string TrailingStopLossAtr = "TrailingStopLossAtr";
        public const string AdaptiveOrder = "AdaptiveOrder";

        // Position Management
        public const string ExitStrategy = "ExitStrategy";
        public const string IsProfitable = "IsProfitable";

        // Order Config
        public const string TimeInForce = "TimeInForce";
        public const string OutsideRTH = "OutsideRTH";
        public const string AllOrNone = "AllOrNone";

        // Execution Behavior
        public const string Repeat = "Repeat";
    }

    /// <summary>
    /// Category name constants.
    /// </summary>
    public static class Category
    {
        public const string Start = "Start";
        public const string Identity = "Identity";
        public const string Session = "Session";
        public const string PriceCondition = "PriceCondition";
        public const string VwapCondition = "VwapCondition";
        public const string IndicatorCondition = "IndicatorCondition";
        public const string Order = "Order";
        public const string RiskManagement = "RiskManagement";
        public const string PositionManagement = "PositionManagement";
        public const string OrderConfig = "OrderConfig";
        public const string ExecutionBehavior = "ExecutionBehavior";
    }

    /// <summary>
    /// IdiotScript command constants.
    /// </summary>
    public static class Command
    {
        // Ticker/Symbol
        public const string Ticker = "TICKER";
        public const string Sym = "SYM";
        public const string Symbol = "SYMBOL";
        public const string Stock = "STOCK";

        // Session
        public const string Session = "SESSION";

        // Orders
        public const string Order = "ORDER";
        public const string Long = "LONG";
        public const string Short = "SHORT";
        public const string Qty = "QTY";
        public const string Quantity = "QUANTITY";
        public const string Close = "CLOSE";
        public const string CloseLong = "CLOSELONG";
        public const string CloseShort = "CLOSESHORT";

        // Risk Management
        public const string Tp = "TP";
        public const string TakeProfit = "TAKEPROFIT";
        public const string Sl = "SL";
        public const string StopLoss = "STOPLOSS";
        public const string Tsl = "TSL";
        public const string TrailingStopLoss = "TRAILINGSTOPLOSS";
        public const string AdaptiveOrder = "ADAPTIVEORDER";
        public const string IsAdaptiveOrder = "ISADAPTIVEORDER";
    }

    /// <summary>
    /// Direction/Side constants.
    /// </summary>
    public static class Direction
    {
        public const string Long = "Long";
        public const string Short = "Short";
        public const string Buy = "Buy";
        public const string Sell = "Sell";
    }

    /// <summary>
    /// Boolean string constants.
    /// </summary>
    public static class Bool
    {
        public const string True = "true";
        public const string False = "false";
        public const string Yes = "yes";
        public const string No = "no";
    }

    /// <summary>
    /// Folder name constants.
    /// </summary>
    public static class Folder
    {
        public const string Core = "IdiotProof.Core";
        public const string Scripts = "Scripts";
        public const string Strategies = "Strategies";
        public const string Profiles = "Profiles";
        public const string Data = "Data";
        public const string Logs = "Logs";
        public const string Settings = "Settings";
        public const string Backend = "Backend";
        public const string Console = "Console";
        public const string Frontend = "Frontend";
    }

    /// <summary>
    /// Project name constants.
    /// </summary>
    public static class Project
    {
        public const string Core = "IdiotProof.Core";
        public const string Backend = "IdiotProof.Backend";
        public const string Console = "IdiotProof.Console";
        public const string Frontend = "IdiotProof.Frontend";
        public const string Shared = "IdiotProof.Core";
    }

    /// <summary>
    /// File extension constants.
    /// </summary>
    public static class Extension
    {
        public const string Idiot = ".idiot";
        public const string Json = ".json";
        public const string Log = ".log";
        public const string Txt = ".txt";
    }
}
