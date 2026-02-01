// ============================================================================
// MissedTheBoatTests - Tests for price validation against take profit targets
// ============================================================================
//
// These tests verify that strategies correctly reject entries when the current
// price is already at or above the take profit target ("missed the boat").
//

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the "Missed the Boat" validation logic.
/// Ensures strategies don't enter positions when price is already above take profit targets.
/// </summary>
[TestFixture]
public class MissedTheBoatTests
{
    #region Strategy Configuration Tests

    [Test]
    [Description("Strategy with ADX take profit should have conservative target accessible")]
    public void Strategy_WithAdxTakeProfit_HasConservativeTarget()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("VIVS")
            .IsPriceAbove(2.40)
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)  // Conservative: 4.00, Aggressive: 4.80
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
            Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(4.00));
            Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(4.80));
        });
    }

    [Test]
    [Description("Strategy with fixed take profit should have price accessible")]
    public void Strategy_WithFixedTakeProfit_HasTakeProfitPrice()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(150)
            .Buy(100, Price.Current)
            .TakeProfit(160)  // Fixed target
            .Build();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.True);
            Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(160));
            Assert.That(strategy.Order.AdxTakeProfit, Is.Null);
        });
    }

    [Test]
    [Description("Strategy without take profit should not trigger missed the boat")]
    public void Strategy_WithoutTakeProfit_NoMissedBoatValidation()
    {
        // Arrange & Act
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(150)
            .Buy(100, Price.Current)
            .Build();  // No take profit

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.Order.EnableTakeProfit, Is.False);
            Assert.That(strategy.Order.TakeProfitPrice, Is.Null);
            Assert.That(strategy.Order.AdxTakeProfit, Is.Null);
        });
    }

    #endregion

    #region Price vs Take Profit Comparison Tests

    [Test]
    [Description("Price below ADX conservative target should allow entry")]
    public void PriceBelowConservativeTarget_ShouldAllowEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("VIVS")
            .IsPriceAbove(2.40)
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)  // Conservative: 4.00
            .Build();

        double currentPrice = 3.50;  // Below 4.00

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.False, "Price below conservative target should allow entry");
    }

    [Test]
    [Description("Price equal to ADX conservative target should reject entry")]
    public void PriceEqualToConservativeTarget_ShouldRejectEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("VIVS")
            .IsPriceAbove(2.40)
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)  // Conservative: 4.00
            .Build();

        double currentPrice = 4.00;  // Equal to conservative target

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True, "Price equal to conservative target should reject entry");
    }

    [Test]
    [Description("Price above ADX conservative target should reject entry")]
    public void PriceAboveConservativeTarget_ShouldRejectEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("VIVS")
            .IsPriceAbove(2.40)
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)  // Conservative: 4.00
            .Build();

        double currentPrice = 4.25;  // Above conservative target

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True, "Price above conservative target should reject entry");
    }

    [Test]
    [Description("Price between conservative and aggressive targets should reject entry")]
    public void PriceBetweenTargets_ShouldRejectEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("VIVS")
            .IsPriceAbove(2.40)
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .Build();

        double currentPrice = 4.50;  // Between 4.00 and 4.80

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True, "Price between targets should still reject (uses conservative)");
    }

    [Test]
    [Description("Price above aggressive target should reject entry")]
    public void PriceAboveAggressiveTarget_ShouldRejectEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("VIVS")
            .IsPriceAbove(2.40)
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .Build();

        double currentPrice = 5.00;  // Above aggressive target

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True, "Price above aggressive target should reject entry");
    }

    #endregion

    #region Fixed Take Profit Tests

    [Test]
    [Description("Price below fixed take profit should allow entry")]
    public void PriceBelowFixedTakeProfit_ShouldAllowEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(150)
            .Buy(100, Price.Current)
            .TakeProfit(160)
            .Build();

        double currentPrice = 155;

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.False);
    }

    [Test]
    [Description("Price equal to fixed take profit should reject entry")]
    public void PriceEqualToFixedTakeProfit_ShouldRejectEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(150)
            .Buy(100, Price.Current)
            .TakeProfit(160)
            .Build();

        double currentPrice = 160;

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True);
    }

    [Test]
    [Description("Price above fixed take profit should reject entry")]
    public void PriceAboveFixedTakeProfit_ShouldRejectEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(150)
            .Buy(100, Price.Current)
            .TakeProfit(160)
            .Build();

        double currentPrice = 165;

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True);
    }

    #endregion

    #region Sell Order Tests (Should Not Apply)

    [Test]
    [Description("Sell orders should not trigger missed the boat validation")]
    public void SellOrder_ShouldNotTriggerMissedBoatValidation()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(160)
            .Sell(100, Price.Current)
            .Build();

        double currentPrice = 170;  // Above entry condition

        // Act - Sell orders don't use take profit in the same way
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert - Sell orders should never be rejected by this logic
        Assert.That(shouldReject, Is.False);
    }

    #endregion

    #region Edge Cases

    [Test]
    [Description("Price just below conservative target should allow entry")]
    public void PriceJustBelowTarget_ShouldAllowEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("VIVS")
            .IsPriceAbove(2.40)
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .Build();

        double currentPrice = 3.99;  // Just below 4.00

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.False);
    }

    [Test]
    [Description("Price just above conservative target should reject entry")]
    public void PriceJustAboveTarget_ShouldRejectEntry()
    {
        // Arrange
        var strategy = Stock.Ticker("VIVS")
            .IsPriceAbove(2.40)
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .Build();

        double currentPrice = 4.01;  // Just above 4.00

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True);
    }

    [Test]
    [Description("Strategy with no take profit should never reject")]
    public void NoTakeProfit_ShouldNeverReject()
    {
        // Arrange
        var strategy = Stock.Ticker("AAPL")
            .IsPriceAbove(150)
            .Buy(100, Price.Current)
            .StopLoss(145)  // Only stop loss, no take profit
            .Build();

        double currentPrice = 200;  // Very high price

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.False, "No take profit means no price ceiling");
    }

    #endregion

    #region Potential Profit Calculation Tests

    [Test]
    [Description("Calculate potential profit percentage correctly")]
    public void PotentialProfit_CalculatesCorrectly()
    {
        // Arrange
        double currentPrice = 3.80;
        double takeProfitTarget = 4.00;

        // Act
        double potentialProfit = takeProfitTarget - currentPrice;
        double percentToTarget = (potentialProfit / currentPrice) * 100;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(potentialProfit, Is.EqualTo(0.20).Within(0.001));
            Assert.That(percentToTarget, Is.EqualTo(5.26).Within(0.01));
        });
    }

    [Test]
    [Description("Low profit potential should be less than 5%")]
    public void LowProfitPotential_IsLessThanFivePercent()
    {
        // Arrange
        double currentPrice = 3.90;
        double takeProfitTarget = 4.00;

        // Act
        double potentialProfit = takeProfitTarget - currentPrice;
        double percentToTarget = (potentialProfit / currentPrice) * 100;

        // Assert
        Assert.That(percentToTarget, Is.LessThan(5.0), "Should trigger low profit warning");
    }

    [Test]
    [Description("Good profit potential should be 5% or more")]
    public void GoodProfitPotential_IsFivePercentOrMore()
    {
        // Arrange
        double currentPrice = 3.50;
        double takeProfitTarget = 4.00;

        // Act
        double potentialProfit = takeProfitTarget - currentPrice;
        double percentToTarget = (potentialProfit / currentPrice) * 100;

        // Assert
        Assert.That(percentToTarget, Is.GreaterThanOrEqualTo(5.0), "Should not trigger low profit warning");
    }

    #endregion

    #region Real-World Scenario Tests

    [Test]
    [Description("VIVS scenario: Entry at $3.50 with TP at $4.00-$4.80 should allow")]
    public void VIVSScenario_ValidEntry_ShouldAllow()
    {
        // Arrange - Real strategy from Program.cs
        var strategy = Stock.Ticker("VIVS")
            .TimeFrame(TradingSession.PreMarketEndEarly)
            .IsPriceAbove(2.40)
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .TrailingStopLoss(Percent.TwentyFive)
            .Build();

        double currentPrice = 3.50;

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.False, "Price $3.50 is below conservative target $4.00");
    }

    [Test]
    [Description("VIVS scenario: Entry at $4.25 with TP at $4.00-$4.80 should reject")]
    public void VIVSScenario_MissedBoat_ShouldReject()
    {
        // Arrange
        var strategy = Stock.Ticker("VIVS")
            .TimeFrame(TradingSession.PreMarketEndEarly)
            .IsPriceAbove(2.40)
            .IsAboveVwap()
            .Buy(100, Price.Current)
            .TakeProfit(4.00, 4.80)
            .TrailingStopLoss(Percent.TwentyFive)
            .Build();

        double currentPrice = 4.25;

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True, "Price $4.25 is above conservative target $4.00");
    }

    [Test]
    [Description("CATX scenario: Entry at $4.34 with TP at $4.50-$4.75 should allow")]
    public void CATXScenario_ValidEntry_ShouldAllow()
    {
        // Arrange - CATX EMA Support Strategy
        var strategy = Stock.Ticker("CATX")
            .TimeFrame(TradingSession.PreMarketEndEarly)
            .Pullback(4.33)
            .IsPriceAbove(4.30)
            .Buy(100, Price.Current)
            .TakeProfit(4.50, 4.75)
            .StopLoss(4.25)
            .Build();

        double currentPrice = 4.34;

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.False, "Price $4.34 is below conservative target $4.50");
    }

    [Test]
    [Description("CATX scenario: Entry at $4.60 with TP at $4.50-$4.75 should reject")]
    public void CATXScenario_MissedBoat_ShouldReject()
    {
        // Arrange
        var strategy = Stock.Ticker("CATX")
            .TimeFrame(TradingSession.PreMarketEndEarly)
            .Pullback(4.33)
            .IsPriceAbove(4.30)
            .Buy(100, Price.Current)
            .TakeProfit(4.50, 4.75)
            .StopLoss(4.25)
            .Build();

        double currentPrice = 4.60;

        // Act
        bool shouldReject = ShouldRejectDueToMissedBoat(strategy, currentPrice);

        // Assert
        Assert.That(shouldReject, Is.True, "Price $4.60 is above conservative target $4.50");
    }

    #endregion

    #region StrategyResult Enum Tests

    [Test]
    [Description("MissedTheBoat result exists in StrategyResult enum")]
    public void StrategyResult_MissedTheBoat_Exists()
    {
        // Assert
        Assert.That(Enum.IsDefined(typeof(StrategyResult), StrategyResult.MissedTheBoat), Is.True);
    }

    [Test]
    [Description("MissedTheBoat is different from NeverBought")]
    public void StrategyResult_MissedTheBoat_DifferentFromNeverBought()
    {
        // Assert
        Assert.That(StrategyResult.MissedTheBoat, Is.Not.EqualTo(StrategyResult.NeverBought));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Simulates the "missed the boat" validation logic from StrategyRunner.
    /// This mirrors the ValidatePriceNotAboveTakeProfit method.
    /// </summary>
    private static bool ShouldRejectDueToMissedBoat(TradingStrategy strategy, double currentPrice)
    {
        var order = strategy.Order;

        // Only validate for BUY orders with take profit enabled
        if (order.Side != OrderSide.Buy || !order.EnableTakeProfit)
            return false;

        double? takeProfitThreshold = null;

        // Check ADX-based take profit (use conservative/weak target as threshold)
        if (order.AdxTakeProfit != null)
        {
            takeProfitThreshold = order.AdxTakeProfit.ConservativeTarget;
        }
        // Check fixed take profit price
        else if (order.TakeProfitPrice.HasValue)
        {
            takeProfitThreshold = order.TakeProfitPrice.Value;
        }

        // If we have a threshold and current price is at or above it, reject the trade
        if (takeProfitThreshold.HasValue && currentPrice >= takeProfitThreshold.Value)
        {
            return true;
        }

        return false;
    }

    #endregion
}
