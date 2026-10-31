using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Audit;
using IdiotProof.Operations.Tests.Diary;

namespace IdiotProof.Operations.Tests.Audit;

/// <summary>audit list and audit search (IP-T243): the owner's rows plus system rows, newest first.</summary>
public class AuditOperationsTests
{
    private static async Task<DiaryKit> SeedAsync()
    {
        var kit = new DiaryKit();
        await kit.AddAsync(
            Row(kit.Owner, "signal-fire", "[Gap] NVDA fired at $120.00", DiaryKit.Et(2026, 10, 30, 9, 35)),
            Row(kit.Owner, "order-rejected", "[Gap] AMD order rejected", DiaryKit.Et(2026, 10, 30, 9, 40), "{\"reason\":\"insufficient buying power\"}"),
            Row(null, "system", "Monitor started", DiaryKit.Et(2026, 10, 30, 9, 0)),
            Row(Guid.NewGuid(), "signal-fire", "[Theirs] TSLA fired", DiaryKit.Et(2026, 10, 30, 9, 45)),
            Row(kit.Owner, "entry", "[Gap] NVDA entry filled", DiaryKit.Et(2026, 10, 28, 9, 36)));
        return kit;
    }

    [Test]
    public async Task List_ShowsMineAndSystem_NewestFirst_NeverOtherUsers()
    {
        var kit = await SeedAsync();

        var result = await new AuditListOperation().ExecuteAsync(new AuditListArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var human = result.Human!;
        Assert.That(human.IndexOf("AMD order rejected"), Is.LessThan(human.IndexOf("NVDA fired")));
        Assert.That(human, Does.Contain("Monitor started").And.Contain("system*").And.Contain("2026-10-30 9:35 AM ET")
            .And.Not.Contain("TSLA"));
    }

    [Test]
    public async Task List_FiltersByKindSinceAndSystem()
    {
        var kit = await SeedAsync();

        var kinds = await new AuditListOperation().ExecuteAsync(new AuditListArgs { Kind = ["signal-fire", "ENTRY"] }, kit.Context());
        var since = await new AuditListOperation().ExecuteAsync(new AuditListArgs { Since = new DateOnly(2026, 10, 30), System = false }, kit.Context());

        Assert.That(kinds.Human, Does.Contain("NVDA fired").And.Contain("entry filled").And.Not.Contain("rejected").And.Not.Contain("Monitor"));
        Assert.That(since.Human, Does.Contain("NVDA fired").And.Not.Contain("entry filled").And.Not.Contain("Monitor started"));
    }

    [Test]
    public async Task Search_MatchesMessageOrPayload_CaseInsensitive()
    {
        var kit = await SeedAsync();

        var byPayload = await new AuditSearchOperation().ExecuteAsync(new AuditSearchArgs { Text = "Buying Power" }, kit.Context());
        var byMessage = await new AuditSearchOperation().ExecuteAsync(new AuditSearchArgs { Text = "nvda" }, kit.Context());
        var tooShort = await new AuditSearchOperation().ExecuteAsync(new AuditSearchArgs { Text = "x" }, kit.Context());

        Assert.That(byPayload.Human, Does.Contain("AMD order rejected").And.Contain("1 matched only in the payload"));
        Assert.That(byMessage.Human, Does.Contain("NVDA fired").And.Contain("entry filled").And.Not.Contain("TSLA"));
        Assert.That(tooShort.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    internal static AuditLog Row(Guid? user, string kind, string message, DateTime utc, string? data = null) =>
        new() { UserId = user, Category = kind, Message = message, TimestampUtc = utc, DataJson = data };
}
