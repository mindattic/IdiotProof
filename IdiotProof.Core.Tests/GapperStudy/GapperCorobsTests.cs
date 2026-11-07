using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Gappers;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T451 amendment 03 on planted data: candidate mornings reach +20% when the time-of-day volume
/// ratio sits in a middle band (4..6), a non-monotone pattern a logistic detector cannot draw and a
/// look-alike detector can. Corobs alone must beat the (empty) catalyst benchmark and the logistic
/// detector, every score must be reported side by side, and the amendment parses as registered.
/// </summary>
[TestFixture]
public class GapperCorobsTests
{
    private static readonly Lazy<GapperStudyPaths> Committed = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, GapperStudyPaths.RelativeDir.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(Path.Combine(candidate, "amendment-03.json"))) return new GapperStudyPaths(candidate);
        }
        throw new FileNotFoundException("amendment-03.json was not found above the test binary.");
    });

    private static GapperStudyHost Host()
    {
        var host = new GapperStudyHost();
        foreach (var f in Committed.Value.AmendmentFiles) File.Copy(f, Path.Combine(host.StudyDir, Path.GetFileName(f)));
        return host;
    }

    private static async Task SeedAsync(GapperStudyHost host)
    {
        var rng = new Random(7);
        var a = GapperAmendment.Load(Committed.Value)!;
        await using var db = await host.DbAsync();
        var day = new DateOnly(2023, 1, 3);
        for (int d = 0; d < 100; d++)
        {
            var split = d < 60 ? "train" : "validation";
            var symbols = Enumerable.Range(0, 20).Select(i => (Symbol: $"C{i:00}", Candidate: true)).ToList();
            symbols.AddRange(Enumerable.Range(0, 2000).Select(i => $"N{i:0000}").Where(sym => a.IsSampleMember(sym, day)).Take(10).Select(sym => (sym, false)));
            foreach (var (symbol, candidate) in symbols)
            {
                db.GapperStudyUniverseDays.Add(new GapperStudyUniverseDay { Symbol = symbol, DateEt = day, Split = split, AdjPrevClose = 2m, Adv20Dollars = 400_000m, Adv20Shares = 200_000m, Sessions = 20, AdvDecile = 3, PriceTier = 1, IsCandidate = candidate, DayVolume = 300_000m });
                double ratio = rng.NextDouble() * 10;
                bool reach = candidate && rng.NextDouble() < (ratio is > 4 and < 6 ? 0.85 : 0.03);
                var t = MinuteBarMath.ToUtc(day, new TimeSpan(8, 0, 0));
                db.GapperFormationSnapshots.Add(new GapperFormationSnapshotRow { Source = GapperSnapshotStage.Source, Symbol = symbol, DateEt = day, Split = split, DecisionLabel = "08:00", DecisionTimeEt = new TimeOnly(8, 0), DecisionUtc = t, IsCandidate = candidate, FeatureVersion = 1, PmCumVol = 50_000, VolRatioTod = ratio, GapPct = rng.NextDouble() * 5, ExtraJson = "{}" });
                db.GapperStudyLabels.Add(new GapperStudyLabel { Symbol = symbol, DateEt = day, Split = split, DecisionLabel = "08:00", DecisionTimeEt = new TimeOnly(8, 0), DecisionUtc = t, EntryRef = 2m, EntryUtc = t, Reach20By0945 = reach });
            }
            day = MarketCalendar.NextTradingDay(day);
        }
        await db.SaveChangesAsync();
    }

    private static Task<TestStageResult> RunAsync(GapperStudyHost host, string split, params string[] ids) =>
        host.Get<GapperTestStage>().RunAsync(new TestStageRequest(ids, split, Smoke: false), CancellationToken.None);

    [Test]
    public void RegisteredAmendment03_ParsesWithFourCorobsQuestions()
    {
        var a = GapperAmendment.LoadAll(Committed.Value).Single(x => x.Id == "A03");
        Assert.Multiple(() =>
        {
            Assert.That(a.DefinesSample, Is.False, "amendment 03 reuses amendment 01's population");
            Assert.That(a.Hypotheses.Select(h => (h.Id, h.Test)), Is.EqualTo(new[]
            {
                ("H21", GapperTestStage.CorobsAucTest), ("H22", GapperTestStage.CorobsPluginTest),
                ("H23", GapperTestStage.CorobsVersusLogisticTest), ("H24", GapperTestStage.CorobsNoveltyTest),
            }));
            Assert.That(a.Hypotheses.Where(h => h.Primary).Select(h => h.Id), Is.EqualTo(new[] { "H21", "H22" }));
        });
    }

    [Test]
    public async Task CorobsAlone_FindsTheBandTheLogisticDetectorCannotDraw()
    {
        using var host = Host();
        await SeedAsync(host);
        var runs = (await RunAsync(host, "validation", "H21", "H22", "H23")).Runs;
        var h21 = runs.Single(r => r.Id == "H21").Parts.Single();
        var h23 = runs.Single(r => r.Id == "H23").Parts.Single();
        var h22 = runs.Single(r => r.Id == "H22").Parts.Single();
        double Stat(HypothesisPart p, string k) => Convert.ToDouble(p.Statistic[k]);
        Assert.Multiple(() =>
        {
            Assert.That(runs.All(r => r.Inputs.AmendmentId == "A03"));
            Assert.That(h21.N, Is.EqualTo(40 * 30), "validation population rows: 20 candidates and 10 drawn sample days per day");
            Assert.That(Stat(h21, "aucBenchmark"), Is.EqualTo(0.5).Within(1e-9), "no catalyst in the planted data");
            Assert.That(Stat(h21, "aucCorobs"), Is.GreaterThan(0.75), "the look-alike score finds the band");
            Assert.That(h21.Effect, Is.GreaterThan(0.2));
            Assert.That(h23.Effect, Is.GreaterThan(0.1), "and beats the logistic detector head to head");
            Assert.That(Stat(h23, "aucLogistic"), Is.LessThan(Stat(h23, "aucCorobs")));
            Assert.That(Stat(h22, "aucCombined"), Is.GreaterThan(Stat(h22, "aucLogistic")), "plugging Corobs into the logistic detector helps it");
            Assert.That(h21.Statistic["fitOn"], Is.EqualTo("train"));
            Assert.That(Stat(h21, "dimensions"), Is.EqualTo(28 * CorobsLookAlike.DimensionsPerPly));
            Assert.That(Stat(h21, "trainRows"), Is.EqualTo(60 * 30));
        });
    }

    [Test]
    public async Task Novelty_RunsOnTrain_LeaveOneDayOut()
    {
        using var host = Host();
        await SeedAsync(host);
        var run = (await RunAsync(host, "train", "H24")).Runs.Single();
        var part = run.Parts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Inputs.AmendmentId, Is.EqualTo("A03"));
            Assert.That(part.Statistic, Does.ContainKey("noveltyEdge"));
            Assert.That(part.Statistic["fitOn"], Does.Contain("leave-one-day-out"));
            Assert.That(part.Effect, Is.Not.Null, "the top decile holds enough rows to score");
            Assert.That(part.Verdict, Is.Not.EqualTo(HypothesisVerdict.Void));
        });
    }
}
