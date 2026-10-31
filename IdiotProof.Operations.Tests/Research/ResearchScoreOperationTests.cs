using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research score (IP-T235): the SignificanceScorer terms, recomputed and explained.</summary>
public class ResearchScoreOperationTests
{
    [Test]
    public async Task ExplainsEveryTerm_AndTheFormula()
    {
        var kit = new ResearchKit();
        // Medium (60) × Low confidence (0.4) = 24; source right 80% → +6; on a watchlist → +8; 16 days old → ×0.5.
        var claim = await kit.AddClaimAsync("NVDA", "Pending supply deal", score: 33, magnitude: "Medium", happened: false,
            createdUtc: DateTime.UtcNow.AddDays(-16), source: "Reuters");
        await SetConfidence(kit, claim.Id, "Low");
        await kit.AddAsync(
            new SourceTrustScore { SourceName = "Reuters", TotalClaims = 10, PortentsRealized = 8 },
            new WorkspaceRow { WorkspaceId = "w1", Name = "Main", BodyJson = """{"Watchlist":["nvda"]}""" });

        var result = await new ResearchScoreOperation().ExecuteAsync(new ResearchClaimRefArgs { Claim = claim.Id.ToString() }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Medium (High 100, Medium 60, Low 20)").And.Contain("×0.40")
            .And.Contain("right 80%").And.Contain("on a watchlist").And.Contain("×0.50")
            .And.Contain("= 19.0").And.Contain("Stored score 33.0"));
    }

    private static async Task SetConfidence(ResearchKit kit, Guid id, string confidence)
    {
        await using var db = await kit.Db.CreateDbContextAsync();
        (await db.ResearchClaims.FindAsync(id))!.TriggerConfidence = confidence;
        await db.SaveChangesAsync();
    }
}
