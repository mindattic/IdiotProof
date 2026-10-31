using IdiotProof.Core.Data;
using IdiotProof.Operations.Research;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>research backfill (IP-T234): OutcomeBackfillService over scripted Alpaca daily bars.</summary>
public class ResearchBackfillOperationTests
{
    [Test]
    public async Task SettlesOldPortents_FromDailyCloses_AndCreditsTheSource()
    {
        var kit = new ResearchKit();
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);
        var claim = await kit.AddClaimAsync("NVDA", "Pending fab deal", happened: false, articleDate: day, source: "Reuters");
        await kit.AddClaimAsync("AMD", "Too recent", articleDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        await kit.AddAsync(new SourceTrustScore { SourceName = "Reuters", SourceTier = 2, TotalClaims = 2, PortentsClaimed = 1 });
        var bars = "{\"bars\":[{\"t\":\"" + day.ToString("yyyy-MM-dd") + "T04:00:00Z\",\"c\":100},{\"t\":\""
            + day.AddDays(7).ToString("yyyy-MM-dd") + "T04:00:00Z\",\"c\":110}]}";
        kit.Http.On("GET /v2/stocks/NVDA/bars", bars);

        var result = await new ResearchBackfillOperation().ExecuteAsync(new ResearchBackfillArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Backfilled 1 of 1 waiting claims").And.Contain("1 realized").And.Contain("+10.0%"));
        await using var db = await kit.Db.CreateDbContextAsync();
        var saved = await db.ResearchClaims.SingleAsync(c => c.Id == claim.Id);
        Assert.That(saved.Status, Is.EqualTo("Realized"));
        Assert.That(saved.OutcomePctChange, Is.EqualTo(10m));
        Assert.That((await db.SourceTrustScores.SingleAsync()).PortentsRealized, Is.EqualTo(1));
        Assert.That(kit.Http.Requests.Select(r => r.Uri.AbsolutePath), Is.EqualTo(new[] { "/v2/stocks/NVDA/bars" }));
    }

    [Test]
    public async Task NothingOldEnough_SaysSo()
    {
        var kit = new ResearchKit();
        await kit.AddClaimAsync("AMD", "Too recent", articleDate: DateOnly.FromDateTime(DateTime.UtcNow));
        var result = await new ResearchBackfillOperation().ExecuteAsync(new ResearchBackfillArgs(), kit.Context());
        Assert.That(result.Human, Does.Contain("Nothing to backfill"));
        Assert.That(kit.Http.Requests, Is.Empty);
    }
}
