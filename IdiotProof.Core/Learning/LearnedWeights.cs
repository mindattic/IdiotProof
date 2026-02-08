// ============================================================================
// LearnedWeights - The DNA of a Stock's Trading Personality
// ============================================================================
//
// Unlike the old system with 7 tunable parameters, this has 100+ learnable
// weights that interact with each other. The weights don't need human-readable
// names - they just need to provably increase profitability.
//
// KEY INSIGHT: We measure success on HELD-OUT validation data, not training data.
// If validation performance improves, we're actually learning. If training
// improves but validation doesn't, we're overfitting.
//
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Core.Learning;

/// <summary>
/// A learned weight vector that captures a stock's trading "DNA".
/// Contains 100+ interacting weights that are evolved or trained.
/// </summary>
public sealed class LearnedWeights
{
    // ========================================================================
    // WEIGHT DIMENSIONS (Total: ~150 weights)
    // ========================================================================
    
    /// <summary>
    /// Primary indicator weights (16 weights).
    /// Index: 0=VWAP, 1=EMA9, 2=EMA21, 3=EMA50, 4=RSI, 5=MACD, 6=ADX,
    /// 7=Volume, 8=Bollinger, 9=Momentum, 10=ROC, 11=ATR, 12-15=reserved
    /// </summary>
    public double[] IndicatorWeights { get; set; } = new double[16];
    
    /// <summary>
    /// Weights when market is trending (ADX > 25) (16 weights).
    /// Applied as multipliers to IndicatorWeights.
    /// </summary>
    public double[] TrendingMultipliers { get; set; } = new double[16];
    
    /// <summary>
    /// Weights when market is ranging (ADX < 20) (16 weights).
    /// Applied as multipliers to IndicatorWeights.
    /// </summary>
    public double[] RangingMultipliers { get; set; } = new double[16];
    
    /// <summary>
    /// Time-of-day weights (16 x 30-minute windows from 4:00 AM to 8:00 PM).
    /// Scales overall score based on time.
    /// </summary>
    public double[] TimeWeights { get; set; } = new double[16];
    
    /// <summary>
    /// Indicator interaction matrix (16x16 = 256 values, stored as flat array).
    /// InteractionMatrix[i*16+j] = how much indicator i affects indicator j's importance.
    /// This is the "secret sauce" - indicators don't work in isolation.
    /// </summary>
    public double[] InteractionMatrix { get; set; } = new double[256];
    
    /// <summary>
    /// Entry threshold biases (8 weights).
    /// Index: 0=base_long, 1=base_short, 2=trend_long, 3=trend_short,
    /// 4=range_long, 5=range_short, 6=volatile_long, 7=volatile_short
    /// </summary>
    public double[] EntryBiases { get; set; } = new double[8];
    
    /// <summary>
    /// Exit sensitivity weights (8 weights).
    /// Controls how aggressively to exit based on score drops.
    /// </summary>
    public double[] ExitSensitivity { get; set; } = new double[8];
    
    /// <summary>
    /// Price action pattern weights (16 weights).
    /// Learned responses to specific candlestick patterns.
    /// </summary>
    public double[] PatternWeights { get; set; } = new double[16];
    
    // ========================================================================
    // METADATA
    // ========================================================================
    
