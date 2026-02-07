// ============================================================================
// Weight Optimizer - Genetic Algorithm for Indicator Weight Optimization
// ============================================================================
//
// Uses evolutionary algorithms to find optimal indicator weights that
// maximize win rate while maintaining statistical significance.
//
// Algorithm:
// 1. Generate initial population of random weight configurations
// 2. Evaluate each configuration via backtesting
// 3. Select top performers (tournament selection)
// 4. Create next generation via crossover and mutation
// 5. Repeat until convergence or max generations
//
// ============================================================================

using IdiotProof.Backend.Models;

namespace IdiotProof.BackTesting.Optimization;

/// <summary>
/// Configuration for the genetic optimization process.
/// </summary>
public sealed record GeneticOptimizationConfig
{
    /// <summary>Number of configurations in each generation.</summary>
    public int PopulationSize { get; init; } = 50;

    /// <summary>Maximum number of generations to evolve.</summary>
    public int MaxGenerations { get; init; } = 100;

    /// <summary>Probability of mutation per gene.</summary>
    public double MutationRate { get; init; } = 0.1;

    /// <summary>Strength of mutations (0-1).</summary>
    public double MutationStrength { get; init; } = 0.15;

    /// <summary>Probability of crossover.</summary>
    public double CrossoverRate { get; init; } = 0.7;

    /// <summary>Number of best individuals to keep (elitism).</summary>
    public int EliteCount { get; init; } = 5;

    /// <summary>Tournament size for selection.</summary>
    public int TournamentSize { get; init; } = 5;

    /// <summary>Stop if no improvement after this many generations.</summary>
    public int EarlyStopGenerations { get; init; } = 15;

    /// <summary>Target win rate to optimize for.</summary>
    public double TargetWinRate { get; init; } = 0.80;

    /// <summary>Minimum trades required for valid evaluation.</summary>
    public int MinTrades { get; init; } = 20;

    /// <summary>Random seed for reproducibility (null = random).</summary>
    public int? RandomSeed { get; init; }

    /// <summary>Weight for win rate in fitness function.</summary>
    public double FitnessWinRateWeight { get; init; } = 0.50;

    /// <summary>Weight for profit factor in fitness function.</summary>
    public double FitnessProfitFactorWeight { get; init; } = 0.25;

    /// <summary>Weight for avoiding drawdown in fitness function.</summary>
    public double FitnessDrawdownWeight { get; init; } = 0.15;

    /// <summary>Weight for trade count (prefer more trades) in fitness.</summary>
    public double FitnessTradeCountWeight { get; init; } = 0.10;

    /// <summary>Default configuration for balanced optimization.</summary>
    public static GeneticOptimizationConfig Default => new();

    /// <summary>Fast optimization for quick testing.</summary>
    public static GeneticOptimizationConfig Fast => new()
    {
        PopulationSize = 20,
        MaxGenerations = 30,
        EarlyStopGenerations = 8
    };

    /// <summary>Thorough optimization for production.</summary>
    public static GeneticOptimizationConfig Thorough => new()
    {
        PopulationSize = 100,
        MaxGenerations = 200,
        MutationStrength = 0.10,
        EarlyStopGenerations = 25
    };
}

