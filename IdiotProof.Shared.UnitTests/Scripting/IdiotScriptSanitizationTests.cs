// ============================================================================
// IdiotScriptSanitizationTests - Tests for input sanitization
// ============================================================================
//
// Tests the sanitization functionality of IdiotScriptParser.
// Validates that common typos and malformed input are corrected.
//
// ============================================================================

using IdiotProof.Shared.Scripting;

namespace IdiotProof.Shared.UnitTests.Scripting;

/// <summary>
/// Tests for input sanitization in IdiotScriptParser.
/// </summary>
[TestFixture]
public class IdiotScriptSanitizationTests
{
    #region Double Character Consolidation

    [TestCase("TICKER((AAPL))", "AAPL")]
    [TestCase("TICKER(AAPL))", "AAPL")]
    [TestCase("SYM((NVDA))", "NVDA")]
    public void Sanitize_DoubleParentheses_Consolidated(string script, string expectedSymbol)
    {
        var result = IdiotScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo(expectedSymbol));
    }

    [TestCase("TICKER(AAPL).TP($$158)", 158.0)]
    [TestCase("TICKER(AAPL).TP($$$158)", 158.0)]
    [TestCase("TICKER(AAPL).TP($$$$158)", 158.0)]
    public void Sanitize_DoubleDollars_Consolidated(string script, double expectedTp)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TakeProfit, Is.EqualTo(expectedTp).Within(0.01));
    }

    [TestCase("TICKER(AAPL).TSL(15%%)", 0.15)]
    [TestCase("TICKER(AAPL).TSL(15%%%)", 0.15)]
    public void Sanitize_DoublePercent_Consolidated(string script, double expectedTsl)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expectedTsl).Within(0.01));
    }

    #endregion

    #region Colon Before Parenthesis

    [TestCase("TICKER(AAPL).TP:(158)", 158.0)]
    [TestCase("TICKER(AAPL).TP: (158)", 158.0)]
    [TestCase("TICKER(AAPL).TP:  (158)", 158.0)]
    public void Sanitize_ColonBeforeParen_Removed(string script, double expectedTp)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TakeProfit, Is.EqualTo(expectedTp).Within(0.01));
    }

    [Test]
    public void Sanitize_MultipleColonsBeforeParen_AllRemoved()
    {
        var result = IdiotScriptParser.Parse("TICKER:(PLTR).QTY:(10).TP:($158)");
        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(158.0).Within(0.01));
    }

    #endregion

    #region Whitespace Handling

    [TestCase("  TICKER(AAPL)  ")]
    [TestCase("TICKER(AAPL)")]
    [TestCase("   TICKER(AAPL)")]
    [TestCase("TICKER(AAPL)   ")]
    public void Sanitize_LeadingTrailingWhitespace_Trimmed(string script)
    {
        var result = IdiotScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [TestCase("TICKER( AAPL )")]
    [TestCase("TICKER(  AAPL  )")]
    public void Sanitize_WhitespaceInParens_Trimmed(string script)
    {
        var result = IdiotScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Sanitize_WhitespaceBetweenCommands_Handled()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL) . QTY(10) . TP(160)");
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(160.0).Within(0.01));
    }

    #endregion

    #region Comma Preservation

    [Test]
    public void Sanitize_CommasPreservedInsideParens()
    {
        // EMABETWEEN(9, 21) should keep the comma
        var result = IdiotScriptParser.Parse("TICKER(AAPL).EMABETWEEN(9, 21)");
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));

        var emaSegment = result.Segments.FirstOrDefault(s =>
            s.Type == IdiotProof.Shared.Enums.SegmentType.IsEmaBetween);
        Assert.That(emaSegment, Is.Not.Null);
    }

    [Test]
    public void Sanitize_MultipleCommas_Consolidated()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).EMABETWEEN(9,, 21)");
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region IS. Constant Preservation

    [Test]
    public void Sanitize_ISConstantsPreserved_InSession()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).SESSION(IS.PREMARKET)");
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));

        var sessionSegment = result.Segments.FirstOrDefault(s =>
            s.Type == IdiotProof.Shared.Enums.SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
    }

    [Test]
    public void Sanitize_ISConstantsPreserved_InTsl()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).TSL(IS.MODERATE)");
        var stats = result.GetStats();
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.10).Within(0.01));
    }

    [Test]
    public void Sanitize_ISConstantsPreserved_InClosePosition()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).CLOSEPOSITION(IS.BELL)");

        var closeSegment = result.Segments.FirstOrDefault(s =>
            s.Type == IdiotProof.Shared.Enums.SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);
    }

    #endregion

    #region Complex Sanitization

    [Test]
    public void Sanitize_MultipleIssues_AllFixed()
    {
        // Script with multiple sanitization issues
        var result = IdiotScriptParser.Parse("  TICKER:(AAPL)).QTY:(10).TP:($$158).TSL:(15%%)  ");

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(158.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));
    }

    #endregion
}


