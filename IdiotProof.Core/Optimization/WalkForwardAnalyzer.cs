// ============================================================================
// Walk-Forward Analyzer - Proper train/test validation
// ============================================================================
//
// Implements walk-forward analysis to prevent overfitting:
// 1. Split historical data into training and testing windows
// 2. Optimize parameters on training window
// 3. Validate on out-of-sample testing window
// 4. Roll forward and repeat
//
// This prevents curve-fitting and provides realistic performance estimates.
//
// ============================================================================

using IdiotProof.Backend.Models;

namespace IdiotProof.BackTesting.Optimization;

/// <summary>
/// Configuration for walk-forward analysis.
/// </summary>
public sealed record WalkForwardConfig
{
    /// <summary>Number of bars in each training window.</summary>
    public int TrainingBars { get; init; } = 1000;

    /// <summary>Number of bars in each testing window.</summary>
    public int TestingBars { get; init; } = 200;

    /// <summary>Step size to advance between windows (overlap if less than TestingBars).</summary>
    public int StepBars { get; init; } = 200;

    /// <summary>Minimum number of trades required in training period.</summary>
    public int MinTrainingTrades { get; init; } = 20;

    /// <summary>Minimum number of trades required in testing period.</summary>
    public int MinTestingTrades { get; init; } = 5;

    /// <summary>Target win rate for optimization objective.</summary>
    public double TargetWinRate { get; init; } = 0.80;

    /// <summary>Weight for win rate in scoring (vs profit).</summary>
    public double WinRateWeight { get; init; } = 0.6;

    /// <summary>Weight for profit factor in scoring.</summary>
    public double ProfitFactorWeight { get; init; } = 0.3;

    /// <summary>Weight for avoiding drawdown in scoring.</summary>
    public double DrawdownWeight { get; init; } = 0.1;

    /// <summary>
    /// Calculates total number of windows for given data length.
    /// </summary>
    public int CalculateWindowCount(int totalBars)
    {
        int windowSize = TrainingBars + TestingBars;
        if (totalBars < windowSize) return 0;
        return (totalBars - windowSize) / StepBars + 1;
    }

    /// <summary>Default config for daily trading analysis.</summary>
    public static WalkForwardConfig Daily => new()
    {
        TrainingBars = 390 * 10, // 10 trading days of 1-min bars
        TestingBars = 390 * 2,   // 2 trading days
        StepBars = 390,          // Step 1 day at a time
        MinTrainingTrades = 20,
        MinTestingTrades = 5
    };

    /// <summary>Config for intraday analysis.</summary>
    public static WalkForwardConfig Intraday => new()
    {
        TrainingBars = 390 * 5,  // 5 trading days
        TestingBars = 390,       // 1 trading day
        StepBars = 195,          // Step half day
        MinTrainingTrades = 15,
        MinTestingTrades = 3
    };

    /// <summary>Config for aggressive high-frequency testing.</summary>
    public static WalkForwardConfig HighFrequency => new()
    {
        TrainingBars = 390 * 3,  // 3 trading days
        TestingBars = 195,       // Half day
        StepBars = 60,           // Step 1 hour
        MinTrainingTrades = 10,
        MinTestingTrades = 2
    };
}

/// <summary>
/// Result of a single walk-forward window.
/// </summary>
public sealed record WalkForwardWindow
{
    /// <summary>Window index (0-based).</summary>
    public int WindowIndex { get; init; }

    /// <summary>Start time of training period.</summary>
    public DateTime TrainingStart { get; init; }

    /// <summary>End time of training period.</summary>
    public DateTime TrainingEnd { get; init; }

    /// <summary>Start time of testing period.</summary>
    public DateTime TestingStart { get; init; }

    /// <summary>End time of testing period.</summary>
    public DateTime TestingEnd { get; init; }

    /// <summary>Configuration that performed best in training.</summary>
    public required OptimizableConfig OptimizedConfig { get; init; }

    /// <summary>Training period performance.</summary>
    public required WindowPerformance TrainingPerformance { get; init; }

    /// <summary>Testing period performance (out-of-sample).</summary>
    public required WindowPerformance TestingPerformance { get; init; }

    /// <summary>How much performance degraded from training to testing.</summary>
    public double PerformanceDegradation => TrainingPerformance.WinRate - TestingPerformance.WinRate;

    /// <summary>Whether the optimized parameters worked out-of-sample.</summary>
    public bool IsSuccessful => TestingPerformance.WinRate >= 0.5 && TestingPerformance.TotalPnL > 0;

