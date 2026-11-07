using IdiotProof.Evidence.Search;

namespace IdiotProof.Evidence.EventStudy;

/// <summary>One event on one day with its outcome and whether it belongs to the treated group.</summary>
public readonly record struct GroupEvent(DateOnly Day, double Value, bool Treated);

public sealed record GroupStudyResult(
    int Treated, int Control, int Days, int DaysWithBothGroups,
    double MeanTreated, double MeanControl, double MeanDifference,
    double BmpT, double BmpP, double PlaceboP, int PlaceboDraws);

/// <summary>
/// Event study for intraday outcomes measured on the same calendar day across a cross-section of
/// events (the gapper study's catalyst groups): each event's abnormal outcome is its value minus the
/// same-day cross-sectional mean, standardized by that day's cross-sectional dispersion, and the
/// treated-minus-control difference is tested with the Boehmer-Musumeci-Poulsen statistic (Welch
/// t over the standardized abnormal outcomes, robust to event-induced variance) and a seeded placebo
/// that reassigns the group labels within the same day, so the day's common move can never create a
/// group difference by itself.
/// </summary>
public static class SameDayGroupStudy
{
    public static GroupStudyResult Run(IReadOnlyList<GroupEvent> events, int placeboDraws = 1000, int seed = 1337)
    {
        var byDay = events.GroupBy(e => e.Day).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
        var treated = events.Where(e => e.Treated).Select(e => e.Value).ToList();
        var control = events.Where(e => !e.Treated).Select(e => e.Value).ToList();
        int daysWithBoth = byDay.Count(d => d.Any(e => e.Treated) && d.Any(e => !e.Treated));
        double meanT = treated.Count == 0 ? double.NaN : treated.Average();
        double meanC = control.Count == 0 ? double.NaN : control.Average();
        double diff = meanT - meanC;

        var sarT = new List<double>();
        var sarC = new List<double>();
        foreach (var day in byDay)
        {
            if (day.Length < 2) continue;
            double mean = day.Average(e => e.Value);
            double sd = EvidenceStats.StdDev(day.Select(e => e.Value).ToList());
            if (sd <= 0) continue;
            foreach (var e in day) (e.Treated ? sarT : sarC).Add((e.Value - mean) / sd);
        }
        var (bmpT, bmpP) = WelchT(sarT, sarC);

        double placeboP = double.NaN;
        if (placeboDraws > 0 && treated.Count > 0 && control.Count > 0)
        {
            var rng = new Random(seed);
            int extreme = 0;
            var flags = byDay.Select(d => d.Select(e => e.Treated).ToArray()).ToArray();
            for (int b = 0; b < placeboDraws; b++)
            {
                double sT = 0, sC = 0;
                int nT = 0, nC = 0;
                for (int k = 0; k < byDay.Length; k++)
                {
                    var f = flags[k];
                    for (int i = f.Length - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        (f[i], f[j]) = (f[j], f[i]);
                    }
                    for (int i = 0; i < f.Length; i++)
                    {
                        if (f[i]) { sT += byDay[k][i].Value; nT++; }
                        else { sC += byDay[k][i].Value; nC++; }
                    }
                }
                if (nT == 0 || nC == 0) continue;
                if (Math.Abs(sT / nT - sC / nC) >= Math.Abs(diff)) extreme++;
            }
            placeboP = (1.0 + extreme) / (placeboDraws + 1.0);
        }
        return new GroupStudyResult(treated.Count, control.Count, byDay.Length, daysWithBoth, meanT, meanC, diff, bmpT, bmpP, placeboP, placeboDraws);
    }

    private static (double T, double P) WelchT(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count < 2 || b.Count < 2) return (double.NaN, double.NaN);
        double ma = EvidenceStats.Mean(a), mb = EvidenceStats.Mean(b);
        double va = Math.Pow(EvidenceStats.StdDev(a), 2) / a.Count, vb = Math.Pow(EvidenceStats.StdDev(b), 2) / b.Count;
        if (va + vb <= 0) return (double.NaN, double.NaN);
        double t = (ma - mb) / Math.Sqrt(va + vb);
        double df = Math.Pow(va + vb, 2) / (va * va / (a.Count - 1) + vb * vb / (b.Count - 1));
        return (t, 2 * Distributions.StudentTUpperTail(Math.Abs(t), df));
    }
}
