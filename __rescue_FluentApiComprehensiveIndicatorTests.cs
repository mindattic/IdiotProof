// ============================================================================
// FluentApiComprehensiveIndicatorTests - Tests for Fluent API indicator methods
// ============================================================================
//
// This file contains comprehensive unit tests for all fluent API methods that
// add indicator conditions to strategies. These tests verify:
// 1. Each Stock.IsXxx() method correctly adds the appropriate condition
// 2. Conditions can be chained together
// 3. Conditions are properly configured with parameters
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for fluent API indicator condition methods on Stock.
/// </summary>
[TestFixture]
public class FluentApiComprehensiveIndicatorTests
{
    #region IsMomentumAbove Tests

    [Test]
    public void IsMomentumAbove_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsMomentumAbove(0)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<MomentumAboveCondition>());
    }

    [Test]
    public void IsMomentumAbove_WithThreshold_SetsCorrectThreshold()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsMomentumAbove(2.5)
            .Long().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as MomentumAboveCondition;
        Assert.That(condition!.Threshold, Is.EqualTo(2.5));
    }

    [Test]
    public void IsMomentumAbove_CanBeChainedWithOtherConditions()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsMomentumAbove(0)
            .IsAboveVwap()
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
        Assert.That(strategy.Conditions[0], Is.TypeOf<MomentumAboveCondition>());
    }

    #endregion

    #region IsMomentumBelow Tests

    [Test]
    public void IsMomentumBelow_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsMomentumBelow(0)
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<MomentumBelowCondition>());
    }

    [Test]
    public void IsMomentumBelow_WithNegativeThreshold_SetsCorrectThreshold()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsMomentumBelow(-1.5)
            .Short().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as MomentumBelowCondition;
        Assert.That(condition!.Threshold, Is.EqualTo(-1.5));
    }

    #endregion

    #region IsRocAbove Tests

    [Test]
    public void IsRocAbove_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsRocAbove(2.0)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<RocAboveCondition>());
    }

    [Test]
    public void IsRocAbove_WithThreshold_SetsCorrectThreshold()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsRocAbove(3.5)
            .Long().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as RocAboveCondition;
        Assert.That(condition!.Threshold, Is.EqualTo(3.5));
    }

    [Test]
    public void IsRocAbove_NameContainsThreshold()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsRocAbove(2.0)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions[0].Name, Does.Contain("2.0"));
        Assert.That(strategy.Conditions[0].Name, Does.Contain("ROC"));
    }

    #endregion

    #region IsRocBelow Tests

    [Test]
    public void IsRocBelow_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsRocBelow(-2.0)
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<RocBelowCondition>());
    }

    [Test]
    public void IsRocBelow_WithNegativeThreshold_SetsCorrectThreshold()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsRocBelow(-3.0)
            .Short().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as RocBelowCondition;
        Assert.That(condition!.Threshold, Is.EqualTo(-3.0));
    }

    #endregion

    #region IsHigherLows Tests

    [Test]
    public void IsHigherLows_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsHigherLows()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<HigherLowsCondition>());
    }

    [Test]
    public void IsHigherLows_DefaultLookback_Is3()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsHigherLows()
            .Long().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as HigherLowsCondition;
        Assert.That(condition!.LookbackBars, Is.EqualTo(3));
    }

    [Test]
    public void IsHigherLows_WithCustomLookback_SetsLookback()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsHigherLows(5)
            .Long().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as HigherLowsCondition;
        Assert.That(condition!.LookbackBars, Is.EqualTo(5));
    }

    [Test]
    public void IsHigherLows_CanBeChainedWithVwap()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsHigherLows()
            .IsAboveVwap()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<HigherLowsCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<AboveVwapCondition>());
    }

    #endregion

    #region IsLowerHighs Tests

    [Test]
    public void IsLowerHighs_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsLowerHighs()
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<LowerHighsCondition>());
    }

    [Test]
    public void IsLowerHighs_DefaultLookback_Is3()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsLowerHighs()
            .Short().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as LowerHighsCondition;
        Assert.That(condition!.LookbackBars, Is.EqualTo(3));
    }

    [Test]
    public void IsLowerHighs_WithCustomLookback_SetsLookback()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsLowerHighs(4)
            .Short().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as LowerHighsCondition;
        Assert.That(condition!.LookbackBars, Is.EqualTo(4));
    }

    [Test]
    public void IsLowerHighs_CanBeChainedWithBelowVwap()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsLowerHighs()
            .IsBelowVwap()
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
    }

    #endregion

    #region IsVolumeAbove Tests

    [Test]
    public void IsVolumeAbove_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsVolumeAbove(1.5)
            .Breakout(150)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<VolumeAboveCondition>());
    }

    [Test]
    public void IsVolumeAbove_WithMultiplier_SetsCorrectMultiplier()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsVolumeAbove(2.0)
            .Long().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as VolumeAboveCondition;
        Assert.That(condition!.Multiplier, Is.EqualTo(2.0));
    }

    [Test]
    public void IsVolumeAbove_NameContainsMultiplier()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsVolumeAbove(1.5)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions[0].Name, Does.Contain("1.5"));
        Assert.That(strategy.Conditions[0].Name, Does.Contain("Volume").IgnoreCase);
    }

    #endregion

    #region IsCloseAboveVwap Tests

    [Test]
    public void IsCloseAboveVwap_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsCloseAboveVwap()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<CloseAboveVwapCondition>());
    }

    [Test]
    public void IsCloseAboveVwap_NameIsDescriptive()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsCloseAboveVwap()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions[0].Name, Does.Contain("Close").IgnoreCase);
        Assert.That(strategy.Conditions[0].Name, Does.Contain("VWAP").IgnoreCase);
    }

    [Test]
    public void IsCloseAboveVwap_CanBeChainedWithHigherLows()
    {
        // Arrange & Act - Example from copilot-instructions.md
        var strategy = Stock.Ticker("AAPL")
            .IsCloseAboveVwap()
            .IsHigherLows()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<CloseAboveVwapCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<HigherLowsCondition>());
    }

    #endregion

    #region IsVwapRejection Tests

    [Test]
    public void IsVwapRejection_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsVwapRejection()
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<VwapRejectionCondition>());
    }

    [Test]
    public void IsVwapRejection_NameIsDescriptive()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsVwapRejection()
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions[0].Name, Does.Contain("VWAP").IgnoreCase);
        Assert.That(strategy.Conditions[0].Name, Does.Contain("Rejection").IgnoreCase);
    }

    [Test]
    public void IsVwapRejection_CanBeChainedWithMomentumBelow()
    {
        // Arrange & Act - Example from copilot-instructions.md
        var strategy = Stock.Ticker("AAPL")
            .IsVwapRejection()
            .IsMomentumBelow(0)
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<VwapRejectionCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<MomentumBelowCondition>());
    }

    #endregion

    #region IsEmaTurningUp Tests

    [Test]
    public void IsEmaTurningUp_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsEmaTurningUp(9)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<EmaTurningUpCondition>());
    }

    [Test]
    public void IsEmaTurningUp_WithPeriod_SetsCorrectPeriod()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsEmaTurningUp(21)
            .Long().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as EmaTurningUpCondition;
        Assert.That(condition!.Period, Is.EqualTo(21));
    }

    [Test]
    public void IsEmaTurningUp_NameContainsPeriod()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsEmaTurningUp(9)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions[0].Name, Does.Contain("9"));
        Assert.That(strategy.Conditions[0].Name, Does.Contain("EMA").IgnoreCase);
    }

    [Test]
    public void IsEmaTurningUp_CanBeChainedWithAboveVwap()
    {
        // Arrange & Act - Example from copilot-instructions.md
        var strategy = Stock.Ticker("AAPL")
            .IsEmaTurningUp(9)
            .IsAboveVwap()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<EmaTurningUpCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<AboveVwapCondition>());
    }

    #endregion

    #region GapUp Fluent API Tests

    [Test]
    public void GapUp_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("NVDA")
            .GapUp(5)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<GapUpCondition>());
    }

    [Test]
    public void GapUp_WithPercentage_SetsCorrectPercentage()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("NVDA")
            .GapUp(7.5)
            .Long().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as GapUpCondition;
        Assert.That(condition!.Percentage, Is.EqualTo(7.5));
    }

    [Test]
    public void GapUp_CanBeChainedWithVwapAndDi()
    {
        // Arrange & Act - Example from copilot-instructions.md
        var strategy = Stock.Ticker("NVDA")
            .GapUp(5)
            .IsAboveVwap()
            .IsDI(DiDirection.Positive)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
        Assert.That(strategy.Conditions[0], Is.TypeOf<GapUpCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<AboveVwapCondition>());
        Assert.That(strategy.Conditions[2], Is.TypeOf<DiCondition>());
    }

    #endregion

    #region GapDown Fluent API Tests

    [Test]
    public void GapDown_AddsConditionToStrategy()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("NVDA")
            .GapDown(5)
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
        Assert.That(strategy.Conditions[0], Is.TypeOf<GapDownCondition>());
    }

    [Test]
    public void GapDown_WithPercentage_SetsCorrectPercentage()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("NVDA")
            .GapDown(3.0)
            .Short().Quantity(100)
            .Build();

        // Assert
        var condition = strategy.Conditions[0] as GapDownCondition;
        Assert.That(condition!.Percentage, Is.EqualTo(3.0));
    }

    [Test]
    public void GapDown_CanBeChainedWithBelowVwap()
    {
        // Arrange & Act - Example from copilot-instructions.md
        var strategy = Stock.Ticker("NVDA")
            .GapDown(5)
            .IsBelowVwap()
            .Short().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
        Assert.That(strategy.Conditions[0], Is.TypeOf<GapDownCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<BelowVwapCondition>());
    }

    #endregion

    #region Combined Indicator Strategy Tests

    [Test]
    public void TrendFollowing_CombinedIndicators_AllConditionsAdded()
    {
        // Arrange & Act - From copilot-instructions.md Trend Following example
        var strategy = Stock.Ticker("AAPL")
            .IsAdx(Comparison.Gte, 25)
            .IsDI(DiDirection.Positive)
            .IsEmaAbove(9)
            .IsEmaAbove(21)
            .IsMomentumAbove(0)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(5));
        Assert.That(strategy.Conditions[0], Is.TypeOf<AdxCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<DiCondition>());
        Assert.That(strategy.Conditions[2], Is.TypeOf<EmaAboveCondition>());
        Assert.That(strategy.Conditions[3], Is.TypeOf<EmaAboveCondition>());
        Assert.That(strategy.Conditions[4], Is.TypeOf<MomentumAboveCondition>());
    }

    [Test]
    public void MeanReversion_CombinedIndicators_AllConditionsAdded()
    {
        // Arrange & Act - From copilot-instructions.md Mean Reversion example
        var strategy = Stock.Ticker("AAPL")
            .IsRsi(RsiState.Oversold, 30)
            .IsEmaBetween(9, 21)
            .IsVolumeAbove(1.5)
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
        Assert.That(strategy.Conditions[0], Is.TypeOf<RsiCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<EmaBetweenCondition>());
        Assert.That(strategy.Conditions[2], Is.TypeOf<VolumeAboveCondition>());
    }

    [Test]
    public void VwapBounce_CombinedIndicators_AllConditionsAdded()
    {
        // Arrange & Act - From copilot-instructions.md VWAP Bounce example
        var strategy = Stock.Ticker("AAPL")
            .IsCloseAboveVwap()
            .IsEmaAbove(9)
            .IsHigherLows()
            .Long().Quantity(100)
            .Build();

        // Assert
        Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
        Assert.That(strategy.Conditions[0], Is.TypeOf<CloseAboveVwapCondition>());
        Assert.That(strategy.Conditions[1], Is.TypeOf<EmaAboveCondition>());
        Assert.That(strategy.Conditions[2], Is.TypeOf<HigherLowsCondition>());
    }

    #endregion
}
