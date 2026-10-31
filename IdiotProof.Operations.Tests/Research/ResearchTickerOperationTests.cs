using IdiotProof.Core.Data;
using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research ticker (IP-T229): one ticker's universe row, claims, portents, macro, insiders and outcomes.</summary>
public class ResearchTickerOperationTests
{
    [Test]
    public async Task GathersEverythingAboutOneTicker()
    {
        var kit = new ResearchKit();
        await kit.AddAsync(new TrackedTicker { Symbol = "NVDA", Exchange = "NASDAQ", IsTradable = true, LastPrice = 100m, SharesOutstanding = 1_000 });
        var c1 = await kit.AddClaimAsync("NVDA", "Beat earnings", outcomePct: 4m);
        await kit.AddClaimAsync("NVDA", "Pending fab deal", happened: false, trigger: "signature", sentiment: "Bullish");
        await kit.AddClaimAsync("AMD", "Someone else");
        await kit.AddClaimAsync(summary: "Chip export rule", macro: true, affects: ["NVDA", "AMD"]);
        await kit.AddAsync(new InsiderTransaction
        {
            ClaimId = c1.Id, FilerName = "CEO", FilerRole = "Officer", TransactionCode = "S",
            TransactionDate = new DateOnly(2026, 10, 1), SharesTransacted = 100, DollarValue = 10_000m,
        });

        var result = await new ResearchTickerOperation().ExecuteAsync(new ResearchTickerArgs { Symbol = "nvda" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("NASDAQ").And.Contain("last $100.00").And.Contain("mkt cap $100,000")
            .And.Contain("2 in 90d").And.Contain("1 pending portents").And.Contain("average +4.00%")
            .And.Contain("$10,000 sold").And.Contain("Chip export rule").And.Contain("Pending fab deal")
            .And.Not.Contain("Someone else"));
    }

    [Test]
    public async Task UnknownTicker_SuggestsAScan()
    {
        var result = await new ResearchTickerOperation().ExecuteAsync(new ResearchTickerArgs { Symbol = "ZZZZ" }, new ResearchKit().Context());
        Assert.That(result.Human, Does.Contain("research scan --tickers ZZZZ").And.Contain("not in the tracked universe"));
    }
}
