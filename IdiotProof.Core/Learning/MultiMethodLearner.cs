// ============================================================================
// MultiMethodLearner - Three Learning Approaches in One
// ============================================================================
//
// Implements three different learning methods:
// 1. GENETIC - Evolve a population of weight vectors
// 2. NEURAL  - Small neural network that outputs weight adjustments
// 3. GRADIENT - Direct gradient descent on weights
//
// All three use the same train/validation split to measure true learning.
//
// ============================================================================

using IdiotProof.Backend.Models;
using IdiotProof.Backend.Services;
using IdiotProof.BackTesting.Models;
using IdiotProof.Core.Helpers;
using IdiotProof.Core.Settings;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IdiotProof.Core.Learning;

/// <summary>
/// Results from running a learning method.
/// </summary>
public sealed class MethodResult
{
    public string MethodName { get; set; } = "";
    public LearnedWeights BestWeights { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public int GenerationsRun { get; set; }
    
    // Training metrics
    public double TrainingWinRate { get; set; }
    public double TrainingPnL { get; set; }
    public double TrainingFitness { get; set; }
    
    // Validation metrics (THE TRUE MEASURE)
    public double ValidationWinRate { get; set; }
    public double ValidationPnL { get; set; }
    public double ValidationFitness { get; set; }
    public double ValidationSharpe { get; set; }
    
    public List<string> Log { get; set; } = new();
}

/// <summary>
/// Multi-method learning system that tries all three approaches.
/// </summary>
public sealed class MultiMethodLearner
{
    private readonly HistoricalDataService? _histService;
    private readonly HistoricalDataCache _dataCache;
    private readonly string _dataFolder;
    private readonly Random _rng = new();
    private readonly StringBuilder _fullLog = new();
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public MultiMethodLearner(HistoricalDataService? histService)
    {
        _histService = histService;
        _dataCache = new HistoricalDataCache();
        _dataFolder = SettingsManager.GetDataFolder();
    }
    
    /// <summary>
    /// Runs all three learning methods and returns comparison results.
    /// </summary>
    public async Task<LearningComparison> LearnAndCompareAsync(
        string symbol,
        int generationsPerMethod = 50,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var comparison = new LearningComparison { Symbol = symbol };
        var sw = Stopwatch.StartNew();
        
        // Step 1: Load historical data
        progress?.Report($"[LEARN] Loading historical data for {symbol}...");
        var allBars = await LoadHistoricalDataAsync(symbol, ct);
        
        if (allBars.Count < 100)
        {
            progress?.Report($"ERROR: Not enough data for {symbol} (need 100+ bars, have {allBars.Count})");
            return comparison;
        }
        
        // Step 2: Split into train (70%) and validation (30%)
        var (trainBars, valBars) = SplitData(allBars, 0.70);
        progress?.Report($"[DATA] Total: {allBars.Count} bars | Train: {trainBars.Count} | Validation: {valBars.Count}");
        
        // Convert to candles for backtesting
        var trainCandles = ConvertToCandles(trainBars);
        var valCandles = ConvertToCandles(valBars);
        
        progress?.Report($"[LEARN] Running 3 learning methods with {generationsPerMethod} generations each...\n");
        
        // Step 3: Run Genetic Algorithm
        progress?.Report("=== METHOD 1: GENETIC ALGORITHM ===");
        var geneticSw = Stopwatch.StartNew();
        var geneticResult = await RunGeneticAsync(symbol, trainCandles, valCandles, generationsPerMethod, progress, ct);
        comparison.GeneticDuration = geneticSw.Elapsed;
        comparison.GeneticResult = geneticResult.BestWeights;
        PrintMethodSummary(geneticResult, progress);
        
        // Step 4: Run Neural Network
        progress?.Report("\n=== METHOD 2: NEURAL NETWORK ===");
        var neuralSw = Stopwatch.StartNew();
        var neuralResult = await RunNeuralAsync(symbol, trainCandles, valCandles, generationsPerMethod, progress, ct);
        comparison.NeuralDuration = neuralSw.Elapsed;
        comparison.NeuralResult = neuralResult.BestWeights;
        PrintMethodSummary(neuralResult, progress);
        
        // Step 5: Run Gradient Descent
        progress?.Report("\n=== METHOD 3: GRADIENT DESCENT ===");
        var gradientSw = Stopwatch.StartNew();
        var gradientResult = await RunGradientAsync(symbol, trainCandles, valCandles, generationsPerMethod, progress, ct);
        comparison.GradientDuration = gradientSw.Elapsed;
        comparison.GradientResult = gradientResult.BestWeights;
        PrintMethodSummary(gradientResult, progress);
        
        // Step 6: Determine winner (highest VALIDATION fitness)
        var results = new[] { geneticResult, neuralResult, gradientResult };
        var best = results.OrderByDescending(r => r.ValidationFitness).First();
        
        comparison.BestMethod = best.MethodName;
        comparison.BestWeights = best.BestWeights;
        
        // Save the best weights
        SaveWeights(best.BestWeights);
        
        // Print comparison
        progress?.Report("\n" + new string('=', 70));
        progress?.Report("LEARNING COMPARISON RESULTS");
        progress?.Report(new string('=', 70));
        progress?.Report($"{"Method",-12} {"TrainFit",10} {"ValFit",10} {"ValWin%",10} {"ValPnL",10} {"Time",10}");
        progress?.Report(new string('-', 70));
        
        foreach (var r in results.OrderByDescending(x => x.ValidationFitness))
        {
            string marker = r == best ? " * BEST" : "";
            progress?.Report($"{r.MethodName,-12} {r.TrainingFitness,10:F2} {r.ValidationFitness,10:F2} {r.ValidationWinRate,9:F1}% ${r.ValidationPnL,9:F2} {r.Duration:mm\\:ss}{marker}");
        }
        
        progress?.Report(new string('=', 70));
        progress?.Report($"\nBest method: {comparison.BestMethod} (Validation Fitness: {best.ValidationFitness:F2})");
        progress?.Report($"Saved to: {_dataFolder}\\{symbol}.weights.json");
        progress?.Report($"Total time: {sw.Elapsed:mm\\:ss}");
        
        // Save log
        SaveLog(symbol, progress);
        
        return comparison;
    }
    
