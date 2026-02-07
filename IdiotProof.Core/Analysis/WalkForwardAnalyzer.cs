// ============================================================================
// Walk-Forward Analyzer - Validates strategy robustness
// ============================================================================
//
// Walk-forward analysis splits data into training and testing periods,
// optimizes on training, validates on testing, then walks forward.
// This helps detect overfitting and assess real-world performance.
//
// ============================================================================

using IdiotProof.BackTesting.Models;
using IdiotProof.BackTesting.Optimization;
using IdiotProof.BackTesting.Services;

namespace IdiotProof.BackTesting.Analysis;

/// <summary>
/// Configuration for walk-forward analysis.
/// </summary>
public sealed record WalkForwardConfig
{
    /// <summary>Number of candles for optimization (in-sample).</summary>
    public int TrainingWindowSize { get; init; } = 180;  // 3 hours

    /// <summary>Number of candles for testing (out-of-sample).</summary>
    public int TestingWindowSize { get; init; } = 60;   // 1 hour

    /// <summary>Step size between windows.</summary>
    public int StepSize { get; init; } = 30;  // 30 minutes

    /// <summary>Minimum efficiency ratio to consider strategy valid.</summary>
    public double MinEfficiencyRatio { get; init; } = 0.5;
}

/// <summary>
/// Result of a single walk-forward window.
/// </summary>
public sealed record WalkForwardWindow
{
    /// <summary>Window number.</summary>
    public int WindowNumber { get; init; }

    /// <summary>Training period start time.</summary>
    public DateTime TrainingStart { get; init; }

    /// <summary>Training period end time.</summary>
    public DateTime TrainingEnd { get; init; }

    /// <summary>Testing period start time.</summary>
    public DateTime TestingStart { get; init; }

    /// <summary>Testing period end time.</summary>
    public DateTime TestingEnd { get; init; }

    /// <summary>Best parameters found during training.</summary>
    public StrategyParameters? BestParameters { get; init; }

    /// <summary>Training (in-sample) PnL.</summary>
    public double TrainingPnL { get; init; }

    /// <summary>Testing (out-of-sample) PnL.</summary>
    public double TestingPnL { get; init; }

    /// <summary>Efficiency ratio (Testing PnL / Training PnL).</summary>
    public double EfficiencyRatio => TrainingPnL != 0 ? TestingPnL / TrainingPnL : 0;

    /// <summary>Whether this window was profitable out-of-sample.</summary>
    public bool IsValidated => TestingPnL > 0;
}

/// <summary>
/// Complete walk-forward analysis result.
/// </summary>
public sealed class WalkForwardResult
{
    /// <summary>All walk-forward windows.</summary>
    public List<WalkForwardWindow> Windows { get; init; } = [];

    /// <summary>Number of validated (profitable OOS) windows.</summary>
    public int ValidatedCount => Windows.Count(w => w.IsValidated);

    /// <summary>Total number of windows.</summary>
    public int TotalWindows => Windows.Count;

    /// <summary>Validation rate (% of windows that were profitable OOS).</summary>
    public double ValidationRate => TotalWindows > 0 ? (double)ValidatedCount / TotalWindows * 100 : 0;

    /// <summary>Average efficiency ratio across all windows.</summary>
    public double AverageEfficiency => Windows.Count > 0 ? Windows.Average(w => w.EfficiencyRatio) : 0;

    /// <summary>Total out-of-sample PnL.</summary>
    public double TotalOosPnL => Windows.Sum(w => w.TestingPnL);

    /// <summary>Overall robustness score.</summary>
    public double RobustnessScore => ValidationRate * 0.5 + Math.Min(AverageEfficiency * 100, 50);

    public override string ToString()
    {
        return $"""
            +------------------------------------------------------------------+
            | WALK-FORWARD ANALYSIS RESULTS                                    |
            +------------------------------------------------------------------+
            | Windows Tested:     {TotalWindows,6}
            | Windows Validated:  {ValidatedCount,6} ({ValidationRate:F1}%)
            | Avg Efficiency:     {AverageEfficiency,6:F2}
            | Total OOS PnL:     ${TotalOosPnL,8:F2}
            | Robustness Score:   {RobustnessScore,6:F1}/100
            +------------------------------------------------------------------+
            """;
    }
}

/// <summary>
/// Performs walk-forward analysis on historical data.
/// </summary>
public sealed class WalkForwardAnalyzer
{
    private readonly BackTestSession _session;
    private readonly WalkForwardConfig _config;

    public WalkForwardAnalyzer(BackTestSession session, WalkForwardConfig? config = null)
    {
        _session = session;
        _config = config ?? new WalkForwardConfig();
    }

