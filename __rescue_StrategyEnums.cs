// ============================================================================
// StrategyEnums - Shared enums for strategy configuration
// ============================================================================

namespace IdiotProof.Shared.Enums
{
    /// <summary>
    /// Trading session time windows.
    /// </summary>
    public enum TradingSession
    {
        Active,
        PreMarket,
        RTH,
        AfterHours,
        Extended,
        PreMarketEndEarly,
        PreMarketStartLate,
        RTHEndEarly,
        RTHStartLate,
        AfterHoursEndEarly
    }

    /// <summary>
    /// Price type for order execution.
    /// </summary>
    public enum Price
    {
        Current,
        VWAP,
        Bid,
        Ask
    }

    /// <summary>
    /// Order type determining execution method.
    /// </summary>
    public enum OrderType
    {
        Market,
        Limit
    }

    /// <summary>
    /// Order side indicating whether to buy or sell.
    /// </summary>
    public enum OrderSide
    {
        Buy,
        Sell
    }

    /// <summary>
    /// Time in force for orders.
    /// </summary>
    public enum TimeInForce
    {
        Day,
        GoodTillCancel,
        ImmediateOrCancel,
        FillOrKill
    }

    /// <summary>
    /// RSI states for indicator conditions.
    /// </summary>
    public enum RsiState
    {
        Overbought,
        Oversold
    }

    /// <summary>
    /// MACD states for indicator conditions.
    /// </summary>
    public enum MacdState
    {
        Bullish,
        Bearish,
        AboveZero,
        BelowZero,
        HistogramRising,
        HistogramFalling
    }

    /// <summary>
    /// Comparison operators for indicator conditions.
    /// </summary>
    public enum Comparison
    {
        Gt,
        Gte,
        Lt,
        Lte,
        Eq
    }

    /// <summary>
    /// Directional indicator direction.
    /// </summary>
    public enum DiDirection
    {
        Positive,
        Negative
    }

    /// <summary>
    /// Contract exchange types.
    /// </summary>
    public enum ContractExchange
    {
        Smart,
        Pink
    }

    /// <summary>
    /// RSI condition types for fluent API.
    /// </summary>
    public enum RsiCondition
    {
        Above,
        Below,
        Overbought,
        Oversold
    }

    /// <summary>
    /// ADX condition types for fluent API.
    /// </summary>
    public enum AdxCondition
    {
        Above,
        Below,
        Strong,
        Weak
    }
}
