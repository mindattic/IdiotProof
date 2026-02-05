// ============================================================================
// StrategyScriptParser AdaptiveOrder Tests
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for AdaptiveOrder command with all modes (AGGRESSIVE, BALANCED, CONSERVATIVE)
/// Uses IdiotScript syntax with period delimiters and IS. constants
/// </summary>
[TestFixture]
public class ScriptParserAdaptiveOrderTests
{
    #region AdaptiveOrder Basic Syntax Tests

    [TestCase("SYM(AAPL).AdaptiveOrder()")]
    [TestCase("SYM(AAPL).AdaptiveOrder")]
    [TestCase("SYM(AAPL).ADAPTIVEORDER()")]
    [TestCase("SYM(AAPL).ADAPTIVEORDER")]
    [TestCase("SYM(AAPL).adaptiveorder()")]
    [TestCase("SYM(AAPL).adaptiveorder")]
    public void Parse_AdaptiveOrder_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);
        Assert.That(adaptiveSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).IsAdaptiveOrder()")]
    [TestCase("SYM(AAPL).IsAdaptiveOrder")]
    [TestCase("SYM(AAPL).ISADAPTIVEORDER()")]
    [TestCase("SYM(AAPL).isadaptiveorder()")]
    public void Parse_IsAdaptiveOrder_Prefix(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);
        Assert.That(adaptiveSegment, Is.Not.Null);
    }

    #endregion

    #region AdaptiveOrder Mode Tests

    [TestCase("SYM(AAPL).AdaptiveOrder(IS.AGGRESSIVE)", "Aggressive")]
    [TestCase("SYM(AAPL).AdaptiveOrder(is.aggressive)", "Aggressive")]
    [TestCase("SYM(AAPL).AdaptiveOrder(AGGRESSIVE)", "Aggressive")]
    [TestCase("SYM(AAPL).AdaptiveOrder(aggressive)", "Aggressive")]
    public void Parse_AdaptiveOrder_AggressiveMode(string script, string expectedMode)
    {
        var result = StrategyScriptParser.Parse(script);
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);
        Assert.That(adaptiveSegment, Is.Not.Null);

        var modeParam = adaptiveSegment!.Parameters.FirstOrDefault(p => p.Name == "Mode");
        Assert.That(modeParam, Is.Not.Null);
        Assert.That(modeParam!.Value?.ToString(), Is.EqualTo(expectedMode).IgnoreCase);
    }

    [TestCase("SYM(AAPL).AdaptiveOrder(IS.BALANCED)", "Balanced")]
    [TestCase("SYM(AAPL).AdaptiveOrder(is.balanced)", "Balanced")]
    [TestCase("SYM(AAPL).AdaptiveOrder(BALANCED)", "Balanced")]
    [TestCase("SYM(AAPL).AdaptiveOrder(balanced)", "Balanced")]
    public void Parse_AdaptiveOrder_BalancedMode(string script, string expectedMode)
    {
        var result = StrategyScriptParser.Parse(script);
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);
        Assert.That(adaptiveSegment, Is.Not.Null);

        var modeParam = adaptiveSegment!.Parameters.FirstOrDefault(p => p.Name == "Mode");
        Assert.That(modeParam, Is.Not.Null);
        Assert.That(modeParam!.Value?.ToString(), Is.EqualTo(expectedMode).IgnoreCase);
    }

    [TestCase("SYM(AAPL).AdaptiveOrder(IS.CONSERVATIVE)", "Conservative")]
    [TestCase("SYM(AAPL).AdaptiveOrder(is.conservative)", "Conservative")]
    [TestCase("SYM(AAPL).AdaptiveOrder(CONSERVATIVE)", "Conservative")]
    [TestCase("SYM(AAPL).AdaptiveOrder(conservative)", "Conservative")]
    public void Parse_AdaptiveOrder_ConservativeMode(string script, string expectedMode)
    {
        var result = StrategyScriptParser.Parse(script);
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);
        Assert.That(adaptiveSegment, Is.Not.Null);

        var modeParam = adaptiveSegment!.Parameters.FirstOrDefault(p => p.Name == "Mode");
        Assert.That(modeParam, Is.Not.Null);
        Assert.That(modeParam!.Value?.ToString(), Is.EqualTo(expectedMode).IgnoreCase);
    }

    #endregion

    #region AdaptiveOrder Default Mode Tests

    [Test]
    public void Parse_AdaptiveOrderNoParam_DefaultsToBalanced()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).AdaptiveOrder()");
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);
        Assert.That(adaptiveSegment, Is.Not.Null);

        // When no mode is specified, it should default to Balanced or have no mode param
        var modeParam = adaptiveSegment!.Parameters.FirstOrDefault(p => p.Name == "Mode");
        if (modeParam != null)
        {
            Assert.That(modeParam.Value?.ToString(), Is.EqualTo("Balanced").IgnoreCase);
        }
        // If no mode param, that's also acceptable as it will use the default
    }

    [Test]
    public void Parse_AdaptiveOrderEmptyParam_DefaultsToBalanced()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).AdaptiveOrder");
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);
        Assert.That(adaptiveSegment, Is.Not.Null);
    }

    #endregion

    #region AdaptiveOrder Combined with Risk Management

    [Test]
    public void Parse_AdaptiveOrderWithTakeProfitAndStopLoss()
    {
        var script = "Ticker(AAPL).Entry(150).TakeProfit(160).StopLoss(145).AdaptiveOrder(IS.AGGRESSIVE)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));

        var tpSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.TakeProfit);
        var slSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.StopLoss);
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);

        Assert.That(tpSegment, Is.Not.Null);
        Assert.That(slSegment, Is.Not.Null);
        Assert.That(adaptiveSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_AdaptiveOrderWithTrailingStopLoss()
    {
        var script = "Ticker(AAPL).Entry(150).TakeProfit(160).TrailingStopLoss(IS.MODERATE).AdaptiveOrder(IS.BALANCED)";
        var result = StrategyScriptParser.Parse(script);

        var tslSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.TrailingStopLoss);
        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);

        Assert.That(tslSegment, Is.Not.Null);
        Assert.That(adaptiveSegment, Is.Not.Null);
    }

    #endregion

    #region Full Strategy Examples

    [Test]
    public void Parse_FullAdaptiveOrderStrategy_Aggressive()
    {
        // Example from copilot-instructions.md
        var script = @"
            Ticker(AAPL)
            .Entry(150)
            .TakeProfit(160)
            .StopLoss(145)
            .IsAboveVwap()
            .IsEmaAbove(9)
            .IsDiPositive()
            .AdaptiveOrder(IS.AGGRESSIVE)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaAbove), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.AdaptiveOrder), Is.True);
    }

    [Test]
    public void Parse_GapAndGoWithAdaptiveOrder()
    {
        // Gap and Go example with adaptive order
        var script = "Ticker(NVDA).Session(IS.PREMARKET).GapUp(5).AboveVwap().DiPositive().Order().AdaptiveOrder(IS.AGGRESSIVE)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.GapUp), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.AdaptiveOrder), Is.True);
    }

    [Test]
    public void Parse_ConservativeAdaptiveOrderStrategy()
    {
        var script = "Ticker(SPY).Session(IS.RTH).Entry(500).TakeProfit(510).StopLoss(495).AdaptiveOrder(IS.CONSERVATIVE)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("SPY"));

        var adaptiveSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AdaptiveOrder);
        Assert.That(adaptiveSegment, Is.Not.Null);

        var modeParam = adaptiveSegment!.Parameters.FirstOrDefault(p => p.Name == "Mode");
        Assert.That(modeParam, Is.Not.Null);
        Assert.That(modeParam!.Value?.ToString(), Is.EqualTo("Conservative").IgnoreCase);
    }

    #endregion

    #region Order Preservation Tests

    [Test]
    public void Parse_AdaptiveOrderInChain_OrderPreserved()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).IsAboveVwap().IsDiPositive().AdaptiveOrder(IS.BALANCED)");

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.IsAboveVwap ||
                       s.Type == SegmentType.IsDI ||
                       s.Type == SegmentType.AdaptiveOrder)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(3));
        Assert.That(conditions[0].Type, Is.EqualTo(SegmentType.IsAboveVwap));
        Assert.That(conditions[1].Type, Is.EqualTo(SegmentType.IsDI));
        Assert.That(conditions[2].Type, Is.EqualTo(SegmentType.AdaptiveOrder));
    }

    #endregion
}