    // ========================================================================
    // METHOD 1: GENETIC ALGORITHM
    // ========================================================================
    
    private async Task<MethodResult> RunGeneticAsync(
        string symbol,
        List<BackTestCandle> trainData,
        List<BackTestCandle> valData,
        int generations,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        await Task.Yield();
        
        var result = new MethodResult { MethodName = "GENETIC" };
        var sw = Stopwatch.StartNew();
        
        const int populationSize = 20;
        const int eliteCount = 4;
        const double mutationRate = 0.15;
        
        // Initialize population
        var population = new List<LearnedWeights>();
        for (int i = 0; i < populationSize; i++)
        {
            population.Add(LearnedWeights.CreateRandom(symbol, _rng));
        }
        
        LearnedWeights? best = null;
        double bestValFitness = double.MinValue;
        int noImprovementCount = 0;
        
        for (int gen = 1; gen <= generations; gen++)
        {
            ct.ThrowIfCancellationRequested();
            
            // Evaluate all on training data
            var scored = new List<(LearnedWeights w, double trainFit, double valFit)>();
            foreach (var w in population)
            {
                var trainResult = EvaluateWeights(w, trainData);
                var valResult = EvaluateWeights(w, valData);
                scored.Add((w, trainResult.fitness, valResult.fitness));
            }
            
            // Sort by training fitness for selection
            scored = scored.OrderByDescending(x => x.trainFit).ToList();
            
            // Check if we have a new best (by VALIDATION)
            var currentBest = scored.OrderByDescending(x => x.valFit).First();
            if (currentBest.valFit > bestValFitness)
            {
                bestValFitness = currentBest.valFit;
                best = currentBest.w.Clone();
                noImprovementCount = 0;
                progress?.Report($"  Gen {gen,3}: TrainFit={currentBest.trainFit:F1}, ValFit={currentBest.valFit:F1} [NEW BEST]");
            }
            else
            {
                noImprovementCount++;
                if (gen % 10 == 0)
                    progress?.Report($"  Gen {gen,3}: Best TrainFit={scored[0].trainFit:F1}, No improvement for {noImprovementCount} gens");
            }
            
            // Early stopping if no improvement
            if (noImprovementCount >= 15)
            {
                progress?.Report($"  Early stopping at gen {gen} (no improvement for 15 generations)");
                break;
            }
            
            // Selection and breeding
            var newPopulation = new List<LearnedWeights>();
            
            // Keep elites
            for (int i = 0; i < eliteCount; i++)
                newPopulation.Add(scored[i].w.Clone());
            
            // Breed rest
            while (newPopulation.Count < populationSize)
            {
                var parent1 = TournamentSelect(scored, 3);
                var parent2 = TournamentSelect(scored, 3);
                var child = parent1.Crossover(parent2, _rng);
                child = child.Mutate(_rng, mutationRate);
                newPopulation.Add(child);
            }
            
            population = newPopulation;
        }
        
        result.Duration = sw.Elapsed;
        result.GenerationsRun = generations;
        
        if (best != null)
        {
            result.BestWeights = best;
            var trainEval = EvaluateWeights(best, trainData);
            var valEval = EvaluateWeights(best, valData);
            
            result.TrainingFitness = trainEval.fitness;
            result.TrainingWinRate = trainEval.winRate;
            result.TrainingPnL = trainEval.pnl;
            result.ValidationFitness = valEval.fitness;
            result.ValidationWinRate = valEval.winRate;
            result.ValidationPnL = valEval.pnl;
            result.ValidationSharpe = valEval.sharpe;
            
            best.LearningMethod = "genetic";
            best.TrainingFitness = trainEval.fitness;
            best.ValidationFitness = valEval.fitness;
        }
        
        return result;
    }
    
