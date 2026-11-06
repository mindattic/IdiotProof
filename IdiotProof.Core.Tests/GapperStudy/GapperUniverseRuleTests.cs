using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Models;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T442: the universe rule reads its numbers from the frozen params.json and applies them as the protocol states.</summary>
[TestFixture]
public class GapperUniverseRuleTests
{
    private static readonly DateOnly D = new(2026, 7, 17);

    [Test]
    public void Params_AreTheFrozenNumbers()
    {
        var p = Params;
        Assert.Multiple(() =>
        {
            Assert.That(p.FrozenUtc, Is.Not.Null);
            Assert.That(p.Universe.PriceMinInclusive, Is.EqualTo(0.10m));
            Assert.That(p.Universe.PriceMaxExclusive, Is.EqualTo(20m));
            Assert.That(p.Universe.Adv20DollarMin, Is.EqualTo(300_000m));
            Assert.That(p.Universe.MinSessionsOfLast20, Is.EqualTo(5));
            Assert.That(p.Gapper.PremarketDollarVolumeFloor, Is.EqualTo(50_000m));
            Assert.That(p.Gapper.OrderedTiers().Select(t => t.Key), Is.EqualTo(new[] { "Tier20", "Tier50", "Tier100" }));
            Assert.That(p.DecisionTimes(), Has.Count.EqualTo(13));
            Assert.That(p.DecisionTimes()[0], Is.EqualTo(new DecisionTime("D-1 16:00", -1, new TimeSpan(16, 0, 0))));
            Assert.That(p.Controls.Salt, Is.EqualTo("gapper-study-2026-11"));
            Assert.That(p.SplitOf(new DateOnly(2022, 1, 3)), Is.EqualTo("train"));
            Assert.That(p.SplitOf(new DateOnly(2025, 1, 2)), Is.EqualTo("validation"));
            Assert.That(p.SplitOf(new DateOnly(2026, 1, 2)), Is.EqualTo("holdout"));
            Assert.That(p.SplitOf(new DateOnly(2026, 11, 3)), Is.Null);
            Assert.That(p.DataTiers.MinuteBarRoot(), Is.EqualTo(@"D:\IdiotProofData\gapper-study\bars"));
        });
    }

    private static Dictionary<string, IReadOnlyList<StudyDailyBar>> History(string symbol, double close, double volume, int sessions = 25, Func<int, double>? closeAt = null, double? highOnD = null)
    {
        var days = MarketCalendar.PriorTradingDays(D, sessions);
        var bars = days.Select((d, i) => Daily(d, closeAt?.Invoke(i) ?? close, volume)).ToList();
        if (highOnD is { } h) bars.Add(Daily(D, close, volume, h));
        return new Dictionary<string, IReadOnlyList<StudyDailyBar>> { [symbol] = bars };
    }

    private static (List<Core.Data.GapperStudyUniverseDay> Rows, UniverseBuildStats Stats) Build(Dictionary<string, IReadOnlyList<StudyDailyBar>> daily, params SplitEvent[] splits) =>
        GapperUniverseBuilder.Build(Params, daily,
            splits.GroupBy(s => s.Symbol).ToDictionary(g => g.Key, g => (IReadOnlyList<SplitEvent>)g.ToList()),
            new Dictionary<string, string> { ["PNY"] = "Penny Corp", ["ADRX"] = "Foreign Co ADR" }, D, D, _ => "train");

