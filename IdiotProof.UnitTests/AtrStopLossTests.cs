// ============================================================================
// ATR Stop Loss Unit Tests
// ============================================================================
//
// Tests for ATR-based trailing stop loss functionality including:
// - AtrStopLossConfig presets (Tight, Balanced, Loose, VeryLoose)
// - Custom ATR multipliers
// - AtrCalculator price updates and ATR calculation
// - Stop price calculation with min/max bounds
// - Fluent API integration
//
// ============================================================================

using IdiotProof.Enums;
using IdiotProof.Helpers;
using IdiotProof.Models;
using NUnit.Framework;

namespace IdiotProof.UnitTests;

[TestFixture]
public class AtrStopLossTests
{
    // ========================================================================
    // ATR Preset Configuration Tests
    // ========================================================================

    [Test]
    public void Atr_Tight_HasCorrectMultiplier()
    {
        var config = Atr.Tight;

        Assert.That(config.Multiplier, Is.EqualTo(1.5));
        Assert.That(config.Period, Is.EqualTo(14));
        Assert.That(config.IsTrailing, Is.True);
    }

    [Test]
    public void Atr_Balanced_HasCorrectMultiplier()
    {
        var config = Atr.Balanced;

        Assert.That(config.Multiplier, Is.EqualTo(2.0));
        Assert.That(config.Period, Is.EqualTo(14));
        Assert.That(config.IsTrailing, Is.True);
    }

    [Test]
    public void Atr_Loose_HasCorrectMultiplier()
    {
        var config = Atr.Loose;

        Assert.That(config.Multiplier, Is.EqualTo(3.0));
        Assert.That(config.Period, Is.EqualTo(14));
        Assert.That(config.IsTrailing, Is.True);
    }

    [Test]
    public void Atr_VeryLoose_HasCorrectMultiplier()
    {
        var config = Atr.VeryLoose;

        Assert.That(config.Multiplier, Is.EqualTo(4.0));
        Assert.That(config.Period, Is.EqualTo(14));
        Assert.That(config.IsTrailing, Is.True);
    }

    // ========================================================================
    // Custom ATR Multiplier Tests
    // ========================================================================

    [Test]
    public void Atr_Multiplier_CreatesCustomConfig()
    {
        var config = Atr.Multiplier(2.5);

        Assert.That(config.Multiplier, Is.EqualTo(2.5));
        Assert.That(config.Period, Is.EqualTo(14)); // Default period
        Assert.That(config.IsTrailing, Is.True);
    }

    [Test]
    public void Atr_Multiplier_WithCustomPeriod()
    {
        var config = Atr.Multiplier(2.0, period: 20);

        Assert.That(config.Multiplier, Is.EqualTo(2.0));
        Assert.That(config.Period, Is.EqualTo(20));
        Assert.That(config.IsTrailing, Is.True);
    }

    [Test]
    public void Atr_Multiplier_WithTrailingFalse()
    {
        var config = Atr.Multiplier(2.0, isTrailing: false);

        Assert.That(config.Multiplier, Is.EqualTo(2.0));
        Assert.That(config.IsTrailing, Is.False);
    }