    private LearnedWeights TournamentSelect(List<(LearnedWeights w, double trainFit, double valFit)> pop, int size)
    {
        var contestants = new List<(LearnedWeights w, double trainFit, double valFit)>();
        for (int i = 0; i < size; i++)
        {
            contestants.Add(pop[_rng.Next(pop.Count)]);
        }
        return contestants.OrderByDescending(x => x.trainFit).First().w;
    }
    
    // ========================================================================
    // METHOD 2: NEURAL NETWORK
    // ========================================================================
    
    private async Task<MethodResult> RunNeuralAsync(
        string symbol,
        List<BackTestCandle> trainData,
        List<BackTestCandle> valData,
        int epochs,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        await Task.Yield();
        
        var result = new MethodResult { MethodName = "NEURAL" };
        var sw = Stopwatch.StartNew();
        
        // Simple 2-layer neural network to predict weight adjustments
        // Input: 16 raw indicator values
        // Hidden: 32 neurons
        // Output: 16 weight adjustments
        
        int inputSize = 16;
        int hiddenSize = 32;
        int outputSize = 16;
        
        // Initialize network weights randomly
        var weightsIH = new double[inputSize * hiddenSize];
        var weightsHO = new double[hiddenSize * outputSize];
        var biasH = new double[hiddenSize];
        var biasO = new double[outputSize];
        
        for (int i = 0; i < weightsIH.Length; i++)
            weightsIH[i] = (_rng.NextDouble() - 0.5) * 0.1;
        for (int i = 0; i < weightsHO.Length; i++)
            weightsHO[i] = (_rng.NextDouble() - 0.5) * 0.1;
        
        // Start with a base weight vector
        var baseWeights = LearnedWeights.CreateRandom(symbol, _rng);
        LearnedWeights? best = null;
        double bestValFitness = double.MinValue;
        double learningRate = 0.01;
        int noImprovementCount = 0;
        
        for (int epoch = 1; epoch <= epochs; epoch++)
        {
            ct.ThrowIfCancellationRequested();
            
            // Get average indicator values from training data to feed into network
            var avgIndicators = GetAverageIndicators(trainData);
            
            // Forward pass
            var hidden = new double[hiddenSize];
            for (int h = 0; h < hiddenSize; h++)
            {
                double sum = biasH[h];
                for (int i = 0; i < inputSize; i++)
                    sum += avgIndicators[i] * weightsIH[i * hiddenSize + h];
                hidden[h] = Math.Tanh(sum); // Activation
            }
            
            var output = new double[outputSize];
            for (int o = 0; o < outputSize; o++)
            {
                double sum = biasO[o];
                for (int h = 0; h < hiddenSize; h++)
                    sum += hidden[h] * weightsHO[h * outputSize + o];
                output[o] = sum; // Linear output
            }
            
            // Apply output as adjustments to base weights
            var testWeights = baseWeights.Clone();
            for (int i = 0; i < Math.Min(output.Length, testWeights.IndicatorWeights.Length); i++)
            {
                testWeights.IndicatorWeights[i] += output[i] * 0.1;
            }
            
            // Evaluate
            var trainEval = EvaluateWeights(testWeights, trainData);
            var valEval = EvaluateWeights(testWeights, valData);
            
            // Simple gradient estimation: perturb and measure
            double epsilon = 0.01;
            for (int w = 0; w < weightsHO.Length; w++)
            {
                double original = weightsHO[w];
                weightsHO[w] += epsilon;
                
                // Recalculate output
                for (int o = 0; o < outputSize; o++)
                {
                    double sum = biasO[o];
                    for (int h = 0; h < hiddenSize; h++)
                        sum += hidden[h] * weightsHO[h * outputSize + o];
                    output[o] = sum;
                }
                
                var perturbedWeights = baseWeights.Clone();
                for (int i = 0; i < Math.Min(output.Length, perturbedWeights.IndicatorWeights.Length); i++)
                {
                    perturbedWeights.IndicatorWeights[i] += output[i] * 0.1;
                }
                
                var perturbedEval = EvaluateWeights(perturbedWeights, trainData);
                double gradient = (perturbedEval.fitness - trainEval.fitness) / epsilon;
                
                weightsHO[w] = original + learningRate * gradient;
            }
            
            // Check for improvement
            if (valEval.fitness > bestValFitness)
            {
                bestValFitness = valEval.fitness;
                best = testWeights.Clone();
                noImprovementCount = 0;
                progress?.Report($"  Epoch {epoch,3}: TrainFit={trainEval.fitness:F1}, ValFit={valEval.fitness:F1} [NEW BEST]");
            }
            else
            {
                noImprovementCount++;
                if (epoch % 10 == 0)
                    progress?.Report($"  Epoch {epoch,3}: TrainFit={trainEval.fitness:F1}, No improvement for {noImprovementCount}");
            }
            
            // Early stopping
            if (noImprovementCount >= 15)
            {
                progress?.Report($"  Early stopping at epoch {epoch}");
                break;
            }
            
            // Decay learning rate
            learningRate *= 0.99;
        }
        
        result.Duration = sw.Elapsed;
        result.GenerationsRun = epochs;
        
        if (best != null)
        {
            result.BestWeights = best;
            var trainEval = EvaluateWeights(best, trainData);
            var valEval = EvaluateWeights(best, valData);
            
            result.TrainingFitness = trainEval.fitness;
            result.TrainingWinRate = trainEval.winRate;
            result.TrainingPnL = trainEval.pnl;
            result.ValidationFitness = valEval.fitness;
            result.ValidationWinRate = valEval.winRate;
            result.ValidationPnL = valEval.pnl;
            result.ValidationSharpe = valEval.sharpe;
            
            best.LearningMethod = "neural";
            best.TrainingFitness = trainEval.fitness;
            best.ValidationFitness = valEval.fitness;
        }
        else
        {
            result.BestWeights = baseWeights;
        }
        
        return result;
    }
    