    /// <summary>Whether we achieved target win rate in testing.</summary>
    public bool AchievedTarget(double targetRate) => TestingPerformance.WinRate >= targetRate;
}

/// <summary>
/// Performance metrics for a single window.
/// </summary>
public sealed record WindowPerformance
{
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades : 0;
    public double TotalPnL { get; init; }
    public double AvgPnL => TotalTrades > 0 ? TotalPnL / TotalTrades : 0;
    public double MaxDrawdown { get; init; }
    public double ProfitFactor { get; init; }
    public double SharpeRatio { get; init; }

    /// <summary>Composite score for optimization.</summary>
    public double Score(WalkForwardConfig config) =>
        WinRate * config.WinRateWeight +
        Math.Min(ProfitFactor, 5) / 5 * config.ProfitFactorWeight +
        Math.Max(0, 1 - MaxDrawdown / 10) * config.DrawdownWeight;

    public static WindowPerformance Empty => new()
    {
        TotalTrades = 0,
        WinningTrades = 0,
        LosingTrades = 0,
        TotalPnL = 0,
        MaxDrawdown = 0,
        ProfitFactor = 0,
        SharpeRatio = 0
    };
}

/// <summary>
/// Complete results from walk-forward analysis.
/// </summary>
public sealed class WalkForwardResult
{
    /// <summary>Symbol analyzed.</summary>
    public required string Symbol { get; init; }

    /// <summary>Configuration used for analysis.</summary>
    public required WalkForwardConfig Config { get; init; }

    /// <summary>Individual window results.</summary>
    public required List<WalkForwardWindow> Windows { get; init; }

    /// <summary>Best configuration across all windows.</summary>
    public OptimizableConfig? BestConfig { get; init; }

    /// <summary>Total bars analyzed.</summary>
    public int TotalBars { get; init; }

    /// <summary>Start of data range.</summary>
    public DateTime DataStart { get; init; }

    /// <summary>End of data range.</summary>
    public DateTime DataEnd { get; init; }

    // Aggregate statistics

    /// <summary>Number of windows that achieved target win rate.</summary>
    public int SuccessfulWindows => Windows.Count(w => w.IsSuccessful);

    /// <summary>Percentage of windows that worked out-of-sample.</summary>
    public double SuccessRate => Windows.Count > 0 ? (double)SuccessfulWindows / Windows.Count : 0;

    /// <summary>Average training win rate across all windows.</summary>
    public double AvgTrainingWinRate => Windows.Count > 0 
        ? Windows.Average(w => w.TrainingPerformance.WinRate) : 0;

    /// <summary>Average testing win rate across all windows (realistic expectation).</summary>
    public double AvgTestingWinRate => Windows.Count > 0 
        ? Windows.Average(w => w.TestingPerformance.WinRate) : 0;

    /// <summary>Average degradation from training to testing.</summary>
    public double AvgDegradation => Windows.Count > 0 
        ? Windows.Average(w => w.PerformanceDegradation) : 0;

    /// <summary>Total P&L from testing periods (realistic backtest).</summary>
    public double TotalTestingPnL => Windows.Sum(w => w.TestingPerformance.TotalPnL);

    /// <summary>Total trades in testing periods.</summary>
    public int TotalTestingTrades => Windows.Sum(w => w.TestingPerformance.TotalTrades);

    /// <summary>Overall win rate from all testing periods combined.</summary>
    public double CombinedTestingWinRate
    {
        get
        {
            int totalWins = Windows.Sum(w => w.TestingPerformance.WinningTrades);
            int totalTrades = TotalTestingTrades;
            return totalTrades > 0 ? (double)totalWins / totalTrades : 0;
        }
    }

    /// <summary>Statistical significance of testing results.</summary>
    public StatisticalTestResult Significance
    {
        get
        {
            int totalWins = Windows.Sum(w => w.TestingPerformance.WinningTrades);
            int totalTrades = TotalTestingTrades;
            return StatisticalTests.WinRateTest(totalWins, totalTrades);
        }
    }

    /// <summary>Windows that achieved 80% or higher win rate in testing.</summary>
    public int WindowsAbove80Percent => Windows.Count(w => w.TestingPerformance.WinRate >= 0.80);

    /// <summary>Percentage of windows achieving 80% win rate.</summary>
    public double HighPerformanceRate => Windows.Count > 0 
        ? (double)WindowsAbove80Percent / Windows.Count : 0;

