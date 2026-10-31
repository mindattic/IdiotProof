using IdiotProof.Core.Data;
using IdiotProof.Operations.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Monitor;

/// <summary>monitor tail --follow (IP-T324): new events and progress changes after the snapshot.</summary>
public partial class MonitorOperationTests
{
    [Test]
    public async Task TailFollow_Bounded_ReturnsTheSnapshotThenWhatHappenedInTheWindow()
    {
        var nvda = await AddStrategyAsync("NVDA", 100m);
        await sp.GetRequiredService<ConditionProgressRepository>().UpsertAsync(nvda.Id, 0, 1, "Breakout(100)", lastPrice: 99.5m);

        var tail = new MonitorTailOperation().ExecuteAsync(new MonitorTailArgs { Follow = true, Seconds = 3 }, Ctx());
        await Task.Delay(400);
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            db.AuditLogs.Add(new AuditLog { TimestampUtc = Now, Category = "signal-fire", Message = "[NVDA breakout] fire", UserId = Owner });
            db.AuditLogs.Add(new AuditLog { TimestampUtc = Now, Category = "entry", Message = "someone else's entry", UserId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        await sp.GetRequiredService<ConditionProgressRepository>().UpsertAsync(nvda.Id, 1, 1, null, lastPrice: 100.25m);
        var result = await tail;

        Assert.That(result.IsOk, Is.True, result.Message);
        var following = result.Human![result.Human.IndexOf("Following:", StringComparison.Ordinal)..];
        Assert.That(following, Does.Contain("signal-fire").And.Contain("[NVDA breakout] fire"));
        Assert.That(following, Does.Contain("progress").And.Contain("NVDA 1/1 (all met) @ $100.25"));
        Assert.That(following, Does.Not.Contain("someone else's"));
        Assert.That(result.Human, Does.Contain("a 3s window"));
    }
}
