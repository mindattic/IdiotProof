// ============================================================================
// StrategyScriptParser Gap Condition Tests - IsGapUp, IsGapDown
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for gap conditions: IsGapUp, IsGapDown
/// Uses IdiotScript syntax with period delimiters
/// </summary>
[TestFixture]
public class ScriptParserGapConditionTests
{
    #region GapUp Tests

    [TestCase("SYM(AAPL).GapUp(5)", 5)]
    [TestCase("SYM(AAPL).GapUp(10)", 10)]
    [TestCase("SYM(AAPL).GapUp(3.5)", 3.5)]
    [TestCase("SYM(AAPL).GAPUP(5)", 5)]
    [TestCase("SYM(AAPL).gapup(5)", 5)]
    public void Parse_GapUp_AllSyntax(string script, double expectedPercent)
    {
        var result = StrategyScriptParser.Parse(script);
        var gapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.GapUp);
        Assert.That(gapSegment, Is.Not.Null);

        var percentParam = gapSegment!.Parameters.FirstOrDefault(p => p.Name == "Percentage");
        Assert.That(percentParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(percentParam!.Value), Is.EqualTo(expectedPercent));
    }

    [TestCase("SYM(AAPL).GapUp(5%)", 5)]
    [TestCase("SYM(AAPL).GapUp(10%)", 10)]
    [TestCase("SYM(AAPL).GAPUP(3%)", 3)]
    public void Parse_GapUp_WithPercentSign(string script, double expectedPercent)
    {
        var result = StrategyScriptParser.Parse(script);
        var gapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.GapUp);
        Assert.That(gapSegment, Is.Not.Null);

        var percentParam = gapSegment!.Parameters.FirstOrDefault(p => p.Name == "Percentage");
        Assert.That(percentParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(percentParam!.Value), Is.EqualTo(expectedPercent));
    }

    [TestCase("SYM(AAPL).IsGapUp(5)", 5)]
    [TestCase("SYM(AAPL).IsGapUp(10)", 10)]
    [TestCase("SYM(AAPL).ISGAPUP(5)", 5)]
    [TestCase("SYM(AAPL).isgapup(5)", 5)]
    public void Parse_IsGapUp_Prefix(string script, double expectedPercent)
    {
        var result = StrategyScriptParser.Parse(script);
        var gapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.GapUp);
        Assert.That(gapSegment, Is.Not.Null);

        var percentParam = gapSegment!.Parameters.FirstOrDefault(p => p.Name == "Percentage");
        Assert.That(percentParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(percentParam!.Value), Is.EqualTo(expectedPercent));
    }

    #endregion

    #region GapDown Tests

    [TestCase("SYM(AAPL).GapDown(5)", 5)]
    [TestCase("SYM(AAPL).GapDown(10)", 10)]
    [TestCase("SYM(AAPL).GapDown(3.5)", 3.5)]
    [TestCase("SYM(AAPL).GAPDOWN(5)", 5)]
    [TestCase("SYM(AAPL).gapdown(5)", 5)]
    public void Parse_GapDown_AllSyntax(string script, double expectedPercent)
    {
        var result = StrategyScriptParser.Parse(script);
        var gapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.GapDown);
        Assert.That(gapSegment, Is.Not.Null);

        var percentParam = gapSegment!.Parameters.FirstOrDefault(p => p.Name == "Percentage");
        Assert.That(percentParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(percentParam!.Value), Is.EqualTo(expectedPercent));
    }

    [TestCase("SYM(AAPL).GapDown(5%)", 5)]
    [TestCase("SYM(AAPL).GapDown(10%)", 10)]
    [TestCase("SYM(AAPL).GAPDOWN(3%)", 3)]
    public void Parse_GapDown_WithPercentSign(string script, double expectedPercent)
    {
        var result = StrategyScriptParser.Parse(script);
        var gapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.GapDown);
        Assert.That(gapSegment, Is.Not.Null);

        var percentParam = gapSegment!.Parameters.FirstOrDefault(p => p.Name == "Percentage");
        Assert.That(percentParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(percentParam!.Value), Is.EqualTo(expectedPercent));
    }

    [TestCase("SYM(AAPL).IsGapDown(5)", 5)]
    [TestCase("SYM(AAPL).IsGapDown(10)", 10)]
    [TestCase("SYM(AAPL).ISGAPDOWN(5)", 5)]
    [TestCase("SYM(AAPL).isgapdown(5)", 5)]
    public void Parse_IsGapDown_Prefix(string script, double expectedPercent)
    {
        var result = StrategyScriptParser.Parse(script);
        var gapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.GapDown);
        Assert.That(gapSegment, Is.Not.Null);

        var percentParam = gapSegment!.Parameters.FirstOrDefault(p => p.Name == "Percentage");
        Assert.That(percentParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(percentParam!.Value), Is.EqualTo(expectedPercent));
    }

    #endregion

    #region Gap Combined with Other Conditions

    [Test]
    public void Parse_GapUpWithVwapAndEma_Combined()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).IsGapUp(5).IsAboveVwap().IsEmaAbove(9)");

        var gapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.GapUp);
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove);

        Assert.That(gapSegment, Is.Not.Null);
        Assert.That(vwapSegment, Is.Not.Null);
        Assert.That(emaSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_GapDownWithVwapAndEma_Combined()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).IsGapDown(3).IsBelowVwap().IsEmaBelow(9)");

        var gapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.GapDown);
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsBelowVwap);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBelow);

        Assert.That(gapSegment, Is.Not.Null);
        Assert.That(vwapSegment, Is.Not.Null);
        Assert.That(emaSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_FullGapAndGoStrategy()
    {
        // Example from the copilot-instructions.md
        var script = "Ticker(NVDA).Session(IS.PREMARKET).GapUp(5).AboveVwap().DiPositive().Order().TakeProfit(160).StopLoss(140)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.GapUp), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
    }

    #endregion

    #region Gap Order Preservation

    [Test]
    public void Parse_GapInConditionChain_OrderPreserved()
    {
        var result = StrategyScriptParser.Parse("SYM(PLTR).GapUp(5).Breakout(150).Pullback(145).AboveVwap()");

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.GapUp ||
                       s.Type == SegmentType.Breakout ||
                       s.Type == SegmentType.Pullback ||
                       s.Type == SegmentType.IsAboveVwap)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(4));
        Assert.That(conditions[0].Type, Is.EqualTo(SegmentType.GapUp));
        Assert.That(conditions[1].Type, Is.EqualTo(SegmentType.Breakout));
        Assert.That(conditions[2].Type, Is.EqualTo(SegmentType.Pullback));
        Assert.That(conditions[3].Type, Is.EqualTo(SegmentType.IsAboveVwap));
    }

    #endregion
}
