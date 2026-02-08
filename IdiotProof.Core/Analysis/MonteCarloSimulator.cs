// ============================================================================
// Monte Carlo Simulator - Risk analysis through randomization
// ============================================================================
//
// Monte Carlo simulation helps assess the range of possible outcomes
// by running many iterations with randomized trade sequences and/or
// slight parameter variations.
//
// ============================================================================

using IdiotProof.Models;

namespace IdiotProof.Analysis;

/// <summary>
/// Configuration for Monte Carlo simulation.
/// </summary>
public sealed record MonteCarloConfig
{
    /// <summary>Number of iterations to run.</summary>
    public int Iterations { get; init; } = 1000;

    /// <summary>Confidence level for risk metrics (e.g., 95%).</summary>
    public double ConfidenceLevel { get; init; } = 0.95;

    /// <summary>Whether to shuffle trade order.</summary>
    public bool ShuffleTrades { get; init; } = true;

    /// <summary>Whether to randomly skip some trades (simulate missed entries).</summary>
    public bool RandomSkipTrades { get; init; } = false;

    /// <summary>Probability of skipping a trade (0-1).</summary>
    public double SkipProbability { get; init; } = 0.1;

    /// <summary>Whether to add random slippage.</summary>
    public bool AddSlippage { get; init; } = true;

    /// <summary>Maximum slippage per share in dollars.</summary>
    public double MaxSlippage { get; init; } = 0.05;
}

/// <summary>
/// Results from a single Monte Carlo iteration.
/// </summary>
public sealed record MonteCarloIteration
{
    /// <summary>Iteration number.</summary>
    public int IterationNumber { get; init; }

    /// <summary>Final PnL for this iteration.</summary>
    public double FinalPnL { get; init; }

    /// <summary>Maximum drawdown encountered.</summary>
    public double MaxDrawdown { get; init; }

    /// <summary>Number of trades executed.</summary>
    public int TradeCount { get; init; }

    /// <summary>Win rate for this iteration.</summary>
    public double WinRate { get; init; }
}

/// <summary>
/// Complete Monte Carlo simulation results.
/// </summary>
public sealed class MonteCarloResult
{
    private readonly List<double> _sortedPnLs;
    private readonly List<double> _sortedDrawdowns;

    public MonteCarloResult(List<MonteCarloIteration> iterations)
    {
        Iterations = iterations;
        _sortedPnLs = iterations.Select(i => i.FinalPnL).OrderBy(x => x).ToList();
        _sortedDrawdowns = iterations.Select(i => i.MaxDrawdown).OrderByDescending(x => x).ToList();
    }

    /// <summary>All iteration results.</summary>
    public List<MonteCarloIteration> Iterations { get; }

    /// <summary>Number of iterations run.</summary>
    public int IterationCount => Iterations.Count;

    // ========================================================================
    // PnL Statistics
    // ========================================================================

    /// <summary>Average PnL across all iterations.</summary>
    public double MeanPnL => Iterations.Count > 0 ? Iterations.Average(i => i.FinalPnL) : 0;

    /// <summary>Median PnL.</summary>
    public double MedianPnL => GetPercentile(_sortedPnLs, 0.5);

    /// <summary>Standard deviation of PnL.</summary>
    public double StdDevPnL
    {
        get
        {
            if (Iterations.Count < 2) return 0;
            double mean = MeanPnL;
            double variance = Iterations.Sum(i => Math.Pow(i.FinalPnL - mean, 2)) / (Iterations.Count - 1);
            return Math.Sqrt(variance);
        }
    }

    /// <summary>Minimum PnL (worst case).</summary>
    public double MinPnL => _sortedPnLs.Count > 0 ? _sortedPnLs[0] : 0;

    /// <summary>Maximum PnL (best case).</summary>
    public double MaxPnL => _sortedPnLs.Count > 0 ? _sortedPnLs[^1] : 0;

    /// <summary>PnL at given percentile (e.g., 5th percentile for 95% confidence).</summary>
    public double GetPnLAtPercentile(double percentile) => GetPercentile(_sortedPnLs, percentile);

    /// <summary>Probability of profit (% of iterations with positive PnL).</summary>
    public double ProbabilityOfProfit => Iterations.Count > 0
        ? (double)Iterations.Count(i => i.FinalPnL > 0) / Iterations.Count * 100
        : 0;

    // ========================================================================
    // Risk Metrics
    // ========================================================================

    /// <summary>Value at Risk at given confidence level.</summary>
    public double ValueAtRisk(double confidence = 0.95)
    {
        double percentile = 1 - confidence;
        return -GetPnLAtPercentile(percentile);  // VaR is positive for losses
    }

    /// <summary>Expected Shortfall (average loss beyond VaR).</summary>
    public double ExpectedShortfall(double confidence = 0.95)
    {
        double varValue = GetPnLAtPercentile(1 - confidence);
        var tailLosses = _sortedPnLs.Where(p => p <= varValue).ToList();
        return tailLosses.Count > 0 ? -tailLosses.Average() : 0;
    }

    /// <summary>Average maximum drawdown.</summary>
    public double AvgMaxDrawdown => Iterations.Count > 0 ? Iterations.Average(i => i.MaxDrawdown) : 0;

