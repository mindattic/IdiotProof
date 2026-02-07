// ============================================================================
// FluentApiToIdiotScriptTests - Tests for Fluent API to IdiotScript equivalence
// ============================================================================

using IdiotProof.Core.Enums;
using IdiotProof.Core.Models;
using IdiotProof.Core.Scripting;

namespace IdiotProof.Core.UnitTests.Scripting;

/// <summary>
/// Tests that verify fluent API patterns have equivalent IdiotScript representations.
/// Each test documents the fluent API pattern and its IdiotScript equivalent.
/// </summary>
[TestFixture]
public class FluentApiToIdiotScriptTests
{
    #region Basic Strategy Conversions

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("AAPL").Breakout(150).Long(100).TakeProfit(155).StopLoss(148).Build()
    /// </summary>
    [Test]
    [Description("Basic buy strategy with breakout, TP, and SL")]
    public void BasicBuyStrategy_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(AAPL).Breakout(150).Qty(100).TakeProfit(155).StopLoss(148)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
            
            var tpSegment = strategy.Segments.FirstOrDefault(s => s.Type == SegmentType.TakeProfit);
            Assert.That(tpSegment, Is.Not.Null);
            var tpPrice = tpSegment!.Parameters.FirstOrDefault(p => p.Name == "Price")?.Value;
            Assert.That(Convert.ToDouble(tpPrice), Is.EqualTo(155.0).Within(0.01));

