// ============================================================================
// Strategy Optimizer - Finds optimal strategy parameters
// ============================================================================
//
// Uses grid search and/or genetic algorithms to find the best parameters
// that maximize profit for a given historical session.
//
// ============================================================================

using IdiotProof.Models;
using IdiotProof.Services;

namespace IdiotProof.Optimization;

/// <summary>
/// Configuration for the optimization process.
/// </summary>
public sealed record OptimizationConfig
{
    /// <summary>Minimum entry price to test.</summary>
    public double MinEntry { get; init; }

    /// <summary>Maximum entry price to test.</summary>
    public double MaxEntry { get; init; }

    /// <summary>Entry price step size.</summary>
    public double EntryStep { get; init; } = 0.05;

    /// <summary>Take profit percentages to test.</summary>
    public double[] TakeProfitPercents { get; init; } = [2, 3, 5, 7, 10, 15, 20];

    /// <summary>Stop loss percentages to test.</summary>
    public double[] StopLossPercents { get; init; } = [2, 3, 5, 7, 10];

    /// <summary>Trailing stop percentages to test (null = no trailing stop).</summary>
    public double?[] TrailingStopPercents { get; init; } = [null, 5, 8, 10, 12, 15];

    /// <summary>Whether to test above VWAP requirement.</summary>
    public bool[] RequireAboveVwapOptions { get; init; } = [true, false];

    /// <summary>Whether to test higher lows requirement.</summary>
    public bool[] RequireHigherLowsOptions { get; init; } = [true, false];

    /// <summary>Maximum number of results to return.</summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>Minimum R:R ratio to consider.</summary>
    public double MinRiskRewardRatio { get; init; } = 1.0;

    /// <summary>Position size for calculations.</summary>
    public int Quantity { get; init; } = 100;

    /// <summary>
    /// Creates a config from session data.
    /// </summary>
    public static OptimizationConfig FromSession(BackTestSession session, double rangePercent = 0.1)
    {
        double range = session.High - session.Low;
        double buffer = range * rangePercent;

        return new OptimizationConfig
        {
            MinEntry = session.Low + buffer,
            MaxEntry = session.High - buffer,
            EntryStep = range / 50  // ~50 entry points to test
        };
    }
}

/// <summary>
/// Result of an optimization run.
/// </summary>
public sealed record OptimizationResult
{
    /// <summary>The optimized parameters.</summary>
    public required StrategyParameters Parameters { get; init; }

    /// <summary>The simulation result for these parameters.</summary>
    public required SimulationResult SimulationResult { get; init; }

    /// <summary>Optimization score (higher is better).</summary>
    public double Score { get; init; }

    /// <summary>Rank in the optimization results.</summary>
    public int Rank { get; init; }
}

/// <summary>
/// Finds optimal strategy parameters through grid search.
/// </summary>
public sealed class StrategyOptimizer
{
    private readonly BackTestSession _session;
    private readonly StrategySimulator _simulator;

    public StrategyOptimizer(BackTestSession session)
    {
        _session = session;
        _simulator = new StrategySimulator(session);
    }