/// <summary>
/// Result of evaluating a single configuration.
/// </summary>
public sealed record ConfigEvaluation
{
    public required OptimizableConfig Config { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades : 0;
    public double TotalPnL { get; init; }
    public double ProfitFactor { get; init; }
    public double MaxDrawdown { get; init; }
    public double Fitness { get; init; }
    public int Generation { get; init; }

    /// <summary>Calculates fitness score.</summary>
    public static double CalculateFitness(
        double winRate, double profitFactor, double maxDrawdown, int tradeCount,
        GeneticOptimizationConfig config)
    {
        // Normalize components to 0-1 scale
        double wrScore = winRate;
        double pfScore = Math.Min(profitFactor, 5.0) / 5.0;
        double ddScore = Math.Max(0, 1.0 - maxDrawdown / 20.0); // 20% drawdown = 0 score
        double tcScore = Math.Min(tradeCount, 100) / 100.0;

        // Bonus for exceeding target win rate
        double targetBonus = winRate >= config.TargetWinRate ? 0.2 : 0;

        // Penalty for too few trades
        double tradePenalty = tradeCount < config.MinTrades ? -0.3 : 0;

        return wrScore * config.FitnessWinRateWeight +
               pfScore * config.FitnessProfitFactorWeight +
               ddScore * config.FitnessDrawdownWeight +
               tcScore * config.FitnessTradeCountWeight +
               targetBonus + tradePenalty;
    }
}

/// <summary>
/// Complete result from genetic optimization.
/// </summary>
public sealed class GeneticOptimizationResult
{
    public required string Symbol { get; init; }
    public required OptimizableConfig BestConfig { get; init; }
    public required ConfigEvaluation BestEvaluation { get; init; }
    public int GenerationsRun { get; init; }
    public int TotalEvaluations { get; init; }
    public TimeSpan Duration { get; init; }
    public List<ConfigEvaluation> TopConfigs { get; init; } = [];
    public List<(int Generation, double BestFitness, double AvgFitness)> EvolutionHistory { get; init; } = [];

    /// <summary>Did we achieve the target win rate?</summary>
    public bool AchievedTarget(double targetRate) => BestEvaluation.WinRate >= targetRate;

    public string GetReport()
    {
        return $"""
            +======================================================================+
            | GENETIC OPTIMIZATION RESULTS: {Symbol,-30}        |
            +======================================================================+
            | Generations:     {GenerationsRun,8}                                    |
            | Evaluations:     {TotalEvaluations,8}                                    |
            | Duration:        {Duration.TotalSeconds,8:F1}s                                   |
            +----------------------------------------------------------------------+
            
            BEST CONFIGURATION
            +----------------------------------------------------------------------+
            | Win Rate:        {BestEvaluation.WinRate * 100,8:F2}%                                |
            | Profit Factor:   {BestEvaluation.ProfitFactor,8:F2}                                |
            | Total Trades:    {BestEvaluation.TotalTrades,8}                                  |
            | Total P&L:       ${BestEvaluation.TotalPnL,10:N2}                           |
            | Max Drawdown:    {BestEvaluation.MaxDrawdown,8:F2}%                               |
            | Fitness Score:   {BestEvaluation.Fitness,8:F4}                                |
            +----------------------------------------------------------------------+
            
            OPTIMIZED WEIGHTS
            +----------------------------------------------------------------------+
            | VWAP:     {BestConfig.Weights.Vwap * 100,5:F1}%                                        |
            | EMA:      {BestConfig.Weights.Ema * 100,5:F1}%                                        |
            | RSI:      {BestConfig.Weights.Rsi * 100,5:F1}%                                        |
            | MACD:     {BestConfig.Weights.Macd * 100,5:F1}%                                        |
            | ADX:      {BestConfig.Weights.Adx * 100,5:F1}%                                        |
            | Volume:   {BestConfig.Weights.Volume * 100,5:F1}%                                        |
            +----------------------------------------------------------------------+
            
            OPTIMIZED THRESHOLDS
            +----------------------------------------------------------------------+
            | Long Entry:   >= {BestConfig.LongEntryThreshold,3}                                     |
            | Short Entry:  <= {BestConfig.ShortEntryThreshold,3}                                     |
            | Long Exit:    <  {BestConfig.LongExitThreshold,3}                                     |
            | Short Exit:   >  {BestConfig.ShortExitThreshold,3}                                     |
            | TP Multiplier: {BestConfig.TakeProfitAtr,4:F1}x ATR                                 |
            | SL Multiplier: {BestConfig.StopLossAtr,4:F1}x ATR                                 |
            +----------------------------------------------------------------------+
            """;
    }
}

/// <summary>
/// Genetic algorithm optimizer for finding optimal indicator weights.
/// </summary>
public sealed class WeightOptimizer
{
    private readonly GeneticOptimizationConfig _config;
    private readonly Random _random;

    public WeightOptimizer(GeneticOptimizationConfig? config = null)
    {
        _config = config ?? GeneticOptimizationConfig.Default;
        _random = _config.RandomSeed.HasValue 
            ? new Random(_config.RandomSeed.Value) 
            : new Random();
    }

