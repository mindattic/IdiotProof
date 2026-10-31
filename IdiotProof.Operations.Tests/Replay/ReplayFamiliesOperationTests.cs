using IdiotProof.Operations.Replay;

using IdiotProof.Core.Monitoring;
namespace IdiotProof.Operations.Tests.Replay;

/// <summary>replay families (IP-T182): every built-in family, each one resolvable, with a description.</summary>
public class ReplayFamiliesOperationTests
{
    [Test]
    public async Task ListsEveryFamily_WithSessionSideAndDescription()
    {
        using var w = new ReplayWorld();
        var result = await new ReplayFamiliesOperation().ExecuteAsync(new NoArgs(), w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            foreach (var f in StrategyReplay.Families)
            {
                Assert.That(result.Human, Does.Contain(f.Id));
                Assert.That(StrategyReplay.ResolveFamily(f.Id, "TICK"), Is.Not.Null, f.Id);
            }
            Assert.That(result.Human, Does.Contain("Short").And.Contain("RTH").And.Contain("Premarket"));
            Assert.That(result.Human, Does.Contain($"{StrategyReplay.Families.Count} families."));
        });
    }
}
