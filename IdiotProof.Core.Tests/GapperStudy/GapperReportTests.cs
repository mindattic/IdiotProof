using System.Text.Json.Nodes;
using IdiotProof.Core.Data;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T442 A2: the results document from a tiny fake run set — one table per family, holdout rows
/// withheld without holdout-run.json, the post-registration variant marked, post-hoc hypotheses
/// voided, the newest run per hypothesis and split, and the CSV export with its hashed manifest.
/// </summary>
[TestFixture]
public class GapperReportTests
{
    private GapperStudyHost host = null!;

    [SetUp] public void SetUp() => host = new GapperStudyHost();
    [TearDown] public void TearDown() => host.Dispose();

    private static HypothesisRun Run(string id, string family, string split, string verdict, DateTime computed, bool primary = true) => new()
    {
        Id = id, Family = family, Primary = primary, Split = split, Test = "x", Claim = "claim", ComputedUtc = computed, Verdict = verdict,
        Inputs = new HypothesisRunInputs { N = 100, Days = 10, Head = "abcdef0123456789" },
        Parts = [new HypothesisPart { Name = "lift", EffectName = "lift", Effect = 2.5, MinEffect = 2.0, RawP = 0.01, FamilyQ = 0.02, HolmP = 0.03, CiLow = 2.1, CiHigh = 2.9, N = 100, Days = 10, Verdict = verdict, MeetsMinimum = true }],
    };

    private GapperReportModel Model(bool holdoutRunExists, params HypothesisRun[] runs) => new()
    {
        Params = host.Params, Hypotheses = GapperHypothesisSet.Load(host.Paths), GeneratedUtc = GapperStudyHost.Now.UtcDateTime, Head = "1234567890abcdef", FrozenCommit = "13f145219f97c40fb346d68336c31dfa59d3eba0",
        Runs = runs.ToList(), HoldoutRunExists = holdoutRunExists, PostRegistrationFeatures = [VelocityDeathNearPmHighFeature.FeatureName],
        PostRegistrationSummaries = [new PostRegistrationSummary("train", VelocityDeathNearPmHighFeature.FeatureName, 120, 30, 6.5, 30)],
        SpreadMultiplier = 1.8, SpreadMultiplierSource = GapperSpreadMultiplier.FileName,
        // sample.json deliberately shows a stale tier20=0 (as it would before a running pull has classified any gapper day) and no pull/sipShare at all;
        // the live Status/LiveSipSharePct below are what the report must actually show.
        Sample = new JsonObject { ["writtenUtc"] = "2026-11-06T00:00:00Z", ["splits"] = new JsonObject { ["train"] = new JsonObject { ["universeDays"] = 1, ["candidates"] = 1, ["gappers"] = new JsonObject { ["tier20"] = 0 }, ["controls"] = 0, ["excluded"] = 0, ["symbols"] = 1, ["firstDate"] = "2021-11-01", ["lastDate"] = "2024-10-31" }, ["holdout"] = new JsonObject { ["universeDays"] = 99 } }, ["cikCoverage"] = new JsonObject { ["mapped"] = 3, ["candidateSymbols"] = 4, ["coveragePct"] = 75.0, ["h8Dropped"] = false } },
        Status = new StudyStatus("db", "root", "dir", null, null,
            [new SplitStatus("train", 10, 5, 3500, 1200, 300, 1, 99.0, 1000, 1000, new DateOnly(2021, 11, 1), new DateOnly(2024, 10, 31))],
            0, 0, 0, 0, 0, 0, 0, 0, false, false, false, "locked"),
        LiveSipSharePct = 0.97,
    };