    /// <summary>
    /// Runs genetic optimization to find the best configuration.
    /// </summary>
    /// <param name="symbol">Symbol being optimized.</param>
    /// <param name="evaluator">Function that evaluates a configuration and returns performance.</param>
    /// <param name="progress">Optional progress reporter.</param>
    public GeneticOptimizationResult Optimize(
        string symbol,
        Func<OptimizableConfig, (int trades, int wins, double pnl, double profitFactor, double drawdown)> evaluator,
        IProgress<int>? progress = null)
    {
        var startTime = DateTime.Now;
        var history = new List<(int, double, double)>();
        int totalEvaluations = 0;

        // Initialize population with diverse configurations
        var population = InitializePopulation();

        // Evaluate initial population
        var evaluations = EvaluatePopulation(population, evaluator, 0);
        totalEvaluations += population.Count;

        double bestFitness = evaluations.Max(e => e.Fitness);
        int generationsWithoutImprovement = 0;

        history.Add((0, bestFitness, evaluations.Average(e => e.Fitness)));
        progress?.Report(0);

        // Evolution loop
        for (int gen = 1; gen <= _config.MaxGenerations; gen++)
        {
            // Select parents
            var parents = SelectParents(evaluations);

            // Create next generation
            var nextGen = new List<OptimizableConfig>();

            // Elitism: keep best individuals
            var elites = evaluations
                .OrderByDescending(e => e.Fitness)
                .Take(_config.EliteCount)
                .Select(e => e.Config)
                .ToList();
            nextGen.AddRange(elites);

            // Fill rest with offspring
            while (nextGen.Count < _config.PopulationSize)
            {
                var parent1 = parents[_random.Next(parents.Count)];
                var parent2 = parents[_random.Next(parents.Count)];

                OptimizableConfig child;
                if (_random.NextDouble() < _config.CrossoverRate)
                {
                    child = parent1.Crossover(parent2, _random);
                }
                else
                {
                    child = parent1;
                }

                if (_random.NextDouble() < _config.MutationRate)
                {
                    child = child.Mutate(_random, _config.MutationStrength);
                }

                nextGen.Add(child);
            }

            population = nextGen;

            // Evaluate new population
            evaluations = EvaluatePopulation(population, evaluator, gen);
            totalEvaluations += population.Count;

            double genBestFitness = evaluations.Max(e => e.Fitness);
            double genAvgFitness = evaluations.Average(e => e.Fitness);
            history.Add((gen, genBestFitness, genAvgFitness));

            // Check for improvement
            if (genBestFitness > bestFitness + 0.001)
            {
                bestFitness = genBestFitness;
                generationsWithoutImprovement = 0;
            }
            else
            {
                generationsWithoutImprovement++;
            }

            progress?.Report(gen * 100 / _config.MaxGenerations);

            // Early stopping
            if (generationsWithoutImprovement >= _config.EarlyStopGenerations)
            {
                break;
            }

            // Check if we achieved target
            var bestEval = evaluations.MaxBy(e => e.Fitness);
            if (bestEval != null && bestEval.WinRate >= _config.TargetWinRate && bestEval.TotalTrades >= _config.MinTrades)
            {
                // Found a solution meeting target, continue a few more generations to refine
                if (generationsWithoutImprovement >= 5)
                    break;
            }
        }

        // Get final best configuration
        var allEvaluations = EvaluatePopulation(population, evaluator, -1);
        var best = allEvaluations.OrderByDescending(e => e.Fitness).First();
        var topConfigs = allEvaluations.OrderByDescending(e => e.Fitness).Take(10).ToList();

        return new GeneticOptimizationResult
        {
            Symbol = symbol,
            BestConfig = best.Config,
            BestEvaluation = best,
            GenerationsRun = history.Count,
            TotalEvaluations = totalEvaluations,
            Duration = DateTime.Now - startTime,
            TopConfigs = topConfigs,
            EvolutionHistory = history
        };
    }

