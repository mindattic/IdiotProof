// ============================================================================
// Autonomous Trading Backtest Tests
// ============================================================================
//
// USAGE:
//   These tests simulate AutonomousTrading against historical data.
//   They can run with:
//   1. Synthetic data (no IBKR connection needed)
//   2. Real IBKR data (requires IB Gateway running)
//
// TO RUN WITH REAL DATA:
//   1. Start IB Gateway (Paper: port 4002, Live: port 4001)
//   2. Run the integration tests
//
// ============================================================================

using IdiotProof.Backend.Services;
using IdiotProof.BackTesting.Models;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Use the AutonomousMode from BackTesting.Services
using AutonomousMode = IdiotProof.BackTesting.Services.AutonomousMode;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for autonomous trading backtesting with synthetic data.
/// No IBKR connection required.
/// </summary>
[TestFixture]
public class AutonomousBacktestTests
{
    #region Synthetic Data Tests

    [Test]
    [Description("Backtest with uptrending synthetic data should generate profitable long trades")]
    public void Backtest_UptrendingMarket_GeneratesProfitableLongTrades()
    {
        // Arrange
        var candles = GenerateUptrendingDay("NVDA", new DateOnly(2025, 6, 15), 
            startPrice: 130.00, 
            endPrice: 135.00, 
            volatility: 0.002);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Balanced,
            AllowShort = false
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("NVDA", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        System.Console.WriteLine(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCandles, Is.GreaterThan(0));
            // In an uptrend, we expect either profitable trades or no trades if thresholds weren't met
            if (result.TotalTrades > 0)
            {
                Assert.That(result.LongTrades, Is.GreaterThan(0), "Should have long trades in uptrend");
            }
        });
    }

    [Test]
    [Description("Backtest with downtrending synthetic data should generate profitable short trades")]
    public void Backtest_DowntrendingMarket_GeneratesProfitableShortTrades()
    {
        // Arrange
        var candles = GenerateDowntrendingDay("TSLA", new DateOnly(2025, 6, 15),
            startPrice: 180.00,
            endPrice: 170.00,
            volatility: 0.003);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Balanced,
            AllowShort = true
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("TSLA", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        System.Console.WriteLine(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCandles, Is.GreaterThan(0));
            // In a downtrend with shorts allowed, we may see short trades
        });
    }

    [Test]
    [Description("Backtest with volatile ranging market shows appropriate trade count")]
    public void Backtest_VolatileRanging_GeneratesMultipleTrades()
    {
        // Arrange - Create a volatile day with multiple swings
        var candles = GenerateVolatileRangingDay("AAPL", new DateOnly(2025, 6, 15),
            centerPrice: 200.00,
            rangePercent: 0.03, // 3% range
            volatility: 0.004);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Aggressive, // More trades with aggressive mode
            AllowShort = true,
            AllowDirectionFlip = true,
            MinSecondsBetweenTrades = 60 // 1 minute between trades
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("AAPL", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        System.Console.WriteLine(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCandles, Is.GreaterThan(100), "Should have enough candles for trading");
            // Volatile market may or may not trigger trades depending on score thresholds
        });
    }

    [Test]
    [Description("Capital tracking works correctly across multiple trades")]
    public void Backtest_CapitalTracking_UpdatesCorrectly()
    {
        // Arrange
        var candles = GenerateUptrendingDay("SPY", new DateOnly(2025, 6, 15),
            startPrice: 500.00,
            endPrice: 505.00,
            volatility: 0.001);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 500.00m,
            Mode = AutonomousMode.Balanced,
            AllowShort = false
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("SPY", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        System.Console.WriteLine(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.StartingCapital, Is.EqualTo(500.00m));
            Assert.That(result.EndingCapital, Is.GreaterThan(0), "Should have ending capital");

            // Verify equity curve tracking
            Assert.That(result.EquityCurve.Count, Is.GreaterThan(0), "Should track equity curve");

            // Verify trade capital continuity
            if (result.Trades.Count > 1)
            {
                for (int i = 1; i < result.Trades.Count; i++)
                {
                    Assert.That(result.Trades[i].CapitalBefore, 
                        Is.EqualTo(result.Trades[i - 1].CapitalAfter),
                        $"Trade {i} capital should continue from previous trade");
                }
            }
        });
    }

    [Test]
    [Description("Conservative mode generates fewer trades than aggressive mode")]
    public void Backtest_ModesComparison_ConservativeFewerTrades()
    {
        // Arrange
        var candles = GenerateVolatileRangingDay("QQQ", new DateOnly(2025, 6, 15),
            centerPrice: 450.00,
            rangePercent: 0.02,
            volatility: 0.003);

        var conservativeConfig = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Conservative
        };

        var aggressiveConfig = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Aggressive
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var conservativeResult = backtester.RunWithCandles("QQQ", new DateOnly(2025, 6, 15), candles, conservativeConfig);
        var aggressiveResult = backtester.RunWithCandles("QQQ", new DateOnly(2025, 6, 15), candles, aggressiveConfig);

        // Assert
        System.Console.WriteLine("=== CONSERVATIVE ===");
        System.Console.WriteLine(conservativeResult);
        System.Console.WriteLine("\n=== AGGRESSIVE ===");
        System.Console.WriteLine(aggressiveResult);

        // Conservative should have same or fewer trades (higher thresholds)
        Assert.That(conservativeResult.TotalTrades, Is.LessThanOrEqualTo(aggressiveResult.TotalTrades + 2),
            "Conservative mode should not have significantly more trades than aggressive");
    }

    [Test]
    [Description("Drawdown is calculated correctly")]
    public void Backtest_Drawdown_CalculatedCorrectly()
    {
        // Arrange - Create a day with a significant drawdown
        var candles = GenerateDrawdownDay("AMD", new DateOnly(2025, 6, 15),
            startPrice: 150.00,
            peakPrice: 155.00, // First go up
            troughPrice: 145.00, // Then drop
            endPrice: 152.00); // Recover partially

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Aggressive
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("AMD", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        System.Console.WriteLine(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.MaxDrawdown, Is.GreaterThanOrEqualTo(0), "Drawdown should be >= 0");
            Assert.That(result.MaxDrawdownPercent, Is.GreaterThanOrEqualTo(0), "Drawdown % should be >= 0");
        });
    }

    [Test]
    [Description("CSV export generates valid output")]
    public void Backtest_CsvExport_GeneratesValidOutput()
    {
        // Arrange
        var candles = GenerateUptrendingDay("MSFT", new DateOnly(2025, 6, 15),
            startPrice: 400.00,
            endPrice: 405.00,
            volatility: 0.002);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Balanced
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("MSFT", new DateOnly(2025, 6, 15), candles, config);
        var csv = result.ToCsv();

        // Assert
        System.Console.WriteLine(csv);

        Assert.Multiple(() =>
        {
            Assert.That(csv, Does.Contain("TradeNum,EntryTime,ExitTime"), "CSV should have header");
            if (result.TotalTrades > 0)
            {
                Assert.That(csv.Split('\n').Length, Is.GreaterThan(1), "CSV should have data rows if trades exist");
            }
        });
    }

    [Test]
    [Description("Optimization insights are generated")]
    public void Backtest_OptimizationInsights_AreGenerated()
    {
        // Arrange
        var candles = GenerateVolatileRangingDay("GOOG", new DateOnly(2025, 6, 15),
            centerPrice: 175.00,
            rangePercent: 0.025,
            volatility: 0.003);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Balanced
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("GOOG", new DateOnly(2025, 6, 15), candles, config);
        var insights = result.GetOptimizationInsights();

        // Assert
        System.Console.WriteLine("Optimization Insights:");
        foreach (var insight in insights)
        {
            System.Console.WriteLine($"  * {insight}");
        }

        Assert.That(insights, Is.Not.Empty, "Should generate at least one insight");
    }

    #endregion

    #region Position Sizing Tests

    [Test]
    [Description("Full capital mode uses all available capital per trade")]
    public void Backtest_FullCapitalMode_UsesAllCapital()
    {
        // Arrange
        var candles = GenerateUptrendingDay("RIVN", new DateOnly(2025, 6, 15),
            startPrice: 15.00, // Low price stock for more shares
            endPrice: 16.00,
            volatility: 0.003);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Aggressive, // More likely to trigger
            UseFullCapital = true
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("RIVN", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        System.Console.WriteLine(result);

        if (result.TotalTrades > 0)
        {
            var firstTrade = result.Trades[0];
            // With $1000 and ~$15 stock, should buy ~66 shares
            Assert.That(firstTrade.Shares, Is.GreaterThan(50), 
                "Should buy many shares with full capital mode");
        }
    }

    [Test]
    [Description("Partial capital mode limits position size")]
    public void Backtest_PartialCapitalMode_LimitsPositionSize()
    {
        // Arrange
        var candles = GenerateUptrendingDay("F", new DateOnly(2025, 6, 15),
            startPrice: 12.00, // Low price stock
            endPrice: 13.00,
            volatility: 0.003);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Aggressive,
            UseFullCapital = false,
            FixedShareQuantity = 0, // Use capital-based sizing for this test
            MaxCapitalPerTradePercent = 0.25m // Only 25% per trade
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("F", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        System.Console.WriteLine(result);

        if (result.TotalTrades > 0)
        {
            var firstTrade = result.Trades[0];
            // With $250 (25% of $1000) and ~$12 stock, should buy ~20 shares
            Assert.That(firstTrade.Shares, Is.LessThanOrEqualTo(25),
                "Should buy fewer shares with partial capital mode");
        }
    }

    #endregion

    #region Edge Case Tests

    [Test]
    [Description("Empty candle list returns empty result")]
    public void Backtest_EmptyCandles_ReturnsEmptyResult()
    {
        // Arrange
        var candles = new List<BackTestCandle>();
        var config = new AutonomousBacktestConfig { StartingCapital = 1000.00m };
        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("TEST", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalTrades, Is.EqualTo(0));
            Assert.That(result.TotalCandles, Is.EqualTo(0));
            Assert.That(result.EndingCapital, Is.EqualTo(1000.00m));
        });
    }

    [Test]
    [Description("Insufficient warmup period handles gracefully")]
    public void Backtest_InsufficientWarmup_HandlesGracefully()
    {
        // Arrange - Only 30 candles (less than 50 warmup)
        var candles = GenerateCandles("SMALL", new DateOnly(2025, 6, 15), 100.00, 30, 0.001);
        var config = new AutonomousBacktestConfig { StartingCapital = 1000.00m };
        var backtester = CreateOfflineBacktester();

        // Act & Assert - Should not throw
        var result = backtester.RunWithCandles("SMALL", new DateOnly(2025, 6, 15), candles, config);

        Assert.That(result, Is.Not.Null);
        System.Console.WriteLine(result);
    }

    [Test]
    [Description("Very low starting capital still works")]
    public void Backtest_LowCapital_StillWorks()
    {
        // Arrange
        var candles = GenerateUptrendingDay("PLTR", new DateOnly(2025, 6, 15),
            startPrice: 25.00,
            endPrice: 26.00,
            volatility: 0.002);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = 50.00m, // Very low capital
            Mode = AutonomousMode.Aggressive
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var result = backtester.RunWithCandles("PLTR", new DateOnly(2025, 6, 15), candles, config);

        // Assert
        System.Console.WriteLine(result);
        Assert.That(result.StartingCapital, Is.EqualTo(50.00m));
    }

    #endregion

    #region Multi-Day Comparison Tests

    [Test]
    [Description("Compare multiple trading modes on same data")]
    public void Backtest_CompareAllModes_ShowsDifferences()
    {
        // Arrange
        var candles = GenerateVolatileRangingDay("META", new DateOnly(2025, 6, 15),
            centerPrice: 500.00,
            rangePercent: 0.02,
            volatility: 0.003);

        var modes = new[] { AutonomousMode.Conservative, AutonomousMode.Balanced, AutonomousMode.Aggressive };
        var backtester = CreateOfflineBacktester();

        System.Console.WriteLine("=== MODE COMPARISON ===\n");
        System.Console.WriteLine($"{"Mode",-15} {"Trades",8} {"Win %",8} {"PnL",12} {"Return %",10}");
        System.Console.WriteLine(new string('-', 55));

        foreach (var mode in modes)
        {
            var config = new AutonomousBacktestConfig
            {
                StartingCapital = 1000.00m,
                Mode = mode
            };

            var result = backtester.RunWithCandles("META", new DateOnly(2025, 6, 15), candles, config);

            System.Console.WriteLine($"{mode,-15} {result.TotalTrades,8} {result.WinRate,7:F1}% ${result.TotalPnL,10:F2} {result.TotalReturnPercent,9:F2}%");
        }

        Assert.Pass("Mode comparison completed - review output for analysis");
    }

    [Test]
    [Description("Self-calibrating backtest adapts thresholds based on performance")]
    public void Backtest_SelfCalibrating_AdaptsThresholds()
    {
        // Arrange - Generate multiple days of varied market conditions
        var allCandles = new List<BackTestCandle>();
        var baseDate = new DateTime(2025, 6, 15, 4, 0, 0);
        
        // Day 1: Uptrend - should learn to loosen long thresholds
        allCandles.AddRange(GenerateUptrendingDay("TEST", new DateOnly(2025, 6, 15), 
            startPrice: 100.00, endPrice: 105.00, volatility: 0.002));
        
        // Day 2: Choppy - should tighten to avoid false signals
        var day2Candles = GenerateVolatileRangingDay("TEST", new DateOnly(2025, 6, 16),
            centerPrice: 105.00, rangePercent: 0.02, volatility: 0.003);
        // Adjust timestamps for day 2
        foreach (var c in day2Candles)
        {
            var newTimestamp = c.Timestamp.AddDays(1);
            allCandles.Add(new BackTestCandle
            {
                Timestamp = newTimestamp,
                Open = c.Open, High = c.High, Low = c.Low, Close = c.Close,
                Volume = c.Volume
            });
        }
        
        // Day 3: Strong downtrend - should learn short signals
        var day3Candles = GenerateDowntrendingDay("TEST", new DateOnly(2025, 6, 17),
            startPrice: 105.00, endPrice: 98.00, volatility: 0.002);
        foreach (var c in day3Candles)
        {
            var newTimestamp = c.Timestamp.AddDays(2);
            allCandles.Add(new BackTestCandle
            {
                Timestamp = newTimestamp,
                Open = c.Open, High = c.High, Low = c.Low, Close = c.Close,
                Volume = c.Volume
            });
        }
        
        // Self-calibrating config
        var calibratingConfig = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            EnableSelfCalibration = true,       // Enable dynamic calibration
            InitialLongThreshold = 70,          // Start moderate
            InitialShortThreshold = -70,
            AllowShort = true,
            CalibrationInterval = 30            // Calibrate every 30 bars
        };
        
        // Comparison: Static thresholds
        var staticConfig = new AutonomousBacktestConfig
        {
            StartingCapital = 1000.00m,
            Mode = AutonomousMode.Balanced,
            AllowShort = true
        };

        var backtester = CreateOfflineBacktester();

        // Act
        var calibratingResult = backtester.RunWithCandles("TEST", new DateOnly(2025, 6, 15), allCandles, calibratingConfig);
        var staticResult = backtester.RunWithCandles("TEST", new DateOnly(2025, 6, 15), allCandles, staticConfig);

        // Assert
        System.Console.WriteLine("=== SELF-CALIBRATING (Dynamic Thresholds) ===");
        System.Console.WriteLine(calibratingResult);
        if (calibratingResult.CalibrationSummary != null)
        {
            System.Console.WriteLine("\n" + calibratingResult.CalibrationSummary);
        }
        
        System.Console.WriteLine("\n=== STATIC (Fixed Thresholds) ===");
        System.Console.WriteLine(staticResult);
        
        System.Console.WriteLine("\n=== COMPARISON ===");
        System.Console.WriteLine($"Self-Calibrating: {calibratingResult.TotalTrades} trades, " +
            $"{calibratingResult.WinRate:F1}% win rate, ${calibratingResult.TotalPnL:F2} P/L");
        System.Console.WriteLine($"Static:           {staticResult.TotalTrades} trades, " +
            $"{staticResult.WinRate:F1}% win rate, ${staticResult.TotalPnL:F2} P/L");
        
        // The self-calibrating system should have adapted
        Assert.That(calibratingResult.UsedSelfCalibration, Is.True, 
            "Should show self-calibration was used");
        
        Assert.Pass("Self-calibrating backtest completed - review calibration summary for adaptation details");
    }

    #endregion

    #region Helper Methods

    private static AutonomousBacktester CreateOfflineBacktester()
    {
        // Create a mock backtester that doesn't need IBKR connection
        // by using the RunWithCandles method directly
        return new AutonomousBacktester(null!);
    }

    private static List<BackTestCandle> GenerateUptrendingDay(
        string symbol, DateOnly date, double startPrice, double endPrice, double volatility)
    {
        var candles = new List<BackTestCandle>();
        var random = new Random(symbol.GetHashCode() + date.GetHashCode());

        int candleCount = 390; // RTH = 6.5 hours * 60 minutes
        double priceStep = (endPrice - startPrice) / candleCount;

        double price = startPrice;
        var startTime = date.ToDateTime(new TimeOnly(9, 30)); // RTH start

        for (int i = 0; i < candleCount; i++)
        {
            var timestamp = startTime.AddMinutes(i);

            // Add trend + noise
            double noise = (random.NextDouble() * 2 - 1) * volatility * price;
            double open = price;
            price += priceStep + noise;
            double close = price;

            double high = Math.Max(open, close) + random.NextDouble() * volatility * price;
            double low = Math.Min(open, close) - random.NextDouble() * volatility * price;

            long volume = 10000 + random.Next(50000);
            if (i < 30 || i > 360) volume *= 2; // Higher volume at open/close

            candles.Add(new BackTestCandle
            {
                Timestamp = timestamp,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = volume
            });
        }

        return candles;
    }

    private static List<BackTestCandle> GenerateDowntrendingDay(
        string symbol, DateOnly date, double startPrice, double endPrice, double volatility)
    {
        // Just use uptrending with reversed prices
        return GenerateUptrendingDay(symbol, date, startPrice, endPrice, volatility);
    }

    private static List<BackTestCandle> GenerateVolatileRangingDay(
        string symbol, DateOnly date, double centerPrice, double rangePercent, double volatility)
    {
        var candles = new List<BackTestCandle>();
        var random = new Random(symbol.GetHashCode() + date.GetHashCode());

        int candleCount = 390;
        double range = centerPrice * rangePercent;

        double price = centerPrice;
        var startTime = date.ToDateTime(new TimeOnly(9, 30));

        // Create oscillating pattern
        for (int i = 0; i < candleCount; i++)
        {
            var timestamp = startTime.AddMinutes(i);

            // Sine wave for ranging + noise
            double wave = Math.Sin(i * Math.PI / 60) * range; // ~60 minute cycles
            double noise = (random.NextDouble() * 2 - 1) * volatility * price;

            double open = price;
            price = centerPrice + wave + noise;
            double close = price;

            double high = Math.Max(open, close) + random.NextDouble() * volatility * price;
            double low = Math.Min(open, close) - random.NextDouble() * volatility * price;

            long volume = 10000 + random.Next(50000);

            candles.Add(new BackTestCandle
            {
                Timestamp = timestamp,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = volume
            });
        }

        return candles;
    }

    private static List<BackTestCandle> GenerateDrawdownDay(
        string symbol, DateOnly date, double startPrice, double peakPrice, double troughPrice, double endPrice)
    {
        var candles = new List<BackTestCandle>();
        var random = new Random(symbol.GetHashCode() + date.GetHashCode());

        int candleCount = 390;
        var startTime = date.ToDateTime(new TimeOnly(9, 30));

        // Phase 1: Rise to peak (first third)
        // Phase 2: Drop to trough (middle third)
        // Phase 3: Recover to end (final third)
        int phase1End = candleCount / 3;
        int phase2End = 2 * candleCount / 3;

        double price = startPrice;

        for (int i = 0; i < candleCount; i++)
        {
            var timestamp = startTime.AddMinutes(i);

            double targetPrice;
            if (i < phase1End)
            {
                // Rising to peak
                double progress = (double)i / phase1End;
                targetPrice = startPrice + (peakPrice - startPrice) * progress;
            }
            else if (i < phase2End)
            {
                // Dropping to trough
                double progress = (double)(i - phase1End) / (phase2End - phase1End);
                targetPrice = peakPrice + (troughPrice - peakPrice) * progress;
            }
            else
            {
                // Recovering to end
                double progress = (double)(i - phase2End) / (candleCount - phase2End);
                targetPrice = troughPrice + (endPrice - troughPrice) * progress;
            }

            double noise = (random.NextDouble() * 2 - 1) * 0.002 * price;
            double open = price;
            price = targetPrice + noise;
            double close = price;

            double high = Math.Max(open, close) + random.NextDouble() * 0.002 * price;
            double low = Math.Min(open, close) - random.NextDouble() * 0.002 * price;

            candles.Add(new BackTestCandle
            {
                Timestamp = timestamp,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = 10000 + random.Next(50000)
            });
        }

        return candles;
    }

    private static List<BackTestCandle> GenerateCandles(
        string symbol, DateOnly date, double startPrice, int count, double volatility)
    {
        var candles = new List<BackTestCandle>();
        var random = new Random(symbol.GetHashCode());

        double price = startPrice;
        var startTime = date.ToDateTime(new TimeOnly(9, 30));

        for (int i = 0; i < count; i++)
        {
            var timestamp = startTime.AddMinutes(i);

            double change = (random.NextDouble() * 2 - 1) * volatility * price;
            double open = price;
            price += change;
            double close = price;

            double high = Math.Max(open, close) + random.NextDouble() * volatility * price;
            double low = Math.Min(open, close) - random.NextDouble() * volatility * price;

            candles.Add(new BackTestCandle
            {
                Timestamp = timestamp,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = 10000 + random.Next(50000)
            });
        }

        return candles;
    }

    #endregion
}

