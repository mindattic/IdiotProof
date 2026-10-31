using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Audit;
using IdiotProof.Operations.Tests.Diary;

namespace IdiotProof.Operations.Tests.Audit;

/// <summary>ledger list (IP-T245): the owner's command ledger, filtered by operation, noun, date, caller and status.</summary>
public class LedgerListOperationTests
{
    private static async Task<DiaryKit> SeedAsync()
    {
        var kit = new DiaryKit();
        await kit.AddAsync(
            Entry(kit.Owner, "strategy create", "cli", "Ok", DiaryKit.Et(2026, 10, 29, 9, 0), "{\"text\":\"buy NVDA\"}"),
            Entry(kit.Owner, "strategy list", "mcp", "Ok", DiaryKit.Et(2026, 10, 30, 9, 0)),
            Entry(kit.Owner, "order place", "mcp", "Blocked", DiaryKit.Et(2026, 10, 30, 9, 30), message: "Risk Guardian: daily loss limit"),
            Entry(Guid.NewGuid(), "order place", "cli", "Ok", DiaryKit.Et(2026, 10, 30, 9, 31)));
        return kit;
    }

    [Test]
    public async Task ListsTheOwnersCommands_NewestFirst()
    {
        var kit = await SeedAsync();

        var result = await new LedgerListOperation().ExecuteAsync(new LedgerListArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var human = result.Human!;
        Assert.That(human.IndexOf("order place"), Is.LessThan(human.IndexOf("strategy list")));
        Assert.That(human, Does.Contain("Risk Guardian: daily loss limit").And.Contain("{\"text\":\"buy NVDA\"}")
            .And.Contain("2026-10-30 9:30 AM ET").And.Contain("3 commands, 1 not ok."));
    }

    [Test]
    public async Task FiltersByOpNounCallerStatusAndSince()
    {
        var kit = await SeedAsync();

        var noun = await new LedgerListOperation().ExecuteAsync(new LedgerListArgs { Op = "strategy" }, kit.Context());
        var tool = await new LedgerListOperation().ExecuteAsync(new LedgerListArgs { Op = "strategy_create" }, kit.Context());
        var blocked = await new LedgerListOperation().ExecuteAsync(new LedgerListArgs { Caller = "MCP", Status = "blocked" }, kit.Context());
        var since = await new LedgerListOperation().ExecuteAsync(new LedgerListArgs { Since = new DateOnly(2026, 10, 30) }, kit.Context());
        var bad = await new LedgerListOperation().ExecuteAsync(new LedgerListArgs { Caller = "fax" }, kit.Context());

        Assert.That(noun.Human, Does.Contain("strategy create").And.Contain("strategy list").And.Not.Contain("order place"));
        Assert.That(tool.Human, Does.Contain("strategy create").And.Not.Contain("strategy list"));
        Assert.That(blocked.Human, Does.Contain("order place").And.Contain("1 commands"));
        Assert.That(since.Human, Does.Contain("2 commands"));
        Assert.That(bad.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    private static CommandLedgerEntry Entry(Guid owner, string op, string caller, string status, DateTime utc, string? args = null, string? message = null) => new()
    {
        OwnerUserId = owner, Operation = op, Caller = caller, Status = status, TimestampUtc = utc, ArgsJson = args,
        Message = message, ExitCode = status == "Ok" ? 0 : 3, DurationMs = 12,
    };
}
