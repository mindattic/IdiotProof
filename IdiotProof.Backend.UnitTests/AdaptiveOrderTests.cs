// ============================================================================
// AdaptiveOrder Unit Tests
// ============================================================================
// Tests verify the market score calculation and TP/SL multiplier formulas.
//
// Key formulas tested:
// TP Multiplier (Strong Bullish):  1.0 + (MaxExtension × (score-70)/30)
// TP Multiplier (Neutral):         1.0 - (0.15 × (30-score)/60)
// SL Multiplier (Strong Bullish):  1.0 + (MaxTighten × (score-70)/30)
// SL Multiplier (Bearish):         1.0 - (MaxWiden × widenFactor)
// ============================================================================

using IdiotProof.Backend.Models;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests
{
    [TestFixture]
    public class AdaptiveOrderTests
    {
        // ====================================================================
        // TAKE PROFIT MULTIPLIER TESTS
        // ====================================================================

        [TestFixture]
        public class TakeProfitMultiplierTests
        {
            private AdaptiveOrderConfig _balanced = null!;
            private AdaptiveOrderConfig _aggressive = null!;
            private AdaptiveOrderConfig _conservative = null!;

            [SetUp]
            public void Setup()
            {
                _balanced = Adaptive.Balanced;
                _aggressive = Adaptive.Aggressive;
                _conservative = Adaptive.Conservative;
            }

            // -----------------------------------------------------------------
            // Strong Bullish (Score >= 70) - TP should extend
            // Formula: 1.0 + (MaxExtension × (score-70)/30)
            // -----------------------------------------------------------------

            [Test]
            public void StrongBullish_Score70_NoExtension()
            {
                // At score 70, extension factor = 0, so multiplier = 1.0
                double multiplier = CalculateTakeProfitMultiplier(70, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.0).Within(0.001));
            }

            [Test]
            public void StrongBullish_Score85_PartialExtension()
            {
                // Score 85: factor = (85-70)/30 = 0.5
                // Balanced MaxExtension = 0.50
                // Expected: 1.0 + (0.50 × 0.5) = 1.25
                double multiplier = CalculateTakeProfitMultiplier(85, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.25).Within(0.001));
            }

            [Test]
            public void StrongBullish_Score100_MaxExtension_Balanced()
            {
                // Score 100: factor = (100-70)/30 = 1.0
                // Balanced MaxExtension = 0.50
                // Expected: 1.0 + (0.50 × 1.0) = 1.50
                double multiplier = CalculateTakeProfitMultiplier(100, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.50).Within(0.001));
            }

            [Test]
            public void StrongBullish_Score100_MaxExtension_Aggressive()
            {
                // Score 100: factor = 1.0
                // Aggressive MaxExtension = 0.75
                // Expected: 1.0 + (0.75 × 1.0) = 1.75
                double multiplier = CalculateTakeProfitMultiplier(100, _aggressive);
                Assert.That(multiplier, Is.EqualTo(1.75).Within(0.001));
            }

            [Test]
            public void StrongBullish_Score100_MaxExtension_Conservative()
            {
                // Score 100: factor = 1.0
                // Conservative MaxExtension = 0.25
                // Expected: 1.0 + (0.25 × 1.0) = 1.25
                double multiplier = CalculateTakeProfitMultiplier(100, _conservative);
                Assert.That(multiplier, Is.EqualTo(1.25).Within(0.001));
            }

            // -----------------------------------------------------------------
            // Moderate Bullish (Score 30-70) - TP unchanged
            // -----------------------------------------------------------------

            [Test]
            public void ModerateBullish_Score50_NoChange()
            {
                double multiplier = CalculateTakeProfitMultiplier(50, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.0).Within(0.001));
            }

            [Test]
            public void ModerateBullish_Score30_NoChange()
            {
                double multiplier = CalculateTakeProfitMultiplier(30, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.0).Within(0.001));
            }

            [Test]
            public void ModerateBullish_Score69_NoChange()
            {
                // Just below strong bullish threshold
                double multiplier = CalculateTakeProfitMultiplier(69, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.0).Within(0.001));
            }

            // -----------------------------------------------------------------
            // Neutral (-30 to 30) - Slight TP reduction
            // Formula: 1.0 - (0.15 × (30-score)/60)
            // -----------------------------------------------------------------

            [Test]
            public void Neutral_Score0_MiddleReduction()
            {
                // Score 0: factor = (30-0)/60 = 0.5
                // Expected: 1.0 - (0.15 × 0.5) = 0.925
                double multiplier = CalculateTakeProfitMultiplier(0, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.925).Within(0.001));
            }

            [Test]
            public void Neutral_ScoreMinus30_MaxNeutralReduction()
            {
                // Score -30: factor = (30-(-30))/60 = 1.0
                // Expected: 1.0 - (0.15 × 1.0) = 0.85
                double multiplier = CalculateTakeProfitMultiplier(-30, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.85).Within(0.001));
            }

            [Test]
            public void Neutral_Score29_MinimalReduction()
            {
                // Score 29: factor = (30-29)/60 = 0.0167
                // Expected: 1.0 - (0.15 × 0.0167) = 0.9975
                double multiplier = CalculateTakeProfitMultiplier(29, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.9975).Within(0.01));
            }

            // -----------------------------------------------------------------
            // Moderate Bearish (-70 to -30) - Significant TP reduction
            // Formula: 0.85 - ((MaxReduction/2-0.15) × (-30-score)/40)
            // -----------------------------------------------------------------

            [Test]
            public void ModerateBearish_ScoreMinus50_PartialReduction()
            {
                // Score -50: factor = (-30-(-50))/40 = 20/40 = 0.5
                // Balanced: 0.85 - ((0.50/2 - 0.15) × 0.5) = 0.85 - (0.10 × 0.5) = 0.80
                double multiplier = CalculateTakeProfitMultiplier(-50, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.80).Within(0.01));
            }

            [Test]
            public void ModerateBearish_ScoreMinus70_TransitionToStrongBearish()
            {
                // Score -70: factor = (-30-(-70))/40 = 1.0
                // Balanced: 0.85 - ((0.50/2 - 0.15) × 1.0) = 0.85 - 0.10 = 0.75
                double multiplier = CalculateTakeProfitMultiplier(-70, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.75).Within(0.01));
            }

            // -----------------------------------------------------------------
            // Strong Bearish (< -70) - Maximum TP reduction
            // Formula: 1.0 - MaxReduction
            // -----------------------------------------------------------------

            [Test]
            public void StrongBearish_ScoreMinus80_MaxReduction_Balanced()
            {
                // Balanced MaxReduction = 0.50
                // Expected: 1.0 - 0.50 = 0.50
                double multiplier = CalculateTakeProfitMultiplier(-80, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.50).Within(0.001));
            }

            [Test]
            public void StrongBearish_ScoreMinus100_MaxReduction_Aggressive()
            {
                // Aggressive MaxReduction = 0.30
                // Expected: 1.0 - 0.30 = 0.70
                double multiplier = CalculateTakeProfitMultiplier(-100, _aggressive);
                Assert.That(multiplier, Is.EqualTo(0.70).Within(0.001));
            }

            [Test]
            public void StrongBearish_ScoreMinus100_MaxReduction_Conservative()
            {
                // Conservative MaxReduction = 0.60
                // Expected: 1.0 - 0.60 = 0.40
                double multiplier = CalculateTakeProfitMultiplier(-100, _conservative);
                Assert.That(multiplier, Is.EqualTo(0.40).Within(0.001));
            }
        }

        // ====================================================================
        // STOP LOSS MULTIPLIER TESTS
        // ====================================================================

        [TestFixture]
        public class StopLossMultiplierTests
        {
            private AdaptiveOrderConfig _balanced = null!;
            private AdaptiveOrderConfig _aggressive = null!;
            private AdaptiveOrderConfig _conservative = null!;

            [SetUp]
            public void Setup()
            {
                _balanced = Adaptive.Balanced;
                _aggressive = Adaptive.Aggressive;
                _conservative = Adaptive.Conservative;
            }

            // -----------------------------------------------------------------
            // Strong Bullish (Score >= 70) - SL should tighten
            // Formula: 1.0 + (MaxTighten × (score-70)/30)
            // Multiplier > 1.0 means stop moves closer to entry
            // -----------------------------------------------------------------

            [Test]
            public void StrongBullish_Score70_NoTighten()
            {
                double multiplier = CalculateStopLossMultiplier(70, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.0).Within(0.001));
            }

            [Test]
            public void StrongBullish_Score85_PartialTighten()
            {
                // Score 85: factor = (85-70)/30 = 0.5
                // Balanced MaxTighten = 0.50
                // Expected: 1.0 + (0.50 × 0.5) = 1.25
                double multiplier = CalculateStopLossMultiplier(85, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.25).Within(0.001));
            }

            [Test]
            public void StrongBullish_Score100_MaxTighten_Aggressive()
            {
                // Score 100: factor = 1.0
                // Aggressive MaxTighten = 0.60
                // Expected: 1.0 + (0.60 × 1.0) = 1.60
                double multiplier = CalculateStopLossMultiplier(100, _aggressive);
                Assert.That(multiplier, Is.EqualTo(1.60).Within(0.001));
            }

            [Test]
            public void StrongBullish_Score100_MaxTighten_Conservative()
            {
                // Score 100: factor = 1.0
                // Conservative MaxTighten = 0.30
                // Expected: 1.0 + (0.30 × 1.0) = 1.30
                double multiplier = CalculateStopLossMultiplier(100, _conservative);
                Assert.That(multiplier, Is.EqualTo(1.30).Within(0.001));
            }

            // -----------------------------------------------------------------
            // Moderate Bullish / Neutral Positive (Score 0-70) - SL unchanged
            // -----------------------------------------------------------------

            [Test]
            public void ModerateBullish_Score50_NoChange()
            {
                double multiplier = CalculateStopLossMultiplier(50, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.0).Within(0.001));
            }

            [Test]
            public void Neutral_Score0_NoChange()
            {
                double multiplier = CalculateStopLossMultiplier(0, _balanced);
                Assert.That(multiplier, Is.EqualTo(1.0).Within(0.001));
            }

            // -----------------------------------------------------------------
            // Bearish (Score -50 to 0) - SL should widen
            // Formula: 1.0 - (MaxWiden × (-score/50) × 0.5)
            // Multiplier < 1.0 means stop moves further from entry
            // -----------------------------------------------------------------

            [Test]
            public void Bearish_ScoreMinus25_PartialWiden()
            {
                // Score -25: widenFactor = 25/50 = 0.5
                // Balanced MaxWiden = 0.25
                // Expected: 1.0 - (0.25 × 0.5 × 0.5) = 1.0 - 0.0625 = 0.9375
                double multiplier = CalculateStopLossMultiplier(-25, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.9375).Within(0.001));
            }

            [Test]
            public void Bearish_ScoreMinus50_HalfWiden()
            {
                // Score -50: widenFactor = 1.0
                // Balanced MaxWiden = 0.25
                // Expected: 1.0 - (0.25 × 1.0 × 0.5) = 0.875
                double multiplier = CalculateStopLossMultiplier(-50, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.875).Within(0.001));
            }

            // -----------------------------------------------------------------
            // Strong Bearish (Score < -50) - SL widened at half max
            // Formula: 1.0 - (MaxWiden × 0.5)
            // -----------------------------------------------------------------

            [Test]
            public void StrongBearish_ScoreMinus80_MaxWiden_Balanced()
            {
                // Balanced MaxWiden = 0.25
                // Expected: 1.0 - (0.25 × 0.5) = 0.875
                double multiplier = CalculateStopLossMultiplier(-80, _balanced);
                Assert.That(multiplier, Is.EqualTo(0.875).Within(0.001));
            }

            [Test]
            public void StrongBearish_ScoreMinus80_MaxWiden_Conservative()
            {
                // Conservative MaxWiden = 0.40
                // Expected: 1.0 - (0.40 × 0.5) = 0.80
                double multiplier = CalculateStopLossMultiplier(-80, _conservative);
                Assert.That(multiplier, Is.EqualTo(0.80).Within(0.001));
            }

            [Test]
            public void StrongBearish_ScoreMinus80_MaxWiden_Aggressive()
            {
                // Aggressive MaxWiden = 0.15
                // Expected: 1.0 - (0.15 × 0.5) = 0.925
                double multiplier = CalculateStopLossMultiplier(-80, _aggressive);
                Assert.That(multiplier, Is.EqualTo(0.925).Within(0.001));
            }
        }

        // ====================================================================
        // PRICE ADJUSTMENT TESTS
        // ====================================================================

        [TestFixture]
        public class PriceAdjustmentTests
        {
            [Test]
            public void CIGL_Example_StrongBullish_Score85()
            {
                // From documentation example:
                // Entry: $4.15  |  Original TP: $4.80  |  Original SL: $3.90
                // Profit Range: $0.65  |  Loss Range: $0.25
                // Mode: AGGRESSIVE, Score: +85

                var config = Adaptive.Aggressive;
                double entry = 4.15;
                double originalTp = 4.80;
                double originalSl = 3.90;
                double profitRange = originalTp - entry; // $0.65
                double lossRange = entry - originalSl;   // $0.25

                double tpMultiplier = CalculateTakeProfitMultiplier(85, config);
                double slMultiplier = CalculateStopLossMultiplier(85, config);

                // TP: 1.0 + (0.75 × (85-70)/30) = 1.0 + (0.75 × 0.5) = 1.375
                Assert.That(tpMultiplier, Is.EqualTo(1.375).Within(0.001));

                // SL: 1.0 + (0.60 × (85-70)/30) = 1.0 + (0.60 × 0.5) = 1.30
                Assert.That(slMultiplier, Is.EqualTo(1.30).Within(0.001));

                // Adjusted TP: entry + (profitRange × multiplier)
                double adjustedTp = entry + (profitRange * tpMultiplier);
                Assert.That(adjustedTp, Is.EqualTo(5.04).Within(0.01)); // $5.04

                // Adjusted SL: entry - (lossRange / slMultiplier)
                // Tighter stop means divide by multiplier > 1
                double adjustedSl = entry - (lossRange / slMultiplier);
                Assert.That(adjustedSl, Is.EqualTo(3.96).Within(0.01)); // $3.96
            }

            [Test]
            public void AAPL_Example_Neutral_Score0()
            {
                // Entry: $150  |  Original TP: $160  |  Original SL: $145
                // Profit Range: $10  |  Loss Range: $5
                // Mode: BALANCED, Score: 0

                var config = Adaptive.Balanced;
                double entry = 150;
                double originalTp = 160;
                double originalSl = 145;
                double profitRange = originalTp - entry; // $10
                double lossRange = entry - originalSl;   // $5

                double tpMultiplier = CalculateTakeProfitMultiplier(0, config);
                double slMultiplier = CalculateStopLossMultiplier(0, config);

                // TP in neutral: 1.0 - (0.15 × 0.5) = 0.925
                Assert.That(tpMultiplier, Is.EqualTo(0.925).Within(0.001));

                // SL at 0 score: no change = 1.0
                Assert.That(slMultiplier, Is.EqualTo(1.0).Within(0.001));

                // Adjusted TP: entry + (profitRange × multiplier)
                double adjustedTp = entry + (profitRange * tpMultiplier);
                Assert.That(adjustedTp, Is.EqualTo(159.25).Within(0.01)); // $159.25

                // Adjusted SL: unchanged
                double adjustedSl = entry - (lossRange / slMultiplier);
                Assert.That(adjustedSl, Is.EqualTo(145.00).Within(0.01)); // $145.00
            }

            [Test]
            public void StrongBearish_EmergencyExit_Conservative()
            {
                var config = Adaptive.Conservative;
                int score = -65; // Below -60 threshold for conservative

                // Conservative EmergencyExitThreshold = -60
                bool shouldEmergencyExit = score <= config.EmergencyExitThreshold;
                Assert.That(shouldEmergencyExit, Is.True);
            }

            [Test]
            public void StrongBearish_NoEmergencyExit_Aggressive()
            {
                var config = Adaptive.Aggressive;
                int score = -75; // Above -80 threshold for aggressive

                // Aggressive EmergencyExitThreshold = -80
                bool shouldEmergencyExit = score <= config.EmergencyExitThreshold;
                Assert.That(shouldEmergencyExit, Is.False);
            }

            [Test]
            public void StrongBearish_EmergencyExit_Aggressive()
            {
                var config = Adaptive.Aggressive;
                int score = -85; // Below -80 threshold for aggressive

                // Aggressive EmergencyExitThreshold = -80
                bool shouldEmergencyExit = score <= config.EmergencyExitThreshold;
                Assert.That(shouldEmergencyExit, Is.True);
            }
        }

        // ====================================================================
        // MODE CONFIGURATION TESTS
        // ====================================================================

        [TestFixture]
        public class ModeConfigurationTests
        {
            [Test]
            public void Conservative_HasCorrectSettings()
            {
                var config = Adaptive.Conservative;

                Assert.Multiple(() =>
                {
                    Assert.That(config.Mode, Is.EqualTo(AdaptiveMode.Conservative));
                    Assert.That(config.MaxTakeProfitExtension, Is.EqualTo(0.25));
                    Assert.That(config.MaxTakeProfitReduction, Is.EqualTo(0.60));
                    Assert.That(config.MaxStopLossTighten, Is.EqualTo(0.30));
                    Assert.That(config.MaxStopLossWiden, Is.EqualTo(0.40));
                    Assert.That(config.EmergencyExitThreshold, Is.EqualTo(-60));
                    Assert.That(config.MinScoreChangeForAdjustment, Is.EqualTo(10));
                });
            }

            [Test]
            public void Balanced_HasCorrectSettings()
            {
                var config = Adaptive.Balanced;

                Assert.Multiple(() =>
                {
                    Assert.That(config.Mode, Is.EqualTo(AdaptiveMode.Balanced));
                    Assert.That(config.MaxTakeProfitExtension, Is.EqualTo(0.50));
                    Assert.That(config.MaxTakeProfitReduction, Is.EqualTo(0.50));
                    Assert.That(config.MaxStopLossTighten, Is.EqualTo(0.50));
                    Assert.That(config.MaxStopLossWiden, Is.EqualTo(0.25));
                    Assert.That(config.EmergencyExitThreshold, Is.EqualTo(-70));
                    Assert.That(config.MinScoreChangeForAdjustment, Is.EqualTo(15));
                });
            }

            [Test]
            public void Aggressive_HasCorrectSettings()
            {
                var config = Adaptive.Aggressive;

                Assert.Multiple(() =>
                {
                    Assert.That(config.Mode, Is.EqualTo(AdaptiveMode.Aggressive));
                    Assert.That(config.MaxTakeProfitExtension, Is.EqualTo(0.75));
                    Assert.That(config.MaxTakeProfitReduction, Is.EqualTo(0.30));
                    Assert.That(config.MaxStopLossTighten, Is.EqualTo(0.60));
                    Assert.That(config.MaxStopLossWiden, Is.EqualTo(0.15));
                    Assert.That(config.EmergencyExitThreshold, Is.EqualTo(-80));
                    Assert.That(config.MinScoreChangeForAdjustment, Is.EqualTo(20));
                });
            }

            [Test]
            public void DefaultWeights_SumToOne()
            {
                var config = new AdaptiveOrderConfig();
                double totalWeight = config.WeightVwap + config.WeightEma + config.WeightRsi +
                                    config.WeightMacd + config.WeightAdx + config.WeightVolume;

                Assert.That(totalWeight, Is.EqualTo(1.0).Within(0.001));
            }

            [Test]
            public void Custom_AllowsUserSettings()
            {
                var config = Adaptive.Custom(
                    tpExtension: 0.80,
                    tpReduction: 0.40,
                    slTighten: 0.70,
                    slWiden: 0.10,
                    emergencyThreshold: -75,
                    minScoreChange: 12);

                Assert.Multiple(() =>
                {
                    Assert.That(config.MaxTakeProfitExtension, Is.EqualTo(0.80));
                    Assert.That(config.MaxTakeProfitReduction, Is.EqualTo(0.40));
                    Assert.That(config.MaxStopLossTighten, Is.EqualTo(0.70));
                    Assert.That(config.MaxStopLossWiden, Is.EqualTo(0.10));
                    Assert.That(config.EmergencyExitThreshold, Is.EqualTo(-75));
                    Assert.That(config.MinScoreChangeForAdjustment, Is.EqualTo(12));
                });
            }
        }

        // ====================================================================
        // MARKET SCORE CONDITION TESTS
        // ====================================================================

        [TestFixture]
        public class MarketScoreConditionTests
        {
            [TestCase(100, "Strong Bullish")]
            [TestCase(85, "Strong Bullish")]
            [TestCase(70, "Strong Bullish")]
            [TestCase(69, "Moderate Bullish")]
            [TestCase(50, "Moderate Bullish")]
            [TestCase(30, "Moderate Bullish")]
            [TestCase(29, "Neutral")]
            [TestCase(0, "Neutral")]
            [TestCase(-30, "Neutral")]
            [TestCase(-31, "Moderate Bearish")]
            [TestCase(-50, "Moderate Bearish")]
            [TestCase(-70, "Moderate Bearish")]
            [TestCase(-71, "Strong Bearish")]
            [TestCase(-100, "Strong Bearish")]
            public void MarketScore_ReturnsCorrectCondition(int score, string expectedCondition)
            {
                var marketScore = new MarketScore { TotalScore = score };
                Assert.That(marketScore.Condition, Is.EqualTo(expectedCondition));
            }
        }

        // ====================================================================
        // BOUNDARY TESTS
        // ====================================================================

        [TestFixture]
        public class BoundaryTests
        {
            private AdaptiveOrderConfig _balanced = null!;

            [SetUp]
            public void Setup()
            {
                _balanced = Adaptive.Balanced;
            }

            [Test]
            public void TakeProfitMultiplier_NeverGoesNegative()
            {
                // Even at worst case, multiplier should be positive
                double multiplier = CalculateTakeProfitMultiplier(-100, _balanced);
                Assert.That(multiplier, Is.GreaterThan(0));
            }

            [Test]
            public void StopLossMultiplier_NeverGoesNegative()
            {
                double multiplier = CalculateStopLossMultiplier(-100, _balanced);
                Assert.That(multiplier, Is.GreaterThan(0));
            }

            [Test]
            public void TakeProfitMultiplier_AtMaxScore_DoesNotExceedUpperBound()
            {
                // With Aggressive (75% max extension), multiplier at score 100 should be 1.75
                var aggressive = Adaptive.Aggressive;
                double multiplier = CalculateTakeProfitMultiplier(100, aggressive);
                Assert.That(multiplier, Is.LessThanOrEqualTo(1.0 + aggressive.MaxTakeProfitExtension));
            }

            [Test]
            public void StopLossMultiplier_AtMaxScore_DoesNotExceedUpperBound()
            {
                // With Aggressive (60% max tighten), multiplier at score 100 should be 1.60
                var aggressive = Adaptive.Aggressive;
                double multiplier = CalculateStopLossMultiplier(100, aggressive);
                Assert.That(multiplier, Is.LessThanOrEqualTo(1.0 + aggressive.MaxStopLossTighten));
            }

            [TestCase(70)]
            [TestCase(30)]
            [TestCase(-30)]
            [TestCase(-70)]
            public void TakeProfitMultiplier_TransitionPoints_AreContinuous(int score)
            {
                // Test that transitions between score ranges are smooth
                double atPoint = CalculateTakeProfitMultiplier(score, _balanced);
                double justBelow = CalculateTakeProfitMultiplier(score - 1, _balanced);
                double justAbove = CalculateTakeProfitMultiplier(score + 1, _balanced);

                // Multiplier should be between neighbors (allowing for transition)
                double maxNeighbor = Math.Max(justBelow, justAbove);
                double minNeighbor = Math.Min(justBelow, justAbove);

                Assert.That(atPoint, Is.InRange(minNeighbor - 0.05, maxNeighbor + 0.05),
                    $"Score {score}: {atPoint} should be near [{minNeighbor}, {maxNeighbor}]");
            }
        }

        // ====================================================================
        // HELPER METHODS (Mirror the StrategyRunner implementation)
        // ====================================================================

        /// <summary>
        /// Calculates the take profit multiplier based on market score.
        /// This mirrors the private method in StrategyRunner.
        /// </summary>
        private static double CalculateTakeProfitMultiplier(int score, AdaptiveOrderConfig config)
        {
            if (score >= 70)
            {
                // Extend: 1.0 to 1.0 + MaxExtension
                double extensionFactor = (score - 70) / 30.0;
                return 1.0 + (config.MaxTakeProfitExtension * extensionFactor);
            }
            else if (score >= 30)
            {
                // Keep original
                return 1.0;
            }
            else if (score >= -30)
            {
                // Slight reduction: 1.0 to 0.85
                double reductionFactor = (30 - score) / 60.0;
                return 1.0 - (0.15 * reductionFactor);
            }
            else if (score >= -70)
            {
                // Moderate reduction: 0.85 to 1.0 - MaxReduction/2
                double reductionFactor = (-30 - score) / 40.0;
                return 0.85 - ((config.MaxTakeProfitReduction / 2 - 0.15) * reductionFactor);
            }
            else
            {
                // Maximum reduction
                return 1.0 - config.MaxTakeProfitReduction;
            }
        }

        /// <summary>
        /// Calculates the stop loss multiplier based on market score.
        /// This mirrors the private method in StrategyRunner.
        /// </summary>
        private static double CalculateStopLossMultiplier(int score, AdaptiveOrderConfig config)
        {
            if (score >= 70)
            {
                // Tighten: Multiplier > 1 means stop gets closer
                double tightenFactor = (score - 70) / 30.0;
                return 1.0 + (config.MaxStopLossTighten * tightenFactor);
            }
            else if (score >= 0)
            {
                // Slight tighten to neutral
                return 1.0;
            }
            else if (score >= -50)
            {
                // Widen slightly: Multiplier < 1 means stop gets further
                double widenFactor = -score / 50.0;
                return 1.0 - (config.MaxStopLossWiden * widenFactor * 0.5);
            }
            else
            {
                // Keep relatively tight in bearish conditions
                return 1.0 - (config.MaxStopLossWiden * 0.5);
            }
        }
    }
}