    private List<OptimizableConfig> InitializePopulation()
    {
        var population = new List<OptimizableConfig>();

        // Add known good configurations
        population.Add(new OptimizableConfig()); // Default
        population.Add(new OptimizableConfig { Weights = IndicatorWeights.Momentum });
        population.Add(new OptimizableConfig { Weights = IndicatorWeights.TrendFollowing });
        population.Add(new OptimizableConfig { Weights = IndicatorWeights.MeanReversion });
        population.Add(new OptimizableConfig { Weights = IndicatorWeights.Equal });

        // Add random configurations
        while (population.Count < _config.PopulationSize)
        {
            population.Add(OptimizableConfig.Random(_random));
        }

        return population;
    }

    private List<ConfigEvaluation> EvaluatePopulation(
        List<OptimizableConfig> population,
        Func<OptimizableConfig, (int trades, int wins, double pnl, double profitFactor, double drawdown)> evaluator,
        int generation)
    {
        var evaluations = new List<ConfigEvaluation>();

        foreach (var config in population)
        {
            var (trades, wins, pnl, pf, dd) = evaluator(config);
            double winRate = trades > 0 ? (double)wins / trades : 0;
            double fitness = ConfigEvaluation.CalculateFitness(winRate, pf, dd, trades, _config);

            evaluations.Add(new ConfigEvaluation
            {
                Config = config,
                TotalTrades = trades,
                WinningTrades = wins,
                TotalPnL = pnl,
                ProfitFactor = pf,
                MaxDrawdown = dd,
                Fitness = fitness,
                Generation = generation
            });
        }

        return evaluations;
    }

    private List<OptimizableConfig> SelectParents(List<ConfigEvaluation> evaluations)
    {
        var parents = new List<OptimizableConfig>();
        int targetCount = _config.PopulationSize / 2;

        while (parents.Count < targetCount)
        {
            // Tournament selection
            var tournament = evaluations
                .OrderBy(_ => _random.Next())
                .Take(_config.TournamentSize)
                .OrderByDescending(e => e.Fitness)
                .First();

            parents.Add(tournament.Config);
        }

        return parents;
    }

    /// <summary>
    /// Runs a quick grid search over common weight presets.
    /// Faster than genetic optimization but less thorough.
    /// </summary>
    public static OptimizableConfig GridSearch(
        Func<OptimizableConfig, (int trades, int wins, double pnl, double profitFactor, double drawdown)> evaluator,
        IProgress<int>? progress = null)
    {
        var presets = new List<IndicatorWeights>
        {
            IndicatorWeights.Default,
            IndicatorWeights.Equal,
            IndicatorWeights.Momentum,
            IndicatorWeights.TrendFollowing,
            IndicatorWeights.MeanReversion
        };

        var thresholds = new[] { 60, 65, 70, 75, 80 };
        var tpAtrs = new[] { 1.5, 2.0, 2.5, 3.0 };
        var slAtrs = new[] { 1.0, 1.5, 2.0, 2.5 };

        int total = presets.Count * thresholds.Length * tpAtrs.Length * slAtrs.Length;
        int current = 0;

        OptimizableConfig? bestConfig = null;
        double bestFitness = double.MinValue;

        foreach (var weights in presets)
        {
            foreach (var threshold in thresholds)
            {
                foreach (var tp in tpAtrs)
                {
                    foreach (var sl in slAtrs)
                    {
                        var config = new OptimizableConfig
                        {
                            Weights = weights,
                            LongEntryThreshold = threshold,
                            ShortEntryThreshold = -threshold,
                            LongExitThreshold = threshold - 30,
                            ShortExitThreshold = -(threshold - 30),
                            TakeProfitAtr = tp,
                            StopLossAtr = sl
                        };

                        var (trades, wins, pnl, pf, dd) = evaluator(config);
                        double winRate = trades > 0 ? (double)wins / trades : 0;
                        double fitness = ConfigEvaluation.CalculateFitness(
                            winRate, pf, dd, trades, GeneticOptimizationConfig.Default);

                        if (fitness > bestFitness)
                        {
                            bestFitness = fitness;
                            bestConfig = config;
                        }

                        current++;
                        progress?.Report(current * 100 / total);
                    }
                }
            }
        }

        return bestConfig ?? new OptimizableConfig();
    }
}
