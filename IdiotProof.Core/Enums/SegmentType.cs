// ============================================================================
// SegmentType - Types of strategy segments available for drag and drop
// ============================================================================

namespace IdiotProof.Enums {
    /// <summary>
    /// Categories of strategy segments for the WYSIWYG builder.
    /// </summary>
    public enum SegmentCategory
    {
        /// <summary>Starting segment (Ticker)</summary>
        Start,

        /// <summary>Session configuration (SessionDuration, Start, End)</summary>
        Session,

        /// <summary>Price conditions (Breakout, Pullback, PriceAbove, etc.)</summary>
        PriceCondition,

        /// <summary>VWAP conditions (AboveVwap, BelowVwap)</summary>
        VwapCondition,

        /// <summary>Indicator conditions (RSI, MACD, ADX, DI)</summary>
        IndicatorCondition,

        /// <summary>Order actions (Buy, Sell, Close)</summary>
        Order,

        /// <summary>Risk management (TakeProfit, StopLoss, TrailingStopLoss)</summary>
        RiskManagement,

        /// <summary>Position management (ClosePosition)</summary>
        PositionManagement,

        /// <summary>Order configuration (TimeInForce, OutsideRTH, AllOrNone)</summary>
        OrderConfig,

        /// <summary>Execution behavior (Repeat)</summary>
        Execution
    }

    /// <summary>
    /// Specific segment types that can be dragged onto the canvas.
    /// </summary>
    public enum SegmentType
    {
        // Start
        Ticker,

        // Session
        SessionDuration,
        Start,
        End,

        // Price Conditions
        Breakout,
        Pullback,
        IsPriceAbove,
        IsPriceBelow,
        GapUp,
        GapDown,

        // VWAP Conditions
        IsAboveVwap,
        IsBelowVwap,

        // Indicator Conditions
            IsRsi,
            IsMacd,
            IsAdx,
            IsDI,
            IsEmaAbove,
            IsEmaBelow,
            IsEmaBetween,
            IsEmaTurningUp,
            IsMomentum,
            IsRoc,
            IsHigherLows,
            IsLowerHighs,
            IsVolumeAbove,
            IsCloseAboveVwap,
            IsVwapRejection,

        // Orders
        Order,      // Unified order type with direction parameter (IS.LONG or IS.SHORT)
        Long,       // Alias for Order with IS.LONG direction
        Short,      // Alias for Order with IS.SHORT direction
        Close,
        CloseLong,
        CloseShort,

        // Risk Management
        TakeProfit,
        TakeProfitRange,
        StopLoss,
        TrailingStopLoss,
        TrailingStopLossAtr,
        AdaptiveOrder,

        // Position Management
        ExitStrategy,
        IsProfitable,

        // Order Config
        TimeInForce,
        OutsideRTH,
        AllOrNone,
        OrderType,

        // Execution Behavior
        Repeat,
        AutonomousTrading
    }
}


