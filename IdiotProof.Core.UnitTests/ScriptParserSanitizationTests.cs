// ============================================================================
// StrategyScriptParser Sanitization Tests - Input Cleaning
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for input sanitization: double characters, colons, commas, whitespace
/// Uses IdiotScript syntax with period delimiters
/// </summary>
[TestFixture]
public class ScriptParserSanitizationTests
{
    #region Double Character Consolidation

    [TestCase("SYM((AAPL))", "AAPL")]
    [TestCase("SYM(AAPL))", "AAPL")]
    public void Sanitize_DoubleParentheses_Consolidated(string script, string expectedSymbol)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo(expectedSymbol));
    }

    [TestCase("SYM(AAPL)..QTY(10)", 10)]
    [TestCase("SYM(AAPL)...QTY(10)", 10)]
    public void Sanitize_DoublePeriods_Consolidated(string script, int expectedQty)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(expectedQty));
    }

    [TestCase("SYM(AAPL).TP($$158)", 158.0)]
    [TestCase("SYM(AAPL).TP($$$158)", 158.0)]
    public void Sanitize_DoubleDollars_Consolidated(string script, double expectedTp)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TakeProfit, Is.EqualTo(expectedTp).Within(0.01));
    }

    [TestCase("SYM(AAPL).TSL(15%%)", 0.15)]
    [TestCase("SYM(AAPL).TSL(15%%%)", 0.15)]
    public void Sanitize_DoublePercent_Consolidated(string script, double expectedTsl)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expectedTsl).Within(0.01));
    }

    #endregion

    #region Colon Before Parenthesis

    [TestCase("SYM(AAPL).TP:(158)", 158.0)]
    [TestCase("SYM(AAPL).TP: (158)", 158.0)]
    [TestCase("SYM(AAPL).TP:  (158)", 158.0)]
    public void Sanitize_ColonBeforeParen_Removed(string script, double expectedTp)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TakeProfit, Is.EqualTo(expectedTp).Within(0.01));
    }

    [Test]
    public void Sanitize_MultipleColonsBeforeParen_AllRemoved()
    {
        var result = StrategyScriptParser.Parse("SYM:(PLTR).QTY:(10).TP:($158)");
        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(158.0).Within(0.01));
    }

    #endregion

    #region Comma Preservation

    [Test]
    public void Sanitize_CommasPreservedInsideParens()
    {
        // BETWEEN_EMA(9, 21) should keep the comma
        var result = StrategyScriptParser.Parse("SYM(AAPL).BETWEEN_EMA(9, 21)");
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));

        var emaSegment = result.Segments.FirstOrDefault(s => 
            s.Type == IdiotProof.Core.Enums.SegmentType.IsEmaBetween);
        Assert.That(emaSegment, Is.Not.Null);
    }

    #endregion

    #region Whitespace Handling

    [TestCase("  SYM(AAPL)  ")]
    [TestCase("SYM(AAPL)")]
    [TestCase("   SYM(AAPL)")]
    [TestCase("SYM(AAPL)   ")]
    public void Sanitize_LeadingTrailingWhitespace_Trimmed(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [TestCase("SYM(AAPL).   QTY(10)")]
    [TestCase("SYM(AAPL)  .  QTY(10)")]
    [TestCase("SYM(AAPL).QTY(10)")]
    public void Sanitize_WhitespaceBetweenCommands_Handled(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
    }

    [Test]
    public void Sanitize_WhitespaceInsideParens_Handled()
    {
        // Whitespace inside parentheses IS now handled by the sanitizer
        var result = StrategyScriptParser.Parse("SYM( AAPL )");
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region Complex Sanitization Scenarios

    [Test]
    public void Sanitize_MultipleIssues_AllFixed()
    {
        // Multiple issues: double periods, colon before paren
        var result = StrategyScriptParser.Parse(
            "SYM(PLTR)..QTY:(10).TP:($158)..TSL(15%%)");

        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(158.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));
    }

    [Test]
    public void Sanitize_RealWorldTypos()
    {
        // Common user mistakes (double periods, colons before parens, double percent)
        var result = StrategyScriptParser.Parse(
            "SYM(NVDA)..QTY(5).ENTRY:(150.00).TP:($160).TSL:(10%%)");

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(5));
        Assert.That(stats.Price, Is.EqualTo(150.0).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(160.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.10).Within(0.01));
    }

    #endregion
}


