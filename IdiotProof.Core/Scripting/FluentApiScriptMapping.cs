// ============================================================================
// FluentApiScriptMapping - Maps fluent API methods to IdiotScript equivalents
// ============================================================================
//
// NOMENCLATURE:
// - Fluent API: The C# builder pattern API (Stock.Ticker().Breakout().Long())
// - IdiotScript: The text-based DSL (Ticker(AAPL).Breakout(150).Order())
// - Strategy: The intermediate StrategyDefinition object
// - Segment: A single strategy component (Breakout, AboveVwap, etc.)
//
// CONVERSION FLOW:
//   Fluent API → TradingStrategy → (Backend execution)
//   IdiotScript → StrategyDefinition → IdiotScript (round-trip)
//   TradingStrategy ↔ StrategyDefinition (via JSON serialization)
//
// SUPPORTED CATEGORIES:
// - Start: Symbol/ticker commands
// - Identity: Name, description, enabled
// - Session: Trading session and time configuration
// - Order: Long/Short/Close operations with quantity
// - PriceCondition: Price-based entry conditions
// - VwapCondition: VWAP-based conditions
// - IndicatorCondition: RSI, EMA, MACD, ADX, DI conditions
// - RiskManagement: Take profit, stop loss, trailing stop
// - PositionManagement: Auto-close positions
// - OrderConfig: Time-in-force, extended hours, order type
//
// ============================================================================

using IdiotProof.Core.Enums;

namespace IdiotProof.Core.Scripting;

/// <summary>
/// Maps fluent API methods to their IdiotScript equivalents.
/// Used for validation and documentation.
/// </summary>
public static class FluentApiScriptMapping
{
    /// <summary>
    /// Represents a mapping between a fluent API method and its IdiotScript equivalent.
    /// </summary>
    public sealed record MethodMapping(
        string FluentMethod,
        string[] IdiotScriptCommands,
        string[] Parameters,
        string Category,
        string Description,
        bool RequiresParameters = false);

