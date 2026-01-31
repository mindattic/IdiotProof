// ============================================================================
// StrategyValidatorTests - Tests for StrategyValidator server-side validation
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for StrategyValidator server-side validation logic.
/// Covers all validation rules, error conditions, and edge cases.
/// </summary>
[TestFixture]
public class StrategyValidatorTests
{
    #region ValidateAll Tests

    [Test]
    public void ValidateAll_EmptyList_ReturnsError()
    {
        // Arrange
        var strategies = new List<TradingStrategy>();

        // Act
        var result = StrategyValidator.ValidateAll(strategies);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0], Does.Contain("No strategies provided"));
        });
    }

    [Test]
    public void ValidateAll_SingleValidStrategy_ReturnsValid()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .StopLoss(148)
            .Build();

        // Act
        var result = StrategyValidator.ValidateAll(new[] { strategy });

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateAll_MultipleValidStrategies_ReturnsValid()
    {
        // Arrange
        var strategies = new[]
        {
            Stock.Ticker("AAPL")
                .SessionDuration(new TimeOnly(9, 30), new TimeOnly(10, 30))
                .Breakout(150)
                .Buy(100, Price.Current)
                .TakeProfit(155)
                .Build(),
            Stock.Ticker("MSFT")
                .SessionDuration(new TimeOnly(11, 0), new TimeOnly(12, 0))
                .Breakout(350)
                .Buy(50, Price.Current)
                .TakeProfit(360)
                .Build()
        };

        // Act
        var result = StrategyValidator.ValidateAll(strategies);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateAll_DuplicateSymbolsWithOverlappingTimes_AddsWarning()
    {
        // Arrange
        var strategies = new[]
        {
            Stock.Ticker("AAPL")
                .SessionDuration(new TimeOnly(9, 0), new TimeOnly(10, 0))
                .Breakout(150)
                .Buy(100, Price.Current)
                .TakeProfit(155)
                .Build(),
            Stock.Ticker("AAPL")
                .SessionDuration(new TimeOnly(9, 30), new TimeOnly(10, 30))  // Overlaps with first
                .Breakout(155)
                .Buy(50, Price.Current)
                .TakeProfit(160)
                .Build()
        };

        // Act
        var result = StrategyValidator.ValidateAll(strategies);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Warnings, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(result.Warnings.Any(w => w.Contains("overlapping time windows")), Is.True);
        });
    }

    [Test]
    public void ValidateAll_DuplicateSymbolsWithNoTimeWindow_AddsWarning()
    {
        // Arrange - Strategies without time windows are assumed to overlap
        var strategies = new[]
        {
            Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .TakeProfit(155)
                .Build(),
            Stock.Ticker("AAPL")
                .Breakout(155)
                .Buy(50, Price.Current)
                .TakeProfit(160)
                .Build()
        };

        // Act
        var result = StrategyValidator.ValidateAll(strategies);

        // Assert
        Assert.That(result.Warnings.Any(w => w.Contains("overlapping")), Is.True);
    }

    [Test]
    public void ValidateAll_DuplicateSymbolsWithNonOverlappingTimes_NoWarning()
    {
        // Arrange
        var strategies = new[]
        {
            Stock.Ticker("AAPL")
                .SessionDuration(new TimeOnly(9, 0), new TimeOnly(10, 0))
                .Breakout(150)
                .Buy(100, Price.Current)
                .TakeProfit(155)
                .Build(),
            Stock.Ticker("AAPL")
                .SessionDuration(new TimeOnly(11, 0), new TimeOnly(12, 0))  // No overlap
                .Breakout(155)
                .Buy(50, Price.Current)
                .TakeProfit(160)
                .Build()
        };

        // Act
        var result = StrategyValidator.ValidateAll(strategies);

        // Assert
        Assert.That(result.Warnings.Any(w => w.Contains("overlapping")), Is.False);
    }

    [Test]
    public void ValidateAll_DisabledStrategiesSkipped_NoOverlapWarning()
    {
        // Arrange - Disabled strategies should not trigger overlap warning
        var strategies = new[]
        {
            Stock.Ticker("AAPL")
                .SessionDuration(new TimeOnly(9, 0), new TimeOnly(10, 0))
                .Breakout(150)
                .Buy(100, Price.Current)
                .TakeProfit(155)
                .Build(),
            Stock.Ticker("AAPL")
                .Enabled(false)  // Disabled
                .SessionDuration(new TimeOnly(9, 30), new TimeOnly(10, 30))
                .Breakout(155)
                .Buy(50, Price.Current)
                .TakeProfit(160)
                .Build()
        };

        // Act
        var result = StrategyValidator.ValidateAll(strategies);

        // Assert
        Assert.That(result.Warnings.Any(w => w.Contains("overlapping")), Is.False);
    }

    #endregion

    #region Symbol Validation Tests

    [Test]
    public void Validate_NullSymbol_ReturnsError()
    {
        // Arrange - Create strategy with null symbol using reflection/direct instantiation
        var strategy = new TradingStrategy
        {
            Symbol = null!,
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction { Quantity = 100 }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Contains("Symbol is required")), Is.True);
        });
    }

    [Test]
    public void Validate_EmptySymbol_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction { Quantity = 100 }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Contains("Symbol is required")), Is.True);
        });
    }

    [Test]
    public void Validate_WhitespaceSymbol_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "   ",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction { Quantity = 100 }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Symbol is required")), Is.True);
    }

    #endregion

    #region Condition Validation Tests

    [Test]
    public void Validate_NoConditions_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition>(),
            Order = new OrderAction { Quantity = 100 }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Contains("At least one condition is required")), Is.True);
        });
    }

    [Test]
    public void Validate_NullConditions_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = null!,
            Order = new OrderAction { Quantity = 100 }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("At least one condition is required")), Is.True);
    }

    #endregion

    #region Order Validation Tests

    [Test]
    public void Validate_NullOrder_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = null!
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Contains("Order configuration is required")), Is.True);
        });
    }

    [Test]
    public void Validate_NegativeQuantity_ReturnsError()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(-100, Price.Current)
            .TakeProfit(155)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Contains("Quantity cannot be negative")), Is.True);
        });
    }

    [Test]
    public void Validate_ZeroQuantity_ReturnsWarning()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(0, Price.Current)
            .TakeProfit(155)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);  // Warning, not error
            Assert.That(result.Warnings.Any(w => w.Contains("Quantity is 0")), Is.True);
        });
    }

    #endregion

    #region Take Profit Validation Tests

    [Test]
    public void Validate_TakeProfitNonPositive_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                Side = OrderSide.Buy,
                EnableTakeProfit = true,
                TakeProfitPrice = 0  // Invalid
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Take profit price must be positive")), Is.True);
    }

    [Test]
    public void Validate_TakeProfitNegative_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                Side = OrderSide.Buy,
                EnableTakeProfit = true,
                TakeProfitPrice = -10  // Invalid
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Take profit price must be positive")), Is.True);
    }

    #endregion

    #region Stop Loss Validation Tests

    [Test]
    public void Validate_StopLossNonPositive_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                Side = OrderSide.Buy,
                EnableStopLoss = true,
                StopLossPrice = 0  // Invalid
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Stop loss price must be positive")), Is.True);
    }

    [Test]
    public void Validate_StopLossNegative_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                Side = OrderSide.Buy,
                EnableStopLoss = true,
                StopLossPrice = -5  // Invalid
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Stop loss price must be positive")), Is.True);
    }

    #endregion

    #region Trailing Stop Loss Validation Tests

    [Test]
    public void Validate_TrailingStopLossZero_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0  // Invalid
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Trailing stop loss percent must be between 0 and 100%")), Is.True);
    }

    [Test]
    public void Validate_TrailingStopLossNegative_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = -0.10  // Invalid
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Trailing stop loss percent must be between 0 and 100%")), Is.True);
    }

    [Test]
    public void Validate_TrailingStopLossOver100Percent_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 1.5  // 150% is invalid
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Trailing stop loss percent must be between 0 and 100%")), Is.True);
    }

    [Test]
    public void Validate_TrailingStopLossVeryTight_ReturnsWarning()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0.005  // 0.5% - very tight
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Warnings.Any(w => w.Contains("very tight")), Is.True);
        });
    }

    [Test]
    public void Validate_TrailingStopLossVeryLoose_ReturnsWarning()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 0.60  // 60% - very loose
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Warnings.Any(w => w.Contains("very loose")), Is.True);
        });
    }

    #endregion

    #region ATR Stop Loss Validation Tests

    [Test]
    public void Validate_AtrMultiplierZero_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                AtrStopLoss = new AtrStopLossConfig { Multiplier = 0, Period = 14 }
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("ATR multiplier must be positive")), Is.True);
    }

    [Test]
    public void Validate_AtrMultiplierNegative_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                AtrStopLoss = new AtrStopLossConfig { Multiplier = -1.5, Period = 14 }
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("ATR multiplier must be positive")), Is.True);
    }

    [Test]
    public void Validate_AtrPeriodZero_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                AtrStopLoss = new AtrStopLossConfig { Multiplier = 2.0, Period = 0 }
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("ATR period must be at least 1")), Is.True);
    }

    [Test]
    public void Validate_AtrPeriodNegative_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                AtrStopLoss = new AtrStopLossConfig { Multiplier = 2.0, Period = -5 }
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("ATR period must be at least 1")), Is.True);
    }

    [Test]
    public void Validate_ValidAtrStopLoss_ReturnsValid()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TrailingStopLoss(Atr.Balanced)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    #endregion

    #region ADX Take Profit Validation Tests

    [Test]
    public void Validate_AdxConservativeTargetZero_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                Side = OrderSide.Buy,
                AdxTakeProfit = new AdxTakeProfitConfig
                {
                    ConservativeTarget = 0,
                    AggressiveTarget = 160
                }
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("ADX conservative target must be positive")), Is.True);
    }

    [Test]
    public void Validate_AdxAggressiveTargetZero_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                Side = OrderSide.Buy,
                AdxTakeProfit = new AdxTakeProfitConfig
                {
                    ConservativeTarget = 155,
                    AggressiveTarget = 0
                }
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("ADX aggressive target must be positive")), Is.True);
    }

    [Test]
    public void Validate_AdxConservativeGreaterThanAggressive_ReturnsWarning()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                Side = OrderSide.Buy,
                AdxTakeProfit = new AdxTakeProfitConfig
                {
                    ConservativeTarget = 160,  // Greater than aggressive
                    AggressiveTarget = 155
                }
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);  // Warning, not error
            Assert.That(result.Warnings.Any(w => w.Contains("ADX conservative target should be less than aggressive")), Is.True);
        });
    }

    [Test]
    public void Validate_AdxConservativeEqualsAggressive_ReturnsWarning()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                Side = OrderSide.Buy,
                AdxTakeProfit = new AdxTakeProfitConfig
                {
                    ConservativeTarget = 155,
                    AggressiveTarget = 155  // Same as conservative
                }
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Warnings.Any(w => w.Contains("ADX conservative target should be less than aggressive")), Is.True);
    }

    #endregion

    #region Time Window Validation Tests

    [Test]
    public void Validate_StartTimeAfterEndTime_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction { Quantity = 100 },
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(10, 0)  // Before start
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Start time must be before end time")), Is.True);
    }

    [Test]
    public void Validate_StartTimeEqualsEndTime_ReturnsError()
    {
        // Arrange
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction { Quantity = 100 },
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(10, 0)  // Same as start
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Errors.Any(e => e.Contains("Start time must be before end time")), Is.True);
    }

    [Test]
    public void Validate_ValidTimeWindow_ReturnsValid()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(new TimeOnly(9, 30), new TimeOnly(16, 0))
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_NoTimeWindow_ReturnsValid()
    {
        // Arrange - No time restrictions
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    #endregion

    #region Risk Management Warning Tests

    [Test]
    public void Validate_NoExitStrategy_ReturnsWarning()
    {
        // Arrange - No TP, SL, or trailing stop
        var strategy = new TradingStrategy
        {
            Symbol = "AAPL",
            Conditions = new List<IStrategyCondition> { new BreakoutCondition(150) },
            Order = new OrderAction
            {
                Quantity = 100,
                EnableTakeProfit = false,
                EnableStopLoss = false,
                EnableTrailingStopLoss = false
            }
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);  // Warning, not error
            Assert.That(result.Warnings.Any(w => w.Contains("No exit strategy configured")), Is.True);
        });
    }

    [Test]
    public void Validate_WithTakeProfit_NoExitWarning()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Warnings.Any(w => w.Contains("No exit strategy")), Is.False);
    }

    [Test]
    public void Validate_WithStopLoss_NoExitWarning()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .StopLoss(145)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Warnings.Any(w => w.Contains("No exit strategy")), Is.False);
    }

    [Test]
    public void Validate_WithTrailingStop_NoExitWarning()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TrailingStopLoss(Percent.Ten)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Warnings.Any(w => w.Contains("No exit strategy")), Is.False);
    }

    [Test]
    public void Validate_AllOrNoneWithLargeQuantity_ReturnsWarning()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(500, Price.Current)
            .AllOrNone(true)
            .TakeProfit(155)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Warnings.Any(w => w.Contains("AllOrNone with large quantity")), Is.True);
        });
    }

    [Test]
    public void Validate_AllOrNoneWithSmallQuantity_NoWarning()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(50, Price.Current)
            .AllOrNone(true)
            .TakeProfit(155)
            .Build();

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.That(result.Warnings.Any(w => w.Contains("AllOrNone")), Is.False);
    }

    #endregion

    #region Multiple Errors Tests

    [Test]
    public void Validate_MultipleErrors_ReturnsAllErrors()
    {
        // Arrange - Multiple validation failures
        var strategy = new TradingStrategy
        {
            Symbol = "",
            Conditions = new List<IStrategyCondition>(),
            Order = new OrderAction
            {
                Quantity = -100,
                EnableTrailingStopLoss = true,
                TrailingStopLossPercent = 2.0  // 200%
            },
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(10, 0)
        };

        // Act
        var result = StrategyValidator.Validate(strategy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Count, Is.GreaterThanOrEqualTo(2));
        });
    }

    #endregion

    #region StrategyValidationResult Tests

    [Test]
    public void StrategyValidationResult_NewInstance_IsValid()
    {
        // Arrange & Act
        var result = new StrategyValidationResult();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public void StrategyValidationResult_PrintResults_DoesNotThrow()
    {
        // Arrange
        var result = StrategyValidator.Validate(Stock.Ticker("AAPL")
            .Breakout(150)
            .Buy(100, Price.Current)
            .TakeProfit(155)
            .Build());

        // Act & Assert
        Assert.DoesNotThrow(() => result.PrintResults());
    }

    #endregion
}
