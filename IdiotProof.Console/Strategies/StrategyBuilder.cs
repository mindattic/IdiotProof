// ============================================================================
// StrategyBuilder - Fluent API for building trading strategies
// ============================================================================

using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Helpers;
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
    public StrategyBuilder IsPriceAbove(double level)
    {
        AddSegment(SegmentType.IsPriceAbove, SegmentCategory.PriceCondition, "Price Above",
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
    public StrategyBuilder IsPriceBelow(double level)
    {
        AddSegment(SegmentType.IsPriceBelow, SegmentCategory.PriceCondition, "Price Below",
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
    public StrategyBuilder IsAboveVwap()
    {
        AddSegment(SegmentType.IsAboveVwap, SegmentCategory.VwapCondition, "Above VWAP", []);
        return this;
    }

    /// <summary>
    /// Adds a below VWAP condition.
    /// </summary>
    public StrategyBuilder IsBelowVwap()
    {
        AddSegment(SegmentType.IsBelowVwap, SegmentCategory.VwapCondition, "Below VWAP", []);
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
    /// Adds an EMA above condition (price >= EMA).
    /// </summary>
    public StrategyBuilder IsEmaAbove(int period)
    {
        AddSegment(SegmentType.IsEmaAbove, SegmentCategory.IndicatorCondition, $"Above EMA {period}",
            [
                new SegmentParameter
                {
                    Name = "Period",
                    Label = "Period",
                    Type = ParameterType.Integer,
                    Value = period,
                    IsRequired = true
                }
            ]);
        return this;
    }

    /// <summary>
    /// Adds an EMA below condition (price <= EMA).
    /// </summary>
    public StrategyBuilder IsEmaBelow(int period)
    {
        AddSegment(SegmentType.IsEmaBelow, SegmentCategory.IndicatorCondition, $"Below EMA {period}",
            [
                new SegmentParameter
                {
                    Name = "Period",
                    Label = "Period",
                    Type = ParameterType.Integer,
                    Value = period,
                    IsRequired = true
                }
            ]);
        return this;
    }

    /// <summary>
    /// Adds an EMA between condition (price is between two EMAs).
    /// </summary>
    public StrategyBuilder IsEmaBetween(int lowerPeriod, int upperPeriod)
    {
        AddSegment(SegmentType.IsEmaBetween, SegmentCategory.IndicatorCondition, $"Between EMA {lowerPeriod} and EMA {upperPeriod}",
            [
                new SegmentParameter
                {
                    Name = "LowerPeriod",
                    Label = "Lower Period",
                    Type = ParameterType.Integer,
                    Value = lowerPeriod,
                    IsRequired = true
                },
                new SegmentParameter
                {
                    Name = "UpperPeriod",
                    Label = "Upper Period",
                    Type = ParameterType.Integer,
                    Value = upperPeriod,
                    IsRequired = true
                }
            ]);
        return this;
    }

    /// <summary>
    /// Opens a long position (buy order).
    /// </summary>
    public StrategyBuilder LongPosition(int quantity, Price priceType = Price.Current, double? limitPrice = null)
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
                EnumTypeName = nameof(Price),
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

        AddSegment(SegmentType.Order, SegmentCategory.Order, "Long Position", parameters);
        return this;
    }

    /// <summary>
    /// Opens a long position (buy order). Alias for LongPosition.
    /// </summary>
    public StrategyBuilder Long(int quantity, Price priceType = Price.Current, double? limitPrice = null)
        => LongPosition(quantity, priceType, limitPrice);

    /// <summary>
    /// Opens a short position (sell order).
    /// </summary>
    public StrategyBuilder ShortPosition(int quantity, Price priceType = Price.Current, double? limitPrice = null)
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
                EnumTypeName = nameof(Price),
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

        AddSegment(SegmentType.Order, SegmentCategory.Order, "Short Position", parameters);
        return this;
    }

    /// <summary>
    /// Opens a short position (sell order). Alias for ShortPosition.
    /// </summary>
    public StrategyBuilder Short(int quantity, Price priceType = Price.Current, double? limitPrice = null)
        => ShortPosition(quantity, priceType, limitPrice);

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
    /// Adds an exit strategy at a specific time.
    /// Chain with .IsProfitable() to only exit if the position is profitable.
    /// </summary>
    /// <param name="time">The time to close the position.</param>
    public StrategyBuilder ExitStrategy(TimeOnly time)
    {
        AddSegment(SegmentType.ExitStrategy, SegmentCategory.PositionManagement, "Exit Strategy",
            [
                new SegmentParameter
                {
                    Name = "Time",
                    Label = "Time",
                    Type = ParameterType.Time,
                    Value = time,
                    IsRequired = true
                }
            ]);
        return this;
    }

    /// <summary>
    /// Only execute the previous exit strategy if the position is profitable.
    /// </summary>
    public StrategyBuilder IsProfitable()
    {
        AddSegment(SegmentType.IsProfitable, SegmentCategory.PositionManagement, "Only If Profitable", []);
        return this;
    }

    /// <summary>
    /// Enables adaptive order management that dynamically adjusts TP/SL based on market conditions.
    /// </summary>
    /// <remarks>
    /// <para><b>Behavior:</b></para>
    /// <list type="bullet">
    ///   <item>Monitors VWAP, EMAs, RSI, MACD, ADX, and volume in real-time.</item>
    ///   <item>Calculates a market score (-100 to +100) indicating trend strength.</item>
    ///   <item>Extends take profit in strong bullish conditions.</item>
    ///   <item>Tightens take profit in weak conditions to secure gains.</item>
    ///   <item>Adjusts stop loss based on volatility and momentum.</item>
    ///   <item>Triggers emergency exit on severely bearish conditions.</item>
    /// </list>
    /// <para><b>Requires:</b> TakeProfit and/or StopLoss must be set.</para>
    /// </remarks>
    public StrategyBuilder AdaptiveOrder(string mode = "Balanced")
    {
        AddSegment(SegmentType.AdaptiveOrder, SegmentCategory.RiskManagement, "Adaptive Order",
            [new SegmentParameter
            {
                Name = "Mode",
                Label = "Mode",
                Type = ParameterType.String,
                Value = mode,
                IsRequired = true
            }]);
        return this;
    }

    /// <summary>
    /// Alias for <see cref="AdaptiveOrder"/>.
    /// Enables adaptive order management that dynamically adjusts TP/SL based on market conditions.
    /// </summary>
    public StrategyBuilder IsAdaptiveOrder(string mode = "Balanced") => AdaptiveOrder(mode);

    /// <summary>
    /// Sets the time in force for orders.
    /// </summary>
    public StrategyBuilder TimeInForce(Shared.Enums.TimeInForce tif)
    {
        AddSegment(SegmentType.TimeInForce, SegmentCategory.OrderConfig, "Time In Force",
            [new SegmentParameter
            {
                Name = "Type",
                Label = "Type",
                Type = ParameterType.Enum,
                EnumTypeName = "TimeInForce",
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
    /// Enables repeating for this strategy.
    /// When enabled, the strategy resets after completion and can fire again when conditions are met.
    /// </summary>
    /// <param name="repeat">True to repeat (default), false to fire once.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <remarks>
    /// <para><b>Behavior:</b></para>
    /// <list type="bullet">
    ///   <item>When true: After take profit or stop loss fills, the strategy resets and waits for conditions again.</item>
    ///   <item>When false (default): The strategy fires once and stops.</item>
    /// </list>
    /// 
    /// <para><b>Example - Repeating scalp strategy:</b></para>
    /// <code>
    /// Stock.Ticker("ABC")
    ///     .SessionDuration(TradingSession.RTH)
    ///     .IsPriceAbove(5.00)
    ///     .IsAboveVwap()
    ///     .Long(100, Price.Current)
    ///     .TakeProfit(6.00)
    ///     .StopLoss(4.50)
    ///     .Repeat()  // Will buy again at $5, sell at $6, repeat
    ///     .Build();
    /// </code>
    /// </remarks>
    public StrategyBuilder Repeat(bool repeat = true)
    {
        _strategy.RepeatEnabled = repeat;
        if (repeat)
        {
            AddSegment(SegmentType.Repeat, SegmentCategory.Execution, "Repeat", []);
        }
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


