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

/// <summary>
/// monitor status|heartbeat|lease (IP-T256), pause|resume|tick-once (IP-T257) and tail (IP-T258)
/// against EF InMemory, a fake lease probe, a temp heartbeat file and a scripted feed. Nothing here
/// can reach a broker: tick-once is given no broker at all.
/// </summary>
public partial class MonitorOperationTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    /// <summary>Fri 10/30/2026 10:00 AM ET — inside RTH.</summary>
    private static readonly DateTime Now = new(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc);

    private string root = "";
    private ServiceProvider sp = null!;
    private FakeLease lease = null!;

    private sealed class FakeLease : IMonitorLeaseProbe
    {
        public MonitorLeaseInfo Info { get; set; } = new() { Held = false, Note = "no session holds the lease" };
        public Task<MonitorLeaseInfo> ProbeAsync(CancellationToken ct = default) => Task.FromResult(Info);
    }

    /// <summary>Minute bars ending at <see cref="Now"/>, closing at <paramref name="close"/>.</summary>
    private sealed class FlatFeed(decimal close) : IMarketDataFeed
    {
        public string FeedName => "Test";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            for (var t = endUtc.AddMinutes(-60); t < endUtc; t = t.AddMinutes(1))
                yield return new Candle { Symbol = symbol, StartUtc = t, EndUtc = t.AddMinutes(1), Open = close, High = close + 0.05m, Low = close - 0.05m, Close = close, Volume = 10_000 };
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);

        public Task<decimal?> GetPreviousCloseAsync(string symbol, DateTime beforeUtc, CancellationToken ct = default) => Task.FromResult<decimal?>(close - 1);
    }

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"monitor-ops-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        lease = new FakeLease();
        sp = Services(new FlatFeed(105m), withRuntime: true);
    }

    [TearDown]
    public void TearDown()
    {
        sp.Dispose();
        try { Directory.Delete(root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private ServiceProvider Services(IMarketDataFeed feed, bool withRuntime)
    {
        var clock = new Fixtures.FixedClock(new DateTimeOffset(Now));
        var s = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"monitor-ops-{Guid.NewGuid():N}"))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<SettingsRepository>()
            .AddSingleton<StrategyRepository>()
            .AddSingleton<ConditionProgressRepository>()
            .AddSingleton(sp => new AuditLogRepository(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), NullLogger<AuditLogRepository>.Instance))
            .AddSingleton(sp => new ConfirmationStore(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), clock))
            .AddSingleton<IStorageProvider>(new WebStorageProvider(root))
            .AddSingleton<IMonitorLeaseProbe>(lease)
            .AddSingleton(feed);
        if (withRuntime) s.AddSingleton(new MonitorRuntimeState(clock));
        return s.BuildServiceProvider();
    }

    private OperationContext Ctx(string? confirm = null) => new()
    {
        Services = sp,
        OwnerId = Owner,
        Clock = new Fixtures.FixedClock(new DateTimeOffset(Now)),
        ConfirmationCode = confirm,
        Caller = OperationCaller.Cli,
    };

    private async Task<Strategy> AddStrategyAsync(string symbol, decimal breakout, string mode = "Paper", int position = 0)
    {
        var def = Stock.Ticker(symbol).Breakout((double)breakout).Long().TakeProfit((double)breakout * 1.2).StopLoss((double)breakout * 0.9).Build();
        var row = new Strategy
        {
            OwnerUserId = Owner, Symbol = symbol, Title = $"{symbol} breakout", IsActive = true,
            ScriptJson = StrategyJson.Serialize(def), BrokerMode = mode, PositionQty = position,
        };
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        db.Strategies.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    [Test]
    public void MonitorOperations_AreValidCatalogEntries_AndTheirExamplesBind()
    {
        var types = typeof(MonitorStatusOperation).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(MonitorStatusOperation).Namespace && t.IsDefined(typeof(OperationAttribute), false))
            .ToArray();
        Assert.That(types, Is.Not.Empty);
        Assert.That(OperationCatalog.Validate(types), Is.Empty);

        var catalog = OperationCatalog.FromTypes(types);
        foreach (var d in catalog.Operations)
        foreach (var example in d.Examples)
        {
            var globals = ArgvParser.ExtractGlobals(ArgvParser.Split(example.CommandLine).Skip(1).ToList());
            Assert.That(globals.Error, Is.Null, example.CommandLine);
            Assert.That(catalog.TryResolve(globals.Remaining, out var resolved, out var rest), Is.True, example.CommandLine);
            Assert.That(resolved, Is.SameAs(d), example.CommandLine);
            Assert.That(ArgsBinder.Bind(d, rest).Errors, Is.Empty, example.CommandLine);
        }
    }

    // ── IP-T256 ─────────────────────────────────────────────────────────

    [Test]
    public async Task Status_NotHosted_WhenTheHubRunsWithoutTheMonitor()
    {
        sp.Dispose();
        sp = Services(new FlatFeed(105m), withRuntime: false);

        var result = await new MonitorStatusOperation().ExecuteAsync(new NoArgs(), Ctx());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("not hosted"));
        Assert.That(result.Human, Does.Contain("Leader lease:").And.Contain("free"));
    }

    [Test]
    public async Task Status_Running_ShowsLeaseLastTickAndStrategies()
    {
        var state = sp.GetRequiredService<MonitorRuntimeState>();
        state.MarkStarting("alpaca", TimeSpan.FromSeconds(1));
        state.MarkLeaseAcquired();
        state.SetWindow("Rth");
        state.MarkTickStarted();
        state.MarkTickFinished(null);
        await AddStrategyAsync("NVDA", 100m, position: 3);
        await AddStrategyAsync("AMD", 200m);

        var result = await new MonitorStatusOperation().ExecuteAsync(new NoArgs(), Ctx());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.StartWith("Monitor:").And.Contain("running"));
        Assert.That(result.Human, Does.Contain($"held by this Hub (PID {Environment.ProcessId})"));
        Assert.That(result.Human, Does.Contain("2 active, 1 holding a position"));
        Assert.That(result.Human, Does.Contain("10:00 AM ET").And.Contain("Rth"));
    }

    [Test]
    public void Classify_StaleWhenNoTickForTenIntervals_PausedBeatsRunning()
    {
        var state = new MonitorRuntimeState(new Fixtures.FixedClock(new DateTimeOffset(Now)));
        state.MarkStarting("alpaca", TimeSpan.FromSeconds(5));
        Assert.That(MonitorReads.Classify(state.Snapshot(), false, Now), Is.EqualTo(MonitorHealth.Standby));
        state.MarkLeaseAcquired();
        state.MarkTickFinished(null);
        Assert.That(MonitorReads.Classify(state.Snapshot(), false, Now.AddSeconds(49)), Is.EqualTo(MonitorHealth.Running));
        Assert.That(MonitorReads.Classify(state.Snapshot(), true, Now.AddSeconds(49)), Is.EqualTo(MonitorHealth.Paused));
        Assert.That(MonitorReads.Classify(state.Snapshot(), false, Now.AddSeconds(51)), Is.EqualTo(MonitorHealth.Stale));
        Assert.That(MonitorReads.Classify(null, false, Now), Is.EqualTo(MonitorHealth.NotHosted));
    }

    [Test]
    public async Task Heartbeat_ParsesTheLoopsFile_AndBlocksWhenStale()
    {
        var path = MonitorWorker.HeartbeatPathFor(new WebStorageProvider(root));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        Assert.That((await new MonitorHeartbeatOperation().ExecuteAsync(new NoArgs(), Ctx())).Status, Is.EqualTo(OperationStatus.Blocked));

        File.WriteAllText(path, $"{Now.AddSeconds(-2):O}\ttickStart={Now.AddSeconds(-3):O}\tstatus=fail\tconsecutiveFailures=2\terror=feed timeout");
        var fresh = await new MonitorHeartbeatOperation().ExecuteAsync(new NoArgs(), Ctx());
        Assert.That(fresh.IsOk, Is.True, fresh.Message);
        Assert.That(fresh.Human, Does.Contain("2s ago").And.Contain("fail").And.Contain("2 in a row").And.Contain("feed timeout"));

        File.WriteAllText(path, $"{Now.AddMinutes(-10):O}\ttickStart={Now.AddMinutes(-10):O}\tstatus=ok\tconsecutiveFailures=0");
        var stale = await new MonitorHeartbeatOperation().ExecuteAsync(new NoArgs(), Ctx());
        Assert.That(stale.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(stale.Message, Does.Contain("stale"));
    }

    [Test]
    public async Task Lease_NamesTheHolder()
    {
        lease.Info = new MonitorLeaseInfo { Held = true, SessionId = 57, HostName = "BOX", HostProcessId = 4242, ProgramName = "Core Microsoft SqlClient Data Provider", Note = "sys.dm_tran_locks" };

        var result = await new MonitorLeaseOperation().ExecuteAsync(new NoArgs(), Ctx());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("held by PID 4242 on BOX"));
        Assert.That(result.Human, Does.Contain("another process is the trading Monitor"));
    }
}
