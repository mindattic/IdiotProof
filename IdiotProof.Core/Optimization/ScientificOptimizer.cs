// ============================================================================
// Scientific Optimizer - Master Pipeline for Achieving 80% Win Rate
// ============================================================================
//
// This is the main orchestrator that combines all optimization components:
// 1. Weight optimization via genetic algorithm
// 2. Walk-forward validation to prevent overfitting
// 3. Statistical significance testing
// 4. TickerProfile learning integration
//
// USAGE:
//   var optimizer = new ScientificOptimizer();
//   var result = await optimizer.OptimizeForTargetAsync("NVDA", bars, 0.80);
//   if (result.IsViable) ApplyConfig(result.BestConfig);
//
// ============================================================================

using IdiotProof.Backend.Models;
using IdiotProof.BackTesting.Learning;
using IdiotProof.BackTesting.Models;
using System.Collections.Concurrent;

// Use alias to avoid conflict with local types
using ModelSimResult = IdiotProof.BackTesting.Models.SimulationResult;

namespace IdiotProof.BackTesting.Optimization;

/// <summary>
/// Configuration for the scientific optimization pipeline.
/// </summary>
public sealed record ScientificOptimizationConfig
{
    /// <summary>Target win rate to achieve.</summary>
    public double TargetWinRate { get; init; } = 0.80;

    /// <summary>Minimum trades for statistical validity.</summary>
    public int MinTrades { get; init; } = 30;

    /// <summary>Maximum acceptable performance degradation (train vs test).</summary>
    public double MaxDegradation { get; init; } = 0.15;

    /// <summary>Minimum statistical significance (p-value threshold).</summary>
    public double MaxPValue { get; init; } = 0.05;

    /// <summary>Use walk-forward validation.</summary>
    public bool UseWalkForward { get; init; } = true;

    /// <summary>Use genetic optimization (vs grid search).</summary>
    public bool UseGeneticOptimization { get; init; } = true;

    /// <summary>Run Monte Carlo simulation for robustness check.</summary>
    public bool RunMonteCarloValidation { get; init; } = true;

    /// <summary>Number of Monte Carlo simulations.</summary>
    public int MonteCarloSimulations { get; init; } = 5000;

    /// <summary>Save optimized profile to disk.</summary>
    public bool SaveProfile { get; init; } = true;

    /// <summary>Genetic optimization configuration.</summary>
    public GeneticOptimizationConfig GeneticConfig { get; init; } = GeneticOptimizationConfig.Default;

    /// <summary>Walk-forward configuration.</summary>
    public WalkForwardConfig WalkForwardConfig { get; init; } = WalkForwardConfig.Daily;

    /// <summary>Default configuration for balanced optimization.</summary>
    public static ScientificOptimizationConfig Default => new();

    /// <summary>Fast configuration for quick testing.</summary>
    public static ScientificOptimizationConfig Fast => new()
    {
        MinTrades = 15,
        UseWalkForward = false,
        RunMonteCarloValidation = false,
        GeneticConfig = GeneticOptimizationConfig.Fast
    };

    /// <summary>Rigorous configuration for production deployment.</summary>
    public static ScientificOptimizationConfig Rigorous => new()
    {
        TargetWinRate = 0.80,
        MinTrades = 50,
        MaxDegradation = 0.10,
        MaxPValue = 0.01,
        MonteCarloSimulations = 10000,
        GeneticConfig = GeneticOptimizationConfig.Thorough,
        WalkForwardConfig = WalkForwardConfig.Daily
    };
}

/// <summary>
/// Complete result from scientific optimization.
/// </summary>
public sealed class ScientificOptimizationResult
{
    public required string Symbol { get; init; }
    public required OptimizableConfig BestConfig { get; init; }
    public required ScientificOptimizationConfig Config { get; init; }

    // Performance metrics
    public double InSampleWinRate { get; init; }
    public double OutOfSampleWinRate { get; init; }
    public double PerformanceDegradation => InSampleWinRate - OutOfSampleWinRate;
    public int TotalTrades { get; init; }
    public double TotalPnL { get; init; }
    public double ProfitFactor { get; init; }
    public double MaxDrawdown { get; init; }
    public double SharpeRatio { get; init; }

    // Statistical validation
    public StatisticalTestResult? WinRateSignificance { get; init; }
    public StatisticalTestResult? MonteCarloResult { get; init; }
    public double PValue => WinRateSignificance?.PValue ?? 1.0;
    public bool IsStatisticallySignificant => PValue < Config.MaxPValue;

