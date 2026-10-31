using IdiotProof.Operations.Research;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research index-events (IP-T231): S&amp;P adds/removes, pending first.</summary>
public class ResearchIndexEventsOperationTests
{
    [Test]
    public async Task PendingEventsComeFirst_WithEffectiveDates()
    {
        var kit = new ResearchKit();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var done = await kit.AddClaimAsync("OLDCO", "Removed from the S&P 500 (effective 2026-09-22)", type: "IndexEvent",
            sentiment: "Bearish", articleDate: today.AddDays(-40));
        var pending = await kit.AddClaimAsync("DDOG", "Added to the S&P 500 (effective 2026-11-20)", type: "IndexEvent",
            happened: false, articleDate: today.AddDays(-2));
        await SetTimeline(kit, done.Id, "2026-09-22");
        await SetTimeline(kit, pending.Id, "2026-11-20");
        await kit.AddClaimAsync("NVDA", "Not an index event");

        var result = await new ResearchIndexEventsOperation().ExecuteAsync(new ResearchIndexEventsArgs(), kit.Context());
        var onlyPending = await new ResearchIndexEventsOperation().ExecuteAsync(new ResearchIndexEventsArgs { Pending = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var human = result.Human!;
        Assert.That(human.IndexOf("DDOG"), Is.LessThan(human.IndexOf("OLDCO")));
        Assert.That(human, Does.Contain("2026-11-20").And.Contain("pending").And.Contain("remove").And.Not.Contain("NVDA"));
        Assert.That(human, Does.Contain("1 pending, 1 effective"));
        Assert.That(onlyPending.Human, Does.Contain("DDOG").And.Not.Contain("OLDCO"));
    }

    private static async Task SetTimeline(ResearchKit kit, Guid id, string date)
    {
        await using var db = await kit.Db.CreateDbContextAsync();
        (await db.ResearchClaims.FindAsync(id))!.ExpectedTimeline = date;
        await db.SaveChangesAsync();
    }
}
