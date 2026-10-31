using IdiotProof.Core.Hosting;
using IdiotProof.Engine.Storage;
using IdiotProof.Monitor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The Hub hosts the Monitor (IP-T74) and its scanners (IP-T75) by calling AddIdiotProofMonitor
/// next to AddIdiotProofCore + AddIdiotProofTrading — the same three calls the old Monitor exe
/// made. These pin that the graph builds, that MonitorWorker is the hosted service (it still takes
/// the SQL leader lease and runs under SupervisedLoop) and that the scanners the tick drives ride
/// with it, plus the runtime state behind `monitor status` (IP-T256).
/// </summary>
[TestFixture]
public sealed class MonitorHostingTests
{
    private string root = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"monitor-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private IServiceCollection Graph()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = Path.Combine(root, "keys") })
            .Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddRouting()
            .AddIdiotProofCore(configuration, new WebStorageProvider(root), new CoreHostOptions { PinDevKeyRing = true })
            .AddIdiotProofTrading()
            .AddIdiotProofMonitor();
    }

    [Test]
    public async Task AddIdiotProofMonitor_HostsTheMonitorWorker_AndTheGraphValidates()
    {
        var services = Graph();
        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType).ToList();
        Assert.That(hosted, Does.Contain(typeof(MonitorWorker)));

        await using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.That(sp.GetServices<IHostedService>().OfType<MonitorWorker>().Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task AddIdiotProofMonitor_RegistersTheScannersTheTickDrives_AsSingletons()
    {
        var services = Graph();
        foreach (var scanner in new[] { typeof(AutoGapperScanner), typeof(PremarketFadeScanner), typeof(BeBexDecayScanner) })
        {
            var d = services.SingleOrDefault(x => x.ServiceType == scanner);
            Assert.That(d, Is.Not.Null, scanner.Name);
            Assert.That(d!.Lifetime, Is.EqualTo(ServiceLifetime.Singleton), scanner.Name);
        }

        await using var sp = services.BuildServiceProvider();
        Assert.That(sp.GetRequiredService<PremarketFadeScanner>(), Is.Not.Null);
        Assert.That(sp.GetRequiredService<BeBexDecayScanner>(), Is.Not.Null);
        Assert.That(sp.GetRequiredService<AutoGapperScanner>(), Is.Not.Null);
    }

    [Test]
    public async Task AddIdiotProofMonitor_SharesOneRuntimeStateAndPauseFlag()
    {
        await using var sp = Graph().BuildServiceProvider();
        var state = sp.GetRequiredService<MonitorRuntimeState>();
        var flag = sp.GetRequiredService<MonitorPauseFlag>();
        Assert.That(sp.GetRequiredService<MonitorRuntimeState>(), Is.SameAs(state));
        Assert.That(sp.GetRequiredService<MonitorPauseFlag>(), Is.SameAs(flag));
        Assert.That(sp.GetRequiredService<IMonitorLeaseProbe>(), Is.InstanceOf<SqlMonitorLeaseProbe>());
    }

    [Test]
    public void RuntimeState_TracksLeaseTicksFailuresAndStop()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 14, 0, 0, TimeSpan.Zero));
        var state = new MonitorRuntimeState(clock);

        state.MarkStarting("alpaca", TimeSpan.FromSeconds(1));
        Assert.That(state.Snapshot() is { Running: true, LeaseHeld: false });

        state.MarkLeaseAcquired();
        state.MarkTickStarted();
        Assert.That(state.Snapshot().TickInProgress, Is.True);
        state.MarkTickFinished(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        state.MarkTickStarted();
        state.MarkTickFinished("feed timeout");

        var s = state.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(s.LeaseHeld, Is.True);
            Assert.That(s.Ticks, Is.EqualTo(2));
            Assert.That(s.FailedTicks, Is.EqualTo(1));
            Assert.That(s.ConsecutiveFailures, Is.EqualTo(1));
            Assert.That(s.LastError, Is.EqualTo("feed timeout"));
            Assert.That(s.LastTickSuccessUtc, Is.EqualTo(new DateTime(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc)));
            Assert.That(s.TickInProgress, Is.False);
        });

        state.MarkStopped();
        s = state.Snapshot();
        Assert.That(s is { Running: false, LeaseHeld: false });
        Assert.That(s.LeaseReleasedUtc, Is.Not.Null);
    }
}
