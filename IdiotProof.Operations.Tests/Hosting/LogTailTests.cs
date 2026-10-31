using System.Text;
using IdiotProof.Operations.Hosting;

namespace IdiotProof.Operations.Tests.Hosting;

/// <summary>IP-T78: /api/logs tails the Hub's window log while the Hub is still writing it.</summary>
public class LogTailTests
{
    private string path = "";

    [SetUp]
    public void SetUp() => path = Path.Combine(Path.GetTempPath(), $"logtail-{Guid.NewGuid():N}.log");

    [TearDown]
    public void TearDown() => File.Delete(path);

    [Test]
    public void ReturnsTheLastLines_OldestFirst_AcrossBlockBoundaries()
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= 20_000; i++) sb.Append("line ").Append(i).Append(" — ").Append(new string('x', 20)).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

        var tail = LogTail.ReadLastLines(path, 3);

        Assert.That(tail.Select(l => l.Split(' ')[1]), Is.EqualTo(new[] { "19998", "19999", "20000" }));
        Assert.That(tail[0], Does.Contain("—"));
    }

    [Test]
    public void ShortFile_ReturnsWhatThereIs_WithoutTheBom()
    {
        File.WriteAllText(path, "one\ntwo\n", new UTF8Encoding(true));
        Assert.That(LogTail.ReadLastLines(path, 50), Is.EqualTo(new[] { "one", "two" }));
    }

    [Test]
    public void ReadsWhileAnotherWriterHoldsTheFile()
    {
        using var writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        writer.WriteLine("[9:41:07 PM] >>> cli  account summary");

        Assert.That(LogTail.ReadLastLines(path, 10), Is.EqualTo(new[] { "[9:41:07 PM] >>> cli  account summary" }));
    }

    [Test]
    public void MissingFile_IsEmpty() => Assert.That(LogTail.ReadLastLines(path, 10), Is.Empty);
}
