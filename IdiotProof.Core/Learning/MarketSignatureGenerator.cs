// ============================================================================
// Market Signature Generator - Converts Market Data to LSH Feature Vectors
// ============================================================================
//
// Transforms indicator snapshots into normalized feature vectors suitable
// for Locality-Sensitive Hashing. The goal is to capture market "state"
// in a way that similar market conditions produce similar signatures.
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  FEATURE VECTOR CONSTRUCTION                                              ║
// ║                                                                           ║
// ║  Each market snapshot is converted to a 16-dimensional feature vector:   ║
// ║                                                                           ║
// ║  [0] VWAP Position      - Price distance from VWAP (normalized)          ║
// ║  [1] EMA9 Position      - Price distance from EMA9 (normalized)          ║
// ║  [2] EMA21 Position     - Price distance from EMA21 (normalized)         ║
// ║  [3] EMA Spread         - EMA9/EMA21 relationship                        ║
// ║  [4] RSI                - RSI value (0-100 → -1 to +1)                   ║
// ║  [5] MACD Signal        - MACD vs Signal line                            ║
// ║  [6] MACD Histogram     - Momentum direction                             ║
// ║  [7] ADX Strength       - Trend strength (0-100 → 0 to +1)               ║
// ║  [8] DI Direction       - +DI vs -DI                                     ║
// ║  [9] Volume Ratio       - Current volume vs average                      ║
// ║  [10] Bollinger Position - Where price is in bands                       ║
// ║  [11] Stochastic K      - Overbought/oversold                            ║
// ║  [12] OBV Trend         - On-balance volume direction                    ║
// ║  [13] CCI               - Commodity Channel Index                        ║
// ║  [14] Williams %R       - Overbought/oversold                            ║
// ║  [15] ATR Normalized    - Volatility level                               ║
// ║                                                                           ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// ============================================================================

using IdiotProof.Calculators;
using IdiotProof.Helpers;

namespace IdiotProof.Learning;

/// <summary>
/// Generates normalized feature vectors from market indicator snapshots.
/// These vectors can be hashed by LSHService to find similar market conditions.
/// </summary>
public sealed class MarketSignatureGenerator
{
    /// <summary>
    /// Number of features in the generated vector.
    /// Must match LSHService.FeatureDimension.
    /// </summary>
    public const int FeatureDimension = 16;

    private readonly LSHService _lsh;

    /// <summary>
    /// Creates a new signature generator with an associated LSH service.
    /// </summary>
    /// <param name="dataDirectory">Directory for LSH hyperplanes storage.</param>
    /// <param name="bitCount">Number of bits in the signature.</param>
    public MarketSignatureGenerator(string dataDirectory, int bitCount = 256)
    {
        _lsh = new LSHService(dataDirectory, FeatureDimension, bitCount);
    }

    /// <summary>
    /// Gets the underlying LSH service.
    /// </summary>
    public LSHService LSH => _lsh;

