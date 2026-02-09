// ============================================================================
// LSH Service - Locality-Sensitive Hashing for Pattern Matching
// ============================================================================
//
// Uses random hyperplane projections to create compact binary signatures
// from market indicator vectors. Similar market conditions produce similar
// signatures, enabling fast approximate nearest neighbor search.
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  HOW IT WORKS                                                             ║
// ║                                                                           ║
// ║  1. Generate N random hyperplanes in feature space                        ║
// ║  2. For each indicator snapshot, compute dot product with each plane       ║
// ║  3. Sign of dot product becomes a bit (1 if >= 0, else 0)                 ║
// ║  4. Result: N-bit binary signature representing market state              ║
// ║                                                                           ║
// ║  Key insight from N-cube geometry:                                        ║
// ║  - Random points cluster around N/2 Hamming distance                      ║
// ║  - Distance << N/2 indicates genuine correlation                          ║
// ║  - In 256 bits: random pairs ≈ 128 distance, similar pairs < 80           ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// ============================================================================

using System.Numerics;
using System.Text.Json;

namespace IdiotProof.Learning;

/// <summary>
/// Locality-Sensitive Hashing service using random hyperplane projections.
/// Produces compact binary signatures from feature vectors for fast pattern matching.
/// </summary>
public sealed class LSHService
{
    private const string HyperplanesFileName = "lsh_hyperplanes.json";
    private const int DefaultBitCount = 256;
    private const int DefaultSeed = 1337;

    private readonly object _lock = new();
    private readonly string _dataDirectory;

    private float[][]? _hyperplanes;

    /// <summary>
    /// Number of bits in the signature (number of hyperplanes).
    /// </summary>
    public int BitCount { get; }

    /// <summary>
    /// Dimension of the feature vectors.
    /// </summary>
    public int FeatureDimension { get; }

    /// <summary>
    /// Random seed used for hyperplane generation.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// Path to the hyperplanes file.
    /// </summary>
    private string HyperplanesFilePath => Path.Combine(_dataDirectory, HyperplanesFileName);

