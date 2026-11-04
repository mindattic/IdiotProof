namespace IdiotProof.Evidence;

/// <summary>A historical pattern found by the index, with what happened after it.</summary>
public readonly record struct PatternNeighbor<T>(int Id, double Similarity, T Payload);

/// <summary>
/// Locality-sensitive hashing for cosine similarity (random-hyperplane SimHash with banding).
/// Each vector gets <c>bands × bitsPerBand</c> sign bits; two vectors land in the same bucket of a
/// band with probability (1 - θ/π)^bitsPerBand, so near shapes collide in at least one band while
/// far ones almost never do. Candidates are then re-ranked by exact cosine, so the hashing only
/// decides who gets looked at, never the reported similarity.
/// </summary>
public sealed class LshIndex<T>
{
    private readonly int dimension;
    private readonly int bands;
    private readonly int bitsPerBand;
    private readonly double[][] planes;
    private readonly Dictionary<(int Band, ulong Key), List<int>> buckets = new();
    private readonly List<double[]> vectors = new();
    private readonly List<T> payloads = new();

    public LshIndex(int dimension, int bands = 16, int bitsPerBand = 8, int seed = 1337)
    {
        if (dimension < 1) throw new ArgumentOutOfRangeException(nameof(dimension));
        if (bitsPerBand < 1 || bitsPerBand > 64) throw new ArgumentOutOfRangeException(nameof(bitsPerBand));
        if (bands < 1) throw new ArgumentOutOfRangeException(nameof(bands));
        this.dimension = dimension;
        this.bands = bands;
        this.bitsPerBand = bitsPerBand;

        var rng = new Random(seed);
        planes = new double[bands * bitsPerBand][];
        for (int p = 0; p < planes.Length; p++)
        {
            var plane = new double[dimension];
            for (int d = 0; d < dimension; d++) plane[d] = Gaussian(rng);
            planes[p] = plane;
        }
    }

    public int Count => vectors.Count;

    public int Add(double[] vector, T payload)
    {
        if (vector.Length != dimension) throw new ArgumentException("Dimension mismatch.", nameof(vector));
        int id = vectors.Count;
        vectors.Add(vector);
        payloads.Add(payload);
        for (int b = 0; b < bands; b++)
        {
            var key = (b, BandKey(vector, b));
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
            list.Add(id);
        }
        return id;
    }

    /// <summary>The k most similar stored vectors among the LSH candidates, best first.</summary>
    public IReadOnlyList<PatternNeighbor<T>> Query(double[] vector, int k, double minSimilarity = double.NegativeInfinity)
    {
        if (vector.Length != dimension) throw new ArgumentException("Dimension mismatch.", nameof(vector));
        var seen = new HashSet<int>();
        for (int b = 0; b < bands; b++)
            if (buckets.TryGetValue((b, BandKey(vector, b)), out var list))
                foreach (var id in list) seen.Add(id);

        var scored = new List<PatternNeighbor<T>>(seen.Count);
        foreach (var id in seen)
        {
            double sim = ShapeVector.Cosine(vector, vectors[id]);
            if (sim >= minSimilarity) scored.Add(new PatternNeighbor<T>(id, sim, payloads[id]));
        }
        scored.Sort((x, y) => y.Similarity.CompareTo(x.Similarity));
        return scored.Count > k ? scored.GetRange(0, k) : scored;
    }

    private ulong BandKey(double[] v, int band)
    {
        ulong key = 0;
        int offset = band * bitsPerBand;
        for (int i = 0; i < bitsPerBand; i++)
        {
            var plane = planes[offset + i];
            double dot = 0;
            for (int d = 0; d < dimension; d++) dot += plane[d] * v[d];
            if (dot >= 0) key |= 1UL << i;
        }
        return key;
    }

    private static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