    /// <summary>
    /// Converts an indicator snapshot to a normalized feature vector.
    /// All features are normalized to approximately [-1, +1] range.
    /// </summary>
    /// <param name="snapshot">The indicator snapshot to convert.</param>
    /// <returns>A 16-dimensional normalized feature vector.</returns>
    public float[] ToFeatureVector(IndicatorSnapshot snapshot)
    {
        var features = new float[FeatureDimension];

        // [0] VWAP Position: How far price is from VWAP
        // Normalize: 5% above VWAP = +1, 5% below = -1
        if (snapshot.Vwap > 0)
        {
            var vwapDist = (snapshot.Price - snapshot.Vwap) / snapshot.Vwap;
            features[0] = Clamp((float)(vwapDist / 0.05), -1, 1);
        }

        // [1] EMA9 Position: Price distance from EMA9
        if (snapshot.Ema9 > 0)
        {
            var ema9Dist = (snapshot.Price - snapshot.Ema9) / snapshot.Ema9;
            features[1] = Clamp((float)(ema9Dist / 0.03), -1, 1);
        }

        // [2] EMA21 Position: Price distance from EMA21
        if (snapshot.Ema21 > 0)
        {
            var ema21Dist = (snapshot.Price - snapshot.Ema21) / snapshot.Ema21;
            features[2] = Clamp((float)(ema21Dist / 0.04), -1, 1);
        }

        // [3] EMA Spread: Short vs long EMA relationship
        if (snapshot.Ema21 > 0)
        {
            var emaSpread = (snapshot.Ema9 - snapshot.Ema21) / snapshot.Ema21;
            features[3] = Clamp((float)(emaSpread / 0.02), -1, 1);
        }

        // [4] RSI: Convert 0-100 to -1 to +1
        // 50 = 0, 30 or below = -1, 70 or above = +1
        features[4] = Clamp((float)((snapshot.Rsi - 50) / 25), -1, 1);

        // [5] MACD Signal: Relationship between MACD and Signal line
        if (snapshot.MacdSignal != 0)
        {
            var macdRatio = (snapshot.Macd - snapshot.MacdSignal) / Math.Abs(snapshot.MacdSignal);
            features[5] = Clamp((float)macdRatio, -1, 1);
        }
        else if (snapshot.Macd != 0)
        {
            features[5] = snapshot.Macd > 0 ? 1f : -1f;
        }

        // [6] MACD Histogram: Momentum direction and strength
        // Normalize by typical histogram range
        features[6] = Clamp((float)(snapshot.MacdHistogram * 100), -1, 1);

        // [7] ADX Strength: Trend strength (0-100 → 0 to 1)
        // Strong trend (40+) = 1, no trend (0) = 0
        features[7] = Clamp((float)(snapshot.Adx / 50), 0, 1);

        // [8] DI Direction: +DI vs -DI
        var diDiff = snapshot.PlusDi - snapshot.MinusDi;
        var diSum = snapshot.PlusDi + snapshot.MinusDi;
        if (diSum > 0)
        {
            features[8] = Clamp((float)(diDiff / diSum), -1, 1);
        }

        // [9] Volume Ratio: Current volume vs average
        // 1x = 0, 2x = +1, 0.5x = -0.5
        features[9] = Clamp((float)(snapshot.VolumeRatio - 1), -1, 2);

        // [10] Bollinger Position: Where price is within bands
        if (snapshot.BollingerUpper > snapshot.BollingerLower)
        {
            var bandWidth = snapshot.BollingerUpper - snapshot.BollingerLower;
            var position = (snapshot.Price - snapshot.BollingerMiddle) / (bandWidth / 2);
            features[10] = Clamp((float)position, -1, 1);
        }

        // [11] Stochastic K: Overbought/oversold
        // K 50 = 0, K 0 = -1, K 100 = +1
        features[11] = Clamp((float)((snapshot.StochasticK - 50) / 50), -1, 1);

        // [12] OBV Trend: On-balance volume direction
        features[12] = Clamp((float)snapshot.ObvSlope, -1, 1);

        // [13] CCI: Already ranges from -300 to +300 typically
        // Normalize so ±100 = ±0.5
        features[13] = Clamp((float)(snapshot.Cci / 200), -1, 1);

        // [14] Williams %R: Ranges 0 to -100 typically
        // -50 = 0, 0 = +1 (overbought), -100 = -1 (oversold)
        features[14] = Clamp((float)((snapshot.WilliamsR + 50) / 50), -1, 1);

        // [15] ATR Normalized: Volatility level relative to price
        if (snapshot.Price > 0)
        {
            var atrPct = snapshot.Atr / snapshot.Price * 100;
            // 1% ATR = 0.5, 3% ATR = 1.5 (capped)
            features[15] = Clamp((float)(atrPct / 2), 0, 2);
        }

        return features;
    }

    /// <summary>
    /// Generates a binary signature from an indicator snapshot.
    /// </summary>
    /// <param name="snapshot">The indicator snapshot.</param>
    /// <returns>Binary signature suitable for Hamming distance comparison.</returns>
    public byte[] GetSignature(IndicatorSnapshot snapshot)
    {
        var features = ToFeatureVector(snapshot);
        return _lsh.GetSignature(features);
    }

    /// <summary>
    /// Generates a binary signature from a raw feature vector.
    /// </summary>
    public byte[] GetSignature(float[] features)
    {
        return _lsh.GetSignature(features);
    }

    /// <summary>
    /// Computes Hamming distance between two signatures.
    /// </summary>
    public int HammingDistance(byte[] a, byte[] b)
    {
        return LSHService.HammingDistance(a, b);
    }

    /// <summary>
    /// Computes similarity percentage between two snapshots.
    /// </summary>
    public double ComputeSimilarity(IndicatorSnapshot a, IndicatorSnapshot b)
    {
        var sigA = GetSignature(a);
        var sigB = GetSignature(b);
        return _lsh.ComputeSimilarity(sigA, sigB);
    }

    /// <summary>
    /// Clamps a value to a range.
    /// </summary>
    private static float Clamp(float value, float min, float max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    /// <summary>
    /// Converts a feature vector to a human-readable string for debugging.
    /// </summary>
    public static string FeatureVectorToString(float[] features)
    {
        if (features == null || features.Length < FeatureDimension)
            return "[invalid]";

        return $"VWAP:{features[0]:+0.00;-0.00} EMA9:{features[1]:+0.00;-0.00} EMA21:{features[2]:+0.00;-0.00} " +
               $"Spread:{features[3]:+0.00;-0.00} RSI:{features[4]:+0.00;-0.00} MACD:{features[5]:+0.00;-0.00} " +
               $"Hist:{features[6]:+0.00;-0.00} ADX:{features[7]:0.00} DI:{features[8]:+0.00;-0.00} " +
               $"Vol:{features[9]:+0.00;-0.00}";
    }

    /// <summary>
    /// Converts a binary signature to a hex string for storage/display.
    /// </summary>
    public static string SignatureToHex(byte[] signature)
    {
        return Convert.ToHexString(signature);
    }

    /// <summary>
    /// Converts a hex string back to a binary signature.
    /// </summary>
    public static byte[] HexToSignature(string hex)
    {
        return Convert.FromHexString(hex);
    }
}
