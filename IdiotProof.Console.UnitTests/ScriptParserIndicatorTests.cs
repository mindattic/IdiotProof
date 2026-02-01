// ============================================================================
// StrategyScriptParser Indicator Condition Tests - VWAP, EMA, RSI, ADX
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for indicator conditions: VWAP, EMA, RSI, ADX
/// Uses new IdiotScript syntax with period delimiters and IS. constants
/// </summary>
[TestFixture]
public class ScriptParserIndicatorTests
{
    #region VWAP Tests

    [TestCase("SYM(AAPL).VWAP")]
    [TestCase("SYM(AAPL).ABOVE_VWAP")]
    [TestCase("SYM(AAPL).vwap")]
    [TestCase("SYM(AAPL).above_vwap")]
    [TestCase("SYM(AAPL).Above_Vwap")]
    [TestCase("SYM(AAPL).Above_VWAP")]
    public void Parse_AboveVwap_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).BELOW_VWAP")]
    [TestCase("SYM(AAPL).below_vwap")]
    [TestCase("SYM(AAPL).Below_Vwap")]
    public void Parse_BelowVwap_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsBelowVwap);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    #endregion

    #region EMA Tests

    [TestCase("SYM(AAPL).ABOVE_EMA(9)", 9)]
    [TestCase("SYM(AAPL).above_ema(21)", 21)]
    [TestCase("SYM(AAPL).above_ema(50)", 50)]
    [TestCase("SYM(AAPL).Above_Ema(200)", 200)]
    public void Parse_AboveEma_AllSyntax(string script, int expectedPeriod)
    {
        var result = StrategyScriptParser.Parse(script);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove);
        Assert.That(emaSegment, Is.Not.Null);
        var periodParam = emaSegment!.Parameters.FirstOrDefault(p => p.Name == "Period");
        Assert.That(periodParam, Is.Not.Null);
        Assert.That(Convert.ToInt32(periodParam!.Value), Is.EqualTo(expectedPeriod));
    }

    [TestCase("SYM(AAPL).BELOW_EMA(9)", 9)]
    [TestCase("SYM(AAPL).below_ema(21)", 21)]
    [TestCase("SYM(AAPL).below_ema(50)", 50)]
    [TestCase("SYM(AAPL).Below_Ema(200)", 200)]
    public void Parse_BelowEma_AllSyntax(string script, int expectedPeriod)
    {
        var result = StrategyScriptParser.Parse(script);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBelow);
        Assert.That(emaSegment, Is.Not.Null);
        var periodParam = emaSegment!.Parameters.FirstOrDefault(p => p.Name == "Period");
        Assert.That(periodParam, Is.Not.Null);
        Assert.That(Convert.ToInt32(periodParam!.Value), Is.EqualTo(expectedPeriod));
    }

    [TestCase("SYM(AAPL).BETWEEN_EMA(9, 21)", 9, 21)]
    [TestCase("SYM(AAPL).between_ema(9, 50)", 9, 50)]
    [TestCase("SYM(AAPL).between_ema(21, 200)", 21, 200)]
    [TestCase("SYM(AAPL).Between_Ema(9, 21)", 9, 21)]
    public void Parse_EmaBetween_AllSyntax(string script, int expectedLower, int expectedUpper)
    {
        var result = StrategyScriptParser.Parse(script);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBetween);
        Assert.That(emaSegment, Is.Not.Null);

        var lowerParam = emaSegment!.Parameters.FirstOrDefault(p => p.Name == "LowerPeriod");
        var upperParam = emaSegment.Parameters.FirstOrDefault(p => p.Name == "UpperPeriod");

        Assert.That(lowerParam, Is.Not.Null);
        Assert.That(upperParam, Is.Not.Null);
        Assert.That(Convert.ToInt32(lowerParam!.Value), Is.EqualTo(expectedLower));
        Assert.That(Convert.ToInt32(upperParam!.Value), Is.EqualTo(expectedUpper));
    }

    #endregion

    #region RSI Tests

    [TestCase("SYM(AAPL).RSI_OVERSOLD(30)", 30)]
    [TestCase("SYM(AAPL).rsi_oversold(25)", 25)]
    [TestCase("SYM(AAPL).rsi_oversold(20)", 20)]
    public void Parse_RsiOversold_AllSyntax(string script, double expectedValue)
    {
        var result = StrategyScriptParser.Parse(script);
        var rsiSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRsi);
        Assert.That(rsiSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).RSI_OVERBOUGHT(70)")]
    [TestCase("SYM(AAPL).rsi_overbought(70)")]
    [TestCase("SYM(AAPL).rsi_overbought(80)")]
    public void Parse_RsiOverbought_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var rsiSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRsi);
        Assert.That(rsiSegment, Is.Not.Null);
    }

    #endregion

    #region ADX Tests

    [TestCase("SYM(AAPL).ADX_ABOVE(25)", 25)]
    [TestCase("SYM(AAPL).adx_above(30)", 30)]
    [TestCase("SYM(AAPL).adx_above(20)", 20)]
    [TestCase("SYM(AAPL).Adx_Above(25)", 25)]
    public void Parse_Adx_AllSyntax(string script, double expectedValue)
    {
        var result = StrategyScriptParser.Parse(script);
        var adxSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAdx);
        Assert.That(adxSegment, Is.Not.Null);
    }

    #endregion

    #region Multiple Indicators Combined

    [Test]
    public void Parse_MultipleIndicators_Combined()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).ABOVE_VWAP.ABOVE_EMA(9).ABOVE_EMA(21)");

        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        var emaSegments = result.Segments.Where(s => s.Type == SegmentType.IsEmaAbove).ToList();

        Assert.That(vwapSegment, Is.Not.Null);
        Assert.That(emaSegments.Count, Is.EqualTo(2));
    }

    #endregion
}
