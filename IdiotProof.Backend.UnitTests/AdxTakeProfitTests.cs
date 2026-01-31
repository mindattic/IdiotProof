// ============================================================================
// AdxTakeProfitTests - Unit tests for ADX-based dynamic take profit
// ============================================================================
//
// Tests the AdxTakeProfitConfig class and the TakeProfit fluent API overloads
// that support ADX-based dynamic take profit levels.
//
// ADX Take Profit Rules:
//   ADX < 15:     Weak/No Trend    → Conservative target (midpoint)
//   ADX 15-25:    Developing Trend → Interpolate between targets
//   ADX 25-35:    Strong Trend     → Aggressive target (range high)
//   ADX > 35:     Very Strong      → Aggressive target + extensions
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for ADX-based dynamic take profit configuration.
/// </summary>
[TestFixture]
public class AdxTakeProfitTests
{
    #region AdxTakeProfitConfig Creation Tests

    [Test]
    public void FromRange_CreatesConfigWithCorrectTargets()
    {
        // Arrange & Act
        var config = AdxTakeProfitConfig.FromRange(1.30, 1.70);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.ConservativeTarget, Is.EqualTo(1.30));
            Assert.That(config.AggressiveTarget, Is.EqualTo(1.70));
        });
    }

    [Test]
    public void FromRangeWithMidpoint_CalculatesMidpointAsConservative()
    {
        // Arrange & Act
        var config = AdxTakeProfitConfig.FromRangeWithMidpoint(1.30, 1.70);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.ConservativeTarget, Is.EqualTo(1.50)); // Midpoint of 1.30-1.70
            Assert.That(config.AggressiveTarget, Is.EqualTo(1.70));
        });
    }

    [Test]
    public void DefaultThresholds_AreCorrect()
    {
        // Arrange & Act
        var config = AdxTakeProfitConfig.FromRange(100, 110);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.WeakTrendThreshold, Is.EqualTo(15.0));
            Assert.That(config.DevelopingTrendThreshold, Is.EqualTo(25.0));
            Assert.That(config.StrongTrendThreshold, Is.EqualTo(35.0));
            Assert.That(config.ExitOnAdxRollover, Is.True);
            Assert.That(config.AdxRolloverThreshold, Is.EqualTo(2.0));
        });
    }

    [Test]
    public void CustomThresholds_ArePreserved()
    {
        // Arrange & Act
        var config = new AdxTakeProfitConfig
        {
            ConservativeTarget = 100,
            AggressiveTarget = 120,
            WeakTrendThreshold = 20,
            DevelopingTrendThreshold = 30,
            StrongTrendThreshold = 40,
            ExitOnAdxRollover = false,
            AdxRolloverThreshold = 3.5
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.WeakTrendThreshold, Is.EqualTo(20));
            Assert.That(config.DevelopingTrendThreshold, Is.EqualTo(30));
            Assert.That(config.StrongTrendThreshold, Is.EqualTo(40));
            Assert.That(config.ExitOnAdxRollover, Is.False);
            Assert.That(config.AdxRolloverThreshold, Is.EqualTo(3.5));
        });
    }

    #endregion

    #region GetTargetForAdx Tests

    [Test]
    public void GetTargetForAdx_WeakTrend_ReturnsConservativeTarget()
    {
        // Arrange
        var config = AdxTakeProfitConfig.FromRange(1.30, 1.70);

        // Act & Assert - ADX values below weak threshold (15)
        Assert.Multiple(() =>
        {
            Assert.That(config.GetTargetForAdx(0), Is.EqualTo(1.30));
            Assert.That(config.GetTargetForAdx(5), Is.EqualTo(1.30));
            Assert.That(config.GetTargetForAdx(10), Is.EqualTo(1.30));
            Assert.That(config.GetTargetForAdx(14.9), Is.EqualTo(1.30));
        });
    }

    [Test]
    public void GetTargetForAdx_StrongTrend_ReturnsAggressiveTarget()
    {
        // Arrange
        var config = AdxTakeProfitConfig.FromRange(1.30, 1.70);

        // Act & Assert - ADX values at or above developing threshold (25)
        Assert.Multiple(() =>
        {
            Assert.That(config.GetTargetForAdx(25), Is.EqualTo(1.70));
            Assert.That(config.GetTargetForAdx(30), Is.EqualTo(1.70));
            Assert.That(config.GetTargetForAdx(35), Is.EqualTo(1.70));
            Assert.That(config.GetTargetForAdx(50), Is.EqualTo(1.70));
        });
    }

    [Test]
    public void GetTargetForAdx_DevelopingTrend_InterpolatesBetweenTargets()
    {
        // Arrange
        var config = AdxTakeProfitConfig.FromRange(1.30, 1.70);
        // Range is 0.40 (1.70 - 1.30)
        // ADX range for interpolation is 15-25 (10 points)

        // Act & Assert
        Assert.Multiple(() =>
        {
            // ADX 15 = 0% into developing range → Conservative (1.30)
            Assert.That(config.GetTargetForAdx(15), Is.EqualTo(1.30).Within(0.01));

            // ADX 20 = 50% into developing range → Midpoint (1.50)
            Assert.That(config.GetTargetForAdx(20), Is.EqualTo(1.50).Within(0.01));

            // ADX 24.9 ≈ 99% into developing range → Near aggressive
            Assert.That(config.GetTargetForAdx(24.9), Is.EqualTo(1.696).Within(0.01));
        });
    }

    [Test]
    public void GetTargetForAdx_ExactThresholdBoundaries()
    {
        // Arrange
        var config = AdxTakeProfitConfig.FromRange(100, 200);

        // Act & Assert - Test exact boundary values
        Assert.Multiple(() =>
        {
            // Just below weak threshold
            Assert.That(config.GetTargetForAdx(14.99), Is.EqualTo(100));

            // At weak threshold (start of interpolation)
            Assert.That(config.GetTargetForAdx(15), Is.EqualTo(100));

            // Just below developing threshold (end of interpolation)
            Assert.That(config.GetTargetForAdx(24.99), Is.EqualTo(199.9).Within(0.1));

            // At developing threshold
            Assert.That(config.GetTargetForAdx(25), Is.EqualTo(200));
        });
    }

    [Test]
    public void GetTargetForAdx_WithCustomThresholds_UsesCustomValues()
    {
        // Arrange - Custom thresholds: weak=20, developing=30
        var config = new AdxTakeProfitConfig
        {
            ConservativeTarget = 50,
            AggressiveTarget = 100,
            WeakTrendThreshold = 20,
            DevelopingTrendThreshold = 30,
            StrongTrendThreshold = 40
        };

        // Act & Assert
        Assert.Multiple(() =>
        {
            // ADX < 20 → Conservative
            Assert.That(config.GetTargetForAdx(15), Is.EqualTo(50));

            // ADX 25 = 50% into 20-30 range → Midpoint (75)
            Assert.That(config.GetTargetForAdx(25), Is.EqualTo(75).Within(0.01));

            // ADX >= 30 → Aggressive
            Assert.That(config.GetTargetForAdx(35), Is.EqualTo(100));
        });
    }

    #endregion

    #region GetTrendStrength Tests

    [Test]
    public void GetTrendStrength_ReturnsCorrectDescriptions()
    {
        // Arrange
        var config = AdxTakeProfitConfig.FromRange(100, 200);

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.GetTrendStrength(10), Is.EqualTo("Weak/No Trend"));
            Assert.That(config.GetTrendStrength(14.9), Is.EqualTo("Weak/No Trend"));
            Assert.That(config.GetTrendStrength(15), Is.EqualTo("Developing Trend"));
            Assert.That(config.GetTrendStrength(20), Is.EqualTo("Developing Trend"));
            Assert.That(config.GetTrendStrength(24.9), Is.EqualTo("Developing Trend"));
            Assert.That(config.GetTrendStrength(25), Is.EqualTo("Strong Trend"));
            Assert.That(config.GetTrendStrength(30), Is.EqualTo("Strong Trend"));
            Assert.That(config.GetTrendStrength(34.9), Is.EqualTo("Strong Trend"));
            Assert.That(config.GetTrendStrength(35), Is.EqualTo("Very Strong Trend"));
            Assert.That(config.GetTrendStrength(50), Is.EqualTo("Very Strong Trend"));
        });
    }

    #endregion

    #region ToString Tests

    [Test]
    public void ToString_IncludesAllRelevantInfo()
    {
        // Arrange
        var config = AdxTakeProfitConfig.FromRange(1.30, 1.70);

        // Act
        var result = config.ToString();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("1.30"));
            Assert.That(result, Does.Contain("1.70"));
            Assert.That(result, Does.Contain("15"));
            Assert.That(result, Does.Contain("25"));
            Assert.That(result, Does.Contain("35"));
        });
    }

    #endregion

    #region Fluent API TakeProfit Overload Tests

    [Test]
    public void TakeProfit_SinglePrice_SetsFixedTarget()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("TEST")
            .IsPriceAbove(100)
            .Buy(100, Price.Current)
            .TakeProfit(150)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(150));
            Assert.That(strategy.Order.AdxTakeProfit, Is.Null);
        });
    }

    [Test]
    public void TakeProfit_Range_CreatesAdxConfig()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("TEST")
            .IsPriceAbove(100)
            .Buy(100, Price.Current)
            .TakeProfit(130, 170)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(130)); // Fallback
            Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(130));
            Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(170));
        });
    }

    [Test]
    public void TakeProfit_RangeWithCustomThresholds_SetsAllValues()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("TEST")
            .IsPriceAbove(100)
            .Buy(100, Price.Current)
            .TakeProfit(
                lowTarget: 130,
                highTarget: 170,
                weakThreshold: 20,
                developingThreshold: 30,
                strongThreshold: 40,
                exitOnRollover: false)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(130));
            Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(170));
            Assert.That(strategy.Order.AdxTakeProfit!.WeakTrendThreshold, Is.EqualTo(20));
            Assert.That(strategy.Order.AdxTakeProfit!.DevelopingTrendThreshold, Is.EqualTo(30));
            Assert.That(strategy.Order.AdxTakeProfit!.StrongTrendThreshold, Is.EqualTo(40));
            Assert.That(strategy.Order.AdxTakeProfit!.ExitOnAdxRollover, Is.False);
        });
    }

    [Test]
    public void TakeProfit_FixedAfterRange_ClearsAdxConfig()
    {
        // Arrange & Act - Set range first, then override with fixed price
        var strategy = Stock.Ticker("TEST")
            .IsPriceAbove(100)
            .Buy(100, Price.Current)
            .TakeProfit(130, 170)  // Range
            .TakeProfit(150)       // Fixed - should clear ADX config
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(150));
            Assert.That(strategy.Order.AdxTakeProfit, Is.Null);
        });
    }

    [Test]
    public void TakeProfit_RangeAfterFixed_SetsAdxConfig()
    {
        // Arrange & Act - Set fixed first, then override with range
        var strategy = Stock.Ticker("TEST")
            .IsPriceAbove(100)
            .Buy(100, Price.Current)
            .TakeProfit(150)       // Fixed
            .TakeProfit(130, 170)  // Range - should set ADX config
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(130)); // Fallback
            Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(130));
            Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(170));
        });
    }

    #endregion

    #region Real-World Scenario Tests

    [Test]
    public void VIVS_Strategy_HasCorrectAdxConfig()
    {
        // Arrange & Act - VIVS: Range 4.00-4.80
        var strategy = Stock.Ticker("VIVS")
            .Start(MarketTime.PreMarket.Start)
            .IsPriceAbove(2.40)
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(4.00));
            Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(4.80));

            // Verify target calculation at different ADX levels
            Assert.That(strategy.Order.AdxTakeProfit!.GetTargetForAdx(10), Is.EqualTo(4.00));  // Weak
            Assert.That(strategy.Order.AdxTakeProfit!.GetTargetForAdx(20), Is.EqualTo(4.40).Within(0.01));  // Mid
            Assert.That(strategy.Order.AdxTakeProfit!.GetTargetForAdx(30), Is.EqualTo(4.80));  // Strong
        });
    }

    [Test]
    public void RPGL_Strategy_HasCorrectAdxConfig()
    {
        // Arrange & Act - RPGL: Range 1.30-1.70
        var strategy = Stock.Ticker("RPGL")
            .Start(MarketTime.PreMarket.Start)
            .IsPriceAbove(0.88)
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .TakeProfit(1.30, 1.70)
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(1.30));
            Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(1.70));

            // Midpoint at ADX 20 should be 1.50
            Assert.That(strategy.Order.AdxTakeProfit!.GetTargetForAdx(20), Is.EqualTo(1.50).Within(0.01));
        });
    }

    [Test]
    public void PremarketStrategy_WithAdxTakeProfit_FullConfiguration()
    {
        // Arrange & Act - Full premarket strategy with all options
        var strategy = Stock.Ticker("TEST")
            .Start(MarketTime.PreMarket.Start)
            .IsPriceAbove(5.00)
            .IsAboveVwap()
            .Buy(200, Price.Current)
            .TakeProfit(6.00, 7.50, weakThreshold: 12, developingThreshold: 22, strongThreshold: 32)
            .StopLoss(4.50)
            .ClosePosition(MarketTime.PreMarket.Ending)
            .End(MarketTime.PreMarket.End);

        // Assert
        Assert.Multiple(() =>
        {
            // Basic strategy config
            Assert.That(strategy.Symbol, Is.EqualTo("TEST"));
            Assert.That(strategy.Order.Quantity, Is.EqualTo(200));
            Assert.That(strategy.Order.EnableStopLoss, Is.True);
            Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(4.50));

            // ADX Take Profit config
            Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(6.00));
            Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(7.50));
            Assert.That(strategy.Order.AdxTakeProfit!.WeakTrendThreshold, Is.EqualTo(12));
            Assert.That(strategy.Order.AdxTakeProfit!.DevelopingTrendThreshold, Is.EqualTo(22));
            Assert.That(strategy.Order.AdxTakeProfit!.StrongTrendThreshold, Is.EqualTo(32));

            // Time configuration
            Assert.That(strategy.StartTime, Is.EqualTo(MarketTime.PreMarket.Start));
            Assert.That(strategy.EndTime, Is.EqualTo(MarketTime.PreMarket.End));
            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
        });
    }

    #endregion

    #region Edge Cases

    [Test]
    public void GetTargetForAdx_NegativeAdx_ReturnsConservative()
    {
        // Arrange
        var config = AdxTakeProfitConfig.FromRange(100, 200);

        // Act & Assert - Negative ADX (shouldn't happen, but handle gracefully)
        Assert.That(config.GetTargetForAdx(-5), Is.EqualTo(100));
    }

    [Test]
    public void GetTargetForAdx_VeryHighAdx_ReturnsAggressive()
    {
        // Arrange
        var config = AdxTakeProfitConfig.FromRange(100, 200);

        // Act & Assert - Very high ADX
        Assert.That(config.GetTargetForAdx(100), Is.EqualTo(200));
    }

    [Test]
    public void TakeProfit_SameTargets_WorksCorrectly()
    {
        // Arrange - Edge case: same conservative and aggressive target
        var config = AdxTakeProfitConfig.FromRange(150, 150);

        // Act & Assert - Should always return 150 regardless of ADX
        Assert.Multiple(() =>
        {
            Assert.That(config.GetTargetForAdx(5), Is.EqualTo(150));
            Assert.That(config.GetTargetForAdx(20), Is.EqualTo(150));
            Assert.That(config.GetTargetForAdx(40), Is.EqualTo(150));
        });
    }

    [Test]
    public void TakeProfit_InvertedTargets_StillInterpolates()
    {
        // Arrange - Edge case: conservative > aggressive (unusual but possible)
        var config = AdxTakeProfitConfig.FromRange(200, 100);

        // Act & Assert
        Assert.Multiple(() =>
        {
            // Weak ADX → "conservative" (200)
            Assert.That(config.GetTargetForAdx(10), Is.EqualTo(200));
            // Strong ADX → "aggressive" (100)
            Assert.That(config.GetTargetForAdx(30), Is.EqualTo(100));
            // Developing → interpolates (150 at ADX 20)
            Assert.That(config.GetTargetForAdx(20), Is.EqualTo(150).Within(0.01));
        });
    }

    #endregion
}
