namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Market-day normalisation and within-morning ranks (methods-onset.md P2, strategy-refined.md §2.2b,
/// IP-T470): a value divided by its weighted median across that morning's universe, and its weighted
/// percentile rank there. Live the universe is exact (weights 1); in the explore panel it is the
/// candidates plus the weighted population sample, so both are weighted estimates. NaN in, NaN out.
/// </summary>
public static class CrossSectionalRank
{
    /// <summary>Weighted median of the finite values with positive weight; null when none.</summary>
    public static double? WeightedMedian(IReadOnlyList<double> values, IReadOnlyList<double> weights)
    {
        var items = Finite(values, weights).OrderBy(x => x.Value).ToList();
        double total = items.Sum(x => x.Weight);
        if (total <= 0) return null;
        double cum = 0;
        for (int i = 0; i < items.Count; i++)
        {
            cum += items[i].Weight;
            if (cum > total / 2) return items[i].Value;
            if (cum == total / 2) return i + 1 < items.Count ? (items[i].Value + items[i + 1].Value) / 2 : items[i].Value;
        }
        return items[^1].Value;
    }

    /// <summary>Each value over the weighted median; NaN when the value is missing or the median is not positive.</summary>
    public static double[] MarketNormalised(IReadOnlyList<double> values, IReadOnlyList<double> weights)
    {
        var median = WeightedMedian(values, weights);
        var result = new double[values.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = median is > 0 && double.IsFinite(values[i]) ? values[i] / median.Value : double.NaN;
        return result;
    }

    /// <summary>
    /// Weighted mid-rank percentile in [0, 1]: (weight strictly below + half the weight tied, own
    /// included) / total weight. 1 is the top of the morning.
    /// </summary>
    public static double[] PercentileRanks(IReadOnlyList<double> values, IReadOnlyList<double> weights)
    {
        var result = new double[values.Count];
        Array.Fill(result, double.NaN);
        var items = Finite(values, weights).OrderBy(x => x.Value).ToList();
        double total = items.Sum(x => x.Weight);
        if (total <= 0) return result;
        double below = 0;
        for (int i = 0; i < items.Count;)
        {
            int j = i;
            double tied = 0;
            while (j < items.Count && items[j].Value == items[i].Value) { tied += items[j].Weight; j++; }
            double p = (below + tied / 2) / total;
            for (int k = i; k < j; k++) result[items[k].Index] = p;
            below += tied;
            i = j;
        }
        return result;
    }

    private static IEnumerable<(int Index, double Value, double Weight)> Finite(IReadOnlyList<double> values, IReadOnlyList<double> weights)
    {
        if (values.Count != weights.Count) throw new ArgumentException("Values and weights must have the same length.");
        for (int i = 0; i < values.Count; i++)
            if (double.IsFinite(values[i]) && weights[i] > 0) yield return (i, values[i], weights[i]);
    }
}