    [Test]
    public void Builder_WithholdsHoldout_MarksPostRegistration_AndRendersEveryFamily()
    {
        var now = GapperStudyHost.Now.UtcDateTime;
        var md = GapperReportBuilder.Build(Model(false,
            Run("H1", "F1", "train", HypothesisVerdict.Supported, now),
            Run("H1", "F1", "holdout", HypothesisVerdict.Supported, now),
            Run("H4", "F3", "train", HypothesisVerdict.Undecided, now),
            Run("H13", "F3", "train", HypothesisVerdict.Refuted, now, primary: false)));
        Assert.Multiple(() =>
        {
            Assert.That(md, Does.Contain("## Protocol (fixed before running)"));
            Assert.That(md, Does.Contain("### F1 prediction buckets").And.Contain("### F5 structural"));
            Assert.That(md, Does.Contain("| H1 | train |"));
            Assert.That(md, Does.Not.Contain("| H1 | holdout |"), "holdout numbers are withheld without holdout-run.json");
            Assert.That(md, Does.Contain("| holdout | withheld |"));
            Assert.That(md, Does.Contain("Post-registration variant"));
            Assert.That(md, Does.Contain($"`{VelocityDeathNearPmHighFeature.FeatureName}` ⚠ post-registration"));
            Assert.That(md, Does.Contain("H13 (secondary)"));
            Assert.That(md, Does.Contain("Catalyst-latency benchmark (H11"));
            Assert.That(md, Does.Contain("not yet testable"));
            Assert.That(md, Does.Contain("Kill-criteria status"));
            Assert.That(md, Does.Contain("3,500"), "live Tier20 train count from Status wins over sample.json's stale 0");
            Assert.That(md, Does.Not.Contain("| train | 1 | 1 | 0 |"), "sample.json's stale per-split numbers are not what gets rendered");
            Assert.That(md, Does.Contain("97.00 %").Or.Contain("97.00%"), "live SIP share, not a cached sample.json figure");
            Assert.That(md, Does.Contain("1.800"), "the calibrated spread multiplier");
            Assert.That(md, Does.Contain("| H4 | train |").And.Contain("interval only (descriptive)"));
        });
    }

    [Test]
    public void Builder_IncludesHoldout_AndVoidsPostHocHypotheses_WhenTheRunExists()
    {
        var now = GapperStudyHost.Now.UtcDateTime;
        var model = Model(true, Run("H1", "F1", "holdout", HypothesisVerdict.Supported, now), Run("H9", "F1", "train", HypothesisVerdict.Supported, now));
        model.PostHocHypotheses.Add("H9");
        model.PostUnlockCommits.Add(("deadbeefdeadbeef", "2026-12-02T10:00:00-05:00", "tweak H9"));
        var md = GapperReportBuilder.Build(model);
        Assert.Multiple(() =>
        {
            Assert.That(md, Does.Contain("| H1 | holdout |"));
            Assert.That(md, Does.Contain("H9 **post-hoc**"));
            Assert.That(md, Does.Contain("void (post-hoc)"));
            Assert.That(md, Does.Contain("`deadbeef`"));
            Assert.That(md, Does.Contain("affected hypotheses void: H9"));
        });
    }

    [Test]
    public void LoadLatestRuns_KeepsTheNewestPerHypothesisAndSplit_AndSkipsSmoke()
    {
        Directory.CreateDirectory(host.Paths.RunsDir);
        var older = Run("H1", "F1", "train", HypothesisVerdict.Undecided, new DateTime(2026, 11, 7, 1, 0, 0, DateTimeKind.Utc));
        var newer = Run("H1", "F1", "train", HypothesisVerdict.Supported, new DateTime(2026, 11, 7, 2, 0, 0, DateTimeKind.Utc));
        var smoke = Run("H1", "F1", "smoke", HypothesisVerdict.Supported, new DateTime(2026, 11, 7, 3, 0, 0, DateTimeKind.Utc));
        smoke.Smoke = true;
        File.WriteAllText(Path.Combine(host.Paths.RunsDir, HypothesisRun.FileName("H1", "train", older.ComputedUtc)), older.ToJson());
        File.WriteAllText(Path.Combine(host.Paths.RunsDir, HypothesisRun.FileName("H1", "train", newer.ComputedUtc)), newer.ToJson());
        File.WriteAllText(Path.Combine(host.Paths.RunsDir, HypothesisRun.FileName("H1", "smoke", smoke.ComputedUtc)), smoke.ToJson());
        File.WriteAllText(Path.Combine(host.Paths.RunsDir, "test-garbage.json"), "{not json");
        var runs = GapperReportStage.LoadLatestRuns(host.Paths.RunsDir);
        Assert.That(runs, Has.Count.EqualTo(1));
        Assert.That(runs[0].Verdict, Is.EqualTo(HypothesisVerdict.Supported));
        Assert.That(runs[0].File, Does.EndWith("test-H1-train-20261107-0200.json"));
    }

