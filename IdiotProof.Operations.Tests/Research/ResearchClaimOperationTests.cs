using IdiotProof.Core.Data;
using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research claim (IP-T228): one claim in full, found by id prefix.</summary>
public class ResearchClaimOperationTests
{
    [Test]
    public async Task ShowsSourceTriggerInsidersAndScoreBreakdown()
    {
        var kit = new ResearchKit();
        var claim = await kit.AddClaimAsync("NVDA", "CFO bought 10,000 shares", source: "SEC EDGAR Form 4", happened: false,
            trigger: "Board approval", type: "Insider");
        await kit.AddAsync(
            new InsiderTransaction
            {
                ClaimId = claim.Id, FilerName = "Jane Doe", FilerRole = "Officer", TransactionCode = "P",
                TransactionDate = new DateOnly(2026, 10, 28), SharesTransacted = 10_000, PricePerShare = 120m,
                DollarValue = 1_200_000m, SharesOwnedAfter = 50_000, PctOfHoldingsChanged = 25m,
            },
            new SourceTrustScore { SourceName = "SEC EDGAR Form 4", SourceTier = 1, TotalClaims = 10, PortentsRealized = 6, ImmediateCorrect = 2 });

        var result = await new ResearchClaimOperation().ExecuteAsync(
            new ResearchClaimRefArgs { Claim = claim.Id.ToString("N")[..8] }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain(claim.Id.ToString()).And.Contain("portent (announced, not yet happened)")
            .And.Contain("Board approval").And.Contain("80% right over 10 claims")
            .And.Contain("Jane Doe").And.Contain("purchase").And.Contain("$1,200,000")
            .And.Contain("50.0 stored; now").And.Contain("magnitude"));
    }

    [Test]
    public async Task UnknownOrAmbiguousIds_AreUsageErrors()
    {
        var kit = new ResearchKit();
        await kit.AddClaimAsync();

        var none = await Run(kit, "ffffffff");
        var junk = await Run(kit, "nope");

        Assert.That(none.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(junk.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    private static async Task<OperationResult> Run(ResearchKit kit, string id)
    {
        try { return await new ResearchClaimOperation().ExecuteAsync(new ResearchClaimRefArgs { Claim = id }, kit.Context()); }
        catch (OperationException ex) { return ex.ToResult(); }
    }
}
