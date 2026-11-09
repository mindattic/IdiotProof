namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// One morning-day's weighted alarm tallies for two rules (index 0 = the rule under test, 1 = the
/// benchmark): scorable and forming weight, every row's weight, and per rule alarms (any row), hits
/// and false alarms.
/// </summary>
public sealed record AlarmDayTotals(DateOnly Day, double Scorable, double Forming, double Population, double[] Alarms, double[] Hits, double[] False);

/// <summary>A hit's lead to the first +20% touch, with its row weight and day.</summary>
public readonly record struct AlarmLead(DateOnly Day, double Minutes, double Weight);

/// <summary>A point estimate with its percentile bootstrap interval and two-sided p against zero.</summary>
public sealed record AlarmInterval(double Estimate, double Low, double High, double PValueTwoSided, int Days, int Draws);

/// <summary>
/// Amendment 04's statistics (IP-T475), pure: weighted hit rate, false alarms per 100 scorable mornings
/// and alarms per day from day totals, the rule-minus-benchmark differences with a whole-day bootstrap
/// (weights kept), and the weighted median lead with a day-clustered bootstrap. Seeded and reproducible;
/// the interval and p follow <see cref="Evaluation.DayClusteredBootstrap"/>'s percentile convention.
/// </summary>
public static class AlarmComparison
{
    public static double HitRate(IEnumerable<AlarmDayTotals> days, int rule)
    {
        double hits = 0, forming = 0;
        foreach (var d in days) { hits += d.Hits[rule]; forming += d.Forming; }
        return forming > 0 ? hits / forming : double.NaN;
    }

    public static double FaPer100(IEnumerable<AlarmDayTotals> days, int rule, bool allRows = false)
    {
        double fa = 0, denom = 0;
        foreach (var d in days) { fa += d.False[rule]; denom += allRows ? d.Population : d.Scorable; }
        return denom > 0 ? 100 * fa / denom : double.NaN;
    }

    public static double AlarmsPerDay(IReadOnlyCollection<AlarmDayTotals> days, int rule) => days.Count == 0 ? double.NaN : days.Sum(d => d.Alarms[rule]) / days.Count;

    /// <summary>Hit-rate difference (rule 0 - rule 1) and FA/100 difference, each with a whole-day bootstrap interval.</summary>
    public static (AlarmInterval Hit, AlarmInterval Fa) Differences(IReadOnlyList<AlarmDayTotals> days, int draws, int seed, double ciLevel, int minDays)
    {
        static (double Hit, double Fa) Stat(double[] s) =>
            (s[1] > 0 ? (s[2] - s[3]) / s[1] : double.NaN, s[0] > 0 ? 100 * (s[4] - s[5]) / s[0] : double.NaN);
        var vectors = days.Select(d => new[] { d.Scorable, d.Forming, d.Hits[0], d.Hits[1], d.False[0], d.False[1] }).ToArray();
        var total = new double[6];
        foreach (var v in vectors) for (int k = 0; k < 6; k++) total[k] += v[k];
        var est = Stat(total);
        if (vectors.Length < minDays || draws < 1 || double.IsNaN(est.Hit))
            return (new AlarmInterval(est.Hit, double.NaN, double.NaN, 1, vectors.Length, 0), new AlarmInterval(est.Fa, double.NaN, double.NaN, 1, vectors.Length, 0));
        var rng = new Random(seed);
        var hits = new List<double>(draws);
        var fas = new List<double>(draws);
        var sum = new double[6];
        for (int b = 0; b < draws; b++)
        {
            Array.Clear(sum);
            for (int i = 0; i < vectors.Length; i++)
            {
                var v = vectors[rng.Next(vectors.Length)];
                for (int k = 0; k < 6; k++) sum[k] += v[k];
            }
            var s = Stat(sum);
            if (!double.IsNaN(s.Hit)) hits.Add(s.Hit);
            if (!double.IsNaN(s.Fa)) fas.Add(s.Fa);
        }
        return (Interval(est.Hit, hits, ciLevel, vectors.Length), Interval(est.Fa, fas, ciLevel, vectors.Length));
    }

    /// <summary>Weighted median of the leads (<see cref="CrossSectionalRank.WeightedMedian"/>), with a bootstrap over whole days.</summary>
    public static AlarmInterval MedianLead(IReadOnlyList<AlarmLead> leads, int draws, int seed, double ciLevel, int minDays)
    {
        static double Median(IEnumerable<AlarmLead> l)
        {
            var list = l.ToList();
            return CrossSectionalRank.WeightedMedian(list.Select(x => x.Minutes).ToList(), list.Select(x => x.Weight).ToList()) ?? double.NaN;
        }
        var byDay = leads.GroupBy(l => l.Day).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
        double est = leads.Count == 0 ? double.NaN : Median(leads);
        if (byDay.Length < minDays || draws < 1 || double.IsNaN(est)) return new AlarmInterval(est, double.NaN, double.NaN, 1, byDay.Length, 0);
        var rng = new Random(seed);
        var boot = new List<double>(draws);
        var buffer = new List<AlarmLead>(leads.Count * 2);
        for (int b = 0; b < draws; b++)
        {
            buffer.Clear();
            for (int i = 0; i < byDay.Length; i++) buffer.AddRange(byDay[rng.Next(byDay.Length)]);
            var m = Median(buffer);
            if (!double.IsNaN(m)) boot.Add(m);
        }
        return Interval(est, boot, ciLevel, byDay.Length);
    }

    private static AlarmInterval Interval(double estimate, List<double> boot, double ciLevel, int days)
    {
        if (boot.Count == 0) return new AlarmInterval(estimate, double.NaN, double.NaN, 1, days, 0);
        boot.Sort();
        double tail = (1 - ciLevel) / 2;
        double low = boot[(int)Math.Floor(tail * (boot.Count - 1))];
        double high = boot[(int)Math.Ceiling((1 - tail) * (boot.Count - 1))];
        int below = boot.Count(v => v <= 0), above = boot.Count(v => v >= 0);
        double p = Math.Min(1, 2.0 * (1 + Math.Min(below, above)) / (boot.Count + 1));
        return new AlarmInterval(estimate, low, high, p, days, boot.Count);
    }
}
