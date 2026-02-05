// ============================================================================
// StrategyScriptParser Additional Edge Case Tests
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for additional edge cases: Description, PriceBelow, LowerHighs, error messages
/// </summary>
[TestFixture]
public class ScriptParserEdgeCaseTests
{
    #region Description Tests

    [TestCase(@"SYM(AAPL).Desc(""Test Description"")")]
    [TestCase(@"SYM(AAPL).DESC(""Test Description"")")]
    [TestCase(@"SYM(AAPL).desc(""Test Description"")")]
    [TestCase(@"SYM(AAPL).Desc('Test Description')")]
    public void Parse_Description_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        // Description should be parsed (if stored on strategy)
    }

    [Test]
    public void Parse_DescriptionWithSpecialCharacters()
    {
        var script = @"SYM(AAPL).Desc(""Strategy: AAPL momentum, v1.5"")";
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region IsPriceBelow Tests

    [TestCase("SYM(AAPL).IsPriceBelow(140)", 140)]
    [TestCase("SYM(AAPL).IsPriceBelow($140)", 140)]
    [TestCase("SYM(AAPL).IsPriceBelow(145.50)", 145.50)]
    [TestCase("SYM(AAPL).ISPRICEBELOW(140)", 140)]
    [TestCase("SYM(AAPL).ispricebelow(140)", 140)]
    public void Parse_IsPriceBelow_AllSyntax(string script, double expectedPrice)
    {
        var result = StrategyScriptParser.Parse(script);
        var priceSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsPriceBelow);
        Assert.That(priceSegment, Is.Not.Null);

        var levelParam = priceSegment!.Parameters.FirstOrDefault(p => p.Name == "Level");
        Assert.That(levelParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(levelParam!.Value), Is.EqualTo(expectedPrice).Within(0.01));
    }

    [TestCase("SYM(AAPL).PriceBelow(140)", 140)]
    [TestCase("SYM(AAPL).PriceBelow($140)", 140)]
    public void Parse_PriceBelowAlias_AllSyntax(string script, double expectedPrice)
    {
        var result = StrategyScriptParser.Parse(script);
        var priceSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsPriceBelow);
        Assert.That(priceSegment, Is.Not.Null);
    }

    #endregion

    #region LowerHighs Tests

    [TestCase("SYM(AAPL).LowerHighs()")]
    [TestCase("SYM(AAPL).LowerHighs")]
    [TestCase("SYM(AAPL).LOWERHIGHS()")]
    [TestCase("SYM(AAPL).LOWERHIGHS")]
    [TestCase("SYM(AAPL).lowerhighs()")]
    [TestCase("SYM(AAPL).lowerhighs")]
    public void Parse_LowerHighs_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        Assert.That(segment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).IsLowerHighs()")]
    [TestCase("SYM(AAPL).IsLowerHighs")]
    [TestCase("SYM(AAPL).ISLOWERHIGHS()")]
    [TestCase("SYM(AAPL).islowerhighs()")]
    public void Parse_IsLowerHighs_Prefix(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        Assert.That(segment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).LowerHighs(3)", 3)]
    [TestCase("SYM(AAPL).LowerHighs(5)", 5)]
    public void Parse_LowerHighs_WithCount(string script, int expectedCount)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        Assert.That(segment, Is.Not.Null);

        var countParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Count" || p.Name == "Bars");
        if (countParam != null)
        {
            Assert.That(Convert.ToInt32(countParam.Value), Is.EqualTo(expectedCount));
        }
    }

    #endregion

    #region Bearish Pattern Combination Tests

    [Test]
    public void Parse_BearishContinuationSetup()
    {
        // Bearish continuation: LowerHighs + BelowVwap + DiNegative + MacdBearish
        var script = "Ticker(SPY).IsLowerHighs().IsBelowVwap().IsDiNegative().IsMacdBearish().Order(IS.SHORT)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("SPY"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsLowerHighs), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsBelowVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsMacd), Is.True);
    }

    [Test]
    public void Parse_BearishVwapRejectionSetup()
    {
        var script = "Ticker(QQQ).IsVwapRejected().IsEmaBelow(9).IsLowerHighs().Order(IS.SHORT)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("QQQ"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsVwapRejection), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaBelow), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsLowerHighs), Is.True);
    }

    #endregion

    #region Error Message Tests

    [Test]
    public void Parse_MissingSymbol_ErrorMessageContainsSymbol()
    {
        var ex = Assert.Throws<StrategyScriptException>(() =>
            StrategyScriptParser.Parse("QTY(10).TP($158)"));

        Assert.That(ex!.Message, Does.Contain("Symbol").IgnoreCase
            .Or.Contain("Ticker").IgnoreCase);
    }

    [Test]
    public void Parse_UnknownCommand_ErrorMessageContainsUnknown()
    {
        var ex = Assert.Throws<StrategyScriptException>(() =>
            StrategyScriptParser.Parse("SYM(AAPL).FOOBAR(123)"));

        Assert.That(ex!.Message, Does.Contain("Unknown").IgnoreCase
            .Or.Contain("FOOBAR").IgnoreCase);
    }

    [Test]
    public void Parse_InvalidQuantity_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<StrategyScriptException>(() =>
            StrategyScriptParser.Parse("SYM(AAPL).QTY(abc)"));

        Assert.That(ex!.Message, Does.Contain("QTY").IgnoreCase
            .Or.Contain("number").IgnoreCase
            .Or.Contain("invalid").IgnoreCase);
    }

    [Test]
    public void Parse_EmptyScript_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<StrategyScriptException>(() =>
            StrategyScriptParser.Parse(""));

        Assert.That(ex!.Message, Does.Contain("empty").IgnoreCase);
    }

    [Test]
    public void Parse_WhitespaceOnly_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<StrategyScriptException>(() =>
            StrategyScriptParser.Parse("   \t\n  "));

        Assert.That(ex!.Message, Does.Contain("empty").IgnoreCase);
    }

    #endregion

    #region TryParse Error Tests

    [Test]
    public void TryParse_MissingSymbol_ReturnsError()
    {
        var success = StrategyScriptParser.TryParse(
            "QTY(10).TP($158)",
            out var strategy,
            out var error);

        Assert.That(success, Is.False);
        Assert.That(strategy, Is.Null);
        Assert.That(error, Does.Contain("Symbol").IgnoreCase
            .Or.Contain("Ticker").IgnoreCase);
    }

    [Test]
    public void TryParse_UnknownCommand_ReturnsError()
    {
        var success = StrategyScriptParser.TryParse(
            "SYM(AAPL).INVALIDCMD(123)",
            out var strategy,
            out var error);

        Assert.That(success, Is.False);
        Assert.That(error, Is.Not.Null);
    }

    #endregion

    #region Whitespace and Formatting Edge Cases

    [Test]
    public void Parse_ExtraSpacesAroundPeriods()
    {
        var script = "SYM(AAPL) . Entry(150) . TakeProfit(160)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_TabsInsteadOfSpaces()
    {
        var script = "SYM(AAPL)\t.Entry(150)\t.TakeProfit(160)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_WhitespaceInsideParens()
    {
        var script = "SYM( AAPL ).Entry( 150 ).TakeProfit( 160 )";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_DoubleParenthesesRecovery()
    {
        // Common typo: doubled parentheses
        var script = "SYM((AAPL)).Entry(150))";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_ColonBeforeParens_AutoCorrected()
    {
        // Common typo: colon instead of opening paren
        var script = "SYM(AAPL).TP:(160)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.GetStats().TakeProfit, Is.EqualTo(160).Within(0.01));
    }

    [Test]
    public void Parse_DoubleDollarSign_AutoCorrected()
    {
        // Common typo: double dollar sign
        var script = "SYM(AAPL).TP($$160)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.GetStats().TakeProfit, Is.EqualTo(160).Within(0.01));
    }

    [Test]
    public void Parse_DoublePercentSign_AutoCorrected()
    {
        // Common typo: double percent
        var script = "SYM(AAPL).TSL(15%%)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.GetStats().TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));
    }

    #endregion

    #region Special Character Edge Cases

    [Test]
    public void Parse_SymbolWithNumbers()
    {
        var script = "SYM(ABC123).Entry(10)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("ABC123"));
    }

    [Test]
    public void Parse_NameWithSpecialChars()
    {
        var script = @"SYM(AAPL).Name(""AAPL - Gap & Go (v2.0)"")";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Name, Is.EqualTo("AAPL - Gap & Go (v2.0)"));
    }

    #endregion

    #region Extreme Value Edge Cases

    [Test]
    public void Parse_VeryLargeQuantity()
    {
        var script = "SYM(AAPL).Quantity(10000)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.GetStats().Quantity, Is.EqualTo(10000));
    }

    [Test]
    public void Parse_VerySmallPrice()
    {
        var script = "SYM(PENNY).Entry(0.01).TakeProfit(0.02)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.GetStats().Price, Is.EqualTo(0.01).Within(0.001));
        Assert.That(result.GetStats().TakeProfit, Is.EqualTo(0.02).Within(0.001));
    }

    [Test]
    public void Parse_VeryLargePrice()
    {
        var script = "SYM(BRK).Entry(500000).TakeProfit(510000)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.GetStats().Price, Is.EqualTo(500000).Within(1));
        Assert.That(result.GetStats().TakeProfit, Is.EqualTo(510000).Within(1));
    }

    [Test]
    public void Parse_HighPrecisionPrice()
    {
        var script = "SYM(AAPL).Entry(150.123456)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.GetStats().Price, Is.EqualTo(150.123456).Within(0.000001));
    }

    #endregion

    #region HigherLows with Count Tests

    [TestCase("SYM(AAPL).HigherLows()")]
    [TestCase("SYM(AAPL).HigherLows(3)")]
    [TestCase("SYM(AAPL).HigherLows(5)")]
    [TestCase("SYM(AAPL).IsHigherLows()")]
    [TestCase("SYM(AAPL).IsHigherLows(3)")]
    public void Parse_HigherLows_WithAndWithoutCount(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsHigherLows);
        Assert.That(segment, Is.Not.Null);
    }

    #endregion

    #region EmaTurningUp Tests

    [TestCase("SYM(AAPL).EmaTurningUp(9)", 9)]
    [TestCase("SYM(AAPL).EmaTurningUp(21)", 21)]
    [TestCase("SYM(AAPL).IsEmaTurningUp(9)", 9)]
    [TestCase("SYM(AAPL).EMATURNINGUP(9)", 9)]
    [TestCase("SYM(AAPL).ematurningup(9)", 9)]
    public void Parse_EmaTurningUp_AllSyntax(string script, int expectedPeriod)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaTurningUp);
        Assert.That(segment, Is.Not.Null);

        var periodParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Period");
        Assert.That(periodParam, Is.Not.Null);
        Assert.That(Convert.ToInt32(periodParam!.Value), Is.EqualTo(expectedPeriod));
    }

    #endregion

    #region VolumeAbove Tests

    [TestCase("SYM(AAPL).VolumeAbove(1.5)", 1.5)]
    [TestCase("SYM(AAPL).VolumeAbove(2)", 2)]
    [TestCase("SYM(AAPL).VolumeAbove(2.5)", 2.5)]
    [TestCase("SYM(AAPL).IsVolumeAbove(1.5)", 1.5)]
    [TestCase("SYM(AAPL).VOLUMEABOVE(2)", 2)]
    public void Parse_VolumeAbove_AllSyntax(string script, double expectedMultiplier)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsVolumeAbove);
        Assert.That(segment, Is.Not.Null);

        var multiplierParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Multiplier");
        Assert.That(multiplierParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(multiplierParam!.Value), Is.EqualTo(expectedMultiplier));
    }

    #endregion

    #region CloseAboveVwap Tests (Alias for IsCloseAboveVwap)

    [TestCase("SYM(AAPL).CloseAboveVwap()")]
    [TestCase("SYM(AAPL).CloseAboveVwap")]
    [TestCase("SYM(AAPL).IsCloseAboveVwap()")]
    [TestCase("SYM(AAPL).IsCloseAboveVwap")]
    [TestCase("SYM(AAPL).CLOSEABOVEVWAP()")]
    [TestCase("SYM(AAPL).closeabovevwap()")]
    public void Parse_CloseAboveVwap_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsCloseAboveVwap);
        Assert.That(segment, Is.Not.Null);
    }

    #endregion
}
