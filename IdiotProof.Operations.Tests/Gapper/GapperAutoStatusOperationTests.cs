using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Gapper;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper auto status (IP-T169): settings, recent scans and your auto-armed gappers.</summary>
public class GapperAutoStatusOperationTests
{
    internal static async Task<AutoGapperScan> SeedScanAsync(GapperKit kit, string day, DateTime startedUtc, params AutoGapperCandidate[] candidates)
    {
        var scan = new AutoGapperScan
        {
            ScanEtDate = day, Phase = "manual", ScanStartedUtc = startedUtc, ScanCompletedUtc = startedUtc.AddSeconds(20),
            MoversScreened = 50, Qualified = candidates.Length, Armed = candidates.Count(c => c.Armed), Skipped = candidates.Count(c => !c.Armed),
            MinGapPercent = 15, MaxCount = 5, BrokerMode = "paper", Note = "complete",
        };
        await using var db = await kit.Db.CreateDbContextAsync();
        db.AutoGapperScans.Add(scan);
        foreach (var c in candidates) { c.ScanId = scan.Id; c.ScanEtDate = day; c.CapturedUtc = startedUtc; db.AutoGapperCandidates.Add(c); }
        await db.SaveChangesAsync();
        return scan;
    }

    internal static AutoGapperCandidate Candidate(string symbol, int rank, bool armed, string? skip = null) => new()
    {
        Symbol = symbol, Rank = rank, Price = 3.2, GapPercent = 42.5, VolumeRatio = 1.8, AtrPercent = 11, BehaviorClass = "penny",
        StopLossPercent = 11, TrailingStopPercent = 13.8, PeakGivebackPercent = 22, ArmExitEt = "09:10", SellByEt = "09:27",
        Notional = 350m, Armed = armed, SkipReason = skip, StrategyId = armed ? Guid.NewGuid() : null,
    };

    [Test]
    public async Task Status_ShowsSettingsScansAndYourAutoArmedGappers()
    {
        using var kit = new GapperKit();
        await SeedScanAsync(kit, "2026-10-29", new DateTime(2026, 10, 29, 7, 55, 0, DateTimeKind.Utc), Candidate("ABCD", 1, true));
        await kit.AddGapperAsync("ABCD", active: true, author: GapperAutoRules.AuthorTag);
        await kit.AddGapperAsync("OTHR", active: true, author: GapperAutoRules.AuthorTag, owner: Guid.NewGuid());

        var result = await new GapperAutoStatusOperation().ExecuteAsync(new GapperAutoStatusArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("gap ≥15%").And.Contain("paper only").And.Contain("top 5"));
            Assert.That(result.Human, Does.Contain("2026-10-29").And.Contain("3:55 AM"), "scan start shown in ET");
            Assert.That(result.Human, Does.Contain("your auto-armed").And.Contain("ABCD").And.Not.Contain("OTHR"), "owner-scoped");
        });
    }
}