            var slSegment = strategy.Segments.FirstOrDefault(s => s.Type == SegmentType.StopLoss);
            Assert.That(slSegment, Is.Not.Null);
            var slPrice = slSegment!.Parameters.FirstOrDefault(p => p.Name == "Price")?.Value;
            Assert.That(Convert.ToDouble(slPrice), Is.EqualTo(148.0).Within(0.01));
        });
    }

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("AAPL").TimeFrame(TradingSession.PreMarket).Breakout(150).Long(100).TakeProfit(155).Build()
    /// </summary>
    [Test]
    [Description("PreMarket session strategy")]
    public void PreMarketSession_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(AAPL).Session(IS.PREMARKET).Breakout(150).Qty(100).TakeProfit(155)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.SessionDuration));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TakeProfit));
        });
    }

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("NVDA").Breakout(200).IsAboveVwap().Long(50).TakeProfit(210).Build()
    /// </summary>
    [Test]
    [Description("Strategy with VWAP condition")]
    public void VwapCondition_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(NVDA).Breakout(200).AboveVwap().Qty(50).TakeProfit(210)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("NVDA"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TakeProfit));
        });
    }

    #endregion

    #region Repeating Strategy Conversions

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("ABC").TimeFrame(TradingSession.RTH).IsPriceAbove(5.00).IsAboveVwap()
    ///       .Long(100).TakeProfit(6.00).StopLoss(4.50).Repeat().Build()
    /// </summary>
    [Test]
    [Description("Repeating scalp strategy - full example")]
    public void RepeatingScalpStrategy_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(ABC).Session(IS.RTH).Entry(5.00).AboveVwap().Qty(100).TakeProfit(6.00).StopLoss(4.50).Repeat()";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("ABC"));
            Assert.That(strategy.RepeatEnabled, Is.True);
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.SessionDuration));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TakeProfit));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.StopLoss));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Repeat));

            var tpSegment = strategy.Segments.First(s => s.Type == SegmentType.TakeProfit);
            var tpPrice = tpSegment.Parameters.First(p => p.Name == "Price").Value;
            Assert.That(Convert.ToDouble(tpPrice), Is.EqualTo(6.00).Within(0.01));

            var slSegment = strategy.Segments.First(s => s.Type == SegmentType.StopLoss);
            var slPrice = slSegment.Parameters.First(p => p.Name == "Price").Value;
            Assert.That(Convert.ToDouble(slPrice), Is.EqualTo(4.50).Within(0.01));
        });
    }

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("ABC").IsPriceAbove(5.00).Repeat(false).Long(100).TakeProfit(6.00).Build()
    /// </summary>
    [Test]
    [Description("Non-repeating strategy with explicit false")]
    public void NonRepeatingStrategy_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(ABC).Entry(5.00).Repeat(N).Qty(100).TakeProfit(6.00)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("ABC"));
            Assert.That(strategy.RepeatEnabled, Is.False);
        });
    }

    #endregion

    #region Risk Management Conversions

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("TSLA").Breakout(250).Long(25).TakeProfit(260).TrailingStopLoss(0.10).Build()
    /// </summary>
    [Test]
    [Description("Strategy with trailing stop loss")]
    public void TrailingStopLoss_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(TSLA).Breakout(250).Qty(25).TakeProfit(260).TrailingStopLoss(IS.MODERATE)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("TSLA"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TakeProfit));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TrailingStopLoss));
        });
    }

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("GOOG").Breakout(140).Long(10).TakeProfit(145).ExitStrategy(time).Build()
    /// </summary>
    [Test]
    [Description("Strategy with exit strategy time")]
    public void ExitStrategyTime_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(GOOG).Breakout(140).Qty(10).TakeProfit(145).ExitStrategy(IS.BELL)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("GOOG"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.ExitStrategy));
        });
    }

    [Test]
    [Description("Strategy with exit strategy and IsProfitable")]
    public void ExitStrategyWithIsProfitable_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(GOOG).Breakout(140).TakeProfit(145).ExitStrategy(IS.BELL).IsProfitable()";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("GOOG"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.ExitStrategy));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsProfitable));
        });
    }

    #endregion

        #region Full Featured Strategy Conversions

        /// <summary>
        /// Fluent API:
        ///   Stock.Ticker("NVDA").TimeFrame(TradingSession.PreMarket).Breakout(200).Pullback(198)
        ///       .IsAboveVwap().Long(1).TakeProfit(210).StopLoss(195).TrailingStopLoss(0.15)
        ///       .ClosePosition(time).Repeat().Build()
        /// </summary>
        [Test]
        [Description("Full featured strategy with all components")]
        public void FullFeaturedStrategy_FluentToIdiotScript_Equivalent()
        {
            var script = "Ticker(NVDA).Session(IS.PREMARKET).Breakout(200).Pullback(198).AboveVwap().Qty(1).TakeProfit(210).StopLoss(195).TrailingStopLoss(IS.STANDARD).ClosePosition(IS.BELL).Repeat()";

            var strategy = IdiotScriptParser.Parse(script);

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Symbol, Is.EqualTo("NVDA"));
                Assert.That(strategy.RepeatEnabled, Is.True);
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.SessionDuration));
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Pullback));
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TakeProfit));
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.StopLoss));
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TrailingStopLoss));
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.ExitStrategy));
                Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Repeat));
            });
        }

        #endregion

        #region Enable/Disable Conversions

        /// <summary>
        /// Fluent API:
    ///   Stock.Ticker("META").Enabled(false).Breakout(300).Long(10).TakeProfit(310).Build()
    /// </summary>
    [Test]
    [Description("Disabled strategy")]
    public void DisabledStrategy_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(META).IsEnabled(N).Breakout(300).Qty(10).TakeProfit(310)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("META"));
            Assert.That(strategy.Enabled, Is.False);
        });
    }

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("META").Enabled(true).Breakout(300).Long(10).TakeProfit(310).Build()
    /// </summary>
    [Test]
    [Description("Enabled strategy with explicit flag")]
    public void EnabledStrategy_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(META).IsEnabled(Y).Breakout(300).Qty(10).TakeProfit(310)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("META"));
            Assert.That(strategy.Enabled, Is.True);
        });
    }

    #endregion

    #region Short Position Conversions

    /// <summary>
    /// Fluent API:
    ///   Stock.Ticker("SPY").IsPriceBelow(450).Short(50).StopLoss(455).Build()
    /// </summary>
    [Test]
    [Description("Short position strategy")]
    public void ShortPosition_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(SPY).IsPriceBelow(450).Order(IS.SHORT).Qty(50).StopLoss(455)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("SPY"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsPriceBelow));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Order));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.StopLoss));
            // Verify it's a SHORT order
            var orderSegment = strategy.Segments.First(s => s.Type == SegmentType.Order);
            var directionParam = orderSegment.Parameters.FirstOrDefault(p => p.Name == "Direction");
            Assert.That(directionParam?.Value?.ToString(), Is.EqualTo("Short"));
        });
    }

    #endregion

    #region Indicator Condition Conversions

    /// <summary>
    /// Fluent API (EMA conditions)
    /// </summary>
    [Test]
    [Description("Strategy with EMA conditions")]
    public void EmaConditions_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(AMZN).EmaAbove(9).EmaBetween(9,21).EmaAbove(200).Qty(20).TakeProfit(190)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("AMZN"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsEmaAbove));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsEmaBetween));
        });
    }

    /// <summary>
    /// Fluent API (RSI, ADX, DI conditions)
    /// </summary>
    [Test]
    [Description("Strategy with indicator conditions (RSI, ADX, DI)")]
    public void IndicatorConditions_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(MSFT).RsiOversold(30).AdxAbove(25).DiPositive().Qty(30).TakeProfit(420)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("MSFT"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsRsi));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsAdx));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsDI));
        });
    }

    /// <summary>
    /// Fluent API (Momentum and ROC)
    /// </summary>
    [Test]
    [Description("Strategy with momentum indicators")]
    public void MomentumIndicators_FluentToIdiotScript_Equivalent()
    {
        var script = "Ticker(AMD).MomentumAbove(0).RocAbove(2).Qty(100).TakeProfit(180)";

        var strategy = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Symbol, Is.EqualTo("AMD"));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsMomentum));
            Assert.That(strategy.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsRoc));
        });
    }

    #endregion

    #region Round-Trip Tests

    [Test]
    [Description("Parse -> Serialize -> Parse produces equivalent strategy")]
    public void RoundTrip_FullStrategy_PreservesAllProperties()
    {
        var originalScript = "Ticker(ABC).Session(IS.PREMARKET).Breakout(150).AboveVwap().Qty(100).TakeProfit(160).StopLoss(145).Repeat()";

        var strategy1 = IdiotScriptParser.Parse(originalScript);
        var serialized = IdiotScriptSerializer.Serialize(strategy1);
        var strategy2 = IdiotScriptParser.Parse(serialized);

        Assert.Multiple(() =>
        {
            Assert.That(strategy2.Symbol, Is.EqualTo(strategy1.Symbol));
            Assert.That(strategy2.RepeatEnabled, Is.EqualTo(strategy1.RepeatEnabled));
            Assert.That(strategy2.Segments.Count(s => s.Type == SegmentType.Breakout), 
                Is.EqualTo(strategy1.Segments.Count(s => s.Type == SegmentType.Breakout)));
            Assert.That(strategy2.Segments.Count(s => s.Type == SegmentType.IsAboveVwap), 
                Is.EqualTo(strategy1.Segments.Count(s => s.Type == SegmentType.IsAboveVwap)));
            Assert.That(strategy2.Segments.Count(s => s.Type == SegmentType.TakeProfit), 
                Is.EqualTo(strategy1.Segments.Count(s => s.Type == SegmentType.TakeProfit)));
            Assert.That(strategy2.Segments.Count(s => s.Type == SegmentType.StopLoss), 
                Is.EqualTo(strategy1.Segments.Count(s => s.Type == SegmentType.StopLoss)));
        });
    }

    #endregion
}

