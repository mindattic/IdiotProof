using IdiotProof.Evidence.Microstructure;
using IdiotProof.Models;

namespace IdiotProof.Evidence.PointProcess;

/// <summary>Per-bar Hawkes features: how hot activity runs against the baseline, and how self-exciting it is right now.</summary>
public readonly record struct IgnitionReading(double IntensityRatio, double BranchingRatio, bool Ignition);

/// <summary>
/// Momentum-ignition detector. At each bar's close it looks only at trades printed before that close:
/// the trailing trade rate against a baseline fitted on an EARLIER session (never the current one),
/// and a rolling branching ratio from the count-dispersion identity Var/Mean = 1/(1 − n)² for a
/// stationary Hawkes process at windows long against the kernel, n̂ = 1 − 1/√(Var/Mean) over
/// fixed sub-bins. Ignition = both a self-exciting burst and an intensity spike at once.
/// </summary>
public sealed class IgnitionDetector
{
    public int WindowMinutes { get; init; } = 10;
    public int BinSeconds { get; init; } = 5;
    public double SpikeThreshold { get; init; } = 3.0;
    public double BranchingThreshold { get; init; } = 0.7;

    /// <param name="baselineRate">Events per second expected without a burst, e.g. a prior session's
    /// <see cref="HawkesParams.UnconditionalRate"/>; NaN or non-positive makes the intensity ratio NaN.</param>
    public IReadOnlyList<IgnitionReading> Read(IReadOnlyList<Candle> bars, IReadOnlyList<TradePrint> trades, double baselineRate)
    {
        var times = trades.Select(t => t.TimeUtc).OrderBy(t => t).ToArray();
        var result = new IgnitionReading[bars.Count];
        int binsPerWindow = WindowMinutes * 60 / BinSeconds;
        for (int i = 0; i < bars.Count; i++)
        {
            var close = bars[i].EndUtc;
            var start = close.AddMinutes(-WindowMinutes);
            int lo = LowerBound(times, start), hi = LowerBound(times, close);
            int count = hi - lo;

            double ratio = baselineRate > 0 && double.IsFinite(baselineRate)
                ? count / (baselineRate * WindowMinutes * 60.0)
                : double.NaN;

            var bins = new int[binsPerWindow];
            for (int k = lo; k < hi; k++)
            {
                int b = (int)((times[k] - start).TotalSeconds / BinSeconds);
                if (b >= 0 && b < binsPerWindow) bins[b]++;
            }
            double branching = DispersionBranching(bins);
            bool ignition = !double.IsNaN(ratio) && !double.IsNaN(branching)
                && ratio >= SpikeThreshold && branching >= BranchingThreshold;
            result[i] = new IgnitionReading(ratio, branching, ignition);
        }
        return result;
    }

    /// <summary>n̂ = 1 − 1/√(Var/Mean) of event counts per bin, clipped to [0, 0.99]; NaN with too few events.</summary>
    public static double DispersionBranching(IReadOnlyList<int> counts)
    {
        if (counts.Count < 2) return double.NaN;
        double mean = 0;
        foreach (var c in counts) mean += c;
        mean /= counts.Count;
        if (mean * counts.Count < 10) return double.NaN;
        double var = 0;
        foreach (var c in counts) var += (c - mean) * (c - mean);
        var /= counts.Count - 1;
        double fano = var / mean;
        if (fano <= 1) return 0;
        return Math.Min(0.99, 1 - 1 / Math.Sqrt(fano));
    }

    private static int LowerBound(DateTime[] xs, DateTime value)
    {
        int lo = 0, hi = xs.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (xs[mid] < value) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}
