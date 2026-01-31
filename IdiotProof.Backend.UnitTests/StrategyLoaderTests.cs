// ============================================================================
// StrategyLoaderTests - Tests for JSON to TradingStrategy conversion
// ============================================================================

using IdiotProof.Backend.Models;
using IdiotProof.Shared.Models;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for StrategyLoader JSON to TradingStrategy conversion.
/// Covers all segment types, positive and negative cases.
/// </summary>
[TestFixture]
public class StrategyLoaderTests
{
    #region ConvertDefinition - Null and Empty Tests

    [Test]
    public void ConvertDefinition_NullDefinition_ReturnsNull()
    {
        // Act
        var result = StrategyLoader.ConvertDefinition(null!);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConvertDefinition_EmptySymbol_ReturnsNull()
    {
        // Arrange
        var definition = new StrategyDefinition
        {
            Name = "Test Strategy",
            Symbol = "",
            Segments = new List<StrategySegment>
            {
                CreateTickerSegment("AAPL"),
                CreateBreakoutSegment(150)
            }
        };

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConvertDefinition_NullSymbol_ReturnsNull()
    {
        // Arrange
        var definition = new StrategyDefinition
        {
            Name = "Test Strategy",
            Symbol = null!,
            Segments = new List<StrategySegment>
            {
                CreateTickerSegment("AAPL")
            }
        };

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConvertDefinition_EmptySegments_ReturnsNull()
    {
        // Arrange
        var definition = new StrategyDefinition
        {
            Name = "Test Strategy",
            Symbol = "AAPL",
            Segments = new List<StrategySegment>()
        };

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region Ticker Segment Tests

    [Test]
    public void ConvertDefinition_WithTickerAndBuyOrder_CreatesStrategy()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Symbol, Is.EqualTo("AAPL"));
            Assert.That(result.Order.Side, Is.EqualTo(Backend.Enums.OrderSide.Buy));
            Assert.That(result.Order.Quantity, Is.EqualTo(100));
        });
    }

    #endregion

    #region Session Duration Tests

