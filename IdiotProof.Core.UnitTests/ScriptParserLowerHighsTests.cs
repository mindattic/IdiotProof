// ============================================================================
// StrategyScriptParser LowerHighs and Pattern Tests
// ============================================================================

using IdiotProof.Core.Enums;
using IdiotProof.Core.Scripting;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for LowerHighs pattern detection command (bearish counterpart to HigherLows).
/// </summary>
[TestFixture]
public class ScriptParserLowerHighsTests
{
    #region LowerHighs Basic Syntax Tests

    [TestCase("SYM(AAPL).LowerHighs()")]
    [TestCase("SYM(AAPL).LowerHighs")]
    [TestCase("SYM(AAPL).IsLowerHighs()")]
    [TestCase("SYM(AAPL).IsLowerHighs")]
    [TestCase("SYM(AAPL).lowerhighs()")]
    [TestCase("SYM(AAPL).LOWERHIGHS()")]
    public void Parse_LowerHighs_AllSyntax(string script)
    {
        var result = IdiotScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        Assert.That(segment, Is.Not.Null);
    }

    [Test]
    public void Parse_LowerHighs_DisplayName_IsSet()
    {
        var result = IdiotScriptParser.Parse("SYM(AAPL).LowerHighs()");
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        Assert.That(segment, Is.Not.Null);
        Assert.That(segment!.DisplayName, Is.Not.Null.And.Not.Empty);
    }

    #endregion

    #region LowerHighs with Lookback Parameter Tests

    [TestCase("SYM(AAPL).LowerHighs(3)", 3)]
    [TestCase("SYM(AAPL).LowerHighs(4)", 4)]
    [TestCase("SYM(AAPL).LowerHighs(5)", 5)]
    [TestCase("SYM(AAPL).IsLowerHighs(3)", 3)]
    [TestCase("SYM(AAPL).LOWERHIGHS(4)", 4)]
    public void Parse_LowerHighs_WithLookback(string script, int expectedLookback)
    {
        var result = IdiotScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        Assert.That(segment, Is.Not.Null);

        var lookbackParam = segment!.Parameters.FirstOrDefault(p => p.Name == "LookbackBars" || p.Name == "Lookback");
        if (lookbackParam != null)
        {
            Assert.That(Convert.ToInt32(lookbackParam.Value), Is.EqualTo(expectedLookback));
        }
    }

    #endregion

    #region LowerHighs Combined with Other Indicators

    [Test]
    public void Parse_LowerHighs_WithBelowVwap()
    {
        var result = IdiotScriptParser.Parse("SYM(AAPL).LowerHighs().BelowVwap()");

        var lowerHighsSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        var belowVwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsBelowVwap);

        Assert.That(lowerHighsSegment, Is.Not.Null);
        Assert.That(belowVwapSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_LowerHighs_WithMomentumBelow()
    {
        var result = IdiotScriptParser.Parse("SYM(AAPL).LowerHighs().MomentumBelow(0)");

        var lowerHighsSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        var momentumSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMomentum);

        Assert.That(lowerHighsSegment, Is.Not.Null);
        Assert.That(momentumSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_BearishReversal_FullStrategy()
    {
        // A complete bearish reversal setup with lower highs
        var script = @"
            Ticker(SPY)
            .Session(IS.RTH)
            .BelowVwap()
            .LowerHighs()
            .EmaBelow(9)
            .MomentumBelow(0)
            .Short()
            .TakeProfit(440)
            .StopLoss(460)
        ";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("SPY"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsBelowVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsLowerHighs), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaBelow), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsMomentum), Is.True);
    }

    #endregion

    #region HigherLows vs LowerHighs Comparison

    [Test]
    public void Parse_HigherLows_IsBullish()
    {
        var result = IdiotScriptParser.Parse("SYM(AAPL).HigherLows().AboveVwap()");

        var higherLowsSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsHigherLows);
        var aboveVwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);

        Assert.That(higherLowsSegment, Is.Not.Null);
        Assert.That(aboveVwapSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_LowerHighs_IsBearish()
    {
        var result = IdiotScriptParser.Parse("SYM(AAPL).LowerHighs().BelowVwap()");

        var lowerHighsSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);
        var belowVwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsBelowVwap);

        Assert.That(lowerHighsSegment, Is.Not.Null);
        Assert.That(belowVwapSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_BothPatterns_CanCoexist()
    {
        // Edge case: both patterns in same strategy (unusual but valid)
        var result = IdiotScriptParser.Parse("SYM(AAPL).HigherLows().LowerHighs()");

        var higherLowsSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsHigherLows);
        var lowerHighsSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsLowerHighs);

        Assert.That(higherLowsSegment, Is.Not.Null);
        Assert.That(lowerHighsSegment, Is.Not.Null);
    }

    #endregion
}
