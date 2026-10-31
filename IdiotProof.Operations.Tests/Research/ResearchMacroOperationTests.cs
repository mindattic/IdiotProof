using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research macro (IP-T230): macro/regulatory claims with the tickers they affect.</summary>
public class ResearchMacroOperationTests
{
    [Test]
    public async Task ListsMacroClaims_AndFiltersByAffectedTicker()
    {
        var kit = new ResearchKit();
        await kit.AddClaimAsync("NVDA", "Company claim");
        await kit.AddClaimAsync(summary: "Nasdaq bid-price rule", macro: true, affects: ["SOUN", "BBAI"], type: "Regulatory");
        await kit.AddClaimAsync(summary: "Tariff notice", macro: true, affects: ["F"], type: "Regulatory");

        var all = await new ResearchMacroOperation().ExecuteAsync(new ResearchMacroArgs(), kit.Context());
        var soun = await new ResearchMacroOperation().ExecuteAsync(new ResearchMacroArgs { Affects = "soun" }, kit.Context());

        Assert.That(all.IsOk, Is.True, all.Message);
        Assert.That(all.Human, Does.Contain("Nasdaq bid-price rule").And.Contain("SOUN,BBAI").And.Contain("Tariff notice")
            .And.Not.Contain("Company claim"));
        Assert.That(soun.Human, Does.Contain("Nasdaq bid-price rule").And.Not.Contain("Tariff notice"));
    }
}
