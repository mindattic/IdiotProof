namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// Features computed across the rows of one decision time's panel rather than from one symbol's own
/// data (explore-plan.md track 2, "sympathy"). Each reads only values already known at t on the same
/// morning, so nothing looks ahead.
/// </summary>
public static class ExploreDerived
{
    public const string SympathyFormed = "x.sympathyFormed20";
    public const string SympathyForming = "x.sympathyForming10";

    /// <summary>Appends feature columns; <paramref name="values"/> returns one value per name for a row (NaN = missing).</summary>
    public static ExploreTable AddColumns(ExploreTable t, IReadOnlyList<string> names, Func<int, IReadOnlyList<double>> values)
    {
        int d = t.FeatureCount, nd = d + names.Count;
        var x = new float[(long)t.Rows * nd];
        for (int r = 0; r < t.Rows; r++)
        {
            Array.Copy(t.X, (long)r * d, x, (long)r * nd, d);
            var v = values(r);
            if (v.Count != names.Count) throw new InvalidOperationException($"Row {r} gave {v.Count} values for {names.Count} columns.");
            for (int k = 0; k < names.Count; k++) x[(long)r * nd + d + k] = (float)v[k];
        }
        return new ExploreTable
        {
            Decision = t.Decision, FeatureNames = t.FeatureNames.Concat(names).ToList(), LabelNames = t.LabelNames, Symbols = t.Symbols, Days = t.Days,
            IsCandidate = t.IsCandidate, Weights = t.Weights, X = x, L = t.L,
        };
    }

    /// <summary>
    /// Adds, per row, how many OTHER symbols the same morning were already up at least 20% (formed) and
    /// at least 10% (forming) at t. A symbol up 10% at t has a daily high at least 10% over its prior
    /// close, so it is a candidate day and always in the panel: the counts are a census of candidate
    /// days, not of the sampled controls. Rows without a gap value count nothing and get NaN.
    /// </summary>
    public static ExploreTable AddSympathy(ExploreTable t)
    {
        if (t.Feature(SympathyFormed) >= 0) return t;
        int gap = t.Feature("GapPct");
        var formed = new Dictionary<DateOnly, int>();
        var forming = new Dictionary<DateOnly, int>();
        for (int r = 0; r < t.Rows; r++)
        {
            if (gap < 0) break;
            float g = t.Value(r, gap);
            if (float.IsNaN(g)) continue;
            if (g >= 20) formed[t.Days[r]] = formed.GetValueOrDefault(t.Days[r]) + 1;
            if (g >= 10) forming[t.Days[r]] = forming.GetValueOrDefault(t.Days[r]) + 1;
        }

        int d = t.FeatureCount, nd = d + 2;
        var names = t.FeatureNames.Concat([SympathyFormed, SympathyForming]).ToList();
        var x = new float[(long)t.Rows * nd];
        for (int r = 0; r < t.Rows; r++)
        {
            Array.Copy(t.X, (long)r * d, x, (long)r * nd, d);
            float g = gap >= 0 ? t.Value(r, gap) : float.NaN;
            if (float.IsNaN(g))
            {
                x[(long)r * nd + d] = float.NaN;
                x[(long)r * nd + d + 1] = float.NaN;
                continue;
            }
            x[(long)r * nd + d] = formed.GetValueOrDefault(t.Days[r]) - (g >= 20 ? 1 : 0);
            x[(long)r * nd + d + 1] = forming.GetValueOrDefault(t.Days[r]) - (g >= 10 ? 1 : 0);
        }
        return new ExploreTable
        {
            Decision = t.Decision, FeatureNames = names, LabelNames = t.LabelNames, Symbols = t.Symbols, Days = t.Days,
            IsCandidate = t.IsCandidate, Weights = t.Weights, X = x, L = t.L,
        };
    }

    public const string OpenWindowPauseLabel = "OpenWindowPauseMinEt";

    /// <summary>
    /// Adds the halt-history features (IP-T457: prior-day pauses, a news halt since the prior close, pauses
    /// today, minutes since the last halt; all from halts stamped at or before t) and one outcome label,
    /// the first LULD/volatility pause in 09:30-09:50 ET. Only same-day decision times get features.
    /// </summary>
    public static ExploreTable AddHalts(ExploreTable t, IdiotProof.Evidence.Gappers.HaltHistory halts, TimeSpan? decisionEt)
    {
        if (t.Feature(IdiotProof.Evidence.Gappers.HaltFeatures.PausesToday) >= 0) return t;
        string[] names = [IdiotProof.Evidence.Gappers.HaltFeatures.PriorDayPauses, IdiotProof.Evidence.Gappers.HaltFeatures.NewsHaltSinceClose,
            IdiotProof.Evidence.Gappers.HaltFeatures.PausesToday, IdiotProof.Evidence.Gappers.HaltFeatures.MinutesSinceLastHalt];
        int d = t.FeatureCount, nd = d + names.Length, m = t.LabelNames.Count, nm = m + 1;
        var x = new float[(long)t.Rows * nd];
        var l = new float[(long)t.Rows * nm];
        var prior = new Dictionary<DateOnly, DateOnly>();
        for (int r = 0; r < t.Rows; r++)
        {
            Array.Copy(t.X, (long)r * d, x, (long)r * nd, d);
            Array.Copy(t.L, (long)r * m, l, (long)r * nm, m);
            var day = t.Days[r];
            l[(long)r * nm + m] = (float)IdiotProof.Evidence.Gappers.HaltFeatures.FirstOpenWindowPauseMinEt(halts, t.Symbols[r], day);
            if (decisionEt is not { } at)
            {
                for (int k = 0; k < names.Length; k++) x[(long)r * nd + d + k] = float.NaN;
                continue;
            }
            if (!prior.TryGetValue(day, out var p)) prior[day] = p = IdiotProof.Core.Evidence.MarketCalendar.PreviousTradingDay(day);
            int j = 0;
            foreach (var (_, v) in IdiotProof.Evidence.Gappers.HaltFeatures.Compute(halts, t.Symbols[r], day, IdiotProof.Evidence.Gappers.MinuteBarMath.ToUtc(day, at), p))
                x[(long)r * nd + d + j++] = (float)v;
        }
        return new ExploreTable
        {
            Decision = t.Decision, FeatureNames = t.FeatureNames.Concat(names).ToList(), LabelNames = t.LabelNames.Append(OpenWindowPauseLabel).ToList(),
            Symbols = t.Symbols, Days = t.Days, IsCandidate = t.IsCandidate, Weights = t.Weights, X = x, L = l,
        };
    }

    /// <summary>Weighted mean with a day-clustered 95% interval (day means, normal approximation); null below 2 days.</summary>
    public static (double Mean, double Lo, double Hi, int Days)? ClusteredMean(IEnumerable<(DateOnly Day, double Value, double Weight)> rows)
    {
        var byDay = rows.Where(r => double.IsFinite(r.Value) && r.Weight > 0).GroupBy(r => r.Day)
            .Select(g => (Sum: g.Sum(r => r.Value * r.Weight), W: g.Sum(r => r.Weight))).ToList();
        double w = byDay.Sum(g => g.W);
        if (byDay.Count < 2 || w <= 0) return null;
        double mean = byDay.Sum(g => g.Sum) / w;
        // Cluster-robust variance of a ratio estimator: residual totals per day.
        int k = byDay.Count;
        double v = byDay.Sum(g => Math.Pow(g.Sum - mean * g.W, 2)) / (w * w) * k / (k - 1);
        double half = 1.96 * Math.Sqrt(v);
        return (mean, mean - half, mean + half, k);
    }
}