    /// <summary>
    /// Creates a new LSH service with the specified parameters.
    /// </summary>
    /// <param name="dataDirectory">Directory to store hyperplanes file.</param>
    /// <param name="featureDimension">Dimension of feature vectors (number of indicators).</param>
    /// <param name="bitCount">Number of bits in the signature (default 256).</param>
    /// <param name="seed">Random seed for deterministic hyperplane generation.</param>
    public LSHService(string dataDirectory, int featureDimension = 16, int bitCount = DefaultBitCount, int seed = DefaultSeed)
    {
        _dataDirectory = dataDirectory;
        FeatureDimension = featureDimension;
        BitCount = bitCount;
        Seed = seed;

        // Ensure directory exists
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }
    }

    /// <summary>
    /// Computes a binary signature from a feature vector using random hyperplane projections.
    /// Each bit indicates which side of a hyperplane the vector falls on.
    /// </summary>
    /// <param name="features">The feature vector (normalized indicator values).</param>
    /// <returns>A packed byte array containing the binary signature. Length is (BitCount + 7) / 8.</returns>
    public byte[] GetSignature(float[] features)
    {
        if (features == null || features.Length == 0)
            return [];

        EnsureHyperplanesLoaded();

        var signatureBytes = (BitCount + 7) / 8;
        var signature = new byte[signatureBytes];

        for (int i = 0; i < BitCount; i++)
        {
            var plane = _hyperplanes![i];
            var dot = DotProduct(features, plane);

            // If dot product >= 0, set bit to 1 (LSB first within each byte)
            if (dot >= 0)
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;
                signature[byteIndex] |= (byte)(1 << bitIndex);
            }
        }

        return signature;
    }

    /// <summary>
    /// Computes the Hamming distance between two binary signatures.
    /// The Hamming distance is the number of bit positions that differ.
    /// </summary>
    /// <param name="a">First signature.</param>
    /// <param name="b">Second signature.</param>
    /// <returns>Number of differing bits.</returns>
    public static int HammingDistance(byte[] a, byte[] b)
    {
        if (a == null || b == null)
            return int.MaxValue;

        var minLen = Math.Min(a.Length, b.Length);
        var maxLen = Math.Max(a.Length, b.Length);
        var distance = 0;

        // Count differing bits in common portion
        for (int i = 0; i < minLen; i++)
        {
            distance += BitOperations.PopCount((uint)(a[i] ^ b[i]));
        }

        // Any extra bytes in the longer array count as all differing bits
        distance += (maxLen - minLen) * 8;

        return distance;
    }

    /// <summary>
    /// Computes similarity as a percentage (0-100) based on Hamming distance.
    /// 100% = identical signatures, 0% = completely different.
    /// </summary>
    public double ComputeSimilarity(byte[] a, byte[] b)
    {
        var distance = HammingDistance(a, b);
        var maxDistance = BitCount;
        return Math.Max(0, (1.0 - (double)distance / maxDistance) * 100);
    }

    /// <summary>
    /// Determines if two signatures are "similar" (close in Hamming space).
    /// Uses threshold based on N-cube geometry:
    /// - Random pairs cluster around 50% (N/2 bits)
    /// - Similar items are significantly below that
    /// </summary>
    /// <param name="a">First signature.</param>
    /// <param name="b">Second signature.</param>
    /// <param name="threshold">Maximum Hamming distance to consider similar (default: BitCount/3).</param>
    public bool IsSimilar(byte[] a, byte[] b, int? threshold = null)
    {
        var maxDistance = threshold ?? BitCount / 3;  // ~33% different = similar
        return HammingDistance(a, b) <= maxDistance;
    }

    /// <summary>
    /// Computes the dot product of two vectors.
    /// </summary>
    private static float DotProduct(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        float sum = 0;
        for (int i = 0; i < len; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    /// <summary>
    /// Ensures hyperplanes are loaded from disk or generated.
    /// </summary>
    private void EnsureHyperplanesLoaded()
    {
        if (_hyperplanes != null)
            return;

        lock (_lock)
        {
            if (_hyperplanes != null)
                return;

            // Try to load from disk first
            if (TryLoadHyperplanes())
                return;

            // Generate new hyperplanes
            GenerateHyperplanes();

            // Persist for stability across sessions
            SaveHyperplanes();
        }
    }

    /// <summary>
    /// Attempts to load hyperplanes from the persisted file.
    /// </summary>
    private bool TryLoadHyperplanes()
    {
        try
        {
            if (!File.Exists(HyperplanesFilePath))
                return false;

            var json = File.ReadAllText(HyperplanesFilePath);
            var data = JsonSerializer.Deserialize<HyperplaneData>(json);

            if (data == null ||
                data.BitCount != BitCount ||
                data.FeatureDimension != FeatureDimension ||
                data.Seed != Seed ||
                data.Hyperplanes == null ||
                data.Hyperplanes.Length != BitCount)
            {
                // Mismatch in parameters, regenerate
                return false;
            }

            _hyperplanes = data.Hyperplanes;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates random hyperplanes using a deterministic PRNG.
    /// Uses Box-Muller transform to generate Gaussian-distributed values.
    /// </summary>
    private void GenerateHyperplanes()
    {
        var rng = new Random(Seed);
        _hyperplanes = new float[BitCount][];

        for (int i = 0; i < BitCount; i++)
        {
            var plane = new float[FeatureDimension];

            // Generate Gaussian-distributed random values using Box-Muller
            for (int j = 0; j < FeatureDimension; j += 2)
            {
                var (g1, g2) = BoxMuller(rng);
                plane[j] = g1;
                if (j + 1 < FeatureDimension)
                {
                    plane[j + 1] = g2;
                }
            }

            // Normalize the plane to unit length
            NormalizeVector(plane);

            _hyperplanes[i] = plane;
        }
    }

    /// <summary>
    /// Box-Muller transform to generate two independent standard normal random variables.
    /// </summary>
    private static (float, float) BoxMuller(Random rng)
    {
        double u1, u2;
        do
        {
            u1 = rng.NextDouble();
            u2 = rng.NextDouble();
        } while (u1 <= double.Epsilon);

        var mag = Math.Sqrt(-2.0 * Math.Log(u1));
        var angle = 2.0 * Math.PI * u2;

        return ((float)(mag * Math.Cos(angle)), (float)(mag * Math.Sin(angle)));
    }

    /// <summary>
    /// Normalizes a vector to unit length in place.
    /// </summary>
    private static void NormalizeVector(float[] vector)
    {
        float sumSq = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            sumSq += vector[i] * vector[i];
        }

        if (sumSq > 0)
        {
            var norm = (float)Math.Sqrt(sumSq);
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }
    }

    /// <summary>
    /// Saves hyperplanes to disk for persistence.
    /// </summary>
    private void SaveHyperplanes()
    {
        try
        {
            var data = new HyperplaneData
            {
                BitCount = BitCount,
                FeatureDimension = FeatureDimension,
                Seed = Seed,
                Hyperplanes = _hyperplanes!
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HyperplanesFilePath, json);
        }
        catch
        {
            // Hyperplanes are still in memory, continue operation
        }
    }

    /// <summary>
    /// Forces regeneration of hyperplanes (e.g., if feature dimension changes).
    /// </summary>
    public void RegenerateHyperplanes()
    {
        lock (_lock)
        {
            GenerateHyperplanes();
            SaveHyperplanes();
        }
    }

    /// <summary>
    /// Data structure for persisting hyperplanes.
    /// </summary>
    private sealed class HyperplaneData
    {
        public int BitCount { get; set; }
        public int FeatureDimension { get; set; }
        public int Seed { get; set; }
        public float[][] Hyperplanes { get; set; } = [];
    }
}
