using System.Runtime.CompilerServices;
using IdiotProof.Core.Adaptation;
using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace IdiotProof.Core.Tests;

/// <summary>
/// IP-T386: only the instance holding the Monitor leader lease runs adaptation passes, so a
/// standby never writes duplicate AdaptationDecisions rows. The lease is a real sp_getapplock on a
/// throwaway LocalDB database; strategies and decisions live in an in-memory store.
/// </summary>
[TestFixture]
public sealed class AdaptationWorkerLeaseTests
{
    private static readonly string DbName = $"IdiotProof_AdaptLease_{Guid.NewGuid():N}";
    private static readonly string Master = @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;";
    private static readonly string ConnStr = $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";
    private static readonly DateTime Open = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc); // 9:30 ET

    private IDbContextFactory<AppDbContext> factory = null!;
    private AdaptationService service = null!;
    private TapeFeed feed = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        await using var conn = new SqlConnection(Master);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"CREATE DATABASE [{DbName}]", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(Master);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"ALTER DATABASE [{DbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DbName}]", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"adapt-lease-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        var settings = new SettingsRepository(factory);
        service = new AdaptationService(factory, settings, new RiskGuardianService(factory));
        await settings.SetAsync(AdaptationService.GlobalEnabledKey, "true");
        feed = new TapeFeed(Tape(120));

        var def = Stock.Ticker("PNY").IsAboveVwap().Long().Quantity(1).StopLossPercent(5).Repeat().Build();
        var s = new Strategy
        {
            OwnerUserId = Guid.NewGuid(), Symbol = "PNY", Title = "adapt me",
            ScriptJson = StrategyJson.Serialize(def), ScriptText = def.ToScript(),
            IsActive = true, BrokerMode = "Paper",
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Strategies.Add(s);
            await db.SaveChangesAsync();
        }
        await service.SetOptInAsync(s.Id, true);
    }

    private AdaptationWorker Worker(MonitorRuntimeState runtime) =>
        new(service, factory, feed, new FakeTimeProvider(new DateTimeOffset(Open.AddMinutes(90))), runtime,
            NullLogger<AdaptationWorker>.Instance);

    private async Task<int> DecisionsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.AdaptationDecisions.CountAsync();
    }

    [Test]
    public async Task StandbyWithoutTheLease_SkipsThePass_WritesNoDecisions_AndNeverReadsTheFeed()
    {
        await using var leader = await MonitorLeaderLease.AcquireAsync(ConnStr, NullLogger.Instance, CancellationToken.None);
        var standby = new MonitorRuntimeState(); // a standby Monitor never registers a held lease

        Assert.That(await Worker(standby).RunOnceAsync(CancellationToken.None), Is.False);
        Assert.That(await DecisionsAsync(), Is.EqualTo(0));
        Assert.That(feed.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task LeaderHoldingTheLease_RunsThePass_ThenStopsTheMomentTheLeaseIsGone()
    {
        var runtime = new MonitorRuntimeState();
        var lease = await MonitorLeaderLease.AcquireAsync(ConnStr, NullLogger.Instance, CancellationToken.None);
        runtime.SetLeaderCheck(lease.IsHeldAsync);
        var worker = Worker(runtime);

        Assert.That(await worker.RunOnceAsync(CancellationToken.None), Is.True);
        var afterLeading = await DecisionsAsync();
        Assert.That(afterLeading, Is.EqualTo(1), "the leader's pass is not vacuous");

        await lease.DisposeAsync();
        Assert.That(await worker.RunOnceAsync(CancellationToken.None), Is.False);
        Assert.That(await DecisionsAsync(), Is.EqualTo(afterLeading));
    }

    [Test]
    public async Task ALeaderCheckThatThrows_FailsClosed()
    {
        var runtime = new MonitorRuntimeState();
        runtime.SetLeaderCheck(_ => throw new SqlNullValueException_());
        Assert.That(await runtime.IsLeaderAsync(), Is.False);
        Assert.That(await Worker(runtime).RunOnceAsync(CancellationToken.None), Is.False);
        Assert.That(await DecisionsAsync(), Is.EqualTo(0));
    }

    private sealed class SqlNullValueException_ : Exception;

    private sealed class TapeFeed(List<Candle> tape) : IMarketDataFeed
    {
        public int Calls { get; private set; }
        public string FeedName => "tape";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls++;
            foreach (var c in tape.Where(c => c.StartUtc >= startUtc && c.EndUtc <= endUtc)) yield return c;
            await Task.CompletedTask;
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult<LatestPrice?>(null);
    }

    private static List<Candle> Tape(int bars)
    {
        var rng = new Random(17);
        var list = new List<Candle>();
        double p = 3.0;
        for (int i = 0; i < bars; i++)
        {
            double o = p;
            p *= 1 + 0.006 * Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
            double hi = Math.Max(o, p) * (1 + 0.002 * rng.NextDouble());
            double lo = Math.Min(o, p) * (1 - 0.002 * rng.NextDouble());
            list.Add(new Candle
            {
                Symbol = "PNY", StartUtc = Open.AddMinutes(i), EndUtc = Open.AddMinutes(i + 1),
                Open = (decimal)o, Close = (decimal)p, High = (decimal)hi, Low = (decimal)lo, Volume = 20_000,
            });
        }
        return list;
    }
}
