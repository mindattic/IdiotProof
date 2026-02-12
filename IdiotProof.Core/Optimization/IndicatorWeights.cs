// ============================================================================
// Indicator Weights - Configurable indicator weight configuration
// ============================================================================
//
// Defines the weights applied to each indicator when calculating market score.
// These weights are optimizable through backtesting to find the best combination
// for a specific ticker or market regime.
//
// ============================================================================

using IdiotProof.Services;

namespace IdiotProof.Optimization;

/// <summary>
/// Configurable weights for market score calculation.
/// Weights should sum to 1.0 (100%).
/// </summary>
public sealed record IndicatorWeights
{
    /// <summary>VWAP Position weight (default 15%).</summary>
    public double Vwap { get; init; } = 0.15;

    /// <summary>EMA Stack Alignment weight (default 20%).</summary>
    public double Ema { get; init; } = 0.20;

    /// <summary>RSI Momentum weight (default 15%).</summary>
    public double Rsi { get; init; } = 0.15;

    /// <summary>MACD Signal weight (default 20%).</summary>
    public double Macd { get; init; } = 0.20;

    /// <summary>ADX Trend Strength weight (default 20%).</summary>
    public double Adx { get; init; } = 0.20;

    /// <summary>Volume Confirmation weight (default 10%).</summary>
    public double Volume { get; init; } = 0.10;

    /// <summary>
    /// Validates that weights sum to approximately 1.0.
    /// </summary>
    public bool IsValid => Math.Abs(Sum - 1.0) < 0.001;

    /// <summary>Sum of all weights.</summary>
    public double Sum => Vwap + Ema + Rsi + Macd + Adx + Volume;

    /// <summary>
    /// Normalizes weights to sum to 1.0.
    /// </summary>
    public IndicatorWeights Normalize()
    {
        double sum = Sum;
        if (sum == 0) return Default;
        
        return new IndicatorWeights
        {
            Vwap = Vwap / sum,
            Ema = Ema / sum,
            Rsi = Rsi / sum,
            Macd = Macd / sum,
            Adx = Adx / sum,
            Volume = Volume / sum
        };
    }

    /// <summary>
    /// Converts to array for optimization algorithms.
    /// Order: [VWAP, EMA, RSI, MACD, ADX, Volume]
    /// </summary>
    public double[] ToArray() => [Vwap, Ema, Rsi, Macd, Adx, Volume];

    /// <summary>
    /// Creates from array.
    /// </summary>
    public static IndicatorWeights FromArray(double[] weights)
    {
        if (weights.Length != 6)
            throw new ArgumentException("Expected 6 weights", nameof(weights));

        return new IndicatorWeights
        {
            Vwap = weights[0],
            Ema = weights[1],
            Rsi = weights[2],
            Macd = weights[3],
            Adx = weights[4],
            Volume = weights[5]
        }.Normalize();
    }

    /// <summary>
    /// Creates a mutated version with random perturbation.
    /// </summary>
    public IndicatorWeights Mutate(Random random, double mutationStrength = 0.1)
    {
        return new IndicatorWeights
        {
            Vwap = Math.Max(0.01, Vwap + (random.NextDouble() - 0.5) * mutationStrength),
            Ema = Math.Max(0.01, Ema + (random.NextDouble() - 0.5) * mutationStrength),
            Rsi = Math.Max(0.01, Rsi + (random.NextDouble() - 0.5) * mutationStrength),
            Macd = Math.Max(0.01, Macd + (random.NextDouble() - 0.5) * mutationStrength),
            Adx = Math.Max(0.01, Adx + (random.NextDouble() - 0.5) * mutationStrength),
            Volume = Math.Max(0.01, Volume + (random.NextDouble() - 0.5) * mutationStrength)
        }.Normalize();
    }

    /// <summary>
    /// Crossover with another weight set (genetic algorithm).
    /// </summary>
    public IndicatorWeights Crossover(IndicatorWeights other, Random random)
    {
        return new IndicatorWeights
        {
            Vwap = random.Next(2) == 0 ? Vwap : other.Vwap,
            Ema = random.Next(2) == 0 ? Ema : other.Ema,
            Rsi = random.Next(2) == 0 ? Rsi : other.Rsi,
            Macd = random.Next(2) == 0 ? Macd : other.Macd,
            Adx = random.Next(2) == 0 ? Adx : other.Adx,
            Volume = random.Next(2) == 0 ? Volume : other.Volume
        }.Normalize();
    }

    /// <summary>Default weights as documented.</summary>
    public static IndicatorWeights Default => new();

    /// <summary>Equal weights for all indicators.</summary>
    public static IndicatorWeights Equal => new()
    {
        Vwap = 1.0 / 6,
        Ema = 1.0 / 6,
        Rsi = 1.0 / 6,
        Macd = 1.0 / 6,
        Adx = 1.0 / 6,
        Volume = 1.0 / 6
    };

