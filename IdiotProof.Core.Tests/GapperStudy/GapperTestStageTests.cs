using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T442 A2: every test type on a synthetic panel with a planted answer — a planted top-quintile
/// lift (H1), a planted same-day CAR (H2), a planted fade-rate gap (H3), Wilson timing (H4), the
/// two-part quantile verdict (H5), the two-part structural group difference (H7), a planted AUC gap
/// over the catalyst benchmark (H11), the H12 grid plumbing, run-file output and the holdout refusal.
/// </summary>
[TestFixture]
public class GapperTestStageTests
{
    private const string Split = GapperStudyServices.SmokeSplit;
    private static readonly string[] Labels = ["08:00", "08:30", "09:00", "09:15", "09:30", "09:45"];
    private GapperStudyHost host = null!;

    [SetUp] public void SetUp() => host = new GapperStudyHost();
    [TearDown] public void TearDown() => host.Dispose();

    private sealed record Cell(int Day, int Index, DateOnly DateEt, string Symbol, Random Rng);

    /// <summary>Seeds <paramref name="days"/> x <paramref name="symbols"/> candidate cells; the callback fills the universe row, every snapshot and every label from the cell.</summary>
    private async Task SeedAsync(int days, int symbols, Action<Cell, GapperStudyUniverseDay, GapperFormationSnapshotRow, GapperStudyLabel> fill, int seed = 42)
    {
        var rng = new Random(seed);
        var start = new DateOnly(2026, 1, 5);
        await using var db = await host.DbAsync();
        var day = start;
        for (int d = 0; d < days; d++)
        {
            for (int i = 0; i < symbols; i++)
            {
                var cell = new Cell(d, i, day, $"S{i:00}", rng);
                var u = new GapperStudyUniverseDay { Symbol = cell.Symbol, DateEt = day, Split = Split, AdjPrevClose = 2m, Adv20Dollars = 400_000m, Adv20Shares = 200_000m, Sessions = 20, AdvDecile = 3, PriceTier = 1, IsCandidate = true, DayVolume = 300_000m };
                var snapshots = new List<GapperFormationSnapshotRow>();
                var labels = new List<GapperStudyLabel>();
                foreach (var label in Labels)
                {
                    var t = MinuteBarMath.ToUtc(day, GapperStudyParams.ParseEt(label));
                    var snap = new GapperFormationSnapshotRow { Source = GapperSnapshotStage.Source, Symbol = cell.Symbol, DateEt = day, Split = Split, DecisionLabel = label, DecisionTimeEt = TimeOnly.FromTimeSpan(GapperStudyParams.ParseEt(label)), DecisionUtc = t, IsCandidate = true, FeatureVersion = 1, PmCumVol = 50_000, ExtraJson = "{}" };
                    var lab = new GapperStudyLabel { Symbol = cell.Symbol, DateEt = day, Split = Split, DecisionLabel = label, DecisionTimeEt = TimeOnly.FromTimeSpan(GapperStudyParams.ParseEt(label)), DecisionUtc = t, EntryRef = 2m, EntryUtc = t };
                    fill(cell, u, snap, lab);
                    snapshots.Add(snap);
                    labels.Add(lab);
                }
                db.GapperStudyUniverseDays.Add(u);
                db.GapperFormationSnapshots.AddRange(snapshots);
                db.GapperStudyLabels.AddRange(labels);
            }
            day = MarketCalendar.NextTradingDay(day);
        }
        await db.SaveChangesAsync();
    }

    private Task<TestStageResult> RunAsync(params string[] ids) =>
        host.Get<GapperTestStage>().RunAsync(new TestStageRequest(ids, "train", Smoke: true), CancellationToken.None);

