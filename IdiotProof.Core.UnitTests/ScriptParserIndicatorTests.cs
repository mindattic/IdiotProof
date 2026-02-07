// ============================================================================
// StrategyScriptParser Indicator Condition Tests - VWAP, EMA, RSI, ADX
// ============================================================================

using IdiotProof.Core.Scripting;
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

    #region Momentum Tests

    [TestCase("SYM(AAPL).MomentumAbove(0)", 0)]
    [TestCase("SYM(AAPL).MomentumAbove(5)", 5)]
    [TestCase("SYM(AAPL).MomentumAbove(10.5)", 10.5)]
    [TestCase("SYM(AAPL).momentumabove(0)", 0)]
    [TestCase("SYM(AAPL).MOMENTUMABOVE(5)", 5)]
    [TestCase("SYM(AAPL).IsMomentumAbove(0)", 0)]
    public void Parse_MomentumAbove_AllSyntax(string script, double expectedThreshold)
    {
        var result = StrategyScriptParser.Parse(script);
        var momentumSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMomentum);
        Assert.That(momentumSegment, Is.Not.Null);

        var conditionParam = momentumSegment!.Parameters.FirstOrDefault(p => p.Name == "Condition");
        var thresholdParam = momentumSegment.Parameters.FirstOrDefault(p => p.Name == "Threshold");

        Assert.That(conditionParam, Is.Not.Null);
        Assert.That(thresholdParam, Is.Not.Null);
        Assert.That(conditionParam!.Value, Is.EqualTo("Above"));
        Assert.That(Convert.ToDouble(thresholdParam!.Value), Is.EqualTo(expectedThreshold));
    }

    [TestCase("SYM(AAPL).MomentumBelow(0)", 0)]
    [TestCase("SYM(AAPL).MomentumBelow(-5)", -5)]
    [TestCase("SYM(AAPL).momentumbelow(0)", 0)]
    [TestCase("SYM(AAPL).MOMENTUMBELOW(-5)", -5)]
    [TestCase("SYM(AAPL).IsMomentumBelow(0)", 0)]
    public void Parse_MomentumBelow_AllSyntax(string script, double expectedThreshold)
    {
        var result = StrategyScriptParser.Parse(script);
        var momentumSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMomentum);
        Assert.That(momentumSegment, Is.Not.Null);

        var conditionParam = momentumSegment!.Parameters.FirstOrDefault(p => p.Name == "Condition");
        var thresholdParam = momentumSegment.Parameters.FirstOrDefault(p => p.Name == "Threshold");

        Assert.That(conditionParam, Is.Not.Null);
        Assert.That(thresholdParam, Is.Not.Null);
        Assert.That(conditionParam!.Value, Is.EqualTo("Below"));
        Assert.That(Convert.ToDouble(thresholdParam!.Value), Is.EqualTo(expectedThreshold));
    }

    #endregion

    #region Rate of Change (ROC) Tests

    [TestCase("SYM(AAPL).RocAbove(2)", 2)]
    [TestCase("SYM(AAPL).RocAbove(5.5)", 5.5)]
    [TestCase("SYM(AAPL).rocabove(2)", 2)]
    [TestCase("SYM(AAPL).ROCABOVE(3)", 3)]
    [TestCase("SYM(AAPL).IsRocAbove(2)", 2)]
    public void Parse_RocAbove_AllSyntax(string script, double expectedThreshold)
    {
        var result = StrategyScriptParser.Parse(script);
        var rocSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRoc);
        Assert.That(rocSegment, Is.Not.Null);

        var conditionParam = rocSegment!.Parameters.FirstOrDefault(p => p.Name == "Condition");
        var thresholdParam = rocSegment.Parameters.FirstOrDefault(p => p.Name == "Threshold");

        Assert.That(conditionParam, Is.Not.Null);
        Assert.That(thresholdParam, Is.Not.Null);
        Assert.That(conditionParam!.Value, Is.EqualTo("Above"));
        Assert.That(Convert.ToDouble(thresholdParam!.Value), Is.EqualTo(expectedThreshold));
    }

    [TestCase("SYM(AAPL).RocBelow(-2)", -2)]
    [TestCase("SYM(AAPL).RocBelow(-5.5)", -5.5)]
    [TestCase("SYM(AAPL).rocbelow(-2)", -2)]
    [TestCase("SYM(AAPL).ROCBELOW(-3)", -3)]
    [TestCase("SYM(AAPL).IsRocBelow(-2)", -2)]
    public void Parse_RocBelow_AllSyntax(string script, double expectedThreshold)
    {
        var result = StrategyScriptParser.Parse(script);
        var rocSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRoc);
        Assert.That(rocSegment, Is.Not.Null);

        var conditionParam = rocSegment!.Parameters.FirstOrDefault(p => p.Name == "Condition");
        var thresholdParam = rocSegment.Parameters.FirstOrDefault(p => p.Name == "Threshold");

        Assert.That(conditionParam, Is.Not.Null);
        Assert.That(thresholdParam, Is.Not.Null);
        Assert.That(conditionParam!.Value, Is.EqualTo("Below"));
        Assert.That(Convert.ToDouble(thresholdParam!.Value), Is.EqualTo(expectedThreshold));
    }

    #endregion

    #region Momentum Combined with Other Indicators

    [Test]
    public void Parse_MomentumWithVwapAndEma_Combined()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).IsAboveVwap().EmaAbove(9).MomentumAbove(0).RocAbove(2)");

        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove);
        var momentumSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsMomentum);
        var rocSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRoc);

        Assert.That(vwapSegment, Is.Not.Null);
        Assert.That(emaSegment, Is.Not.Null);
        Assert.That(momentumSegment, Is.Not.Null);
        Assert.That(rocSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_FullStrategyWithMomentum()
    {
        var script = "Ticker(PLTR).Session(IS.PREMARKET).Qty(10).Entry(25).TakeProfit(28).TrailingStopLoss(IS.MODERATE).IsAboveVwap().EmaAbove(9).MomentumAbove(0)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaAbove), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsMomentum), Is.True);
    }

    #endregion

    // ========================================================================
    // CONTINUATION PATTERN TESTS
    // ========================================================================
    //
    // These indicators help detect bullish/bearish continuation patterns
    //
    // ========================================================================

    #region Higher Lows Tests

    [TestCase("SYM(AAPL).HigherLows()")]
    [TestCase("SYM(AAPL).HigherLows")]
    [TestCase("SYM(AAPL).IsHigherLows()")]
    [TestCase("SYM(AAPL).IsHigherLows")]
    [TestCase("SYM(AAPL).higherlows()")]
    [TestCase("SYM(AAPL).HIGHERLOWS()")]
    public void Parse_HigherLows_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsHigherLows);
        Assert.That(segment, Is.Not.Null);
    }

    #endregion

    #region EMA Turning Up Tests

    [TestCase("SYM(AAPL).EmaTurningUp(9)", 9)]
    [TestCase("SYM(AAPL).EmaTurningUp(21)", 21)]
    [TestCase("SYM(AAPL).IsEmaTurningUp(9)", 9)]
    [TestCase("SYM(AAPL).EMATURNINGUP(9)", 9)]
    [TestCase("SYM(AAPL).ematurningup(50)", 50)]
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

    #region Volume Above Tests

    [TestCase("SYM(AAPL).VolumeAbove(1.5)", 1.5)]
    [TestCase("SYM(AAPL).VolumeAbove(2)", 2)]
    [TestCase("SYM(AAPL).IsVolumeAbove(1.5)", 1.5)]
    [TestCase("SYM(AAPL).VOLUMEABOVE(3)", 3)]
    [TestCase("SYM(AAPL).volumeabove(2.5)", 2.5)]
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

    #region Close Above VWAP Tests

    [TestCase("SYM(AAPL).CloseAboveVwap()")]
    [TestCase("SYM(AAPL).CloseAboveVwap")]
    [TestCase("SYM(AAPL).IsCloseAboveVwap()")]
    [TestCase("SYM(AAPL).IsCloseAboveVwap")]
    [TestCase("SYM(AAPL).closeabovevwap()")]
    [TestCase("SYM(AAPL).CLOSEABOVEVWAP()")]
    public void Parse_CloseAboveVwap_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsCloseAboveVwap);
        Assert.That(segment, Is.Not.Null);
    }

    #endregion

    #region VWAP Rejection Tests

    [TestCase("SYM(AAPL).VwapRejection()")]
    [TestCase("SYM(AAPL).VwapRejection")]
    [TestCase("SYM(AAPL).IsVwapRejection()")]
    [TestCase("SYM(AAPL).IsVwapRejection")]
    [TestCase("SYM(AAPL).vwaprejection()")]
    [TestCase("SYM(AAPL).VWAPREJECTION()")]
    // Alias: VwapRejected
    [TestCase("SYM(AAPL).VwapRejected()")]
    [TestCase("SYM(AAPL).VwapRejected")]
    [TestCase("SYM(AAPL).IsVwapRejected()")]
    [TestCase("SYM(AAPL).IsVwapRejected")]
    public void Parse_VwapRejection_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsVwapRejection);
        Assert.That(segment, Is.Not.Null);
    }

    #endregion

    #region Combined Continuation Pattern Tests

    [Test]
    public void Parse_FullContinuationStrategy()
    {
        // A full bullish continuation setup
        var script = @"
            Ticker(NVDA)
            .Session(IS.PREMARKET)
            .AboveVwap()
            .CloseAboveVwap()
            .EmaTurningUp(9)
            .HigherLows()
            .VolumeAbove(1.5)
            .MomentumAbove(0)
            .Entry(150)
            .TakeProfit(160)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsCloseAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaTurningUp), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsHigherLows), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsVolumeAbove), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsMomentum), Is.True);
    }

    [Test]
    public void Parse_BearishRejectionStrategy()
    {
        // A bearish VWAP rejection setup
        var script = "Ticker(SPY).VwapRejected().EmaBelow(9)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("SPY"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsVwapRejection), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaBelow), Is.True);
    }

    #endregion
}