    private double[] GetAverageIndicators(List<BackTestCandle> candles)
    {
        var result = new double[16];
        if (candles.Count == 0) return result;
        
        // Calculate average values
        double avgClose = candles.Average(c => c.Close);
        double avgVwap = candles.Where(c => c.Vwap > 0).DefaultIfEmpty().Average(c => c?.Vwap ?? 0);
        double avgVolume = candles.Average(c => c.Volume);
        
        // Simplified indicator approximations
        result[0] = avgVwap > 0 ? (avgClose - avgVwap) / avgVwap : 0; // VWAP
        result[1] = 0.5; // EMA9 (placeholder)
        result[2] = 0.5; // EMA21
        result[3] = 0.5; // EMA50
        result[4] = 50;  // RSI (neutral)
        result[5] = 0;   // MACD
        result[6] = 25;  // ADX
        result[7] = 1.0; // Volume ratio
        
        return result;
    }
    
    // ========================================================================
    // METHOD 3: GRADIENT DESCENT
    // ========================================================================
    
    private async Task<MethodResult> RunGradientAsync(
        string symbol,
        List<BackTestCandle> trainData,
        List<BackTestCandle> valData,
        int iterations,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        await Task.Yield();
        
        var result = new MethodResult { MethodName = "GRADIENT" };
        var sw = Stopwatch.StartNew();
        
        // Start with random weights
        var currentWeights = LearnedWeights.CreateRandom(symbol, _rng);
        LearnedWeights? best = null;
        double bestValFitness = double.MinValue;
        double learningRate = 0.1;
        int noImprovementCount = 0;
        
        for (int iter = 1; iter <= iterations; iter++)
        {
            ct.ThrowIfCancellationRequested();
            
            var currentEval = EvaluateWeights(currentWeights, trainData);
            var valEval = EvaluateWeights(currentWeights, valData);
            
            // Numerical gradient estimation for key weights
            double epsilon = 0.05;
            
            // Only optimize the most important weights (indicator weights)
            for (int i = 0; i < currentWeights.IndicatorWeights.Length; i++)
            {
                double original = currentWeights.IndicatorWeights[i];
                
                // Positive perturbation
                currentWeights.IndicatorWeights[i] = original + epsilon;
                var plusEval = EvaluateWeights(currentWeights, trainData);
                
                // Negative perturbation
                currentWeights.IndicatorWeights[i] = original - epsilon;
                var minusEval = EvaluateWeights(currentWeights, trainData);
                
                // Gradient
                double gradient = (plusEval.fitness - minusEval.fitness) / (2 * epsilon);
                
                // Update
                currentWeights.IndicatorWeights[i] = original + learningRate * gradient;
                
                // Clamp to reasonable range
                currentWeights.IndicatorWeights[i] = Math.Clamp(currentWeights.IndicatorWeights[i], 0.01, 2.0);
            }
            
            // Also optimize entry biases
            for (int i = 0; i < currentWeights.EntryBiases.Length; i++)
            {
                double original = currentWeights.EntryBiases[i];
                
                currentWeights.EntryBiases[i] = original + epsilon * 10;
                var plusEval = EvaluateWeights(currentWeights, trainData);
                
                currentWeights.EntryBiases[i] = original - epsilon * 10;
                var minusEval = EvaluateWeights(currentWeights, trainData);
                
                double gradient = (plusEval.fitness - minusEval.fitness) / (2 * epsilon * 10);
                currentWeights.EntryBiases[i] = original + learningRate * gradient * 5;
            }
            
            // Check for improvement on validation
            if (valEval.fitness > bestValFitness)
            {
                bestValFitness = valEval.fitness;
                best = currentWeights.Clone();
                noImprovementCount = 0;
                progress?.Report($"  Iter {iter,3}: TrainFit={currentEval.fitness:F1}, ValFit={valEval.fitness:F1} [NEW BEST]");
            }
            else
            {
                noImprovementCount++;
                if (iter % 10 == 0)
                    progress?.Report($"  Iter {iter,3}: TrainFit={currentEval.fitness:F1}, No improvement for {noImprovementCount}");
            }
            
            // Early stopping
            if (noImprovementCount >= 15)
            {
                progress?.Report($"  Early stopping at iter {iter}");
                break;
            }
            
            // Decay learning rate
            learningRate *= 0.98;
        }
        
        result.Duration = sw.Elapsed;
        result.GenerationsRun = iterations;
        
        if (best != null)
        {
            result.BestWeights = best;
            var trainEval = EvaluateWeights(best, trainData);
            var valEval = EvaluateWeights(best, valData);
            
            result.TrainingFitness = trainEval.fitness;
            result.TrainingWinRate = trainEval.winRate;
            result.TrainingPnL = trainEval.pnl;
            result.ValidationFitness = valEval.fitness;
            result.ValidationWinRate = valEval.winRate;
            result.ValidationPnL = valEval.pnl;
            result.ValidationSharpe = valEval.sharpe;
            
            best.LearningMethod = "gradient";
            best.TrainingFitness = trainEval.fitness;
            best.ValidationFitness = valEval.fitness;
        }
        else
        {
            result.BestWeights = currentWeights;
        }
        
        return result;
    }
    
