using System.Runtime.Intrinsics;
using Corobs.Converters;
using Corobs.Seeding;

namespace IdiotProof.Evidence.Gappers;

/// <summary>One scored row: the look-alike score and the novelty of the row against the training set.</summary>
public readonly record struct CorobsScore(double Score, double Novelty, int Neighbours);

/// <summary>
/// Gapper study amendment 03 (IP-T451): a correlithm-object look-alike detector built on the Corobs
/// library, unmodified. Each feature is one ply: a <see cref="ContinuousConverter"/> line string CO
/// of <see cref="DimensionsPerPly"/> dimensions over the feature's weighted 1st-99th percentile range
/// on the training rows (values clamped into it; a missing value, or a feature whose range is empty,
/// converts to the ply midpoint). A row's state point is the plies concatenated.
///
/// A scored row's look-alike score is the weighted label rate of its <c>k</c> nearest training rows,
/// shrunk to the training base rate by <c>priorWeight</c>; its novelty is the mean distance to those
/// neighbours. Training rows from the scored row's own day can be excluded (leave-one-day-out), which
/// is how training rows are scored. Ties in distance go to the earlier training row in the order the
/// caller supplied. Distances are computed in single precision with a fixed per-lane order, so the
/// same inputs give the same neighbours on any machine.
/// </summary>
public sealed class CorobsLookAlike
{
    public const int DimensionsPerPly = 8;
    public const string SeedLabel = "gapper-study-2026-11-amendment-03";

    private readonly ContinuousConverter?[] converters;
    private readonly float[] train;
    private readonly bool[] labels;
    private readonly double[] weights;
    private readonly int[] days;

    public IReadOnlyList<string> Features { get; }
    public IReadOnlyList<(double Low, double High)> Ranges { get; }
    public int K { get; }
    public double PriorWeight { get; }
    public double BaseRate { get; }
    public int TrainRows => labels.Length;
    public int Dimensions => Features.Count * DimensionsPerPly;

    private CorobsLookAlike(IReadOnlyList<string> features, ContinuousConverter?[] converters, (double, double)[] ranges, float[] train, bool[] labels, double[] weights, int[] days, int k, double priorWeight)
    {
        Features = features;
        this.converters = converters;
        Ranges = ranges;
        this.train = train;
        this.labels = labels;
        this.weights = weights;
        this.days = days;
        K = k;
        PriorWeight = priorWeight;
        double w = weights.Sum();
        BaseRate = w > 0 ? labels.Select((l, i) => l ? weights[i] : 0).Sum() / w : double.NaN;
    }

    /// <summary>
    /// Fit the per-feature ranges and converters on the training rows and encode them.
    /// <paramref name="days"/> is any per-row day key (rows sharing a key are one day for leave-one-day-out).
    /// </summary>
    public static CorobsLookAlike Fit(IReadOnlyList<string> features, IReadOnlyList<double[]> x, IReadOnlyList<bool> y, IReadOnlyList<double> w, IReadOnlyList<int> days,
        long seed, int k = 100, double priorWeight = 10, double lowQuantile = 0.01, double highQuantile = 0.99)
    {
        if (x.Count != y.Count || x.Count != w.Count || x.Count != days.Count) throw new ArgumentException("x, y, w and days must have the same length.");
        if (k < 1) throw new ArgumentOutOfRangeException(nameof(k));
        var root = new SeededPrng(seed).Derive(SeedLabel);
        var converters = new ContinuousConverter?[features.Count];
        var ranges = new (double, double)[features.Count];
        for (int f = 0; f < features.Count; f++)
        {
            var values = new List<(double Value, double Weight)>();
            for (int i = 0; i < x.Count; i++)
                if (x[i].Length != features.Count) throw new ArgumentException($"row {i} has {x[i].Length} values, expected {features.Count}.");
                else if (double.IsFinite(x[i][f]) && w[i] > 0) values.Add((x[i][f], w[i]));
            double lo = WeightedQuantile(values, lowQuantile), hi = WeightedQuantile(values, highQuantile);
            ranges[f] = (lo, hi);
            // Every ply draws its string from its own derived stream, so dropping one feature never moves another.
            var prng = root.Derive(features[f]);
            converters[f] = hi > lo ? new ContinuousConverter(prng, DimensionsPerPly, lo, hi) : null;
        }
        var model = new CorobsLookAlike(features, converters, ranges, new float[x.Count * features.Count * DimensionsPerPly], y.ToArray(), w.ToArray(), days.ToArray(), k, priorWeight);
        for (int i = 0; i < x.Count; i++) model.Encode(x[i], model.train.AsSpan(i * model.Dimensions, model.Dimensions));
        return model;
    }