    /// <summary>
    /// Complete list of all fluent API methods and their IdiotScript equivalents.
    /// </summary>
    public static readonly MethodMapping[] AllMappings =
    [
        // ====================================================================
        // SYMBOL/IDENTITY COMMANDS
        // ====================================================================
        new("Stock.Ticker(symbol)",
            ["TICKER", "SYM", "SYMBOL", "STOCK.TICKER", "STOCK.SYMBOL", "STOCK"],
            ["symbol: string (1-5 uppercase letters)"],
            "Start",
            "Sets the stock ticker symbol for the strategy",
            RequiresParameters: true),

        new("Strategy",
            ["STRATEGY"],
            [],
            "Start",
            "Strategy prefix marker (optional)"),

        new("Stock.Ticker(symbol, notes)",
            ["NAME"],
            ["strategyName: string (quoted)"],
            "Identity",
            "Sets the strategy name"),

        new(".WithNotes(notes)",
            ["DESC", "DESCRIPTION"],
            ["description: string (quoted)"],
            "Identity",
            "Sets the strategy description"),

        new(".Enabled(enabled)",
            ["ENABLED", "ISENABLED"],
            ["enabled: boolean (Y, YES, TRUE, N, NO, FALSE, IS.TRUE, IS.FALSE)"],
            "Identity",
            "Enables or disables the strategy"),

        // ====================================================================
        // SESSION/TIME COMMANDS
        // ====================================================================
        new(".TimeFrame(session)",
            ["SESSION", "TIMEFRAME"],
            ["session: IS.PREMARKET, IS.RTH, IS.AFTERHOURS, IS.EXTENDED, IS.ACTIVE"],
            "Session",
            "Sets the trading session time window"),

        new(".TimeFrame(startTime, endTime)",
            ["START", "END"],
            ["startTime: TimeOnly (HH:mm)", "endTime: TimeOnly (HH:mm)"],
            "Session",
            "Sets explicit start and end times"),

        new(".Start(startTime)",
            ["START"],
            ["startTime: TimeOnly (HH:mm)"],
            "Session",
            "Sets the strategy start time"),

        // ====================================================================
        // QUANTITY/ENTRY COMMANDS
        // ====================================================================
        new(".Order(direction, quantity, priceType)",
            ["ORDER"],
            ["direction: IS.LONG, IS.SHORT (default: LONG)", "quantity: int (positive)", "priceType: Price enum (optional)"],
            "Order",
            "Creates an order with specified direction and quantity"),

        new(".Long(quantity, priceType)",
            ["ORDER", "LONG", "QTY", "QUANTITY"],
            ["quantity: int (positive)", "priceType: Price enum (optional)"],
            "Order",
            "Creates a long (buy) order with specified quantity"),

        new(".Short(quantity, priceType)",
            ["ORDER", "SHORT", "QTY"],
            ["quantity: int (positive)", "priceType: Price enum (optional)"],
            "Order",
            "Creates a short (sell) order with specified quantity"),

        new(".Close(quantity)",
            ["CLOSE"],
            ["quantity: int (position size)"],
            "Order",
            "Creates an order to close existing position"),

        new(".CloseLong(quantity)",
            ["CLOSELONG"],
            ["quantity: int (position size)"],
            "Order",
            "Sells to close a long position"),

        new(".CloseShort(quantity)",
            ["CLOSESHORT"],
            ["quantity: int (position size)"],
            "Order",
            "Buys to cover a short position"),

        // ====================================================================
        // PRICE CONDITIONS
        // ====================================================================
        new(".Breakout(level)",
            ["BREAKOUT"],
            ["level: double (price, optional)"],
            "PriceCondition",
            "Price breakout condition (price >= level)"),

        new(".Pullback(level)",
            ["PULLBACK"],
            ["level: double (price, optional)"],
            "PriceCondition",
            "Price pullback condition (price <= level)"),

        new(".IsPriceAbove(level)",
            ["ENTRY", "PRICE", "ISPRICEABOVE"],
            ["level: double (price)"],
            "PriceCondition",
            "Price at or above level condition"),

        new(".IsPriceBelow(level)",
            ["ISPRICEBELOW", "PRICEBELOW"],
            ["level: double (price)"],
            "PriceCondition",
            "Price below level condition"),

        new(".GapUp(percentage)",
            ["GAPUP", "ISGAPUP"],
            ["percentage: double (gap % from previous close)"],
            "PriceCondition",
            "Price gapped up by percentage from previous close"),

        new(".GapDown(percentage)",
            ["GAPDOWN", "ISGAPDOWN"],
            ["percentage: double (gap % from previous close)"],
            "PriceCondition",
            "Price gapped down by percentage from previous close"),

        // ====================================================================
        // VWAP CONDITIONS
        // ====================================================================
        new(".IsAboveVwap(buffer)",
            ["ABOVEVWAP", "ISABOVEVWAP", "VWAP"],
            ["buffer: double (optional, default 0)"],
            "VwapCondition",
            "Price above VWAP + buffer condition"),

        new(".IsBelowVwap(buffer)",
            ["BELOWVWAP", "ISBELOWVWAP"],
            ["buffer: double (optional, default 0)"],
            "VwapCondition",
            "Price below VWAP - buffer condition"),

        // ====================================================================
        // EMA CONDITIONS
        // ====================================================================
        new("EMA conditions",
            ["EMAABOVE", "ISEMAABOVE"],
            ["period: int (EMA period)"],
            "IndicatorCondition",
            "Price above EMA condition"),

        new("EMA conditions",
            ["EMABELOW", "ISEMABELOW"],
            ["period: int (EMA period)"],
            "IndicatorCondition",
            "Price below EMA condition"),

        new("EMA conditions",
            ["EMABETWEEN", "ISEMABETWEEN"],
            ["lowerPeriod: int", "upperPeriod: int"],
            "IndicatorCondition",
            "Price between two EMAs condition"),

        // ====================================================================
        // RSI CONDITIONS
        // ====================================================================
        new(".IsRsi(state, threshold)",
            ["RSIOVERSOLD", "ISRSIOVERSOLD"],
            ["threshold: double (default 30)"],
            "IndicatorCondition",
            "RSI oversold condition (RSI <= threshold)"),

        new(".IsRsi(state, threshold)",
            ["RSIOVERBOUGHT", "ISRSIOVERBOUGHT"],
            ["threshold: double (default 70)"],
            "IndicatorCondition",
            "RSI overbought condition (RSI >= threshold)"),

        // ====================================================================
        // ADX CONDITIONS
        // ====================================================================
        new(".IsAdx(comparison, threshold)",
            ["ADXABOVE", "ISADXABOVE"],
            ["threshold: double (default 25)"],
            "IndicatorCondition",
            "ADX above threshold condition"),

        // ====================================================================
        // RISK MANAGEMENT
        // ====================================================================
        new(".TakeProfit(price)",
            ["TP", "TAKEPROFIT"],
            ["price: double ($prefix optional)"],
            "RiskManagement",
            "Sets take profit price"),

        new(".TakeProfit(lowPrice, highPrice)",
            ["TP", "TAKEPROFIT"],
            ["lowPrice: double", "highPrice: double"],
            "RiskManagement",
            "Sets take profit price range"),

        new(".StopLoss(price)",
            ["SL", "STOPLOSS"],
            ["price: double ($prefix optional)"],
            "RiskManagement",
            "Sets stop loss price"),

        new(".TrailingStopLoss(percent)",
            ["TSL", "TRAILINGSTOPLOSS"],
            ["percent: double or IS.TIGHT, IS.MODERATE, IS.STANDARD, IS.LOOSE, IS.WIDE"],
            "RiskManagement",
            "Sets trailing stop loss percentage"),

        // ====================================================================
        // POSITION MANAGEMENT
        // ====================================================================
        new(".ClosePosition(time)",
            ["CLOSEPOSITION"],
            ["time: TimeOnly or IS.BELL, IS.OPEN, IS.CLOSE, IS.EOD", "onlyIfProfitable: boolean (optional)"],
            "PositionManagement",
            "Auto-close position at specified time"),

        new(".ExitStrategy(time)",
            ["EXITSTRATEGY"],
            ["time: TimeOnly or IS.BELL"],
            "PositionManagement",
            "Sets exit strategy timing"),

        new(".IsProfitable()",
            ["PROFITABLE", "ISPROFITABLE"],
            [],
            "PositionManagement",
            "Only exit if position is profitable"),

        new(".Repeat(enabled)",
            ["REPEAT", "ISREPEAT"],
            ["enabled: boolean (default true)"],
            "ExecutionBehavior",
            "Allow strategy to repeat after exit"),

        // ====================================================================
        // MACD CONDITIONS
        // ====================================================================
        new(".IsMacdBullish(fastPeriod, slowPeriod, signalPeriod)",
            ["MACDBULLISH", "ISMACDBULLISH"],
            ["fastPeriod: int (default 12)", "slowPeriod: int (default 26)", "signalPeriod: int (default 9)"],
            "IndicatorCondition",
            "MACD bullish crossover condition"),

        new(".IsMacdBearish(fastPeriod, slowPeriod, signalPeriod)",
            ["MACDBEARISH", "ISMACDBEARISH"],
            ["fastPeriod: int (default 12)", "slowPeriod: int (default 26)", "signalPeriod: int (default 9)"],
            "IndicatorCondition",
            "MACD bearish crossover condition"),

        // ====================================================================
        // DI CONDITIONS (Directional Index)
        // ====================================================================
        new(".IsDiPositive(threshold)",
            ["DIPOSITIVE", "ISDIPOSITIVE"],
            ["threshold: double (default 25)"],
            "IndicatorCondition",
            "+DI above threshold condition"),

        new(".IsDiNegative(threshold)",
            ["DINEGATIVE", "ISDINEGATIVE"],
            ["threshold: double (default 25)"],
            "IndicatorCondition",
            "-DI above threshold condition"),

        // ====================================================================
        // MOMENTUM CONDITIONS
        // ====================================================================
        new(".IsMomentumAbove(threshold)",
            ["MOMENTUMABOVE", "ISMOMENTUMABOVE"],
            ["threshold: double (momentum value)"],
            "IndicatorCondition",
            "Momentum indicator above threshold (upward momentum)"),

        new(".IsMomentumBelow(threshold)",
            ["MOMENTUMBELOW", "ISMOMENTUMBELOW"],
            ["threshold: double (momentum value)"],
            "IndicatorCondition",
            "Momentum indicator below threshold (downward momentum)"),

        new(".IsRocAbove(threshold)",
            ["ROCABOVE", "ISROCABOVE"],
            ["threshold: double (rate of change %)"],
            "IndicatorCondition",
            "Rate of Change above threshold (positive momentum)"),

        new(".IsRocBelow(threshold)",
            ["ROCBELOW", "ISROCBELOW"],
            ["threshold: double (rate of change %)"],
            "IndicatorCondition",
            "Rate of Change below threshold (negative momentum)"),

        // ====================================================================
        // CONTINUATION/PATTERN CONDITIONS
        // ====================================================================
        new(".IsHigherLows()",
            ["HIGHERLOWS", "ISHIGHERLOWS"],
            [],
            "IndicatorCondition",
            "Detects higher lows forming (ascending support pattern)"),

        new(".IsEmaTurningUp(period)",
            ["EMATURNINGUP", "ISEMATURNINGUP"],
            ["period: int (EMA period, e.g., 9)"],
            "IndicatorCondition",
            "EMA slope turning positive or flattening"),

        new(".IsVolumeAbove(multiplier)",
            ["VOLUMEABOVE", "ISVOLUMEABOVE"],
            ["multiplier: double (e.g., 1.5 = 150% of average)"],
            "IndicatorCondition",
            "Volume above N× average (volume spike confirmation)"),

        new(".IsCloseAboveVwap()",
            ["CLOSEABOVEVWAP", "ISCLOSEABOVEVWAP"],
            [],
            "VwapCondition",
            "Candle closed above VWAP (stronger than just price above)"),

        new(".IsVwapRejection()",
            ["VWAPREJECTION", "ISVWAPREJECTION", "VWAPREJECTED", "ISVWAPREJECTED"],
            [],
            "VwapCondition",
            "Failed VWAP reclaim (wick above, close below) - bearish signal"),

        // ====================================================================
        // ORDER CONFIGURATION
        // ====================================================================
        new(".TimeInForce(tif)",
            ["TIMEINFORCE", "TIF"],
            ["tif: DAY, GTC, IOC, FOK"],
            "OrderConfig",
            "Sets order time-in-force"),

        new(".OutsideRTH(enabled)",
            ["OUTSIDERTH", "EXTENDEDHOURS"],
            ["enabled: boolean (default false)"],
            "OrderConfig",
            "Allows order execution outside regular trading hours"),

        new(".AllOrNone(enabled)",
            ["ALLORNONE", "AON"],
            ["enabled: boolean (default false)"],
            "OrderConfig",
            "Requires full quantity fill or cancel"),

        new(".OrderType(type)",
            ["ORDERTYPE"],
            ["type: MARKET, LIMIT, STOP, STOP_LIMIT"],
            "OrderConfig",
            "Sets the order type")
    ];

