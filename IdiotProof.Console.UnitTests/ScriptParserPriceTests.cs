// ============================================================================
// StrategyScriptParser Price Condition Tests - OPEN, TP, SL, TSL
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for price-related script commands: OPEN, ENTRY, TP, SL, TSL
/// Uses new IdiotScript syntax with period delimiters
/// </summary>
[TestFixture]
public class ScriptParserPriceTests
{
    #region Entry Price Tests

    [TestCase("SYM(AAPL).OPEN(150)", 150.0)]
    [TestCase("SYM(AAPL).OPEN(150.50)", 150.50)]
    [TestCase("SYM(AAPL).OPEN($150)", 150.0)]
    [TestCase("SYM(AAPL).OPEN($150.75)", 150.75)]
    [TestCase("SYM(AAPL).open(100)", 100.0)]
    [TestCase("SYM(AAPL).Open($200.25)", 200.25)]
    public void Parse_OpenPrice_Values(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(expected).Within(0.01));
    }

    [TestCase("SYM(AAPL).ENTRY(150)", 150.0)]
    [TestCase("SYM(AAPL).ENTRY($150.50)", 150.50)]
    [TestCase("SYM(AAPL).entry(200)", 200.0)]
    public void Parse_EntryPrice_Alias(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(expected).Within(0.01));
    }

    #endregion

    #region Take Profit Tests

    [TestCase("SYM(AAPL).TP(160)", 160.0)]
    [TestCase("SYM(AAPL).TP(160.50)", 160.50)]
    [TestCase("SYM(AAPL).TP($160)", 160.0)]
    [TestCase("SYM(AAPL).TP($160.75)", 160.75)]
    [TestCase("SYM(AAPL).tp(150)", 150.0)]
    [TestCase("SYM(AAPL).Tp($200.25)", 200.25)]
    public void Parse_TakeProfit_Values(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TakeProfit, Is.EqualTo(expected).Within(0.01));
    }

    #endregion

    #region Stop Loss Tests

    [TestCase("SYM(AAPL).SL(140)", 140.0)]
    [TestCase("SYM(AAPL).SL(140.50)", 140.50)]
    [TestCase("SYM(AAPL).SL($140)", 140.0)]
    [TestCase("SYM(AAPL).SL($140.25)", 140.25)]
    [TestCase("SYM(AAPL).sl(130)", 130.0)]
    [TestCase("SYM(AAPL).Sl($145.50)", 145.50)]
    public void Parse_StopLoss_Values(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.StopLoss, Is.EqualTo(expected).Within(0.01));
    }

    #endregion

    #region Trailing Stop Loss Tests

    [TestCase("SYM(AAPL).TSL(15%)", 0.15)]
    [TestCase("SYM(AAPL).TSL(10%)", 0.10)]
    [TestCase("SYM(AAPL).TSL(5%)", 0.05)]
    [TestCase("SYM(AAPL).TSL(20%)", 0.20)]
    [TestCase("SYM(AAPL).tsl(15%)", 0.15)]
    [TestCase("SYM(AAPL).Tsl(10%)", 0.10)]
    public void Parse_TrailingStopLoss_Percentage(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expected).Within(0.01));
    }

    [TestCase("SYM(AAPL).TSL(0.15)", 0.15)]
    [TestCase("SYM(AAPL).TSL(0.10)", 0.10)]
    [TestCase("SYM(AAPL).TSL(0.05)", 0.05)]
    public void Parse_TrailingStopLoss_Decimal(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expected).Within(0.01));
    }

    [TestCase("SYM(AAPL).TSL(15)", 0.15)]
    [TestCase("SYM(AAPL).TSL(10)", 0.10)]
    [TestCase("SYM(AAPL).TSL(5)", 0.05)]
    public void Parse_TrailingStopLoss_WholeNumber_ConvertedToPercent(string script, double expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expected).Within(0.01));
    }

    #endregion

    #region Combined Price Tests

    [TestCase("SYM(AAPL).OPEN(150).TP(160).SL(140)", 150.0, 160.0, 140.0)]
    [TestCase("SYM(AAPL).OPEN($148.75).TP($158).SL($145)", 148.75, 158.0, 145.0)]
    public void Parse_AllPriceConditions_Combined(string script, double open, double tp, double sl)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(open).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(tp).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(sl).Within(0.01));
    }

    [Test]
    public void Parse_OpenTPandTSL_Combined()
    {
        var result = StrategyScriptParser.Parse("SYM(PLTR).QTY(10).OPEN(148.75).TP($158).TSL(15%)");
        var stats = result.GetStats();
        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.Price, Is.EqualTo(148.75).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(158.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));
    }

    #endregion
}
