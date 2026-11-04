using IdiotProof.Evidence.Evaluation;

namespace IdiotProof.Evidence.Microstructure;

/// <summary>
/// Turns a per-bar flow metric and forward labels into train/holdout samples for
/// <see cref="HoldoutBucketEvaluator"/>. Training holds only labels that resolve before <c>trainEnd</c>;
/// the holdout holds bars from <c>trainEnd</c> on. "Up" is a positive forward return.
/// </summary>
public static class FlowEvaluator
{
    public static FeatureSamples Samples(string metric, IReadOnlyList<double> values, IReadOnlyList<ForwardLabel> labels,
        int horizon, int trainEnd, Func<int, DateOnly> dayOf) =>
        Split(metric, i => i < values.Count ? values[i] : double.NaN, labels, horizon, trainEnd, dayOf, null);

    /// <summary>For a metric that is already a category (0..<paramref name="categories"/>-1), e.g. absorption -1/0/+1 shifted by one.</summary>
    public static FeatureSamples CategorySamples(string metric, IReadOnlyList<int> values, IReadOnlyList<ForwardLabel> labels,
        int horizon, int trainEnd, Func<int, DateOnly> dayOf, int categories) =>
        Split(metric, i => i < values.Count && values[i] >= 0 && values[i] < categories ? values[i] : double.NaN,
            labels, horizon, trainEnd, dayOf, categories);

    private static FeatureSamples Split(string metric, Func<int, double> valueOf, IReadOnlyList<ForwardLabel> labels,
        int horizon, int trainEnd, Func<int, DateOnly> dayOf, int? categories)
    {
        var train = new List<BucketObservation>();
        var holdout = new List<BucketObservation>();
        foreach (var l in labels)
        {
            double v = valueOf(l.Index);
            if (double.IsNaN(v)) continue;
            var o = new BucketObservation(dayOf(l.Index), v, l.ForwardReturn > 0);
            if (l.Index + horizon < trainEnd) train.Add(o);
            else if (l.Index >= trainEnd) holdout.Add(o);
        }
        return new FeatureSamples(metric, train, holdout, categories);
    }
}
