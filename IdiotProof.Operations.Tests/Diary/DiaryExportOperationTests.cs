using IdiotProof.Operations.Diary;

namespace IdiotProof.Operations.Tests.Diary;

/// <summary>diary export (IP-T242): CSV to a file (absolute path, no silent overwrite) or back as text.</summary>
public class DiaryExportOperationTests
{
    private string dir = "";

    [SetUp]
    public void MakeDir()
    {
        dir = Path.Combine(Path.GetTempPath(), "idiotproof-diary-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void RemoveDir()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task WritesAFile_WithHeaderAndEscapedCells()
    {
        var kit = new DiaryKit();
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 28, 9, 35), 100m, 104m, title: "Gap, \"fast\" one", stop: 98m);
        await kit.TradeAsync("AMD", DiaryKit.Et(2026, 10, 29, 9, 35), 50m, null);
        var path = Path.Combine(dir, "diary.csv");

        var result = await new DiaryExportOperation().ExecuteAsync(new DiaryExportArgs { Csv = path }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Wrote 2 trades").And.Contain("realized +$40.00"));
        var lines = await File.ReadAllLinesAsync(path);
        Assert.That(lines[0], Is.EqualTo(string.Join(",", DiaryExportOperation.Columns)));
        Assert.That(lines[1], Does.Contain("\"Gap, \"\"fast\"\" one\"").And.Contain("NVDA").And.Contain("2026-10-28 9:35 AM ET")
            .And.Contain("2026-10-28T13:35:00Z").And.Contain(",40,").And.Contain(",2,"));
        Assert.That(lines[2], Does.Contain("AMD").And.Contain("Open"));
    }

    [Test]
    public async Task Dash_ReturnsTheCsv()
    {
        var kit = new DiaryKit();
        await kit.TradeAsync("NVDA", DiaryKit.Et(2026, 10, 28, 9, 35), 100m, 104m);

        var result = await new DiaryExportOperation().ExecuteAsync(new DiaryExportArgs { Csv = "-" }, kit.Context());

        Assert.That(result.Human!.Split('\n'), Has.Length.EqualTo(2));
        Assert.That(result.Human, Does.StartWith("id,strategy_id,"));
    }

    [Test]
    public async Task RelativePath_AndExistingFile_AreRefused()
    {
        var kit = new DiaryKit();
        var existing = Path.Combine(dir, "taken.csv");
        await File.WriteAllTextAsync(existing, "keep me");

        var relative = await new DiaryExportOperation().ExecuteAsync(new DiaryExportArgs { Csv = "diary.csv" }, kit.Context());
        var clash = await new DiaryExportOperation().ExecuteAsync(new DiaryExportArgs { Csv = existing }, kit.Context());
        var overwrite = await new DiaryExportOperation().ExecuteAsync(new DiaryExportArgs { Csv = existing, Overwrite = true }, kit.Context());

        Assert.That(relative.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(clash.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(overwrite.IsOk, Is.True, overwrite.Message);
        Assert.That(await File.ReadAllTextAsync(existing), Does.StartWith("id,"));
    }
}
