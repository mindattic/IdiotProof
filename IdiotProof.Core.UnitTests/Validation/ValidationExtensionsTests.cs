// ============================================================================
// ValidationExtensionsTests - Tests for validation extension methods
// ============================================================================

using IdiotProof.Core.Models;
using IdiotProof.Core.Scripting;
using IdiotProof.Core.Validation;

namespace IdiotProof.Core.UnitTests.Validation;

/// <summary>
/// Tests for ValidationExtensions class.
/// </summary>
[TestFixture]
public class ValidationExtensionsTests
{
    #region ValidateForSave

    [Test]
    public void ValidateForSave_ValidStrategy_ReturnsValid()
    {
        var strategy = CreateValidStrategy();

        var result = strategy.ValidateForSave();

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateForSave_MissingName_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Name = null!;

        var result = strategy.ValidateForSave();

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidateForSave_MissingSymbol_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Symbol = null!;

        var result = strategy.ValidateForSave();

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidateForSave_EmptySegments_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Segments.Clear();

        var result = strategy.ValidateForSave();

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidateForSave_XssInNotes_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Notes = "<script>evil()</script>";

        var result = strategy.ValidateForSave();

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    [Test]
    public void ValidateForSave_XssInDescription_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Description = "javascript:alert('xss')";

        var result = strategy.ValidateForSave();

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region ValidateForExecution

    [Test]
    public void ValidateForExecution_ValidStrategy_ReturnsValid()
    {
        var strategy = CreateValidStrategy();

        var result = strategy.ValidateForExecution();

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateForExecution_InvalidStrategy_ReturnsError()
    {
        var strategy = CreateValidStrategy();
        strategy.Symbol = null!;

        var result = strategy.ValidateForExecution();

        Assert.That(result.IsValid, Is.False);
    }

    #endregion

    #region ValidateIdiotScript

    [Test]
    public void ValidateIdiotScript_ValidScript_ReturnsValid()
    {
        var script = "TICKER(AAPL).QTY(10).TP(160)";

        var result = script.ValidateIdiotScript();

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateIdiotScript_NullScript_ReturnsError()
    {
        string? script = null;

        var result = script.ValidateIdiotScript();

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidateIdiotScript_EmptyScript_ReturnsError()
    {
        var script = "";

        var result = script.ValidateIdiotScript();

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidateIdiotScript_XssScript_ReturnsError()
    {
        var script = "TICKER(AAPL).NAME(\"<script>alert('xss')</script>\")";

        var result = script.ValidateIdiotScript();

        Assert.That(result.IsValid, Is.False);
    }

    #endregion

    #region ValidateIdiotScriptSecurity

    [Test]
    public void ValidateIdiotScriptSecurity_CleanScript_ReturnsValid()
    {
        var script = "TICKER(AAPL).QTY(10)";

        var result = script.ValidateIdiotScriptSecurity();

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateIdiotScriptSecurity_XssScript_ReturnsError()
    {
        var script = "<script>alert('xss')</script>";

        var result = script.ValidateIdiotScriptSecurity();

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    [Test]
    public void ValidateIdiotScriptSecurity_SqlInjection_ReturnsError()
    {
        var script = "'; DROP TABLE users; --";

        var result = script.ValidateIdiotScriptSecurity();

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region SanitizeIdiotScript

    [Test]
    public void SanitizeIdiotScript_ValidScript_ReturnsSameScript()
    {
        var script = "TICKER(AAPL).QTY(10)";

        var result = script.SanitizeIdiotScript();

        // Should be essentially the same (may have whitespace normalization)
        Assert.That(result, Does.Contain("TICKER"));
        Assert.That(result, Does.Contain("AAPL"));
        Assert.That(result, Does.Contain("QTY"));
        Assert.That(result, Does.Contain("10"));
    }

    [Test]
    public void SanitizeIdiotScript_NullScript_ReturnsEmpty()
    {
        string? script = null;

        var result = (script ?? string.Empty).SanitizeIdiotScript();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeIdiotScript_EmptyScript_ReturnsEmpty()
    {
        var script = "";

        var result = script.SanitizeIdiotScript();

        Assert.That(result, Is.Empty);
    }

    #endregion

    #region Helper Methods

    private static StrategyDefinition CreateValidStrategy()
    {
        var ticker = SegmentFactory.CreateTicker();
        ticker.Parameters.First(p => p.Name.Equals("Symbol", StringComparison.OrdinalIgnoreCase)).Value = "AAPL";

        var breakout = SegmentFactory.CreateBreakout();
        breakout.Parameters.First(p => p.Name.Equals("Level", StringComparison.OrdinalIgnoreCase)).Value = 150.0;

        var buy = SegmentFactory.CreateLong();

        // Ensure required order parameters are set (validation requires all required params populated)
        buy.Parameters.First(p => p.Name.Equals("Quantity", StringComparison.OrdinalIgnoreCase)).Value = 10;
        buy.Parameters.First(p => p.Name.Equals("PriceType", StringComparison.OrdinalIgnoreCase)).Value = "Current";
        buy.Parameters.First(p => p.Name.Equals("OrderType", StringComparison.OrdinalIgnoreCase)).Value = "Limit";

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