    [Test]
    public async Task H1_BucketLift_FindsThePlantedTopQuintilePrecision()
    {
        await SeedAsync(60, 20, (c, u, snap, lab) =>
        {
            double ratio = c.Rng.NextDouble() * 10;
            snap.VolRatioTod = ratio;
            lab.Reach20By0945 = c.Rng.NextDouble() < (ratio > 8 ? 0.8 : 0.05);
        });
        var r = await RunAsync("H1");
        var run = r.Runs.Single();
        var part = run.Parts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Test, Is.EqualTo("bucket-lift"));
            Assert.That(part.Effect, Is.GreaterThan(3.0), "top-quintile precision over the base rate");
            Assert.That(part.RawP, Is.LessThan(0.001));
            Assert.That(part.FamilyQ, Is.LessThan(0.05));
            Assert.That(part.HolmP, Is.LessThan(0.05));
            Assert.That(run.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(run.Inputs.N, Is.EqualTo(1200));
            Assert.That(run.Inputs.Days, Is.EqualTo(60));
            Assert.That(File.Exists(run.File), Is.True);
            Assert.That(Path.GetFileName(run.File)!, Does.StartWith("test-H1-smoke-"));
        });
        var json = JsonDocument.Parse(File.ReadAllText(run.File!)).RootElement;
        Assert.That(json.GetProperty("inputs").GetProperty("paramsSha256").GetString(), Is.EqualTo(host.Params.ParamsSha256));
        Assert.That(json.GetProperty("parts")[0].GetProperty("statistic").GetProperty("bins").GetInt32(), Is.EqualTo(host.Params.Statistics.BucketBins));
    }

    [Test]
    public async Task H2_And_H3_CatalystGroups_FindThePlantedDifferences()
    {
        await SeedAsync(50, 12, (c, u, snap, lab) =>
        {
            u.IsGapper = true; u.GapTier = "Tier20";
            bool catalyst = c.Index % 2 == 0;
            if (catalyst) { snap.CatalystAgeMinutes = 30; snap.CatalystPreClass = c.Index % 4 == 0 ? "Fda" : "Other"; snap.CatalystScore = 50; }
            double common = c.Day % 7 * 0.01;
            lab.FwdClose1030 = common;
            lab.FwdClose1600 = common + (snap.CatalystPreClass == "Fda" ? 0.05 : 0) + c.Rng.NextDouble() * 0.01;
            lab.FadeFlag30 = c.Rng.NextDouble() < (catalyst ? 0.4 : 0.7);
        });
        var r = await RunAsync("H2", "H3");
        var h2 = r.Runs.Single(x => x.Id == "H2").Parts.Single();
        var h3 = r.Runs.Single(x => x.Id == "H3").Parts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(h2.Effect, Is.EqualTo(5.0).Within(0.6), "Fda/ContractAward/MA minus no-catalyst, in pct");
            Assert.That(h2.RawP, Is.LessThan(0.01));
            Assert.That(h2.Statistic["placeboP"], Is.LessThan(0.01));
            Assert.That(r.Runs.Single(x => x.Id == "H2").Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(h3.Effect, Is.GreaterThan(20), "no-catalyst fade rate minus catalyst fade rate, in pp");
            Assert.That(h3.CiLow, Is.GreaterThan(10));
            Assert.That(r.Runs.Single(x => x.Id == "H3").Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(h2.FamilyQ, Is.Not.Null.And.LessThan(0.05), "BH within F2 over both runs");
        });
    }

    [Test]
    public async Task H4_H5_H13_Timing_AreJudgedByIntervalsOnly()
    {
        await SeedAsync(40, 10, (c, u, snap, lab) =>
        {
            u.IsGapper = true; u.GapTier = "Tier50";
            lab.HodTimeRthEt = c.Rng.NextDouble() < 0.8 ? new TimeOnly(9, 35) : new TimeOnly(11, 0);
            lab.VelocityDeathTimeEt = new TimeOnly(9, 20);
            lab.DeathToHodMin = c.Rng.Next(0, 4);
            lab.HodTimeEt = new TimeOnly(9, 24);
            lab.HodBucket = c.Rng.NextDouble() < 0.1 ? "09:30-09:31" : "09:15-09:29";
            lab.ExtensionFromOpen0945 = 0.05;
            lab.CloseBelowOpen = c.Rng.NextDouble() < 0.5;
        });
        var r = await RunAsync("H4", "H5", "H13");
        var h4 = r.Runs.Single(x => x.Id == "H4");
        var h5 = r.Runs.Single(x => x.Id == "H5");
        var h13 = r.Runs.Single(x => x.Id == "H13");
        Assert.Multiple(() =>
        {
            Assert.That(h4.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(h4.Parts[0].RawP, Is.Null, "F3 carries no p");
            Assert.That(h4.Parts[0].CiLow, Is.GreaterThan(0.6));
            Assert.That(h5.Parts, Has.Count.EqualTo(2));
            Assert.That(h5.Parts[0].Verdict, Is.EqualTo(HypothesisVerdict.Supported), "median death-to-HOD inside [-2, 5]");
            Assert.That(h5.Parts[1].Verdict, Is.EqualTo(HypothesisVerdict.Supported), "09:30-09:31 share under 25%");
            Assert.That(h5.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(h13.Verdict, Is.EqualTo(HypothesisVerdict.Refuted), "a 50% close-below-open rate sits entirely under 85%");
            Assert.That(string.Join(" ", h4.Parts[0].Reasons), Does.Not.Contain("significant"));
        });
    }

    [Test]
    public async Task H7_StructuralGroupDifference_HasTwoPartsBothMovingTheClaimedWay()
    {
        await SeedAsync(50, 12, (c, u, snap, lab) =>
        {
            u.IsGapper = true; u.GapTier = "Tier20";
            bool split = c.Index % 3 == 0;
            snap.DaysSinceReverseSplit = split ? 10 : null;
            lab.FwdClose1030 = (split ? -0.05 : 0) + c.Rng.NextDouble() * 0.01;
            lab.Fade = (split ? -0.5 : -0.2) + c.Rng.NextDouble() * 0.05;
        });
        var r = await RunAsync("H7");
        var run = r.Runs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Parts.Select(p => p.Name), Is.EqualTo(new[] { "fwdCloseDifferencePct", "fadeDifferencePct" }));
            Assert.That(run.Parts[0].Effect!.Value, Is.EqualTo(-5.0).Within(0.5));
            Assert.That(run.Parts[1].Effect!.Value, Is.EqualTo(-30.0).Within(2.0));
            Assert.That(run.Parts.All(p => p.FamilyQ < 0.05), Is.True);
            Assert.That(run.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
        });
    }

    [Test]
    public async Task H11_AucDelong_FindsThePlantedGapOverTheCatalystBenchmark()
    {
        await SeedAsync(60, 20, (c, u, snap, lab) =>
        {
            double signal = c.Rng.NextDouble() * 10;
            snap.VolRatioTod = signal;
            snap.Velocity3m = c.Rng.NextDouble();
            snap.GapPct = c.Rng.NextDouble() * 5;
            snap.CatalystAgeMinutes = c.Rng.NextDouble() < 0.3 ? 20 : null;
            lab.Reach20By0945 = signal + c.Rng.NextDouble() * 3 > 8;
        });
        var r = await RunAsync("H11");
        var part = r.Runs.Single().Parts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(part.Statistic["aucDetector"], Is.GreaterThan(0.85));
            Assert.That((double)part.Statistic["aucBenchmark"]!, Is.EqualTo(0.5).Within(0.08));
            Assert.That(part.Effect, Is.GreaterThan(0.3));
            Assert.That(part.CiLow, Is.GreaterThan(0.05));
            Assert.That(r.Runs.Single().Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(r.Runs.Single().Inputs.FeatureColumns, Does.Contain("VolRatioTod").And.Not.Contain("CatalystScore"), "the detector never sees the catalyst block");
        });
    }

    [Test]
    public async Task H12_Grid_RunsEveryConfig_WithTheFrozenCostModel()
    {
        Directory.CreateDirectory(host.Paths.DataDir);
        File.WriteAllText(GapperSpreadMultiplier.File(host.Paths), "{\"multiplier\": 1.5, \"n\": 500}");
        var store = host.Store;
        await SeedAsync(8, 3, (c, u, snap, lab) =>
        {
            snap.VolRatioTod = 12; snap.Velocity3m = 2.0; snap.PmCumVol = 100_000;
            snap.ExtraJson = "{\"csSpreadPrior\": 0.004}";
            lab.EntryRef = 2.00m;
            if (snap.DecisionLabel == "08:30")
                store.AppendDay(c.Symbol, c.DateEt, Session(c.DateEt, i => i < 270 ? 2.00 : i < 285 ? 2.00 + 0.02 * (i - 270) : 2.30 - 0.01 * (i - 285), _ => 500));
        });
        var r = await RunAsync("H12");
        var part = r.Runs.Single().Parts.Single();
        var configs = (System.Collections.IList)part.Statistic["configs"]!;
        Assert.Multiple(() =>
        {
            Assert.That(configs, Has.Count.EqualTo(24));
            Assert.That(part.Statistic["cellsPassingLoosest"], Is.EqualTo(24));
            Assert.That(part.Statistic["spreadMultiplier"], Is.EqualTo(1.5));
            Assert.That((double)part.Statistic["meanRoundTripCost"]!, Is.GreaterThanOrEqualTo(host.Params.CostModel.RoundTripFloor));
            Assert.That(part.Verdict, Is.EqualTo(HypothesisVerdict.Void), "24 trades can never reach the 150-trade minimum");
            Assert.That(part.Reasons[0], Does.Contain("150"));
        });
        Assert.That(GapperTestStage.Grid(host.Params), Has.Count.EqualTo(24));
    }

    [Test]
    public async Task UnknownHypothesis_IsAUsageError_AndTooFewRows_IsVoid()
    {
        await SeedAsync(3, 2, (c, u, snap, lab) => { snap.VolRatioTod = 1; lab.Reach20By0945 = false; });
        Assert.ThrowsAsync<ArgumentException>(() => RunAsync("H42"));
        var r = await RunAsync("H1");
        Assert.That(r.Runs.Single().Verdict, Is.EqualTo(HypothesisVerdict.Void));
        Assert.That(r.Runs.Single().Parts[0].Reasons[0], Does.Contain("minBucketN"));
    }

    [Test]
    public void HoldoutSplit_RefusesWithoutTheUnlockFile()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<GapperTestStage>().RunAsync(new TestStageRequest(["H1"], "holdout", false), CancellationToken.None));
        Assert.That(ex!.Message, Does.StartWith("Holdout locked"));
        Assert.That(Directory.Exists(host.Paths.RunsDir) ? Directory.GetFiles(host.Paths.RunsDir).Length : 0, Is.EqualTo(0), "nothing is written when the lock refuses");
    }

    [Test]
    public async Task SplitNotAllowed_IsVoid_WithoutTouchingTheData()
    {
        await SeedAsync(2, 2, (c, u, snap, lab) => { });
        var r = await host.Get<GapperTestStage>().RunAsync(new TestStageRequest(["H11"], "train", false), CancellationToken.None);
        Assert.That(r.Runs.Single().Verdict, Is.EqualTo(HypothesisVerdict.Void));
        Assert.That(r.Runs.Single().Parts[0].Reasons[0], Does.Contain("validation"));
    }
}
