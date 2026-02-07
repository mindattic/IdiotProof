// ============================================================================
// StrategyScriptParser DI/MACD Condition Tests
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Enums;
using IdiotProof.Core.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for DI and MACD conditions: DiPositive, DiNegative, MacdBullish, MacdBearish
/// Uses IdiotScript syntax with period delimiters
/// Note: DI uses "Direction" parameter, MACD uses "State" parameter
/// </summary>
[TestFixture]
public class ScriptParserDiMacdTests
{
    #region DiPositive Tests

    [TestCase("SYM(AAPL).DiPositive()")]
    [TestCase("SYM(AAPL).DiPositive")]
    [TestCase("SYM(AAPL).DIPOSITIVE()")]
    [TestCase("SYM(AAPL).DIPOSITIVE")]
    [TestCase("SYM(AAPL).dipositive()")]
    [TestCase("SYM(AAPL).dipositive")]
    public void Parse_DiPositive_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        Assert.That(diSegment, Is.Not.Null);

        var directionParam = diSegment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam, Is.Not.Null);
        Assert.That(directionParam!.Value?.ToString(), Is.EqualTo("Positive"));
    }

    [TestCase("SYM(AAPL).IsDiPositive()")]
    [TestCase("SYM(AAPL).IsDiPositive")]
    [TestCase("SYM(AAPL).ISDIPOSITIVE()")]
    [TestCase("SYM(AAPL).isdipositive()")]
    public void Parse_IsDiPositive_Prefix(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        Assert.That(diSegment, Is.Not.Null);

        var directionParam = diSegment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam, Is.Not.Null);
        Assert.That(directionParam!.Value?.ToString(), Is.EqualTo("Positive"));
    }

    [Test]
    public void Parse_DiPositive_DefaultThreshold()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).DiPositive()");
        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        Assert.That(diSegment, Is.Not.Null);

        var thresholdParam = diSegment!.Parameters.FirstOrDefault(p => p.Name == "Threshold");
        Assert.That(thresholdParam, Is.Not.Null);
        // Default threshold is 25
        Assert.That(Convert.ToDouble(thresholdParam!.Value), Is.EqualTo(25));
    }

    [Test]
    public void Parse_DiPositive_CustomThreshold()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).DiPositive(30)");
        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        Assert.That(diSegment, Is.Not.Null);

        var thresholdParam = diSegment!.Parameters.FirstOrDefault(p => p.Name == "Threshold");
        Assert.That(thresholdParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(thresholdParam!.Value), Is.EqualTo(30));
    }

    #endregion

    #region DiNegative Tests

    [TestCase("SYM(AAPL).DiNegative()")]
    [TestCase("SYM(AAPL).DiNegative")]
    [TestCase("SYM(AAPL).DINEGATIVE()")]
    [TestCase("SYM(AAPL).DINEGATIVE")]
    [TestCase("SYM(AAPL).dinegative()")]
    [TestCase("SYM(AAPL).dinegative")]
    public void Parse_DiNegative_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        Assert.That(diSegment, Is.Not.Null);

        var directionParam = diSegment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam, Is.Not.Null);
        Assert.That(directionParam!.Value?.ToString(), Is.EqualTo("Negative"));
    }

    [TestCase("SYM(AAPL).IsDiNegative()")]
    [TestCase("SYM(AAPL).IsDiNegative")]
    [TestCase("SYM(AAPL).ISDINEGATIVE()")]
    [TestCase("SYM(AAPL).isdinegative()")]
    public void Parse_IsDiNegative_Prefix(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        Assert.That(diSegment, Is.Not.Null);

        var directionParam = diSegment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam, Is.Not.Null);
        Assert.That(directionParam!.Value?.ToString(), Is.EqualTo("Negative"));
    }

    [Test]
    public void Parse_DiNegative_CustomThreshold()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).DiNegative(35)");
        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        Assert.That(diSegment, Is.Not.Null);

        var thresholdParam = diSegment!.Parameters.FirstOrDefault(p => p.Name == "Threshold");
        Assert.That(thresholdParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(thresholdParam!.Value), Is.EqualTo(35));
    }

    #endregion

    #region MacdBullish Tests

    [TestCase("SYM(AAPL).MacdBullish()")]
    [TestCase("SYM(AAPL).MacdBullish")]
    [TestCase("SYM(AAPL).MACDBULLISH()")]
    [TestCase("SYM(AAPL).MACDBULLISH")]
    [TestCase("SYM(AAPL).macdbullish()")]
    [TestCase("SYM(AAPL).macdbullish")]
    public void Parse_MacdBullish_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var macdSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMacd);
        Assert.That(macdSegment, Is.Not.Null);

        var stateParam = macdSegment!.Parameters.FirstOrDefault(p => p.Name == "State");
        Assert.That(stateParam, Is.Not.Null);
        Assert.That(stateParam!.Value?.ToString(), Is.EqualTo("Bullish"));
    }

    [TestCase("SYM(AAPL).IsMacdBullish()")]
    [TestCase("SYM(AAPL).IsMacdBullish")]
    [TestCase("SYM(AAPL).ISMACDBULLISH()")]
    [TestCase("SYM(AAPL).ismacdbullish()")]
    public void Parse_IsMacdBullish_Prefix(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var macdSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMacd);
        Assert.That(macdSegment, Is.Not.Null);

        var stateParam = macdSegment!.Parameters.FirstOrDefault(p => p.Name == "State");
        Assert.That(stateParam, Is.Not.Null);
        Assert.That(stateParam!.Value?.ToString(), Is.EqualTo("Bullish"));
    }

    #endregion

    #region MacdBearish Tests

    [TestCase("SYM(AAPL).MacdBearish()")]
    [TestCase("SYM(AAPL).MacdBearish")]
    [TestCase("SYM(AAPL).MACDBEARISH()")]
    [TestCase("SYM(AAPL).MACDBEARISH")]
    [TestCase("SYM(AAPL).macdbearish()")]
    [TestCase("SYM(AAPL).macdbearish")]
    public void Parse_MacdBearish_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var macdSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMacd);
        Assert.That(macdSegment, Is.Not.Null);

        var stateParam = macdSegment!.Parameters.FirstOrDefault(p => p.Name == "State");
        Assert.That(stateParam, Is.Not.Null);
        Assert.That(stateParam!.Value?.ToString(), Is.EqualTo("Bearish"));
    }

    [TestCase("SYM(AAPL).IsMacdBearish()")]
    [TestCase("SYM(AAPL).IsMacdBearish")]
    [TestCase("SYM(AAPL).ISMACDBEARISH()")]
    [TestCase("SYM(AAPL).ismacdbearish()")]
    public void Parse_IsMacdBearish_Prefix(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var macdSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMacd);
        Assert.That(macdSegment, Is.Not.Null);

        var stateParam = macdSegment!.Parameters.FirstOrDefault(p => p.Name == "State");
        Assert.That(stateParam, Is.Not.Null);
        Assert.That(stateParam!.Value?.ToString(), Is.EqualTo("Bearish"));
    }

    #endregion

    #region Combined DI/MACD Tests

    [Test]
    public void Parse_DiPositiveWithMacdBullish_Combined()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).DiPositive().MacdBullish()");

        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        var macdSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMacd);

        Assert.That(diSegment, Is.Not.Null);
        Assert.That(macdSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_DiNegativeWithMacdBearish_Combined()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).DiNegative().MacdBearish()");

        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        var macdSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMacd);

        Assert.That(diSegment, Is.Not.Null);
        Assert.That(macdSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_FullTrendFollowingStrategy()
    {
        // Trend following setup with DI and MACD
        var script = "Ticker(NVDA).Session(IS.RTH).IsAdxAbove(25).IsDiPositive().IsMacdBullish().IsEmaAbove(9).Entry(150).TakeProfit(160)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAdx), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsMacd), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaAbove), Is.True);
    }

    [Test]
    public void Parse_BearishSetup_DiNegativeMacdBearish()
    {
        var script = "Ticker(SPY).DiNegative().MacdBearish().EmaBelow(9).Order(IS.SHORT)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("SPY"));

        var diSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsDI);
        var macdSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMacd);

        Assert.That(diSegment, Is.Not.Null);
        Assert.That(macdSegment, Is.Not.Null);

        var diDirection = diSegment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        var macdState = macdSegment!.Parameters.FirstOrDefault(p => p.Name == "State");

        Assert.That(diDirection!.Value?.ToString(), Is.EqualTo("Negative"));
        Assert.That(macdState!.Value?.ToString(), Is.EqualTo("Bearish"));
    }

    #endregion

    #region Order Preservation Tests

    [Test]
    public void Parse_DiAndMacdInChain_OrderPreserved()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).IsAboveVwap().IsDiPositive().IsMacdBullish().IsAdxAbove(25)");

        var conditions = result.Segments
            .Where(s => s.Type == SegmentType.IsAboveVwap ||
                       s.Type == SegmentType.IsDI ||
                       s.Type == SegmentType.IsMacd ||
                       s.Type == SegmentType.IsAdx)
            .OrderBy(s => s.Order)
            .ToList();

        Assert.That(conditions.Count, Is.EqualTo(4));
        Assert.That(conditions[0].Type, Is.EqualTo(SegmentType.IsAboveVwap));
        Assert.That(conditions[1].Type, Is.EqualTo(SegmentType.IsDI));
        Assert.That(conditions[2].Type, Is.EqualTo(SegmentType.IsMacd));
        Assert.That(conditions[3].Type, Is.EqualTo(SegmentType.IsAdx));
    }

    #endregion
}