    /// <summary>Momentum-focused weights (RSI, MACD, ADX emphasized).</summary>
    public static IndicatorWeights Momentum => new()
    {
        Vwap = 0.10,
        Ema = 0.10,
        Rsi = 0.25,
        Macd = 0.25,
        Adx = 0.25,
        Volume = 0.05
    };

    /// <summary>Trend-following weights (ADX, EMA emphasized).</summary>
    public static IndicatorWeights TrendFollowing => new()
    {
        Vwap = 0.10,
        Ema = 0.30,
        Rsi = 0.10,
        Macd = 0.15,
        Adx = 0.30,
        Volume = 0.05
    };

    /// <summary>Mean-reversion weights (RSI, Bollinger emphasized).</summary>
    public static IndicatorWeights MeanReversion => new()
    {
        Vwap = 0.20,
        Ema = 0.10,
        Rsi = 0.35,
        Macd = 0.10,
        Adx = 0.10,
        Volume = 0.15
    };

    /// <summary>
    /// Generates random weights (for initial population in genetic algorithm).
    /// </summary>
    public static IndicatorWeights Random(Random random)
    {
        return new IndicatorWeights
        {
            Vwap = random.NextDouble(),
            Ema = random.NextDouble(),
            Rsi = random.NextDouble(),
            Macd = random.NextDouble(),
            Adx = random.NextDouble(),
            Volume = random.NextDouble()
        }.Normalize();
    }

    public override string ToString() =>
        $"Weights[VWAP:{Vwap:P0} EMA:{Ema:P0} RSI:{Rsi:P0} MACD:{Macd:P0} ADX:{Adx:P0} VOL:{Volume:P0}]";

    /// <summary>
    /// Returns a compact string for logging.
    /// </summary>
    public string ToCompactString() =>
        $"[{Vwap:F2}/{Ema:F2}/{Rsi:F2}/{Macd:F2}/{Adx:F2}/{Volume:F2}]";

    /// <summary>
    /// Converts this simplified optimization weights to the full Calculator weights.
    /// Extended indicators (Bollinger, Stochastic, OBV, CCI, WilliamsR) use small default values.
    /// Respects indicator-config.json toggles: disabled indicators are zeroed, remaining normalized.
    /// </summary>
    public IdiotProof.Calculators.IndicatorWeights ToCalculatorWeights()
    {
        // If using default weights (not custom optimization), defer entirely to indicator config
        if (this == Default)
        {
            return Services.IndicatorConfigManager.GetWeights();
        }

        // For custom optimization weights, build the full 13-indicator set
        // then apply indicator-config.json disabled toggles
        const double extendedTotal = 0.33;  // 33% for extended indicators
        const double coreTotal = 1.0 - extendedTotal;  // 67% for core
        double sum = Vwap + Ema + Rsi + Macd + Adx + Volume;
        double scale = sum > 0 ? coreTotal / sum : coreTotal / 6;

        // Get indicator config to check which are disabled
        var config = Services.IndicatorConfigManager.Load();

        double vwap = config.Vwap.Enabled ? Vwap * scale : 0;
        double ema = config.Ema.Enabled ? Ema * scale : 0;
        double rsi = config.Rsi.Enabled ? Rsi * scale : 0;
        double macd = config.Macd.Enabled ? Macd * scale : 0;
        double adx = config.Adx.Enabled ? Adx * scale : 0;
        double volume = config.Volume.Enabled ? Volume * scale : 0;
        double bollinger = config.Bollinger.Enabled ? 0.05 : 0;
        double stochastic = config.Stochastic.Enabled ? 0.05 : 0;
        double obv = config.Obv.Enabled ? 0.05 : 0;
        double cci = config.Cci.Enabled ? 0.03 : 0;
        double williamsR = config.WilliamsR.Enabled ? 0.03 : 0;
        double sma = config.Sma.Enabled ? 0.06 : 0;
        double momentum = config.Momentum.Enabled ? 0.06 : 0;

        // Normalize so enabled weights sum to 1.0
        double total = vwap + ema + rsi + macd + adx + volume +
                       bollinger + stochastic + obv + cci + williamsR + sma + momentum;
        if (total <= 0) return IdiotProof.Calculators.IndicatorWeights.Default;

        return new IdiotProof.Calculators.IndicatorWeights
        {
            Vwap = vwap / total,
            Ema = ema / total,
            Rsi = rsi / total,
            Macd = macd / total,
            Adx = adx / total,
            Volume = volume / total,
            Bollinger = bollinger / total,
            Stochastic = stochastic / total,
            Obv = obv / total,
            Cci = cci / total,
            WilliamsR = williamsR / total,
            Sma = sma / total,
            Momentum = momentum / total
        };
    }
}

