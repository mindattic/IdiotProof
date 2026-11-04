using System.Runtime.CompilerServices;
using IdiotProof.Brokers;
using IdiotProof.Core.Data;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Monitoring;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Storage;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using MindAttic.Authentication.Entities;

namespace IdiotProof.Core.Tests;

/// <summary>
/// IP-US-K6: the hosted MonitorWorker, built from the same DI graph the Hub uses, runs as one
/// pipeline: it takes the SQL leader lease, re-reads the active strategy from SQL, gates on the
/// session by the wall clock, evaluates against the feed, fires through the gateway and records
/// the Sandbox fill on the Strategy row. The clock is pinned to a chosen ET instant; the database
/// is a throwaway LocalDB one; only the Sandbox broker exists.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class MonitorWorkerHarnessTests
{
    private const string ConnEnv = "ConnectionStrings__IdiotProof";
    private static readonly string DbName = $"IdiotProof_Harness_{Guid.NewGuid():N}";
    private static readonly string ConnStr = $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    // Tuesday 2026-07-14, 10:30 ET (EDT) — inside RTH, not a holiday.
    private static readonly DateTimeOffset RthInstant = new(2026, 7, 14, 14, 30, 0, TimeSpan.Zero);
    // Saturday 2026-07-18, 10:30 ET — the schedule hibernates.
    private static readonly DateTimeOffset WeekendInstant = new(2026, 7, 18, 14, 30, 0, TimeSpan.Zero);

    private string root = "";
    private string? savedConn, savedBroker, savedFeed, savedStreaming;

    [OneTimeSetUp]
    public void PinEnvironment()
    {
        savedConn = Environment.GetEnvironmentVariable(ConnEnv);
        savedBroker = Environment.GetEnvironmentVariable(TradingServiceRegistration.BrokerEnvVar);
        savedFeed = Environment.GetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar);
        savedStreaming = Environment.GetEnvironmentVariable("IDIOTPROOF_STREAMING");
        Environment.SetEnvironmentVariable(ConnEnv, ConnStr);
        Environment.SetEnvironmentVariable(TradingServiceRegistration.BrokerEnvVar, null);
        Environment.SetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar, null);
        Environment.SetEnvironmentVariable("IDIOTPROOF_STREAMING", "0");
        root = Path.Combine(Path.GetTempPath(), $"monitor-harness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
    }

    [OneTimeTearDown]
    public async Task RestoreEnvironment()
    {
        Environment.SetEnvironmentVariable(ConnEnv, savedConn);
        Environment.SetEnvironmentVariable(TradingServiceRegistration.BrokerEnvVar, savedBroker);
        Environment.SetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar, savedFeed);
        Environment.SetEnvironmentVariable("IDIOTPROOF_STREAMING", savedStreaming);
        SqlConnection.ClearAllPools();
        try
        {
            await using var conn = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;");
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"IF DB_ID('{DbName}') IS NOT NULL BEGIN ALTER DATABASE [{DbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DbName}] END", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException) { /* best-effort cleanup */ }
        try { Directory.Delete(root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>
    /// Rising 1-minute bars ending at the requested end, so price stays above VWAP. With
    /// <see cref="Falling"/> set, the last three bars slide back under VWAP (about 4% off the high,
    /// inside a wide stop), the shape that should trip an ExitWhenFalse While(IsAboveVwap) guard.
    /// </summary>
    private sealed class RisingFeed : IMarketDataFeed
    {
        public string FeedName => "HarnessFeed";
        public volatile bool Falling;

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (candleSize != TimeSpan.FromMinutes(1)) yield break;
            var end = new DateTime(endUtc.Ticks - endUtc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
            var first = end.AddMinutes(-60) < startUtc ? startUtc : end.AddMinutes(-60);
            var i = 0;
            var count = (int)(end - first).TotalMinutes;
            for (var t = first; t < end; t = t.AddMinutes(1), i++)
            {
                var px = Falling && i >= count - 3 ? 10.15m : 10m + 0.01m * i;
                yield return new Candle { Symbol = symbol, StartUtc = t, EndUtc = t.AddMinutes(1), Open = px, High = px + 0.02m, Low = px - 0.01m, Close = px + 0.01m, Volume = 5000m };
            }
            await Task.CompletedTask;
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
        public Task<decimal?> GetPreviousCloseAsync(string symbol, DateTime beforeUtc, CancellationToken ct = default) => Task.FromResult<decimal?>(9.5m);
    }

    private ServiceProvider Build(DateTimeOffset now) => Build(new FakeTimeProvider(now), new RisingFeed());

    private ServiceProvider Build(FakeTimeProvider clock, RisingFeed feed)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = Path.Combine(root, "keys") })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddRouting()
            .AddSingleton<TimeProvider>(clock)
            .AddIdiotProofCore(configuration, new WebStorageProvider(root), new CoreHostOptions { PinDevKeyRing = true })
            .AddIdiotProofTrading()
            .AddIdiotProofMonitor();
        services.AddSingleton<IMarketDataFeed>(feed);
        var sp = services.BuildServiceProvider();

        using var db = sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        Assert.That(db.Database.GetConnectionString(), Does.Contain(DbName), "the harness must never touch the real IdiotProof database");
        db.Database.EnsureCreated();
        Assert.That(sp.GetRequiredService<BrokerRouter>().GetAll().Select(b => b.BrokerType), Is.EqualTo(new[] { BrokerType.Sandbox }),
            "only the simulator may be reachable");
        return sp;
    }

    private static Task<Strategy> ArmAsync(IServiceProvider sp, string symbol, TradingSession session) =>
        ArmAsync(sp, symbol, Stock.Ticker(symbol).Session(session).IsAboveVwap().StopLossPercent(2).TakeProfitPercent(4).Build());

    private static async Task<Strategy> ArmAsync(IServiceProvider sp, string symbol, StrategyDefinition def)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var email = $"{symbol.ToLowerInvariant()}@harness.test";
        var user = new AuthUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "x", Role = "User", IsActive = true,
        };
        db.AuthUsers.Add(user);
        var strategy = new Strategy
        {
            OwnerUserId = user.Id, Title = $"harness {symbol}", Symbol = symbol,
            ScriptText = def.ToScript(), ScriptJson = StrategyJson.Serialize(def),
            IsActive = true, BrokerMode = "Sandbox",
        };
        db.Strategies.Add(strategy);
        await db.SaveChangesAsync();
        return strategy;
    }

    private static async Task<(Strategy Row, ConditionProgress? Progress)> ReadAsync(IServiceProvider sp, Guid id)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var row = await db.Strategies.AsNoTracking().SingleAsync(s => s.Id == id);
        var progress = await db.ConditionProgress.AsNoTracking().SingleOrDefaultAsync(p => p.StrategyId == id);
        return (row, progress);
    }

    private static async Task<string> AuditTrailAsync(IServiceProvider sp)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var rows = await db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).Select(a => a.Category + ": " + a.Message).ToListAsync();
        return string.Join("\n", rows);
    }

    /// <summary>Runs the hosted worker until <paramref name="done"/> holds or the timeout passes.</summary>
    private static async Task RunWorkerUntilAsync(IServiceProvider sp, Func<Task<bool>> done, TimeSpan timeout)
    {
        var worker = sp.GetServices<IHostedService>().OfType<MonitorWorker>().Single();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && !await done()) await Task.Delay(250);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task InSession_TheWorkerFires_ThroughTheGateway_AndRecordsTheSandboxFill()
    {
        await using var sp = Build(RthInstant);
        var armed = await ArmAsync(sp, "HRNA", TradingSession.RTH);

        await RunWorkerUntilAsync(sp, async () => (await ReadAsync(sp, armed.Id)).Row.PositionQty > 0, TimeSpan.FromSeconds(45));

        var (row, progress) = await ReadAsync(sp, armed.Id);
        var sandbox = sp.GetRequiredService<BrokerRouter>().GetAll().Single(b => b.BrokerType == BrokerType.Sandbox);
        var positions = await sandbox.GetPositionsAsync();
        var trail = await AuditTrailAsync(sp);
        Assert.Multiple(() =>
        {
            Assert.That(row.PositionQty, Is.EqualTo(1),
                $"1 share: no quantity or notional in the script. Progress: {progress?.PassedCount}/{progress?.TotalCount} {progress?.FirstFailingVerb}. Audit:\n{trail}");
            Assert.That(row.LastEntryPrice, Is.GreaterThan(10m));
            Assert.That(row.FireCount, Is.EqualTo(1));
            Assert.That(row.EntryFilledUtc, Is.EqualTo(RthInstant.UtcDateTime), "the fill is stamped on the Monitor's clock");
            Assert.That(positions.Any(p => p.Symbol == "HRNA" && p.Quantity == 1), Is.True, "the order reached the Sandbox book");
        });
    }

    [Test]
    public async Task OutsideTheStrategysSession_TheWorkerEvaluatesNothing_AndPlacesNoOrder()
    {
        await using var sp = Build(RthInstant);
        var armed = await ArmAsync(sp, "HRNB", TradingSession.Premarket);

        await RunWorkerUntilAsync(sp, async () => (await ReadAsync(sp, armed.Id)).Progress is not null, TimeSpan.FromSeconds(45));

        var (row, progress) = await ReadAsync(sp, armed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(progress?.FirstFailingVerb, Does.Contain("outside Premarket session"));
            Assert.That(row.PositionQty, Is.EqualTo(0));
            Assert.That(row.FireCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task OnAWeekend_TheScheduleHibernates_AndNoStrategyIsTouched()
    {
        await using var sp = Build(WeekendInstant);
        var armed = await ArmAsync(sp, "HRNC", TradingSession.RTH);

        await RunWorkerUntilAsync(sp, () => Task.FromResult(false), TimeSpan.FromSeconds(4));

        var (row, progress) = await ReadAsync(sp, armed.Id);
        var runtime = sp.GetRequiredService<MonitorRuntimeState>().Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(progress, Is.Null, "hibernate returns before any strategy is read");
            Assert.That(row.PositionQty, Is.EqualTo(0));
            Assert.That(runtime.Window, Is.EqualTo(nameof(TradingWindow.Hibernate)));
        });
    }
    [Test]
    public async Task HeldPosition_WhoseWhileStopsHolding_ExitsWithWhileFailed_UnderExitWhenFalse()
    {
        var clock = new FakeTimeProvider(RthInstant);
        var feed = new RisingFeed();
        await using var sp = Build(clock, feed);
        var def = Stock.Ticker("HRND").Session(TradingSession.RTH)
            .While(c => c.IsAboveVwap()).ExitWhenFalse()
            .IsAboveVwap().StopLossPercent(10).TakeProfitPercent(20).Build();
        var armed = await ArmAsync(sp, "HRND", def);

        // Enter on the rising tape; once the Sandbox holds the shares, roll the tape under VWAP once the bar cache refreshes.
        await RunWorkerUntilAsync(sp, async () =>
        {
            var row = (await ReadAsync(sp, armed.Id)).Row;
            if (row.PositionQty > 0 && !feed.Falling)
            {
                feed.Falling = true;
                clock.Advance(TimeSpan.FromMinutes(6)); // past the Monitor's 5-minute REST bar refresh
            }
            return row.LastExitReason is not null;
        }, TimeSpan.FromSeconds(60));

        var (after, progress) = await ReadAsync(sp, armed.Id);
        var sandbox = sp.GetRequiredService<BrokerRouter>().GetAll().Single(b => b.BrokerType == BrokerType.Sandbox);
        var positions = await sandbox.GetPositionsAsync();
        var trail = await AuditTrailAsync(sp);
        Assert.Multiple(() =>
        {
            Assert.That(feed.Falling, Is.True, $"the entry never filled. Progress: {progress?.FirstFailingVerb}. Audit:\n{trail}");
            Assert.That(after.LastExitReason, Is.EqualTo(nameof(GapperExitReason.WhileFailed)), $"Audit:\n{trail}");
            Assert.That(after.PositionQty, Is.EqualTo(0), "the Strategy row is flat");
            Assert.That(positions.Any(p => p.Symbol == "HRND" && p.Quantity != 0), Is.False, "the Sandbox book is flat");
        });
    }
}