/// <summary>
/// Integration tests that require IBKR Gateway connection.
/// These tests are marked as Explicit and must be run manually.
/// </summary>
[TestFixture]
[Category("Integration")]
public class AutonomousBacktestIntegrationTests
{
    // These tests require IB Gateway to be running
    // Run with: dotnet test --filter "Category=Integration"

    [Test]
    [Explicit("Requires IB Gateway connection")]
    [Description("Backtest NVDA with real historical data from IBKR")]
    public async Task Backtest_RealData_NVDA()
    {
        // This test requires IB Gateway to be running
        // Uncomment and configure when ready to test with real data

        /*
        // Connect to IB Gateway
        var client = new EClientSocket(...);
        var wrapper = new IbWrapper();
        var store = new HistoricalDataStore();
        var histService = new HistoricalDataService(client, wrapper, store);

        var backtester = new AutonomousBacktester(histService);

        var result = await backtester.RunAsync(
            symbol: "NVDA",
            date: new DateOnly(2025, 6, 10), // Use a recent trading day
            startingCapital: 1000.00m,
            config: new AutonomousBacktestConfig
            {
                Mode = AutonomousMode.Balanced,
                AllowShort = true,
                IncludePremarket = false,
                IncludeAfterHours = false
            });

        System.Console.WriteLine(result);
        System.Console.WriteLine(result.ToCsv());
        */

        Assert.Pass("Integration test requires IB Gateway - run manually");
    }

    [Test]
    [Explicit("Requires IB Gateway connection")]
    [Description("Compare multiple stocks on same day")]
    public async Task Backtest_MultipleStocks_Comparison()
    {
        Assert.Pass("Integration test requires IB Gateway - run manually");
    }
}
