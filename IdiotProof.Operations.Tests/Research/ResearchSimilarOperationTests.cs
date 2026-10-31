using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research similar (IP-T237): LSH neighbours nearest first, with their outcomes.</summary>
public class ResearchSimilarOperationTests
{
    [Test]
    public async Task ListsNeighboursNearestFirst_WithinTheDistance()
    {
        var kit = new ResearchKit();
        var target = await kit.AddClaimAsync("NVDA", "Target claim");
        var near = await kit.AddClaimAsync("AMD", "Near twin", outcomePct: 6m);
        var nearer = await kit.AddClaimAsync("INTC", "Nearest twin", outcomePct: -2m);
        var far = await kit.AddClaimAsync("F", "Far away");
        await kit.AddAsync(
            Vector(target.Id, 0x00), Vector(near.Id, 0x07), Vector(nearer.Id, 0x01), Vector(far.Id, 0xFF, allBytes: true));

        var result = await new ResearchSimilarOperation().ExecuteAsync(new ResearchSimilarArgs { Claim = target.Id.ToString("N")[..8] }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var human = result.Human!;
        Assert.That(human.IndexOf("Nearest twin"), Is.LessThan(human.IndexOf("Near twin")));
        Assert.That(human, Does.Not.Contain("Far away"));
        Assert.That(human, Does.Contain("2 of 2 look-alikes have outcomes: 1 rose, 1 fell, average +2.00%"));
    }

    [Test]
    public async Task NoVector_SaysSo()
    {
        var kit = new ResearchKit();
        var c = await kit.AddClaimAsync();
        var result = await new ResearchSimilarOperation().ExecuteAsync(new ResearchSimilarArgs { Claim = c.Id.ToString() }, kit.Context());
        Assert.That(result.Human, Does.Contain("no similarity vector yet"));
    }

    private static ResearchClaimVector Vector(Guid id, byte first, bool allBytes = false)
    {
        var sig = new byte[8];
        for (var i = 0; i < 8; i++) sig[i] = allBytes || i == 0 ? first : (byte)0;
        return new ResearchClaimVector { ClaimId = id, LshSignature = sig };
    }
}
