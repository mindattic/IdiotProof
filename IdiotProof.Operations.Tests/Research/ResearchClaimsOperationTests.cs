using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research claims (IP-T227): the ranked feed over EF InMemory.</summary>
public class ResearchClaimsOperationTests
{
    [Test]
    public async Task Feed_RanksByScore_UnscoredLast_AndRespectsSince()
    {
        var kit = new ResearchKit();
        await kit.AddClaimAsync("AMD", "Low scorer", score: 20);
        await kit.AddClaimAsync("NVDA", "High scorer", score: 90);
        await kit.AddClaimAsync("TSLA", "Not scored yet", score: null);
        await kit.AddClaimAsync("OLD", "Too old", score: 99, createdUtc: DateTime.UtcNow.AddDays(-40));

        var result = await new ResearchClaimsOperation().ExecuteAsync(new ResearchClaimsArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var human = result.Human!;
        Assert.That(human.IndexOf("High scorer"), Is.LessThan(human.IndexOf("Low scorer")));
        Assert.That(human.IndexOf("Low scorer"), Is.LessThan(human.IndexOf("Not scored yet")));
        Assert.That(human, Does.Not.Contain("Too old"));
        Assert.That(human, Does.Contain("No scan has run yet"));
    }

    [Test]
    public async Task TickerFilter_KeepsMacroClaimsThatNameIt()
    {
        var kit = new ResearchKit();
        await kit.AddClaimAsync("NVDA", "NVDA contract");
        await kit.AddClaimAsync("AMD", "AMD contract");
        await kit.AddClaimAsync(summary: "Listing rule change", macro: true, affects: ["NVDA", "SOUN"], type: "Regulatory");
        await kit.AddClaimAsync(summary: "Unrelated rule", macro: true, affects: ["XYZ"], type: "Regulatory");

        var result = await new ResearchClaimsOperation().ExecuteAsync(new ResearchClaimsArgs { Ticker = "nvda" }, kit.Context());

        Assert.That(result.Human, Does.Contain("NVDA contract").And.Contain("Listing rule change").And.Contain("(macro) 2 tickers"));
        Assert.That(result.Human, Does.Not.Contain("AMD contract").And.Not.Contain("Unrelated rule"));
    }

    [Test]
    public async Task MinScore_Macro_AndPortents_Filter()
    {
        var kit = new ResearchKit();
        await kit.AddClaimAsync("NVDA", "Strong portent", score: 75, happened: false, trigger: "signature");
        await kit.AddClaimAsync("NVDA", "Strong event", score: 80);
        await kit.AddClaimAsync("NVDA", "Weak portent", score: 30, happened: false);
        await kit.AddClaimAsync(summary: "Macro thing", macro: true, affects: ["NVDA"], score: 70);

        var portents = await new ResearchClaimsOperation().ExecuteAsync(new ResearchClaimsArgs { MinScore = 60, Portents = true }, kit.Context());
        var macro = await new ResearchClaimsOperation().ExecuteAsync(new ResearchClaimsArgs { Macro = true }, kit.Context());

        Assert.That(portents.Human, Does.Contain("Strong portent").And.Not.Contain("Strong event").And.Not.Contain("Weak portent"));
        Assert.That(macro.Human, Does.Contain("Macro thing").And.Not.Contain("Strong event"));
    }

    [Test]
    public async Task BadLimit_IsAUsageError()
    {
        var result = await new ResearchClaimsOperation().ExecuteAsync(new ResearchClaimsArgs { Limit = 0 }, new ResearchKit().Context());
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
