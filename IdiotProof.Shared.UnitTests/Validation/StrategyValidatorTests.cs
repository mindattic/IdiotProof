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

        Assert.That(result.IsValid, Is.True);
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

        for (int i = 0; i < StrategyValidator.MaxSegments; i++)
        {
            var segment = SegmentFactory.CreateBreakout();
            segment.Order = i;
            strategy.Segments.Add(segment);
        }

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
        var sessionSegment = SegmentFactory.CreateSessionDuration();
        sessionSegment.Order = 0;

        var breakoutSegment = SegmentFactory.CreateBreakout();
        breakoutSegment.Order = 1;

        var tpSegment = SegmentFactory.CreateTakeProfit();
        tpSegment.Order = 2;

        var segments = new List<StrategySegment>
        {
            sessionSegment,
            breakoutSegment,
            tpSegment
        };

        var result = StrategyValidator.ValidateSegmentSequence(segments);

        Assert.That(result.IsValid, Is.True);
    }

    #endregion

    #region Helper Methods

    private static StrategyDefinition CreateValidStrategy()
    {
        return new StrategyDefinition
        {
            Name = "Test Strategy",
            Symbol = "AAPL",
            Enabled = true,
            Segments =
            [
                SegmentFactory.CreateTicker(),
                SegmentFactory.CreateBreakout(),
                SegmentFactory.CreateBuy()
            ]
        };
    }

    #endregion
}
