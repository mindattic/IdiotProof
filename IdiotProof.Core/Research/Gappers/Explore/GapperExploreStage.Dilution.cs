using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    private DilutionHistory? dilution;
    private bool dilutionTried;

    /// <summary>
    /// Adds the dilution-filings block (IP-T458, avenues.md A2) when the widened EDGAR files exist
    /// (<c>pull --tier edgar-dilution</c>, then <c>edgar-dilution-text</c>) and the study CIK map is on
    /// disk (<c>explore float-import</c>). Features read only filings known before the decision: accepted
    /// strictly before t (ET) when the header was fetched, else filed strictly before the decision day.
    /// </summary>
    private ExploreTable AddDilution(ExploreTable t, string decision)
    {
        if (t.Feature(DilutionFeatures.AtmActive) >= 0) return t;
        if (!dilutionTried)
        {
            dilutionTried = true;
            dilution = DilutionHistory.Load(GapperPullStage.DilutionDir(s.Params));
        }
        var map = LoadCikMap();
        if (dilution is null || map is null) return t;
        var dt = DecisionTime.Parse(decision);
        return ExploreDerived.AddColumns(t, DilutionFeatures.All, r =>
        {
            if (!map.TryGetValue(t.Symbols[r], out var cik) || !dilution.HasCik(cik)) return Enumerable.Repeat(double.NaN, DilutionFeatures.All.Length).ToArray();
            var (day, cutoffEt) = DilutionCutoff(t.Days[r], dt);
            return dilution.Features(cik, day, cutoffEt);
        });
    }

    /// <summary>The decision's own trading day and its ET instant: D at t, or for "D-n" the n-th prior trading day at t.</summary>
    internal static (DateOnly Day, DateTime CutoffEt) DilutionCutoff(DateOnly d, DecisionTime dt)
    {
        var day = d;
        for (int i = 0; i < -dt.DayOffset; i++) day = MarketCalendar.PreviousTradingDay(day);
        return (day, day.ToDateTime(TimeOnly.FromTimeSpan(dt.TimeEt)));
    }
}
