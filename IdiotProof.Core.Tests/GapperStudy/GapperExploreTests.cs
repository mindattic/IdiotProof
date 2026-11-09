using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Core.Research.Gappers.Explore;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T453 exploratory round: the train-only guard, the rolling folds and their embargo, the trial
/// ledger, the panel file round trip, and an end-to-end build and score on planted data where
/// premarket volume from 06:00 announces the mornings that run.
/// </summary>
[TestFixture]
public class GapperExploreTests
{
    [Test]
    public void Folds_MatchThePlan_AndEmbargoFiveTradingDays()
    {
        var days = new List<DateOnly>();
        for (var d = new DateOnly(2021, 11, 1); d <= new DateOnly(2024, 10, 31); d = d.AddDays(1))
            if (MarketCalendar.IsTradingDay(d)) days.Add(d);
        var folds = ExploreFolds.Build(days);
        Assert.Multiple(() =>
        {
            Assert.That(folds, Has.Count.EqualTo(4));
            Assert.That(folds.Select(f => (f.EvalFrom.Year, f.EvalFrom.Month)), Is.EqualTo(new[] { (2022, 11), (2023, 5), (2023, 11), (2024, 5) }));
            Assert.That(folds[^1].EvalTo, Is.EqualTo(days[^1]));
            foreach (var f in folds)
            {
                Assert.That(f.FitFrom, Is.EqualTo(days[0]), "each fold fits on everything before its window");
                Assert.That(days.Count(d => d > f.FitTo && d < f.EvalFrom), Is.EqualTo(ExploreFolds.EmbargoTradingDays), "five trading days embargoed");
            }
        });
    }