    // Walk-forward results
    public WalkForwardResult? WalkForwardResult { get; init; }
    public double WalkForwardSuccessRate => WalkForwardResult?.SuccessRate ?? 0;

    // Optimization details
    public GeneticOptimizationResult? GeneticResult { get; init; }
    public TimeSpan OptimizationDuration { get; init; }

    // Viability assessment
    public bool MeetsWinRateTarget => OutOfSampleWinRate >= Config.TargetWinRate;
    public bool MeetsTradeRequirement => TotalTrades >= Config.MinTrades;
    public bool MeetsDegradationLimit => PerformanceDegradation <= Config.MaxDegradation;
    public bool PassesAllCriteria => 
        MeetsWinRateTarget && 
        MeetsTradeRequirement && 
        MeetsDegradationLimit && 
        IsStatisticallySignificant;

    /// <summary>
    /// Overall viability score (0-1). Higher is better.
    /// </summary>
    public double ViabilityScore
    {
        get
        {
            double score = 0;
            
            // Win rate component (40%)
            score += (OutOfSampleWinRate / Config.TargetWinRate).Clamp(0, 1) * 0.40;
            
            // Statistical significance (25%)
            score += (1 - Math.Min(PValue, 1)).Clamp(0, 1) * 0.25;
            
            // Robustness (20%)
            double robustness = 1 - (PerformanceDegradation / Config.MaxDegradation).Clamp(0, 1);
            score += robustness * 0.20;
            
            // Trade count (10%)
            score += (Math.Min(TotalTrades, Config.MinTrades * 2) / (Config.MinTrades * 2.0)) * 0.10;
            
            // Profit factor (5%)
            score += (Math.Min(ProfitFactor, 3) / 3.0) * 0.05;

            return score;
        }
    }

    /// <summary>
    /// Is this configuration viable for live trading?
    /// </summary>
    public bool IsViable => ViabilityScore >= 0.75 && PassesAllCriteria;

    /// <summary>
    /// Recommendation for next steps.
    /// </summary>
    public string Recommendation
    {
        get
        {
            if (IsViable)
                return "Configuration is ready for paper trading validation.";
            
            var issues = new List<string>();
            if (!MeetsWinRateTarget)
                issues.Add($"Win rate {OutOfSampleWinRate*100:F1}% below target {Config.TargetWinRate*100:F0}%");
            if (!MeetsTradeRequirement)
                issues.Add($"Only {TotalTrades} trades, need {Config.MinTrades}+");
            if (!MeetsDegradationLimit)
                issues.Add($"Performance degradation {PerformanceDegradation*100:F1}% exceeds limit");
            if (!IsStatisticallySignificant)
                issues.Add($"Not statistically significant (p={PValue:F3})");

            return $"Not viable: {string.Join("; ", issues)}";
        }
    }

