using IdiotProof.Core.Data;
using IdiotProof.Operations.Gapper;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper auto candidates (IP-T171): one scan day's screened tickers, plans and outcomes.</summary>
public class GapperAutoCandidatesOperationTests
{
    [Test]
    public async Task Candidates_DefaultsToTheLatestScanDay_AndShowsOutcomes()
    {
        using var kit = new GapperKit();
        await GapperAutoStatusOperationTests.SeedScanAsync(kit, "2026-10-28", new DateTime(2026, 10, 28, 7, 55, 0, DateTimeKind.Utc), GapperAutoStatusOperationTests.Candidate("OLD", 1, true));
        await GapperAutoStatusOperationTests.SeedScanAsync(kit, "2026-10-29", new DateTime(2026, 10, 29, 7, 55, 0, DateTimeKind.Utc),
            GapperAutoStatusOperationTests.Candidate("ABCD", 1, true), GapperAutoStatusOperationTests.Candidate("EFGH", 2, false, "duplicate-active"));

        var latest = await new GapperAutoCandidatesOperation().ExecuteAsync(new GapperAutoCandidatesArgs(), kit.Context());
        var older = await new GapperAutoCandidatesOperation().ExecuteAsync(new GapperAutoCandidatesArgs { Date = new DateOnly(2026, 10, 28) }, kit.Context());

        Assert.That(latest.IsOk, Is.True, latest.Message);
        Assert.Multiple(() =>
        {
            Assert.That(latest.Human, Does.Contain("2026-10-29").And.Contain("ABCD").And.Contain("ARMED").And.Contain("skip: duplicate-active").And.Not.Contain("OLD"));
            Assert.That(latest.Human, Does.Contain("+42.5%").And.Contain("22% @09:10"));
            Assert.That(older.Human, Does.Contain("OLD").And.Not.Contain("ABCD"));
        });
    }
}
