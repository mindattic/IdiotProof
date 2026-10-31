using System.Runtime.CompilerServices;
using IdiotProof.Core.Data;
using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Models;
using IdiotProof.Monitor;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Vault.Credentials;

using IdiotProof.Core.Services;
using IdiotProof.Core.Monitoring;
namespace IdiotProof.Monitor.Tests;

/// <summary>
/// IP-T8: <see cref="AutoGapperScanner.RunScanAsync"/>, which arms real strategies unattended. Movers come
/// from the <see cref="AutoGapperScanner.FetchMovers"/> seam, bars from a scripted feed, keys from a
/// temp-directory Vault, rows from an in-memory database, and the global broker is a fake: nothing here
/// reaches Alpaca.
/// </summary>
[TestFixture]
public sealed class AutoGapperScannerTests
{
    private string vaultRoot = null!;
    private AccountSecretVault vault = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private UserKeyService keys = null!;
    private StrategyRepository strategies = null!;
    private BrokerRouter router = null!;
    private ScriptedFeed feed = null!;
    private AppSettings settings = null!;
    private Guid user;
    private int moverCalls;

    [SetUp]
    public async Task SetUp()
    {
        vaultRoot = Path.Combine(Path.GetTempPath(), $"ip-gapper-vault-{Guid.NewGuid():N}");
        vault = new AccountSecretVault(new BrokerCredentialStore(Path.Combine(vaultRoot, "Brokers")),
            new TokenStore(Path.Combine(vaultRoot, "Tokens")), configuration: null, () => null);
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"gapper-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        keys = new UserKeyService(db, new EphemeralDataProtectionProvider(), vault, NullLogger<UserKeyService>.Instance);
        strategies = new StrategyRepository(db);
        router = new BrokerRouter();
        router.Register(new FakeBroker(BrokerType.Sandbox, isPaper: true));
        router.SetActive(BrokerType.Sandbox);
        feed = new ScriptedFeed();
        settings = new AppSettings
        {
            AutoGapperMinGapPercent = 15, AutoGapperMinPrice = 1, AutoGapperMaxCount = 5, AutoGapperBrokerMode = "paper",
        };
        user = await UserAsync(withDataKeys: true);
        moverCalls = 0;
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(vaultRoot, recursive: true); } catch { /* best effort */ }
    }

    private async Task<Guid> UserAsync(bool withDataKeys)
    {
        var id = Guid.NewGuid();
        await keys.SaveAsync(id, new UserApiKeys
        {
            UserId = id, DefaultBroker = "Sandbox",
            AlpacaApiKeyId = withDataKeys ? "PKDATA" : null, AlpacaApiSecretKey = withDataKeys ? "data-secret" : null,
        });
        return id;
    }

    private AutoGapperScanner Scanner(params (string Symbol, double Percent, double Price)[] movers) =>
        new(strategies, feed, new UserBrokerResolver(keys, router, NullLogger<UserBrokerResolver>.Instance), keys,
            new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance), db, settings, NullLogger<AutoGapperScanner>.Instance)
        {
            FetchMovers = (_, _, _) =>
            {
                moverCalls++;
                return Task.FromResult(movers.ToList());
            },
        };

    private async Task<List<AutoGapperCandidate>> CandidatesAsync()
    {
        await using var c = await db.CreateDbContextAsync();
        return await c.AutoGapperCandidates.AsNoTracking().OrderBy(x => x.Rank).ToListAsync();
    }

    private async Task<List<AutoGapperScan>> ScansAsync()
    {
        await using var c = await db.CreateDbContextAsync();
        return await c.AutoGapperScans.AsNoTracking().ToListAsync();
    }

    private async Task<List<Strategy>> ActiveStrategiesAsync()
    {
        await using var c = await db.CreateDbContextAsync();
        return await c.Strategies.AsNoTracking().Where(s => s.IsActive).ToListAsync();
    }

    private async Task<List<AuditLog>> AuditAsync(string category)
    {
        await using var c = await db.CreateDbContextAsync();
        return await c.AuditLogs.AsNoTracking().Where(a => a.Category == category).ToListAsync();
    }

    // ── Guards ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task NoAlpacaDataKeys_ReturnsEarly_WithoutScreening()
    {
        var bare = await UserAsync(withDataKeys: false);
        var summary = await Scanner(("AAA", 30, 10)).RunScanAsync(bare, dryRun: false, "auto", CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(summary.Note, Is.EqualTo("user has no Alpaca data keys"));
            Assert.That(summary.Armed, Is.Zero);
            Assert.That(moverCalls, Is.Zero);
            Assert.That(await ScansAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task PaperModeButLiveBroker_RefusesToArm_AndLogsBlocked()
    {
        router.Register(new FakeBroker(BrokerType.Sandbox, isPaper: false));
        var summary = await Scanner(("AAA", 30, 10)).RunScanAsync(user, dryRun: false, "auto", CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(summary.Note, Does.Contain("routes LIVE"));
            Assert.That(summary.Armed, Is.Zero);
            Assert.That(moverCalls, Is.Zero, "never even screens");
            Assert.That(await ScansAsync(), Is.Empty);
            Assert.That(await ActiveStrategiesAsync(), Is.Empty);
            Assert.That((await AuditAsync("auto-gapper-blocked")).Single().Message, Does.Contain("Auto-gapper aborted"));
        });
    }

    [Test]
    public async Task PaperModeButLiveBroker_DryRun_WritesNoAuditRow()
    {
        router.Register(new FakeBroker(BrokerType.Sandbox, isPaper: false));
        var summary = await Scanner(("AAA", 30, 10)).RunScanAsync(user, dryRun: true, "auto", CancellationToken.None);
        Assert.Multiple(async () =>
        {
            Assert.That(summary.Note, Does.Contain("routes LIVE"));
            Assert.That(await AuditAsync("auto-gapper-blocked"), Is.Empty);
        });
    }

    [Test]
    public async Task MoversScreenFailure_IsReportedNotThrown()
    {
        var scanner = new AutoGapperScanner(strategies, feed, new UserBrokerResolver(keys, router, NullLogger<UserBrokerResolver>.Instance), keys,
            new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance), db, settings, NullLogger<AutoGapperScanner>.Instance)
        {
            FetchMovers = (_, _, _) => throw new HttpRequestException("screener down"),
        };
        var summary = await scanner.RunScanAsync(user, dryRun: false, "auto", CancellationToken.None);
        Assert.That(summary.Note, Is.EqualTo("movers screen failed: screener down"));
    }

    // ── Qualification and ranking ───────────────────────────────────────────────

    [Test]
    public async Task Qualification_ByMinGapAndMinPrice_DeduplicatingSymbols()
    {
        var summary = await Scanner(
            ("GOOD", 20, 10), ("good", 25, 10),   // duplicate symbol, case-insensitive
            ("EDGE", 15, 1),                       // exactly at both floors
            ("SMALLGAP", 14.9, 10),
            ("CHEAP", 40, 0.99)).RunScanAsync(user, dryRun: false, "auto", CancellationToken.None);

        var rows = await CandidatesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(summary.Screened, Is.EqualTo(5));
            Assert.That(summary.Qualified, Is.EqualTo(2));
            Assert.That(rows.Select(r => r.Symbol), Is.EquivalentTo(new[] { "GOOD", "EDGE" }));
            Assert.That(rows.Single(r => r.Symbol == "GOOD").GapPercent, Is.EqualTo(20), "the first screener row wins");
        });
    }

    [Test]
    public async Task Ranking_IsGapTimesOnePlusVolumeRatio_WithMaxCountCutoff()
    {
        settings.AutoGapperMaxCount = 2;
        // YYY: +16% with premarket volume equal to its daily average (ratio 1) → score 32, beating XXX's 20.
        feed.Set("YYY", prevClose: 10, atrPct: 7, dailyVolume: 1000, premarketVolume: 1000, premarketClose: 11.6);

        var summary = await Scanner(("XXX", 20, 10), ("YYY", 16, 10), ("ZZZ", 40, 10))
            .RunScanAsync(user, dryRun: false, "auto", CancellationToken.None);

        var rows = await CandidatesAsync();
        var scan = (await ScansAsync()).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(rows.Select(r => (r.Symbol, r.Rank)), Is.EqualTo(new[] { ("ZZZ", 1), ("YYY", 2), ("XXX", 3) }));
            Assert.That(rows[1].VolumeRatio, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(rows[1].Score, Is.EqualTo(32.0).Within(1e-6));
            Assert.That(rows[0].Armed && rows[1].Armed, Is.True);
            Assert.That(rows[2].Armed, Is.False);
            Assert.That(rows[2].SkipReason, Is.EqualTo("below-rank-cutoff"));
            Assert.That(rows[0].StrategyId, Is.Not.Null);
            Assert.That(summary.Armed, Is.EqualTo(2));
            Assert.That(summary.Skipped, Is.EqualTo(1));
            Assert.That(summary.Note, Is.EqualTo("complete"));
            Assert.That((scan.Armed, scan.Skipped, scan.Qualified, scan.MoversScreened, scan.MaxCount), Is.EqualTo((2, 1, 3, 3, 2)));
            Assert.That(scan.ScanCompletedUtc, Is.Not.Null);
            Assert.That((await ActiveStrategiesAsync()).Select(s => s.Symbol), Is.EquivalentTo(new[] { "ZZZ", "YYY" }));
            Assert.That((await AuditAsync("auto-gapper-scan")).Single().Message, Does.Contain("armed 2 of 3"));
        });
    }

    [Test]
    public async Task DuplicateActiveStrategy_IsSkipped()
    {
        var existing = await strategies.CreateAsync(user, "mine", "ZZZ", "Ticker(ZZZ)");
        await strategies.SetActiveAsync(existing.Id, true, user);

        var summary = await Scanner(("ZZZ", 40, 10), ("XXX", 20, 10)).RunScanAsync(user, dryRun: false, "auto", CancellationToken.None);

        var rows = await CandidatesAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(rows.Single(r => r.Symbol == "ZZZ").SkipReason, Is.EqualTo("duplicate-active"));
            Assert.That(rows.Single(r => r.Symbol == "ZZZ").Armed, Is.False);
            Assert.That(rows.Single(r => r.Symbol == "XXX").Armed, Is.True);
            Assert.That(summary.Armed, Is.EqualTo(1));
            Assert.That((await ActiveStrategiesAsync()).Count(s => s.Symbol == "ZZZ"), Is.EqualTo(1), "no second ZZZ");
        });
    }

    [Test]
    public async Task DryRun_WritesNoRows_AndArmsNothing_ButCountsWhatWouldArm()
    {
        settings.AutoGapperMaxCount = 1;
        var summary = await Scanner(("ZZZ", 40, 10), ("XXX", 20, 10)).RunScanAsync(user, dryRun: true, "auto", CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(summary.Armed, Is.EqualTo(1), "would arm");
            Assert.That(summary.Skipped, Is.EqualTo(1));
            Assert.That(summary.Note, Is.EqualTo("dry-run (nothing armed or written)"));
            Assert.That(await ScansAsync(), Is.Empty);
            Assert.That(await CandidatesAsync(), Is.Empty);
            Assert.That(await ActiveStrategiesAsync(), Is.Empty);
            Assert.That(await AuditAsync("auto-gapper-scan"), Is.Empty);
        });
    }

    [Test]
    public async Task OneTickerWhoseSynthesisThrows_DoesNotAbortTheOthers()
    {
        settings.AutoGapperMinPrice = 0;
        // A $0 print gives a degenerate price band (0.1..0), which GapperProfile.Validate rejects.
        var summary = await Scanner(("BAD", 30, 0), ("GOOD", 20, 10)).RunScanAsync(user, dryRun: false, "auto", CancellationToken.None);

        var rows = await CandidatesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(summary.Qualified, Is.EqualTo(2));
            Assert.That(rows.Select(r => r.Symbol), Is.EqualTo(new[] { "GOOD" }));
            Assert.That(rows.Single().Armed, Is.True);
            Assert.That(summary.Armed, Is.EqualTo(1));
        });
    }

    // ── Synthesized profile tiers ──────────────────────────────────────────────

    [Test]
    public async Task Profiles_ByPriceTier_StopClampedToAtr_TrailingNullForLarge_GivebackNarrowsWithGap_NotionalClamped()
    {
        feed.Set("PEN", prevClose: null, atrPct: 20, dailyVolume: 1000, premarketVolume: null, premarketClose: null);
        feed.Set("MID", prevClose: null, atrPct: 2, dailyVolume: 1000, premarketVolume: null, premarketClose: null);
        feed.Set("MIDS", prevClose: null, atrPct: 2, dailyVolume: 1000, premarketVolume: null, premarketClose: null);
        feed.Set("BIG", prevClose: 50, atrPct: 3, dailyVolume: 1000, premarketVolume: 1000, premarketClose: 57.5);

        await Scanner(("PEN", 20, 3), ("MID", 40, 10), ("MIDS", 20, 10), ("BIG", 15, 50))
            .RunScanAsync(user, dryRun: false, "auto", CancellationToken.None);

        var rows = (await CandidatesAsync()).ToDictionary(r => r.Symbol);
        var pen = rows["PEN"];
        var mid = rows["MID"];
        var mids = rows["MIDS"];
        var big = rows["BIG"];
        Assert.Multiple(() =>
        {
            // Penny (< $5): ATR 20% clamps the stop to the 12% ceiling; trail 1.25x; giveback capped at 22.
            Assert.That(pen.BehaviorClass, Is.EqualTo("penny"));
            Assert.That(pen.StopLossPercent, Is.EqualTo(12.0));
            Assert.That(pen.TrailingStopPercent, Is.EqualTo(15.0));
            Assert.That(pen.PeakGivebackPercent, Is.EqualTo(22.0));
            Assert.That(pen.Notional, Is.EqualTo(250m), "400 / volatility factor 1.6, to the nearest 50");
            Assert.That((pen.ArmExitEt, pen.SellByEt), Is.EqualTo(("09:10", "09:27")));

            // Mid (< $20): ATR 2% clamps the stop up to the 4% floor; trail 1.5x.
            Assert.That(mid.BehaviorClass, Is.EqualTo("mid"));
            Assert.That(mid.StopLossPercent, Is.EqualTo(4.0));
            Assert.That(mid.TrailingStopPercent, Is.EqualTo(6.0));
            Assert.That(mid.Notional, Is.EqualTo(1050m), "800 / volatility factor 0.75, to the nearest 50");

            // Bigger gap → tighter giveback: +40% gives 20, +20% gives 28 (the mid cap).
            Assert.That(mid.PeakGivebackPercent, Is.EqualTo(20.0).Within(1e-9));
            Assert.That(mids.PeakGivebackPercent, Is.EqualTo(28.0).Within(1e-9));

            // Large: stop equals its 3% ATR (inside 2.5..5), no trailing stop, notional clamped to 3000.
            Assert.That(big.BehaviorClass, Is.EqualTo("large"));
            Assert.That(big.StopLossPercent, Is.EqualTo(3.0).Within(1e-9));
            Assert.That(big.TrailingStopPercent, Is.Null);
            Assert.That(big.PeakGivebackPercent, Is.EqualTo(30.0).Within(1e-9));
            Assert.That(big.GapPercent, Is.EqualTo(15.0).Within(1e-9), "recomputed from previous close and the premarket price");
            Assert.That(big.Price, Is.EqualTo(57.5));
            Assert.That(big.Notional, Is.EqualTo(3000m), "1500 x 1.6 / 0.75 = 3200, clamped to 3000");
            Assert.That((big.PriceBandLow, big.PriceBandHigh), Is.EqualTo((34.5, 103.5)));
        });
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────

    /// <summary>Per-symbol daily bars (volume + ATR), premarket minute bars and previous close.</summary>
    private sealed class ScriptedFeed : IMarketDataFeed
    {
        private readonly Dictionary<string, (double? PrevClose, double? AtrPct, double DailyVolume, long? PmVolume, double? PmClose)> specs =
            new(StringComparer.OrdinalIgnoreCase);

        public string FeedName => "Scripted";

        public void Set(string symbol, double? prevClose, double? atrPct, double dailyVolume, long? premarketVolume, double? premarketClose) =>
            specs[symbol] = (prevClose, atrPct, dailyVolume, premarketVolume, premarketClose);

        public Task<decimal?> GetPreviousCloseAsync(string symbol, DateTime beforeUtc, CancellationToken ct = default) =>
            Task.FromResult(specs.TryGetValue(symbol, out var s) && s.PrevClose is { } p ? (decimal?)p : null);

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (!specs.TryGetValue(symbol, out var s)) yield break;
            if (candleSize >= TimeSpan.FromDays(1))
            {
                if (s.AtrPct is not { } atr) yield break;
                // Close 10, high/low spread so (H - L) / C = atr %.
                for (var i = 0; i < 5; i++)
                    yield return new Candle
                    {
                        Symbol = symbol, StartUtc = startUtc.AddDays(i), EndUtc = startUtc.AddDays(i + 1),
                        Open = 10m, Close = 10m, High = 10m + (decimal)(atr / 20), Low = 10m - (decimal)(atr / 20), Volume = (decimal)s.DailyVolume,
                    };
            }
            else if (s.PmVolume is { } vol)
            {
                yield return new Candle { Symbol = symbol, StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1), Close = (decimal)(s.PmClose ?? 0), Volume = vol / 2 };
                yield return new Candle { Symbol = symbol, StartUtc = startUtc.AddMinutes(1), EndUtc = startUtc.AddMinutes(2), Close = (decimal)(s.PmClose ?? 0), Volume = vol - vol / 2 };
            }
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
            throw new InvalidOperationException("the scanner only arms strategies; it must never place an order");
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new InvalidOperationException("never called");
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }
}