    /// <summary>
    /// Runs walk-forward analysis on the session.
    /// </summary>
    public WalkForwardResult Analyze(IProgress<string>? progress = null)
    {
        var result = new WalkForwardResult();
        var candles = _session.Candles;

        int totalWindowSize = _config.TrainingWindowSize + _config.TestingWindowSize;
        if (candles.Count < totalWindowSize)
        {
            progress?.Report("Insufficient data for walk-forward analysis.");
            return result;
        }

        int windowNumber = 0;
        int startIndex = 0;

        while (startIndex + totalWindowSize <= candles.Count)
        {
            windowNumber++;
            progress?.Report($"Processing window {windowNumber}...");

            // Split data into training and testing periods
            var trainingCandles = candles
                .Skip(startIndex)
                .Take(_config.TrainingWindowSize)
                .ToList();

            var testingCandles = candles
                .Skip(startIndex + _config.TrainingWindowSize)
                .Take(_config.TestingWindowSize)
                .ToList();

            // Create sessions for each period
            var trainingSession = CreateSubSession(trainingCandles, "Training");
            var testingSession = CreateSubSession(testingCandles, "Testing");

            // Optimize on training data
            var optimizer = new StrategyOptimizer(trainingSession);
            var trainingConfig = OptimizationConfig.FromSession(trainingSession);
            var trainingResults = optimizer.Optimize(trainingConfig);

            StrategyParameters? bestParams = null;
            double trainingPnL = 0;
            double testingPnL = 0;

            if (trainingResults.Count > 0)
            {
                bestParams = trainingResults[0].Parameters;
                trainingPnL = trainingResults[0].SimulationResult.TotalPnL;

                // Test on out-of-sample data
                var testSimulator = new StrategySimulator(testingSession);
                var testResult = testSimulator.Simulate(bestParams);
                testingPnL = testResult.TotalPnL;
            }

            result.Windows.Add(new WalkForwardWindow
            {
                WindowNumber = windowNumber,
                TrainingStart = trainingCandles[0].Timestamp,
                TrainingEnd = trainingCandles[^1].Timestamp,
                TestingStart = testingCandles[0].Timestamp,
                TestingEnd = testingCandles[^1].Timestamp,
                BestParameters = bestParams,
                TrainingPnL = trainingPnL,
                TestingPnL = testingPnL
            });

            startIndex += _config.StepSize;
        }

        progress?.Report($"Completed {windowNumber} windows.");
        return result;
    }

    private BackTestSession CreateSubSession(List<BackTestCandle> candles, string suffix)
    {
        var session = new BackTestSession
        {
            Symbol = $"{_session.Symbol}_{suffix}",
            Date = _session.Date,
            Candles = candles
        };

        session.CalculateVwap();
        return session;
    }

    /// <summary>
    /// Generates a detailed walk-forward report.
    /// </summary>
    public string GenerateReport(WalkForwardResult result)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(result.ToString());
        sb.AppendLine();
        sb.AppendLine("| WINDOW DETAILS                                                   |");
        sb.AppendLine("+------------------------------------------------------------------+");

        foreach (var window in result.Windows)
        {
            string status = window.IsValidated ? "[OK]" : "[X] ";
            sb.AppendLine($"| {status} Window #{window.WindowNumber}");
            sb.AppendLine($"|     Train: {window.TrainingStart:HH:mm}-{window.TrainingEnd:HH:mm} | " +
                $"PnL: ${window.TrainingPnL:F2}");
            sb.AppendLine($"|     Test:  {window.TestingStart:HH:mm}-{window.TestingEnd:HH:mm} | " +
                $"PnL: ${window.TestingPnL:F2} | Eff: {window.EfficiencyRatio:F2}");

            if (window.BestParameters != null)
            {
                var p = window.BestParameters;
                sb.AppendLine($"|     Entry: ${p.EntryPrice:F2} | TP: ${p.TakeProfitPrice:F2} | " +
                    $"SL: ${p.StopLossPrice:F2}");
            }
            sb.AppendLine("|");
        }

        sb.AppendLine("+------------------------------------------------------------------+");

        // Interpretation
        sb.AppendLine("| INTERPRETATION                                                   |");
        sb.AppendLine("+------------------------------------------------------------------+");

        if (result.RobustnessScore >= 70)
            sb.AppendLine("| [OK] Strategy appears ROBUST for this day.");
        else if (result.RobustnessScore >= 50)
            sb.AppendLine("| [*]  Strategy shows MODERATE robustness.");
        else if (result.RobustnessScore >= 30)
            sb.AppendLine("| [!]  Strategy shows WEAK robustness - may be overfitted.");
        else
            sb.AppendLine("| [X]  Strategy appears OVERFITTED - not recommended.");

        if (result.AverageEfficiency < 0.3)
            sb.AppendLine("| [!]  Low efficiency - in-sample gains don't translate to OOS.");
        
        if (result.ValidationRate < 50)
            sb.AppendLine("| [!]  Less than half of windows validated.");

        sb.AppendLine("+------------------------------------------------------------------+");

        return sb.ToString();
    }
}