    public string GetFullReport()
    {
        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine($"""
            +======================================================================+
            | SCIENTIFIC OPTIMIZATION REPORT: {Symbol,-27}        |
            +======================================================================+
            | Duration:           {OptimizationDuration.TotalMinutes,8:F1} minutes                       |
            +----------------------------------------------------------------------+
            
            ╔══════════════════════════════════════════════════════════════════════╗
            ║  VIABILITY ASSESSMENT                                                ║
            ╠══════════════════════════════════════════════════════════════════════╣
            ║  Overall Score:     {ViabilityScore * 100,6:F1}%                                         ║
            ║  Is Viable:         {(IsViable ? "YES - Ready for paper trading" : "NO - See issues below"),-40} ║
            ║  Recommendation:    {Recommendation,-40} ║
            ╚══════════════════════════════════════════════════════════════════════╝
            
            PERFORMANCE METRICS
            +----------------------------------------------------------------------+
            | In-Sample Win Rate:     {InSampleWinRate * 100,8:F2}%                           |
            | Out-of-Sample Win Rate: {OutOfSampleWinRate * 100,8:F2}%  ({(OutOfSampleWinRate >= Config.TargetWinRate ? "PASS" : "FAIL")})        |
            | Performance Degradation:{PerformanceDegradation * 100,8:F2}%  ({(MeetsDegradationLimit ? "PASS" : "FAIL")})        |
            | Total Trades:           {TotalTrades,8}     ({(MeetsTradeRequirement ? "PASS" : "FAIL")})        |
            | Total P&L:              ${TotalPnL,10:N2}                         |
            | Profit Factor:          {ProfitFactor,8:F2}                              |
            | Max Drawdown:           {MaxDrawdown,8:F2}%                             |
            | Sharpe Ratio:           {SharpeRatio,8:F2}                              |
            +----------------------------------------------------------------------+
            
            STATISTICAL VALIDATION
            +----------------------------------------------------------------------+
            | p-value:                {PValue,12:F6}  ({(IsStatisticallySignificant ? "SIGNIFICANT" : "NOT SIG")})   |
            | 95% Confidence:         [{WinRateSignificance?.ConfidenceIntervalLow*100:F1}%, {WinRateSignificance?.ConfidenceIntervalHigh*100:F1}%]                 |
            """);

        if (MonteCarloResult != null)
        {
            sb.AppendLine($"""
            | Monte Carlo p-value:    {MonteCarloResult.PValue,12:F6}                      |
            """);
        }

        sb.AppendLine("+----------------------------------------------------------------------+");

        if (WalkForwardResult != null)
        {
            sb.AppendLine($"""
            
            WALK-FORWARD VALIDATION
            +----------------------------------------------------------------------+
            | Windows Tested:         {WalkForwardResult.Windows.Count,8}                              |
            | Success Rate:           {WalkForwardResult.SuccessRate * 100,8:F1}%                             |
            | Windows >= 80% WR:      {WalkForwardResult.WindowsAbove80Percent,3} / {WalkForwardResult.Windows.Count,-3} ({WalkForwardResult.HighPerformanceRate * 100:F1}%)                  |
            | Avg Training WR:        {WalkForwardResult.AvgTrainingWinRate * 100,8:F1}%                             |
            | Avg Testing WR:         {WalkForwardResult.AvgTestingWinRate * 100,8:F1}%                             |
            +----------------------------------------------------------------------+
            """);
        }

        sb.AppendLine($"""
            
            OPTIMIZED CONFIGURATION
            +----------------------------------------------------------------------+
            | INDICATOR WEIGHTS
            |   VWAP:      {BestConfig.Weights.Vwap * 100,5:F1}%
            |   EMA:       {BestConfig.Weights.Ema * 100,5:F1}%
            |   RSI:       {BestConfig.Weights.Rsi * 100,5:F1}%
            |   MACD:      {BestConfig.Weights.Macd * 100,5:F1}%
            |   ADX:       {BestConfig.Weights.Adx * 100,5:F1}%
            |   Volume:    {BestConfig.Weights.Volume * 100,5:F1}%
            |
            | THRESHOLDS
            |   Long Entry:    >= {BestConfig.LongEntryThreshold,3}
            |   Short Entry:   <= {BestConfig.ShortEntryThreshold,3}
            |   Long Exit:     <  {BestConfig.LongExitThreshold,3}
            |   Short Exit:    >  {BestConfig.ShortExitThreshold,3}
            |
            | RISK MANAGEMENT
            |   Take Profit:   {BestConfig.TakeProfitAtr,4:F1}x ATR
            |   Stop Loss:     {BestConfig.StopLossAtr,4:F1}x ATR
            |   Min Volume:    {BestConfig.MinVolumeRatio,4:F1}x avg
            |   Min ADX:       {BestConfig.MinAdx,4:F0}
            +----------------------------------------------------------------------+
            """);

        return sb.ToString();
    }
}

/// <summary>
/// Master optimizer that orchestrates all components to achieve target win rate.
/// </summary>
public sealed class ScientificOptimizer
{
    private readonly ScientificOptimizationConfig _config;

    public ScientificOptimizer(ScientificOptimizationConfig? config = null)
    {
        _config = config ?? ScientificOptimizationConfig.Default;
    }