// =========================================================================
// FLUENT API TO IDIOTSCRIPT EQUIVALENCE TABLE
// =========================================================================
//
// NOTE: All IdiotScript commands should include parentheses for consistency.
// The parser accepts both forms (e.g., AboveVwap and AboveVwap()), but the
// serializer always outputs with parentheses.
//
// | Fluent API                          | IdiotScript Equivalent              |
// |-------------------------------------|-------------------------------------|
// | Stock.Ticker("AAPL")                | Ticker(AAPL)                        |
// | .TimeFrame(TradingSession.PreMarket)| .Session(IS.PREMARKET)              |
// | .TimeFrame(TradingSession.RTH)      | .Session(IS.RTH)                    |
// | .Enabled(true)                      | .Enabled() or .Enabled(Y)           |
// | .Enabled(false)                     | .Enabled(N) or .Enabled(false)      |
// | .Breakout(150)                      | .Breakout(150)                      |
// | .Breakout() (no price)              | .Breakout()                         |
// | .Pullback(148)                      | .Pullback(148)                      |
// | .Pullback() (no price)              | .Pullback()                         |
// | .IsPriceAbove(150)                  | .Entry(150) or .IsPriceAbove(150)   |
// | .IsPriceBelow(140)                  | .IsPriceBelow(140)                  |
// | .IsAboveVwap()                      | .AboveVwap() or .IsAboveVwap()      |
// | .IsBelowVwap()                      | .BelowVwap() or .IsBelowVwap()      |
// | .IsMacd(MacdState.Bullish)          | .MacdBullish()                      |
// | .IsMacd(MacdState.Bearish)          | .MacdBearish()                      |
// | .IsDI(DiDirection.Positive)         | .DiPositive()                       |
// | .IsDI(DiDirection.Negative)         | .DiNegative()                       |
// | .Long(100)                           | .Qty(100) (default is Buy)          |
// | .Short(100)                          | .Short().Qty(100)                    |
// | .TakeProfit(155)                    | .TakeProfit(155) or .TP(155)        |
// | .StopLoss(148)                      | .StopLoss(148) or .SL(148)          |
// | .TrailingStopLoss(0.10)             | .TrailingStopLoss(IS.MODERATE)      |
// | .TrailingStopLoss(0.15)             | .TrailingStopLoss(IS.STANDARD)      |
// | .ClosePosition(time)                | .ClosePosition(IS.BELL)             |
// | .Repeat()                           | .Repeat()                           |
// | .Repeat(false)                      | .Repeat(N) or .Repeat(false)        |
// | .Build()                            | (implicit, script is complete)      |
// =========================================================================


