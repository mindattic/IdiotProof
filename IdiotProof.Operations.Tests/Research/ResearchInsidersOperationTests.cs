using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research insiders (IP-T232): Form 4 lines for one ticker with buy/sell totals.</summary>
public class ResearchInsidersOperationTests
{
    [Test]
    public async Task ListsLines_TotalsBuysAndSells_AndFiltersByCode()
    {
        var kit = new ResearchKit();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var nvda = await kit.AddClaimAsync("NVDA", "Form 4");
        var amd = await kit.AddClaimAsync("AMD", "Form 4");
        await kit.AddAsync(
            Line(nvda.Id, "Alice", "P", today.AddDays(-3), 1_000, 100m),
            Line(nvda.Id, "Bob", "S", today.AddDays(-5), 500, 110m),
            Line(nvda.Id, "Carol", "A", today.AddDays(-6), 2_000, null),
            Line(amd.Id, "Dave", "P", today.AddDays(-2), 9_999, 10m));

        var all = await new ResearchInsidersOperation().ExecuteAsync(new ResearchInsidersArgs { Symbol = "NVDA" }, kit.Context());
        var sales = await new ResearchInsidersOperation().ExecuteAsync(new ResearchInsidersArgs { Symbol = "NVDA", Code = ["s"] }, kit.Context());

        Assert.That(all.IsOk, Is.True, all.Message);
        Assert.That(all.Human, Does.Contain("Alice").And.Contain("Bob").And.Contain("grant/award").And.Not.Contain("Dave")
            .And.Contain("1 buys $100,000").And.Contain("1 sells $55,000").And.Contain("net +$45,000"));
        Assert.That(sales.Human, Does.Contain("Bob").And.Not.Contain("Alice"));
    }

    private static InsiderTransaction Line(Guid claim, string filer, string code, DateOnly date, decimal shares, decimal? price) => new()
    {
        ClaimId = claim, FilerName = filer, FilerRole = "Officer", TransactionCode = code, TransactionDate = date,
        SharesTransacted = shares, PricePerShare = price, DollarValue = price * shares, SharesOwnedAfter = 10_000,
    };
}