    /// <summary>
    /// Runs the complete scientific optimization pipeline.
    /// </summary>
    /// <param name="symbol">Symbol to optimize.</param>
    /// <param name="bars">Historical price bars.</param>
    /// <param name="evaluator">Function that simulates trading with a config.</param>
    /// <param name="progress">Optional progress reporter.</param>
    public ScientificOptimizationResult Optimize(
        string symbol,
        IReadOnlyList<HistoricalBar> bars,
        Func<IReadOnlyList<HistoricalBar>, OptimizableConfig, ModelSimResult> evaluator,
        IProgress<(string phase, int percent)>? progress = null)
    {
        var startTime = DateTime.Now;
        
        progress?.Report(("Initializing", 0));

        if (bars.Count < 100)
        {
            return CreateFailedResult(symbol, "Insufficient data (need 100+ bars)");
        }

        // Split data for validation
        var (trainingData, testingData) = WalkForwardAnalyzer.SplitData(bars, 0.7);

        progress?.Report(("Optimizing weights", 10));

        // Phase 1: Optimize weights on training data
        OptimizableConfig bestConfig;
        GeneticOptimizationResult? geneticResult = null;

        if (_config.UseGeneticOptimization)
        {
            var geneticOptimizer = new WeightOptimizer(_config.GeneticConfig);
            
            geneticResult = geneticOptimizer.Optimize(
                symbol,
                config => EvaluateConfig(trainingData, config, evaluator),
                new Progress<int>(p => progress?.Report(("Genetic optimization", 10 + p / 2)))
            );

            bestConfig = geneticResult.BestConfig;
        }
        else
        {
            bestConfig = WeightOptimizer.GridSearch(
                config => EvaluateConfig(trainingData, config, evaluator),
                new Progress<int>(p => progress?.Report(("Grid search", 10 + p / 2)))
            );
        }

        progress?.Report(("Validating", 60));

        // Phase 2: Evaluate on training and testing data
        var trainingResult = evaluator(trainingData, bestConfig);
        var testingResult = evaluator(testingData, bestConfig);

        double inSampleWinRate = trainingResult.Trades.Count > 0 
            ? (double)trainingResult.Trades.Count(t => t.PnL > 0) / trainingResult.Trades.Count : 0;
        double outOfSampleWinRate = testingResult.Trades.Count > 0 
            ? (double)testingResult.Trades.Count(t => t.PnL > 0) / testingResult.Trades.Count : 0;

        // Phase 3: Walk-forward validation
        WalkForwardResult? walkForwardResult = null;
        if (_config.UseWalkForward && bars.Count >= _config.WalkForwardConfig.TrainingBars + _config.WalkForwardConfig.TestingBars)
        {
            progress?.Report(("Walk-forward analysis", 70));

            var wfAnalyzer = new WalkForwardAnalyzer(_config.WalkForwardConfig);
            walkForwardResult = wfAnalyzer.Analyze(
                symbol,
                bars,
                trainingBars =>
                {
                    var opt = new WeightOptimizer(GeneticOptimizationConfig.Fast);
                    return opt.Optimize(symbol, c => EvaluateConfig(trainingBars, c, evaluator)).BestConfig;
                },
                (testBars, config) =>
                {
                    var result = evaluator(testBars, config);
                    return ConvertToWindowPerformance(result);
                },
                new Progress<int>(p => progress?.Report(("Walk-forward", 70 + p / 5)))
            );
        }

        progress?.Report(("Statistical testing", 90));

        // Phase 4: Statistical significance testing
        var allTestTrades = testingResult.Trades;
        int wins = allTestTrades.Count(t => t.PnL > 0);
        int totalTrades = allTestTrades.Count;
        double avgWin = allTestTrades.Where(t => t.PnL > 0).Select(t => t.PnL).DefaultIfEmpty(0).Average();
        double avgLoss = Math.Abs(allTestTrades.Where(t => t.PnL <= 0).Select(t => t.PnL).DefaultIfEmpty(0).Average());

        var winRateTest = StatisticalTests.WinRateTest(wins, totalTrades);

        StatisticalTestResult? monteCarloResult = null;
        if (_config.RunMonteCarloValidation && totalTrades >= 10)
        {
            monteCarloResult = StatisticalTests.MonteCarloTest(
                wins, totalTrades, avgWin, avgLoss, _config.MonteCarloSimulations);
        }

        progress?.Report(("Complete", 100));

        // Calculate Sharpe ratio
        var returns = allTestTrades.Select(t => t.PnLPercent / 100).ToArray();
        double sharpeRatio = returns.Length >= 2 ? StatisticalTests.CalculateSharpeRatio(returns) : 0;

        return new ScientificOptimizationResult
        {
            Symbol = symbol,
            BestConfig = bestConfig,
            Config = _config,
            InSampleWinRate = inSampleWinRate,
            OutOfSampleWinRate = outOfSampleWinRate,
            TotalTrades = totalTrades,
            TotalPnL = testingResult.TotalPnL,
            ProfitFactor = testingResult.ProfitFactor,
            MaxDrawdown = testingResult.MaxDrawdownPercent,
            SharpeRatio = sharpeRatio,
            WinRateSignificance = winRateTest,
            MonteCarloResult = monteCarloResult,
            WalkForwardResult = walkForwardResult,
            GeneticResult = geneticResult,
            OptimizationDuration = DateTime.Now - startTime
        };
    }

