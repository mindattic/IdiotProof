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
}