    [Test]
    public void Member_InBand_WithEnoughDollarVolume_IsInUniverse_AndCandidateOnA10PercentHigh()
    {
        var (rows, stats) = Build(History("PNY", close: 2.0, volume: 200_000, highOnD: 2.25));
        var row = rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.ExcludedReason, Is.Null);
            Assert.That(row.AdjPrevClose, Is.EqualTo(2.0m));
            Assert.That(row.Adv20Dollars, Is.EqualTo(400_000m));
            Assert.That(row.Adv20Shares, Is.EqualTo(200_000m));
            Assert.That(row.Sessions, Is.EqualTo(20));
            Assert.That(row.PriceTier, Is.EqualTo(1), "[1, 3) is the second frozen tier");
            Assert.That(row.IsCandidate, Is.True, "high 2.25 / 2.00 - 1 = 12.5% >= 10%");
            Assert.That(stats.Candidates, Is.EqualTo(1));
        });
    }

    [Test]
    public void Candidate_ByVolume_WhenDayVolumeIsThreeTimesAdv()
    {
        var daily = History("PNY", close: 2.0, volume: 200_000);
        var list = daily["PNY"].ToList();
        list.Add(new StudyDailyBar(D, 2m, 2.05m, 1.95m, 2m, 600_000m));
        daily["PNY"] = list;
        Assert.That(Build(daily).Rows.Single().IsCandidate, Is.True);
    }

    [Test]
    public void TooFewSessions_OrLowDollarVolume_OrOutOfBand_AreNotInUniverse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Build(History("PNY", 2.0, 200_000, sessions: 4)).Rows, Is.Empty, "fewer than 5 of the last 20 sessions");
            Assert.That(Build(History("PNY", 2.0, 100_000)).Rows, Is.Empty, "ADV20$ 200k < 300k");
            Assert.That(Build(History("PNY", 25.0, 200_000)).Rows, Is.Empty, "prior close 25 >= 20");
            Assert.That(Build(History("PNY", 0.05, 20_000_000)).Rows, Is.Empty, "prior close 0.05 < 0.10");
        });
    }

    [Test]
    public void SplitInsideTheWindow_ExcludesTheDay()
    {
        var before = Params.Universe.SplitExclusionWindowDays.Before;
        var after = Params.Universe.SplitExclusionWindowDays.After;
        var inside = Build(History("PNY", 2.0, 200_000), new SplitEvent("PNY", D.AddDays(-before), 1, 10)).Rows.Single();
        var atAfter = Build(History("PNY", 2.0, 200_000), new SplitEvent("PNY", D.AddDays(after), 10, 1)).Rows.Single();
        var outside = Build(History("PNY", 2.0, 200_000), new SplitEvent("PNY", D.AddDays(-before - 1), 1, 10)).Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(inside.ExcludedReason, Is.EqualTo(GapperUniverseBuilder.ExcludedSplitWindow));
            Assert.That(atAfter.ExcludedReason, Is.EqualTo(GapperUniverseBuilder.ExcludedSplitWindow));
            Assert.That(outside.ExcludedReason, Is.Null, "a split older than D-25 is outside the window");
        });
    }

    [Test]
    public void OlderSplit_AdjustsThePriorCloseAndAdvShares()
    {
        var splitDate = D.AddDays(-Params.Universe.SplitExclusionWindowDays.Before - 3);
        // Bars before the split trade at 0.30 with 2M shares; after a 1:10 reverse split they trade at 3.00 with 200k.
        var days = MarketCalendar.PriorTradingDays(D, 20);
        var bars = days.Select(d => d < splitDate ? Daily(d, 0.30, 2_000_000) : Daily(d, 3.00, 200_000)).ToList();
        var daily = new Dictionary<string, IReadOnlyList<StudyDailyBar>> { ["PNY"] = bars };
        var row = Build(daily, new SplitEvent("PNY", splitDate, 10, 1)).Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.ExcludedReason, Is.Null);
            Assert.That(row.AdjPrevClose, Is.EqualTo(3.00m));
            Assert.That(row.Adv20Shares, Is.EqualTo(200_000m).Within(0.01m), "pre-split volume is divided by the price factor");
            Assert.That(row.Adv20Dollars, Is.EqualTo(600_000m).Within(0.01m), "dollar volume is split-invariant");
        });
    }

    [Test]
    public void UnexplainedTripleMove_WithoutASplit_IsExcluded()
    {
        var moved = Build(History("PNY", 2.0, 200_000, closeAt: i => i == 20 ? 6.5 : 2.0)).Rows.Single();
        Assert.That(moved.ExcludedReason, Is.EqualTo(GapperUniverseBuilder.ExcludedUnexplainedMove), "2.0 -> 6.5 is +225% > +200%");
        var dropped = Build(History("PNY", 2.0, 200_000, closeAt: i => i == 20 ? 0.6 : 2.0)).Rows.Single();
        Assert.That(dropped.ExcludedReason, Is.EqualTo(GapperUniverseBuilder.ExcludedUnexplainedMove), "2.0 -> 0.6 is -70% < -66.7%");
        var fine = Build(History("PNY", 2.0, 200_000, closeAt: i => i == 20 ? 5.5 : 2.0)).Rows.Single();
        Assert.That(fine.ExcludedReason, Is.Null, "+175% stays inside the guard");
    }

    [Test]
    public void Deciles_AreRanksWithinTheDay_LowestDollarVolumeFirst()
    {
        var daily = new Dictionary<string, IReadOnlyList<StudyDailyBar>>();
        for (int k = 0; k < 20; k++)
            daily[$"S{k:00}"] = History($"S{k:00}", 2.0, 200_000 + 50_000 * k)[$"S{k:00}"];
        var rows = Build(daily).Rows.OrderBy(r => r.Adv20Dollars).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.AdvDecile), Is.EqualTo(Enumerable.Range(0, 20).Select(i => i / 2)));
            Assert.That(rows.All(r => r.PriceTier == 1), Is.True);
        });
    }

    [Test]
    public void AdrFlag_FromAssetName()
    {
        var daily = History("ADRX", 2.0, 200_000);
        Assert.That(Build(daily).Rows.Single().IsAdr, Is.True);
        Assert.That(Build(History("PNY", 2.0, 200_000)).Rows.Single().IsAdr, Is.False);
        Assert.That(GapperUniverseBuilder.IsAdr("Adrenaline Corp", Params.Universe.AdrNamePatterns), Is.False, "whole-word match only");
    }
}