    /// <summary>
    /// Gets all IdiotScript commands that have fluent API equivalents.
    /// </summary>
    public static IEnumerable<string> GetAllIdiotScriptCommands()
    {
        return AllMappings.SelectMany(m => m.IdiotScriptCommands).Distinct();
    }

    /// <summary>
    /// Gets all fluent API methods that have IdiotScript equivalents.
    /// </summary>
    public static IEnumerable<string> GetAllFluentMethods()
    {
        return AllMappings.Select(m => m.FluentMethod).Distinct();
    }

    /// <summary>
    /// Finds the IdiotScript equivalent for a fluent API method.
    /// </summary>
    public static MethodMapping? FindByFluentMethod(string fluentMethod)
    {
        return AllMappings.FirstOrDefault(m =>
            m.FluentMethod.Contains(fluentMethod, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the fluent API equivalent for an IdiotScript command.
    /// </summary>
    public static MethodMapping? FindByIdiotScriptCommand(string command)
    {
        return AllMappings.FirstOrDefault(m =>
            m.IdiotScriptCommands.Contains(command, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets mappings by category.
    /// </summary>
    public static IEnumerable<MethodMapping> GetByCategory(string category)
    {
        return AllMappings.Where(m =>
            m.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Maps a SegmentType to its IdiotScript command.
    /// </summary>
    public static string GetIdiotScriptCommand(SegmentType segmentType)
    {
        return segmentType switch
        {
            SegmentType.Ticker => "Ticker",
            SegmentType.SessionDuration => "Session",
            SegmentType.Start => "Start",
            SegmentType.End => "End",
            SegmentType.Breakout => "Breakout",
            SegmentType.Pullback => "Pullback",
            SegmentType.IsPriceAbove => "Entry",
            SegmentType.IsPriceBelow => "PriceBelow",
            SegmentType.IsAboveVwap => "AboveVwap",
            SegmentType.IsBelowVwap => "BelowVwap",
            SegmentType.IsEmaAbove => "EmaAbove",
            SegmentType.IsEmaBelow => "EmaBelow",
            SegmentType.IsEmaBetween => "EmaBetween",
            SegmentType.IsRsi => "Rsi",
            SegmentType.IsMacd => "Macd",
            SegmentType.IsAdx => "AdxAbove",
            SegmentType.IsDI => "Di",
            SegmentType.Order => "Order",
            SegmentType.Long => "Long",
            SegmentType.Short => "Short",
            SegmentType.Close => "Close",
            SegmentType.CloseLong => "CloseLong",
            SegmentType.CloseShort => "CloseShort",
            SegmentType.TakeProfit => "TakeProfit",
            SegmentType.TakeProfitRange => "TakeProfit",
            SegmentType.StopLoss => "StopLoss",
            SegmentType.TrailingStopLoss => "TrailingStopLoss",
            SegmentType.TrailingStopLossAtr => "TrailingStopLossAtr",
            SegmentType.ExitStrategy => "ExitStrategy",
            SegmentType.IsProfitable => "IsProfitable",
            SegmentType.TimeInForce => "TimeInForce",
            SegmentType.OutsideRTH => "OutsideRTH",
            SegmentType.AllOrNone => "AllOrNone",
            SegmentType.OrderType => "OrderType",
            _ => segmentType.ToString()
        };
    }

    /// <summary>
    /// Maps an IdiotScript command to its SegmentType.
    /// </summary>
    public static SegmentType? GetSegmentType(string command)
    {
        var upper = command.ToUpperInvariant();
        return upper switch
        {
            "TICKER" or "SYM" or "SYMBOL" => SegmentType.Ticker,
            "SESSION" => SegmentType.SessionDuration,
            "START" => SegmentType.Start,
            "END" => SegmentType.End,
            "BREAKOUT" => SegmentType.Breakout,
            "PULLBACK" => SegmentType.Pullback,
            "ENTRY" or "PRICE" or "ISPRICEABOVE" => SegmentType.IsPriceAbove,
            "ISPRICEBELOW" or "PRICEBELOW" => SegmentType.IsPriceBelow,
            "ABOVEVWAP" or "ISABOVEVWAP" or "VWAP" => SegmentType.IsAboveVwap,
            "BELOWVWAP" or "ISBELOWVWAP" => SegmentType.IsBelowVwap,
            "EMAABOVE" or "ISEMAABOVE" => SegmentType.IsEmaAbove,
            "EMABELOW" or "ISEMABELOW" => SegmentType.IsEmaBelow,
            "EMABETWEEN" or "ISEMABETWEEN" => SegmentType.IsEmaBetween,
            "RSIOVERSOLD" or "ISRSIOVERSOLD" or "RSIOVERBOUGHT" or "ISRSIOVERBOUGHT" => SegmentType.IsRsi,
            "MACDBULLISH" or "ISMACDBULLISH" or "MACDBEARISH" or "ISMACDBEARISH" => SegmentType.IsMacd,
            "ADXABOVE" or "ISADXABOVE" => SegmentType.IsAdx,
            "DIPOSITIVE" or "ISDIPOSITIVE" or "DINEGATIVE" or "ISDINEGATIVE" => SegmentType.IsDI,
            "ORDER" or "LONG" => SegmentType.Order,
            "SHORT" => SegmentType.Order,
            "CLOSE" => SegmentType.Close,
            "CLOSELONG" => SegmentType.CloseLong,
            "CLOSESHORT" => SegmentType.CloseShort,
            "TP" or "TAKEPROFIT" => SegmentType.TakeProfit,
            "SL" or "STOPLOSS" => SegmentType.StopLoss,
            "TSL" or "TRAILINGSTOPLOSS" => SegmentType.TrailingStopLoss,
            "EXITSTRATEGY" or "CLOSEPOSITION" => SegmentType.ExitStrategy,
            "ISPROFITABLE" or "PROFITABLE" => SegmentType.IsProfitable,
            "TIMEINFORCE" or "TIF" => SegmentType.TimeInForce,
            "OUTSIDERTH" or "EXTENDEDHOURS" => SegmentType.OutsideRTH,
            "ALLORNONE" or "AON" => SegmentType.AllOrNone,
            "ORDERTYPE" => SegmentType.OrderType,
            _ => null
        };
    }
}


