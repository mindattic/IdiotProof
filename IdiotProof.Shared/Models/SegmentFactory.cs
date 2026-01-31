// ============================================================================
// SegmentFactory - Creates segment templates with proper parameters
// ============================================================================

using IdiotProof.Shared.Enums;

namespace IdiotProof.Shared.Models
{
    /// <summary>
    /// Factory for creating strategy segment templates with proper parameters.
    /// </summary>
    public static class SegmentFactory
    {
        /// <summary>
        /// Gets all available segment templates organized by category.
        /// </summary>
        public static Dictionary<SegmentCategory, List<StrategySegment>> GetAllTemplates()
        {
            return new Dictionary<SegmentCategory, List<StrategySegment>>
            {
                [SegmentCategory.Start] = [CreateTicker()],
                [SegmentCategory.Session] = [CreateSessionDuration()],
                [SegmentCategory.PriceCondition] =
                [
                    CreateBreakout(),
                    CreatePullback(),
                    CreatePriceAbove(),
                    CreatePriceBelow()
                ],
                [SegmentCategory.VwapCondition] =
                [
                    CreateAboveVwap(),
                    CreateBelowVwap()
                ],
                [SegmentCategory.IndicatorCondition] =
                [
                    CreateIsRsi(),
                    CreateIsMacd(),
                    CreateIsAdx(),
                    CreateIsDI()
                ],
                [SegmentCategory.Order] =
                [
                    CreateBuy(),
                    CreateSell(),
                    CreateClose()
                ],
                [SegmentCategory.RiskManagement] =
                [
                    CreateTakeProfit(),
                    CreateTakeProfitRange(),
                    CreateStopLoss(),
                    CreateTrailingStopLoss()
                ],
                [SegmentCategory.PositionManagement] =
                [
                    CreateClosePosition()
                ],
                [SegmentCategory.OrderConfig] =
                [
                    CreateTimeInForce(),
                    CreateOutsideRTH(),
                    CreateAllOrNone()
                ]
            };
        }

        /// <summary>
        /// Gets the friendly name for a segment category.
        /// </summary>
        public static string GetCategoryDisplayName(SegmentCategory category) => category switch
        {
            SegmentCategory.Start => "📍 Start",
            SegmentCategory.Session => "⏰ Session",
            SegmentCategory.PriceCondition => "💰 Price Conditions",
            SegmentCategory.VwapCondition => "📊 VWAP Conditions",
            SegmentCategory.IndicatorCondition => "📈 Indicators",
            SegmentCategory.Order => "🛒 Orders",
            SegmentCategory.RiskManagement => "🛡️ Risk Management",
            SegmentCategory.PositionManagement => "📤 Position Management",
            SegmentCategory.OrderConfig => "⚙️ Order Config",
            _ => category.ToString()
        };

        /// <summary>
        /// Gets the color for a segment category.
        /// </summary>
        public static string GetCategoryColor(SegmentCategory category) => category switch
        {
            SegmentCategory.Start => "#4CAF50",           // Green
            SegmentCategory.Session => "#9C27B0",          // Purple
            SegmentCategory.PriceCondition => "#2196F3",   // Blue
            SegmentCategory.VwapCondition => "#00BCD4",    // Cyan
            SegmentCategory.IndicatorCondition => "#FF9800", // Orange
            SegmentCategory.Order => "#F44336",            // Red
            SegmentCategory.RiskManagement => "#FFC107",   // Amber
            SegmentCategory.PositionManagement => "#795548", // Brown
            SegmentCategory.OrderConfig => "#607D8B",      // Blue Grey
            _ => "#9E9E9E"                                 // Grey
        };

        // ====================================================================
        // START SEGMENTS
        // ====================================================================