    public string GetReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"""
            +======================================================================+
            | WALK-FORWARD ANALYSIS: {Symbol,-36}        |
            +======================================================================+
            | Data Period:      {DataStart:yyyy-MM-dd} to {DataEnd:yyyy-MM-dd}                  |
            | Total Bars:       {TotalBars,8:N0}                                        |
            | Windows Analyzed: {Windows.Count,8}                                        |
            +----------------------------------------------------------------------+
            
            PERFORMANCE SUMMARY (Out-of-Sample Testing Results)
            +----------------------------------------------------------------------+
            | Testing Win Rate:     {CombinedTestingWinRate * 100,8:F2}%                          |
            | Successful Windows:   {SuccessfulWindows,3} / {Windows.Count,-3} ({SuccessRate * 100:F1}%)                   |
            | Windows >= 80% WR:    {WindowsAbove80Percent,3} / {Windows.Count,-3} ({HighPerformanceRate * 100:F1}%)                   |
            | Total Testing P&L:    ${TotalTestingPnL,10:N2}                         |
            | Total Testing Trades: {TotalTestingTrades,8}                            |
            +----------------------------------------------------------------------+
            
            OVERFITTING ANALYSIS
            +----------------------------------------------------------------------+
            | Avg Training WR:      {AvgTrainingWinRate * 100,8:F2}%                          |
            | Avg Testing WR:       {AvgTestingWinRate * 100,8:F2}%                          |
            | Avg Degradation:      {AvgDegradation * 100,8:F2}%                          |
            | Overfitting Risk:     {(AvgDegradation > 0.15 ? "HIGH" : AvgDegradation > 0.08 ? "MODERATE" : "LOW"),-10}                     |
            +----------------------------------------------------------------------+
            
            STATISTICAL SIGNIFICANCE
            +----------------------------------------------------------------------+
            | p-value:              {Significance.PValue,12:F6}                      |
            | Is Significant:       {(Significance.IsSignificant ? "YES" : "NO"),-10}                     |
            | Confidence Interval:  [{Significance.ConfidenceIntervalLow * 100:F1}%, {Significance.ConfidenceIntervalHigh * 100:F1}%]              |
            +----------------------------------------------------------------------+
            """);

        if (BestConfig != null)
        {
            sb.AppendLine($"""
            
            BEST CONFIGURATION
            +----------------------------------------------------------------------+
            | Weights: VWAP={BestConfig.Weights.Vwap:F2}, EMA={BestConfig.Weights.Ema:F2}, RSI={BestConfig.Weights.Rsi:F2}
            |          MACD={BestConfig.Weights.Macd:F2}, ADX={BestConfig.Weights.Adx:F2}, VOL={BestConfig.Weights.Volume:F2}
            | Entry:   Long >= {BestConfig.LongEntryThreshold}, Short <= {BestConfig.ShortEntryThreshold}
            | Exit:    Long < {BestConfig.LongExitThreshold}, Short > {BestConfig.ShortExitThreshold}
            | TP/SL:   {BestConfig.TakeProfitAtr:F1}x ATR / {BestConfig.StopLossAtr:F1}x ATR
            +----------------------------------------------------------------------+
            """);
        }

        // Individual window summary
        sb.AppendLine();
        sb.AppendLine("WINDOW BREAKDOWN");
        sb.AppendLine("+----------------------------------------------------------------------+");
        sb.AppendLine("| Window |  Training    |   Testing    | Degradation | Status        |");
        sb.AppendLine("|--------|--------------|--------------|-------------|---------------|");

        foreach (var window in Windows)
        {
            string status = window.TestingPerformance.WinRate >= 0.80 ? "* 80%+ *" 
                          : window.IsSuccessful ? "  OK  " : " FAIL ";
            sb.AppendLine($"| {window.WindowIndex,6} | {window.TrainingPerformance.WinRate*100,5:F1}% ({window.TrainingPerformance.TotalTrades,3}) | {window.TestingPerformance.WinRate*100,5:F1}% ({window.TestingPerformance.TotalTrades,3}) | {window.PerformanceDegradation*100,9:F1}%  | {status,13} |");
        }
        sb.AppendLine("+----------------------------------------------------------------------+");

        return sb.ToString();
    }
}

/// <summary>
/// Performs walk-forward analysis on historical data.
/// </summary>
public sealed class WalkForwardAnalyzer
{
    private readonly WalkForwardConfig _config;

