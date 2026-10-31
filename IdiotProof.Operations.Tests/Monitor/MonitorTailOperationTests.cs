using System.Runtime.CompilerServices;
using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Storage;
using IdiotProof.Models;
using IdiotProof.Operations.Monitor;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using IdiotProof.Core.Services;
using IdiotProof.Core.Monitoring;
namespace IdiotProof.Operations.Tests.Monitor;

/// <summary>monitor tail (IP-T258).</summary>
public partial class MonitorOperationTests
{
    // ── IP-T258 ─────────────────────────────────────────────────────────

    [Test]
    public async Task Tail_ShowsRecentVerdictsInOrder_AndProgress()
    {
        var nvda = await AddStrategyAsync("NVDA", 100m);
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            db.AuditLogs.AddRange(
                new AuditLog { TimestampUtc = Now.AddMinutes(-5), Category = "signal-fire", Message = "[NVDA breakout] fire", UserId = Owner },
                new AuditLog { TimestampUtc = Now.AddMinutes(-4), Category = "signal-vetoed", Message = "[NVDA breakout] LLM veto", UserId = Owner },
                new AuditLog { TimestampUtc = Now.AddMinutes(-3), Category = "signal-fire", Message = "someone else's fire", UserId = Guid.NewGuid() },
                new AuditLog { TimestampUtc = Now.AddMinutes(-90), Category = "entry", Message = "too old", UserId = Owner },
                new AuditLog { TimestampUtc = Now.AddMinutes(-2), Category = "settings-changed", Message = "not a monitor event", UserId = Owner });
            await db.SaveChangesAsync();
        }
        await sp.GetRequiredService<ConditionProgressRepository>().UpsertAsync(nvda.Id, 0, 1, "Breakout(100)", lastPrice: 99.5m);

        var result = await new MonitorTailOperation().ExecuteAsync(new MonitorTailArgs(), Ctx());

        Assert.That(result.IsOk, Is.True, result.Message);
        var text = result.Human!;
        Assert.That(text.IndexOf("fire", StringComparison.Ordinal), Is.LessThan(text.IndexOf("LLM veto", StringComparison.Ordinal)), "oldest first");
        Assert.That(text, Does.Not.Contain("someone else's").And.Not.Contain("too old").And.Not.Contain("not a monitor event"));
        Assert.That(text, Does.Contain("Live condition progress:").And.Contain("Breakout(100)").And.Contain("$99.50"));
    }

    [Test]
    public async Task Tail_RejectsOutOfRangeLines()
    {
        var result = await new MonitorTailOperation().ExecuteAsync(new MonitorTailArgs { Lines = 0 }, Ctx());
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