        public static StrategySegment CreateTicker() => new()
        {
            Type = SegmentType.Ticker,
            Category = SegmentCategory.Start,
            DisplayName = "Ticker",
            Description = "Stock symbol to trade",
            Icon = "attach_money",
            Color = GetCategoryColor(SegmentCategory.Start),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "symbol",
                    Label = "Symbol",
                    Type = ParameterType.String,
                    IsRequired = true,
                    Placeholder = "AAPL",
                    HelpText = "Enter the stock ticker symbol (e.g., AAPL, TSLA)"
                }
            ]
        };

        // ====================================================================
        // SESSION SEGMENTS
        // ====================================================================

        public static StrategySegment CreateSessionDuration() => new()
        {
            Type = SegmentType.SessionDuration,
            Category = SegmentCategory.Session,
            DisplayName = "Session Duration",
            Description = "Set trading session time window",
            Icon = "schedule",
            Color = GetCategoryColor(SegmentCategory.Session),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "session",
                    Label = "Trading Session",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "TradingSession",
                    Options = Enum.GetNames<TradingSession>().ToList(),
                    DefaultValue = TradingSession.PreMarket,
                    HelpText = "Select a predefined trading session"
                }
            ]
        };

        // ====================================================================
        // PRICE CONDITION SEGMENTS
        // ====================================================================

        public static StrategySegment CreateBreakout() => new()
        {
            Type = SegmentType.Breakout,
            Category = SegmentCategory.PriceCondition,
            DisplayName = "Breakout",
            Description = "Price >= level",
            Icon = "trending_up",
            Color = GetCategoryColor(SegmentCategory.PriceCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "level",
                    Label = "Breakout Level",
                    Type = ParameterType.Price,
                    IsRequired = true,
                    MinValue = 0.01,
                    Step = 0.01,
                    HelpText = "Price must reach or exceed this level"
                }
            ]
        };

        public static StrategySegment CreatePullback() => new()
        {
            Type = SegmentType.Pullback,
            Category = SegmentCategory.PriceCondition,
            DisplayName = "Pullback",
            Description = "Price <= level",
            Icon = "trending_down",
            Color = GetCategoryColor(SegmentCategory.PriceCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "level",
                    Label = "Pullback Level",
                    Type = ParameterType.Price,
                    IsRequired = true,
                    MinValue = 0.01,
                    Step = 0.01,
                    HelpText = "Price must drop to or below this level"
                }
            ]
        };

        public static StrategySegment CreatePriceAbove() => new()
        {
            Type = SegmentType.PriceAbove,
            Category = SegmentCategory.PriceCondition,
            DisplayName = "Price Above",
            Description = "Price >= level",
            Icon = "arrow_upward",
            Color = GetCategoryColor(SegmentCategory.PriceCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "level",
                    Label = "Price Level",
                    Type = ParameterType.Price,
                    IsRequired = true,
                    MinValue = 0.01,
                    Step = 0.01,
                    HelpText = "Price must be at or above this level"
                }
            ]
        };

        public static StrategySegment CreatePriceBelow() => new()
        {
            Type = SegmentType.PriceBelow,
            Category = SegmentCategory.PriceCondition,
            DisplayName = "Price Below",
            Description = "Price < level",
            Icon = "arrow_downward",
            Color = GetCategoryColor(SegmentCategory.PriceCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "level",
                    Label = "Price Level",
                    Type = ParameterType.Price,
                    IsRequired = true,
                    MinValue = 0.01,
                    Step = 0.01,
                    HelpText = "Price must be below this level"
                }
            ]
        };

        // ====================================================================
        // VWAP CONDITION SEGMENTS
        // ====================================================================

        public static StrategySegment CreateAboveVwap() => new()
        {
            Type = SegmentType.AboveVwap,
            Category = SegmentCategory.VwapCondition,
            DisplayName = "Above VWAP",
            Description = "Price >= VWAP + buffer",
            Icon = "show_chart",
            Color = GetCategoryColor(SegmentCategory.VwapCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "buffer",
                    Label = "Buffer",
                    Type = ParameterType.Price,
                    IsRequired = false,
                    DefaultValue = 0.0,
                    MinValue = 0,
                    Step = 0.01,
                    HelpText = "Optional buffer above VWAP (0 = exactly at VWAP)"
                }
            ]
        };

        public static StrategySegment CreateBelowVwap() => new()
        {
            Type = SegmentType.BelowVwap,
            Category = SegmentCategory.VwapCondition,
            DisplayName = "Below VWAP",
            Description = "Price <= VWAP - buffer",
            Icon = "show_chart",
            Color = GetCategoryColor(SegmentCategory.VwapCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "buffer",
                    Label = "Buffer",
                    Type = ParameterType.Price,
                    IsRequired = false,
                    DefaultValue = 0.0,
                    MinValue = 0,
                    Step = 0.01,
                    HelpText = "Optional buffer below VWAP (0 = exactly at VWAP)"
                }
            ]
        };

        // ====================================================================
        // INDICATOR CONDITION SEGMENTS
        // ====================================================================

        public static StrategySegment CreateIsRsi() => new()
        {
            Type = SegmentType.IsRsi,
            Category = SegmentCategory.IndicatorCondition,
            DisplayName = "RSI Condition",
            Description = "Check RSI state (Overbought/Oversold)",
            Icon = "speed",
            Color = GetCategoryColor(SegmentCategory.IndicatorCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "state",
                    Label = "RSI State",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "RsiState",
                    Options = Enum.GetNames<RsiState>().ToList(),
                    HelpText = "Overbought (RSI >= 70) or Oversold (RSI <= 30)"
                },
                new SegmentParameter
                {
                    Name = "threshold",
                    Label = "Custom Threshold",
                    Type = ParameterType.Double,
                    IsRequired = false,
                    MinValue = 0,
                    MaxValue = 100,
                    Step = 1,
                    HelpText = "Optional custom RSI threshold (default: 70 for overbought, 30 for oversold)"
                }
            ]
        };

        public static StrategySegment CreateIsMacd() => new()
        {
            Type = SegmentType.IsMacd,
            Category = SegmentCategory.IndicatorCondition,
            DisplayName = "MACD Condition",
            Description = "Check MACD state",
            Icon = "insights",
            Color = GetCategoryColor(SegmentCategory.IndicatorCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "state",
                    Label = "MACD State",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "MacdState",
                    Options = Enum.GetNames<MacdState>().ToList(),
                    HelpText = "Bullish/Bearish crossover, Above/Below zero, Histogram direction"
                }
            ]
        };

        public static StrategySegment CreateIsAdx() => new()
        {
            Type = SegmentType.IsAdx,
            Category = SegmentCategory.IndicatorCondition,
            DisplayName = "ADX Condition",
            Description = "Check ADX trend strength",
            Icon = "trending_flat",
            Color = GetCategoryColor(SegmentCategory.IndicatorCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "comparison",
                    Label = "Comparison",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "Comparison",
                    Options = Enum.GetNames<Comparison>().ToList(),
                    DefaultValue = Comparison.Gte,
                    HelpText = "How to compare ADX value"
                },
                new SegmentParameter
                {
                    Name = "threshold",
                    Label = "Threshold",
                    Type = ParameterType.Double,
                    IsRequired = true,
                    MinValue = 0,
                    MaxValue = 100,
                    Step = 1,
                    DefaultValue = 25.0,
                    HelpText = "ADX threshold (25+ = strong trend)"
                }
            ]
        };

        public static StrategySegment CreateIsDI() => new()
        {
            Type = SegmentType.IsDI,
            Category = SegmentCategory.IndicatorCondition,
            DisplayName = "DI Condition",
            Description = "Check directional indicator",
            Icon = "swap_vert",
            Color = GetCategoryColor(SegmentCategory.IndicatorCondition),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "direction",
                    Label = "Direction",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "DiDirection",
                    Options = Enum.GetNames<DiDirection>().ToList(),
                    HelpText = "Positive (+DI > -DI) or Negative (-DI > +DI)"
                },
                new SegmentParameter
                {
                    Name = "minDifference",
                    Label = "Min Difference",
                    Type = ParameterType.Double,
                    IsRequired = false,
                    DefaultValue = 0.0,
                    MinValue = 0,
                    Step = 0.5,
                    HelpText = "Minimum difference between +DI and -DI"
                }
            ]
        };

        // ====================================================================
        // ORDER SEGMENTS
        // ====================================================================

        public static StrategySegment CreateBuy() => new()
        {
            Type = SegmentType.Buy,
            Category = SegmentCategory.Order,
            DisplayName = "Buy",
            Description = "Place a buy order",
            Icon = "shopping_cart",
            Color = GetCategoryColor(SegmentCategory.Order),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "quantity",
                    Label = "Quantity",
                    Type = ParameterType.Integer,
                    IsRequired = true,
                    MinValue = 1,
                    DefaultValue = 1,
                    HelpText = "Number of shares to buy"
                },
                new SegmentParameter
                {
                    Name = "priceType",
                    Label = "Price Type",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "Price",
                    Options = Enum.GetNames<Price>().ToList(),
                    DefaultValue = Price.Current,
                    HelpText = "How to determine the order price"
                },
                new SegmentParameter
                {
                    Name = "orderType",
                    Label = "Order Type",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "OrderType",
                    Options = Enum.GetNames<OrderType>().ToList(),
                    DefaultValue = OrderType.Limit,
                    HelpText = "Market or Limit order"
                }
            ]
        };

        public static StrategySegment CreateSell() => new()
        {
            Type = SegmentType.Sell,
            Category = SegmentCategory.Order,
            DisplayName = "Sell",
            Description = "Place a sell order",
            Icon = "sell",
            Color = GetCategoryColor(SegmentCategory.Order),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "quantity",
                    Label = "Quantity",
                    Type = ParameterType.Integer,
                    IsRequired = true,
                    MinValue = 1,
                    DefaultValue = 1,
                    HelpText = "Number of shares to sell"
                },
                new SegmentParameter
                {
                    Name = "priceType",
                    Label = "Price Type",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "Price",
                    Options = Enum.GetNames<Price>().ToList(),
                    DefaultValue = Price.Current,
                    HelpText = "How to determine the order price"
                },
                new SegmentParameter
                {
                    Name = "orderType",
                    Label = "Order Type",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "OrderType",
                    Options = Enum.GetNames<OrderType>().ToList(),
                    DefaultValue = OrderType.Limit,
                    HelpText = "Market or Limit order"
                }
            ]
        };

        public static StrategySegment CreateClose() => new()
        {
            Type = SegmentType.Close,
            Category = SegmentCategory.Order,
            DisplayName = "Close Position",
            Description = "Close an existing position",
            Icon = "exit_to_app",
            Color = GetCategoryColor(SegmentCategory.Order),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "quantity",
                    Label = "Quantity",
                    Type = ParameterType.Integer,
                    IsRequired = true,
                    MinValue = 1,
                    DefaultValue = 1,
                    HelpText = "Number of shares to close"
                },
                new SegmentParameter
                {
                    Name = "positionSide",
                    Label = "Position Side",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "OrderSide",
                    Options = Enum.GetNames<OrderSide>().ToList(),
                    DefaultValue = OrderSide.Buy,
                    HelpText = "The side of your current position (Buy = long, Sell = short)"
                }
            ]
        };

        // ====================================================================
        // RISK MANAGEMENT SEGMENTS
        // ====================================================================

        public static StrategySegment CreateTakeProfit() => new()
        {
            Type = SegmentType.TakeProfit,
            Category = SegmentCategory.RiskManagement,
            DisplayName = "Take Profit",
            Description = "Set fixed take profit price",
            Icon = "paid",
            Color = GetCategoryColor(SegmentCategory.RiskManagement),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "price",
                    Label = "Take Profit Price",
                    Type = ParameterType.Price,
                    IsRequired = true,
                    MinValue = 0.01,
                    Step = 0.01,
                    HelpText = "Exit position when price reaches this level"
                }
            ]
        };

        public static StrategySegment CreateTakeProfitRange() => new()
        {
            Type = SegmentType.TakeProfitRange,
            Category = SegmentCategory.RiskManagement,
            DisplayName = "Take Profit (ADX Range)",
            Description = "ADX-based take profit with low/high targets",
            Icon = "price_change",
            Color = GetCategoryColor(SegmentCategory.RiskManagement),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "lowTarget",
                    Label = "Conservative Target",
                    Type = ParameterType.Price,
                    IsRequired = true,
                    MinValue = 0.01,
                    Step = 0.01,
                    HelpText = "Take profit when ADX is weak (< 15)"
                },
                new SegmentParameter
                {
                    Name = "highTarget",
                    Label = "Aggressive Target",
                    Type = ParameterType.Price,
                    IsRequired = true,
                    MinValue = 0.01,
                    Step = 0.01,
                    HelpText = "Take profit when ADX is strong (> 35)"
                }
            ]
        };

        public static StrategySegment CreateStopLoss() => new()
        {
            Type = SegmentType.StopLoss,
            Category = SegmentCategory.RiskManagement,
            DisplayName = "Stop Loss",
            Description = "Set fixed stop loss price",
            Icon = "warning",
            Color = GetCategoryColor(SegmentCategory.RiskManagement),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "price",
                    Label = "Stop Loss Price",
                    Type = ParameterType.Price,
                    IsRequired = true,
                    MinValue = 0.01,
                    Step = 0.01,
                    HelpText = "Exit position if price drops to this level"
                }
            ]
        };

        public static StrategySegment CreateTrailingStopLoss() => new()
        {
            Type = SegmentType.TrailingStopLoss,
            Category = SegmentCategory.RiskManagement,
            DisplayName = "Trailing Stop Loss",
            Description = "Set percentage trailing stop",
            Icon = "sync_alt",
            Color = GetCategoryColor(SegmentCategory.RiskManagement),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "percent",
                    Label = "Trailing Percent",
                    Type = ParameterType.Percentage,
                    IsRequired = true,
                    MinValue = 0.01,
                    MaxValue = 0.50,
                    Step = 0.01,
                    DefaultValue = 0.10,
                    HelpText = "Percentage below peak price (e.g., 0.10 = 10%)"
                }
            ]
        };

        // ====================================================================
        // POSITION MANAGEMENT SEGMENTS
        // ====================================================================

        public static StrategySegment CreateClosePosition() => new()
        {
            Type = SegmentType.ClosePosition,
            Category = SegmentCategory.PositionManagement,
            DisplayName = "Close at Time",
            Description = "Close position at specified time",
            Icon = "timer",
            Color = GetCategoryColor(SegmentCategory.PositionManagement),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "time",
                    Label = "Close Time",
                    Type = ParameterType.Time,
                    IsRequired = true,
                    HelpText = "Time to close position (Eastern Time)"
                },
                new SegmentParameter
                {
                    Name = "onlyIfProfitable",
                    Label = "Only If Profitable",
                    Type = ParameterType.Boolean,
                    IsRequired = false,
                    DefaultValue = true,
                    HelpText = "Only close if position is profitable"
                }
            ]
        };

        // ====================================================================
        // ORDER CONFIG SEGMENTS
        // ====================================================================

        public static StrategySegment CreateTimeInForce() => new()
        {
            Type = SegmentType.TimeInForce,
            Category = SegmentCategory.OrderConfig,
            DisplayName = "Time In Force",
            Description = "Set order time in force",
            Icon = "hourglass_empty",
            Color = GetCategoryColor(SegmentCategory.OrderConfig),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "tif",
                    Label = "Time In Force",
                    Type = ParameterType.Enum,
                    IsRequired = true,
                    EnumTypeName = "TimeInForce",
                    Options = Enum.GetNames<TimeInForce>().ToList(),
                    DefaultValue = TimeInForce.GoodTillCancel,
                    HelpText = "How long the order remains active"
                }
            ]
        };

        public static StrategySegment CreateOutsideRTH() => new()
        {
            Type = SegmentType.OutsideRTH,
            Category = SegmentCategory.OrderConfig,
            DisplayName = "Outside RTH",
            Description = "Allow trading outside regular hours",
            Icon = "nightlight",
            Color = GetCategoryColor(SegmentCategory.OrderConfig),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "outsideRth",
                    Label = "Trade Outside RTH",
                    Type = ParameterType.Boolean,
                    IsRequired = false,
                    DefaultValue = true,
                    HelpText = "Allow order to fill during pre-market/after-hours"
                },
                new SegmentParameter
                {
                    Name = "takeProfit",
                    Label = "Take Profit Outside RTH",
                    Type = ParameterType.Boolean,
                    IsRequired = false,
                    DefaultValue = true,
                    HelpText = "Allow take profit to trigger outside regular hours"
                }
            ]
        };

        public static StrategySegment CreateAllOrNone() => new()
        {
            Type = SegmentType.AllOrNone,
            Category = SegmentCategory.OrderConfig,
            DisplayName = "All Or None",
            Description = "Require full order fill",
            Icon = "all_inclusive",
            Color = GetCategoryColor(SegmentCategory.OrderConfig),
            Parameters =
            [
                new SegmentParameter
                {
                    Name = "allOrNone",
                    Label = "All Or None",
                    Type = ParameterType.Boolean,
                    IsRequired = false,
                    DefaultValue = true,
                    HelpText = "Order must fill completely or not at all"
                }
            ]
        };
    }
}
