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

/// <summary>monitor pause|resume|tick-once (IP-T257).</summary>
public partial class MonitorOperationTests
{
    // ── IP-T257 ─────────────────────────────────────────────────────────

    [Test]
    public async Task Pause_SetsTheFlag_AndWarnsAboutUnmanagedPositions()
    {
        await AddStrategyAsync("NVDA", 100m, position: 5);

        var result = await new MonitorPauseOperation().ExecuteAsync(new MonitorPauseArgs { Reason = "fomc" }, Ctx());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(await sp.GetRequiredService<SettingsRepository>().GetAsync(MonitorPauseFlag.PausedKey), Is.EqualTo("true"));
        Assert.That(result.Human, Does.Contain("NOT managed").And.Contain("NVDA"));

        var status = await new MonitorStatusOperation().ExecuteAsync(new NoArgs(), Ctx());
        Assert.That(status.Human, Does.Contain("Paused:").And.Contain("fomc"));
    }

    [Test]
    public async Task Resume_PaperOnly_ClearsTheFlagWithoutACode()
    {
        await AddStrategyAsync("NVDA", 100m);
        await new MonitorPauseOperation().ExecuteAsync(new MonitorPauseArgs(), Ctx());

        var result = await new MonitorResumeOperation().ExecuteAsync(new NoArgs(), Ctx());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(await sp.GetRequiredService<SettingsRepository>().GetAsync(MonitorPauseFlag.PausedKey), Is.EqualTo("false"));
    }

    [Test]
    public async Task Resume_WithALiveStrategy_NeedsACode_ThenResumes()
    {
        await AddStrategyAsync("NVDA", 100m, mode: "Live");
        await new MonitorPauseOperation().ExecuteAsync(new MonitorPauseArgs(), Ctx());

        var first = await new MonitorResumeOperation().ExecuteAsync(new NoArgs(), Ctx());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        Assert.That(await sp.GetRequiredService<SettingsRepository>().GetAsync(MonitorPauseFlag.PausedKey), Is.EqualTo("true"));

        var second = await new MonitorResumeOperation().ExecuteAsync(new NoArgs(), Ctx(first.Confirmation!.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        Assert.That(await sp.GetRequiredService<SettingsRepository>().GetAsync(MonitorPauseFlag.PausedKey), Is.EqualTo("false"));
    }

    [Test]
    public async Task TickOnce_Dry_ShowsVerdicts_AndWritesNothing()
    {
        await AddStrategyAsync("NVDA", 100m);           // close 105 >= 100: would fire
        await AddStrategyAsync("AMD", 200m);            // close 105 < 200: waiting
        await AddStrategyAsync("TSLA", 100m, position: 2);

        var result = await new MonitorTickOnceOperation().ExecuteAsync(new MonitorTickOnceArgs { Dry = true }, Ctx());

        Assert.That(result.IsOk, Is.True, result.Message);
        var lines = result.Human!.Split('\n');
        Assert.That(lines[0], Does.Contain("3 strategies, 1 would fire. Nothing was ordered or written."));
        Assert.That(lines.Single(l => l.StartsWith("NVDA")), Does.Contain("would-fire"));
        Assert.That(lines.Single(l => l.StartsWith("AMD")), Does.Contain("waiting"));
        Assert.That(lines.Single(l => l.StartsWith("TSLA")), Does.Contain("holding"));

        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.That(await db.AuditLogs.CountAsync(), Is.Zero);
        Assert.That(await db.ConditionProgress.CountAsync(), Is.Zero);
        Assert.That(await db.Strategies.SumAsync(s => s.PositionQty), Is.EqualTo(2), "no position changed");
    }

    [Test]
    public async Task TickOnce_NotDry_NotesThePassInTheAuditLog()
    {
        await AddStrategyAsync("NVDA", 100m);

        var result = await new MonitorTickOnceOperation().ExecuteAsync(new MonitorTickOnceArgs(), Ctx());

        Assert.That(result.IsOk, Is.True, result.Message);
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var row = await db.AuditLogs.SingleAsync();
        Assert.That(row.Category, Is.EqualTo("monitor-tick-once"));
        Assert.That(row.Message, Does.Contain("NVDA would-fire"));
    }
}
