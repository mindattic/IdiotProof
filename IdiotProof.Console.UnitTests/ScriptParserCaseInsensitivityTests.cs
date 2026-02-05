// ============================================================================
// StrategyScriptParser Case Insensitivity Tests - Comprehensive
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Comprehensive tests for case insensitivity of all commands
/// Uses new IdiotScript syntax with period delimiters
/// </summary>
[TestFixture]
public class ScriptParserCaseInsensitivityTests
{
    #region All Commands Case Variations

    [TestCase("SYM(AAPL)", "AAPL")]
    [TestCase("sym(AAPL)", "AAPL")]
    [TestCase("Sym(AAPL)", "AAPL")]
    [TestCase("sYm(AAPL)", "AAPL")]
    [TestCase("SYM(aapl)", "AAPL")]
    [TestCase("sym(aapl)", "AAPL")]
    [TestCase("TICKER(AAPL)", "AAPL")]
    [TestCase("ticker(AAPL)", "AAPL")]
    [TestCase("Ticker(AAPL)", "AAPL")]
    public void Parse_Symbol_AllCaseVariations(string script, string expected)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo(expected));
    }

    [TestCase("SYM(AAPL).QTY(10)", 10)]
    [TestCase("SYM(AAPL).qty(10)", 10)]
    [TestCase("SYM(AAPL).Qty(10)", 10)]
    [TestCase("SYM(AAPL).qTy(10)", 10)]
    public void Parse_Quantity_AllCaseVariations(string script, int expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(expected));
    }

    #endregion

    #region VWAP Case Variations

    [TestCase("SYM(AAPL).ABOVE_VWAP")]
    [TestCase("SYM(AAPL).above_vwap")]
    [TestCase("SYM(AAPL).Above_Vwap")]
    [TestCase("SYM(AAPL).ABOVE_vwap")]
    [TestCase("SYM(AAPL).above_VWAP")]
    [TestCase("SYM(AAPL).AbOvE_VwAp")]
    [TestCase("SYM(AAPL).VWAP")]
    [TestCase("SYM(AAPL).vwap")]
    [TestCase("SYM(AAPL).Vwap")]
    public void Parse_AboveVwap_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).BELOW_VWAP")]
    [TestCase("SYM(AAPL).below_vwap")]
    [TestCase("SYM(AAPL).Below_Vwap")]
    public void Parse_BelowVwap_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsBelowVwap);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    #endregion

    #region EMA Case Variations

    [TestCase("SYM(AAPL).ABOVE_EMA(9)")]
    [TestCase("SYM(AAPL).above_ema(9)")]
    [TestCase("SYM(AAPL).Above_Ema(9)")]
    public void Parse_AboveEma_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove);
        Assert.That(emaSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).BELOW_EMA(9)")]
    [TestCase("SYM(AAPL).below_ema(9)")]
    [TestCase("SYM(AAPL).Below_Ema(9)")]
    public void Parse_BelowEma_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBelow);
        Assert.That(emaSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).BETWEEN_EMA(9, 21)")]
    [TestCase("SYM(AAPL).between_ema(9, 21)")]
    [TestCase("SYM(AAPL).Between_Ema(9, 21)")]
    public void Parse_EmaBetween_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBetween);
        Assert.That(emaSegment, Is.Not.Null);
    }

    #endregion

    #region RSI Case Variations

    [TestCase("SYM(AAPL).RSI_OVERSOLD(30)")]
    [TestCase("SYM(AAPL).rsi_oversold(30)")]
    [TestCase("SYM(AAPL).Rsi_Oversold(30)")]
    public void Parse_RsiOversold_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var rsiSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRsi);
        Assert.That(rsiSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).RSI_OVERBOUGHT(70)")]
    [TestCase("SYM(AAPL).rsi_overbought(70)")]
    [TestCase("SYM(AAPL).Rsi_Overbought(70)")]
    public void Parse_RsiOverbought_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var rsiSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRsi);
        Assert.That(rsiSegment, Is.Not.Null);
    }

    #endregion

    #region ADX Case Variations

    [TestCase("SYM(AAPL).ADX_ABOVE(25)")]
    [TestCase("SYM(AAPL).adx_above(25)")]
    [TestCase("SYM(AAPL).Adx_Above(25)")]
    public void Parse_Adx_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var adxSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAdx);
        Assert.That(adxSegment, Is.Not.Null);
    }

    #endregion

    #region Breakout/Pullback Case Variations

    [TestCase("SYM(AAPL).BREAKOUT(150)")]
    [TestCase("SYM(AAPL).breakout(150)")]
    [TestCase("SYM(AAPL).Breakout(150)")]
    [TestCase("SYM(AAPL).BreakOut(150)")]
    public void Parse_Breakout_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var breakoutSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        Assert.That(breakoutSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).PULLBACK(145)")]
    [TestCase("SYM(AAPL).pullback(145)")]
    [TestCase("SYM(AAPL).Pullback(145)")]
    [TestCase("SYM(AAPL).PullBack(145)")]
    public void Parse_Pullback_AllCaseVariations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var pullbackSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);
        Assert.That(pullbackSegment, Is.Not.Null);
    }

    #endregion

    #region Price Command Case Variations

    [TestCase("SYM(AAPL).TP(160)", 160.0)]
    [TestCase("SYM(AAPL).tp(160)", 160.0)]
    [TestCase("SYM(AAPL).Tp(160)", 160.0)]
    [TestCase("SYM(AAPL).tP(160)", 160.0)]
    public void Parse_TakeProfit_AllCaseVariations(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TakeProfit, Is.EqualTo(expected).Within(0.01));
    }

    [TestCase("SYM(AAPL).SL(140)", 140.0)]
    [TestCase("SYM(AAPL).sl(140)", 140.0)]
    [TestCase("SYM(AAPL).Sl(140)", 140.0)]
    [TestCase("SYM(AAPL).sL(140)", 140.0)]
    public void Parse_StopLoss_AllCaseVariations(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.StopLoss, Is.EqualTo(expected).Within(0.01));
    }

    [TestCase("SYM(AAPL).TSL(15%)", 0.15)]
    [TestCase("SYM(AAPL).tsl(15%)", 0.15)]
    [TestCase("SYM(AAPL).Tsl(15%)", 0.15)]
    [TestCase("SYM(AAPL).tSl(15%)", 0.15)]
    public void Parse_TrailingStopLoss_AllCaseVariations(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expected).Within(0.01));
    }

    #endregion

    #region Mixed Case Full Strategy

    [TestCase("SYM(aapl).qty(10).tp($160).tsl(15%).above_vwap.above_ema(9)")]
    [TestCase("SYM(AAPL).QTY(10).TP($160).TSL(15%).ABOVE_VWAP.ABOVE_EMA(9)")]
    [TestCase("Sym(Aapl).Qty(10).Tp($160).Tsl(15%).Above_Vwap.Above_Ema(9)")]
    [TestCase("sYm(aApL).qTy(10).Tp($160).tSl(15%).AbOvE_vWaP.AbOvE_EmA(9)")]
    public void Parse_FullStrategy_MixedCase(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));

        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(160.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));

        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove);

        Assert.That(vwapSegment, Is.Not.Null);
        Assert.That(emaSegment, Is.Not.Null);
    }

    #endregion
}


