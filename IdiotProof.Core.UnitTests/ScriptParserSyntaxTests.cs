// ============================================================================
// StrategyScriptParser Syntax Tests - Period delimiter and IS. constants
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Enums;
using IdiotProof.Core.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for IdiotScript syntax with period (.) delimiter and IS. constants.
/// </summary>
[TestFixture]
public class ScriptParserSyntaxTests
{
    #region Period Delimiter Tests

    [TestCase("TICKER(AAPL)")]
    [TestCase("SYM(AAPL)")]
    [TestCase("SYMBOL(AAPL)")]
    public void Parse_NewSymbolSyntax_Works(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [TestCase("STOCK.TICKER(NVDA)")]
    [TestCase("STOCK.SYMBOL(NVDA)")]
    [TestCase("stock.ticker(nvda)")]
    public void Parse_StockPrefixSyntax_Works(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
    }

    [TestCase("TICKER(AAPL).QTY(100)", 100)]
    [TestCase("TICKER(AAPL).QTY(50)", 50)]
    [TestCase("TICKER(AAPL).QTY(1)", 1)]
    public void Parse_PeriodDelimiter_Basic(string script, int expectedQty)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(stats.Quantity, Is.EqualTo(expectedQty));
    }

    [Test]
    public void Parse_PeriodDelimiter_CompleteScript()
    {
        var script = "TICKER(NVDA).SESSION(IS.PREMARKET).QTY(10).OPEN(200).TP(210).SL(190).TSL(10)";
        var result = StrategyScriptParser.Parse(script);
        
        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
        
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(210));
        Assert.That(stats.StopLoss, Is.EqualTo(190));
    }

    #endregion

    #region IS. Constants Tests

    [TestCase("TICKER(AAPL).SESSION(IS.PREMARKET)")]
    [TestCase("TICKER(AAPL).SESSION(is.premarket)")]
    [TestCase("TICKER(AAPL).SESSION(IS.RTH)")]
    [TestCase("TICKER(AAPL).SESSION(IS.AFTERHOURS)")]
    [TestCase("TICKER(AAPL).SESSION(IS.EXTENDED)")]
    public void Parse_ISConstants_Session(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).CLOSE(IS.BELL)")]
    [TestCase("TICKER(AAPL).CLOSE(IS.PREMARKET.BELL)")]
    [TestCase("TICKER(AAPL).CLOSE(IS.OPEN)")]
    [TestCase("TICKER(AAPL).CLOSE(IS.CLOSE)")]
    public void Parse_ISConstants_CloseTime(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).TSL(IS.TIGHT)", 0.05)]
    [TestCase("TICKER(AAPL).TSL(IS.MODERATE)", 0.10)]
    [TestCase("TICKER(AAPL).TSL(IS.STANDARD)", 0.15)]
    [TestCase("TICKER(AAPL).TSL(IS.LOOSE)", 0.20)]
    [TestCase("TICKER(AAPL).TSL(IS.WIDE)", 0.25)]
    public void Parse_ISConstants_TrailingStopLoss(string script, double expectedPercent)
    {
        var result = StrategyScriptParser.Parse(script);
        var tslSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.TrailingStopLoss);
        Assert.That(tslSegment, Is.Not.Null);
        
        var percentParam = tslSegment!.Parameters.FirstOrDefault(p => p.Name == "Percentage");
        Assert.That(percentParam, Is.Not.Null);
        Assert.That(Convert.ToDouble(percentParam!.Value), Is.EqualTo(expectedPercent).Within(0.01));
    }

    #endregion

    #region Condition Tests (New Syntax)

    [Test]
    public void Parse_NewConditionSyntax_AboveVwap()
    {
        var script = "TICKER(AAPL).ABOVE_VWAP";
        var result = StrategyScriptParser.Parse(script);
        
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_NewConditionSyntax_BelowVwap()
    {
        var script = "TICKER(AAPL).BELOW_VWAP";
        var result = StrategyScriptParser.Parse(script);
        
        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsBelowVwap);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).ABOVE_EMA(9)", 9)]
    [TestCase("TICKER(AAPL).ABOVE_EMA(21)", 21)]
    [TestCase("TICKER(AAPL).ABOVE_EMA(200)", 200)]
    public void Parse_NewConditionSyntax_AboveEma(string script, int expectedPeriod)
    {
        var result = StrategyScriptParser.Parse(script);
        
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove);
        Assert.That(emaSegment, Is.Not.Null);
        
        var periodParam = emaSegment!.Parameters.FirstOrDefault(p => p.Name == "Period");
        Assert.That(periodParam, Is.Not.Null);
        Assert.That(Convert.ToInt32(periodParam!.Value), Is.EqualTo(expectedPeriod));
    }

    [TestCase("TICKER(AAPL).BELOW_EMA(9)", 9)]
    [TestCase("TICKER(AAPL).BELOW_EMA(50)", 50)]
    public void Parse_NewConditionSyntax_BelowEma(string script, int expectedPeriod)
    {
        var result = StrategyScriptParser.Parse(script);
        
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBelow);
        Assert.That(emaSegment, Is.Not.Null);
        
        var periodParam = emaSegment!.Parameters.FirstOrDefault(p => p.Name == "Period");
        Assert.That(periodParam, Is.Not.Null);
        Assert.That(Convert.ToInt32(periodParam!.Value), Is.EqualTo(expectedPeriod));
    }

    [Test]
    public void Parse_NewConditionSyntax_BetweenEma()
    {
        var script = "TICKER(AAPL).BETWEEN_EMA(9, 21)";
        var result = StrategyScriptParser.Parse(script);
        
        var emaSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBetween);
        Assert.That(emaSegment, Is.Not.Null);
        
        var lowerParam = emaSegment!.Parameters.FirstOrDefault(p => p.Name == "LowerPeriod");
        var upperParam = emaSegment.Parameters.FirstOrDefault(p => p.Name == "UpperPeriod");
        Assert.That(lowerParam, Is.Not.Null);
        Assert.That(upperParam, Is.Not.Null);
        Assert.That(Convert.ToInt32(lowerParam!.Value), Is.EqualTo(9));
        Assert.That(Convert.ToInt32(upperParam!.Value), Is.EqualTo(21));
    }

    [Test]
    public void Parse_NewConditionSyntax_RsiOversold()
    {
        var script = "TICKER(AAPL).RSI_OVERSOLD(30)";
        var result = StrategyScriptParser.Parse(script);
        
        var rsiSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRsi);
        Assert.That(rsiSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_NewConditionSyntax_RsiOverbought()
    {
        var script = "TICKER(AAPL).RSI_OVERBOUGHT(70)";
        var result = StrategyScriptParser.Parse(script);
        
        var rsiSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRsi);
        Assert.That(rsiSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_NewConditionSyntax_AdxAbove()
    {
        var script = "TICKER(AAPL).ADX_ABOVE(25)";
        var result = StrategyScriptParser.Parse(script);
        
        var adxSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAdx);
        Assert.That(adxSegment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).BREAKOUT()")]
    [TestCase("TICKER(AAPL).BREAKOUT(150)")]
    public void Parse_NewConditionSyntax_Breakout(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        
        var breakoutSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        Assert.That(breakoutSegment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).PULLBACK()")]
    [TestCase("TICKER(AAPL).PULLBACK(148)")]
    public void Parse_NewConditionSyntax_Pullback(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        
        var pullbackSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);
        Assert.That(pullbackSegment, Is.Not.Null);
    }

    #endregion

    #region Complete Example Tests

    [Test]
    public void Parse_FullExampleScript_Works()
    {
        // The example from the documentation
        var script = "TICKER(NVDA).SESSION(IS.PREMARKET).CLOSE(IS.PREMARKET.BELL).QTY(1).OPEN(200).TP(201).SL(190).TSL(10).BREAKOUT().PULLBACK().ABOVE_VWAP.BETWEEN_EMA(9, 21).ABOVE_EMA(200)";
        
        var result = StrategyScriptParser.Parse(script);
        
        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        
        // Verify session
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
        
        // Verify close
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);
        
        // Verify conditions
        Assert.That(result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout), Is.Not.Null);
        Assert.That(result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback), Is.Not.Null);
        Assert.That(result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap), Is.Not.Null);
        Assert.That(result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBetween), Is.Not.Null);
        Assert.That(result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove), Is.Not.Null);
        
        // Verify risk management
        Assert.That(result.Segments.FirstOrDefault(s => s.Type == SegmentType.TakeProfit), Is.Not.Null);
        Assert.That(result.Segments.FirstOrDefault(s => s.Type == SegmentType.StopLoss), Is.Not.Null);
        Assert.That(result.Segments.FirstOrDefault(s => s.Type == SegmentType.TrailingStopLoss), Is.Not.Null);
    }

    [Test]
    public void Parse_MixedValidPrefixes_Works()
    {
        // All valid symbol declarations
        var scripts = new[]
        {
            "TICKER(AAPL)",
            "SYM(AAPL)",
            "SYMBOL(AAPL)",
            "STOCK.TICKER(AAPL)",
            "STOCK.SYMBOL(AAPL)"
        };

        foreach (var script in scripts)
        {
            var result = StrategyScriptParser.Parse(script);
            Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        }
    }

    #endregion
}


