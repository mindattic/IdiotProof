using IdiotProof.Core.Research.Gappers;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T442: the holdout refuses without the unlock file, with the wrong hash, with uncommitted edits, and a second time.</summary>
[TestFixture]
public class HoldoutLockTests
{
    private const string Frozen = "13f145219f97c40fb346d68336c31dfa59d3eba0";
    private string dir = null!;
    private GapperStudyPaths paths = null!;
    private GapperStudyParams p = null!;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "gapper-holdout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var real = GapperStudyTestKit.Paths;
        File.Copy(real.ParamsFile, Path.Combine(dir, "params.json"));
        File.Copy(real.HypothesesFile, Path.Combine(dir, "hypotheses.json"));
        paths = new GapperStudyPaths(dir);
        p = GapperStudyParams.Load(paths);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(dir, true);

    private HoldoutLock Lock(bool uncommitted = false, string? commit = Frozen) => new(paths, p, () => commit, () => uncommitted);

    [Test]
    public void Refuses_WithoutTheUnlockFile()
    {
        var r = Lock().Check("snapshot");
        Assert.That(r.Allowed, Is.False);
        Assert.That(r.Reason, Does.Contain("holdout-unlock.txt"));
    }

    [Test]
    public void Refuses_WhenTheHashDoesNotMatchTheFrozenCommit()
    {
        File.WriteAllText(paths.UnlockFile(p), "0000000000000000000000000000000000000000");
        var r = Lock().Check("snapshot");
        Assert.That(r.Allowed, Is.False);
        Assert.That(r.Reason, Does.Contain(Frozen));
    }

    [Test]
    public void Refuses_WithUncommittedChangesToTheFrozenFiles()
    {
        File.WriteAllText(paths.UnlockFile(p), Frozen);
        Assert.That(Lock(uncommitted: true).Check("snapshot").Reason, Does.Contain("uncommitted"));
    }

    [Test]
    public void Unlocks_Once_PerStage_ThenRefusesASecondRun()
    {
        File.WriteAllText(paths.UnlockFile(p), Frozen + "\n");
        var gate = Lock();
        Assert.That(gate.Check("snapshot").Allowed, Is.True);
        gate.BeginStage("snapshot", new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc), new { rows = 0 });
        Assert.That(File.Exists(paths.HoldoutRunFile(p)), Is.True, "holdout-run.json is written before the first row");
        gate.EndStage("snapshot", new DateTime(2026, 12, 1, 16, 0, 0, DateTimeKind.Utc));

        var second = gate.Check("snapshot");
        Assert.Multiple(() =>
        {
            Assert.That(second.Allowed, Is.False);
            Assert.That(second.Reason, Does.Contain("already records"));
            Assert.That(gate.Check("label").Allowed, Is.True, "the label stage of the same single run is still allowed");
        });
        var json = File.ReadAllText(paths.HoldoutRunFile(p));
        Assert.That(json, Does.Contain("\"unlockHash\": \"" + Frozen + "\""));
        Assert.That(json, Does.Contain("\"paramsSha256\": \"" + p.ParamsSha256 + "\""));
    }

    [Test]
    public void Refuses_WhenHypothesesAreNotFrozen()
    {
        File.WriteAllText(Path.Combine(dir, "hypotheses.json"), "{\"version\":1,\"frozenUtc\":null,\"hypotheses\":[]}");
        File.WriteAllText(paths.UnlockFile(p), Frozen);
        var unfrozen = GapperStudyParams.Load(paths);
        var r = new HoldoutLock(paths, unfrozen, () => Frozen, () => false).Check("snapshot");
        Assert.That(r.Reason, Does.Contain("frozenUtc"));
    }
}