/// <summary>
/// Extended weight configuration that includes entry/exit thresholds.
/// </summary>
public sealed record OptimizableConfig
{
    /// <summary>Indicator weights for score calculation.</summary>
    public IndicatorWeights Weights { get; init; } = IndicatorWeights.Default;

    /// <summary>Score threshold for long entry (0-100).</summary>
    public int LongEntryThreshold { get; init; } = 70;

    /// <summary>Score threshold for short entry (-100-0).</summary>
    public int ShortEntryThreshold { get; init; } = -70;

    /// <summary>Score threshold for long exit (0-100).</summary>
    public int LongExitThreshold { get; init; } = 40;

    /// <summary>Score threshold for short exit (-100-0).</summary>
    public int ShortExitThreshold { get; init; } = -40;

    /// <summary>ATR multiplier for take profit.</summary>
    public double TakeProfitAtr { get; init; } = 2.0;

    /// <summary>ATR multiplier for stop loss.</summary>
    public double StopLossAtr { get; init; } = 1.5;

    /// <summary>Minimum volume ratio required for entry.</summary>
    public double MinVolumeRatio { get; init; } = 1.0;

    /// <summary>Minimum ADX for trend requirement.</summary>
    public double MinAdx { get; init; } = 20;

    /// <summary>
    /// Mutates the configuration for genetic algorithm.
    /// </summary>
    public OptimizableConfig Mutate(Random random, double strength = 0.1)
    {
        return new OptimizableConfig
        {
            Weights = Weights.Mutate(random, strength),
            LongEntryThreshold = Math.Clamp(LongEntryThreshold + random.Next(-5, 6), 50, 90),
            ShortEntryThreshold = Math.Clamp(ShortEntryThreshold + random.Next(-5, 6), -90, -50),
            LongExitThreshold = Math.Clamp(LongExitThreshold + random.Next(-5, 6), 20, 60),
            ShortExitThreshold = Math.Clamp(ShortExitThreshold + random.Next(-5, 6), -60, -20),
            TakeProfitAtr = Math.Clamp(TakeProfitAtr + (random.NextDouble() - 0.5) * strength, 1.0, 4.0),
            StopLossAtr = Math.Clamp(StopLossAtr + (random.NextDouble() - 0.5) * strength, 0.8, 3.0),
            MinVolumeRatio = Math.Clamp(MinVolumeRatio + (random.NextDouble() - 0.5) * strength, 0.5, 2.0),
            MinAdx = Math.Clamp(MinAdx + (random.NextDouble() - 0.5) * 10, 10, 40)
        };
    }

    /// <summary>
    /// Crossover with another configuration.
    /// </summary>
    public OptimizableConfig Crossover(OptimizableConfig other, Random random)
    {
        return new OptimizableConfig
        {
            Weights = Weights.Crossover(other.Weights, random),
            LongEntryThreshold = random.Next(2) == 0 ? LongEntryThreshold : other.LongEntryThreshold,
            ShortEntryThreshold = random.Next(2) == 0 ? ShortEntryThreshold : other.ShortEntryThreshold,
            LongExitThreshold = random.Next(2) == 0 ? LongExitThreshold : other.LongExitThreshold,
            ShortExitThreshold = random.Next(2) == 0 ? ShortExitThreshold : other.ShortExitThreshold,
            TakeProfitAtr = random.Next(2) == 0 ? TakeProfitAtr : other.TakeProfitAtr,
            StopLossAtr = random.Next(2) == 0 ? StopLossAtr : other.StopLossAtr,
            MinVolumeRatio = random.Next(2) == 0 ? MinVolumeRatio : other.MinVolumeRatio,
            MinAdx = random.Next(2) == 0 ? MinAdx : other.MinAdx
        };
    }

    /// <summary>
    /// Generates random configuration.
    /// </summary>
    public static OptimizableConfig Random(Random random) => new()
    {
        Weights = IndicatorWeights.Random(random),
        LongEntryThreshold = random.Next(50, 90),
        ShortEntryThreshold = -random.Next(50, 90),
        LongExitThreshold = random.Next(20, 60),
        ShortExitThreshold = -random.Next(20, 60),
        TakeProfitAtr = 1.0 + random.NextDouble() * 3.0,
        StopLossAtr = 0.8 + random.NextDouble() * 2.2,
        MinVolumeRatio = 0.5 + random.NextDouble() * 1.5,
        MinAdx = 10 + random.NextDouble() * 30
    };

    public override string ToString() =>
        $"{Weights} Entry:+{LongEntryThreshold}/{ShortEntryThreshold} Exit:+{LongExitThreshold}/{ShortExitThreshold} TP:{TakeProfitAtr:F1}ATR SL:{StopLossAtr:F1}ATR";
}