    [Test]
    public void ChangedHypotheses_FlagsOnlyTheEditedDefinition_OrEverythingWhenParamsMoved()
    {
        var set = GapperHypothesisSet.Load(host.Paths);
        var original = File.ReadAllText(host.Paths.HypothesesFile);
        var edited = original.Replace("\"precisionOverBaseRate\": 3.0", "\"precisionOverBaseRate\": 2.0");
        Assert.That(edited, Is.Not.EqualTo(original));
        var p = File.ReadAllText(host.Paths.ParamsFile);
        Assert.Multiple(() =>
        {
            Assert.That(GapperReportStage.ChangedHypotheses(original, edited, p, p, set), Is.EquivalentTo(new[] { "H1" }));
            Assert.That(GapperReportStage.ChangedHypotheses(original, original, p, p, set), Is.Empty);
            Assert.That(GapperReportStage.ChangedHypotheses(original, original, p, p + " ", set).Count(), Is.EqualTo(17), "a params edit voids every hypothesis");
        });
    }

    [Test]
    public async Task Stage_WritesTheDocument_AndExportsHashedCsvs_WithholdingHoldout()
    {
        await using (var db = await host.DbAsync())
        {
            db.GapperStudyEvents.Add(new GapperStudyEvent { Symbol = "A", DateEt = new DateOnly(2024, 1, 3), Split = "train", AdjPrevClose = 1m, GapMax = 0.3, Tier20 = true, Tier20Touched = true, PmHigh = 1.3m, SubType = "premarket" });
            db.GapperStudyEvents.Add(new GapperStudyEvent { Symbol = "B", DateEt = new DateOnly(2026, 1, 5), Split = "holdout", AdjPrevClose = 1m, GapMax = 0.3, Tier20 = true, Tier20Touched = true, PmHigh = 1.3m });
            db.GapperStudyEvents.Add(new GapperStudyEvent { Symbol = "C", DateEt = new DateOnly(2026, 11, 3), Split = GapperStudyServices.SmokeSplit, AdjPrevClose = 1m, GapMax = 0.3, PmHigh = 1.3m });
            db.GapperStudyLabels.Add(new GapperStudyLabel { Symbol = "A", DateEt = new DateOnly(2024, 1, 3), Split = "train", DecisionLabel = "08:00", DecisionTimeEt = new TimeOnly(8, 0), DecisionUtc = DateTime.UtcNow, HodBucket = "<09:15, really" });
            await db.SaveChangesAsync();
        }
        Directory.CreateDirectory(host.Paths.RunsDir);
        var run = Run("H4", "F3", "train", HypothesisVerdict.Undecided, GapperStudyHost.Now.UtcDateTime);
        File.WriteAllText(Path.Combine(host.Paths.RunsDir, HypothesisRun.FileName("H4", "train", run.ComputedUtc)), run.ToJson());
        var held = Run("H4", "F3", "holdout", HypothesisVerdict.Supported, GapperStudyHost.Now.UtcDateTime);
        File.WriteAllText(Path.Combine(host.Paths.RunsDir, HypothesisRun.FileName("H4", "holdout", held.ComputedUtc)), held.ToJson());

        var stage = host.Get<GapperReportStage>();
        var r = await stage.RunAsync(new ReportStageRequest(Export: true), CancellationToken.None);
        var md = File.ReadAllText(r.ReportFile);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(host.Paths.DataDir, GapperStudyExport.ManifestFile))) as JsonObject;
        var events = File.ReadAllLines(Path.Combine(host.Paths.DataDir, "events.csv"));
        Assert.Multiple(() =>
        {
            Assert.That(r.ReportFile, Is.EqualTo(Path.Combine(Path.GetDirectoryName(host.StudyDir)!, Path.GetFileName(host.StudyDir) + ".md")));
            Assert.That(r.RunsUsed, Is.EqualTo(1));
            Assert.That(r.HoldoutRunsWithheld, Is.EqualTo(1));
            Assert.That(md, Does.Contain("| H4 | train |").And.Not.Contain("| H4 | holdout |"));
            Assert.That(events, Has.Length.EqualTo(2), "header plus the train event: holdout and smoke rows are excluded");
            Assert.That(events[1], Does.StartWith("A,2024-01-03,train,"));
            Assert.That(r.Exports.Select(e => e.Name), Is.EqualTo(new[] { "events.csv", "snapshots.csv", "labels.csv" }));
            Assert.That(r.Exports[0].Rows, Is.EqualTo(1));
            Assert.That(manifest!["files"]![0]!["sha256"]!.GetValue<string>(), Has.Length.EqualTo(64));
            Assert.That(manifest["holdoutIncluded"]!.GetValue<bool>(), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(host.Paths.DataDir, "labels.csv")), Does.Contain("\"<09:15, really\""), "fields with commas are quoted");
        });
    }
}
