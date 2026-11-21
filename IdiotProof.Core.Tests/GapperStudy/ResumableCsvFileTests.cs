using IdiotProof.Core.Research.Gappers.Explore;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T618: explore quotes-pull failed with a sharing violation because two pulls appended to the same CSV at once
/// (the file carries thousands of duplicated legs). One writer holds the lock; a reader mid-pull does not block an
/// append; duplicated legs count once in quotes-check.
/// </summary>
[TestFixture]
public class ResumableCsvFileTests
{
    private string dir = "";

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "resumable-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Test]
    public void SecondWriterIsRefusedUntilTheFirstReleases()
    {
        var file = Path.Combine(dir, "q.csv");
        using (var first = ResumableCsvFile.TryAcquireWriter(file))
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(ResumableCsvFile.TryAcquireWriter(file), Is.Null);
        }
        using var again = ResumableCsvFile.TryAcquireWriter(file);
        Assert.That(again, Is.Not.Null);
    }

    [Test]
    public async Task AppendSucceedsWhileAReaderHoldsTheFileOpen()
    {
        var file = Path.Combine(dir, "q.csv");
        await ResumableCsvFile.AppendAsync(file, "h\na\n", CancellationToken.None);
        using (var lines = ResumableCsvFile.ReadLines(file).GetEnumerator())
        {
            Assert.That(lines.MoveNext(), Is.True);
            await ResumableCsvFile.AppendAsync(file, "b\n", CancellationToken.None, attempts: 1);
        }
        Assert.That(ResumableCsvFile.ReadLines(file).ToList(), Is.EqualTo(new[] { "h", "a", "b" }));
    }

    [Test]
    public async Task DuplicatedLegsAreReadOnce()
    {
        var file = Path.Combine(dir, "q.csv");
        const string row = "ABC,2024-03-01,entry,2024-03-01T13:30:00Z,1,2.5,0.01,0.002,0.03,12,0.02,0.021,2.49,2.51,100,200";
        await ResumableCsvFile.AppendAsync(file, "header\n" + row + "\n" + row + "\n" + row.Replace(",entry,", ",exit,") + "\n", CancellationToken.None);
        var events = GapperExploreStage.ReadQuoteEvents(file);
        Assert.That(events.Select(e => e.Leg), Is.EqualTo(new[] { "entry", "exit" }));
    }
}
