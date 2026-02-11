// ============================================================================
// MarketScoreCalculator Tests - Validates score computation and weights
// ============================================================================

using IdiotProof.Calculators;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MarketScoreCalculatorTests
{
    // ========================================================================
    // IndicatorWeights Tests
    // ========================================================================

    [TestFixture]
    public class IndicatorWeightsTests
    {
        [Test]
        public void DefaultWeights_SumTo1()
        {
            var w = IndicatorWeights.Default;
            Assert.That(w.IsValid(), Is.True, "Default weights should sum to 1.0");
        }

        [Test]
        public void DefaultWeights_AllPositive()
        {
            var w = IndicatorWeights.Default;
            Assert.Multiple(() =>
            {
                Assert.That(w.Vwap, Is.GreaterThan(0));
                Assert.That(w.Ema, Is.GreaterThan(0));
                Assert.That(w.Rsi, Is.GreaterThan(0));
                Assert.That(w.Macd, Is.GreaterThan(0));
                Assert.That(w.Adx, Is.GreaterThan(0));
                Assert.That(w.Volume, Is.GreaterThan(0));
                Assert.That(w.Bollinger, Is.GreaterThan(0));
                Assert.That(w.Stochastic, Is.GreaterThan(0));
                Assert.That(w.Obv, Is.GreaterThan(0));
                Assert.That(w.Cci, Is.GreaterThan(0));
                Assert.That(w.WilliamsR, Is.GreaterThan(0));
                Assert.That(w.Sma, Is.GreaterThan(0));
                Assert.That(w.Momentum, Is.GreaterThan(0));
            });
        }

        [Test]
        public void IsValid_ReturnsFalse_WhenSumFarFrom1()
        {
            var w = new IndicatorWeights
            {
                Vwap = 0.5,
                Ema = 0.5,
                Rsi = 0.5,
                Macd = 0.5,
                Adx = 0.5,
                Volume = 0.5,
                Bollinger = 0.5,
                Stochastic = 0, Obv = 0, Cci = 0, WilliamsR = 0, Sma = 0, Momentum = 0
            };
            Assert.That(w.IsValid(), Is.False);
        }

        [Test]
        public void Normalize_SumsTo1()
        {
            var w = new IndicatorWeights
            {
                Vwap = 1, Ema = 2, Rsi = 1, Macd = 2, Adx = 2, Volume = 1,
                Bollinger = 1, Stochastic = 1, Obv = 1, Cci = 0.5, WilliamsR = 0.5, Sma = 1, Momentum = 1
            };

            var normalized = w.Normalize();
            Assert.That(normalized.IsValid(), Is.True,
                "Normalized weights should sum to 1.0");
        }

        [Test]
        public void Normalize_ZeroSum_ReturnsDefault()
        {
            var w = new IndicatorWeights(); // All zeros
            var normalized = w.Normalize();

            Assert.That(normalized.Vwap, Is.EqualTo(IndicatorWeights.Default.Vwap));
        }

        [Test]
        public void Normalize_PreservesProportions()
        {
            var w = new IndicatorWeights
            {
                Vwap = 2, Ema = 4, Rsi = 0, Macd = 0, Adx = 0, Volume = 0,
                Bollinger = 0, Stochastic = 0, Obv = 0, Cci = 0, WilliamsR = 0, Sma = 0, Momentum = 0
            };
            var n = w.Normalize();

            // EMA should be 2x VWAP weight
            Assert.That(n.Ema, Is.EqualTo(n.Vwap * 2).Within(0.001));
        }
    }

    // ========================================================================
    // Score Calculation Tests
    // ========================================================================

    [TestFixture]
    public class CalculateTests
    {
        private static IndicatorSnapshot CreateBullishSnapshot()
        {
            return new IndicatorSnapshot
            {
                Price = 150,
                Vwap = 145,           // Price above VWAP
                Ema9 = 149,           // Price above EMA9
                Ema21 = 147,          // Price above EMA21
                Ema34 = 145,          // Price above EMA34
                Ema50 = 143,          // Price above EMA50; all aligned bullish
                Rsi = 55,             // Neutral RSI
                Macd = 2.0,           // MACD bullish
                MacdSignal = 1.5,     // MACD > Signal
                MacdHistogram = 0.5,  // Positive histogram
                Adx = 35,             // Strong trend
                PlusDi = 30,          // +DI > -DI 
                MinusDi = 15,         // Bullish direction
                VolumeRatio = 1.5,    // Above average volume
                BollingerUpper = 155,
                BollingerLower = 140,
                BollingerMiddle = 147.5,
                BollingerPercentB = 0.67,
                BollingerBandwidth = 10,
                StochasticK = 65,
                StochasticD = 60,
                ObvSlope = 0.5,       // Rising OBV
                Cci = 80,             // Mildly overbought
                WilliamsR = -30,      // Upper range
                Sma20 = 148,
                Sma50 = 145,          // SMA20 > SMA50 bullish
                Momentum = 5.0,       // Positive momentum
                Roc = 3.0,            // Positive ROC
                Atr = 2.5
            };
        }

        private static IndicatorSnapshot CreateBearishSnapshot()
        {
            return new IndicatorSnapshot
            {
                Price = 130,
                Vwap = 140,           // Price below VWAP
                Ema9 = 132,           // Price below EMA9 (barely)
                Ema21 = 135,          // Price below EMA21
                Ema34 = 137,          // Price below EMA34
                Ema50 = 140,          // Price below EMA50; bearish stack
                Rsi = 35,             // Near oversold
                Macd = -2.0,          // MACD bearish
                MacdSignal = -1.0,    // MACD < Signal
                MacdHistogram = -1.0, // Negative histogram
                Adx = 30,             // Trending
                PlusDi = 12,          // -DI > +DI 
                MinusDi = 28,         // Bearish direction
                VolumeRatio = 1.3,    // Confirming volume
                BollingerUpper = 145,
                BollingerLower = 125,
                BollingerMiddle = 135,
                BollingerPercentB = 0.25,
                BollingerBandwidth = 15,
                StochasticK = 25,
                StochasticD = 30,
                ObvSlope = -0.5,      // Falling OBV
                Cci = -80,            // Mildly oversold
                WilliamsR = -75,      // Lower range
                Sma20 = 134,
                Sma50 = 138,          // SMA20 < SMA50 bearish
                Momentum = -4.0,      // Negative momentum
                Roc = -3.0,           // Negative ROC
                Atr = 3.0
            };
        }

        private static IndicatorSnapshot CreateNeutralSnapshot()
        {
            return new IndicatorSnapshot
            {
                Price = 100,
                Vwap = 100,
                Ema9 = 100, Ema21 = 100, Ema34 = 100, Ema50 = 100,
                Rsi = 50,
                Macd = 0, MacdSignal = 0, MacdHistogram = 0,
                Adx = 15,
                PlusDi = 20, MinusDi = 20,
                VolumeRatio = 1.0,
                BollingerUpper = 102, BollingerLower = 98, BollingerMiddle = 100,
                BollingerPercentB = 0.5, BollingerBandwidth = 4,
                StochasticK = 50, StochasticD = 50,
                ObvSlope = 0, Cci = 0, WilliamsR = -50,
                Sma20 = 100, Sma50 = 100,
                Momentum = 0, Roc = 0, Atr = 1.0
            };
        }

        // ==================================================================
        // Score Direction
        // ==================================================================

        [Test]
        public void Calculate_BullishSnapshot_PositiveScore()
        {
            var snapshot = CreateBullishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.TotalScore, Is.GreaterThan(0),
                $"Bullish snapshot should produce positive score, got {result.TotalScore}");
        }

        [Test]
        public void Calculate_BearishSnapshot_NegativeScore()
        {
            var snapshot = CreateBearishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.TotalScore, Is.LessThan(0),
                $"Bearish snapshot should produce negative score, got {result.TotalScore}");
        }

        [Test]
        public void Calculate_NeutralSnapshot_NearZero()
        {
            var snapshot = CreateNeutralSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(Math.Abs(result.TotalScore), Is.LessThanOrEqualTo(30),
                $"Neutral snapshot should produce near-zero score, got {result.TotalScore}");
        }

        // ==================================================================
        // Score Range
        // ==================================================================

        [Test]
        public void Calculate_TotalScore_BetweenNeg100And100()
        {
            var snapshots = new[]
            {
                CreateBullishSnapshot(),
                CreateBearishSnapshot(),
                CreateNeutralSnapshot()
            };

            foreach (var snapshot in snapshots)
            {
                var result = MarketScoreCalculator.Calculate(snapshot);
                Assert.That(result.TotalScore, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100),
                    $"Total score out of range: {result.TotalScore}");
            }
        }

        // ==================================================================
        // Component Scores
        // ==================================================================

        [Test]
        public void Calculate_BullishSnapshot_VwapScorePositive()
        {
            var snapshot = CreateBullishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.VwapScore, Is.GreaterThan(0),
                "VWAP score should be positive when price > VWAP");
        }

        [Test]
        public void Calculate_BearishSnapshot_VwapScoreNegative()
        {
            var snapshot = CreateBearishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.VwapScore, Is.LessThan(0),
                "VWAP score should be negative when price < VWAP");
        }

        [Test]
        public void Calculate_BullishSnapshot_EmaScorePositive()
        {
            var snapshot = CreateBullishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.EmaScore, Is.GreaterThan(0),
                "EMA score should be positive when price above all EMAs");
        }

        [Test]
        public void Calculate_BullishSnapshot_MacdScorePositive()
        {
            var snapshot = CreateBullishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.MacdScore, Is.GreaterThan(0),
                "MACD score should be positive when MACD > Signal");
        }

        [Test]
        public void Calculate_BullishSnapshot_IsMacdBullish()
        {
            var snapshot = CreateBullishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.IsMacdBullish, Is.True,
                "IsMacdBullish should be true when MACD > Signal");
        }

        [Test]
        public void Calculate_BullishSnapshot_IsDiPositive()
        {
            var snapshot = CreateBullishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.IsDiPositive, Is.True,
                "IsDiPositive should be true when +DI > -DI");
        }

        [Test]
        public void Calculate_BearishSnapshot_IsDiNegative()
        {
            var snapshot = CreateBearishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.That(result.IsDiPositive, Is.False,
                "IsDiPositive should be false when -DI > +DI");
        }

        // ==================================================================
        // Component Score Ranges
        // ==================================================================

        [Test]
        public void Calculate_ComponentScores_EachBetweenNeg100And100()
        {
            var snapshot = CreateBullishSnapshot();
            var result = MarketScoreCalculator.Calculate(snapshot);

            Assert.Multiple(() =>
            {
                AssertScoreInRange(result.VwapScore, "VWAP");
                AssertScoreInRange(result.EmaScore, "EMA");
                AssertScoreInRange(result.RsiScore, "RSI");
                AssertScoreInRange(result.MacdScore, "MACD");
                AssertScoreInRange(result.AdxScore, "ADX");
                AssertScoreInRange(result.VolumeScore, "Volume");
                AssertScoreInRange(result.BollingerScore, "Bollinger");
                AssertScoreInRange(result.StochasticScore, "Stochastic");
                AssertScoreInRange(result.ObvScore, "OBV");
                AssertScoreInRange(result.CciScore, "CCI");
                AssertScoreInRange(result.WilliamsRScore, "WilliamsR");
                AssertScoreInRange(result.SmaScore, "SMA");
                AssertScoreInRange(result.MomentumScore, "Momentum");
            });
        }

        private static void AssertScoreInRange(int score, string name)
        {
            Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100),
                $"{name} component score out of range: {score}");
        }

        // ==================================================================
        // Custom Weights
        // ==================================================================

        [Test]
        public void Calculate_WithCustomWeights_ProducesResult()
        {
            var snapshot = CreateBullishSnapshot();
            var weights = IndicatorWeights.Default;

            var result = MarketScoreCalculator.Calculate(snapshot, weights);

            Assert.That(result.TotalScore, Is.GreaterThan(0),
                "Custom weights with default values should produce same direction as default");
        }

        [Test]
        public void Calculate_WithHeavyMacdWeight_MacdDominates()
        {
            // Snapshot with bullish MACD but bearish everything else
            var snapshot = new IndicatorSnapshot
            {
                Price = 100,
                Vwap = 110,           // Bearish - price below VWAP
                Ema9 = 105, Ema21 = 108, Ema34 = 110, Ema50 = 112,  // All bearish
                Rsi = 25,             // Oversold (but still bearish momentum context)
                Macd = 5.0,           // Very bullish MACD
                MacdSignal = 1.0,     // Strong bullish
                MacdHistogram = 4.0,
                Adx = 10, PlusDi = 15, MinusDi = 20,
                VolumeRatio = 0.5,
                BollingerUpper = 115, BollingerLower = 95, BollingerMiddle = 105,
                StochasticK = 30, StochasticD = 35,
                Sma20 = 104, Sma50 = 108,
                Momentum = -2, Roc = -1
            };

            var weights = new IndicatorWeights
            {
                Macd = 0.90,  // Almost all MACD
                Vwap = 0.01, Ema = 0.01, Rsi = 0.01, Adx = 0.01, Volume = 0.01,
                Bollinger = 0.01, Stochastic = 0.01, Obv = 0.01, Cci = 0.01,
                WilliamsR = 0.005, Sma = 0.005, Momentum = 0.01
            };

            var result = MarketScoreCalculator.Calculate(snapshot, weights);

            Assert.That(result.TotalScore, Is.GreaterThan(0),
                "With 90% MACD weight, bullish MACD should dominate even with bearish indicators");
        }

        // ==================================================================
        // Support/Resistance Score
        // ==================================================================

        [Test]
        public void Calculate_AbovePrevDayHigh_PositiveSRScore()
        {
            var snapshot = CreateBullishSnapshot() with
            {
                PrevDayHigh = 148,    // Price 150 is just above PDH
                PrevDayLow = 140,
                PrevDayClose = 145
            };

            var result = MarketScoreCalculator.Calculate(snapshot);
            Assert.That(result.SupportResistanceScore, Is.GreaterThanOrEqualTo(0),
                "Breaking above PDH should give positive S/R score");
        }

        [Test]
        public void Calculate_NoPrevDayData_SRScoreIsZero()
        {
            var snapshot = CreateBullishSnapshot() with
            {
                PrevDayHigh = 0,
                PrevDayLow = 0,
                PrevDayClose = 0
            };

            var result = MarketScoreCalculator.Calculate(snapshot);
            Assert.That(result.SupportResistanceScore, Is.EqualTo(0));
        }

        // ==================================================================
        // Threshold Methods
        // ==================================================================

        [Test]
        public void GetDefaultThresholds_ReturnsValidPair()
        {
            var (longEntry, shortEntry) = MarketScoreCalculator.GetDefaultThresholds();

            Assert.That(longEntry, Is.GreaterThan(0));
            Assert.That(shortEntry, Is.LessThan(0));
            Assert.That(longEntry, Is.EqualTo(-shortEntry),
                "Long and short thresholds should be symmetric");
        }

        [Test]
        public void GetDefaultExitThresholds_LowerThanEntryThresholds()
        {
            var (longEntry, _) = MarketScoreCalculator.GetDefaultThresholds();
            var (longExit, _) = MarketScoreCalculator.GetDefaultExitThresholds();

            Assert.That(longExit, Is.LessThan(longEntry),
                "Exit threshold should be lower than entry threshold");
        }

        // ==================================================================
        // Edge Cases
        // ==================================================================

        [Test]
        public void Calculate_ZeroEverything_ProducesResult()
        {
            var snapshot = new IndicatorSnapshot(); // All zeros
            var result = MarketScoreCalculator.Calculate(snapshot);

            // Should not throw, and score should be near zero or neutral
            Assert.That(result.TotalScore, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100));
        }

        [Test]
        public void Calculate_ExtremeBullish_ScoreCappedAt100()
        {
            var snapshot = new IndicatorSnapshot
            {
                Price = 200,
                Vwap = 100,           // 100% above VWAP
                Ema9 = 100, Ema21 = 90, Ema34 = 80, Ema50 = 70,
                Rsi = 20,             // Oversold bounce signal
                Macd = 50, MacdSignal = 10, MacdHistogram = 40,
                Adx = 60, PlusDi = 50, MinusDi = 5,
                VolumeRatio = 5.0,
                BollingerUpper = 180, BollingerLower = 150, BollingerMiddle = 165,
                BollingerPercentB = 1.5, BollingerBandwidth = 20,
                StochasticK = 95, StochasticD = 90,
                ObvSlope = 1.0, Cci = 200, WilliamsR = -5,
                Sma20 = 180, Sma50 = 150,
                Momentum = 50, Roc = 10
            };

            var result = MarketScoreCalculator.Calculate(snapshot);
            Assert.That(result.TotalScore, Is.LessThanOrEqualTo(100));
        }
    }
}