    [Test]
    public void Ledger_NumbersTrialsSequentiallyAcrossRuns()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ledger-{Guid.NewGuid():N}.jsonl");
        try
        {
            var ledger = new ExploreLedger(file);
            var first = ledger.Append([new ExploreTrial { Track = "a", Config = "x", FoldValues = [0.6, null] }, new ExploreTrial { Track = "a", Config = "y" }], DateTime.UtcNow);
            var second = new ExploreLedger(file).Append([new ExploreTrial { Track = "b", Config = "z", Pooled = 0.7 }], DateTime.UtcNow);
            Assert.Multiple(() =>
            {
                Assert.That(first.Select(t => t.Id), Is.EqualTo(new[] { 1, 2 }));
                Assert.That(second.Single().Id, Is.EqualTo(3));
                Assert.That(ledger.Count, Is.EqualTo(3));
                Assert.That(ledger.ReadAll()[0].FoldValues, Is.EqualTo(new double?[] { 0.6, null }));
            });
        }
        finally { File.Delete(file); }
    }

    [Test]
    public void PanelFile_RoundTripsSparseRows_WithStableColumnOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"explore-{Guid.NewGuid():N}");
        try
        {
            using (var w = new ExplorePanelFile.Writer(dir, "08:00"))
            {
                w.Add("AAA", new DateOnly(2023, 1, 3), true, 1, [new("zeta", 1), new("alpha", 2)], [new("Reach20By0945", 1)]);
                w.Add("BBB", new DateOnly(2023, 1, 4), false, 10, [new("alpha", 3), new("nan", double.NaN)], []);
                w.Finish(DateTime.UtcNow);
            }
            var t = ExplorePanelFile.Read(dir, "08:00");
            Assert.Multiple(() =>
            {
                Assert.That(t.FeatureNames, Is.EqualTo(new[] { "alpha", "zeta" }), "sorted by name; a NaN-only key never registers");
                Assert.That(t.Rows, Is.EqualTo(2));
                Assert.That(t.Value(0, t.Feature("zeta")), Is.EqualTo(1));
                Assert.That(float.IsNaN(t.Value(1, t.Feature("zeta"))), "absent is NaN");
                Assert.That(t.Weights, Is.EqualTo(new[] { 1.0, 10.0 }));
                Assert.That(t.Binary("Reach20By0945"), Is.EqualTo(new bool?[] { true, null }));
                Assert.That(t.IsCandidate, Is.EqualTo(new[] { true, false }));
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void Stage_RefusesEverySplitButTrain()
    {
        using var host = new GapperStudyHost();
        var stage = host.Get<GapperExploreStage>();
        foreach (var command in new[] { "visibility", "alarms" })
            foreach (var split in new[] { "validation", "holdout", "smoke" })
                Assert.ThrowsAsync<InvalidOperationException>(() => stage.RunAsync(new ExploreRequest(command, split, [], false), CancellationToken.None), $"{command} {split}");
    }

    /// <summary>
    /// 6 candidate symbols over ~7 months of train. A running morning trades 3,000 shares a minute
    /// from 06:00 and climbs from 08:00 to +30% by 09:30; a quiet one trades 100 a minute, flat.
    /// </summary>
    private static async Task SeedAsync(GapperStudyHost host)
    {
        var rng = new Random(11);
        var store = host.Store;
        var profileDays = new List<(int, IReadOnlyList<Candle>)>();
        await using var db = await host.DbAsync();
        var day = new DateOnly(2021, 11, 1);
        while (day < new DateOnly(2022, 6, 1))
        {
            for (int i = 0; i < 6; i++)
            {
                var sym = $"C{i}";
                bool runs = rng.NextDouble() < 0.3;
                Func<int, double> close = runs ? m => m < 240 ? 2.0 : m < 330 ? 2.0 + 0.6 * (m - 240) / 90.0 : 2.6 : _ => 2.0;
                Func<int, double> volume = runs ? m => m >= 120 ? 3000 : 100 : _ => 100;
                var bars = Session(day, close, volume);
                store.AppendDay(sym, day, bars);
                if (!runs && profileDays.Count < 40) profileDays.Add((3, bars));
                db.GapperStudyUniverseDays.Add(new GapperStudyUniverseDay
                {
                    Symbol = sym, DateEt = day, Split = "train", AdjPrevClose = 2m, Adv20Dollars = 400_000m, Adv20Shares = 200_000m, Sessions = 20, AdvDecile = 3, PriceTier = 1,
                    IsCandidate = true, IsGapper = runs, GapTier = runs ? "Tier20" : null, DayVolume = runs ? 2_000_000m : 96_000m,
                });
            }
            day = MarketCalendar.NextTradingDay(day);
        }
        await db.SaveChangesAsync();
        Directory.CreateDirectory(host.Paths.DataDir);
        TimeOfDayProfile.Build(profileDays).Save(host.Paths.ProfileFile(false));
    }

    [Test]
    public async Task BuildThenScore_FindsTheRunnersFromPremarketVolume()
    {
        using var host = new GapperStudyHost();
        var cache = Path.Combine(host.StudyDir, "explore-cache");
        Environment.SetEnvironmentVariable("GAPPER_EXPLORE_DIR", cache);
        try
        {
            await SeedAsync(host);
            var stage = host.Get<GapperExploreStage>();
            ExploreRequest Req(string command, params string[] times) => new(command, "train", times, false, InitialFitMonths: 2, EvalMonths: 1);

            var build = await stage.RunAsync(Req("build", "07:00", "08:00"), CancellationToken.None);
            var again = await stage.RunAsync(Req("build", "08:00"), CancellationToken.None);
            var table = ExplorePanelFile.Read(cache, "08:00");
            var vis = await stage.RunAsync(Req("visibility", "07:00", "08:00"), CancellationToken.None);
            var models = await stage.RunAsync(Req("models", "08:00"), CancellationToken.None);
            var exits = await stage.RunAsync(Req("exits", "08:00"), CancellationToken.None);
            var ledger = stage.Ledger.ReadAll();
            var boosted08 = ledger.Single(t => t.Track == "visibility" && t.Config == "t=08:00 learner=boosted rows=forming");

            Assert.Multiple(() =>
            {
                Assert.That(build.Lines.Single(), Does.Contain("built 2 decision times"));
                Assert.That(again.Lines.Single(), Does.Contain("already cached"));
                Assert.That(table.Rows, Is.EqualTo(table.Days.Distinct().Count() * 6));
                Assert.That(table.Feature("VolRatioTod"), Is.GreaterThanOrEqualTo(0));
                Assert.That(table.Binary(GapperExploreStage.Target).Count(v => v == true), Is.GreaterThan(50));
                Assert.That(boosted08.Pooled, Is.GreaterThan(0.95), "premarket volume from 06:00 gives the runners away by 08:00");
                Assert.That(boosted08.FoldValues, Has.Count.EqualTo(4));
                Assert.That(ledger.Count(t => t.Track == "visibility"), Is.EqualTo(6), "three learners at two times");
                Assert.That(ledger.Count(t => t.Track == "models"), Is.GreaterThan(4));
                Assert.That(models.Trials, Is.EqualTo(ledger.Count(t => t.Track == "models")));
                Assert.That(exits.Trials, Is.Zero, "the exit table is descriptive");
                Assert.That(File.ReadAllText(exits.ReportFile!), Does.Contain("Tier20+"));
                Assert.That(vis.ReportFile, Does.StartWith(Path.Combine(host.Paths.RunsDir, "explore")));
            });
        }
        finally { Environment.SetEnvironmentVariable("GAPPER_EXPLORE_DIR", null); }
    }

    [Test]
    public void Sympathy_CountsOtherSymbolsAlreadyUpTheSameMorning()
    {
        var d1 = new DateOnly(2023, 3, 1); var d2 = new DateOnly(2023, 3, 2);
        float[] gaps = [25, 12, 30, 5, float.NaN, 22];
        var t = new ExploreTable
        {
            Decision = "08:00", FeatureNames = ["GapPct"], LabelNames = [], Symbols = ["A", "B", "C", "D", "E", "F"],
            Days = [d1, d1, d1, d1, d1, d2], IsCandidate = [true, true, true, false, false, true], Weights = [1, 1, 1, 10, 10, 1],
            X = gaps, L = [],
        };
        var s = ExploreDerived.AddSympathy(t);
        int formed = s.Feature(ExploreDerived.SympathyFormed), forming = s.Feature(ExploreDerived.SympathyForming);
        Assert.Multiple(() =>
        {
            Assert.That(Enumerable.Range(0, 4).Select(r => s.Value(r, formed)), Is.EqualTo(new float[] { 1, 2, 1, 2 }), "A and C are up 20%; each counts only the other");
            Assert.That(Enumerable.Range(0, 4).Select(r => s.Value(r, forming)), Is.EqualTo(new float[] { 2, 2, 2, 3 }));
            Assert.That(float.IsNaN(s.Value(4, formed)), "no gap value, no count");
            Assert.That(s.Value(5, formed), Is.EqualTo(0), "another morning is not counted");
            Assert.That(s.Value(0, s.Feature("GapPct")), Is.EqualTo(25), "existing columns are kept");
            Assert.That(ExploreDerived.AddSympathy(s).FeatureCount, Is.EqualTo(s.FeatureCount), "adding twice is a no-op");
        });
    }

    [Test]
    public void ClusteredMean_WeighsRowsAndClustersByDay()
    {
        var d1 = new DateOnly(2023, 3, 1); var d2 = new DateOnly(2023, 3, 2);
        var m = ExploreDerived.ClusteredMean([(d1, 0.10, 1), (d1, 0.30, 1), (d2, -0.10, 2)])!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(m.Mean, Is.EqualTo(0.05).Within(1e-12), "(0.1 + 0.3 - 0.2) / 4");
            Assert.That(m.Days, Is.EqualTo(2));
            Assert.That(m.Lo, Is.LessThan(m.Mean));
            Assert.That(m.Hi, Is.GreaterThan(m.Mean));
            Assert.That(ExploreDerived.ClusteredMean([(d1, 0.1, 1)]), Is.Null, "one day has no interval");
        });
    }
}