    // ========================================================================
    // EVALUATION
    // ========================================================================
    
    private (double fitness, double winRate, double pnl, double sharpe, int trades) 
        EvaluateWeights(LearnedWeights weights, List<BackTestCandle> candles)
    {
        if (candles.Count < 10)
            return (-1000, 0, 0, 0, 0);
        
        // Simulate trading using these weights
        int trades = 0;
        int wins = 0;
        double totalPnL = 0;
        var returns = new List<double>();
        
        bool inPosition = false;
        bool isLong = false;
        double entryPrice = 0;
        
        for (int i = 50; i < candles.Count; i++) // Skip first 50 for indicator warmup
        {
            var candle = candles[i];
            
            // Build snapshot
            var snapshot = BuildSnapshot(candles, i);
            
            // Calculate score using weights
            var (score, shouldEnterLong, shouldEnterShort, shouldExit) = 
                WeightedScoreCalculator.Calculate(snapshot, weights);
            
            if (!inPosition)
            {
                // Entry logic
                if (shouldEnterLong)
                {
                    inPosition = true;
                    isLong = true;
                    entryPrice = candle.Close;
                }
                else if (shouldEnterShort)
                {
                    inPosition = true;
                    isLong = false;
                    entryPrice = candle.Close;
                }
            }
            else
            {
                // Exit logic - simple profit/loss targets
                double pnl = isLong ? 
                    (candle.Close - entryPrice) / entryPrice * 100 :
                    (entryPrice - candle.Close) / entryPrice * 100;
                
                // Exit on target, stop, or shouldExit signal
                bool shouldTakeProfit = pnl >= 1.0; // 1% profit
                bool shouldStopLoss = pnl <= -0.5;  // 0.5% loss
                
                if (shouldTakeProfit || shouldStopLoss || shouldExit)
                {
                    trades++;
                    totalPnL += pnl;
                    returns.Add(pnl);
                    if (pnl > 0) wins++;
                    
                    inPosition = false;
                }
            }
        }
        
        // Calculate metrics
        double winRate = trades > 0 ? (double)wins / trades * 100 : 0;
        
        // Sharpe ratio
        double sharpe = 0;
        if (returns.Count > 1)
        {
            double avgReturn = returns.Average();
            double stdDev = Math.Sqrt(returns.Sum(r => Math.Pow(r - avgReturn, 2)) / returns.Count);
            sharpe = stdDev > 0 ? avgReturn / stdDev * Math.Sqrt(252) : 0;
        }
        
        // Calculate fitness
        double maxDrawdown = CalculateMaxDrawdown(returns);
        double fitness = WeightedScoreCalculator.CalculateFitness(trades, wins, totalPnL, maxDrawdown, sharpe);
        
        return (fitness, winRate, totalPnL, sharpe, trades);
    }
    
