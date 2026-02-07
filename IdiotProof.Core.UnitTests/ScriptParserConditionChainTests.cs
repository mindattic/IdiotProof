// ============================================================================
// StrategyScriptParser Condition Chain Tests - Order of Operations
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for condition chains using period (.) delimiter
/// Uses new IdiotScript syntax
/// </summary>
[TestFixture]
public class ScriptParserConditionChainTests
{
    #region Simple Chain Tests

    [Test]
    public void Parse_TwoConditionChain_BreakoutThenVwap()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).BREAKOUT(150).ABOVE_VWAP");

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.Breakout || s.Type == SegmentType.IsAboveVwap)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(2));
        Assert.That(conditions[0].Type, Is.EqualTo(SegmentType.Breakout));
        Assert.That(conditions[1].Type, Is.EqualTo(SegmentType.IsAboveVwap));
    }

    [Test]
    public void Parse_ThreeConditionChain_OrderPreserved()
    {
        var result = StrategyScriptParser.Parse("SYM(PLTR).BREAKOUT(148).PULLBACK(145).ABOVE_VWAP");

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.Breakout || 
                       s.Type == SegmentType.Pullback || 
                       s.Type == SegmentType.IsAboveVwap)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(3));
        Assert.That(conditions[0].Type, Is.EqualTo(SegmentType.Breakout));
        Assert.That(conditions[1].Type, Is.EqualTo(SegmentType.Pullback));
        Assert.That(conditions[2].Type, Is.EqualTo(SegmentType.IsAboveVwap));
    }

    [Test]
    public void Parse_FourConditionChain_OrderPreserved()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(PLTR).BREAKOUT(148).PULLBACK(145).ABOVE_VWAP.ABOVE_EMA(9)");

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.Breakout || 
                       s.Type == SegmentType.Pullback || 
                       s.Type == SegmentType.IsAboveVwap ||
                       s.Type == SegmentType.IsEmaAbove)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(4));
        Assert.That(conditions[0].Type, Is.EqualTo(SegmentType.Breakout));
        Assert.That(conditions[1].Type, Is.EqualTo(SegmentType.Pullback));
        Assert.That(conditions[2].Type, Is.EqualTo(SegmentType.IsAboveVwap));
        Assert.That(conditions[3].Type, Is.EqualTo(SegmentType.IsEmaAbove));
    }

    [Test]
    public void Parse_FiveConditionChain_OrderPreserved()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(PLTR).BREAKOUT(148).PULLBACK(145).ABOVE_VWAP.BETWEEN_EMA(9, 21).ABOVE_EMA(200)");

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.Breakout || 
                       s.Type == SegmentType.Pullback || 
                       s.Type == SegmentType.IsAboveVwap ||
                       s.Type == SegmentType.IsEmaBetween ||
                       s.Type == SegmentType.IsEmaAbove)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(5));
        Assert.That(conditions[0].Type, Is.EqualTo(SegmentType.Breakout));
        Assert.That(conditions[1].Type, Is.EqualTo(SegmentType.Pullback));
        Assert.That(conditions[2].Type, Is.EqualTo(SegmentType.IsAboveVwap));
        Assert.That(conditions[3].Type, Is.EqualTo(SegmentType.IsEmaBetween));
        Assert.That(conditions[4].Type, Is.EqualTo(SegmentType.IsEmaAbove));
    }

    #endregion

    #region Chain Case Insensitivity

    [TestCase("SYM(AAPL).breakout(150).above_vwap.above_ema(9)")]
    [TestCase("SYM(AAPL).BREAKOUT(150).ABOVE_VWAP.ABOVE_EMA(9)")]
    [TestCase("SYM(AAPL).Breakout(150).Above_Vwap.Above_Ema(9)")]
    [TestCase("SYM(AAPL).BreakOut(150).ABOVE_vwap.above_EMA(9)")]
    public void Parse_Chain_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.Breakout || 
                       s.Type == SegmentType.IsAboveVwap ||
                       s.Type == SegmentType.IsEmaAbove)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(3));
        Assert.That(conditions[0].Type, Is.EqualTo(SegmentType.Breakout));
        Assert.That(conditions[1].Type, Is.EqualTo(SegmentType.IsAboveVwap));
        Assert.That(conditions[2].Type, Is.EqualTo(SegmentType.IsEmaAbove));
    }

    #endregion

    #region Chain with All Condition Types

    [Test]
    public void Parse_Chain_AllConditionTypes()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(AAPL).BREAKOUT(150).PULLBACK(145).ABOVE_VWAP.ABOVE_EMA(9).BETWEEN_EMA(9, 21).RSI_OVERSOLD(30).ADX_ABOVE(25)");

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.Breakout || 
                       s.Type == SegmentType.Pullback || 
                       s.Type == SegmentType.IsAboveVwap ||
                       s.Type == SegmentType.IsEmaAbove ||
                       s.Type == SegmentType.IsEmaBetween ||
                       s.Type == SegmentType.IsRsi ||
                       s.Type == SegmentType.IsAdx)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.GreaterThanOrEqualTo(6));
    }

    #endregion

    #region Chain Position in Full Script

    [Test]
    public void Parse_ChainAtEnd_WithPriceParams()
    {
        // New format: params first, conditions at end
        var result = StrategyScriptParser.Parse(
            "SYM(PLTR).QTY(10).TP($158).TSL(15%).BREAKOUT(148).PULLBACK(145).ABOVE_VWAP");

        Assert.That(result.Symbol, Is.EqualTo("PLTR"));

        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(158.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));

        var breakoutSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        var pullbackSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);

        Assert.That(breakoutSegment, Is.Not.Null);
        Assert.That(pullbackSegment, Is.Not.Null);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_CompleteStrategy_AllElements()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(PLTR).NAME(\"Palantir Premarket\").QTY(10).SESSION(PreMarketEndEarly)." +
            "TP($153.50).SL($145.50).TSL(5%)." +
            "BREAKOUT(148).PULLBACK(145).ABOVE_VWAP.BETWEEN_EMA(9, 21).ABOVE_EMA(200)");

        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        Assert.That(result.Name, Is.EqualTo("Palantir Premarket"));

        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.Breakout || 
                       s.Type == SegmentType.Pullback || 
                       s.Type == SegmentType.IsAboveVwap ||
                       s.Type == SegmentType.IsEmaBetween ||
                       s.Type == SegmentType.IsEmaAbove)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(5));
    }

    #endregion

    #region Standalone Conditions (No Chain)

    [Test]
    public void Parse_StandaloneConditions_NotChained()
    {
        // Conditions without . should still work
        var result = StrategyScriptParser.Parse("SYM(AAPL).ABOVE_VWAP");

        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_MultipleStandaloneConditions_SeparateByPeriod()
    {
        // Multiple conditions separated by . 
        var result = StrategyScriptParser.Parse("SYM(AAPL).ABOVE_VWAP.ABOVE_EMA(9)");

        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove);

        Assert.That(vwapSegment, Is.Not.Null);
        Assert.That(emaSegment, Is.Not.Null);
    }

    #endregion
}