    /// <summary>Worst maximum drawdown at given percentile.</summary>
    public double WorstDrawdown(double percentile = 0.95) => GetPercentile(_sortedDrawdowns, percentile);

    // ========================================================================
    // Helpers
    // ========================================================================

    private static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        double index = percentile * (sortedValues.Count - 1);
        int lowerIndex = (int)Math.Floor(index);
        int upperIndex = (int)Math.Ceiling(index);

        if (lowerIndex == upperIndex)
            return sortedValues[lowerIndex];

        double fraction = index - lowerIndex;
        return sortedValues[lowerIndex] * (1 - fraction) + sortedValues[upperIndex] * fraction;
    }

    public override string ToString()
    {
        return $"""
            +==================================================================+
            | MONTE CARLO SIMULATION RESULTS                                   |
            +==================================================================+
            | Iterations: {IterationCount,8:N0}
            +------------------------------------------------------------------+
            | PnL DISTRIBUTION                                                 |
            +------------------------------------------------------------------+
            | Mean PnL:       ${MeanPnL,10:F2}
            | Median PnL:     ${MedianPnL,10:F2}
            | Std Dev:        ${StdDevPnL,10:F2}
            | Min PnL:        ${MinPnL,10:F2}
            | Max PnL:        ${MaxPnL,10:F2}
            | 5th Percentile: ${GetPnLAtPercentile(0.05),10:F2}
            | 95th Percentile:${GetPnLAtPercentile(0.95),10:F2}
            +------------------------------------------------------------------+
            | RISK METRICS                                                     |
            +------------------------------------------------------------------+
            | Probability of Profit: {ProbabilityOfProfit,6:F1}%
            | VaR (95%):            ${ValueAtRisk(),10:F2}
            | Expected Shortfall:   ${ExpectedShortfall(),10:F2}
            | Avg Max Drawdown:     ${AvgMaxDrawdown,10:F2}
            | Worst Drawdown (95%): ${WorstDrawdown(),10:F2}
            +==================================================================+
            """;
    }
}

/// <summary>
/// Runs Monte Carlo simulations on trading results.
/// </summary>
public sealed class MonteCarloSimulator
{
    private readonly Random _random = new();
    private readonly MonteCarloConfig _config;

    public MonteCarloSimulator(MonteCarloConfig? config = null)
    {
        _config = config ?? new MonteCarloConfig();
    }

    /// <summary>
    /// Runs Monte Carlo simulation on a list of historical trades.
    /// </summary>
    public MonteCarloResult Simulate(List<Trade> trades, IProgress<int>? progress = null)
    {
        var iterations = new List<MonteCarloIteration>();

        for (int i = 0; i < _config.Iterations; i++)
        {
            if (i % 100 == 0)
                progress?.Report(i * 100 / _config.Iterations);

            var iteration = RunIteration(trades, i + 1);
            iterations.Add(iteration);
        }

        progress?.Report(100);
        return new MonteCarloResult(iterations);
    }

    private MonteCarloIteration RunIteration(List<Trade> originalTrades, int iterationNumber)
    {
        // Create a copy we can modify
        var trades = originalTrades.ToList();

        // Shuffle trade order if configured
        if (_config.ShuffleTrades)
        {
            trades = trades.OrderBy(_ => _random.Next()).ToList();
        }

        // Simulate the trades
        double runningPnL = 0;
        double peakPnL = 0;
        double maxDrawdown = 0;
        int tradesExecuted = 0;
        int wins = 0;

        foreach (var trade in trades)
        {
            // Skip trades randomly if configured
            if (_config.RandomSkipTrades && _random.NextDouble() < _config.SkipProbability)
                continue;

            tradesExecuted++;

            // Calculate PnL with optional slippage
            double pnl = trade.PnL;

            if (_config.AddSlippage)
            {
                double slippage = (_random.NextDouble() * 2 - 1) * _config.MaxSlippage * trade.Quantity;
                pnl -= Math.Abs(slippage);  // Slippage always hurts
            }

            if (pnl > 0) wins++;

            runningPnL += pnl;

            if (runningPnL > peakPnL)
                peakPnL = runningPnL;

            double drawdown = peakPnL - runningPnL;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        return new MonteCarloIteration
        {
            IterationNumber = iterationNumber,
            FinalPnL = runningPnL,
            MaxDrawdown = maxDrawdown,
            TradeCount = tradesExecuted,
            WinRate = tradesExecuted > 0 ? (double)wins / tradesExecuted * 100 : 0
        };
    }

    /// <summary>
    /// Generates equity curves for visualization.
    /// </summary>
    public List<double[]> GenerateEquityCurves(List<Trade> trades, int curveCount = 100)
    {
        var curves = new List<double[]>();

        for (int i = 0; i < curveCount; i++)
        {
            var shuffledTrades = trades.OrderBy(_ => _random.Next()).ToList();
            var curve = new double[shuffledTrades.Count + 1];
            curve[0] = 0;  // Starting point

            double runningPnL = 0;
            for (int j = 0; j < shuffledTrades.Count; j++)
            {
                runningPnL += shuffledTrades[j].PnL;
                curve[j + 1] = runningPnL;
            }

            curves.Add(curve);
        }

        return curves;
    }
}
