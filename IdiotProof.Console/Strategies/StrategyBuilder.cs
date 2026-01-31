// ============================================================================
// StrategyBuilder - Fluent API for building trading strategies
// ============================================================================

using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.Strategies;

/// <summary>
/// Entry point for the fluent strategy builder API.
/// </summary>
public static class Stock
{
    /// <summary>
    /// Creates a new strategy builder for the given ticker symbol.
    /// </summary>
    public static StrategyBuilder Ticker(string symbol)
    {
        return new StrategyBuilder(symbol);
    }
}

/// <summary>
/// Common percentage values for trailing stop losses.
/// </summary>
public static class Percent
{
    public const double Five = 0.05;
    public const double Ten = 0.10;
    public const double Fifteen = 0.15;
    public const double Twenty = 0.20;
    public const double TwentyFive = 0.25;
    public const double Thirty = 0.30;
}

/// <summary>
/// Price type for order execution.
/// </summary>
public enum PriceType
{
    Current,
    VWAP,
    Bid,
    Ask
}

/// <summary>
/// RSI condition types.
/// </summary>
public enum RsiCondition
{
    Above,
    Below,
    Overbought,
    Oversold
}

/// <summary>
/// ADX condition types.
/// </summary>
public enum AdxCondition
{
    Above,
    Below,
    Strong,
    Weak
}

/// <summary>
/// Time in force types.
/// </summary>
public enum TimeInForceType
{
    Day,
    GoodTillCancel,
    ImmediateOrCancel,
    FillOrKill
}

/// <summary>
/// Common market times for position management.
/// </summary>
public static class MarketTime
{
    public static class PreMarket
    {
        public static TimeOnly Start => new(4, 0);
        public static TimeOnly Ending => new(9, 15);
        public static TimeOnly End => new(9, 30);
    }

    public static class RTH
    {
        public static TimeOnly Start => new(9, 30);
        public static TimeOnly Ending => new(15, 45);
        public static TimeOnly End => new(16, 0);
    }

    public static class AfterHours
    {
        public static TimeOnly Start => new(16, 0);
        public static TimeOnly Ending => new(19, 45);
        public static TimeOnly End => new(20, 0);
    }
}

/// <summary>
/// Fluent builder for creating trading strategies.
/// </summary>
public class StrategyBuilder
{
    private readonly StrategyDefinition _strategy;
    private int _segmentOrder = 1;

    internal StrategyBuilder(string symbol)
    {
        _strategy = new StrategyDefinition
        {
            Name = $"{symbol} Strategy",
            Symbol = symbol,
            Enabled = true
        };

        // Add the ticker segment
        AddSegment(SegmentType.Ticker, SegmentCategory.Start, "Ticker",
            [new SegmentParameter
            {
                Name = "Symbol",
                Label = "Symbol",
                Type = ParameterType.String,
                Value = symbol,
                IsRequired = true
            }]);
    }

    /// <summary>
    /// Sets the strategy name.
    /// </summary>
    public StrategyBuilder Name(string name)
    {
        _strategy.Name = name;
        return this;
    }

    /// <summary>
    /// Sets the strategy description.
    /// </summary>
    public StrategyBuilder Description(string description)
    {
        _strategy.Description = description;
        return this;
    }

    /// <summary>
    /// Sets the strategy author.
    /// </summary>
    public StrategyBuilder Author(string author)
    {
        _strategy.Author = author;
        return this;
    }

    /// <summary>
    /// Enables or disables the strategy.
    /// </summary>
    public StrategyBuilder Enabled(bool enabled = true)
    {
        _strategy.Enabled = enabled;
        return this;
    }

