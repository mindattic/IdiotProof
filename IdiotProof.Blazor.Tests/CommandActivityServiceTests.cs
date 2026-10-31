using IdiotProof.Core.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The Activity page's Commands list shows the signed-in owner's CLI/MCP ledger rows, newest first,
/// never another owner's, web rows only when asked, and nothing when nobody is signed in.
/// </summary>
[TestFixture]
public sealed class CommandActivityServiceTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static async Task<CommandLedgerRepository> SeededLedgerAsync()
    {
        var factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"ledger-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        var ledger = new CommandLedgerRepository(factory, NullLogger<CommandLedgerRepository>.Instance);
        var t0 = new DateTime(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc);

        await ledger.AppendAsync(Row(Alice, "strategy list", "cli", t0));
        await ledger.AppendAsync(Row(Alice, "orders place", "mcp", t0.AddMinutes(1), """{"symbol":"NVDA","api_key":"sk-secret"}"""));
        await ledger.AppendAsync(Row(Alice, "account show", "blazor", t0.AddMinutes(2)));
        await ledger.AppendAsync(Row(Bob, "orders cancel", "cli", t0.AddMinutes(3)));
        return ledger;
    }

    private static CommandLedgerEntry Row(Guid owner, string op, string caller, DateTime at, string? args = null) => new()
    {
        OwnerUserId = owner, Operation = op, Caller = caller, TimestampUtc = at, Status = "Ok", ArgsJson = args,
    };

    [Test]
    public async Task ShowsTheOwnersCliAndMcpRows_NewestFirst_NeverAnotherOwners()
    {
        var service = new CommandActivityService(await SeededLedgerAsync(), new FixedOwnerContext(Alice));

        var rows = await service.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.Operation), Is.EqualTo(new[] { "orders place", "strategy list" }));
            Assert.That(rows.All(r => r.OwnerUserId == Alice), Is.True);
            Assert.That(rows[0].ArgsJson, Does.Not.Contain("sk-secret"), "arguments are shown redacted");
        });
    }

    [Test]
    public async Task IncludeAllCallers_AddsWebRows()
    {
        var service = new CommandActivityService(await SeededLedgerAsync(), new FixedOwnerContext(Alice));

        var rows = await service.LoadAsync(includeAllCallers: true);

        Assert.That(rows.Select(r => r.Operation), Is.EqualTo(new[] { "account show", "orders place", "strategy list" }));
    }

    [Test]
    public async Task NobodySignedIn_ShowsNothing()
    {
        var service = new CommandActivityService(await SeededLedgerAsync(), new FixedOwnerContext(null));

        Assert.That(await service.LoadAsync(includeAllCallers: true), Is.Empty);
    }

    [Test]
    public void Labels_AndLevels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CommandActivityService.CallerLabel("cli"), Is.EqualTo("CLI"));
            Assert.That(CommandActivityService.CallerLabel("mcp"), Is.EqualTo("MCP"));
            Assert.That(CommandActivityService.CallerLabel("blazor"), Is.EqualTo("Web"));
            Assert.That(CommandActivityService.StatusLevel("Ok"), Is.EqualTo("INFO"));
            Assert.That(CommandActivityService.StatusLevel("ConfirmationRequired"), Is.EqualTo("WARN"));
            Assert.That(CommandActivityService.StatusLevel("Failed"), Is.EqualTo("ERROR"));
        });
    }
}