    /// <summary>
    /// Runs optimization in parallel across multiple symbols.
    /// </summary>
    public Dictionary<string, ScientificOptimizationResult> OptimizeMultiple(
        Dictionary<string, IReadOnlyList<HistoricalBar>> symbolData,
        Func<IReadOnlyList<HistoricalBar>, OptimizableConfig, ModelSimResult> evaluator,
        int maxParallelism = 4)
    {
        var results = new ConcurrentDictionary<string, ScientificOptimizationResult>();

        Parallel.ForEach(
            symbolData,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
            kvp =>
            {
                var result = Optimize(kvp.Key, kvp.Value, evaluator);
                results[kvp.Key] = result;
            });

        return new Dictionary<string, ScientificOptimizationResult>(results);
    }

    /// <summary>
    /// Creates a TickerProfile from optimization results.
    /// </summary>
    public TickerProfile CreateTickerProfile(ScientificOptimizationResult result)
    {
        return new TickerProfile
        {
            Symbol = result.Symbol,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TotalTrades = result.TotalTrades,
            TotalWins = (int)(result.TotalTrades * result.OutOfSampleWinRate),
            TotalLosses = result.TotalTrades - (int)(result.TotalTrades * result.OutOfSampleWinRate),
            TotalPnL = result.TotalPnL,
            OptimalLongEntryThreshold = result.BestConfig.LongEntryThreshold,
            OptimalShortEntryThreshold = result.BestConfig.ShortEntryThreshold,
            OptimalLongExitThreshold = result.BestConfig.LongExitThreshold,
            OptimalShortExitThreshold = result.BestConfig.ShortExitThreshold
        };
    }

    private static (int trades, int wins, double pnl, double profitFactor, double drawdown) EvaluateConfig(
        IReadOnlyList<HistoricalBar> bars,
        OptimizableConfig config,
        Func<IReadOnlyList<HistoricalBar>, OptimizableConfig, ModelSimResult> evaluator)
    {
        var result = evaluator(bars, config);
        int wins = result.WinCount;
        return (result.Trades.Count, wins, result.TotalPnL, result.ProfitFactor, result.MaxDrawdownPercent);
    }

    private static WindowPerformance ConvertToWindowPerformance(ModelSimResult result)
    {
        return new WindowPerformance
        {
            TotalTrades = result.Trades.Count,
            WinningTrades = result.WinCount,
            LosingTrades = result.LossCount,
            TotalPnL = result.TotalPnL,
            MaxDrawdown = result.MaxDrawdownPercent,
            ProfitFactor = result.ProfitFactor,
            SharpeRatio = 0 // Calculate if needed
        };
    }

    private ScientificOptimizationResult CreateFailedResult(string symbol, string reason)
    {
        return new ScientificOptimizationResult
        {
            Symbol = symbol,
            BestConfig = new OptimizableConfig(),
            Config = _config,
            InSampleWinRate = 0,
            OutOfSampleWinRate = 0,
            TotalTrades = 0,
            TotalPnL = 0,
            ProfitFactor = 0,
            MaxDrawdown = 0,
            SharpeRatio = 0,
            OptimizationDuration = TimeSpan.Zero
        };
    }
}

/// <summary>
/// Result from simulating a configuration on historical data.
/// </summary>
public sealed class OptimizationSimResult
{
    public required List<OptimizationTrade> Trades { get; init; }
    public double TotalPnL => Trades.Sum(t => t.PnL);
    public double ProfitFactor
    {
        get
        {
            double grossProfit = Trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
            double grossLoss = Math.Abs(Trades.Where(t => t.PnL < 0).Sum(t => t.PnL));
            return grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 100 : 0;
        }
    }
    public double MaxDrawdownPercent { get; init; }
}

/// <summary>
/// A single simulated trade for optimization purposes.
/// </summary>
public sealed record OptimizationTrade
{
    public DateTime EntryTime { get; init; }
    public DateTime ExitTime { get; init; }
    public double EntryPrice { get; init; }
    public double ExitPrice { get; init; }
    public bool IsLong { get; init; }
    public int Quantity { get; init; }
    public double PnL => IsLong ? (ExitPrice - EntryPrice) * Quantity : (EntryPrice - ExitPrice) * Quantity;
    public double PnLPercent => IsLong ? (ExitPrice - EntryPrice) / EntryPrice * 100 : (EntryPrice - ExitPrice) / EntryPrice * 100;
    public bool IsWin => PnL > 0;
}

/// <summary>
/// Extension methods for clamping values.
/// </summary>
internal static class MathExtensions
{
    public static double Clamp(this double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