    /// <summary>
    /// Sets the trading session duration.
    /// </summary>
    public StrategyBuilder SessionDuration(TradingSession session)
    {
        AddSegment(SegmentType.SessionDuration, SegmentCategory.Session, "Session Duration",
            [new SegmentParameter
            {
                Name = "Session",
                Label = "Session",
                Type = ParameterType.Enum,
                EnumTypeName = nameof(TradingSession),
                Value = session.ToString(),
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Sets a custom start time for the session.
    /// </summary>
    public StrategyBuilder Start(TimeOnly time)
    {
        AddSegment(SegmentType.Start, SegmentCategory.Session, "Start Time",
            [new SegmentParameter
            {
                Name = "Time",
                Label = "Time",
                Type = ParameterType.Time,
                Value = time,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Sets a custom end time for the session.
    /// </summary>
    public StrategyBuilder End(TimeOnly time)
    {
        AddSegment(SegmentType.End, SegmentCategory.Session, "End Time",
            [new SegmentParameter
            {
                Name = "Time",
                Label = "Time",
                Type = ParameterType.Time,
                Value = time,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Adds a breakout condition (price >= level).
    /// </summary>
    public StrategyBuilder Breakout(double level)
    {
        AddSegment(SegmentType.Breakout, SegmentCategory.PriceCondition, "Breakout",
            [new SegmentParameter
            {
                Name = "Level",
                Label = "Level",
                Type = ParameterType.Price,
                Value = level,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Adds a pullback condition (price <= level).
    /// </summary>
    public StrategyBuilder Pullback(double level)
    {
        AddSegment(SegmentType.Pullback, SegmentCategory.PriceCondition, "Pullback",
            [new SegmentParameter
            {
                Name = "Level",
                Label = "Level",
                Type = ParameterType.Price,
                Value = level,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Adds a price above condition (price >= level).
    /// </summary>
    public StrategyBuilder PriceAbove(double level)
    {
        AddSegment(SegmentType.PriceAbove, SegmentCategory.PriceCondition, "Price Above",
            [new SegmentParameter
            {
                Name = "Level",
                Label = "Level",
                Type = ParameterType.Price,
                Value = level,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Adds a price below condition (price <= level).
    /// </summary>
    public StrategyBuilder PriceBelow(double level)
    {
        AddSegment(SegmentType.PriceBelow, SegmentCategory.PriceCondition, "Price Below",
            [new SegmentParameter
            {
                Name = "Level",
                Label = "Level",
                Type = ParameterType.Price,
                Value = level,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Adds an above VWAP condition.
    /// </summary>
    public StrategyBuilder AboveVwap()
    {
        AddSegment(SegmentType.AboveVwap, SegmentCategory.VwapCondition, "Above VWAP", []);
        return this;
    }

    /// <summary>
    /// Adds a below VWAP condition.
    /// </summary>
    public StrategyBuilder BelowVwap()
    {
        AddSegment(SegmentType.BelowVwap, SegmentCategory.VwapCondition, "Below VWAP", []);
        return this;
    }

    /// <summary>
    /// Adds an RSI condition.
    /// </summary>
    public StrategyBuilder IsRsi(RsiCondition condition, double value, int period = 14)
    {
        AddSegment(SegmentType.IsRsi, SegmentCategory.IndicatorCondition, "RSI",
            [
                new SegmentParameter
                {
                    Name = "Condition",
                    Label = "Condition",
                    Type = ParameterType.Enum,
                    EnumTypeName = nameof(RsiCondition),
                    Value = condition.ToString(),
                    IsRequired = true
                },
                new SegmentParameter
                {
                    Name = "Value",
                    Label = "Value",
                    Type = ParameterType.Double,
                    Value = value,
                    IsRequired = true
                },
                new SegmentParameter
                {
                    Name = "Period",
                    Label = "Period",
                    Type = ParameterType.Integer,
                    Value = period,
                    IsRequired = false
                }
            ]);
        return this;
    }

    /// <summary>
    /// Adds an ADX condition.
    /// </summary>
    public StrategyBuilder IsAdx(AdxCondition condition, double value, int period = 14)
    {
        AddSegment(SegmentType.IsAdx, SegmentCategory.IndicatorCondition, "ADX",
            [
                new SegmentParameter
                {
                    Name = "Condition",
                    Label = "Condition",
                    Type = ParameterType.Enum,
                    EnumTypeName = nameof(AdxCondition),
                    Value = condition.ToString(),
                    IsRequired = true
                },
                new SegmentParameter
                {
                    Name = "Value",
                    Label = "Value",
                    Type = ParameterType.Double,
                    Value = value,
                    IsRequired = true
                },
                new SegmentParameter
                {
                    Name = "Period",
                    Label = "Period",
                    Type = ParameterType.Integer,
                    Value = period,
                    IsRequired = false
                }
            ]);
        return this;
    }

    /// <summary>
    /// Adds a buy order.
    /// </summary>
    public StrategyBuilder Buy(int quantity, PriceType priceType = PriceType.Current, double? limitPrice = null)
    {
        var parameters = new List<SegmentParameter>
        {
            new()
            {
                Name = "Quantity",
                Label = "Quantity",
                Type = ParameterType.Integer,
                Value = quantity,
                IsRequired = true
            },
            new()
            {
                Name = "PriceType",
                Label = "Price Type",
                Type = ParameterType.Enum,
                EnumTypeName = nameof(PriceType),
                Value = priceType.ToString(),
                IsRequired = true
            }
        };

        if (limitPrice.HasValue)
        {
            parameters.Add(new SegmentParameter
            {
                Name = "LimitPrice",
                Label = "Limit Price",
                Type = ParameterType.Price,
                Value = limitPrice.Value,
                IsRequired = false
            });
        }

        AddSegment(SegmentType.Buy, SegmentCategory.Order, "Buy", parameters);
        return this;
    }

    /// <summary>
    /// Adds a sell order.
    /// </summary>
    public StrategyBuilder Sell(int quantity, PriceType priceType = PriceType.Current, double? limitPrice = null)
    {
        var parameters = new List<SegmentParameter>
        {
            new()
            {
                Name = "Quantity",
                Label = "Quantity",
                Type = ParameterType.Integer,
                Value = quantity,
                IsRequired = true
            },
            new()
            {
                Name = "PriceType",
                Label = "Price Type",
                Type = ParameterType.Enum,
                EnumTypeName = nameof(PriceType),
                Value = priceType.ToString(),
                IsRequired = true
            }
        };

        if (limitPrice.HasValue)
        {
            parameters.Add(new SegmentParameter
            {
                Name = "LimitPrice",
                Label = "Limit Price",
                Type = ParameterType.Price,
                Value = limitPrice.Value,
                IsRequired = false
            });
        }

        AddSegment(SegmentType.Sell, SegmentCategory.Order, "Sell", parameters);
        return this;
    }

    /// <summary>
    /// Adds a take profit with a single price target.
    /// </summary>
    public StrategyBuilder TakeProfit(double price)
    {
        AddSegment(SegmentType.TakeProfit, SegmentCategory.RiskManagement, "Take Profit",
            [new SegmentParameter
            {
                Name = "Price",
                Label = "Price",
                Type = ParameterType.Price,
                Value = price,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Adds a take profit with a range (ADX-based).
    /// </summary>
    public StrategyBuilder TakeProfit(double lowPrice, double highPrice)
    {
        AddSegment(SegmentType.TakeProfitRange, SegmentCategory.RiskManagement, "Take Profit Range",
            [
                new SegmentParameter
                {
                    Name = "LowPrice",
                    Label = "Low Price",
                    Type = ParameterType.Price,
                    Value = lowPrice,
                    IsRequired = true
                },
                new SegmentParameter
                {
                    Name = "HighPrice",
                    Label = "High Price",
                    Type = ParameterType.Price,
                    Value = highPrice,
                    IsRequired = true
                }
            ]);
        return this;
    }

    /// <summary>
    /// Adds a stop loss.
    /// </summary>
    public StrategyBuilder StopLoss(double price)
    {
        AddSegment(SegmentType.StopLoss, SegmentCategory.RiskManagement, "Stop Loss",
            [new SegmentParameter
            {
                Name = "Price",
                Label = "Price",
                Type = ParameterType.Price,
                Value = price,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Adds a trailing stop loss as a percentage.
    /// </summary>
    public StrategyBuilder TrailingStopLoss(double percentage)
    {
        AddSegment(SegmentType.TrailingStopLoss, SegmentCategory.RiskManagement, "Trailing Stop Loss",
            [new SegmentParameter
            {
                Name = "Percentage",
                Label = "Percentage",
                Type = ParameterType.Percentage,
                Value = percentage,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Adds a trailing stop loss based on ATR.
    /// </summary>
    public StrategyBuilder TrailingStopLossAtr(double multiplier, int period = 14)
    {
        AddSegment(SegmentType.TrailingStopLossAtr, SegmentCategory.RiskManagement, "Trailing Stop Loss ATR",
            [
                new SegmentParameter
                {
                    Name = "Multiplier",
                    Label = "Multiplier",
                    Type = ParameterType.Double,
                    Value = multiplier,
                    IsRequired = true
                },
                new SegmentParameter
                {
                    Name = "Period",
                    Label = "Period",
                    Type = ParameterType.Integer,
                    Value = period,
                    IsRequired = false
                }
            ]);
        return this;
    }

    /// <summary>
    /// Adds a close position at a specific time.
    /// </summary>
    public StrategyBuilder ClosePosition(TimeOnly time, bool allowAfterHours = false)
    {
        AddSegment(SegmentType.ClosePosition, SegmentCategory.PositionManagement, "Close Position",
            [
                new SegmentParameter
                {
                    Name = "Time",
                    Label = "Time",
                    Type = ParameterType.Time,
                    Value = time,
                    IsRequired = true
                },
                new SegmentParameter
                {
                    Name = "AllowAfterHours",
                    Label = "Allow After Hours",
                    Type = ParameterType.Boolean,
                    Value = allowAfterHours,
                    IsRequired = false
                }
            ]);
        return this;
    }

    /// <summary>
    /// Sets the time in force for orders.
    /// </summary>
    public StrategyBuilder TimeInForce(TimeInForceType tif)
    {
        AddSegment(SegmentType.TimeInForce, SegmentCategory.OrderConfig, "Time In Force",
            [new SegmentParameter
            {
                Name = "Type",
                Label = "Type",
                Type = ParameterType.Enum,
                EnumTypeName = nameof(TimeInForceType),
                Value = tif.ToString(),
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Allows trading outside regular trading hours.
    /// </summary>
    public StrategyBuilder OutsideRTH(bool allow = true)
    {
        AddSegment(SegmentType.OutsideRTH, SegmentCategory.OrderConfig, "Outside RTH",
            [new SegmentParameter
            {
                Name = "Allow",
                Label = "Allow",
                Type = ParameterType.Boolean,
                Value = allow,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Builds and returns the strategy definition.
    /// </summary>
    public StrategyDefinition Build()
    {
        _strategy.ModifiedAt = DateTime.UtcNow;
        return _strategy;
    }

    /// <summary>
    /// Implicitly converts the builder to a strategy definition.
    /// </summary>
    public static implicit operator StrategyDefinition(StrategyBuilder builder) => builder.Build();

    private void AddSegment(SegmentType type, SegmentCategory category, string displayName, List<SegmentParameter> parameters)
    {
        _strategy.Segments.Add(new StrategySegment
        {
            Type = type,
            Category = category,
            DisplayName = displayName,
            Parameters = parameters,
            Order = _segmentOrder++
        });
    }
}
