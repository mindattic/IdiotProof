namespace IdiotProof.Evidence.Evaluation;

/// <summary>One row of a sampled population: its day, whether it is in the tested group, whether the outcome happened, and its sampling weight.</summary>
public readonly record struct WeightedOutcome(DateOnly Day, bool InGroup, bool Up, double Weight);

/// <summary>One weighted value on a day (a trade's net return and the weight of the row it came from).</summary>
public readonly record struct WeightedValue(DateOnly Day, double Value, double Weight);

/// <summary>A weighted group rate against the weighted base rate, with a day-clustered interval for the lift and a two-sided p for rate minus base rate.</summary>
public sealed record WeightedLift(
    double Rate, double BaseRate, double Lift, double LiftLow, double LiftHigh, double PValueTwoSided,
    int GroupRows, double GroupWeight, int Rows, double TotalWeight, int Days, int Draws);

/// <summary>
/// Estimates over a stratified sample in which some rows stand for many (a census stratum at weight 1
/// and a uniform sample at weight 1/rate). Every bootstrap resamples whole days from per-day weighted
/// sums, so rows that share a market day move together and a weight never counts as independent
/// trials. Seeded, so a run is reproducible; with every weight 1 the point estimates equal the
/// unweighted ones.
/// </summary>
public static class WeightedPopulation
{
    /// <summary>Each value repeated by its integer weight, for estimators that are exact under replication (quantile edges, return moments).</summary>
    public static List<double> Replicate(IEnumerable<(double Value, double Weight)> rows)
    {
        var result = new List<double>();
        foreach (var (value, weight) in rows)
        {
            int copies = IntegerWeight(weight);
            for (int i = 0; i < copies; i++) result.Add(value);
        }
        return result;
    }

    public static int IntegerWeight(double weight)
    {
        var rounded = Math.Round(weight);
        if (rounded < 1 || Math.Abs(weight - rounded) > 1e-9) throw new ArgumentException($"Weight {weight} is not a positive integer.", nameof(weight));
        return (int)rounded;
    }

    public static WeightedLift Lift(IReadOnlyList<WeightedOutcome> rows, int draws = 10000, int seed = 1337, double ciLevel = 0.95, int minDays = 5)
    {
        var days = rows.GroupBy(r => r.Day).OrderBy(g => g.Key).Select(g =>
        {
            double gw = 0, gu = 0, aw = 0, au = 0;
            foreach (var r in g)
            {
                aw += r.Weight;
                if (r.Up) au += r.Weight;
                if (!r.InGroup) continue;
                gw += r.Weight;
                if (r.Up) gu += r.Weight;
            }
            return (gw, gu, aw, au);
        }).ToArray();

        double sgw = days.Sum(d => d.gw), sgu = days.Sum(d => d.gu), saw = days.Sum(d => d.aw), sau = days.Sum(d => d.au);
        double rate = sgw > 0 ? sgu / sgw : double.NaN;
        double baseRate = saw > 0 ? sau / saw : double.NaN;
        double lift = baseRate > 0 ? rate / baseRate : double.NaN;
        int groupRows = rows.Count(r => r.InGroup);
        if (days.Length < minDays || draws < 1 || double.IsNaN(lift))
            return new WeightedLift(rate, baseRate, lift, double.NaN, double.NaN, 1, groupRows, sgw, rows.Count, saw, days.Length, 0);

        var rng = new Random(seed);
        var lifts = new List<double>(draws);
        var diffs = new List<double>(draws);
        for (int b = 0; b < draws; b++)
        {
            double gw = 0, gu = 0, aw = 0, au = 0;
            for (int i = 0; i < days.Length; i++)
            {
                var d = days[rng.Next(days.Length)];
                gw += d.gw; gu += d.gu; aw += d.aw; au += d.au;
            }
            if (gw <= 0 || aw <= 0) continue;
            double r = gu / gw, br = au / aw;
            diffs.Add(r - br);
            if (br > 0) lifts.Add(r / br);
        }
        var (low, high) = Percentiles(lifts, ciLevel);
        return new WeightedLift(rate, baseRate, lift, low, high, TwoSidedP(diffs), groupRows, sgw, rows.Count, saw, days.Length, diffs.Count);
    }

    /// <summary>Weighted mean with a day-clustered interval and a two-sided p against zero.</summary>
    public static BootstrapInterval Mean(IReadOnlyList<WeightedValue> rows, int draws = 10000, int seed = 1337, double ciLevel = 0.95, int minDays = 5)
    {
        var days = rows.GroupBy(r => r.Day).OrderBy(g => g.Key)
            .Select(g => (Sum: g.Sum(r => r.Weight * r.Value), Weight: g.Sum(r => r.Weight))).ToArray();
        double totalWeight = days.Sum(d => d.Weight);
        double estimate = totalWeight > 0 ? days.Sum(d => d.Sum) / totalWeight : double.NaN;
        if (days.Length < minDays || draws < 1 || double.IsNaN(estimate))
            return new BootstrapInterval(estimate, double.NaN, double.NaN, 1, days.Length, rows.Count, 0);

        var rng = new Random(seed);
        var boot = new List<double>(draws);
        for (int b = 0; b < draws; b++)
        {
            double s = 0, w = 0;
            for (int i = 0; i < days.Length; i++)
            {
                var d = days[rng.Next(days.Length)];
                s += d.Sum; w += d.Weight;
            }
            if (w > 0) boot.Add(s / w);
        }
        var (low, high) = Percentiles(boot, ciLevel);
        return new BootstrapInterval(estimate, low, high, TwoSidedP(boot), days.Length, rows.Count, boot.Count);
    }