    public string Symbol { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Generation { get; set; }
    
    /// <summary>
    /// Which learning method created this: "genetic", "neural", "gradient"
    /// </summary>
    public string LearningMethod { get; set; } = "genetic";
    
    // ========================================================================
    // TRAINING METRICS (on training data - 20 days)
    // ========================================================================
    
    public double TrainingWinRate { get; set; }
    public double TrainingPnL { get; set; }
    public double TrainingFitness { get; set; }
    public int TrainingTrades { get; set; }
    
    // ========================================================================
    // VALIDATION METRICS (on held-out data - 10 days) - THE TRUE MEASURE
    // ========================================================================
    
    public double ValidationWinRate { get; set; }
    public double ValidationPnL { get; set; }
    public double ValidationFitness { get; set; }
    public int ValidationTrades { get; set; }
    public double ValidationSharpe { get; set; }
    
    /// <summary>
    /// Generations since validation fitness last improved.
    /// If this gets too high, we're overfitting.
    /// </summary>
    public int GenerationsWithoutImprovement { get; set; }
    
    // ========================================================================
    // METHODS
    // ========================================================================
    
    /// <summary>
    /// Total number of learnable weights.
    /// </summary>
    [JsonIgnore]
    public int TotalWeights => 
        IndicatorWeights.Length + 
        TrendingMultipliers.Length + 
        RangingMultipliers.Length + 
        TimeWeights.Length + 
        InteractionMatrix.Length + 
        EntryBiases.Length + 
        ExitSensitivity.Length + 
        PatternWeights.Length;
    
    /// <summary>
    /// Creates a new LearnedWeights with random initialization.
    /// </summary>
    public static LearnedWeights CreateRandom(string symbol, Random rng)
    {
        var w = new LearnedWeights { Symbol = symbol };
        
        // Initialize indicator weights around typical values
        for (int i = 0; i < w.IndicatorWeights.Length; i++)
            w.IndicatorWeights[i] = 0.5 + rng.NextDouble() * 0.5; // 0.5 to 1.0
        
        // Initialize multipliers around 1.0
        for (int i = 0; i < w.TrendingMultipliers.Length; i++)
            w.TrendingMultipliers[i] = 0.8 + rng.NextDouble() * 0.4; // 0.8 to 1.2
        
        for (int i = 0; i < w.RangingMultipliers.Length; i++)
            w.RangingMultipliers[i] = 0.8 + rng.NextDouble() * 0.4;
        
        // Time weights around 1.0
        for (int i = 0; i < w.TimeWeights.Length; i++)
            w.TimeWeights[i] = 0.7 + rng.NextDouble() * 0.6; // 0.7 to 1.3
        
        // Interaction matrix - small values, positive and negative
        for (int i = 0; i < w.InteractionMatrix.Length; i++)
            w.InteractionMatrix[i] = (rng.NextDouble() - 0.5) * 0.2; // -0.1 to 0.1
        
        // Entry biases
        w.EntryBiases[0] = 60 + rng.NextDouble() * 20; // base_long: 60-80
        w.EntryBiases[1] = -60 - rng.NextDouble() * 20; // base_short: -60 to -80
        for (int i = 2; i < w.EntryBiases.Length; i++)
            w.EntryBiases[i] = (rng.NextDouble() - 0.5) * 20; // adjustments
        
        // Exit sensitivity
        for (int i = 0; i < w.ExitSensitivity.Length; i++)
            w.ExitSensitivity[i] = 0.3 + rng.NextDouble() * 0.4; // 0.3 to 0.7
        
        // Pattern weights
        for (int i = 0; i < w.PatternWeights.Length; i++)
            w.PatternWeights[i] = rng.NextDouble() - 0.5; // -0.5 to 0.5
        
        return w;
    }
    
    /// <summary>
    /// Creates a mutated copy of this weight vector.
    /// </summary>
    public LearnedWeights Mutate(Random rng, double mutationRate = 0.1, double mutationStrength = 0.1)
    {
        var child = Clone();
        child.Generation = Generation + 1;
        
        MutateArray(child.IndicatorWeights, rng, mutationRate, mutationStrength);
        MutateArray(child.TrendingMultipliers, rng, mutationRate, mutationStrength);
        MutateArray(child.RangingMultipliers, rng, mutationRate, mutationStrength);
        MutateArray(child.TimeWeights, rng, mutationRate, mutationStrength);
        MutateArray(child.InteractionMatrix, rng, mutationRate, mutationStrength * 0.5); // Smaller mutations for interactions
        MutateArray(child.EntryBiases, rng, mutationRate, mutationStrength * 5); // Larger scale
        MutateArray(child.ExitSensitivity, rng, mutationRate, mutationStrength);
        MutateArray(child.PatternWeights, rng, mutationRate, mutationStrength);
        
        return child;
    }
    
    /// <summary>
    /// Crossover with another weight vector.
    /// </summary>
    public LearnedWeights Crossover(LearnedWeights other, Random rng)
    {
        var child = Clone();
        child.Generation = Math.Max(Generation, other.Generation) + 1;
        
        CrossoverArray(child.IndicatorWeights, other.IndicatorWeights, rng);
        CrossoverArray(child.TrendingMultipliers, other.TrendingMultipliers, rng);
        CrossoverArray(child.RangingMultipliers, other.RangingMultipliers, rng);
        CrossoverArray(child.TimeWeights, other.TimeWeights, rng);
        CrossoverArray(child.InteractionMatrix, other.InteractionMatrix, rng);
        CrossoverArray(child.EntryBiases, other.EntryBiases, rng);
        CrossoverArray(child.ExitSensitivity, other.ExitSensitivity, rng);
        CrossoverArray(child.PatternWeights, other.PatternWeights, rng);
        
        return child;
    }
    
    /// <summary>
    /// Creates a deep copy.
    /// </summary>
    public LearnedWeights Clone()
    {
        return new LearnedWeights
        {
            Symbol = Symbol,
            CreatedAt = CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Generation = Generation,
            LearningMethod = LearningMethod,
            IndicatorWeights = (double[])IndicatorWeights.Clone(),
            TrendingMultipliers = (double[])TrendingMultipliers.Clone(),
            RangingMultipliers = (double[])RangingMultipliers.Clone(),
            TimeWeights = (double[])TimeWeights.Clone(),
            InteractionMatrix = (double[])InteractionMatrix.Clone(),
            EntryBiases = (double[])EntryBiases.Clone(),
            ExitSensitivity = (double[])ExitSensitivity.Clone(),
            PatternWeights = (double[])PatternWeights.Clone()
        };
    }
    
    /// <summary>
    /// Flattens all weights to a single vector (for neural network input/output).
    /// </summary>
    public double[] ToFlatVector()
    {
        var result = new List<double>();
        result.AddRange(IndicatorWeights);
        result.AddRange(TrendingMultipliers);
        result.AddRange(RangingMultipliers);
        result.AddRange(TimeWeights);
        result.AddRange(InteractionMatrix);
        result.AddRange(EntryBiases);
        result.AddRange(ExitSensitivity);
        result.AddRange(PatternWeights);
        return result.ToArray();
    }
    
    /// <summary>
    /// Loads weights from a flat vector.
    /// </summary>
    public void FromFlatVector(double[] vector)
    {
        int idx = 0;
        Array.Copy(vector, idx, IndicatorWeights, 0, IndicatorWeights.Length); idx += IndicatorWeights.Length;
        Array.Copy(vector, idx, TrendingMultipliers, 0, TrendingMultipliers.Length); idx += TrendingMultipliers.Length;
        Array.Copy(vector, idx, RangingMultipliers, 0, RangingMultipliers.Length); idx += RangingMultipliers.Length;
        Array.Copy(vector, idx, TimeWeights, 0, TimeWeights.Length); idx += TimeWeights.Length;
        Array.Copy(vector, idx, InteractionMatrix, 0, InteractionMatrix.Length); idx += InteractionMatrix.Length;
        Array.Copy(vector, idx, EntryBiases, 0, EntryBiases.Length); idx += EntryBiases.Length;
        Array.Copy(vector, idx, ExitSensitivity, 0, ExitSensitivity.Length); idx += ExitSensitivity.Length;
        Array.Copy(vector, idx, PatternWeights, 0, PatternWeights.Length);
    }
    
    private static void MutateArray(double[] arr, Random rng, double rate, double strength)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            if (rng.NextDouble() < rate)
            {
                arr[i] += (rng.NextDouble() - 0.5) * 2 * strength;
            }
        }
    }
    
    private static void CrossoverArray(double[] target, double[] other, Random rng)
    {
        for (int i = 0; i < target.Length; i++)
        {
            if (rng.NextDouble() < 0.5)
            {
                target[i] = other[i];
            }
        }
    }
}

/// <summary>
/// Comparison result for different learning methods.
/// </summary>
public sealed class LearningComparison
{
    public string Symbol { get; set; } = "";
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
    
    // Results from each method
    public LearnedWeights? GeneticResult { get; set; }
    public LearnedWeights? NeuralResult { get; set; }
    public LearnedWeights? GradientResult { get; set; }
    
    // The winner (highest validation fitness)
    public string BestMethod { get; set; } = "";
    public LearnedWeights? BestWeights { get; set; }
    
    public TimeSpan GeneticDuration { get; set; }
    public TimeSpan NeuralDuration { get; set; }
    public TimeSpan GradientDuration { get; set; }
}