    [Test]
    public void Atr_Multiplier_ThrowsOnZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Atr.Multiplier(0));
    }

    [Test]
    public void Atr_Multiplier_ThrowsOnNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Atr.Multiplier(-1.5));
    }

    [Test]
    public void Atr_Multiplier_ThrowsOnInvalidPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Atr.Multiplier(2.0, period: 0));
    }

    // ========================================================================
    // ATR WithBounds Tests
    // ========================================================================

    [Test]
    public void Atr_WithBounds_SetsMinMaxCorrectly()
    {
        var config = Atr.WithBounds(
            multiplier: 2.0,
            minStopPercent: 0.02,
            maxStopPercent: 0.20
        );

        Assert.That(config.Multiplier, Is.EqualTo(2.0));
        Assert.That(config.MinStopPercent, Is.EqualTo(0.02));
        Assert.That(config.MaxStopPercent, Is.EqualTo(0.20));
    }

    [Test]
    public void Atr_WithBounds_CustomPeriodAndTrailing()
    {
        var config = Atr.WithBounds(
            multiplier: 2.5,
            minStopPercent: 0.03,
            maxStopPercent: 0.15,
            period: 10,
            isTrailing: false
        );

        Assert.That(config.Multiplier, Is.EqualTo(2.5));
        Assert.That(config.Period, Is.EqualTo(10));
        Assert.That(config.IsTrailing, Is.False);
        Assert.That(config.MinStopPercent, Is.EqualTo(0.03));
        Assert.That(config.MaxStopPercent, Is.EqualTo(0.15));
    }

    // ========================================================================
    // AtrStopLossConfig Description Tests
    // ========================================================================

    [Test]
    public void AtrStopLossConfig_Description_FormatsCorrectly()
    {
        var config = Atr.Balanced;
        Assert.That(config.Description, Is.EqualTo("2.0× ATR (14 periods) trailing"));

        var fixedConfig = Atr.Multiplier(1.5, isTrailing: false);
        Assert.That(fixedConfig.Description, Is.EqualTo("1.5× ATR (14 periods) fixed"));
    }

    // ========================================================================
    // AtrCalculator Basic Tests
    // ========================================================================

    [Test]
    public void AtrCalculator_Constructor_DefaultValues()
    {
        var calc = new AtrCalculator();

        Assert.That(calc.CurrentAtr, Is.EqualTo(0));
        Assert.That(calc.IsReady, Is.False);
    }

    [Test]
    public void AtrCalculator_Constructor_CustomPeriod()
    {
        var calc = new AtrCalculator(period: 20, ticksPerBar: 100);

        Assert.That(calc.CurrentAtr, Is.EqualTo(0));
        Assert.That(calc.IsReady, Is.False);
    }

    [Test]
    public void AtrCalculator_Constructor_ThrowsOnInvalidPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtrCalculator(period: 0));
    }

    [Test]
    public void AtrCalculator_Constructor_ThrowsOnInvalidTicksPerBar()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtrCalculator(ticksPerBar: 0));
    }

    // ========================================================================
    // AtrCalculator Update Tests
    // ========================================================================

    [Test]
    public void AtrCalculator_Update_IgnoresInvalidPrice()
    {
        var calc = new AtrCalculator();

        calc.Update(0);
        calc.Update(-10);

        Assert.That(calc.CurrentAtr, Is.EqualTo(0));
        Assert.That(calc.IsReady, Is.False);
    }

    [Test]
    public void AtrCalculator_Update_InitializesOnFirstTick()
    {
        var calc = new AtrCalculator();

        var result = calc.Update(10.00);

        Assert.That(result, Is.EqualTo(0)); // Not enough data yet
        Assert.That(calc.IsReady, Is.False);
    }

    [Test]
    public void AtrCalculator_Update_CalculatesAtrAfterEnoughData()
    {
        // Use small ticksPerBar for faster testing
        var calc = new AtrCalculator(period: 2, ticksPerBar: 5);

        // Simulate price movement with volatility
        double[] prices = [10.00, 10.10, 10.05, 10.15, 10.08, // Bar 1
                          10.20, 10.25, 10.18, 10.30, 10.22, // Bar 2
                          10.35, 10.40, 10.32, 10.45, 10.38]; // Bar 3

        foreach (var price in prices)
        {
            calc.Update(price);
        }

        Assert.That(calc.CurrentAtr, Is.GreaterThan(0), "ATR should be calculated after enough bars");
    }

    [Test]
    public void AtrCalculator_Update_TracksHighLow()
    {
        var calc = new AtrCalculator(period: 2, ticksPerBar: 3);

        // First bar: High=10.20, Low=10.00
        calc.Update(10.00);
        calc.Update(10.20);
        calc.Update(10.10);

        // Second bar: High=10.30, Low=10.05
        calc.Update(10.15);
        calc.Update(10.30);
        calc.Update(10.05);

        // ATR should reflect the range
        Assert.That(calc.CurrentAtr, Is.GreaterThan(0));
    }

    // ========================================================================
    // AtrCalculator Stop Price Calculation Tests
    // ========================================================================

    [Test]
    public void AtrCalculator_CalculateStopPrice_FallbackWhenNotReady()
    {
        var calc = new AtrCalculator();

        // Not enough data, should use 10% fallback
        var stopPrice = calc.CalculateStopPrice(
            referencePrice: 100.00,
            multiplier: 2.0,
            isLong: true
        );

        Assert.That(stopPrice, Is.EqualTo(90.00)); // 10% fallback
    }

    [Test]
    public void AtrCalculator_CalculateStopPrice_LongPosition()
    {
        var calc = CreateReadyCalculator(atrValue: 1.20);

        var stopPrice = calc.CalculateStopPrice(
            referencePrice: 50.00,
            multiplier: 2.0,
            isLong: true
        );

        // Stop should be below reference price for long positions
        Assert.That(stopPrice, Is.LessThan(50.00));
    }

    [Test]
    public void AtrCalculator_CalculateStopPrice_ShortPosition()
    {
        var calc = CreateReadyCalculator(atrValue: 1.20);

        var stopPrice = calc.CalculateStopPrice(
            referencePrice: 50.00,
            multiplier: 2.0,
            isLong: false
        );

        // Stop should be above reference price for short positions
        Assert.That(stopPrice, Is.GreaterThan(50.00));
    }

    [Test]
    public void AtrCalculator_CalculateStopPrice_RespectsMinBound()
    {
        // ATR is very small, min bound should kick in
        var calc = CreateReadyCalculator(atrValue: 0.05);

        var stopPrice = calc.CalculateStopPrice(
            referencePrice: 100.00,
            multiplier: 2.0,
            isLong: true,
            minPercent: 0.02 // 2% minimum
        );

        // Min stop = 100 × 0.02 = 2.00 (2%)
        // Should be at least 2% below: 100.00 - 2.00 = 98.00 or less
        Assert.That(stopPrice, Is.LessThanOrEqualTo(98.00));
    }

    [Test]
    public void AtrCalculator_CalculateStopPrice_RespectsMaxBound()
    {
        // ATR is very large, max bound should kick in
        var calc = CreateReadyCalculator(atrValue: 20.00);

        var stopPrice = calc.CalculateStopPrice(
            referencePrice: 100.00,
            multiplier: 2.0,
            isLong: true,
            maxPercent: 0.10 // 10% maximum
        );

        // Max stop = 100 × 0.10 = 10.00 (10%)
        // Should not be more than 10% below: stop >= 90.00
        Assert.That(stopPrice, Is.GreaterThanOrEqualTo(90.00));
    }

    [Test]
    public void AtrCalculator_GetAtrPercent_ReturnsZeroWhenNotReady()
    {
        var calc = new AtrCalculator();

        var percent = calc.GetAtrPercent(50.00);

        Assert.That(percent, Is.EqualTo(0));
    }

    [Test]
    public void AtrCalculator_GetAtrPercent_ReturnsZeroForZeroPrice()
    {
        var calc = CreateReadyCalculator(atrValue: 2.50);

        var percent = calc.GetAtrPercent(0);

        Assert.That(percent, Is.EqualTo(0));
    }

    [Test]
    public void AtrCalculator_Reset_ClearsState()
    {
        var calc = CreateReadyCalculator(atrValue: 1.50);
        Assert.That(calc.CurrentAtr, Is.GreaterThan(0));

        calc.Reset();

        Assert.That(calc.CurrentAtr, Is.EqualTo(0));
        Assert.That(calc.IsReady, Is.False);
    }

    // ========================================================================
    // Fluent API Integration Tests
    // ========================================================================

    [Test]
    public void FluentApi_TrailingStopLoss_AtrTight()
    {
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Atr.Tight)
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
        Assert.That(strategy.Order.AtrStopLoss, Is.Not.Null);
        Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(1.5));
    }

    [Test]
    public void FluentApi_TrailingStopLoss_AtrBalanced()
    {
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Atr.Balanced)
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
        Assert.That(strategy.Order.AtrStopLoss, Is.Not.Null);
        Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(2.0));
    }

    [Test]
    public void FluentApi_TrailingStopLoss_AtrLoose()
    {
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Atr.Loose)
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
        Assert.That(strategy.Order.AtrStopLoss, Is.Not.Null);
        Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(3.0));
    }

    [Test]
    public void FluentApi_TrailingStopLoss_AtrVeryLoose()
    {
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Atr.VeryLoose)
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
        Assert.That(strategy.Order.AtrStopLoss, Is.Not.Null);
        Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(4.0));
    }

    [Test]
    public void FluentApi_TrailingStopLoss_CustomMultiplier()
    {
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Atr.Multiplier(2.5))
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
        Assert.That(strategy.Order.AtrStopLoss, Is.Not.Null);
        Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(2.5));
    }

    [Test]
    public void FluentApi_TrailingStopLoss_WithBounds()
    {
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Atr.WithBounds(2.0, 0.02, 0.15))
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
        Assert.That(strategy.Order.AtrStopLoss, Is.Not.Null);
        Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(2.0));
        Assert.That(strategy.Order.AtrStopLoss!.MinStopPercent, Is.EqualTo(0.02));
        Assert.That(strategy.Order.AtrStopLoss!.MaxStopPercent, Is.EqualTo(0.15));
    }

    [Test]
    public void FluentApi_TrailingStopLoss_PercentClearsAtr()
    {
        // Start with ATR, then switch to percent
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Atr.Balanced)
            .TrailingStopLoss(Percent.Ten) // Override with percent
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.False);
        Assert.That(strategy.Order.AtrStopLoss, Is.Null);
        Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
    }

    [Test]
    public void FluentApi_TrailingStopLoss_AtrSetsEnableFlag()
    {
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Atr.Balanced)
            .Build();

        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
    }

    // ========================================================================
    // OrderAction UseAtrStopLoss Property Tests
    // ========================================================================

    [Test]
    public void OrderAction_UseAtrStopLoss_TrueWhenConfigured()
    {
        var order = new OrderAction
        {
            EnableTrailingStopLoss = true,
            AtrStopLoss = Atr.Balanced
        };

        Assert.That(order.UseAtrStopLoss, Is.True);
    }

    [Test]
    public void OrderAction_UseAtrStopLoss_FalseWhenNull()
    {
        var order = new OrderAction
        {
            EnableTrailingStopLoss = true,
            AtrStopLoss = null
        };

        Assert.That(order.UseAtrStopLoss, Is.False);
    }

    [Test]
    public void OrderAction_UseAtrStopLoss_FalseByDefault()
    {
        var order = new OrderAction();

        Assert.That(order.UseAtrStopLoss, Is.False);
    }

    // ========================================================================
    // ATR Multiplier Edge Cases
    // ========================================================================

    [Test]
    public void Atr_Multiplier_VerySmall()
    {
        var config = Atr.Multiplier(0.5);

        Assert.That(config.Multiplier, Is.EqualTo(0.5));
    }

    [Test]
    public void Atr_Multiplier_VeryLarge()
    {
        var config = Atr.Multiplier(10.0);

        Assert.That(config.Multiplier, Is.EqualTo(10.0));
    }

    [Test]
    public void Atr_Multiplier_FractionalValue()
    {
        var config = Atr.Multiplier(1.75);

        Assert.That(config.Multiplier, Is.EqualTo(1.75));
    }

    // ========================================================================
    // ATR Period Variations Tests
    // ========================================================================

    [Test]
    public void Atr_Multiplier_Period7()
    {
        var config = Atr.Multiplier(2.0, period: 7);

        Assert.That(config.Period, Is.EqualTo(7));
    }

    [Test]
    public void Atr_Multiplier_Period21()
    {
        var config = Atr.Multiplier(2.0, period: 21);

        Assert.That(config.Period, Is.EqualTo(21));
    }

    // ========================================================================
    // Combined Scenarios
    // ========================================================================

    [Test]
    public void FluentApi_CompleteStrategy_WithAtrStop()
    {
        var strategy = Stock.Ticker("AAPL")
            .SessionDuration(TradingSession.PreMarketEndEarly)
            .PriceAbove(150.00)
            .AboveVwap()
            .Buy(quantity: 100, Price.Current)
            .TakeProfit(160.00, 175.00)
            .TrailingStopLoss(Atr.Balanced)
            .ClosePosition(MarketTime.PreMarket.Ending, false)
            .Build();

        Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
        Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
        Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
        Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(2.0));
        Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
    }

    [Test]
    public void FluentApi_CanChainMultipleTrailingStopCalls()
    {
        // Last one wins
        var strategy = Stock.Ticker("TEST")
            .PriceAbove(10.00)
            .Buy(quantity: 100, Price.Current)
            .TrailingStopLoss(Percent.Five)
            .TrailingStopLoss(Atr.Tight)
            .TrailingStopLoss(Atr.Loose)
            .Build();

        Assert.That(strategy.Order.UseAtrStopLoss, Is.True);
        Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(3.0)); // Loose = 3.0
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    /// <summary>
    /// Creates an AtrCalculator that is ready with a specific ATR value.
    /// Uses simulation to generate enough data for the calculator to be ready.
    /// </summary>
    private static AtrCalculator CreateReadyCalculator(double atrValue)
    {
        // Create calculator with small periods for fast initialization
        var calc = new AtrCalculator(period: 2, ticksPerBar: 2);

        // Calculate what prices we need to generate the target ATR
        double basePrice = 100.0;
        double range = atrValue;

        // Generate enough data to make calculator ready
        // Bar 1
        calc.Update(basePrice);
        calc.Update(basePrice + range);

        // Bar 2
        calc.Update(basePrice + range / 2);
        calc.Update(basePrice + range * 1.5);

        // Bar 3
        calc.Update(basePrice + range);
        calc.Update(basePrice);

        // The ATR won't be exactly our target, but it will be ready
        return calc;
    }
}
