// ============================================================================
// StrategyValidatorTests - Tests for strategy validation
// ============================================================================

using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Validation;

namespace IdiotProof.Shared.UnitTests.Validation;

/// <summary>
/// Tests for StrategyValidator class.
/// </summary>
[TestFixture]
public class StrategyValidatorTests
{
    #region ValidateName

    [Test]
    public void ValidateName_ValidName_ReturnsValid()
    {
        var result = StrategyValidator.ValidateName("My Strategy");

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateName_NullOrEmpty_ReturnsRequired(string? name)
    {
        var result = StrategyValidator.ValidateName(name);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.Required));
    }

    [Test]
    public void ValidateName_TooLong_ReturnsError()
    {
        var longName = new string('A', StrategyValidator.MaxNameLength + 1);

        var result = StrategyValidator.ValidateName(longName);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InvalidLength));
    }

    [Test]
    public void ValidateName_ExactlyMaxLength_ReturnsValid()
    {
        var maxLengthName = new string('A', StrategyValidator.MaxNameLength);

        var result = StrategyValidator.ValidateName(maxLengthName);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateName_WithXssContent_ReturnsInjectionError()
    {
        var result = StrategyValidator.ValidateName("<script>alert('xss')</script>");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InjectionDetected));
    }

    #endregion

    #region ValidateStrategy

    [Test]
    public void ValidateStrategy_ValidStrategy_ReturnsValid()
    {
        var strategy = CreateValidStrategy();

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.True,
            $"Errors: {string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
    }

    [Test]
    public void ValidateStrategy_NullName_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Name = null!;

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.Required));
    }

    [Test]
    public void ValidateStrategy_InvalidSymbol_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Symbol = "INVALID123";

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidSymbol));
    }

    [Test]
    public void ValidateStrategy_EmptySegments_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Segments.Clear();

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.Required));
    }

    [Test]
    public void ValidateStrategy_TooManySegments_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Segments.Clear();

        for (int i = 0; i < StrategyValidator.MaxSegments + 1; i++)
        {
            var segment = SegmentFactory.CreateBreakout();
            segment.Order = i;
            strategy.Segments.Add(segment);
        }

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidRange));
    }

    [Test]
    public void ValidateStrategy_ExactlyMaxSegments_ReturnsValid()
    {
        var strategy = CreateValidStrategy();
        strategy.Segments.Clear();

        // Add Ticker first (required)
        var ticker = SegmentFactory.CreateTicker();
        ticker.Order = 0;
        ticker.Parameters.First(p => p.Name == "symbol").Value = "AAPL";
        strategy.Segments.Add(ticker);

        // Add conditions to fill up to MaxSegments - 2 (leave room for order)
        for (int i = 1; i < StrategyValidator.MaxSegments - 1; i++)
        {
            var segment = SegmentFactory.CreateBreakout();
            segment.Order = i;
            segment.Parameters.First(p => p.Name == "level").Value = 150.0;
            strategy.Segments.Add(segment);
        }

        // Add Order last (required)
        var buy = SegmentFactory.CreateBuy();
        buy.Order = StrategyValidator.MaxSegments - 1;
        buy.Parameters.First(p => p.Name == "quantity").Value = 1;
        buy.Parameters.First(p => p.Name == "priceType").Value = Price.Current;
        buy.Parameters.First(p => p.Name == "orderType").Value = OrderType.Limit;
        strategy.Segments.Add(buy);

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateStrategy_NotesTooLong_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Notes = new string('A', StrategyValidator.MaxNotesLength + 1);

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidLength));
    }

    [Test]
    public void ValidateStrategy_NotesWithXss_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Notes = "<script>alert('xss')</script>";

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region ValidateSegmentSequence

    [Test]
    public void ValidateSegmentSequence_ValidSequence_ReturnsValid()
    {
        var tickerSegment = SegmentFactory.CreateTicker();
        tickerSegment.Order = 0;
        tickerSegment.Parameters.First(p => p.Name == "symbol").Value = "AAPL";

        var breakoutSegment = SegmentFactory.CreateBreakout();
        breakoutSegment.Order = 1;
        breakoutSegment.Parameters.First(p => p.Name == "level").Value = 150.0;

        var buySegment = SegmentFactory.CreateBuy();
        buySegment.Order = 2;
        buySegment.Parameters.First(p => p.Name == "quantity").Value = 1;
        buySegment.Parameters.First(p => p.Name == "priceType").Value = Price.Current;
        buySegment.Parameters.First(p => p.Name == "orderType").Value = OrderType.Limit;

        var segments = new List<StrategySegment>
        {
            tickerSegment,
            breakoutSegment,
            buySegment
        };

        var result = StrategyValidator.ValidateSegmentSequence(segments);

        Assert.That(result.IsValid, Is.True);
    }

    #endregion

    #region Helper Methods

    private static StrategyDefinition CreateValidStrategy()
    {
        var ticker = SegmentFactory.CreateTicker();
        ticker.Parameters.First(p => p.Name == "symbol").Value = "AAPL";

        var breakout = SegmentFactory.CreateBreakout();
        breakout.Parameters.First(p => p.Name == "level").Value = 150.0;

        var buy = SegmentFactory.CreateBuy();
        buy.Parameters.First(p => p.Name == "quantity").Value = 1;
        buy.Parameters.First(p => p.Name == "priceType").Value = Price.Current;
        buy.Parameters.First(p => p.Name == "orderType").Value = OrderType.Limit;

        return new StrategyDefinition
        {
            Name = "Test Strategy",
            Symbol = "AAPL",
            Enabled = true,
            Segments = [ticker, breakout, buy]
        };
    }

    #endregion

    #region Short Position Price Validation

    [Test]
    public void ValidateShortPositionPrices_ValidPrices_ReturnsValid()
    {
        // Short: sell at $100, buy back at $90 (take profit), stop at $110
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: 90.0,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.True,
            $"Errors: {string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
    }

    [Test]
    public void ValidateShortPositionPrices_EntryBelowTakeProfit_ReturnsError()
    {
        // Invalid: Entry $90 is below take profit $100
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 90.0,
            takeProfitPrice: 100.0,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.ShortEntryBelowTakeProfit));
    }

    [Test]
    public void ValidateShortPositionPrices_EntryEqualToTakeProfit_ReturnsError()
    {
        // Invalid: Entry equals take profit (no profit possible)
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: 100.0,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.ShortEntryBelowTakeProfit));
    }

    [Test]
    public void ValidateShortPositionPrices_StopLossBelowEntry_ReturnsError()
    {
        // Invalid: Stop loss $90 is below entry $100 (would trigger immediately on price drop)
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: 80.0,
            stopLossPrice: 90.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.ShortStopLossBelowEntry));
    }

    [Test]
    public void ValidateShortPositionPrices_StopLossEqualToEntry_ReturnsError()
    {
        // Invalid: Stop loss equals entry
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: 80.0,
            stopLossPrice: 100.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.ShortStopLossBelowEntry));
    }

    [Test]
    public void ValidateShortPositionPrices_TakeProfitAboveStopLoss_ReturnsError()
    {
        // Invalid: Take profit $115 is above stop loss $110
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 120.0,
            takeProfitPrice: 115.0,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.ShortTakeProfitAboveStopLoss));
    }

    [Test]
    public void ValidateShortPositionPrices_TakeProfitEqualToStopLoss_ReturnsError()
    {
        // Invalid: Take profit equals stop loss
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 120.0,
            takeProfitPrice: 110.0,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.ShortTakeProfitAboveStopLoss));
    }

    [Test]
    public void ValidateShortPositionPrices_NullEntryPrice_SkipsEntryValidation()
    {
        // Valid: No entry price, only take profit and stop loss
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: null,
            takeProfitPrice: 90.0,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateShortPositionPrices_OnlyTakeProfitProvided_ReturnsValid()
    {
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: 90.0,
            stopLossPrice: null);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateShortPositionPrices_OnlyStopLossProvided_ReturnsValid()
    {
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: null,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateShortPositionPrices_MultipleErrors_ReturnsAll()
    {
        // Invalid: Entry below take profit AND stop loss below entry
        var result = StrategyValidator.ValidateShortPositionPrices(
            entryPrice: 90.0,
            takeProfitPrice: 100.0,
            stopLossPrice: 80.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Count, Is.GreaterThanOrEqualTo(2));
    }

    #endregion

    #region Long Position Price Validation

    [Test]
    public void ValidateLongPositionPrices_ValidPrices_ReturnsValid()
    {
        // Long: buy at $100, sell at $110 (take profit), stop at $90
        var result = StrategyValidator.ValidateLongPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: 110.0,
            stopLossPrice: 90.0);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateLongPositionPrices_TakeProfitBelowEntry_ReturnsError()
    {
        // Invalid: Take profit $90 is below entry $100
        var result = StrategyValidator.ValidateLongPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: 90.0,
            stopLossPrice: 80.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.TakeProfitBelowEntry));
    }

    [Test]
    public void ValidateLongPositionPrices_StopLossAboveEntry_ReturnsError()
    {
        // Invalid: Stop loss $110 is above entry $100
        var result = StrategyValidator.ValidateLongPositionPrices(
            entryPrice: 100.0,
            takeProfitPrice: 120.0,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.StopLossAboveEntry));
    }

    #endregion

    #region ValidateOrderPrices (Unified)

    [Test]
    public void ValidateOrderPrices_ShortPosition_UsesShortValidation()
    {
        // Invalid short: entry below take profit
        var result = StrategyValidator.ValidateOrderPrices(
            isShortPosition: true,
            entryPrice: 90.0,
            takeProfitPrice: 100.0,
            stopLossPrice: 110.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.ShortEntryBelowTakeProfit));
    }

    [Test]
    public void ValidateOrderPrices_LongPosition_UsesLongValidation()
    {
        // Invalid long: take profit below entry
        var result = StrategyValidator.ValidateOrderPrices(
            isShortPosition: false,
            entryPrice: 100.0,
            takeProfitPrice: 90.0,
            stopLossPrice: 80.0);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(
            e => e.Code == ValidationCodes.TakeProfitBelowEntry));
    }

    #endregion

    #region Repeat Validation

    [Test]
    public void ValidateSegmentSequence_RepeatWithTakeProfit_ReturnsValidNoWarnings()
    {
        var ticker = SegmentFactory.CreateTicker();
        ticker.Order = 0;
        ticker.Parameters.First(p => p.Name == "symbol").Value = "ABC";

        var breakout = SegmentFactory.CreateBreakout();
        breakout.Order = 1;
        breakout.Parameters.First(p => p.Name == "level").Value = 5.0;

        var buy = SegmentFactory.CreateBuy();
        buy.Order = 2;
        buy.Parameters.First(p => p.Name == "quantity").Value = 100;
        buy.Parameters.First(p => p.Name == "priceType").Value = Price.Current;
        buy.Parameters.First(p => p.Name == "orderType").Value = OrderType.Limit;

        var takeProfit = new StrategySegment
        {
            Type = SegmentType.TakeProfit,
            Category = SegmentCategory.RiskManagement,
            DisplayName = "Take Profit",
            Order = 3,
            Parameters = [new SegmentParameter
            {
                Name = "Price",
                Label = "Price",
                Type = ParameterType.Price,
                Value = 6.0,
                IsRequired = true
            }]
        };

        var repeat = new StrategySegment
        {
            Type = SegmentType.Repeat,
            Category = SegmentCategory.Execution,
            DisplayName = "Repeat",
            Order = 4,
            Parameters = []
        };

        var segments = new List<StrategySegment> { ticker, breakout, buy, takeProfit, repeat };
        var result = StrategyValidator.ValidateSegmentSequence(segments);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Warnings, Has.None.Matches<ValidationWarning>(w => w.Code == "REPEAT_WITHOUT_TAKEPROFIT"));
    }

    [Test]
    public void ValidateSegmentSequence_RepeatWithoutTakeProfit_ReturnsWarning()
    {
        var ticker = SegmentFactory.CreateTicker();
        ticker.Order = 0;
        ticker.Parameters.First(p => p.Name == "symbol").Value = "ABC";

        var breakout = SegmentFactory.CreateBreakout();
        breakout.Order = 1;
        breakout.Parameters.First(p => p.Name == "level").Value = 5.0;

        var buy = SegmentFactory.CreateBuy();
        buy.Order = 2;
        buy.Parameters.First(p => p.Name == "quantity").Value = 100;
        buy.Parameters.First(p => p.Name == "priceType").Value = Price.Current;
        buy.Parameters.First(p => p.Name == "orderType").Value = OrderType.Limit;

        var repeat = new StrategySegment
        {
            Type = SegmentType.Repeat,
            Category = SegmentCategory.Execution,
            DisplayName = "Repeat",
            Order = 3,
            Parameters = []
        };

        var segments = new List<StrategySegment> { ticker, breakout, buy, repeat };
        var result = StrategyValidator.ValidateSegmentSequence(segments);

        Assert.That(result.IsValid, Is.True); // Warnings don't fail validation
        Assert.That(result.Warnings, Has.Some.Matches<ValidationWarning>(w => w.Code == "REPEAT_WITHOUT_TAKEPROFIT"));
    }

    [Test]
    public void ValidateStrategy_RepeatEnabledWithTakeProfit_NoRepeatWarning()
    {
        var strategy = CreateValidStrategy();
        strategy.RepeatEnabled = true;
        strategy.Segments.Add(new StrategySegment
        {
            Type = SegmentType.TakeProfit,
            Category = SegmentCategory.RiskManagement,
            DisplayName = "Take Profit",
            Order = 10,
            Parameters = [new SegmentParameter
            {
                Name = "Price",
                Label = "Price",
                Type = ParameterType.Price,
                Value = 160.0,
                IsRequired = true
            }]
        });
        strategy.Segments.Add(new StrategySegment
        {
            Type = SegmentType.Repeat,
            Category = SegmentCategory.Execution,
            DisplayName = "Repeat",
            Order = 11,
            Parameters = []
        });

        var result = StrategyValidator.ValidateStrategy(strategy);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Warnings, Has.None.Matches<ValidationWarning>(w => w.Code == "REPEAT_WITHOUT_TAKEPROFIT"));
    }

    #endregion
}