    private ExtendedSnapshot BuildSnapshot(List<BackTestCandle> candles, int index)
    {
        var c = candles[index];
        
        // Calculate EMAs
        double ema9 = CalculateEma(candles, index, 9);
        double ema21 = CalculateEma(candles, index, 21);
        double ema50 = CalculateEma(candles, index, 50);
        
        // Calculate RSI
        double rsi = CalculateRsi(candles, index, 14);
        
        // Calculate ADX (simplified)
        double adx = 25; // Placeholder
        double plusDi = 25;
        double minusDi = 20;
        
        return new ExtendedSnapshot
        {
            Price = c.Close,
            Vwap = c.Vwap,
            Ema9 = ema9,
            Ema21 = ema21,
            Ema50 = ema50,
            Rsi = rsi,
            Macd = 0,
            MacdSignal = 0,
            MacdHistogram = 0,
            Adx = adx,
            PlusDi = plusDi,
            MinusDi = minusDi,
            VolumeRatio = 1.0,
            BollingerUpper = c.Close * 1.02,
            BollingerLower = c.Close * 0.98,
            BollingerMiddle = c.Close,
            Atr = c.High - c.Low,
            TimeOfDay = TimeOnly.FromDateTime(c.Timestamp)
        };
    }
    
    private double CalculateEma(List<BackTestCandle> candles, int endIndex, int period)
    {
        if (endIndex < period) return candles[endIndex].Close;
        
        double multiplier = 2.0 / (period + 1);
        double ema = candles[endIndex - period].Close;
        
        for (int i = endIndex - period + 1; i <= endIndex; i++)
        {
            ema = (candles[i].Close - ema) * multiplier + ema;
        }
        
        return ema;
    }
    
