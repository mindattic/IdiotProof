namespace IdiotProof.Evidence.Evaluation;

/// <summary>One observation of a day-clustered sample: its trading day, its value, and the group it belongs to (0 = control, 1 = treated).</summary>
public readonly record struct ClusterObservation(DateOnly Day, double Value, int Group = 0);

/// <summary>A statistic with its day-clustered bootstrap interval and two-sided p against zero.</summary>
public sealed record BootstrapInterval(double Estimate, double Low, double High, double PValueTwoSided, int Days, int Observations, int Draws)
{
    /// <summary>The whole interval sits at or above <paramref name="threshold"/>.</summary>
    public bool EntirelyAbove(double threshold) => !double.IsNaN(Low) && Low >= threshold;

    /// <summary>The whole interval sits at or below <paramref name="threshold"/>.</summary>
    public bool EntirelyBelow(double threshold) => !double.IsNaN(High) && High <= threshold;
}

/// <summary>
/// Bootstrap that resamples whole trading days, so observations sharing a day (every symbol that
/// gapped on one market move) count as one draw of evidence. Any statistic of the resampled
/// observations can be bootstrapped; the common ones (mean, group mean difference, median) are
/// provided. Seeded, so a run is reproducible.
/// </summary>
public static class DayClusteredBootstrap
{
    public static BootstrapInterval Run(IReadOnlyList<ClusterObservation> observations, Func<IReadOnlyList<ClusterObservation>, double> statistic,
        int draws = 10000, int seed = 1337, double ciLevel = 0.95, int minDays = 5)
    {
        var byDay = observations.GroupBy(o => o.Day).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
        double estimate = observations.Count == 0 ? double.NaN : statistic(observations);
        if (byDay.Length < minDays || draws < 1 || double.IsNaN(estimate))
            return new BootstrapInterval(estimate, double.NaN, double.NaN, 1, byDay.Length, observations.Count, 0);

        var rng = new Random(seed);
        var buffer = new List<ClusterObservation>(observations.Count);
        var boot = new List<double>(draws);
        for (int b = 0; b < draws; b++)
        {
            buffer.Clear();
            for (int i = 0; i < byDay.Length; i++) buffer.AddRange(byDay[rng.Next(byDay.Length)]);
            var s = statistic(buffer);
            if (!double.IsNaN(s)) boot.Add(s);
        }
        if (boot.Count == 0) return new BootstrapInterval(estimate, double.NaN, double.NaN, 1, byDay.Length, observations.Count, 0);
        boot.Sort();
        double tail = (1 - ciLevel) / 2;
        double low = boot[(int)Math.Floor(tail * (boot.Count - 1))];
        double high = boot[(int)Math.Ceiling((1 - tail) * (boot.Count - 1))];
        int atOrBelowZero = boot.Count(v => v <= 0), atOrAboveZero = boot.Count(v => v >= 0);
        double p = Math.Min(1, 2.0 * (1 + Math.Min(atOrBelowZero, atOrAboveZero)) / (boot.Count + 1));
        return new BootstrapInterval(estimate, low, high, p, byDay.Length, observations.Count, boot.Count);
    }

    /// <summary>Mean of group 1 minus mean of group 0; NaN when either group is empty.</summary>
    public static double MeanDifference(IReadOnlyList<ClusterObservation> obs)
    {
        double s1 = 0, s0 = 0;
        int n1 = 0, n0 = 0;
        foreach (var o in obs)
        {
            if (o.Group == 1) { s1 += o.Value; n1++; }
            else { s0 += o.Value; n0++; }
        }
        return n1 == 0 || n0 == 0 ? double.NaN : s1 / n1 - s0 / n0;
    }

    public static double Mean(IReadOnlyList<ClusterObservation> obs)
    {
        if (obs.Count == 0) return double.NaN;
        double s = 0;
        foreach (var o in obs) s += o.Value;
        return s / obs.Count;
    }

    public static double Median(IReadOnlyList<ClusterObservation> obs)
    {
        if (obs.Count == 0) return double.NaN;
        var values = new double[obs.Count];
        for (int i = 0; i < obs.Count; i++) values[i] = obs[i].Value;
        Array.Sort(values);
        int n = values.Length;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
    }

    /// <summary>Rate of group 1 over rate of group 0 for 0/1 values; NaN when either group is empty or the control rate is zero.</summary>
    public static double RateRatio(IReadOnlyList<ClusterObservation> obs)
    {
        double s1 = 0, s0 = 0;
        int n1 = 0, n0 = 0;
        foreach (var o in obs)
        {
            if (o.Group == 1) { s1 += o.Value; n1++; }
            else { s0 += o.Value; n0++; }
        }
        if (n1 == 0 || n0 == 0 || s0 == 0) return double.NaN;
        return (s1 / n1) / (s0 / n0);
    }
}
