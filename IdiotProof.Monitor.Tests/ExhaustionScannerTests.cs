using System.Runtime.CompilerServices;
using IdiotProof.Brokers;
using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Adaptation;
using IdiotProof.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Vault.Credentials;

namespace IdiotProof.Monitor.Tests;

/// <summary>
/// IP-T387 / IP-US-S2: <see cref="ExhaustionScanner"/> arms a PAPER mean-reversion long at a capitulation
/// low only when the symbol's own history says such lows bounce, records fade shorts for analysis only,
/// waits for the information gate, and arms nothing when the paper route is live. Bars come from a
/// scripted feed, rows from an in-memory database, and the broker is a fake that throws on any order.
/// </summary>
[TestFixture]
public sealed class ExhaustionScannerTests
{
    // Monday 2026-11-02, 9:30 ET (EST) = 14:30 UTC.
    private static readonly DateTime Open = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
    private static readonly ExhaustionOptions Options = new() { MinSamples = 10, KnifeMinSamples = 10 };

    private string vaultRoot = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private UserKeyService keys = null!;
    private BrokerRouter router = null!;
    private SettingsRepository settings = null!;
    private BarFeed feed = null!;
    private Guid user;

    [SetUp]
    public async Task SetUp()
    {
        vaultRoot = Path.Combine(Path.GetTempPath(), $"ip-exhaust-vault-{Guid.NewGuid():N}");
        var vault = new AccountSecretVault(new BrokerCredentialStore(Path.Combine(vaultRoot, "Brokers")),
            new TokenStore(Path.Combine(vaultRoot, "Tokens")), configuration: null, () => null);
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"exhaust-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        keys = new UserKeyService(db, new EphemeralDataProtectionProvider(), vault, NullLogger<UserKeyService>.Instance);
        router = new BrokerRouter();
        router.Register(new FakeBroker(BrokerType.Sandbox, isPaper: true));
        router.SetActive(BrokerType.Sandbox);
        settings = new SettingsRepository(db);
        feed = new BarFeed();
        user = Guid.NewGuid();
        await keys.SaveAsync(user, new UserApiKeys { UserId = user, DefaultBroker = "Sandbox" });
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(vaultRoot, recursive: true); } catch { /* best effort */ }
    }

    private ExhaustionScanner Scanner() => new(new StrategyRepository(db), feed,
        new UserBrokerResolver(keys, router, NullLogger<UserBrokerResolver>.Instance), settings,
        new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance), db, NullLogger<ExhaustionScanner>.Instance);

    private async Task EnableAsync(params string[] symbols)
    {
        await settings.SetAsync(ExhaustionScanner.EnabledKey, "true");
        await settings.SetAsync(ExhaustionScanner.OwnerKey, user.ToString());
        await settings.SetAsync(ExhaustionScanner.SymbolsKey, string.Join(",", symbols));
        await settings.SetAsync(ExhaustionScanner.HistoryDaysKey, "80");
    }

    private void Script(string symbol, Func<int, string, int, bool, List<Candle>> session, string after, bool todayTruncated = true)
    {
        var bars = new List<Candle>();
        for (int d = 80; d >= 1; d--) bars.AddRange(session(500 + d, after, -d, false));
        bars.AddRange(session(7, "bounce", 0, todayTruncated));
        feed.Bars[symbol] = bars.Select(b => Re(b, symbol)).ToList();
    }

    private static Candle Re(Candle c, string symbol) => new()
    {
        Symbol = symbol, StartUtc = c.StartUtc, EndUtc = c.EndUtc, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume,
    };

    private DateTime NowAfter(string symbol) => feed.Bars[symbol].Where(b => b.StartUtc >= Open).Max(b => b.EndUtc);

    private async Task<List<ExhaustionDecisionRow>> RowsAsync()
    {
        await using var c = await db.CreateDbContextAsync();
        return await c.ExhaustionDecisions.AsNoTracking().ToListAsync();
    }

    private async Task<List<Strategy>> ActiveAsync()
    {
        await using var c = await db.CreateDbContextAsync();
        return await c.Strategies.AsNoTracking().Where(s => s.IsActive).ToListAsync();
    }

    [Test]
    public async Task OffByDefault_DoesNothing()
    {
        Script("PNY", Capitulation, "bounce");
        var summary = await Scanner().RunScanAsync(NowAfter("PNY"), Options);
        Assert.That(summary.Note, Is.EqualTo("disabled"));
        Assert.That(await RowsAsync(), Is.Empty);
        Assert.That(feed.Calls, Is.Zero);
    }

    [Test]
    public async Task CapitulationThatHistorySaysBounces_ArmsAPaperLong_WithAKnifeGuard()
    {
        await EnableAsync("PNY");
        Script("PNY", Capitulation, "bounce");
        var summary = await Scanner().RunScanAsync(NowAfter("PNY"), Options);

        var active = await ActiveAsync();
        var row = (await RowsAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(summary.Armed, Is.EqualTo(1), row.Reason);
            Assert.That(active, Has.Count.EqualTo(1));
            Assert.That(active[0].BrokerMode, Is.EqualTo("Paper"));
            Assert.That(active[0].Author, Is.EqualTo(ExhaustionScanner.Author));
            Assert.That(active[0].ScriptText, Does.Contain("NotFallingKnife"));
            Assert.That(row.Armed, Is.True);
            Assert.That(row.Side, Is.EqualTo("Long"));
            Assert.That(row.StrategyId, Is.EqualTo(active[0].Id));
            Assert.That(row.ReversalSamples, Is.GreaterThanOrEqualTo(10));
            Assert.That(row.StopLossPercent, Is.GreaterThan(0));
            Assert.That(row.FeaturesJson, Does.Contain("features"));
        });

        var again = await Scanner().RunScanAsync(NowAfter("PNY"), Options);
        Assert.That(again.Armed, Is.Zero, "one arm per symbol per day");
        Assert.That(await ActiveAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CapitulationThatHistorySaysKeepsFalling_IsSkipped_AndRecorded()
    {
        await EnableAsync("PNY");
        Script("PNY", Capitulation, "knife");
        await Scanner().RunScanAsync(NowAfter("PNY"), Options);

        var row = (await RowsAsync()).Single();
        Assert.That(row.Armed, Is.False);
        Assert.That(row.Side, Is.EqualTo("Long"));
        Assert.That(await ActiveAsync(), Is.Empty);
    }

    [Test]
    public async Task BeforeTheInformationGate_NothingIsDecided()
    {
        await EnableAsync("PNY");
        Script("PNY", Capitulation, "bounce");
        var summary = await Scanner().RunScanAsync(Open.AddMinutes(10), Options);
        Assert.That(summary.Decided, Is.Zero);
        Assert.That(await RowsAsync(), Is.Empty);
        Assert.That(await ActiveAsync(), Is.Empty);
    }

    [Test]
    public async Task LivePaperRoute_RefusesToArmAnything()
    {
        router.Register(new FakeBroker(BrokerType.Sandbox, isPaper: false));
        await EnableAsync("PNY");
        Script("PNY", Capitulation, "bounce");
        var summary = await Scanner().RunScanAsync(NowAfter("PNY"), Options);
        Assert.That(summary.Note, Does.Contain("LIVE"));
        Assert.That(await RowsAsync(), Is.Empty);
        Assert.That(await ActiveAsync(), Is.Empty);
        Assert.That(feed.Calls, Is.Zero, "never even reads bars");
    }

    [Test]
    public async Task ClearedFadeShort_IsRecordedForAnalysisOnly_NeverArmed()
    {
        await EnableAsync("PNY");
        Script("PNY", Spike, "fade");
        var summary = await Scanner().RunScanAsync(NowAfter("PNY"), Options);

        var row = (await RowsAsync()).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(row.Side, Is.EqualTo("Short"), row.Reason);
            Assert.That(row.AnalysisOnly, Is.True, row.Reason);
            Assert.That(row.Armed, Is.False);
            Assert.That(row.Reason, Does.Contain("analysis only"));
            Assert.That(summary.AnalysisOnly, Is.EqualTo(1));
            Assert.That(await ActiveAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task Worker_NeverScans_WithoutTheLeaderLease()
    {
        await EnableAsync("PNY");
        Script("PNY", Capitulation, "bounce");
        var worker = new ExhaustionScanWorker(Scanner(), new MonitorRuntimeState(), TimeProvider.System,
            NullLogger<ExhaustionScanWorker>.Instance);
        Assert.That(await worker.RunOnceAsync(CancellationToken.None), Is.False);
        Assert.That(feed.Calls, Is.Zero);
    }

    [Test]
    public void LongTemplate_IsALongWithAKnifeGuard_AndPerTickerExits()
    {
        var p = AdaptiveParameterDeriver.Derive(Capitulation(7, "bounce", 0, false));
        var d = new ExhaustionDecision(ExhaustionSide.Long, true, "ok", default, 5, 2.7, 2.75, null, null, p, 41);
        var script = ExhaustionScanner.LongTemplate("PNY", d, Options).ToScript();
        Assert.That(script, Does.Contain("NotFallingKnife"));
        Assert.That(script, Does.Contain("BreaksAbove(2.75)"));
        Assert.That(script, Does.Contain("Long()"));
    }

    // ── synthetic sessions ──────────────────────────────────────────────────

    /// <summary>40 quiet bars near $3, one ~8% capitulation bar, then a bounce or a continued bleed.</summary>
    private static List<Candle> Capitulation(int seed, string after, int dayOffset, bool truncate)
    {
        var rng = new Random(seed);
        var bars = new List<Candle>();
        double price = 3.0;
        for (int i = 0; i < 40; i++) { price *= 1 + 0.002 * (rng.NextDouble() - 0.5); Add(bars, Open.AddDays(dayOffset), price, rng); }
        price *= 1 - 0.08 - 0.01 * rng.NextDouble();
        Add(bars, Open.AddDays(dayOffset), price, rng);
        if (truncate) return bars;
        for (int i = 0; i < 20; i++)
        {
            price *= after == "bounce" ? 1.012 + 0.003 * rng.NextDouble() : 0.988 - 0.003 * rng.NextDouble();
            Add(bars, Open.AddDays(dayOffset), price, rng);
        }
        return bars;
    }

    /// <summary>40 quiet bars drifting up, an ~8% spike to a new high-of-day, a give-back bar, then a fade or a squeeze.</summary>
    private static List<Candle> Spike(int seed, string after, int dayOffset, bool truncate)
    {
        var rng = new Random(seed);
        var bars = new List<Candle>();
        double price = 3.0;
        for (int i = 0; i < 40; i++) { price *= 1.0004 + 0.002 * (rng.NextDouble() - 0.5); Add(bars, Open.AddDays(dayOffset), price, rng); }
        double before = price;
        price *= 1.08 + 0.01 * rng.NextDouble(); Add(bars, Open.AddDays(dayOffset), price, rng);
        price = before * (0.996 - 0.002 * rng.NextDouble()); Add(bars, Open.AddDays(dayOffset), price, rng);
        if (truncate) return bars;
        for (int i = 0; i < 20; i++)
        {
            price *= after == "fade" ? 0.988 - 0.003 * rng.NextDouble() : 1.012 + 0.003 * rng.NextDouble();
            Add(bars, Open.AddDays(dayOffset), price, rng);
        }
        return bars;
    }

    private static void Add(List<Candle> bars, DateTime start, double close, Random rng)
    {
        double open = bars.Count == 0 ? close : (double)bars[^1].Close;
        int i = bars.Count;
        bars.Add(new Candle
        {
            Symbol = "PNY", StartUtc = start.AddMinutes(i), EndUtc = start.AddMinutes(i + 1),
            Open = (decimal)open, Close = (decimal)close,
            High = (decimal)(Math.Max(open, close) * 1.002), Low = (decimal)(Math.Min(open, close) * 0.998),
            Volume = 10_000 + rng.Next(5_000),
        });
    }

    private sealed class BarFeed : IMarketDataFeed
    {
        public Dictionary<string, List<Candle>> Bars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Calls { get; private set; }
        public string FeedName => "Scripted";

        public Task<decimal?> GetPreviousCloseAsync(string symbol, DateTime beforeUtc, CancellationToken ct = default) =>
            Task.FromResult<decimal?>(null);

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls++;
            await Task.Yield();
            if (!Bars.TryGetValue(symbol, out var bars)) yield break;
            foreach (var b in bars)
                if (b.StartUtc >= startUtc && b.StartUtc < endUtc) yield return b;
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
    }

    private sealed class FakeBroker(BrokerType type, bool isPaper) : IBrokerClient
    {
        public BrokerType BrokerType => type;
        public bool IsPaper => isPaper;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("the exhaustion scanner only arms strategies; it must never place an order");
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new InvalidOperationException("never called");
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }
}