    private double CalculateRsi(List<BackTestCandle> candles, int endIndex, int period)
    {
        if (endIndex < period) return 50;
        
        double gains = 0, losses = 0;
        for (int i = endIndex - period + 1; i <= endIndex; i++)
        {
            double change = candles[i].Close - candles[i - 1].Close;
            if (change > 0) gains += change;
            else losses -= change;
        }
        
        if (losses == 0) return 100;
        double rs = gains / losses;
        return 100 - (100 / (1 + rs));
    }
    
    private double CalculateMaxDrawdown(List<double> returns)
    {
        if (returns.Count == 0) return 0;
        
        double peak = 0;
        double maxDD = 0;
        double cumulative = 0;
        
        foreach (var r in returns)
        {
            cumulative += r;
            peak = Math.Max(peak, cumulative);
            maxDD = Math.Max(maxDD, peak - cumulative);
        }
        
        return maxDD;
    }
    
    // ========================================================================
    // HELPERS
    // ========================================================================
    
    private async Task<List<HistoricalBar>> LoadHistoricalDataAsync(string symbol, CancellationToken ct)
    {
        if (_histService != null)
        {
            return await _dataCache.GetOrFetchIncrementalAsync(
                symbol, _histService, 30, 5, ct);
        }
        else if (_dataCache.HasCachedData(symbol))
        {
            return _dataCache.LoadFromCache(symbol);
        }
        
        return new List<HistoricalBar>();
    }
    
    private (List<HistoricalBar> train, List<HistoricalBar> val) SplitData(
        List<HistoricalBar> all, double trainRatio)
    {
        int trainCount = (int)(all.Count * trainRatio);
        var train = all.Take(trainCount).ToList();
        var val = all.Skip(trainCount).ToList();
        return (train, val);
    }
    
    private List<BackTestCandle> ConvertToCandles(List<HistoricalBar> bars)
    {
        return bars.Select(b => new BackTestCandle
        {
            Timestamp = b.Time,
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume,
            Vwap = b.Vwap ?? 0
        }).ToList();
    }
    
    private void PrintMethodSummary(MethodResult r, IProgress<string>? progress)
    {
        progress?.Report($"  {r.MethodName} completed in {r.Duration:mm\\:ss}");
        progress?.Report($"  Training: Fitness={r.TrainingFitness:F1}, WinRate={r.TrainingWinRate:F1}%, PnL=${r.TrainingPnL:F2}");
        progress?.Report($"  Validation: Fitness={r.ValidationFitness:F1}, WinRate={r.ValidationWinRate:F1}%, PnL=${r.ValidationPnL:F2}, Sharpe={r.ValidationSharpe:F2}");
    }
    
    private void SaveWeights(LearnedWeights weights)
    {
        var path = Path.Combine(_dataFolder, $"{weights.Symbol}.weights.json");
        var json = JsonSerializer.Serialize(weights, JsonOptions);
        File.WriteAllText(path, json);
    }
    
    private void SaveLog(string symbol, IProgress<string>? progress)
    {
        try
        {
            var logsFolder = Path.Combine(Path.GetDirectoryName(_dataFolder) ?? _dataFolder, "Logs");
            Directory.CreateDirectory(logsFolder);
            
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"multilearn_{symbol}_{timestamp}.txt";
            var path = Path.Combine(logsFolder, filename);
            
            File.WriteAllText(path, _fullLog.ToString());
            progress?.Report($"Log saved to: {path}");
        }
        catch { }
    }
}
