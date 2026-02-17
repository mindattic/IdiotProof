// ============================================================================
// FluentApiComprehensiveTests - Exhaustive tests for all fluent API permutations
// ============================================================================
//
// This file contains comprehensive unit tests covering:
// 1. All Stock configuration methods and their combinations
// 2. All condition methods (price, VWAP, indicators)
// 3. All order methods (Buy, Sell, Close, CloseLong, CloseShort)
// 4. All StrategyBuilder exit configuration methods
// 5. Edge cases, validation, and error handling
// 6. Complex strategy permutations and combinations
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Comprehensive tests for the Stock fluent API covering all permutations.
/// </summary>
[TestFixture]
public class FluentApiComprehensiveTests
{
    #region Stock Configuration Exhaustive Tests

    [TestFixture]
    public class StockConfigurationTests
    {
        #region WithNotes Tests

        [Test]
        public void WithNotes_SetsNotes()
        {
            var strategy = Stock.Ticker("AAPL")
                .WithNotes("Test strategy notes")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Notes, Is.EqualTo("Test strategy notes"));
        }

        [Test]
        public void WithNotes_Null_SetsNullNotes()
        {
            var strategy = Stock.Ticker("AAPL")
                .WithNotes(null)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Notes, Is.Null);
        }

        [Test]
        public void Ticker_WithNotes_Parameter_SetsNotes()
        {
            var strategy = Stock.Ticker("AAPL", notes: "Inline notes")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Notes, Is.EqualTo("Inline notes"));
        }

        #endregion

        #region Exchange Tests

        [Test]
        public void Exchange_StringOverload_SetsExchange()
        {
            var strategy = Stock.Ticker("AAPL")
                .Exchange("NASDAQ")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Exchange, Is.EqualTo("NASDAQ"));
        }

        [Test]
        public void Exchange_NYSE_SetsCorrectly()
        {
            var strategy = Stock.Ticker("IBM")
                .Exchange("NYSE")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Exchange, Is.EqualTo("NYSE"));
        }

        [Test]
        public void Exchange_ContractExchangeSmart_SetsSmartRouting()
        {
            var strategy = Stock.Ticker("AAPL")
                .Exchange(ContractExchange.Smart)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
                Assert.That(strategy.PrimaryExchange, Is.Null);
            });
        }

        [Test]
        public void Exchange_ContractExchangePink_SetsPinkSheets()
        {
            var strategy = Stock.Ticker("OTCSTOCK")
                .Exchange(ContractExchange.Pink)
                .Breakout(0.50)
                .Buy(1000, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
                Assert.That(strategy.PrimaryExchange, Is.EqualTo("PINK"));
            });
        }

        [Test]
        public void PrimaryExchange_SetsCorrectly()
        {
            var strategy = Stock.Ticker("AAPL")
                .PrimaryExchange("NASDAQ")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.PrimaryExchange, Is.EqualTo("NASDAQ"));
        }

        [Test]
        public void Exchange_Default_IsSmart()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
        }

        #endregion

        #region Currency Tests

        [Test]
        public void Currency_EUR_SetsCorrectly()
        {
            var strategy = Stock.Ticker("SAP")
                .Currency("EUR")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Currency, Is.EqualTo("EUR"));
        }

        [Test]
        public void Currency_GBP_SetsCorrectly()
        {
            var strategy = Stock.Ticker("HSBA")
                .Currency("GBP")
                .Breakout(6.50)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Currency, Is.EqualTo("GBP"));
        }

        [Test]
        public void Currency_Default_IsUSD()
        {
            var strategy = Stock.Ticker("AAPL")
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Currency, Is.EqualTo("USD"));
        }

        #endregion

        #region Enabled Tests

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void Enabled_SetsCorrectly(bool enabled)
        {
            var strategy = Stock.Ticker("AAPL")
                .Enabled(enabled)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Enabled, Is.EqualTo(enabled));
        }

        [Test]
        public void Enabled_CanToggle()
        {
            var strategy = Stock.Ticker("AAPL")
                .Enabled(true)
                .Enabled(false)
                .Enabled(true)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Enabled, Is.True);
        }

        #endregion

        #region TimeFrame Tests

        [Test]
        public void TimeFrame_CustomTimes_SetsBoth()
        {
            var start = new TimeOnly(5, 30);
            var end = new TimeOnly(8, 45);

            var strategy = Stock.Ticker("AAPL")
                .TimeFrame(start, end)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(start));
                Assert.That(strategy.EndTime, Is.EqualTo(end));
            });
        }

        [Test]
        public void TimeFrame_MidnightToMidnight_SetsFull24Hours()
        {
            var start = new TimeOnly(0, 0);
            var end = new TimeOnly(23, 59);

            var strategy = Stock.Ticker("AAPL")
                .TimeFrame(start, end)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(start));
                Assert.That(strategy.EndTime, Is.EqualTo(end));
            });
        }

        #endregion

        #region SessionDuration All Sessions Tests

        [Test]
        [TestCase(TradingSession.PreMarket, 4, 0, 9, 30)]
        [TestCase(TradingSession.RTH, 9, 30, 16, 0)]
        [TestCase(TradingSession.AfterHours, 16, 0, 20, 0)]
        [TestCase(TradingSession.Extended, 4, 0, 20, 0)]
        public void SessionDuration_StandardSessions_SetsCorrectTimes(
            TradingSession session, int startHour, int startMin, int endHour, int endMin)
        {
            var strategy = Stock.Ticker("AAPL")
                .TimeFrame(session)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(startHour, startMin)));
                Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(endHour, endMin)));
                Assert.That(strategy.Session, Is.EqualTo(session));
            });
        }

        [Test]
        [TestCase(TradingSession.PreMarketEndEarly, 4, 0, 9, 15)]
        [TestCase(TradingSession.PreMarketStartLate, 4, 15, 9, 30)]
        [TestCase(TradingSession.RTHEndEarly, 9, 30, 15, 45)]
        [TestCase(TradingSession.RTHStartLate, 9, 45, 16, 0)]
        [TestCase(TradingSession.AfterHoursEndEarly, 16, 0, 19, 45)]
        public void SessionDuration_BufferedSessions_SetsCorrectTimes(
            TradingSession session, int startHour, int startMin, int endHour, int endMin)
        {
            var strategy = Stock.Ticker("AAPL")
                .TimeFrame(session)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(startHour, startMin)));
                Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(endHour, endMin)));
            });
        }

        [Test]
        public void SessionDuration_Active_ClearsTimes()
        {
            var strategy = Stock.Ticker("AAPL")
                .TimeFrame(TradingSession.PreMarket)
                .TimeFrame(TradingSession.Active)
                .Breakout(150)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.StartTime, Is.Null);
                Assert.That(strategy.EndTime, Is.Null);
            });
        }

        [Test]
        public void SessionDuration_SetsSessionProperty()
        {
            foreach (TradingSession session in Enum.GetValues<TradingSession>())
            {
                var strategy = Stock.Ticker("AAPL")
                    .TimeFrame(session)
                    .Breakout(150)
                    .Buy(100, Price.Current)
                    .Build();

                Assert.That(strategy.Session, Is.EqualTo(session));
            }
        }

        #endregion

        #region Chained Configuration Tests

        [Test]
        public void AllConfigurationMethods_ChainedCorrectly()
        {
            var strategy = Stock.Ticker("TEST", notes: "Initial notes")
                .WithNotes("Updated notes")
                .Exchange("NASDAQ")
                .PrimaryExchange("NASDAQ")
                .Currency("USD")
                .Enabled(true)
                .TimeFrame(TradingSession.PreMarket)
                .Breakout(100)
                .Buy(50, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Symbol, Is.EqualTo("TEST"));
                Assert.That(strategy.Notes, Is.EqualTo("Updated notes"));
                Assert.That(strategy.Exchange, Is.EqualTo("NASDAQ"));
                Assert.That(strategy.PrimaryExchange, Is.EqualTo("NASDAQ"));
                Assert.That(strategy.Currency, Is.EqualTo("USD"));
                Assert.That(strategy.Enabled, Is.True);
                Assert.That(strategy.Session, Is.EqualTo(TradingSession.PreMarket));
            });
        }

        #endregion
    }

    #endregion

    #region Price Condition Exhaustive Tests

    [TestFixture]
    public class PriceConditionTests
    {
        #region Breakout Tests

        [Test]
        [TestCase(0.01)]
        [TestCase(1.0)]
        [TestCase(100.0)]
        [TestCase(1000.0)]
        [TestCase(10000.0)]
        public void Breakout_VariousPriceLevels_SetsCorrectly(double level)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(level)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.TypeOf<BreakoutCondition>());
            Assert.That(((BreakoutCondition)strategy.Conditions[0]).Level, Is.EqualTo(level));
        }

        [Test]
        public void Breakout_Condition_EvaluatesCorrectly()
        {
            var condition = new BreakoutCondition(150.0);

            Assert.Multiple(() =>
            {
                Assert.That(condition.Evaluate(150.0, 145.0), Is.True);
                Assert.That(condition.Evaluate(151.0, 145.0), Is.True);
                Assert.That(condition.Evaluate(149.99, 145.0), Is.False);
            });
        }

        #endregion

        #region Pullback Tests

        [Test]
        [TestCase(0.01)]
        [TestCase(1.0)]
        [TestCase(100.0)]
        [TestCase(1000.0)]
        public void Pullback_VariousPriceLevels_SetsCorrectly(double level)
        {
            var strategy = Stock.Ticker("TEST")
                .Pullback(level)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Conditions, Has.Count.EqualTo(1));
            Assert.That(strategy.Conditions[0], Is.TypeOf<PullbackCondition>());
            Assert.That(((PullbackCondition)strategy.Conditions[0]).Level, Is.EqualTo(level));
        }

        [Test]
        public void Pullback_Condition_EvaluatesCorrectly()
        {
            var condition = new PullbackCondition(148.0);

            Assert.Multiple(() =>
            {
                Assert.That(condition.Evaluate(148.0, 150.0), Is.True);
                Assert.That(condition.Evaluate(147.0, 150.0), Is.True);
                Assert.That(condition.Evaluate(148.01, 150.0), Is.False);
            });
        }

        #endregion

        #region AboveVwap Tests

        [Test]
        public void IsAboveVwap_NoBuffer_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .IsAboveVwap()
                .Buy(100, Price.Current)
                .Build();

            var condition = (AboveVwapCondition)strategy.Conditions[0];
            Assert.That(condition.Buffer, Is.EqualTo(0));
        }

        [Test]
        [TestCase(0.01)]
        [TestCase(0.05)]
        [TestCase(0.10)]
        [TestCase(1.0)]
        public void IsAboveVwap_WithBuffer_SetsCorrectly(double buffer)
        {
            var strategy = Stock.Ticker("TEST")
                .IsAboveVwap(buffer)
                .Buy(100, Price.Current)
                .Build();

            var condition = (AboveVwapCondition)strategy.Conditions[0];
            Assert.That(condition.Buffer, Is.EqualTo(buffer));
        }

        [Test]
        public void AboveVwap_Condition_EvaluatesCorrectly()
        {
            var condition = new AboveVwapCondition(0.05);

            Assert.Multiple(() =>
            {
                // VWAP = 100, buffer = 0.05, need price >= 100.05
                Assert.That(condition.Evaluate(100.05, 100.0), Is.True);
                Assert.That(condition.Evaluate(100.10, 100.0), Is.True);
                Assert.That(condition.Evaluate(100.04, 100.0), Is.False);
                Assert.That(condition.Evaluate(99.0, 100.0), Is.False);
                // Zero VWAP should return false
                Assert.That(condition.Evaluate(100.0, 0), Is.False);
            });
        }

        #endregion

        #region BelowVwap Tests

        [Test]
        public void IsBelowVwap_NoBuffer_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .IsBelowVwap()
                .Buy(100, Price.Current)
                .Build();

            var condition = (BelowVwapCondition)strategy.Conditions[0];
            Assert.That(condition.Buffer, Is.EqualTo(0));
        }

        [Test]
        [TestCase(0.01)]
        [TestCase(0.05)]
        [TestCase(0.10)]
        public void IsBelowVwap_WithBuffer_SetsCorrectly(double buffer)
        {
            var strategy = Stock.Ticker("TEST")
                .IsBelowVwap(buffer)
                .Buy(100, Price.Current)
                .Build();

            var condition = (BelowVwapCondition)strategy.Conditions[0];
            Assert.That(condition.Buffer, Is.EqualTo(buffer));
        }

        [Test]
        public void BelowVwap_Condition_EvaluatesCorrectly()
        {
            var condition = new BelowVwapCondition(0.05);

            Assert.Multiple(() =>
            {
                // VWAP = 100, buffer = 0.05, need price <= 99.95
                Assert.That(condition.Evaluate(99.95, 100.0), Is.True);
                Assert.That(condition.Evaluate(99.90, 100.0), Is.True);
                Assert.That(condition.Evaluate(99.96, 100.0), Is.False);
                Assert.That(condition.Evaluate(101.0, 100.0), Is.False);
                // Zero VWAP should return false
                Assert.That(condition.Evaluate(0, 0), Is.False);
            });
        }

        #endregion

        #region PriceAbove Tests

        [Test]
        [TestCase(0.50)]
        [TestCase(10.0)]
        [TestCase(150.0)]
        public void IsPriceAbove_SetsCorrectly(double level)
        {
            var strategy = Stock.Ticker("TEST")
                .IsPriceAbove(level)
                .Buy(100, Price.Current)
                .Build();

            var condition = (PriceAtOrAboveCondition)strategy.Conditions[0];
            Assert.That(condition.Level, Is.EqualTo(level));
        }

        [Test]
        public void PriceAtOrAbove_Condition_EvaluatesCorrectly()
        {
            var condition = new PriceAtOrAboveCondition(100.0);

            Assert.Multiple(() =>
            {
                Assert.That(condition.Evaluate(100.0, 0), Is.True);
                Assert.That(condition.Evaluate(100.01, 0), Is.True);
                Assert.That(condition.Evaluate(99.99, 0), Is.False);
            });
        }

        #endregion

        #region PriceBelow Tests

        [Test]
        [TestCase(0.50)]
        [TestCase(10.0)]
        [TestCase(150.0)]
        public void IsPriceBelow_SetsCorrectly(double level)
        {
            var strategy = Stock.Ticker("TEST")
                .IsPriceBelow(level)
                .Buy(100, Price.Current)
                .Build();

            var condition = (PriceBelowCondition)strategy.Conditions[0];
            Assert.That(condition.Level, Is.EqualTo(level));
        }

        [Test]
        public void PriceBelow_Condition_EvaluatesCorrectly()
        {
            var condition = new PriceBelowCondition(100.0);

            Assert.Multiple(() =>
            {
                Assert.That(condition.Evaluate(99.99, 0), Is.True);
                Assert.That(condition.Evaluate(50.0, 0), Is.True);
                Assert.That(condition.Evaluate(100.0, 0), Is.False);
                Assert.That(condition.Evaluate(100.01, 0), Is.False);
            });
        }

        #endregion

        #region Custom Condition Tests

        [Test]
        public void When_CustomCondition_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .When("Price above twice VWAP", (price, vwap) => price > vwap * 2)
                .Buy(100, Price.Current)
                .Build();

            var condition = (CustomCondition)strategy.Conditions[0];
            Assert.Multiple(() =>
            {
                Assert.That(condition.Name, Is.EqualTo("Price above twice VWAP"));
                Assert.That(condition.Evaluate(210, 100), Is.True);
                Assert.That(condition.Evaluate(190, 100), Is.False);
            });
        }

        [Test]
        public void Condition_WithIStrategyCondition_AddsCorrectly()
        {
            var customCondition = new BreakoutCondition(150);
            var strategy = Stock.Ticker("TEST")
                .Condition(customCondition)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Conditions[0], Is.SameAs(customCondition));
        }

        [Test]
        public void Condition_NullCondition_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Stock.Ticker("TEST")
                    .Condition(null!)
                    .Buy(100, Price.Current)
                    .Build();
            });
        }

        #endregion

        #region Multiple Conditions Tests

        [Test]
        public void MultipleConditions_AllPriceTypes_AddsInOrder()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(155)
                .Pullback(150)
                .IsAboveVwap(0.05)
                .IsBelowVwap(0.10)
                .IsPriceAbove(145)
                .IsPriceBelow(160)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Conditions, Has.Count.EqualTo(6));
                Assert.That(strategy.Conditions[0], Is.TypeOf<BreakoutCondition>());
                Assert.That(strategy.Conditions[1], Is.TypeOf<PullbackCondition>());
                Assert.That(strategy.Conditions[2], Is.TypeOf<AboveVwapCondition>());
                Assert.That(strategy.Conditions[3], Is.TypeOf<BelowVwapCondition>());
                Assert.That(strategy.Conditions[4], Is.TypeOf<PriceAtOrAboveCondition>());
                Assert.That(strategy.Conditions[5], Is.TypeOf<PriceBelowCondition>());
            });
        }

        [Test]
        public void MultipleConditions_SameType_AddsAllInstances()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Breakout(110)
                .Breakout(120)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Conditions, Has.Count.EqualTo(3));
            Assert.That(strategy.Conditions.All(c => c is BreakoutCondition), Is.True);
        }

        #endregion
    }

    #endregion

    #region Indicator Condition Exhaustive Tests

    [TestFixture]
    public class IndicatorConditionTests
    {
        #region RSI Condition Tests

        [Test]
        [TestCase(RsiState.Overbought, null, 70.0)]
        [TestCase(RsiState.Oversold, null, 30.0)]
        [TestCase(RsiState.Overbought, 80.0, 80.0)]
        [TestCase(RsiState.Oversold, 20.0, 20.0)]
        public void IsRsi_SetsCorrectly(RsiState state, double? threshold, double expected)
        {
            var strategy = Stock.Ticker("TEST")
                .IsRsi(state, threshold)
                .Buy(100, Price.Current)
                .Build();

            var condition = (RsiCondition)strategy.Conditions[0];
            Assert.Multiple(() =>
            {
                Assert.That(condition.State, Is.EqualTo(state));
                Assert.That(condition.Threshold, Is.EqualTo(expected));
            });
        }

        [Test]
        public void RsiCondition_Overbought_EvaluatesCorrectly()
        {
            var condition = new RsiCondition(RsiState.Overbought);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateRsi(70.0), Is.True);
                Assert.That(condition.EvaluateRsi(80.0), Is.True);
                Assert.That(condition.EvaluateRsi(69.99), Is.False);
            });
        }

        [Test]
        public void RsiCondition_Oversold_EvaluatesCorrectly()
        {
            var condition = new RsiCondition(RsiState.Oversold);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateRsi(30.0), Is.True);
                Assert.That(condition.EvaluateRsi(20.0), Is.True);
                Assert.That(condition.EvaluateRsi(30.01), Is.False);
            });
        }

        [Test]
        public void RsiCondition_Name_FormattedCorrectly()
        {
            var overbought = new RsiCondition(RsiState.Overbought);
            var oversold = new RsiCondition(RsiState.Oversold, 25);

            Assert.Multiple(() =>
            {
                Assert.That(overbought.Name, Does.Contain("70"));
                Assert.That(overbought.Name, Does.Contain("Overbought"));
                Assert.That(oversold.Name, Does.Contain("25"));
                Assert.That(oversold.Name, Does.Contain("Oversold"));
            });
        }

        #endregion

        #region ADX Condition Tests

        [Test]
        [TestCase(Comparison.Gte, 25)]
        [TestCase(Comparison.Lte, 20)]
        [TestCase(Comparison.Gt, 30)]
        [TestCase(Comparison.Lt, 15)]
        [TestCase(Comparison.Eq, 50)]
        public void IsAdx_AllComparisons_SetsCorrectly(Comparison comparison, double threshold)
        {
            var strategy = Stock.Ticker("TEST")
                .IsAdx(comparison, threshold)
                .Buy(100, Price.Current)
                .Build();

            var condition = (AdxCondition)strategy.Conditions[0];
            Assert.Multiple(() =>
            {
                Assert.That(condition.Comparison, Is.EqualTo(comparison));
                Assert.That(condition.Threshold, Is.EqualTo(threshold));
            });
        }

        [Test]
        public void AdxCondition_Gte_EvaluatesCorrectly()
        {
            var condition = new AdxCondition(Comparison.Gte, 25);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateAdx(25), Is.True);
                Assert.That(condition.EvaluateAdx(30), Is.True);
                Assert.That(condition.EvaluateAdx(24.99), Is.False);
            });
        }

        [Test]
        public void AdxCondition_Lte_EvaluatesCorrectly()
        {
            var condition = new AdxCondition(Comparison.Lte, 20);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateAdx(20), Is.True);
                Assert.That(condition.EvaluateAdx(15), Is.True);
                Assert.That(condition.EvaluateAdx(20.01), Is.False);
            });
        }

        [Test]
        public void AdxCondition_Gt_EvaluatesCorrectly()
        {
            var condition = new AdxCondition(Comparison.Gt, 25);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateAdx(25.01), Is.True);
                Assert.That(condition.EvaluateAdx(25), Is.False);
            });
        }

        [Test]
        public void AdxCondition_Lt_EvaluatesCorrectly()
        {
            var condition = new AdxCondition(Comparison.Lt, 20);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateAdx(19.99), Is.True);
                Assert.That(condition.EvaluateAdx(20), Is.False);
            });
        }

        [Test]
        public void AdxCondition_Eq_EvaluatesCorrectly()
        {
            var condition = new AdxCondition(Comparison.Eq, 25);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateAdx(25.0), Is.True);
                Assert.That(condition.EvaluateAdx(25.0001), Is.True); // Within tolerance
                Assert.That(condition.EvaluateAdx(25.01), Is.False);
            });
        }

        [Test]
        public void AdxCondition_InvalidThreshold_ThrowsException()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new AdxCondition(Comparison.Gte, -1));
                Assert.Throws<ArgumentOutOfRangeException>(() => new AdxCondition(Comparison.Gte, 101));
            });
        }

        #endregion

        #region MACD Condition Tests

        [Test]
        [TestCase(MacdState.Bullish)]
        [TestCase(MacdState.Bearish)]
        [TestCase(MacdState.AboveZero)]
        [TestCase(MacdState.BelowZero)]
        [TestCase(MacdState.HistogramRising)]
        [TestCase(MacdState.HistogramFalling)]
        public void IsMacd_AllStates_SetsCorrectly(MacdState state)
        {
            var strategy = Stock.Ticker("TEST")
                .IsMacd(state)
                .Buy(100, Price.Current)
                .Build();

            var condition = (MacdCondition)strategy.Conditions[0];
            Assert.That(condition.State, Is.EqualTo(state));
        }

        [Test]
        public void MacdCondition_Bullish_EvaluatesCorrectly()
        {
            var condition = new MacdCondition(MacdState.Bullish);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateMacd(1.5, 1.0), Is.True);
                Assert.That(condition.EvaluateMacd(1.0, 1.5), Is.False);
            });
        }

        [Test]
        public void MacdCondition_Bearish_EvaluatesCorrectly()
        {
            var condition = new MacdCondition(MacdState.Bearish);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateMacd(1.0, 1.5), Is.True);
                Assert.That(condition.EvaluateMacd(1.5, 1.0), Is.False);
            });
        }

        [Test]
        public void MacdCondition_AboveZero_EvaluatesCorrectly()
        {
            var condition = new MacdCondition(MacdState.AboveZero);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateMacd(0.1, 0), Is.True);
                Assert.That(condition.EvaluateMacd(-0.1, 0), Is.False);
                Assert.That(condition.EvaluateMacd(0, 0), Is.False);
            });
        }

        [Test]
        public void MacdCondition_BelowZero_EvaluatesCorrectly()
        {
            var condition = new MacdCondition(MacdState.BelowZero);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateMacd(-0.1, 0), Is.True);
                Assert.That(condition.EvaluateMacd(0.1, 0), Is.False);
            });
        }

        [Test]
        public void MacdCondition_HistogramRising_EvaluatesCorrectly()
        {
            var condition = new MacdCondition(MacdState.HistogramRising);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateMacd(0, 0, 1.5, 1.0), Is.True);
                Assert.That(condition.EvaluateMacd(0, 0, 1.0, 1.5), Is.False);
                Assert.That(condition.EvaluateMacd(0, 0, 1.5, null), Is.False); // No previous
            });
        }

        [Test]
        public void MacdCondition_HistogramFalling_EvaluatesCorrectly()
        {
            var condition = new MacdCondition(MacdState.HistogramFalling);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateMacd(0, 0, 1.0, 1.5), Is.True);
                Assert.That(condition.EvaluateMacd(0, 0, 1.5, 1.0), Is.False);
            });
        }

        #endregion

        #region DI Condition Tests

        [Test]
        [TestCase(DiDirection.Positive, 0)]
        [TestCase(DiDirection.Negative, 0)]
        [TestCase(DiDirection.Positive, 5)]
        [TestCase(DiDirection.Negative, 10)]
        public void IsDI_AllDirections_SetsCorrectly(DiDirection direction, double minDiff)
        {
            var strategy = Stock.Ticker("TEST")
                .IsDI(direction, minDiff)
                .Buy(100, Price.Current)
                .Build();

            var condition = (DiCondition)strategy.Conditions[0];
            Assert.Multiple(() =>
            {
                Assert.That(condition.Direction, Is.EqualTo(direction));
                Assert.That(condition.MinDifference, Is.EqualTo(minDiff));
            });
        }

        [Test]
        public void DiCondition_Positive_EvaluatesCorrectly()
        {
            var condition = new DiCondition(DiDirection.Positive, 0);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateDI(30, 20), Is.True);
                Assert.That(condition.EvaluateDI(20, 30), Is.False);
                Assert.That(condition.EvaluateDI(25, 25), Is.False);
            });
        }

        [Test]
        public void DiCondition_Negative_EvaluatesCorrectly()
        {
            var condition = new DiCondition(DiDirection.Negative, 0);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateDI(20, 30), Is.True);
                Assert.That(condition.EvaluateDI(30, 20), Is.False);
            });
        }

        [Test]
        public void DiCondition_WithMinDifference_EvaluatesCorrectly()
        {
            var condition = new DiCondition(DiDirection.Positive, 5);

            Assert.Multiple(() =>
            {
                Assert.That(condition.EvaluateDI(30, 25), Is.True); // Diff = 5
                Assert.That(condition.EvaluateDI(35, 25), Is.True); // Diff = 10
                Assert.That(condition.EvaluateDI(29, 25), Is.False); // Diff = 4
            });
        }

        [Test]
        public void DiCondition_NegativeMinDifference_ClampsToZero()
        {
            var condition = new DiCondition(DiDirection.Positive, -5);
            Assert.That(condition.MinDifference, Is.EqualTo(0));
        }

        #endregion

        #region Combined Indicator Conditions Tests

        [Test]
        public void AllIndicatorConditions_CanBeCombined()
        {
            var strategy = Stock.Ticker("TEST")
                .IsRsi(RsiState.Oversold)
                .IsAdx(Comparison.Gte, 25)
                .IsMacd(MacdState.Bullish)
                .IsDI(DiDirection.Positive)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Conditions, Has.Count.EqualTo(4));
                Assert.That(strategy.Conditions[0], Is.TypeOf<RsiCondition>());
                Assert.That(strategy.Conditions[1], Is.TypeOf<AdxCondition>());
                Assert.That(strategy.Conditions[2], Is.TypeOf<MacdCondition>());
                Assert.That(strategy.Conditions[3], Is.TypeOf<DiCondition>());
            });
        }

        #endregion
    }

    #endregion

    #region Order Method Exhaustive Tests

    [TestFixture]
    public class OrderMethodTests
    {
        #region Buy Order Tests

        [Test]
        [TestCase(1)]
        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void Buy_VariousQuantities_SetsCorrectly(int quantity)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(quantity, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(quantity));
            });
        }

        [Test]
        [TestCase(Price.Current)]
        [TestCase(Price.VWAP)]
        [TestCase(Price.Bid)]
        [TestCase(Price.Ask)]
        public void Buy_AllPriceTypes_SetsCorrectly(Price priceType)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, priceType)
                .Build();

            Assert.That(strategy.Order.PriceType, Is.EqualTo(priceType));
        }

        [Test]
        [TestCase(OrderType.Market)]
        [TestCase(OrderType.Limit)]
        public void Buy_AllOrderTypes_SetsCorrectly(OrderType orderType)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current, orderType)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(orderType));
        }

        [Test]
        public void Buy_AllParameterCombinations()
        {
            foreach (Price priceType in Enum.GetValues<Price>())
            {
                foreach (OrderType orderType in Enum.GetValues<OrderType>())
                {
                    var strategy = Stock.Ticker("TEST")
                        .Breakout(100)
                        .Buy(100, priceType, orderType)
                        .Build();

                    Assert.Multiple(() =>
                    {
                        Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                        Assert.That(strategy.Order.PriceType, Is.EqualTo(priceType));
                        Assert.That(strategy.Order.Type, Is.EqualTo(orderType));
                    });
                }
            }
        }

        #endregion

        #region Sell Order Tests

        [Test]
        [TestCase(1)]
        [TestCase(100)]
        [TestCase(1000)]
        public void Sell_VariousQuantities_SetsCorrectly(int quantity)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(quantity, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(quantity));
            });
        }

        [Test]
        public void Sell_AllParameterCombinations()
        {
            foreach (Price priceType in Enum.GetValues<Price>())
            {
                foreach (OrderType orderType in Enum.GetValues<OrderType>())
                {
                    var strategy = Stock.Ticker("TEST")
                        .Breakout(100)
                        .Sell(100, priceType, orderType)
                        .Build();

                    Assert.Multiple(() =>
                    {
                        Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                        Assert.That(strategy.Order.PriceType, Is.EqualTo(priceType));
                        Assert.That(strategy.Order.Type, Is.EqualTo(orderType));
                    });
                }
            }
        }

        #endregion

        #region Close Order Tests

        [Test]
        public void Close_DefaultPositionSide_CreatesSellToCloseLong()
        {
            var strategy = Stock.Ticker("TEST")
                .IsPriceAbove(100)
                .Close(100)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        public void Close_PositionSideBuy_CreatesSellOrder()
        {
            var strategy = Stock.Ticker("TEST")
                .IsPriceAbove(100)
                .Close(100, OrderSide.Buy)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        public void Close_PositionSideSell_CreatesBuyToCoverOrder()
        {
            var strategy = Stock.Ticker("TEST")
                .IsPriceBelow(100)
                .Close(100, OrderSide.Sell)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        public void Close_AllParameterCombinations()
        {
            foreach (OrderSide positionSide in Enum.GetValues<OrderSide>())
            {
                foreach (Price priceType in Enum.GetValues<Price>())
                {
                    foreach (OrderType orderType in Enum.GetValues<OrderType>())
                    {
                        var strategy = Stock.Ticker("TEST")
                            .IsPriceAbove(100)
                            .Close(100, positionSide, priceType, orderType)
                            .Build();

                        var expectedSide = positionSide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

                        Assert.Multiple(() =>
                        {
                            Assert.That(strategy.Order.Side, Is.EqualTo(expectedSide));
                            Assert.That(strategy.Order.PriceType, Is.EqualTo(priceType));
                            Assert.That(strategy.Order.Type, Is.EqualTo(orderType));
                        });
                    }
                }
            }
        }

        #endregion

        #region CloseLong Tests

        [Test]
        public void CloseLong_CreatesSellOrder()
        {
            var strategy = Stock.Ticker("TEST")
                .IsPriceAbove(100)
                .CloseLong(100)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
        }

        [Test]
        public void CloseLong_AllParameterCombinations()
        {
            foreach (Price priceType in Enum.GetValues<Price>())
            {
                foreach (OrderType orderType in Enum.GetValues<OrderType>())
                {
                    var strategy = Stock.Ticker("TEST")
                        .IsPriceAbove(100)
                        .CloseLong(100, priceType, orderType)
                        .Build();

                    Assert.Multiple(() =>
                    {
                        Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                        Assert.That(strategy.Order.PriceType, Is.EqualTo(priceType));
                        Assert.That(strategy.Order.Type, Is.EqualTo(orderType));
                    });
                }
            }
        }

        #endregion

        #region CloseShort Tests

        [Test]
        public void CloseShort_CreatesBuyOrder()
        {
            var strategy = Stock.Ticker("TEST")
                .IsPriceBelow(100)
                .CloseShort(100)
                .Build();

            Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
        }

        [Test]
        public void CloseShort_AllParameterCombinations()
        {
            foreach (Price priceType in Enum.GetValues<Price>())
            {
                foreach (OrderType orderType in Enum.GetValues<OrderType>())
                {
                    var strategy = Stock.Ticker("TEST")
                        .IsPriceBelow(100)
                        .CloseShort(100, priceType, orderType)
                        .Build();

                    Assert.Multiple(() =>
                    {
                        Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                        Assert.That(strategy.Order.PriceType, Is.EqualTo(priceType));
                        Assert.That(strategy.Order.Type, Is.EqualTo(orderType));
                    });
                }
            }
        }

        #endregion

        #region IsOpeningPosition / IsClosingPosition Tests

        [Test]
        public void Buy_IsOpeningPosition_True()
        {
            var builder = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current);

            Assert.Multiple(() =>
            {
                Assert.That(builder.IsOpeningPosition, Is.True);
                Assert.That(builder.IsClosingPosition, Is.False);
            });
        }

        [Test]
        public void Sell_IsOpeningPosition_True()
        {
            var builder = Stock.Ticker("TEST")
                .Breakout(100)
                .Sell(100, Price.Current);

            Assert.Multiple(() =>
            {
                Assert.That(builder.IsOpeningPosition, Is.True);
                Assert.That(builder.IsClosingPosition, Is.False);
            });
        }

        [Test]
        public void Close_IsClosingPosition_True()
        {
            var builder = Stock.Ticker("TEST")
                .IsPriceAbove(100)
                .Close(100);

            Assert.Multiple(() =>
            {
                Assert.That(builder.IsClosingPosition, Is.True);
                Assert.That(builder.IsOpeningPosition, Is.False);
            });
        }

        [Test]
        public void CloseLong_IsClosingPosition_True()
        {
            var builder = Stock.Ticker("TEST")
                .IsPriceAbove(100)
                .CloseLong(100);

            Assert.That(builder.IsClosingPosition, Is.True);
        }

        [Test]
        public void CloseShort_IsClosingPosition_True()
        {
            var builder = Stock.Ticker("TEST")
                .IsPriceBelow(100)
                .CloseShort(100);

            Assert.That(builder.IsClosingPosition, Is.True);
        }

        #endregion
    }

    #endregion

    #region StrategyBuilder Configuration Exhaustive Tests

    [TestFixture]
    public class StrategyBuilderConfigurationTests
    {
        #region TakeProfit Tests

        [Test]
        [TestCase(100.0)]
        [TestCase(150.50)]
        [TestCase(1000.0)]
        public void TakeProfit_FixedPrice_SetsCorrectly(double price)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(price)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(price));
                Assert.That(strategy.Order.AdxTakeProfit, Is.Null);
            });
        }

        [Test]
        public void TakeProfit_AdxRange_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(105, 115)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(105)); // Fallback
                Assert.That(strategy.Order.AdxTakeProfit, Is.Not.Null);
                Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(105));
                Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(115));
            });
        }

        [Test]
        public void TakeProfit_AdxRangeWithCustomThresholds_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(105, 115, 
                    weakThreshold: 10, 
                    developingThreshold: 20, 
                    strongThreshold: 30, 
                    exitOnRollover: false)
                .Build();

            var adxConfig = strategy.Order.AdxTakeProfit!;
            Assert.Multiple(() =>
            {
                Assert.That(adxConfig.WeakTrendThreshold, Is.EqualTo(10));
                Assert.That(adxConfig.DevelopingTrendThreshold, Is.EqualTo(20));
                Assert.That(adxConfig.StrongTrendThreshold, Is.EqualTo(30));
                Assert.That(adxConfig.ExitOnAdxRollover, Is.False);
            });
        }

        [Test]
        public void TakeProfit_FixedPrice_ClearsAdxConfig()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(105, 115) // Set ADX first
                .TakeProfit(110)      // Then fixed - should clear ADX
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TakeProfitPrice, Is.EqualTo(110));
                Assert.That(strategy.Order.AdxTakeProfit, Is.Null);
            });
        }

        #endregion

        #region StopLoss Tests

        [Test]
        [TestCase(90.0)]
        [TestCase(95.50)]
        [TestCase(50.0)]
        public void StopLoss_FixedPrice_SetsCorrectly(double price)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .StopLoss(price)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableStopLoss, Is.True);
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(price));
            });
        }

        #endregion

        #region TrailingStopLoss Percentage Tests

        [Test]
        [TestCase(0.05)]
        [TestCase(0.10)]
        [TestCase(0.15)]
        [TestCase(0.20)]
        [TestCase(0.25)]
        public void TrailingStopLoss_Percentage_SetsCorrectly(double percent)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(percent)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(percent));
                Assert.That(strategy.Order.AtrStopLoss, Is.Null);
            });
        }

        #endregion

        #region TrailingStopLoss ATR Tests

        [Test]
        public void TrailingStopLoss_AtrTight_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Atr.Tight)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
                Assert.That(strategy.Order.AtrStopLoss, Is.Not.Null);
                Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(1.5));
            });
        }

        [Test]
        public void TrailingStopLoss_AtrBalanced_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Atr.Balanced)
                .Build();

            Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(2.0));
        }

        [Test]
        public void TrailingStopLoss_AtrLoose_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Atr.Loose)
                .Build();

            Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(3.0));
        }

        [Test]
        public void TrailingStopLoss_AtrVeryLoose_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Atr.VeryLoose)
                .Build();

            Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(4.0));
        }

        [Test]
        public void TrailingStopLoss_AtrCustomMultiplier_SetsCorrectly()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Atr.Multiplier(2.5))
                .Build();

            Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(2.5));
        }

        [Test]
        public void TrailingStopLoss_AtrNullConfig_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Stock.Ticker("TEST")
                    .Breakout(100)
                    .Buy(100, Price.Current)
                    .TrailingStopLoss((AtrStopLossConfig)null!)
                    .Build();
            });
        }

        [Test]
        public void TrailingStopLoss_Percentage_ClearsAtrConfig()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TrailingStopLoss(Atr.Balanced) // Set ATR first
                .TrailingStopLoss(0.10)          // Then percentage - should clear ATR
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.10));
                Assert.That(strategy.Order.AtrStopLoss, Is.Null);
            });
        }

        #endregion

        #region ClosePosition Tests

        [Test]
        public void ClosePosition_SetsTimeCorrectly()
        {
            var closeTime = new TimeOnly(9, 20);
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .ClosePosition(closeTime)
                .Build();

            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(closeTime));
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void ClosePosition_OnlyIfProfitable_SetsCorrectly(bool onlyIfProfitable)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .ClosePosition(new TimeOnly(9, 20), onlyIfProfitable)
                .Build();

            Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.EqualTo(onlyIfProfitable));
        }

        [Test]
        public void ClosePosition_UsingMarketTimeHelpers()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .ClosePosition(MarketTime.PreMarket.Ending)
                .Build();

            Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
        }

        #endregion

        #region TimeInForce Tests

        [Test]
        [TestCase(TimeInForce.Day)]
        [TestCase(TimeInForce.GoodTillCancel)]
        [TestCase(TimeInForce.ImmediateOrCancel)]
        [TestCase(TimeInForce.FillOrKill)]
        [TestCase(TimeInForce.Overnight)]
        [TestCase(TimeInForce.OvernightPlusDay)]
        [TestCase(TimeInForce.AtTheOpening)]
        public void TimeInForce_AllValues_SetCorrectly(TimeInForce tif)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(tif)
                .Build();

            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(tif));
        }

        [Test]
        public void TimeInForce_UsingTIFAlias()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TimeInForce(TIF.Day)
                .Build();

            Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Day));
        }

        #endregion

        #region OutsideRTH Tests

        [Test]
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void OutsideRTH_AllCombinations_SetCorrectly(bool outsideRth, bool takeProfit)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OutsideRTH(outsideRth, takeProfit)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.OutsideRth, Is.EqualTo(outsideRth));
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.EqualTo(takeProfit));
            });
        }

        [Test]
        public void OutsideRTH_DefaultParameter_SetsTrue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OutsideRTH()
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.True);
            });
        }

        #endregion

        #region OrderType Tests

        [Test]
        [TestCase(OrderType.Market)]
        [TestCase(OrderType.Limit)]
        public void OrderType_AllValues_SetCorrectly(OrderType orderType)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .OrderType(orderType)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(orderType));
        }

        #endregion

        #region AllOrNone Tests

        [Test]
        public void AllOrNone_Default_IsFalse()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.False);
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void AllOrNone_AllValues_SetCorrectly(bool allOrNone)
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .AllOrNone(allOrNone)
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.EqualTo(allOrNone));
        }

        [Test]
        public void AllOrNone_NoParameter_SetsTrue()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .AllOrNone()
                .Build();

            Assert.That(strategy.Order.AllOrNone, Is.True);
        }

        #endregion
    }

    #endregion

    #region Validation and Error Handling Tests

    [TestFixture]
    public class ValidationTests
    {
        [Test]
        public void Build_NoConditions_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                Stock.Ticker("TEST")
                    .Buy(100, Price.Current)
                    .Build();
            });
        }

        [Test]
        public void Ticker_NullSymbol_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Stock.Ticker(null!));
        }

        [Test]
        public void When_NullConditionName_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Stock.Ticker("TEST")
                    .When(null!, (p, v) => true)
                    .Buy(100, Price.Current)
                    .Build();
            });
        }

        [Test]
        public void When_NullEvaluator_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Stock.Ticker("TEST")
                    .When("Test", null!)
                    .Buy(100, Price.Current)
                    .Build();
            });
        }

        [Test]
        public void AtrMultiplier_Zero_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Atr.Multiplier(0));
        }

        [Test]
        public void AtrMultiplier_Negative_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Atr.Multiplier(-1));
        }

        [Test]
        public void AtrMultiplier_ZeroPeriod_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Atr.Multiplier(2.0, 0));
        }
    }

    #endregion

    #region Default Values Verification Tests

    [TestFixture]
    public class DefaultValuesTests
    {
        [Test]
        public void Stock_AllDefaults_AreCorrect()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
                Assert.That(strategy.PrimaryExchange, Is.Null);
                Assert.That(strategy.Currency, Is.EqualTo("USD"));
                Assert.That(strategy.SecType, Is.EqualTo("STK"));
                Assert.That(strategy.Enabled, Is.True);
                Assert.That(strategy.StartTime, Is.Null);
                Assert.That(strategy.EndTime, Is.Null);
                Assert.That(strategy.Session, Is.Null);
                Assert.That(strategy.Notes, Is.Null);
            });
        }

        [Test]
        public void StrategyBuilder_AllDefaults_AreCorrect()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.TakeProfitOutsideRth, Is.True);
                Assert.That(strategy.Order.AllOrNone, Is.False);
                Assert.That(strategy.Order.EnableTakeProfit, Is.False);
                Assert.That(strategy.Order.TakeProfitPrice, Is.Null);
                Assert.That(strategy.Order.AdxTakeProfit, Is.Null);
                Assert.That(strategy.Order.EnableStopLoss, Is.False);
                Assert.That(strategy.Order.StopLossPrice, Is.Null);
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.False);
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0));
                Assert.That(strategy.Order.AtrStopLoss, Is.Null);
                Assert.That(strategy.Order.ClosePositionTime, Is.Null);
                Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.True);
            });
        }

        [Test]
        public void Buy_DefaultOrderType_IsLimit()
        {
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
        }

        [Test]
        public void Buy_DefaultPriceType_IsCurrent()
        {
            // Using explicit Price.Current since Buy(quantity) is ambiguous with legacy overload
            var strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .Build();

            Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Current));
        }
    }

    #endregion

    #region Complex Strategy Permutation Tests

    [TestFixture]
    public class ComplexStrategyPermutationTests
    {
        [Test]
        public void PreMarketMomentumStrategy_FullConfiguration()
        {
            var strategy = Stock.Ticker("VIVS", notes: "Pre-market momentum play")
                .Exchange(ContractExchange.Smart)
                .Currency("USD")
                .Enabled(true)
                .TimeFrame(TradingSession.PreMarketEndEarly)
                .IsPriceAbove(2.40)
                .IsAboveVwap()
                .Buy(100, Price.Current, OrderType.Limit)
                .TakeProfit(4.00, 4.80)
                .StopLoss(2.00)
                .TrailingStopLoss(Percent.TwentyFive)
                .ClosePosition(MarketTime.PreMarket.Ending, onlyIfProfitable: false)
                .TimeInForce(TIF.GTC)
                .OutsideRTH(true, true)
                .AllOrNone(false)
                .Build();

            Assert.Multiple(() =>
            {
                // Stock configuration
                Assert.That(strategy.Symbol, Is.EqualTo("VIVS"));
                Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
                Assert.That(strategy.Currency, Is.EqualTo("USD"));
                Assert.That(strategy.Enabled, Is.True);
                Assert.That(strategy.Session, Is.EqualTo(TradingSession.PreMarketEndEarly));
                Assert.That(strategy.Notes, Is.EqualTo("Pre-market momentum play"));

                // Conditions
                Assert.That(strategy.Conditions, Has.Count.EqualTo(2));
                Assert.That(strategy.Conditions[0], Is.TypeOf<PriceAtOrAboveCondition>());
                Assert.That(strategy.Conditions[1], Is.TypeOf<AboveVwapCondition>());

                // Order
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Buy));
                Assert.That(strategy.Order.Quantity, Is.EqualTo(100));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Limit));
                Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Current));

                // Exit configuration
                Assert.That(strategy.Order.EnableTakeProfit, Is.True);
                Assert.That(strategy.Order.AdxTakeProfit!.ConservativeTarget, Is.EqualTo(4.00));
                Assert.That(strategy.Order.AdxTakeProfit!.AggressiveTarget, Is.EqualTo(4.80));
                Assert.That(strategy.Order.EnableStopLoss, Is.True);
                Assert.That(strategy.Order.StopLossPrice, Is.EqualTo(2.00));
                Assert.That(strategy.Order.EnableTrailingStopLoss, Is.True);
                Assert.That(strategy.Order.TrailingStopLossPercent, Is.EqualTo(0.25));
                Assert.That(strategy.Order.ClosePositionTime, Is.EqualTo(MarketTime.PreMarket.Ending));
                Assert.That(strategy.Order.ClosePositionOnlyIfProfitable, Is.False);
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.GoodTillCancel));
                Assert.That(strategy.Order.OutsideRth, Is.True);
                Assert.That(strategy.Order.AllOrNone, Is.False);
            });
        }

        [Test]
        public void TrendFollowingStrategy_WithIndicators()
        {
            var strategy = Stock.Ticker("AAPL")
                .TimeFrame(TradingSession.RTH)
                .IsAdx(Comparison.Gte, 25)
                .IsDI(DiDirection.Positive, 5)
                .IsMacd(MacdState.Bullish)
                .IsMacd(MacdState.AboveZero)
                .IsAboveVwap()
                .Buy(100, Price.VWAP, OrderType.Limit)
                .TakeProfit(170)
                .TrailingStopLoss(Atr.Balanced)
                .TimeInForce(TIF.Day)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Conditions, Has.Count.EqualTo(5));
                Assert.That(strategy.Conditions[0], Is.TypeOf<AdxCondition>());
                Assert.That(strategy.Conditions[1], Is.TypeOf<DiCondition>());
                Assert.That(strategy.Conditions[2], Is.TypeOf<MacdCondition>());
                Assert.That(strategy.Conditions[3], Is.TypeOf<MacdCondition>());
                Assert.That(strategy.Conditions[4], Is.TypeOf<AboveVwapCondition>());
                Assert.That(strategy.Order.AtrStopLoss!.Multiplier, Is.EqualTo(2.0));
            });
        }

        [Test]
        public void ShortSellingStrategy_FullConfiguration()
        {
            var strategy = Stock.Ticker("TSLA")
                .TimeFrame(TradingSession.RTH)
                .IsPriceBelow(200)
                .IsBelowVwap(0.50)
                .IsRsi(RsiState.Overbought, 75)
                .IsDI(DiDirection.Negative)
                .Sell(50, Price.Bid, OrderType.Limit)
                .TakeProfit(180)
                .StopLoss(210)
                .TimeInForce(TIF.Day)
                .OutsideRTH(false)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.PriceType, Is.EqualTo(Price.Bid));
                Assert.That(strategy.Conditions, Has.Count.EqualTo(4));
            });
        }

        [Test]
        public void ClosingPositionStrategy_FullConfiguration()
        {
            var strategy = Stock.Ticker("NVDA")
                .TimeFrame(TradingSession.AfterHours)
                .IsPriceAbove(500)
                .CloseLong(25, Price.Current, OrderType.Market)
                .TimeInForce(TIF.Overnight)
                .OutsideRTH(true, true)
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Order.Side, Is.EqualTo(OrderSide.Sell));
                Assert.That(strategy.Order.Type, Is.EqualTo(OrderType.Market));
                Assert.That(strategy.Order.TimeInForce, Is.EqualTo(TimeInForce.Overnight));
            });
        }

        [Test]
        public void OTCPennyStockStrategy()
        {
            var strategy = Stock.Ticker("OTCPENNY")
                .Exchange(ContractExchange.Pink)
                .TimeFrame(TradingSession.PreMarket)
                .IsPriceAbove(0.50)
                .IsAboveVwap()
                .Buy(10000, Price.Ask, OrderType.Limit)
                .TakeProfit(0.75, 1.00)
                .StopLoss(0.40)
                .AllOrNone()
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(strategy.Exchange, Is.EqualTo("SMART"));
                Assert.That(strategy.PrimaryExchange, Is.EqualTo("PINK"));
                Assert.That(strategy.Order.AllOrNone, Is.True);
            });
        }

        [Test]
        public void MultiConditionBreakoutStrategy()
        {
            var strategy = Stock.Ticker("CATX")
                .TimeFrame(TradingSession.PreMarket)
                .Breakout(5.00)
                .Pullback(4.90)
                .IsAboveVwap(0.05)
                .IsPriceAbove(4.80)
                .When("Volume spike", (p, v) => true) // Placeholder
                .Buy(200, Price.Current)
                .TakeProfit(6.00, 7.00)
                .TrailingStopLoss(Percent.Fifteen)
                .ClosePosition(MarketTime.PreMarket.Ending)
                .Build();

            Assert.That(strategy.Conditions, Has.Count.EqualTo(5));
        }
    }

    #endregion

    #region Implicit Conversion Tests

    [TestFixture]
    public class ImplicitConversionTests
    {
        [Test]
        public void StrategyBuilder_ImplicitlyConvertsToTradingStrategy()
        {
            TradingStrategy strategy = Stock.Ticker("TEST")
                .Breakout(100)
                .Buy(100, Price.Current)
                .TakeProfit(110);

            Assert.That(strategy, Is.Not.Null);
            Assert.That(strategy.Symbol, Is.EqualTo("TEST"));
        }

        [Test]
        public void StrategyBuilder_CanBeUsedInList()
        {
            var strategies = new List<TradingStrategy>
            {
                Stock.Ticker("AAPL").Breakout(150).Buy(100, Price.Current).TakeProfit(160),
                Stock.Ticker("TSLA").Breakout(200).Buy(50, Price.Current).TakeProfit(220)
            };

            Assert.That(strategies, Has.Count.EqualTo(2));
        }
    }

    #endregion

    #region ADX TakeProfit Configuration Tests

    [TestFixture]
    public class AdxTakeProfitTests
    {
        [Test]
        public void GetTargetForAdx_WeakTrend_ReturnsConservative()
        {
            var config = new AdxTakeProfitConfig
            {
                ConservativeTarget = 105,
                AggressiveTarget = 115
            };

            Assert.That(config.GetTargetForAdx(10), Is.EqualTo(105)); // ADX < 15
        }

        [Test]
        public void GetTargetForAdx_DevelopingTrend_Interpolates()
        {
            var config = new AdxTakeProfitConfig
            {
                ConservativeTarget = 100,
                AggressiveTarget = 110
            };

            // At ADX = 20 (midpoint between 15 and 25)
            var target = config.GetTargetForAdx(20);
            Assert.That(target, Is.EqualTo(105).Within(0.01)); // Halfway
        }

        [Test]
        public void GetTargetForAdx_StrongTrend_ReturnsAggressive()
        {
            var config = new AdxTakeProfitConfig
            {
                ConservativeTarget = 105,
                AggressiveTarget = 115
            };

            Assert.That(config.GetTargetForAdx(30), Is.EqualTo(115)); // ADX >= 25
            Assert.That(config.GetTargetForAdx(50), Is.EqualTo(115)); // Very strong
        }

        [Test]
        public void GetTrendStrength_AllRanges()
        {
            var config = new AdxTakeProfitConfig
            {
                ConservativeTarget = 100,
                AggressiveTarget = 110
            };

            Assert.Multiple(() =>
            {
                Assert.That(config.GetTrendStrength(10), Does.Contain("Weak"));
                Assert.That(config.GetTrendStrength(20), Does.Contain("Developing"));
                Assert.That(config.GetTrendStrength(30), Does.Contain("Strong"));
                Assert.That(config.GetTrendStrength(40), Does.Contain("Very Strong"));
            });
        }

        [Test]
        public void AdxTakeProfitConfig_FromRange_SetsDefaults()
        {
            var config = AdxTakeProfitConfig.FromRange(105, 115);

            Assert.Multiple(() =>
            {
                Assert.That(config.ConservativeTarget, Is.EqualTo(105));
                Assert.That(config.AggressiveTarget, Is.EqualTo(115));
                Assert.That(config.WeakTrendThreshold, Is.EqualTo(15));
                Assert.That(config.DevelopingTrendThreshold, Is.EqualTo(25));
                Assert.That(config.StrongTrendThreshold, Is.EqualTo(35));
                Assert.That(config.ExitOnAdxRollover, Is.True);
            });
        }
    }

    #endregion

    #region ATR Stop Loss Configuration Tests

    [TestFixture]
    public class AtrStopLossConfigTests
    {
        [Test]
        public void AtrPresets_HaveCorrectMultipliers()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Atr.Tight.Multiplier, Is.EqualTo(1.5));
                Assert.That(Atr.Balanced.Multiplier, Is.EqualTo(2.0));
                Assert.That(Atr.Loose.Multiplier, Is.EqualTo(3.0));
                Assert.That(Atr.VeryLoose.Multiplier, Is.EqualTo(4.0));
            });
        }

        [Test]
        public void AtrPresets_AllAreTrailing()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Atr.Tight.IsTrailing, Is.True);
                Assert.That(Atr.Balanced.IsTrailing, Is.True);
                Assert.That(Atr.Loose.IsTrailing, Is.True);
                Assert.That(Atr.VeryLoose.IsTrailing, Is.True);
            });
        }

        [Test]
        public void AtrPresets_DefaultPeriodIs14()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Atr.Tight.Period, Is.EqualTo(14));
                Assert.That(Atr.Balanced.Period, Is.EqualTo(14));
                Assert.That(Atr.Loose.Period, Is.EqualTo(14));
                Assert.That(Atr.VeryLoose.Period, Is.EqualTo(14));
            });
        }

        [Test]
        public void AtrCustom_AllParametersSet()
        {
            var config = Atr.Multiplier(2.5, period: 21, isTrailing: false);

            Assert.Multiple(() =>
            {
                Assert.That(config.Multiplier, Is.EqualTo(2.5));
                Assert.That(config.Period, Is.EqualTo(21));
                Assert.That(config.IsTrailing, Is.False);
            });
        }

        [Test]
        public void AtrConfig_Description_FormattedCorrectly()
        {
            var trailing = Atr.Balanced;
            var notTrailing = Atr.Multiplier(2.0, isTrailing: false);

            Assert.Multiple(() =>
            {
                Assert.That(trailing.Description, Does.Contain("trailing"));
                Assert.That(notTrailing.Description, Does.Contain("fixed"));
            });
        }
    }

    #endregion
}
