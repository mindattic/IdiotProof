// ============================================================================
// StrategyScriptParser Integration Tests - Complete Strategy Scenarios
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Integration tests for complete strategy scenarios
/// Uses new IdiotScript syntax with period delimiters and IS. constants
/// </summary>
[TestFixture]
public class ScriptParserIntegrationTests
{
    #region Real-World Strategy Examples

    [Test]
    public void Parse_SimpleBreakoutStrategy()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(PLTR).QTY(10).OPEN(148.75).TP($158).TSL(15%).IsAboveVwap()");

        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.Price, Is.EqualTo(148.75).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(158.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));

        var vwapSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        Assert.That(vwapSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_PremarketMomentumStrategy()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(PLTR).NAME(\"Palantir Premarket Momentum\").QTY(10)." +
            "SESSION(PreMarketEndEarly).TP($153.50).SL($145.50).TSL(5%)." +
            "ExitStrategy(9:29).IsProfitable().BREAKOUT(148).PULLBACK(145).IsAboveVwap().IsEmaBetween(9, 21)");

        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        Assert.That(result.Name, Is.EqualTo("Palantir Premarket Momentum"));

        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(153.50).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(145.50).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.05).Within(0.01));

        // Verify session
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);

        // Verify close
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);

        // Verify condition chain
        var breakout = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        var pullback = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);
        var vwap = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        var emaBetween = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBetween);

        Assert.That(breakout, Is.Not.Null);
        Assert.That(pullback, Is.Not.Null);
        Assert.That(vwap, Is.Not.Null);
        Assert.That(emaBetween, Is.Not.Null);
    }

    [Test]
    public void Parse_SwingTradeStrategy()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(NVDA).NAME(\"NVDA Swing\").QTY(5)." +
            "OPEN(500).TP($550).SL($480)." +
            "IsAboveVwap().IsEmaAbove(21).IsEmaAbove(50).IsRsiOversold(40)");

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Name, Is.EqualTo("NVDA Swing"));

        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(5));
        Assert.That(stats.Price, Is.EqualTo(500).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(550).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(480).Within(0.01));
    }

    [Test]
    public void Parse_ScalpStrategy()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(SPY).QTY(100).TP($0.50).TSL(2%)." +
            "SESSION(RTH).ExitStrategy(IS.BELL).IsProfitable()." +
            "BREAKOUT().IsAboveVwap().IsAdxAbove(25)");

        Assert.That(result.Symbol, Is.EqualTo("SPY"));

        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(100));
        Assert.That(stats.TakeProfit, Is.EqualTo(0.50).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.02).Within(0.01));
    }

    #endregion

    #region Complete Order Format Tests

    [Test]
    public void Parse_NewFormat_ConditionsAtEnd()
    {
        // New format: SYM > NAME > QTY > SESSION > OPEN > TP > SL > TSL > CLOSE > CONDITIONS
        var result = StrategyScriptParser.Parse(
            "SYM(AAPL).NAME(\"Apple Trade\").QTY(10).SESSION(RTH)." +
            "TP($180).SL($170).TSL(5%).ExitStrategy(IS.BELL).IsProfitable()." +
            "BREAKOUT(175).IsAboveVwap().IsEmaAbove(9)");

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Name, Is.EqualTo("Apple Trade"));

        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(180.0).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(170.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.05).Within(0.01));

        // Verify conditions exist and are ordered
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

    #region Condition Permutation Tests

    [TestCase("SYM(AAPL).BREAKOUT(150)")]
    [TestCase("SYM(AAPL).PULLBACK(145)")]
    [TestCase("SYM(AAPL).ABOVE_VWAP")]
    [TestCase("SYM(AAPL).BELOW_VWAP")]
    [TestCase("SYM(AAPL).ABOVE_EMA(9)")]
    [TestCase("SYM(AAPL).BELOW_EMA(9)")]
    [TestCase("SYM(AAPL).BETWEEN_EMA(9, 21)")]
    [TestCase("SYM(AAPL).RSI_OVERSOLD(30)")]
    [TestCase("SYM(AAPL).RSI_OVERBOUGHT(70)")]
    [TestCase("SYM(AAPL).ADX_ABOVE(25)")]
    public void Parse_SingleCondition_AllTypes(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments.Count, Is.GreaterThan(0));
    }

    [TestCase("SYM(AAPL).BREAKOUT(150).ABOVE_VWAP")]
    [TestCase("SYM(AAPL).PULLBACK(145).BELOW_VWAP")]
    [TestCase("SYM(AAPL).BREAKOUT(150).PULLBACK(145)")]
    [TestCase("SYM(AAPL).ABOVE_VWAP.ABOVE_EMA(9)")]
    [TestCase("SYM(AAPL).BREAKOUT(150).ABOVE_VWAP.ABOVE_EMA(9)")]
    [TestCase("SYM(AAPL).BREAKOUT(150).PULLBACK(145).ABOVE_VWAP")]
    [TestCase("SYM(AAPL).BREAKOUT(150).PULLBACK(145).ABOVE_VWAP.ABOVE_EMA(9)")]
    [TestCase("SYM(AAPL).BREAKOUT(150).PULLBACK(145).ABOVE_VWAP.BETWEEN_EMA(9, 21)")]
    public void Parse_ConditionChains_AllCombinations(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));

        // Count conditions (excluding Buy/Sell segments)
        var conditionCount = result.Segments.Count(s => 
            s.Type == SegmentType.Breakout ||
            s.Type == SegmentType.Pullback ||
            s.Type == SegmentType.IsAboveVwap ||
            s.Type == SegmentType.IsBelowVwap ||
            s.Type == SegmentType.IsEmaAbove ||
            s.Type == SegmentType.IsEmaBelow ||
            s.Type == SegmentType.IsEmaBetween);

        // Script has at least 2 conditions (chained with .)
        Assert.That(conditionCount, Is.GreaterThanOrEqualTo(2));
    }

    #endregion

    #region Order Permutation Tests

    [Test]
    public void Parse_AllOrderParameters_AnyOrder()
    {
        // Order of commands shouldn't matter (except condition chain at end)
        var scripts = new[]
        {
            "SYM(AAPL).QTY(10).TP($160).SL($150).TSL(5%)",
            "QTY(10).SYM(AAPL).TSL(5%).TP($160).SL($150)",
            "TP($160).SL($150).SYM(AAPL).QTY(10).TSL(5%)",
            "TSL(5%).QTY(10).SL($150).TP($160).SYM(AAPL)"
        };

        foreach (var script in scripts)
        {
            var result = StrategyScriptParser.Parse(script);
            Assert.That(result.Symbol, Is.EqualTo("AAPL"));
            var stats = result.GetStats();
            Assert.That(stats.Quantity, Is.EqualTo(10));
            Assert.That(stats.TakeProfit, Is.EqualTo(160.0).Within(0.01));
            Assert.That(stats.StopLoss, Is.EqualTo(150.0).Within(0.01));
            Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.05).Within(0.01));
        }
    }

    #endregion

    #region Boundary Tests

    [TestCase("SYM(A)")]
    [TestCase("SYM(ABCDEFGHIJ)")]
    [TestCase("SYM(ABC123)")]
    public void Parse_SymbolBoundaries(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).QTY(1)", 1)]
    [TestCase("SYM(AAPL).QTY(999999)", 999999)]
    public void Parse_QuantityBoundaries(string script, int expectedQty)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(expectedQty));
    }

    [TestCase("SYM(AAPL).TP($0.01)", 0.01)]
    [TestCase("SYM(AAPL).TP($9999.99)", 9999.99)]
    public void Parse_PriceBoundaries(string script, double expectedTp)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TakeProfit, Is.EqualTo(expectedTp).Within(0.01));
    }

    [TestCase("SYM(AAPL).TSL(5%)", 0.05)]
    [TestCase("SYM(AAPL).TSL(99%)", 0.99)]
    public void Parse_TSLBoundaries(string script, double expectedTsl)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expectedTsl).Within(0.01));
    }

    #endregion
}


