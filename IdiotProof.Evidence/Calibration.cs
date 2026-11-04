namespace IdiotProof.Evidence;

/// <summary>One forecast and what really happened: a decision's predicted win probability and whether its trade made money.</summary>
public readonly record struct CalibrationSample(string Source, string Template, double Predicted, bool Won);

/// <summary>Forecasts whose predicted probability fell in [Low, High): mean prediction vs realized win rate.</summary>
public sealed record CalibrationBucket(double Low, double High, int Samples, double MeanPredicted, double RealizedRate,
    double IntervalLow, double IntervalHigh);

/// <summary>How well one (source, template)'s stated probabilities matched reality.</summary>
public sealed record CalibrationGroup(string Source, string Template, int Samples, int Wins, double MeanPredicted,
    double RealizedRate, double Brier, double BaselineBrier, IReadOnlyList<CalibrationBucket> Buckets);

/// <summary>
/// Checks learned base rates against realized trades. A template is well calibrated when its 60% calls
/// win about 60% of the time; Brier below <see cref="CalibrationGroup.BaselineBrier"/> (always predicting
/// the group's own realized rate) means the probabilities carried information beyond the base rate.
/// </summary>
public static class Calibration
{
    public static IReadOnlyList<CalibrationGroup> Report(IEnumerable<CalibrationSample> samples, int buckets = 5)
    {
        if (buckets < 1) throw new ArgumentOutOfRangeException(nameof(buckets));
        return samples
            .Where(s => !double.IsNaN(s.Predicted))
            .GroupBy(s => (s.Source, s.Template))
            .OrderBy(g => g.Key.Source, StringComparer.Ordinal).ThenBy(g => g.Key.Template, StringComparer.Ordinal)
            .Select(g => Group(g.Key.Source, g.Key.Template, g.ToList(), buckets))
            .ToList();
    }

    private static CalibrationGroup Group(string source, string template, List<CalibrationSample> xs, int buckets)
    {
        var predicted = xs.Select(x => Math.Clamp(x.Predicted, 0, 1)).ToList();
        var outcomes = xs.Select(x => x.Won).ToList();
        int wins = outcomes.Count(w => w);
        double rate = (double)wins / xs.Count;
        double brier = EvidenceStats.BrierScore(predicted, outcomes);
        double baseline = EvidenceStats.BrierScore(Enumerable.Repeat(rate, xs.Count).ToList(), outcomes);

        var rows = new List<CalibrationBucket>();
        for (int b = 0; b < buckets; b++)
        {
            double lo = (double)b / buckets, hi = (double)(b + 1) / buckets;
            var idx = Enumerable.Range(0, xs.Count)
                .Where(i => predicted[i] >= lo && (predicted[i] < hi || (b == buckets - 1 && predicted[i] <= hi)))
                .ToList();
            if (idx.Count == 0) continue;
            int w = idx.Count(i => outcomes[i]);
            var (ciLo, ciHi) = EvidenceStats.WilsonInterval(w, idx.Count);
            rows.Add(new CalibrationBucket(lo, hi, idx.Count, idx.Average(i => predicted[i]), (double)w / idx.Count, ciLo, ciHi));
        }
        return new CalibrationGroup(source, template, xs.Count, wins, predicted.Average(), rate, brier, baseline, rows);
    }
}
