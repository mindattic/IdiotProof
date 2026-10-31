using IdiotProof.Operations.Gapper;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper caps (IP-T175): active gappers against the 3-cap, one-per-symbol and the auto-gapper limit.</summary>
public class GapperCapsOperationTests
{
    [Test]
    public async Task CountsActiveGappers_FreeSlots_AndAutoArmed()
    {
        using var kit = new GapperKit();
        await kit.AddGapperAsync("AAA", active: true);
        await kit.AddGapperAsync("BBB", active: true, author: GapperAutoRules.AuthorTag);
        await kit.AddGapperAsync("CCC");
        await kit.AddPlainAsync("MSFT", active: true);

        var result = await new GapperCapsOperation().ExecuteAsync(new NoArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("2/3").And.Contain("AAA, BBB").And.Not.Contain("CCC"));
            Assert.That(result.Human, Does.Contain("free slots").And.Contain("ok — no symbol"));
            Assert.That(result.Human, Does.Contain("1 active (auto-gapper arms at most 5 per run)"));
        });
    }

    [Test]
    public async Task AFullCap_SaysToDisarmFirst()
    {
        using var kit = new GapperKit();
        foreach (var s in new[] { "AAA", "BBB", "CCC" }) await kit.AddGapperAsync(s, active: true);
        var result = await new GapperCapsOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(result.Human, Does.Contain("3/3").And.Contain("0 — disarm one"));
    }
}