    /// <summary>The weighted quantile: the smallest value whose cumulative weight reaches q of the total; NaN when empty.</summary>
    public static double WeightedQuantile(List<(double Value, double Weight)> values, double q)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(v => v.Value).ToList();
        double total = sorted.Sum(v => v.Weight), target = q * total, cum = 0;
        foreach (var (value, weight) in sorted)
        {
            cum += weight;
            if (cum >= target) return value;
        }
        return sorted[^1].Value;
    }

    /// <summary>The state point of one row: each feature clamped into its range and converted, concatenated.</summary>
    public void Encode(double[] row, Span<float> point)
    {
        for (int f = 0; f < converters.Length; f++)
        {
            var c = converters[f];
            var slot = point.Slice(f * DimensionsPerPly, DimensionsPerPly);
            if (c is null || !double.IsFinite(row[f]))
            {
                slot.Fill(0.5f);
                continue;
            }
            var (lo, hi) = Ranges[f];
            var co = c.Convert(Math.Clamp(row[f], lo, hi));
            var coords = co.Coordinates;
            for (int d = 0; d < DimensionsPerPly; d++) slot[d] = (float)coords[d];
        }
    }

    /// <summary>Score each row; <paramref name="excludeDays"/> (when given) removes training rows of the row's own day.</summary>
    public CorobsScore[] Score(IReadOnlyList<double[]> rows, IReadOnlyList<int>? excludeDays = null, int blockSize = 32)
    {
        if (excludeDays is not null && excludeDays.Count != rows.Count) throw new ArgumentException("excludeDays must match rows.");
        int dims = Dimensions;
        var points = new float[rows.Count * dims];
        for (int i = 0; i < rows.Count; i++) Encode(rows[i], points.AsSpan(i * dims, dims));
        var result = new CorobsScore[rows.Count];
        int blocks = (rows.Count + blockSize - 1) / blockSize;
        Parallel.For(0, blocks, b =>
        {
            int start = b * blockSize, end = Math.Min(rows.Count, start + blockSize);
            var heaps = new NeighbourHeap[end - start];
            for (int q = 0; q < heaps.Length; q++) heaps[q] = new NeighbourHeap(K);
            for (int j = 0; j < labels.Length; j++)
            {
                var t = train.AsSpan(j * dims, dims);
                for (int q = start; q < end; q++)
                {
                    if (excludeDays is not null && excludeDays[q] == days[j]) continue;
                    float d2 = SquaredDistance(points.AsSpan(q * dims, dims), t);
                    heaps[q - start].Offer(d2, j);
                }
            }
            for (int q = start; q < end; q++) result[q] = Summarize(heaps[q - start]);
        });
        return result;
    }

    private CorobsScore Summarize(NeighbourHeap heap)
    {
        double wSum = 0, wPos = 0, dist = 0;
        int n = heap.Count;
        for (int i = 0; i < n; i++)
        {
            var (d2, j) = heap[i];
            wSum += weights[j];
            if (labels[j]) wPos += weights[j];
            dist += Math.Sqrt(d2);
        }
        double score = (wPos + PriorWeight * BaseRate) / (wSum + PriorWeight);
        return new CorobsScore(score, n > 0 ? dist / n : double.NaN, n);
    }

    /// <summary>Squared Euclidean distance with a fixed summation order: per-lane sums across plies, then lanes 0..7 in order.</summary>
    public static float SquaredDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var acc = Vector256<float>.Zero;
        int i = 0;
        for (; i + 8 <= a.Length; i += 8)
        {
            var d = Vector256.Create(a.Slice(i, 8)) - Vector256.Create(b.Slice(i, 8));
            acc += d * d;
        }
        float sum = 0;
        for (int lane = 0; lane < 8; lane++) sum += acc.GetElement(lane);
        for (; i < a.Length; i++)
        {
            float d = a[i] - b[i];
            sum += d * d;
        }
        return sum;
    }

    /// <summary>A bounded max-heap on (distance, training index): keeps the k nearest, earlier index wins a tie.</summary>
    private sealed class NeighbourHeap(int capacity)
    {
        private readonly float[] dist = new float[capacity];
        private readonly int[] index = new int[capacity];
        public int Count { get; private set; }
        public (float D2, int Index) this[int i] => (dist[i], index[i]);

        private bool Worse(int a, int b) => dist[a] > dist[b] || (dist[a] == dist[b] && index[a] > index[b]);

        public void Offer(float d2, int j)
        {
            if (Count < capacity)
            {
                dist[Count] = d2; index[Count] = j;
                int c = Count++;
                while (c > 0)
                {
                    int p = (c - 1) / 2;
                    if (!Worse(c, p)) break;
                    Swap(c, p); c = p;
                }
                return;
            }
            // Training rows arrive in increasing index order, so an equal distance never displaces the root.
            if (d2 >= dist[0]) return;
            dist[0] = d2; index[0] = j;
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < Count && Worse(l, m)) m = l;
                if (r < Count && Worse(r, m)) m = r;
                if (m == i) break;
                Swap(i, m); i = m;
            }
        }

        private void Swap(int a, int b)
        {
            (dist[a], dist[b]) = (dist[b], dist[a]);
            (index[a], index[b]) = (index[b], index[a]);
        }
    }
}