    public WalkForwardAnalyzer(WalkForwardConfig? config = null)
    {
        _config = config ?? WalkForwardConfig.Daily;
    }

    /// <summary>
    /// Runs walk-forward analysis on historical bars.
    /// </summary>
    /// <param name="symbol">Symbol being analyzed.</param>
    /// <param name="bars">Historical price bars.</param>
    /// <param name="optimizer">Function that finds optimal config for given bars.</param>
    /// <param name="simulator">Function that simulates trading and returns performance.</param>
    /// <param name="progress">Optional progress reporter.</param>
    public WalkForwardResult Analyze(
        string symbol,
        IReadOnlyList<HistoricalBar> bars,
        Func<IReadOnlyList<HistoricalBar>, OptimizableConfig> optimizer,
        Func<IReadOnlyList<HistoricalBar>, OptimizableConfig, WindowPerformance> simulator,
        IProgress<int>? progress = null)
    {
        if (bars.Count < _config.TrainingBars + _config.TestingBars)
        {
            return new WalkForwardResult
            {
                Symbol = symbol,
                Config = _config,
                Windows = [],
                TotalBars = bars.Count,
                DataStart = bars.Count > 0 ? bars[0].Time : DateTime.MinValue,
                DataEnd = bars.Count > 0 ? bars[^1].Time : DateTime.MinValue
            };
        }

        var windows = new List<WalkForwardWindow>();
        int windowCount = _config.CalculateWindowCount(bars.Count);
        int windowIndex = 0;

        for (int startBar = 0; startBar + _config.TrainingBars + _config.TestingBars <= bars.Count; startBar += _config.StepBars)
        {
            // Extract training and testing windows
            var trainingBars = bars.Skip(startBar).Take(_config.TrainingBars).ToList();
            var testingBars = bars.Skip(startBar + _config.TrainingBars).Take(_config.TestingBars).ToList();

            // Optimize on training data
            var optimizedConfig = optimizer(trainingBars);

            // Evaluate on training (in-sample)
            var trainingPerformance = simulator(trainingBars, optimizedConfig);

            // Evaluate on testing (out-of-sample)
            var testingPerformance = simulator(testingBars, optimizedConfig);

            windows.Add(new WalkForwardWindow
            {
                WindowIndex = windowIndex,
                TrainingStart = trainingBars[0].Time,
                TrainingEnd = trainingBars[^1].Time,
                TestingStart = testingBars[0].Time,
                TestingEnd = testingBars[^1].Time,
                OptimizedConfig = optimizedConfig,
                TrainingPerformance = trainingPerformance,
                TestingPerformance = testingPerformance
            });

            windowIndex++;
            progress?.Report(windowIndex * 100 / windowCount);
        }

        // Find configuration that worked best across all testing windows
        var bestWindow = windows
            .OrderByDescending(w => w.TestingPerformance.Score(_config))
            .FirstOrDefault();

        return new WalkForwardResult
        {
            Symbol = symbol,
            Config = _config,
            Windows = windows,
            BestConfig = bestWindow?.OptimizedConfig,
            TotalBars = bars.Count,
            DataStart = bars[0].Time,
            DataEnd = bars[^1].Time
        };
    }

    /// <summary>
    /// Splits data into training and testing sets for simple hold-out validation.
    /// </summary>
    public static (IReadOnlyList<HistoricalBar> training, IReadOnlyList<HistoricalBar> testing) 
        SplitData(IReadOnlyList<HistoricalBar> bars, double trainRatio = 0.8)
    {
        int splitIndex = (int)(bars.Count * trainRatio);
        var training = bars.Take(splitIndex).ToList();
        var testing = bars.Skip(splitIndex).ToList();
        return (training, testing);
    }

    /// <summary>
    /// Creates K-fold splits for cross-validation.
    /// </summary>
    public static IEnumerable<(IReadOnlyList<HistoricalBar> training, IReadOnlyList<HistoricalBar> testing)> 
        KFoldSplit(IReadOnlyList<HistoricalBar> bars, int folds = 5)
    {
        int foldSize = bars.Count / folds;
        
        for (int i = 0; i < folds; i++)
        {
            int testStart = i * foldSize;
            int testEnd = (i == folds - 1) ? bars.Count : (i + 1) * foldSize;
            
            var testing = bars.Skip(testStart).Take(testEnd - testStart).ToList();
            var training = bars.Take(testStart).Concat(bars.Skip(testEnd)).ToList();
            
            yield return (training, testing);
        }
    }
}
