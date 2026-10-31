using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>replay regen (IP-T181): the whole archive's HTML rebuilt from SQL into a folder; nothing published.</summary>
public class ReplayRegenOperationTests
{
    [Test]
    public async Task RewritesEveryRunPageAndIndex_FromSql()
    {
        using var w = new ReplayWorld();
        var a = await ReplayListOperationTests.SeedRunAsync(w, "NVDA", "2026-10-29", fired: true, pnl: 2);
        var b = await ReplayListOperationTests.SeedRunAsync(w, "TSLA", "2026-10-29", fired: false, pnl: 0);

        var result = await new ReplayRegenOperation().ExecuteAsync(new ReplayRegenArgs { Out = w.Pages }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("2 run page(s) across 2 ticker(s)").And.Contain("Nothing was published"));
            Assert.That(File.Exists(Path.Combine(w.Pages, "nvda", a.Stamp, "index.htm")), Is.True);
            Assert.That(File.Exists(Path.Combine(w.Pages, "tsla", b.Stamp, "index.htm")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(w.Pages, "index.htm")), Does.Contain("NVDA").And.Contain("TSLA"));
            Assert.That(File.ReadAllText(Path.Combine(w.Pages, "nvda", "index.htm")), Does.Contain("FIRED"));
        });
    }
}