    /// <summary>
    /// Runs grid search optimization to find the best parameters.
    /// </summary>
    public List<OptimizationResult> Optimize(OptimizationConfig config, IProgress<int>? progress = null)
    {
        var results = new List<OptimizationResult>();
        int totalIterations = CalculateTotalIterations(config);
        int currentIteration = 0;

        // Grid search over all parameter combinations
        for (double entry = config.MinEntry; entry <= config.MaxEntry; entry += config.EntryStep)
        {
            foreach (var tpPct in config.TakeProfitPercents)
            {
                foreach (var slPct in config.StopLossPercents)
                {
                    // Skip if R:R ratio is too low
                    if (tpPct / slPct < config.MinRiskRewardRatio) continue;

                    foreach (var tslPct in config.TrailingStopPercents)
                    {
                        foreach (var requireVwap in config.RequireAboveVwapOptions)
                        {
                            foreach (var requireHL in config.RequireHigherLowsOptions)
                            {
                                currentIteration++;
                                progress?.Report(currentIteration * 100 / totalIterations);

                                var parameters = StrategyParameters.FromPercent(
                                    entry: entry,
                                    takeProfitPct: tpPct,
                                    stopLossPct: slPct,
                                    quantity: config.Quantity,
                                    trailingStopPct: tslPct
                                ) with
                                {
                                    RequireAboveVwap = requireVwap,
                                    RequireHigherLows = requireHL
                                };

                                var simResult = _simulator.Simulate(parameters);

                                // Only add if there were trades
                                if (simResult.Trades.Count > 0)
                                {
                                    double score = CalculateScore(simResult);
                                    
                                    results.Add(new OptimizationResult
                                    {
                                        Parameters = parameters,
                                        SimulationResult = simResult,
                                        Score = score
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        // Sort by score and rank
        return results
            .OrderByDescending(r => r.Score)
            .Take(config.MaxResults)
            .Select((r, i) => r with { Rank = i + 1 })
            .ToList();
    }

    /// <summary>
    /// Calculates a composite score for ranking strategies.
    /// </summary>
    private static double CalculateScore(SimulationResult result)
    {
        // Weighted scoring:
        // - Total PnL (40%)
        // - Win rate (20%)
        // - Profit factor (20%)
        // - Low drawdown (20%)

        double pnlScore = result.TotalPnL;
        double winRateScore = result.WinRate * 10;  // 0-1000
        double pfScore = Math.Min(result.ProfitFactor, 10) * 50;  // Cap at 10, scale to 500
        double ddScore = Math.Max(0, 100 - result.MaxDrawdownPercent) * 5;  // Lower is better

        return pnlScore * 0.4 + winRateScore * 0.2 + pfScore * 0.2 + ddScore * 0.2;
    }

    private static int CalculateTotalIterations(OptimizationConfig config)
    {
        int entrySteps = (int)((config.MaxEntry - config.MinEntry) / config.EntryStep) + 1;
        return entrySteps
            * config.TakeProfitPercents.Length
            * config.StopLossPercents.Length
            * config.TrailingStopPercents.Length
            * config.RequireAboveVwapOptions.Length
            * config.RequireHigherLowsOptions.Length;
    }

    /// <summary>
    /// Finds the perfect entry/exit that would have maximized profit.
    /// (Hindsight analysis - useful for understanding the day's potential)
    /// </summary>
    public (double bestEntry, double bestExit, double maxProfit) FindPerfectTrade()
    {
        double bestEntry = 0;
        double bestExit = 0;
        double maxProfit = 0;

        var candles = _session.Candles;

        // Find the lowest low followed by the highest high
        for (int i = 0; i < candles.Count; i++)
        {
            double entryPrice = candles[i].Low;

            for (int j = i + 1; j < candles.Count; j++)
            {
                double exitPrice = candles[j].High;
                double profit = exitPrice - entryPrice;

                if (profit > maxProfit)
                {
                    maxProfit = profit;
                    bestEntry = entryPrice;
                    bestExit = exitPrice;
                }
            }
        }

        return (bestEntry, bestExit, maxProfit);
    }

    /// <summary>
    /// Generates an IdiotScript strategy from the best parameters.
    /// </summary>
    public string GenerateIdiotScript(OptimizationResult result)
    {
        var p = result.Parameters;
        var lines = new List<string>
        {
            $"# Auto-generated strategy from backtesting optimization",
            $"# Symbol: {_session.Symbol} | Date: {_session.Date:yyyy-MM-dd}",
            $"# Score: {result.Score:F2} | Rank: #{result.Rank}",
            $"# PnL: ${result.SimulationResult.TotalPnL:F2} | Win Rate: {result.SimulationResult.WinRate:F1}%",
            $"",
            $"Ticker({_session.Symbol})",
            $".Name(\"{_session.Symbol} Optimized {_session.Date:MMdd}\")",
            $".Session(IS.RTH)",
            $".Quantity({p.Quantity})",
            $".Entry({p.EntryPrice:F2})"
        };

        if (p.PullbackPrice.HasValue)
            lines.Add($".Pullback({p.PullbackPrice.Value:F2})");

        if (p.RequireAboveVwap)
            lines.Add(".IsAboveVwap()");

        if (p.RequireHigherLows)
            lines.Add(".IsHigherLows()");

        lines.Add($".TakeProfit({p.TakeProfitPrice:F2})");
        lines.Add($".StopLoss({p.StopLossPrice:F2})");

        if (p.TrailingStopPercent.HasValue)
            lines.Add($".TrailingStopLoss({p.TrailingStopPercent.Value:F0})");

        lines.Add(".ExitStrategy(IS.BELL).IsProfitable()");

        return string.Join("\n", lines);
    }
}