    [Test]
    public void ConvertDefinition_WithSessionDurationPreMarket_SetsTimeWindow()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateSessionDurationSegment("PreMarket"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.StartTime, Is.Not.Null);
            Assert.That(result.EndTime, Is.Not.Null);
        });
    }

    [Test]
    public void ConvertDefinition_WithSessionDurationRTH_SetsTimeWindow()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateSessionDurationSegment("RTH"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Session, Is.EqualTo(Backend.Enums.TradingSession.RTH));
    }

    [Test]
    public void ConvertDefinition_WithInvalidSession_DoesNotThrow()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateSessionDurationSegment("InvalidSession"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100));

        // Act & Assert - Should not throw, may return null or strategy without session
        Assert.DoesNotThrow(() => StrategyLoader.ConvertDefinition(definition));
    }

    #endregion

    #region Price Condition Tests

    [Test]
    public void ConvertDefinition_WithBreakout_CreatesBreakoutCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150.50),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(1));
            Assert.That(result.Conditions[0], Is.TypeOf<BreakoutCondition>());
        });
    }

    [Test]
    public void ConvertDefinition_WithPullback_CreatesPullbackCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreatePullbackSegment(148.00),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(1));
            Assert.That(result.Conditions[0], Is.TypeOf<PullbackCondition>());
        });
    }

    [Test]
    public void ConvertDefinition_WithPriceAbove_CreatesPriceAboveCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreatePriceAboveSegment(150.00),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(1));
            Assert.That(result.Conditions[0], Is.TypeOf<PriceAtOrAboveCondition>());
        });
    }

    [Test]
    public void ConvertDefinition_WithPriceBelow_CreatesPriceBelowCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreatePriceBelowSegment(145.00),
            CreateSellSegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(1));
            Assert.That(result.Conditions[0], Is.TypeOf<PriceBelowCondition>());
        });
    }

    [Test]
    public void ConvertDefinition_WithMultiplePriceConditions_CreatesAllConditions()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreatePullbackSegment(148),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(2));
        });
    }

    #endregion

    #region VWAP Condition Tests

    [Test]
    public void ConvertDefinition_WithAboveVwap_CreatesAboveVwapCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateAboveVwapSegment(0.10),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(1));
            Assert.That(result.Conditions[0], Is.TypeOf<AboveVwapCondition>());
        });
    }

    [Test]
    public void ConvertDefinition_WithBelowVwap_CreatesBelowVwapCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBelowVwapSegment(0.05),
            CreateSellSegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(1));
            Assert.That(result.Conditions[0], Is.TypeOf<BelowVwapCondition>());
        });
    }

    #endregion

    #region Indicator Condition Tests

    [Test]
    public void ConvertDefinition_WithIsRsiOversold_CreatesRsiCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateRsiSegment("Oversold", 30),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(1));
            Assert.That(result.Conditions[0], Is.TypeOf<RsiCondition>());
        });
    }

    [Test]
    public void ConvertDefinition_WithIsMacdBullish_CreatesMacdCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateMacdSegment("Bullish"),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions, Has.Count.EqualTo(1));
            Assert.That(result.Conditions[0], Is.TypeOf<MacdCondition>());
        });
    }

    [Test]
    public void ConvertDefinition_WithIsAdxAbove_CreatesAdxCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateAdxSegment("Above", 25),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions[0], Is.TypeOf<AdxCondition>());
        });
    }

    [Test]
    public void ConvertDefinition_WithIsDI_CreatesDiCondition()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateDiSegment("PlusDominant", 5),
            CreateBuySegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Conditions[0], Is.TypeOf<DiCondition>());
        });
    }

    #endregion

    #region Order Segment Tests

    [Test]
    public void ConvertDefinition_WithBuyMarketOrder_CreatesBuyOrder()
    {
        // Arrange
        var segment = CreateBuySegment(100, "Current", "Market");
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            segment);

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Order.Side, Is.EqualTo(Backend.Enums.OrderSide.Buy));
            Assert.That(result.Order.Quantity, Is.EqualTo(100));
            Assert.That(result.Order.Type, Is.EqualTo(Backend.Enums.OrderType.Market));
        });
    }

    [Test]
    public void ConvertDefinition_WithBuyLimitOrder_CreatesBuyOrder()
    {
        // Arrange
        var segment = CreateBuySegment(50, "Current", "Limit");
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            segment);

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Order.Side, Is.EqualTo(Backend.Enums.OrderSide.Buy));
            Assert.That(result.Order.Quantity, Is.EqualTo(50));
            Assert.That(result.Order.Type, Is.EqualTo(Backend.Enums.OrderType.Limit));
        });
    }

    [Test]
    public void ConvertDefinition_WithSellOrder_CreatesSellOrder()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreatePriceBelowSegment(145),
            CreateSellSegment(100));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Order.Side, Is.EqualTo(Backend.Enums.OrderSide.Sell));
            Assert.That(result.Order.Quantity, Is.EqualTo(100));
        });
    }

    [Test]
    public void ConvertDefinition_WithCloseOrder_CreatesCloseOrder()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreatePriceAboveSegment(155),
            CreateCloseSegment(100, "Buy"));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region Risk Management Segment Tests

    [Test]
    public void ConvertDefinition_WithTakeProfit_SetsTakeProfit()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100),
            CreateTakeProfitSegment(155));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Order.EnableTakeProfit, Is.True);
            Assert.That(result.Order.TakeProfitPrice, Is.EqualTo(155));
        });
    }

    [Test]
    public void ConvertDefinition_WithStopLoss_SetsStopLoss()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100),
            CreateStopLossSegment(145));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Order.EnableStopLoss, Is.True);
            Assert.That(result.Order.StopLossPrice, Is.EqualTo(145));
        });
    }

    [Test]
    public void ConvertDefinition_WithTrailingStopLoss_SetsTrailingStopLoss()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100),
            CreateTrailingStopLossSegment(0.10));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Order.EnableTrailingStopLoss, Is.True);
            Assert.That(result.Order.TrailingStopLossPercent, Is.EqualTo(0.10).Within(0.001));
        });
    }

    #endregion

    #region Order Config Segment Tests

    [Test]
    public void ConvertDefinition_WithTimeInForceGTC_SetsTimeInForce()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100),
            CreateTimeInForceSegment("GoodTillCancel"));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result!.Order.TimeInForce, Is.EqualTo(Backend.Enums.TimeInForce.GoodTillCancel));
    }

    [Test]
    public void ConvertDefinition_WithTimeInForceDAY_SetsTimeInForce()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100),
            CreateTimeInForceSegment("Day"));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result!.Order.TimeInForce, Is.EqualTo(Backend.Enums.TimeInForce.Day));
    }

    [Test]
    public void ConvertDefinition_WithOutsideRTH_SetsOutsideRth()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100),
            CreateOutsideRTHSegment(true, true));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Order.OutsideRth, Is.True);
            Assert.That(result.Order.TakeProfitOutsideRth, Is.True);
        });
    }

    [Test]
    public void ConvertDefinition_WithAllOrNone_SetsAllOrNone()
    {
        // Arrange
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateBuySegment(100),
            CreateAllOrNoneSegment(true));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result!.Order.AllOrNone, Is.True);
    }

    #endregion

    #region Complex Strategy Tests

    [Test]
    public void ConvertDefinition_FullStrategyWithAllSegments_CreatesCompleteStrategy()
    {
        // Arrange - A realistic full strategy
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateSessionDurationSegment("PreMarket"),
            CreateBreakoutSegment(150),
            CreateAboveVwapSegment(0.05),
            CreateRsiSegment("Oversold", 30),
            CreateBuySegment(100, "Current", "Limit"),
            CreateTakeProfitSegment(155),
            CreateStopLossSegment(148),
            CreateTimeInForceSegment("GoodTillCancel"),
            CreateOutsideRTHSegment(true, true));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Symbol, Is.EqualTo("AAPL"));
            Assert.That(result.Conditions, Has.Count.EqualTo(3));
            Assert.That(result.Order.Side, Is.EqualTo(Backend.Enums.OrderSide.Buy));
            Assert.That(result.Order.Quantity, Is.EqualTo(100));
            Assert.That(result.Order.EnableTakeProfit, Is.True);
            Assert.That(result.Order.EnableStopLoss, Is.True);
            Assert.That(result.Order.TimeInForce, Is.EqualTo(Backend.Enums.TimeInForce.GoodTillCancel));
            Assert.That(result.Order.OutsideRth, Is.True);
        });
    }

    [Test]
    public void ConvertDefinition_StrategyWithMultipleIndicators_CreatesAllConditions()
    {
        // Arrange
        var definition = CreateStrategyDefinition("NVDA",
            CreateTickerSegment("NVDA"),
            CreateRsiSegment("Oversold", 30),
            CreateMacdSegment("Bullish"),
            CreateAdxSegment("Above", 25),
            CreateDiSegment("PlusDominant", 0),
            CreateBuySegment(25));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Conditions, Has.Count.EqualTo(4));
            Assert.That(result.Conditions[0], Is.TypeOf<RsiCondition>());
            Assert.That(result.Conditions[1], Is.TypeOf<MacdCondition>());
            Assert.That(result.Conditions[2], Is.TypeOf<AdxCondition>());
            Assert.That(result.Conditions[3], Is.TypeOf<DiCondition>());
        });
    }

    #endregion

    #region Edge Cases and Error Handling

    [Test]
    public void ConvertDefinition_DisabledStrategy_PreservesEnabledState()
    {
        // Arrange
        var definition = new StrategyDefinition
        {
            Name = "Test",
            Symbol = "AAPL",
            Enabled = false,
            Segments = new List<StrategySegment>
            {
                CreateTickerSegment("AAPL"),
                CreateBreakoutSegment(150),
                CreateBuySegment(100)
            }
        };

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert
        Assert.That(result!.Enabled, Is.False);
    }

    [Test]
    public void ConvertDefinition_StrategyWithNoOrder_ReturnsNull()
    {
        // Arrange - Only conditions, no buy/sell/close
        var definition = CreateStrategyDefinition("AAPL",
            CreateTickerSegment("AAPL"),
            CreateBreakoutSegment(150),
            CreateAboveVwapSegment(0));

        // Act
        var result = StrategyLoader.ConvertDefinition(definition);

        // Assert - No order means the strategy is incomplete
        Assert.That(result, Is.Null);
    }

    #endregion

    #region Default Folder Tests

    [Test]
    public void GetDefaultFolder_ReturnsPath()
    {
        // Act
        var folder = StrategyLoader.GetDefaultFolder();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(folder, Is.Not.Null.And.Not.Empty);
            Assert.That(folder, Does.Contain("IdiotProof"));
            Assert.That(folder, Does.Contain("Strategies"));
        });
    }

    [Test]
    public void GetDateFolder_WithDate_ReturnsCorrectPath()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 15);

        // Act
        var folder = StrategyLoader.GetDateFolder(date);

        // Assert
        Assert.That(folder, Does.Contain("2025-01-15"));
    }

    [Test]
    public void GetDateFolder_WithCustomBaseFolder_UsesBaseFolder()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 15);
        var baseFolder = @"C:\CustomFolder";

        // Act
        var folder = StrategyLoader.GetDateFolder(date, baseFolder);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(folder, Does.StartWith(baseFolder));
            Assert.That(folder, Does.Contain("2025-01-15"));
        });
    }

    #endregion

    #region Helper Methods

    private static StrategyDefinition CreateStrategyDefinition(string symbol, params StrategySegment[] segments)
    {
        int order = 1;
        foreach (var segment in segments)
        {
            segment.Order = order++;
        }

        return new StrategyDefinition
        {
            Name = $"Test Strategy for {symbol}",
            Symbol = symbol,
            Enabled = true,
            Segments = segments.ToList()
        };
    }

    private static SegmentParameter CreateParam(string name, ParameterType type, object? value, string? enumTypeName = null)
    {
        return new SegmentParameter
        {
            Name = name,
            Label = name,  // Use name as label for tests
            Type = type,
            Value = value,
            EnumTypeName = enumTypeName
        };
    }

    private static StrategySegment CreateTickerSegment(string symbol)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.Ticker,
            Category = Shared.Enums.SegmentCategory.Start,
            DisplayName = "Ticker",
            Parameters = new List<SegmentParameter> { CreateParam("symbol", ParameterType.String, symbol) }
        };
    }

    private static StrategySegment CreateSessionDurationSegment(string session)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.SessionDuration,
            Category = Shared.Enums.SegmentCategory.Session,
            DisplayName = "Session Duration",
            Parameters = new List<SegmentParameter> { CreateParam("session", ParameterType.Enum, session, "TradingSession") }
        };
    }

    private static StrategySegment CreateBreakoutSegment(double level)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.Breakout,
            Category = Shared.Enums.SegmentCategory.PriceCondition,
            DisplayName = "Breakout",
            Parameters = new List<SegmentParameter> { CreateParam("level", ParameterType.Price, level) }
        };
    }

    private static StrategySegment CreatePullbackSegment(double level)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.Pullback,
            Category = Shared.Enums.SegmentCategory.PriceCondition,
            DisplayName = "Pullback",
            Parameters = new List<SegmentParameter> { CreateParam("level", ParameterType.Price, level) }
        };
    }

    private static StrategySegment CreatePriceAboveSegment(double level)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.PriceAbove,
            Category = Shared.Enums.SegmentCategory.PriceCondition,
            DisplayName = "Price Above",
            Parameters = new List<SegmentParameter> { CreateParam("level", ParameterType.Price, level) }
        };
    }

    private static StrategySegment CreatePriceBelowSegment(double level)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.PriceBelow,
            Category = Shared.Enums.SegmentCategory.PriceCondition,
            DisplayName = "Price Below",
            Parameters = new List<SegmentParameter> { CreateParam("level", ParameterType.Price, level) }
        };
    }

    private static StrategySegment CreateAboveVwapSegment(double buffer)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.AboveVwap,
            Category = Shared.Enums.SegmentCategory.VwapCondition,
            DisplayName = "Above VWAP",
            Parameters = new List<SegmentParameter> { CreateParam("buffer", ParameterType.Double, buffer) }
        };
    }

    private static StrategySegment CreateBelowVwapSegment(double buffer)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.BelowVwap,
            Category = Shared.Enums.SegmentCategory.VwapCondition,
            DisplayName = "Below VWAP",
            Parameters = new List<SegmentParameter> { CreateParam("buffer", ParameterType.Double, buffer) }
        };
    }

    private static StrategySegment CreateRsiSegment(string state, double? threshold = null)
    {
        var parameters = new List<SegmentParameter> { CreateParam("state", ParameterType.Enum, state, "RsiState") };
        if (threshold.HasValue)
        {
            parameters.Add(CreateParam("threshold", ParameterType.Double, threshold.Value));
        }

        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.IsRsi,
            Category = Shared.Enums.SegmentCategory.IndicatorCondition,
            DisplayName = "RSI",
            Parameters = parameters
        };
    }

    private static StrategySegment CreateMacdSegment(string state)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.IsMacd,
            Category = Shared.Enums.SegmentCategory.IndicatorCondition,
            DisplayName = "MACD",
            Parameters = new List<SegmentParameter> { CreateParam("state", ParameterType.Enum, state, "MacdState") }
        };
    }

    private static StrategySegment CreateAdxSegment(string comparison, double threshold)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.IsAdx,
            Category = Shared.Enums.SegmentCategory.IndicatorCondition,
            DisplayName = "ADX",
            Parameters = new List<SegmentParameter>
            {
                CreateParam("comparison", ParameterType.Enum, comparison, "Comparison"),
                CreateParam("threshold", ParameterType.Double, threshold)
            }
        };
    }

    private static StrategySegment CreateDiSegment(string direction, double minDifference)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.IsDI,
            Category = Shared.Enums.SegmentCategory.IndicatorCondition,
            DisplayName = "DI",
            Parameters = new List<SegmentParameter>
            {
                CreateParam("direction", ParameterType.Enum, direction, "DiDirection"),
                CreateParam("minDifference", ParameterType.Double, minDifference)
            }
        };
    }

    private static StrategySegment CreateBuySegment(int quantity, string priceType = "Current", string orderType = "Market")
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.Buy,
            Category = Shared.Enums.SegmentCategory.Order,
            DisplayName = "Buy",
            Parameters = new List<SegmentParameter>
            {
                CreateParam("quantity", ParameterType.Integer, quantity),
                CreateParam("priceType", ParameterType.Enum, priceType, "Price"),
                CreateParam("orderType", ParameterType.Enum, orderType, "OrderType")
            }
        };
    }

    private static StrategySegment CreateSellSegment(int quantity, string priceType = "Current", string orderType = "Market")
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.Sell,
            Category = Shared.Enums.SegmentCategory.Order,
            DisplayName = "Sell",
            Parameters = new List<SegmentParameter>
            {
                CreateParam("quantity", ParameterType.Integer, quantity),
                CreateParam("priceType", ParameterType.Enum, priceType, "Price"),
                CreateParam("orderType", ParameterType.Enum, orderType, "OrderType")
            }
        };
    }

    private static StrategySegment CreateCloseSegment(int quantity, string positionSide)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.Close,
            Category = Shared.Enums.SegmentCategory.Order,
            DisplayName = "Close",
            Parameters = new List<SegmentParameter>
            {
                CreateParam("quantity", ParameterType.Integer, quantity),
                CreateParam("positionSide", ParameterType.Enum, positionSide, "OrderSide")
            }
        };
    }

    private static StrategySegment CreateTakeProfitSegment(double price)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.TakeProfit,
            Category = Shared.Enums.SegmentCategory.RiskManagement,
            DisplayName = "Take Profit",
            Parameters = new List<SegmentParameter> { CreateParam("price", ParameterType.Price, price) }
        };
    }

    private static StrategySegment CreateStopLossSegment(double price)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.StopLoss,
            Category = Shared.Enums.SegmentCategory.RiskManagement,
            DisplayName = "Stop Loss",
            Parameters = new List<SegmentParameter> { CreateParam("price", ParameterType.Price, price) }
        };
    }

    private static StrategySegment CreateTrailingStopLossSegment(double percent)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.TrailingStopLoss,
            Category = Shared.Enums.SegmentCategory.RiskManagement,
            DisplayName = "Trailing Stop Loss",
            Parameters = new List<SegmentParameter> { CreateParam("percent", ParameterType.Percentage, percent) }
        };
    }

    private static StrategySegment CreateTimeInForceSegment(string tif)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.TimeInForce,
            Category = Shared.Enums.SegmentCategory.OrderConfig,
            DisplayName = "Time In Force",
            Parameters = new List<SegmentParameter> { CreateParam("tif", ParameterType.Enum, tif, "TimeInForce") }
        };
    }

    private static StrategySegment CreateOutsideRTHSegment(bool outsideRth, bool takeProfit)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.OutsideRTH,
            Category = Shared.Enums.SegmentCategory.OrderConfig,
            DisplayName = "Outside RTH",
            Parameters = new List<SegmentParameter>
            {
                CreateParam("outsideRth", ParameterType.Boolean, outsideRth),
                CreateParam("takeProfit", ParameterType.Boolean, takeProfit)
            }
        };
    }

    private static StrategySegment CreateAllOrNoneSegment(bool allOrNone)
    {
        return new StrategySegment
        {
            Type = Shared.Enums.SegmentType.AllOrNone,
            Category = Shared.Enums.SegmentCategory.OrderConfig,
            DisplayName = "All Or None",
            Parameters = new List<SegmentParameter> { CreateParam("allOrNone", ParameterType.Boolean, allOrNone) }
        };
    }

    #endregion
}
