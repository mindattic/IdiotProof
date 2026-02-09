// ============================================================================
// MultiMethodLearner - Five Learning Approaches in One
// ============================================================================
//
// Implements five different learning methods:
// 1. GENETIC - Evolve a population of weight vectors
// 2. NEURAL  - Small neural network that outputs weight adjustments
// 3. GRADIENT - Direct gradient descent on weights
// 4. LSH     - Locality Sensitive Hashing for pattern matching
// 5. LSTM    - Long Short-Term Memory for sequence prediction
//
// All five use the same train/validation split to measure true learning.
// ALL methods work both OFFLINE (training) and LIVE (real-time trading).
//
// ============================================================================

using IdiotProof.Calculators;
using IdiotProof.Constants;
using IdiotProof.Models;
using IdiotProof.Services;
using IdiotProof.Helpers;
using IdiotProof.Settings;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IdiotProof.Learning;

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
    // ========================================================================
    // CONFIGURATION CONSTANTS
    // ========================================================================
    
    // Genetic Algorithm
    private const int GeneticPopulationSize = 20;
    private const int GeneticEliteCount = 4;
    private const double GeneticMutationRate = 0.15;
    private const int GeneticTournamentSize = 3;
    
    // All methods
    private const int EarlyStoppingPatience = 15;  // Generations without improvement before stopping
    private const int IndicatorWarmupBars = 50;    // Bars needed for indicator warmup
    private const int ForecastHorizonBars = 5;     // Bars ahead for outcome calculation
    
    // Neural Network
    private const int NeuralHiddenSize = 32;
    private const int NeuralInputSize = 16;
    private const int NeuralOutputSize = 16;
    private const double NeuralInitialLearningRate = 0.01;
    private const double NeuralLearningRateDecay = 0.99;
    
    // Gradient Descent
    private const double GradientInitialLearningRate = 0.1;
    private const double GradientLearningRateDecay = 0.98;
    private const double GradientEpsilon = 0.05;
    
    // LSH Pattern Matching
    private const int LshMaxAnalogs = 15;
    private const int LshMaxDistance = 90;
    private const double LshMinConfidence = 0.5;
    
    // LSTM Neural Network
    private const double LstmLearningRate = 0.001;
    private const double LstmMinConfidence = 0.5;
    private const double LstmDirectionThreshold = 0.1;
    
    // Trading Simulation
    private const double TrailingStopPercent = 0.10;  // 10% trailing stop
    
    // ========================================================================
    // FIELDS
    // ========================================================================
    
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
        
        // PERFORMANCE: Pre-compute all snapshots ONCE - O(n) instead of O(n² × evaluations)
        progress?.Report($"[DATA] Pre-computing indicators (this eliminates O(n²) recalculation)...");
        var trainSnapshots = PrecomputeSnapshots(trainCandles);
        var valSnapshots = PrecomputeSnapshots(valCandles);
        progress?.Report($"[DATA] Pre-computed {trainSnapshots.Count} train + {valSnapshots.Count} val snapshots");
        
        progress?.Report($"[LEARN] Running 5 learning methods IN PARALLEL with {generationsPerMethod} generations each...\n");
        
        // Step 3: Run ALL methods in parallel for maximum speed
        progress?.Report("Learning... Genetic Algorithm");
        progress?.Report("Learning... Feedforward Neural Network");
        progress?.Report("Learning... Gradient Descent");
        progress?.Report("Learning... Locality-Sensitive Hashing");
        progress?.Report("Learning... Long Short-Term Memory");
        
        var parallelSw = Stopwatch.StartNew();
        
        // Launch all 5 tasks simultaneously - pass pre-computed snapshots
        var geneticTask = Task.Run(() =>
        {
            var taskSw = Stopwatch.StartNew();
            var result = RunGenetic(symbol, trainCandles, valCandles, trainSnapshots, valSnapshots, generationsPerMethod, null, ct);
            result.Duration = taskSw.Elapsed;
            return result;
        }, ct);
        
        var neuralTask = Task.Run(() =>
        {
            var taskSw = Stopwatch.StartNew();
            var result = RunNeural(symbol, trainCandles, valCandles, trainSnapshots, valSnapshots, generationsPerMethod, null, ct);
            result.Duration = taskSw.Elapsed;
            return result;
        }, ct);
        
        var gradientTask = Task.Run(() =>
        {
            var taskSw = Stopwatch.StartNew();
            var result = RunGradient(symbol, trainCandles, valCandles, trainSnapshots, valSnapshots, generationsPerMethod, null, ct);
            result.Duration = taskSw.Elapsed;
            return result;
        }, ct);
        
        var lshTask = Task.Run(() =>
        {
            var taskSw = Stopwatch.StartNew();
            var result = RunLSH(symbol, trainCandles, valCandles, trainSnapshots, valSnapshots, generationsPerMethod, null, ct);
            result.Duration = taskSw.Elapsed;
            return result;
        }, ct);
        
        var lstmTask = Task.Run(() =>
        {
            var taskSw = Stopwatch.StartNew();
            var result = RunLSTM(symbol, trainCandles, valCandles, trainSnapshots, valSnapshots, generationsPerMethod, null, ct);
            result.Duration = taskSw.Elapsed;
            return result;
        }, ct);
        
        // Wait for all to complete
        await Task.WhenAll(geneticTask, neuralTask, gradientTask, lshTask, lstmTask);
        
        var geneticResult = geneticTask.Result;
        var neuralResult = neuralTask.Result;
        var gradientResult = gradientTask.Result;
        var lshResult = lshTask.Result;
        var lstmResult = lstmTask.Result;
        
        parallelSw.Stop();
        progress?.Report($"=== ALL METHODS COMPLETE (Total: {parallelSw.Elapsed:mm\\:ss\\.fff}) ===\n");
        
        // Store results in comparison
        comparison.GeneticDuration = geneticResult.Duration;
        comparison.GeneticResult = geneticResult.BestWeights;
        
        comparison.NeuralDuration = neuralResult.Duration;
        comparison.NeuralResult = neuralResult.BestWeights;
        
        comparison.GradientDuration = gradientResult.Duration;
        comparison.GradientResult = gradientResult.BestWeights;
        
        comparison.LshDuration = lshResult.Duration;
        comparison.LshResult = lshResult.BestWeights;
        comparison.LshPatternsStored = lshResult.GenerationsRun;
        
        comparison.LstmDuration = lstmResult.Duration;
        comparison.LstmResult = lstmResult.BestWeights;
        comparison.LstmTrainingSamples = lstmResult.GenerationsRun;
        comparison.LstmDirectionAccuracy = lstmResult.ValidationWinRate;
        
        // Print individual summaries
        progress?.Report("--- METHOD RESULTS ---");
        PrintMethodSummary(geneticResult, progress);
        PrintMethodSummary(neuralResult, progress);
        PrintMethodSummary(gradientResult, progress);
        PrintMethodSummary(lshResult, progress);
        PrintMethodSummary(lstmResult, progress);
        
        // Step 4: Determine winner (highest VALIDATION fitness)
        var results = new[] { geneticResult, neuralResult, gradientResult, lshResult, lstmResult };
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
        
        // Step 5: Get AI analysis of the learning results
        progress?.Report("\n=== AI ANALYSIS ===");
        try
        {
            using var advisor = new AIAdvisor();
            if (advisor.IsConfigured)
            {
                var methodSummaries = results.Select(r => new LearningMethodSummary
                {
                    MethodName = r.MethodName,
                    ValidationFitness = r.ValidationFitness,
                    ValidationWinRate = r.ValidationWinRate,
                    ValidationPnL = r.ValidationPnL,
                    IsBest = r == best
                });
                
                var aiAnalysis = await advisor.AnalyzeLearningResultsAsync(
                    symbol, methodSummaries, trainCandles.Count, valCandles.Count, ct);
                
                if (aiAnalysis.IsUsable)
                {
                    comparison.AIRecommendation = aiAnalysis.Action;
                    comparison.AIReasoning = aiAnalysis.Reasoning;
                    comparison.AIConfidence = aiAnalysis.Confidence;
                    
                    progress?.Report($"[AI] Recommendation: {aiAnalysis.Action} (Confidence: {aiAnalysis.Confidence}%)");
                    progress?.Report($"[AI] Reasoning: {aiAnalysis.Reasoning}");
                    if (aiAnalysis.RiskFactors.Count > 0)
                    {
                        progress?.Report($"[AI] Concerns: {string.Join(", ", aiAnalysis.RiskFactors)}");
                    }
                }
                else
                {
                    progress?.Report($"[AI] Analysis unavailable: {aiAnalysis.Error}");
                }
            }
            else
            {
                progress?.Report("[AI] Skipped - OPENAI_IDIOTPROOF_API_KEY not configured");
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"[AI] Error: {ex.Message}");
        }
        
        progress?.Report($"\nTotal time: {sw.Elapsed:mm\\:ss}");
        
        // Save log
        SaveLog(symbol, progress);
        
        return comparison;
    }
    
    // ========================================================================
    // METHOD 1: GENETIC ALGORITHM
    // ========================================================================
    
    private MethodResult RunGenetic(
        string symbol,
        List<BackTestCandle> trainData,
        List<BackTestCandle> valData,
        List<ExtendedSnapshot> trainSnapshots,
        List<ExtendedSnapshot> valSnapshots,
        int generations,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var result = new MethodResult { MethodName = "GENETIC" };
        var sw = Stopwatch.StartNew();
        
        // Initialize population
        var population = new List<LearnedWeights>();
        for (int i = 0; i < GeneticPopulationSize; i++)
        {
            population.Add(LearnedWeights.CreateRandom(symbol, _rng));
        }
        
        LearnedWeights? best = null;
        double bestValFitness = double.MinValue;
        int noImprovementCount = 0;
        
        for (int gen = 1; gen <= generations; gen++)
        {
            ct.ThrowIfCancellationRequested();
            
            // Evaluate all on training data using pre-computed snapshots
            var scored = new List<(LearnedWeights w, double trainFit, double valFit)>();
            foreach (var w in population)
            {
                var trainResult = EvaluateWeightsWithSnapshots(w, trainData, trainSnapshots);
                var valResult = EvaluateWeightsWithSnapshots(w, valData, valSnapshots);
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
            if (noImprovementCount >= EarlyStoppingPatience)
            {
                progress?.Report($"  Early stopping at gen {gen} (no improvement for {EarlyStoppingPatience} generations)");
                break;
            }
            
            // Selection and breeding
            var newPopulation = new List<LearnedWeights>();
            
            // Keep elites
            for (int i = 0; i < GeneticEliteCount; i++)
                newPopulation.Add(scored[i].w.Clone());
            
            // Breed rest
            while (newPopulation.Count < GeneticPopulationSize)
            {
                var parent1 = TournamentSelect(scored, GeneticTournamentSize);
                var parent2 = TournamentSelect(scored, GeneticTournamentSize);
                var child = parent1.Crossover(parent2, _rng);
                child = child.Mutate(_rng, GeneticMutationRate);
                newPopulation.Add(child);
            }
            
            population = newPopulation;
        }
        
        result.Duration = sw.Elapsed;
        result.GenerationsRun = generations;
        
        if (best != null)
        {
            result.BestWeights = best;
            var trainEval = EvaluateWeightsWithSnapshots(best, trainData, trainSnapshots);
            var valEval = EvaluateWeightsWithSnapshots(best, valData, valSnapshots);
            
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
    
    private MethodResult RunNeural(
        string symbol,
        List<BackTestCandle> trainData,
        List<BackTestCandle> valData,
        List<ExtendedSnapshot> trainSnapshots,
        List<ExtendedSnapshot> valSnapshots,
        int epochs,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var result = new MethodResult { MethodName = "NEURAL" };
        var sw = Stopwatch.StartNew();
        
        // Simple 2-layer neural network to predict weight adjustments
        // Input: 16 normalized indicator values
        // Hidden: 32 neurons with tanh activation
        // Output: 16 weight adjustments (linear)
        
        // Initialize network weights randomly (Xavier initialization)
        double xavierIH = Math.Sqrt(6.0 / (NeuralInputSize + NeuralHiddenSize));
        double xavierHO = Math.Sqrt(6.0 / (NeuralHiddenSize + NeuralOutputSize));
        
        var weightsIH = new double[NeuralInputSize * NeuralHiddenSize];
        var weightsHO = new double[NeuralHiddenSize * NeuralOutputSize];
        var biasH = new double[NeuralHiddenSize];
        var biasO = new double[NeuralOutputSize];
        
        for (int i = 0; i < weightsIH.Length; i++)
            weightsIH[i] = (_rng.NextDouble() * 2 - 1) * xavierIH;
        for (int i = 0; i < weightsHO.Length; i++)
            weightsHO[i] = (_rng.NextDouble() * 2 - 1) * xavierHO;
        
        // Start with a base weight vector
        var baseWeights = LearnedWeights.CreateRandom(symbol, _rng);
        LearnedWeights? best = null;
        double bestValFitness = double.MinValue;
        double learningRate = NeuralInitialLearningRate;
        int noImprovementCount = 0;
        
        // Get normalized indicators from snapshots (use last snapshot which is fully warmed up)
        var inputIndicators = GetNormalizedIndicators(trainSnapshots[^1]);
        
        for (int epoch = 1; epoch <= epochs; epoch++)
        {
            ct.ThrowIfCancellationRequested();
            
            // Forward pass
            var hidden = new double[NeuralHiddenSize];
            for (int h = 0; h < NeuralHiddenSize; h++)
            {
                double sum = biasH[h];
                for (int i = 0; i < NeuralInputSize; i++)
                    sum += inputIndicators[i] * weightsIH[i * NeuralHiddenSize + h];
                hidden[h] = Math.Tanh(sum);
            }
            
            var output = new double[NeuralOutputSize];
            for (int o = 0; o < NeuralOutputSize; o++)
            {
                double sum = biasO[o];
                for (int h = 0; h < NeuralHiddenSize; h++)
                    sum += hidden[h] * weightsHO[h * NeuralOutputSize + o];
                output[o] = sum;
            }
            
            // Apply output as adjustments to base weights
            var testWeights = ApplyNetworkOutput(baseWeights, output);
            
            // Evaluate current weights
            var trainEval = EvaluateWeightsWithSnapshots(testWeights, trainData, trainSnapshots);
            var valEval = EvaluateWeightsWithSnapshots(testWeights, valData, valSnapshots);
            
            // SPSA-style gradient estimation (perturb all weights simultaneously)
            // This is much more efficient than per-weight finite differences
            double epsilon = 0.01;
            var perturbation = new double[weightsHO.Length];
            for (int i = 0; i < perturbation.Length; i++)
                perturbation[i] = _rng.NextDouble() > 0.5 ? 1.0 : -1.0;  // Bernoulli ±1
            
            // Positive perturbation
            var plusWeightsHO = new double[weightsHO.Length];
            for (int i = 0; i < weightsHO.Length; i++)
                plusWeightsHO[i] = weightsHO[i] + epsilon * perturbation[i];
            
            var plusOutput = ForwardPassHO(hidden, plusWeightsHO, biasO);
            var plusTestWeights = ApplyNetworkOutput(baseWeights, plusOutput);
            var plusEval = EvaluateWeightsWithSnapshots(plusTestWeights, trainData, trainSnapshots);
            
            // Negative perturbation
            var minusWeightsHO = new double[weightsHO.Length];
            for (int i = 0; i < weightsHO.Length; i++)
                minusWeightsHO[i] = weightsHO[i] - epsilon * perturbation[i];
            
            var minusOutput = ForwardPassHO(hidden, minusWeightsHO, biasO);
            var minusTestWeights = ApplyNetworkOutput(baseWeights, minusOutput);
            var minusEval = EvaluateWeightsWithSnapshots(minusTestWeights, trainData, trainSnapshots);
            
            // SPSA gradient: g_i = (f(θ+) - f(θ-)) / (2 * epsilon * Δ_i)
            double fitnessDiff = plusEval.fitness - minusEval.fitness;
            for (int i = 0; i < weightsHO.Length; i++)
            {
                double gradient = fitnessDiff / (2 * epsilon * perturbation[i]);
                weightsHO[i] += learningRate * gradient;
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
            if (noImprovementCount >= EarlyStoppingPatience)
            {
                progress?.Report($"  Early stopping at epoch {epoch}");
                break;
            }
            
            // Decay learning rate
            learningRate *= NeuralLearningRateDecay;
        }
        
        result.Duration = sw.Elapsed;
        result.GenerationsRun = epochs;
        
        if (best != null)
        {
            result.BestWeights = best;
            var trainEval = EvaluateWeightsWithSnapshots(best, trainData, trainSnapshots);
            var valEval = EvaluateWeightsWithSnapshots(best, valData, valSnapshots);
            
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
    
    /// <summary>
    /// Forward pass for hidden-to-output layer only (for gradient estimation).
    /// </summary>
    private double[] ForwardPassHO(double[] hidden, double[] weightsHO, double[] biasO)
    {
        var output = new double[NeuralOutputSize];
        for (int o = 0; o < NeuralOutputSize; o++)
        {
            double sum = biasO[o];
            for (int h = 0; h < NeuralHiddenSize; h++)
                sum += hidden[h] * weightsHO[h * NeuralOutputSize + o];
            output[o] = sum;
        }
        return output;
    }
    
    /// <summary>
    /// Applies neural network output to base weights.
    /// </summary>
    private static LearnedWeights ApplyNetworkOutput(LearnedWeights baseWeights, double[] output)
    {
        var testWeights = baseWeights.Clone();
        for (int i = 0; i < Math.Min(output.Length, testWeights.IndicatorWeights.Length); i++)
        {
            testWeights.IndicatorWeights[i] += output[i] * 0.1;
        }
        return testWeights;
    }
    
    /// <summary>
    /// Normalizes indicators from a snapshot for neural network input.
    /// </summary>
    private static double[] GetNormalizedIndicators(ExtendedSnapshot snapshot)
    {
        var result = new double[16];
        
        result[0] = snapshot.Vwap > 0 ? (snapshot.Price - snapshot.Vwap) / snapshot.Vwap * 10 : 0;
        result[1] = snapshot.Ema9 > 0 ? (snapshot.Price - snapshot.Ema9) / snapshot.Ema9 * 10 : 0;
        result[2] = snapshot.Ema21 > 0 ? (snapshot.Price - snapshot.Ema21) / snapshot.Ema21 * 10 : 0;
        result[3] = snapshot.Ema50 > 0 ? (snapshot.Price - snapshot.Ema50) / snapshot.Ema50 * 10 : 0;
        result[4] = (snapshot.Rsi - 50) / 50;
        result[5] = snapshot.MacdHistogram * 100;
        result[6] = (snapshot.Adx - 25) / 25;
        result[7] = snapshot.VolumeRatio - 1.0;
        result[8] = snapshot.PlusDi > snapshot.MinusDi ? 1 : -1;
        result[9] = snapshot.Macd > snapshot.MacdSignal ? 1 : -1;
        result[10] = snapshot.Price > 0 ? snapshot.Momentum / snapshot.Price * 100 : 0;
        result[11] = snapshot.Roc / 10;
        result[12] = snapshot.IsHigherLow ? 1 : 0;
        result[13] = snapshot.IsLowerHigh ? 1 : 0;
        result[14] = snapshot.IsVwapReclaim ? 1 : 0;
        result[15] = snapshot.IsVwapRejection ? 1 : 0;
        
        return result;
    }
    
    private double[] GetAverageIndicators(List<BackTestCandle> candles)
    {
        if (candles.Count < IndicatorWarmupBars) return new double[16];
        
        // Calculate REAL indicator values from the most recent candles
        int lastIdx = candles.Count - 1;
        var snapshot = BuildSnapshot(candles, lastIdx);
        return GetNormalizedIndicators(snapshot);
    }
    
    // ========================================================================
    // METHOD 3: GRADIENT DESCENT
    // ========================================================================
    
    private MethodResult RunGradient(
        string symbol,
        List<BackTestCandle> trainData,
        List<BackTestCandle> valData,
        List<ExtendedSnapshot> trainSnapshots,
        List<ExtendedSnapshot> valSnapshots,
        int iterations,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var result = new MethodResult { MethodName = "GRADIENT" };
        var sw = Stopwatch.StartNew();
        
        // Start with random weights
        var currentWeights = LearnedWeights.CreateRandom(symbol, _rng);
        LearnedWeights? best = null;
        double bestValFitness = double.MinValue;
        double learningRate = GradientInitialLearningRate;
        int noImprovementCount = 0;
        
        for (int iter = 1; iter <= iterations; iter++)
        {
            ct.ThrowIfCancellationRequested();
            
            var currentEval = EvaluateWeightsWithSnapshots(currentWeights, trainData, trainSnapshots);
            var valEval = EvaluateWeightsWithSnapshots(currentWeights, valData, valSnapshots);
            
            // Numerical gradient estimation for key weights
            // Only optimize the most important weights (indicator weights)
            for (int i = 0; i < currentWeights.IndicatorWeights.Length; i++)
            {
                double original = currentWeights.IndicatorWeights[i];
                
                // Positive perturbation
                currentWeights.IndicatorWeights[i] = original + GradientEpsilon;
                var plusEval = EvaluateWeightsWithSnapshots(currentWeights, trainData, trainSnapshots);
                
                // Negative perturbation
                currentWeights.IndicatorWeights[i] = original - GradientEpsilon;
                var minusEval = EvaluateWeightsWithSnapshots(currentWeights, trainData, trainSnapshots);
                
                // Gradient (central difference)
                double gradient = (plusEval.fitness - minusEval.fitness) / (2 * GradientEpsilon);
                
                // Update
                currentWeights.IndicatorWeights[i] = original + learningRate * gradient;
                
                // Clamp to reasonable range
                currentWeights.IndicatorWeights[i] = Math.Clamp(currentWeights.IndicatorWeights[i], 0.01, 2.0);
            }
            
            // Also optimize entry biases
            for (int i = 0; i < currentWeights.EntryBiases.Length; i++)
            {
                double original = currentWeights.EntryBiases[i];
                double biasEpsilon = GradientEpsilon * 10;
                
                currentWeights.EntryBiases[i] = original + biasEpsilon;
                var plusEval = EvaluateWeightsWithSnapshots(currentWeights, trainData, trainSnapshots);
                
                currentWeights.EntryBiases[i] = original - biasEpsilon;
                var minusEval = EvaluateWeightsWithSnapshots(currentWeights, trainData, trainSnapshots);
                
                double gradient = (plusEval.fitness - minusEval.fitness) / (2 * biasEpsilon);
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
            if (noImprovementCount >= EarlyStoppingPatience)
            {
                progress?.Report($"  Early stopping at iter {iter}");
                break;
            }
            
            // Decay learning rate
            learningRate *= GradientLearningRateDecay;
        }
        
        result.Duration = sw.Elapsed;
        result.GenerationsRun = iterations;
        
        if (best != null)
        {
            result.BestWeights = best;
            var trainEval = EvaluateWeightsWithSnapshots(best, trainData, trainSnapshots);
            var valEval = EvaluateWeightsWithSnapshots(best, valData, valSnapshots);
            
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
    // METHOD 4: LSH PATTERN MATCHING (Locality-Sensitive Hashing)
    // ========================================================================
    //
    // Uses Hamming distance in N-cube space to find historical patterns
    // that are similar to current market conditions. Based on the insight
    // that in high-dimensional spaces, random vectors cluster around N/2
    // distance, so genuinely similar patterns stand out clearly.
    //
    // Unlike the other methods that optimize weights, LSH:
    // 1. Builds a pattern database from training data
    // 2. For each validation point, finds analog patterns
    // 3. Uses analog outcomes to predict direction
    // 4. Measures accuracy of predictions
    //
    // ========================================================================
    
    private MethodResult RunLSH(
        string symbol,
        List<BackTestCandle> trainData,
        List<BackTestCandle> valData,
        List<ExtendedSnapshot> trainSnapshots,
        List<ExtendedSnapshot> valSnapshots,
        int _iterations,  // Not used for LSH, but kept for API consistency
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var result = new MethodResult { MethodName = "LSH" };
        var sw = Stopwatch.StartNew();
        
        // Create pattern matcher for this symbol
        var patternMatcher = new PatternMatcher(symbol, _dataFolder);
        
        progress?.Report($"  Building pattern database from {trainData.Count} training bars...");
        
        // Step 1: Build pattern database from training data using pre-computed snapshots
        int patternsAdded = 0;
        for (int i = IndicatorWarmupBars; i < trainData.Count - ForecastHorizonBars; i++)
        {
            ct.ThrowIfCancellationRequested();
            
            var snapshot = trainSnapshots[i];
            var indicatorSnapshot = ToIndicatorSnapshot(snapshot);
            
            // Calculate next-period outcome (5 bars forward = 5 minutes on 1-min bars)
            double currentPrice = trainData[i].Close;
            double futurePrice = trainData[i + ForecastHorizonBars].Close;
            double nextReturn = (futurePrice - currentPrice) / currentPrice;
            
            // Find max gain and max drawdown in next bars
            double maxHigh = currentPrice;
            double maxLow = currentPrice;
            for (int j = i + 1; j <= i + ForecastHorizonBars && j < trainData.Count; j++)
            {
                maxHigh = Math.Max(maxHigh, trainData[j].High);
                maxLow = Math.Min(maxLow, trainData[j].Low);
            }
            double maxGain = (maxHigh - currentPrice) / currentPrice;
            double maxDrawdown = (currentPrice - maxLow) / currentPrice;
            
            // Record pattern
            patternMatcher.RecordPattern(
                indicatorSnapshot,
                trainData[i].Timestamp,
                currentPrice,
                0,  // Market score not needed for pure LSH
                nextReturn,
                maxGain,
                maxDrawdown);
            
            patternsAdded++;
            
            if (patternsAdded % 500 == 0)
                progress?.Report($"  Built {patternsAdded} patterns...");
        }
        
        progress?.Report($"  Pattern database: {patternMatcher.PatternCount} patterns");
        progress?.Report($"  Testing on {valData.Count} validation bars...");
        
        // Step 2: Test on validation data using pattern predictions
        int predictions = 0;
        int correctPredictions = 0;
        double totalPnL = 0;
        int wins = 0;
        int losses = 0;
        var returns = new List<double>();
        
        double slippagePercent = TradingDefaults.SlippagePercent;
        
        for (int i = IndicatorWarmupBars; i < valData.Count - ForecastHorizonBars; i += ForecastHorizonBars)
        {
            ct.ThrowIfCancellationRequested();
            
            var snapshot = valSnapshots[i];
            var indicatorSnapshot = ToIndicatorSnapshot(snapshot);
            
            // Get forecast from pattern matcher
            var forecast = patternMatcher.GetForecast(indicatorSnapshot, maxAnalogs: LshMaxAnalogs, maxDistance: LshMaxDistance);
            
            if (!forecast.IsUsable || forecast.Confidence < LshMinConfidence)
                continue;  // Skip if not enough good analogs
            
            predictions++;
            
            // Calculate actual outcome
            double currentPrice = valData[i].Close;
            double futurePrice = valData[i + ForecastHorizonBars].Close;
            double actualReturn = (futurePrice - currentPrice) / currentPrice;
            bool wentHigher = actualReturn > 0;
            
            // Check prediction accuracy
            bool predictedHigher = forecast.SuggestedDirection == 1;
            bool predictedLower = forecast.SuggestedDirection == -1;
            
            // Only count if we made a directional prediction
            if (predictedHigher || predictedLower)
            {
                bool correct = (predictedHigher && wentHigher) || (predictedLower && !wentHigher);
                if (correct)
                    correctPredictions++;
                
                // Simulate trade based on prediction
                double entryPrice, exitPrice, pnl;
                if (predictedHigher)
                {
                    entryPrice = currentPrice * (1 + slippagePercent);
                    exitPrice = futurePrice * (1 - slippagePercent);
                    pnl = exitPrice - entryPrice;
                }
                else
                {
                    entryPrice = currentPrice * (1 - slippagePercent);
                    exitPrice = futurePrice * (1 + slippagePercent);
                    pnl = entryPrice - exitPrice;
                }
                
                totalPnL += pnl;
                returns.Add(pnl / entryPrice * 100);
                
                if (pnl > 0) wins++;
                else losses++;
            }
        }
        
        result.Duration = sw.Elapsed;
        result.GenerationsRun = patternMatcher.PatternCount;
        
        // Calculate metrics
        double winRate = predictions > 0 ? (double)correctPredictions / predictions * 100 : 0;
        double sharpe = returns.Count > 1 
            ? returns.Average() / (StandardDeviation(returns) + 0.0001) * Math.Sqrt(252) 
            : 0;
        
        // Fitness: weighted combination of accuracy, PnL, and Sharpe
        double fitness = winRate * 0.4 + Math.Max(0, totalPnL * 10) * 0.3 + Math.Max(0, sharpe * 10) * 0.3;
        
        progress?.Report($"  Predictions: {predictions}, Correct: {correctPredictions}, Accuracy: {winRate:F1}%");
        
        // Create weights result (LSH doesn't optimize weights, but we need to return something)
        var weights = LearnedWeights.CreateRandom(symbol, _rng);
        weights.LearningMethod = "lsh";
        weights.TrainingFitness = fitness;
        weights.ValidationFitness = fitness;
        
        result.BestWeights = weights;
        result.TrainingFitness = fitness;
        result.TrainingWinRate = winRate;
        result.TrainingPnL = totalPnL;
        result.ValidationFitness = fitness;
        result.ValidationWinRate = winRate;
        result.ValidationPnL = totalPnL;
        result.ValidationSharpe = sharpe;
        
        // Save the pattern database for future use
        patternMatcher.Save();
        progress?.Report($"  Saved pattern database to disk");
        
        return result;
    }
    
    // ========================================================================
    // METHOD 5: LSTM (Long Short-Term Memory Neural Network)
    // ========================================================================
    //
    // Uses LSTM neural network to learn temporal patterns in price data.
    // LSTM excels at capturing sequential dependencies that other methods miss.
    // Unlike the weight optimization methods, LSTM:
    // 1. Learns its own internal weights during training
    // 2. Uses trained weights to make directional predictions
    // 3. Persists the model for future use
    //
    // ========================================================================
    
    private MethodResult RunLSTM(
        string symbol,
        List<BackTestCandle> trainData,
        List<BackTestCandle> valData,
        List<ExtendedSnapshot> trainSnapshots,
        List<ExtendedSnapshot> valSnapshots,
        int epochs,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var result = new MethodResult { MethodName = "LSTM" };
        var sw = Stopwatch.StartNew();
        
        // Create LSTM predictor for this symbol
        var lstmPredictor = new LstmPredictor(symbol);
        
        progress?.Report($"  Training LSTM on {trainData.Count} bars for {epochs} epochs...");
        
        // Step 1: Build training data using pre-computed snapshots
        int samplesAdded = 0;
        for (int i = IndicatorWarmupBars; i < trainData.Count - ForecastHorizonBars; i++)
        {
            ct.ThrowIfCancellationRequested();
            
            var snapshot = trainSnapshots[i];
            lstmPredictor.AddDataPoint(snapshot);
            
            samplesAdded++;
            
            if (samplesAdded % 500 == 0)
                progress?.Report($"  Added {samplesAdded} training samples...");
        }
        
        progress?.Report($"  LSTM training data: {samplesAdded} samples");
        
        // Step 2: Train the LSTM for specified epochs
        progress?.Report($"  Training for {epochs} epochs...");
        lstmPredictor.Train(epochs: epochs, learningRate: LstmLearningRate);
        
        var (trainingSamples, isTrained, meanReturn, stdReturn) = lstmPredictor.GetStats();
        progress?.Report($"  Training complete: {trainingSamples} samples, mean return: {meanReturn:F4}%");
        
        // Step 3: Evaluate on validation data using the TRAINED model
        // NOTE: Do NOT reset - we need to keep the trained weights!
        progress?.Report($"  Validating on {valData.Count} bars...");
        
        int predictions = 0;
        int correctPredictions = 0;
        double totalPnL = 0;
        int wins = 0;
        int losses = 0;
        var returns = new List<double>();
        
        double slippagePercent = TradingDefaults.SlippagePercent;
        
        for (int i = IndicatorWarmupBars; i < valData.Count - ForecastHorizonBars; i += ForecastHorizonBars)
        {
            ct.ThrowIfCancellationRequested();
            
            var snapshot = valSnapshots[i];
            lstmPredictor.AddDataPoint(snapshot);
            
            var prediction = lstmPredictor.Predict();
            
            // Skip if not usable (not enough sequence or low confidence)
            if (!prediction.IsUsable || prediction.Confidence < LstmMinConfidence)
                continue;
            
            predictions++;
            
            // Calculate actual outcome
            double currentPrice = valData[i].Close;
            double futurePrice = valData[i + ForecastHorizonBars].Close;
            double actualReturn = (futurePrice - currentPrice) / currentPrice;
            bool wentHigher = actualReturn > 0;
            
            // Check prediction accuracy
            bool predictedHigher = prediction.Direction > LstmDirectionThreshold;
            bool predictedLower = prediction.Direction < -LstmDirectionThreshold;
            
            if (predictedHigher || predictedLower)
            {
                bool correct = (predictedHigher && wentHigher) || (predictedLower && !wentHigher);
                if (correct)
                    correctPredictions++;
                
                // Simulate trade based on prediction
                double entryPrice, exitPrice, pnl;
                if (predictedHigher)
                {
                    entryPrice = currentPrice * (1 + slippagePercent);
                    exitPrice = futurePrice * (1 - slippagePercent);
                    pnl = exitPrice - entryPrice;
                }
                else
                {
                    entryPrice = currentPrice * (1 - slippagePercent);
                    exitPrice = futurePrice * (1 + slippagePercent);
                    pnl = entryPrice - exitPrice;
                }
                
                totalPnL += pnl;
                returns.Add(pnl / entryPrice * 100);
                
                if (pnl > 0) wins++;
                else losses++;
            }
        }
        
        result.Duration = sw.Elapsed;
        result.GenerationsRun = samplesAdded;
        
        // Calculate metrics
        double winRate = predictions > 0 ? (double)correctPredictions / predictions * 100 : 0;
        double sharpe = returns.Count > 1 
            ? returns.Average() / (StandardDeviation(returns) + 0.0001) * Math.Sqrt(252) 
            : 0;
        
        // Fitness: weighted combination of accuracy, PnL, and Sharpe
        double fitness = winRate * 0.4 + Math.Max(0, totalPnL * 10) * 0.3 + Math.Max(0, sharpe * 10) * 0.3;
        
        progress?.Report($"  Predictions: {predictions}, Correct: {correctPredictions}, Accuracy: {winRate:F1}%");
        
        // Create weights result
        var weights = LearnedWeights.CreateRandom(symbol, _rng);
        weights.LearningMethod = "lstm";
        weights.TrainingFitness = fitness;
        weights.ValidationFitness = fitness;
        
        result.BestWeights = weights;
        result.TrainingFitness = fitness;
        result.TrainingWinRate = winRate;
        result.TrainingPnL = totalPnL;
        result.ValidationFitness = fitness;
        result.ValidationWinRate = winRate;
        result.ValidationPnL = totalPnL;
        result.ValidationSharpe = sharpe;
        
        // Note: Model is saved automatically by Train() when sufficient data
        progress?.Report($"  LSTM model persisted");
        
        return result;
    }
    
    /// <summary>
    /// Converts ExtendedSnapshot to IndicatorSnapshot for LSH processing.
    /// </summary>
    private static IdiotProof.Calculators.IndicatorSnapshot ToIndicatorSnapshot(ExtendedSnapshot ext)
    {
        return new IdiotProof.Calculators.IndicatorSnapshot
        {
            Price = ext.Price,
            Vwap = ext.Vwap,
            Ema9 = ext.Ema9,
            Ema21 = ext.Ema21,
            Ema50 = ext.Ema50,
            Rsi = ext.Rsi,
            Macd = ext.Macd,
            MacdSignal = ext.MacdSignal,
            MacdHistogram = ext.MacdHistogram,
            Adx = ext.Adx,
            PlusDi = ext.PlusDi,
            MinusDi = ext.MinusDi,
            VolumeRatio = ext.VolumeRatio,
            BollingerUpper = ext.BollingerUpper,
            BollingerLower = ext.BollingerLower,
            BollingerMiddle = ext.BollingerMiddle,
            Atr = ext.Atr
        };
    }
    
    /// <summary>
    /// Calculates standard deviation for Sharpe ratio calculation.
    /// </summary>
    private static double StandardDeviation(List<double> values)
    {
        if (values.Count < 2) return 0;
        double avg = values.Average();
        double sumSquares = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }
    
    // ========================================================================
    // EVALUATION
    // ========================================================================
    
    /// <summary>
    /// Pre-computes all snapshots for a candle list. O(n) operation done once.
    /// Much faster than recalculating in EvaluateWeights which is called thousands of times.
    /// </summary>
    private List<ExtendedSnapshot> PrecomputeSnapshots(List<BackTestCandle> candles)
    {
        var snapshots = new List<ExtendedSnapshot>(candles.Count);
        
        // For the first 50 candles, we create placeholder snapshots (indicators need warmup)
        for (int i = 0; i < Math.Min(50, candles.Count); i++)
        {
            snapshots.Add(new ExtendedSnapshot { Price = candles[i].Close, Vwap = candles[i].Vwap });
        }
        
        // For remaining candles, calculate full snapshots
        for (int i = 50; i < candles.Count; i++)
        {
            snapshots.Add(BuildSnapshot(candles, i));
        }
        
        return snapshots;
    }
    
    private (double fitness, double winRate, double pnl, double sharpe, int trades) 
        EvaluateWeights(LearnedWeights weights, List<BackTestCandle> candles)
    {
        return EvaluateWeightsWithLSH(weights, candles, null);
    }
    
    /// <summary>
    /// Evaluate weights using pre-computed snapshots for O(1) indicator access.
    /// This is the performance-optimized version used by the learning methods.
    /// </summary>
    private (double fitness, double winRate, double pnl, double sharpe, int trades) 
        EvaluateWeightsWithSnapshots(
            LearnedWeights weights, 
            List<BackTestCandle> candles, 
            List<ExtendedSnapshot> snapshots)
    {
        if (candles.Count < 10 || snapshots.Count < 10)
            return (-1000, 0, 0, 0, 0);
        
        // Simulate trading using these weights - MATCHING LIVE TRADING BEHAVIOR
        double slippagePercent = TradingDefaults.SlippagePercent;
        
        int trades = 0;
        int wins = 0;
        double totalPnL = 0;
        var returns = new List<double>();
        
        bool inPosition = false;
        bool isLong = false;
        double entryPrice = 0;
        double highWaterMark = 0;
        double trailingStopPrice = 0;
        
        for (int i = IndicatorWarmupBars; i < candles.Count && i < snapshots.Count; i++)
        {
            var candle = candles[i];
            var snapshot = snapshots[i];
            
            // Calculate score using weights
            var (score, shouldEnterLong, shouldEnterShort, shouldExit) = 
                WeightedScoreCalculator.Calculate(snapshot, weights);
            
            if (!inPosition)
            {
                // Entry logic - apply slippage (worse entry price)
                if (shouldEnterLong)
                {
                    inPosition = true;
                    isLong = true;
                    entryPrice = candle.Close * (1 + slippagePercent);
                    highWaterMark = entryPrice;
                    trailingStopPrice = entryPrice * (1 - TrailingStopPercent);
                }
                else if (shouldEnterShort)
                {
                    inPosition = true;
                    isLong = false;
                    entryPrice = candle.Close * (1 - slippagePercent);
                    highWaterMark = entryPrice;
                    trailingStopPrice = entryPrice * (1 + TrailingStopPercent);
                }
            }
            else
            {
                // Update trailing stop based on high water mark
                if (isLong)
                {
                    if (candle.High > highWaterMark)
                    {
                        highWaterMark = candle.High;
                        double newTrailingStop = highWaterMark * (1 - TrailingStopPercent);
                        if (newTrailingStop > trailingStopPrice)
                            trailingStopPrice = newTrailingStop;
                    }
                }
                else
                {
                    if (candle.Low < highWaterMark)
                    {
                        highWaterMark = candle.Low;
                        double newTrailingStop = highWaterMark * (1 + TrailingStopPercent);
                        if (newTrailingStop < trailingStopPrice)
                            trailingStopPrice = newTrailingStop;
                    }
                }
                
                // Exit logic - USE SAME ATR-BASED TP/SL AS LIVE TRADING
                double atr = snapshot.Atr > 0 ? snapshot.Atr : CalculateAtr(candles, i, 14);
                double tpMultiplier = TradingDefaults.TpAtrMultiplier;
                double slMultiplier = TradingDefaults.SlAtrMultiplier;
                
                double tpDistance = atr * tpMultiplier;
                double slDistance = atr * slMultiplier;
                
                double tpTarget = isLong ? entryPrice + tpDistance : entryPrice - tpDistance;
                double slTarget = isLong ? entryPrice - slDistance : entryPrice + slDistance;
                
                // Check trailing stop hit
                bool hitTrailingStop = isLong 
                    ? candle.Low <= trailingStopPrice 
                    : candle.High >= trailingStopPrice;
                
                bool hitTp = isLong ? candle.High >= tpTarget : candle.Low <= tpTarget;
                bool hitSl = isLong ? candle.Low <= slTarget : candle.High >= slTarget;
                
                // Check for direction flip opportunity
                bool shouldFlip = (isLong && shouldEnterShort) || (!isLong && shouldEnterLong);
                
                // Calculate actual PnL based on exit price
                double exitPrice = 0;
                bool shouldExitNow = false;
                
                if (hitTp && hitSl)
                {
                    exitPrice = slTarget; // Conservative: assume SL hit first
                    shouldExitNow = true;
                }
                else if (hitTp)
                {
                    exitPrice = tpTarget;
                    shouldExitNow = true;
                }
                else if (hitSl || hitTrailingStop)
                {
                    if (isLong)
                        exitPrice = Math.Max(slTarget, trailingStopPrice);
                    else
                        exitPrice = Math.Min(slTarget, trailingStopPrice);
                    shouldExitNow = true;
                }
                else if (shouldExit || shouldFlip)
                {
                    exitPrice = candle.Close;
                    shouldExitNow = true;
                }
                
                if (!shouldExitNow)
                    continue;
                
                // Apply exit slippage
                if (isLong)
                    exitPrice *= (1 - slippagePercent);
                else
                    exitPrice *= (1 + slippagePercent);
                
                double pnl = isLong ? 
                    (exitPrice - entryPrice) / entryPrice * 100 :
                    (entryPrice - exitPrice) / entryPrice * 100;
                
                trades++;
                totalPnL += pnl;
                returns.Add(pnl);
                if (pnl > 0) wins++;
                
                inPosition = false;
                
                // Direction flip: immediately enter opposite position
                if (shouldFlip)
                {
                    inPosition = true;
                    isLong = shouldEnterLong;
                    entryPrice = candle.Close * (isLong ? (1 + slippagePercent) : (1 - slippagePercent));
                    highWaterMark = entryPrice;
                    trailingStopPrice = isLong 
                        ? entryPrice * (1 - TrailingStopPercent) 
                        : entryPrice * (1 + TrailingStopPercent);
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
    
    private (double fitness, double winRate, double pnl, double sharpe, int trades) 
        EvaluateWeightsWithLSH(LearnedWeights weights, List<BackTestCandle> candles, PatternMatcher? patternMatcher)
    {
        if (candles.Count < 10)
            return (-1000, 0, 0, 0, 0);
        
        // Simulate trading using these weights - MATCHING LIVE TRADING BEHAVIOR
        // Constants matching live AutonomousTradingConfig
        double slippagePercent = TradingDefaults.SlippagePercent;  // 0.05% slippage per trade (entry + exit)
        const double TrailingStopPercent = 0.10; // 10% trailing stop (matches live default)
        const bool AllowDirectionFlip = true;   // Allow immediate direction flip
        
        // LSH configuration (matching live StrategyRunner logic)
        bool useLSH = patternMatcher != null && patternMatcher.PatternCount >= 100;
        
        int trades = 0;
        int wins = 0;
        double totalPnL = 0;
        var returns = new List<double>();
        
        bool inPosition = false;
        bool isLong = false;
        double entryPrice = 0;
        double highWaterMark = 0;  // For trailing stop (highest price for long, lowest for short)
        double trailingStopPrice = 0;
        
        for (int i = 50; i < candles.Count; i++) // Skip first 50 for indicator warmup
        {
            var candle = candles[i];
            
            // Build snapshot
            var snapshot = BuildSnapshot(candles, i);
            
            // Calculate score using weights
            var (score, shouldEnterLong, shouldEnterShort, shouldExit) = 
                WeightedScoreCalculator.Calculate(snapshot, weights);
            
            // Apply LSH "second opinion" if available
            if (useLSH && !inPosition)
            {
                var indicatorSnap = ToIndicatorSnapshot(snapshot);
                var lshForecast = patternMatcher!.GetForecast(indicatorSnap, maxAnalogs: 15, maxDistance: 85);
                
                if (lshForecast.IsUsable)
                {
                    bool lshConfirmsLong = lshForecast.SuggestedDirection == 1 && lshForecast.Confidence >= 0.6;
                    bool lshConfirmsShort = lshForecast.SuggestedDirection == -1 && lshForecast.Confidence >= 0.6;
                    bool lshVetoesLong = lshForecast.SuggestedDirection == -1 && lshForecast.Confidence >= 0.7;
                    bool lshVetoesShort = lshForecast.SuggestedDirection == 1 && lshForecast.Confidence >= 0.7;
                    
                    // Apply LSH influence (same logic as live StrategyRunner)
                    if (shouldEnterLong && lshVetoesLong)
                    {
                        shouldEnterLong = false;  // LSH veto
                    }
                    else if (shouldEnterShort && lshVetoesShort)
                    {
                        shouldEnterShort = false;  // LSH veto
                    }
                    else if (!shouldEnterLong && !shouldEnterShort && lshForecast.Confidence >= 0.65)
                    {
                        // LSH boost (only when score is close to threshold)
                        // Use simplified threshold check since we don't have exact thresholds here
                        if (lshConfirmsLong && score >= 60)
                        {
                            shouldEnterLong = true;  // LSH boost
                        }
                        else if (lshConfirmsShort && score <= -60)
                        {
                            shouldEnterShort = true;  // LSH boost
                        }
                    }
                }
            }
            
            if (!inPosition)
            {
                // Entry logic - apply slippage (worse entry price)
                if (shouldEnterLong)
                {
                    inPosition = true;
                    isLong = true;
                    entryPrice = candle.Close * (1 + slippagePercent); // Buy at slightly higher price
                    highWaterMark = entryPrice;
                    trailingStopPrice = entryPrice * (1 - TrailingStopPercent);
                }
                else if (shouldEnterShort)
                {
                    inPosition = true;
                    isLong = false;
                    entryPrice = candle.Close * (1 - slippagePercent); // Sell at slightly lower price
                    highWaterMark = entryPrice; // For shorts this tracks low water mark
                    trailingStopPrice = entryPrice * (1 + TrailingStopPercent);
                }
            }
            else
            {
                // Update trailing stop based on high water mark
                if (isLong)
                {
                    if (candle.High > highWaterMark)
                    {
                        highWaterMark = candle.High;
                        double newTrailingStop = highWaterMark * (1 - TrailingStopPercent);
                        if (newTrailingStop > trailingStopPrice)
                            trailingStopPrice = newTrailingStop;
                    }
                }
                else
                {
                    if (candle.Low < highWaterMark)
                    {
                        highWaterMark = candle.Low;
                        double newTrailingStop = highWaterMark * (1 + TrailingStopPercent);
                        if (newTrailingStop < trailingStopPrice)
                            trailingStopPrice = newTrailingStop;
                    }
                }
                
                // Exit logic - USE SAME ATR-BASED TP/SL AS LIVE TRADING
                double atr = CalculateAtr(candles, i, 14);
                double tpMultiplier = TradingDefaults.TpAtrMultiplier;  // Same as live AutonomousTradingConfig default
                double slMultiplier = TradingDefaults.SlAtrMultiplier;  // Same as live AutonomousTradingConfig default
                
                double tpDistance = atr * tpMultiplier;
                double slDistance = atr * slMultiplier;
                
                double tpTarget = isLong ? entryPrice + tpDistance : entryPrice - tpDistance;
                double slTarget = isLong ? entryPrice - slDistance : entryPrice + slDistance;
                
                // Check trailing stop hit
                bool hitTrailingStop = isLong 
                    ? candle.Low <= trailingStopPrice 
                    : candle.High >= trailingStopPrice;
                
                bool hitTp = isLong ? candle.High >= tpTarget : candle.Low <= tpTarget;
                bool hitSl = isLong ? candle.Low <= slTarget : candle.High >= slTarget;
                
                // Check for direction flip opportunity (matches live AllowDirectionFlip behavior)
                bool shouldFlip = AllowDirectionFlip && 
                    ((isLong && shouldEnterShort) || (!isLong && shouldEnterLong));
                
                // Calculate actual PnL based on exit price
                double exitPrice = 0;
                bool shouldExitNow = false;
                
                if (hitTp && hitSl)
                {
                    // Both hit in same candle - assume SL hit first (conservative)
                    exitPrice = slTarget;
                    shouldExitNow = true;
                }
                else if (hitTp)
                {
                    exitPrice = tpTarget;
                    shouldExitNow = true;
                }
                else if (hitSl || hitTrailingStop)
                {
                    // Use the tighter stop (higher for long, lower for short)
                    if (isLong)
                        exitPrice = Math.Max(slTarget, trailingStopPrice);
                    else
                        exitPrice = Math.Min(slTarget, trailingStopPrice);
                    shouldExitNow = true;
                }
                else if (shouldExit || shouldFlip)
                {
                    // Exit signal from learned weights or direction flip
                    exitPrice = candle.Close;
                    shouldExitNow = true;
                }
                
                if (!shouldExitNow)
                    continue; // No exit this candle
                
                // Apply exit slippage (worse exit price)
                if (isLong)
                    exitPrice *= (1 - slippagePercent);
                else
                    exitPrice *= (1 + slippagePercent);
                
                double pnl = isLong ? 
                    (exitPrice - entryPrice) / entryPrice * 100 :
                    (entryPrice - exitPrice) / entryPrice * 100;
                
                trades++;
                totalPnL += pnl;
                returns.Add(pnl);
                if (pnl > 0) wins++;
                
                inPosition = false;
                
                // Direction flip: immediately enter opposite position
                if (shouldFlip)
                {
                    inPosition = true;
                    isLong = shouldEnterLong; // Flip to the new direction
                    entryPrice = candle.Close * (isLong ? (1 + slippagePercent) : (1 - slippagePercent));
                    highWaterMark = entryPrice;
                    trailingStopPrice = isLong 
                        ? entryPrice * (1 - TrailingStopPercent) 
                        : entryPrice * (1 + TrailingStopPercent);
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
        
        // Calculate MACD (12, 26, 9)
        var (macd, signal, histogram) = CalculateMacd(candles, index);
        
        // Calculate ADX (14 period)
        var (adx, plusDi, minusDi) = CalculateAdx(candles, index, 14);
        
        // Calculate Volume Ratio (current vs 20-bar average)
        double volumeRatio = CalculateVolumeRatio(candles, index, 20);
        
        // Calculate ATR
        double atr = CalculateAtr(candles, index, 14);
        
        // Calculate Bollinger Bands (20, 2)
        var (bbMiddle, bbUpper, bbLower) = CalculateBollingerBands(candles, index, 20, 2.0);
        
        // Calculate Momentum and ROC
        double momentum = CalculateMomentum(candles, index, 10);
        double roc = CalculateRoc(candles, index, 10);
        
        // Pattern detection
        bool isHigherLow = DetectHigherLow(candles, index);
        bool isLowerHigh = DetectLowerHigh(candles, index);
        bool isNearLod = c.Close <= c.Low * 1.005;
        bool isNearHod = c.Close >= c.High * 0.995;
        bool isVwapReclaim = index > 0 && candles[index - 1].Close < c.Vwap && c.Close > c.Vwap;
        bool isVwapRejection = c.High > c.Vwap && c.Close < c.Vwap;
        
        return new ExtendedSnapshot
        {
            Price = c.Close,
            Vwap = c.Vwap,
            Ema9 = ema9,
            Ema21 = ema21,
            Ema50 = ema50,
            Rsi = rsi,
            Macd = macd,
            MacdSignal = signal,
            MacdHistogram = histogram,
            Adx = adx,
            PlusDi = plusDi,
            MinusDi = minusDi,
            VolumeRatio = volumeRatio,
            BollingerUpper = bbUpper,
            BollingerLower = bbLower,
            BollingerMiddle = bbMiddle,
            Atr = atr,
            Momentum = momentum,
            Roc = roc,
            TimeOfDay = TimeOnly.FromDateTime(c.Timestamp),
            IsHigherLow = isHigherLow,
            IsLowerHigh = isLowerHigh,
            IsNearLod = isNearLod,
            IsNearHod = isNearHod,
            IsVwapReclaim = isVwapReclaim,
            IsVwapRejection = isVwapRejection
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
    
    private (double macd, double signal, double histogram) CalculateMacd(List<BackTestCandle> candles, int endIndex)
    {
        if (endIndex < 35) return (0, 0, 0);  // Need 26 + 9 for proper MACD
        
        // Calculate current MACD line
        double ema12 = CalculateEma(candles, endIndex, 12);
        double ema26 = CalculateEma(candles, endIndex, 26);
        double macd = ema12 - ema26;
        
        // Signal line is 9-period EMA of MACD values
        // Calculate historical MACD values and compute EMA
        double multiplier = 2.0 / 10.0;  // 9-period EMA multiplier
        double signal = 0;
        
        // Compute MACD at startpoint (9 bars back)
        int startIdx = endIndex - 9;
        if (startIdx >= 26)
        {
            signal = CalculateEma(candles, startIdx, 12) - CalculateEma(candles, startIdx, 26);
        }
        
        // Apply EMA smoothing for signal line
        for (int i = startIdx + 1; i <= endIndex; i++)
        {
            double macdAtI = CalculateEma(candles, i, 12) - CalculateEma(candles, i, 26);
            signal = (macdAtI - signal) * multiplier + signal;
        }
        
        double histogram = macd - signal;
        
        return (macd, signal, histogram);
    }
    
    private (double adx, double plusDi, double minusDi) CalculateAdx(List<BackTestCandle> candles, int endIndex, int period)
    {
        if (endIndex < period * 2) return (25, 25, 20);
        
        double sumPlusDm = 0, sumMinusDm = 0, sumTr = 0;
        
        for (int i = endIndex - period + 1; i <= endIndex; i++)
        {
            double high = candles[i].High;
            double low = candles[i].Low;
            double prevHigh = candles[i - 1].High;
            double prevLow = candles[i - 1].Low;
            double prevClose = candles[i - 1].Close;
            
            double tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            double plusDm = high - prevHigh > prevLow - low && high - prevHigh > 0 ? high - prevHigh : 0;
            double minusDm = prevLow - low > high - prevHigh && prevLow - low > 0 ? prevLow - low : 0;
            
            sumTr += tr;
            sumPlusDm += plusDm;
            sumMinusDm += minusDm;
        }
        
        if (sumTr == 0) return (25, 25, 20);
        
        double plusDi = 100 * sumPlusDm / sumTr;
        double minusDi = 100 * sumMinusDm / sumTr;
        double dx = Math.Abs(plusDi - minusDi) / (plusDi + minusDi + 0.0001) * 100;
        double adx = dx; // Simplified - should be smoothed average of DX
        
        return (adx, plusDi, minusDi);
    }
    
    private double CalculateVolumeRatio(List<BackTestCandle> candles, int endIndex, int period)
    {
        if (endIndex < period) return 1.0;
        
        double avgVolume = 0;
        for (int i = endIndex - period; i < endIndex; i++)
        {
            avgVolume += candles[i].Volume;
        }
        avgVolume /= period;
        
        if (avgVolume == 0) return 1.0;
        return candles[endIndex].Volume / avgVolume;
    }
    
    private double CalculateAtr(List<BackTestCandle> candles, int endIndex, int period)
    {
        if (endIndex < period) return candles[endIndex].High - candles[endIndex].Low;
        
        double atr = 0;
        for (int i = endIndex - period + 1; i <= endIndex; i++)
        {
            double high = candles[i].High;
            double low = candles[i].Low;
            double prevClose = candles[i - 1].Close;
            double tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            atr += tr;
        }
        
        return atr / period;
    }
    
    private (double middle, double upper, double lower) CalculateBollingerBands(List<BackTestCandle> candles, int endIndex, int period, double stdDevMultiplier)
    {
        if (endIndex < period) return (candles[endIndex].Close, candles[endIndex].Close * 1.02, candles[endIndex].Close * 0.98);
        
        double sum = 0;
        for (int i = endIndex - period + 1; i <= endIndex; i++)
        {
            sum += candles[i].Close;
        }
        double middle = sum / period;
        
        double sumSq = 0;
        for (int i = endIndex - period + 1; i <= endIndex; i++)
        {
            sumSq += Math.Pow(candles[i].Close - middle, 2);
        }
        double stdDev = Math.Sqrt(sumSq / period);
        
        return (middle, middle + stdDev * stdDevMultiplier, middle - stdDev * stdDevMultiplier);
    }
    
    private double CalculateMomentum(List<BackTestCandle> candles, int endIndex, int period)
    {
        if (endIndex < period) return 0;
        return candles[endIndex].Close - candles[endIndex - period].Close;
    }
    
    private double CalculateRoc(List<BackTestCandle> candles, int endIndex, int period)
    {
        if (endIndex < period || candles[endIndex - period].Close == 0) return 0;
        return (candles[endIndex].Close - candles[endIndex - period].Close) / candles[endIndex - period].Close * 100;
    }
    
    private bool DetectHigherLow(List<BackTestCandle> candles, int endIndex)
    {
        if (endIndex < 3) return false;
        return candles[endIndex].Low > candles[endIndex - 2].Low && candles[endIndex - 1].Low > candles[endIndex - 3].Low;
    }
    
    private bool DetectLowerHigh(List<BackTestCandle> candles, int endIndex)
    {
        if (endIndex < 3) return false;
        return candles[endIndex].High < candles[endIndex - 2].High && candles[endIndex - 1].High < candles[endIndex - 3].High;
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
