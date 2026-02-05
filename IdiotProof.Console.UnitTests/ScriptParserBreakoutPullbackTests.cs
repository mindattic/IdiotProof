// ============================================================================
// StrategyScriptParser Breakout/Pullback Tests
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for BREAKOUT and PULLBACK conditions
/// Uses new IdiotScript syntax with period delimiters
/// </summary>
[TestFixture]
public class ScriptParserBreakoutPullbackTests
{
    #region Breakout Tests

    [TestCase("SYM(AAPL).OPEN(150).BREAKOUT()")]
    [TestCase("SYM(AAPL).OPEN(150).breakout()")]
    [TestCase("SYM(AAPL).OPEN(150).Breakout()")]
    public void Parse_Breakout_WithEntryPrice_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var breakoutSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        Assert.That(breakoutSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).BREAKOUT(150)", 150.0)]
    [TestCase("SYM(AAPL).BREAKOUT($150)", 150.0)]
    [TestCase("SYM(AAPL).BREAKOUT(148.75)", 148.75)]
    [TestCase("SYM(AAPL).breakout(200)", 200.0)]
    [TestCase("SYM(AAPL).Breakout($175.50)", 175.50)]
    public void Parse_Breakout_WithPrice(string script, double expectedPrice)
    {
        var result = StrategyScriptParser.Parse(script);
        var breakoutSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        Assert.That(breakoutSegment, Is.Not.Null);

        var levelParam = breakoutSegment!.Parameters.FirstOrDefault(p => p.Name == "Level");
        Assert.That(levelParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(levelParam!.Value), Is.EqualTo(expectedPrice).Within(0.01));
    }

    #endregion

    #region Pullback Tests

    [TestCase("SYM(AAPL).OPEN(150).PULLBACK()")]
    [TestCase("SYM(AAPL).OPEN(150).pullback()")]
    [TestCase("SYM(AAPL).OPEN(150).Pullback()")]
    public void Parse_Pullback_WithEntryPrice_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var pullbackSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);
        Assert.That(pullbackSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).PULLBACK(145)", 145.0)]
    [TestCase("SYM(AAPL).PULLBACK($145)", 145.0)]
    [TestCase("SYM(AAPL).PULLBACK(142.50)", 142.50)]
    [TestCase("SYM(AAPL).pullback(140)", 140.0)]
    [TestCase("SYM(AAPL).Pullback($138.25)", 138.25)]
    public void Parse_Pullback_WithPrice(string script, double expectedPrice)
    {
        var result = StrategyScriptParser.Parse(script);
        var pullbackSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);
        Assert.That(pullbackSegment, Is.Not.Null);

        var levelParam = pullbackSegment!.Parameters.FirstOrDefault(p => p.Name == "Level");
        Assert.That(levelParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(levelParam!.Value), Is.EqualTo(expectedPrice).Within(0.01));
    }

    #endregion

    #region Breakout and Pullback Combined

    [Test]
    public void Parse_BreakoutAndPullback_InSequence()
    {
        var result = StrategyScriptParser.Parse("SYM(PLTR).BREAKOUT(150).PULLBACK(145)");

        var breakoutSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        var pullbackSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);

        Assert.That(breakoutSegment, Is.Not.Null);
        Assert.That(pullbackSegment, Is.Not.Null);

        // Verify order
        Assert.That(breakoutSegment!.Order, Is.LessThan(pullbackSegment!.Order));
    }

    #endregion
}


