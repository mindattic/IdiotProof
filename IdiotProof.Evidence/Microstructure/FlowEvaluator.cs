namespace IdiotProof.Evidence.Microstructure;

/// <summary>One metric bucket's measured up-rate in the training and the held-out part.</summary>
public sealed record FlowBucket(int Bin, OutcomeRate Train, OutcomeRate Test);

/// <summary>How a flow metric's buckets lined up with what price did next.</summary>
public sealed record FlowEvaluation(string Metric, int Bins, double TrainBaseRate, double TestBaseRate,
    int TrainSamples, int TestSamples, IReadOnlyList<FlowBucket> Buckets);

/// <summary>
/// Tests whether a flow metric says anything about the next move. Bucket edges are learned only from
/// bars before <c>trainEnd</c>; the training table counts only labels that resolve before <c>trainEnd</c>;
/// the test table counts bars from <c>trainEnd</c> on. "Success" is a positive forward return.
/// </summary>
public static class FlowEvaluator
{
    public static FlowEvaluation Evaluate(string metric, IReadOnlyList<double> values, IReadOnlyList<ForwardLabel> labels,
        int horizon, int trainEnd, int bins = 5)
    {
        var trainValues = new List<double>();
        for (int i = 0; i < Math.Min(trainEnd, values.Count); i++)
            if (!double.IsNaN(values[i])) trainValues.Add(values[i]);
        var binner = new QuantileBinner(trainValues, bins);
        return Tabulate(metric, i => i < values.Count && !double.IsNaN(values[i]) ? binner.Bin(values[i]) : -1,
            binner.Bins, labels, horizon, trainEnd);
    }

    /// <summary>For a metric that is already a category (0..<paramref name="categories"/>-1), e.g. absorption -1/0/+1 shifted by one.</summary>
    public static FlowEvaluation EvaluateCategories(string metric, IReadOnlyList<int> values, IReadOnlyList<ForwardLabel> labels,
        int horizon, int trainEnd, int categories) =>
        Tabulate(metric, i => i < values.Count && values[i] >= 0 && values[i] < categories ? values[i] : -1, categories, labels, horizon, trainEnd);

    private static FlowEvaluation Tabulate(string metric, Func<int, int> binOf, int bins, IReadOnlyList<ForwardLabel> labels, int horizon, int trainEnd)
    {
        var train = new ConditionalOutcomeTable<int>();
        var test = new ConditionalOutcomeTable<int>();
        foreach (var l in labels)
        {
            int bin = binOf(l.Index);
            if (bin < 0) continue;
            bool up = l.ForwardReturn > 0;
            if (l.Index + horizon < trainEnd) train.Add(bin, up);
            else if (l.Index >= trainEnd) test.Add(bin, up);
        }

        var buckets = new List<FlowBucket>(bins);
        for (int b = 0; b < bins; b++) buckets.Add(new FlowBucket(b, train.Rate(b), test.Rate(b)));
        return new FlowEvaluation(metric, bins, train.BaseRate, test.BaseRate, train.TotalSamples, test.TotalSamples, buckets);
    }
}