    /// <summary>Percentile interval, the same convention as <see cref="DayClusteredBootstrap"/>.</summary>
    public static (double Low, double High) Percentiles(List<double> values, double ciLevel)
    {
        if (values.Count == 0) return (double.NaN, double.NaN);
        values.Sort();
        double tail = (1 - ciLevel) / 2;
        return (values[(int)Math.Floor(tail * (values.Count - 1))], values[(int)Math.Ceiling((1 - tail) * (values.Count - 1))]);
    }

    /// <summary>Two-sided bootstrap p against zero, the same rule as <see cref="DayClusteredBootstrap"/>.</summary>
    public static double TwoSidedP(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) return 1;
        int atOrBelow = values.Count(v => v <= 0), atOrAbove = values.Count(v => v >= 0);
        return Math.Min(1, 2.0 * (1 + Math.Min(atOrBelow, atOrAbove)) / (values.Count + 1));
    }
}

/// <summary>
/// Weighted area under the ROC curve: the weighted Mann-Whitney statistic, ties counting half, which
/// equals <see cref="Auc.Compute"/> on rows replicated by integer weight. The day-block comparison
/// sorts each scorer once and folds every bootstrap draw in as day multiplicities, so a draw costs one
/// pass over the rows.
/// </summary>
public static class WeightedAuc
{
    public static double Compute(IReadOnlyList<double> scores, IReadOnlyList<bool> labels, IReadOnlyList<double> weights)
    {
        if (scores.Count != labels.Count || scores.Count != weights.Count) throw new ArgumentException("scores, labels and weights differ in length.");
        var order = SortedOrder(scores);
        return FromOrder(order, scores, labels, i => weights[i]);
    }

    public static AucComparison CompareByDay(IReadOnlyList<double> scoresA, IReadOnlyList<double> scoresB, IReadOnlyList<bool> labels,
        IReadOnlyList<double> weights, IReadOnlyList<DateOnly> days, int draws = 10000, int seed = 1337, double ciLevel = 0.95, int minDays = 5)
    {
        int n = labels.Count;
        if (scoresA.Count != n || scoresB.Count != n || weights.Count != n || days.Count != n) throw new ArgumentException("All inputs must have the same length.");
        var orderA = SortedOrder(scoresA);
        var orderB = SortedOrder(scoresB);
        double aucA = FromOrder(orderA, scoresA, labels, i => weights[i]);
        double aucB = FromOrder(orderB, scoresB, labels, i => weights[i]);
        double diff = aucA - aucB;

        var dayKeys = days.Distinct().OrderBy(d => d).ToArray();
        var dayIndex = dayKeys.Select((d, k) => (d, k)).ToDictionary(x => x.d, x => x.k);
        var rowDay = new int[n];
        for (int i = 0; i < n; i++) rowDay[i] = dayIndex[days[i]];
        int positives = labels.Count(l => l);
        if (dayKeys.Length < minDays || double.IsNaN(diff) || draws < 1)
            return new AucComparison(aucA, aucB, diff, double.NaN, double.NaN, 1, dayKeys.Length, n, positives, 0);

        var rng = new Random(seed);
        var multiplicity = new int[dayKeys.Length];
        var boot = new List<double>(draws);
        for (int b = 0; b < draws; b++)
        {
            Array.Clear(multiplicity);
            for (int k = 0; k < dayKeys.Length; k++) multiplicity[rng.Next(dayKeys.Length)]++;
            double a = FromOrder(orderA, scoresA, labels, i => weights[i] * multiplicity[rowDay[i]]);
            double bb = FromOrder(orderB, scoresB, labels, i => weights[i] * multiplicity[rowDay[i]]);
            double d = a - bb;
            if (!double.IsNaN(d)) boot.Add(d);
        }
        if (boot.Count == 0) return new AucComparison(aucA, aucB, diff, double.NaN, double.NaN, 1, dayKeys.Length, n, positives, 0);
        var (low, high) = WeightedPopulation.Percentiles(boot, ciLevel);
        return new AucComparison(aucA, aucB, diff, low, high, WeightedPopulation.TwoSidedP(boot), dayKeys.Length, n, positives, boot.Count);
    }

    private static int[] SortedOrder(IReadOnlyList<double> scores)
    {
        var order = Enumerable.Range(0, scores.Count).ToArray();
        Array.Sort(order, (x, y) => scores[x].CompareTo(scores[y]));
        return order;
    }

    private static double FromOrder(int[] order, IReadOnlyList<double> scores, IReadOnlyList<bool> labels, Func<int, double> weight)
    {
        double negativesBelow = 0, totalPositive = 0, sum = 0;
        int pos = 0, n = order.Length;
        while (pos < n)
        {
            int end = pos;
            while (end + 1 < n && scores[order[end + 1]] == scores[order[pos]]) end++;
            double groupPositive = 0, groupNegative = 0;
            for (int k = pos; k <= end; k++)
            {
                int i = order[k];
                double w = weight(i);
                if (w == 0) continue;
                if (labels[i]) groupPositive += w; else groupNegative += w;
            }
            sum += groupPositive * (negativesBelow + 0.5 * groupNegative);
            negativesBelow += groupNegative;
            totalPositive += groupPositive;
            pos = end + 1;
        }
        double totalNegative = negativesBelow;
        if (totalPositive <= 0 || totalNegative <= 0) return double.NaN;
        return sum / (totalPositive * totalNegative);
    }
}
