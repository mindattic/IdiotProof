using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T80: the Monitor's pause switch is one SettingsKv row read at the top of every tick, cached
/// for two seconds so a 1 s tick does not query SQL every pass, and visible to every process that
/// shares the database (a pause from the CLI reaches the Hub's loop).
/// </summary>
[TestFixture]
public sealed class MonitorPauseFlagTests
{
    private FakeTimeProvider clock = null!;
    private SettingsRepository settings = null!;

    [SetUp]
    public void SetUp()
    {
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 14, 0, 0, TimeSpan.Zero));
        var factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"pause-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        settings = new SettingsRepository(factory);
    }

    [Test]
    public async Task NoRow_MeansNotPaused()
    {
        Assert.That(await new MonitorPauseFlag(settings, clock).IsPausedAsync(), Is.False);
    }

    [Test]
    public async Task SetAsync_WritesTheRow_AndTheReason()
    {
        var flag = new MonitorPauseFlag(settings, clock);
        await flag.SetAsync(true, "paused by cli");

        Assert.That(await settings.GetAsync(MonitorPauseFlag.PausedKey), Is.EqualTo("true"));
        Assert.That(await flag.ReadAsync(), Is.EqualTo((true, "paused by cli")));
        Assert.That(await flag.IsPausedAsync(), Is.True);

        await flag.SetAsync(false, "resumed");
        Assert.That(await flag.IsPausedAsync(), Is.False);
        Assert.That(await settings.GetAsync(MonitorPauseFlag.PausedKey), Is.EqualTo("false"));
    }

    [Test]
    public async Task AnotherProcessesPause_IsSeenOnceTheCacheExpires()
    {
        var monitor = new MonitorPauseFlag(settings, clock);   // the Hub's loop
        var cli = new MonitorPauseFlag(settings, clock);       // a pause written elsewhere

        Assert.That(await monitor.IsPausedAsync(), Is.False);
        await cli.SetAsync(true, "pause");

        // Still inside the 2 s cache: the loop keeps its last answer...
        Assert.That(await monitor.IsPausedAsync(), Is.False);

        // ...and honors the pause on the first tick after it expires.
        clock.Advance(MonitorPauseFlag.CacheFor);
        Assert.That(await monitor.IsPausedAsync(), Is.True);
    }
}
