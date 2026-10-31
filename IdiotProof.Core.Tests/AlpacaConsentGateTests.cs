using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T12: the Alpaca consent gate in <see cref="OptionsTradingService"/> and
/// <see cref="AccountSummaryService"/>. A key existing is not permission: the user must have opted into
/// Alpaca routing, and since IP-T330 the pair comes only from the user's own Vault entries (legacy SQL
/// secret columns are never read). Runs on an in-memory database and a temp-directory Vault, and never
/// reaches Alpaca: every account-summary case here stops before a broker call.
/// </summary>
[TestFixture]
public sealed class AlpacaConsentGateTests
{
    private TempVault vault = null!;
    private IDbContextFactory<AppDbContext> factory = null!;
    private UserKeyService keys = null!;
    private OptionsTradingService options = null!;

    [SetUp]
    public void SetUp()
    {
        vault = new TempVault();
        factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"consent-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        keys = new UserKeyService(factory, new EphemeralDataProtectionProvider(), vault.Vault, NullLogger<UserKeyService>.Instance);
        options = Options(keys, new SettingsRepository(factory));
    }

    [TearDown]
    public async Task TearDown()
    {
        await options.DisposeAsync();
        vault.Dispose();
    }

    private OptionsTradingService Options(UserKeyService userKeys, SettingsRepository settings) =>
        new(userKeys, null!, null!, factory, settings,
            new RiskGuardianService(factory), new ConfirmationStore(factory, TimeProvider.System),
            new AuditLogRepository(factory, NullLogger<AuditLogRepository>.Instance), new TradeDiaryRepository(factory),
            NullLogger<TransactionGateway>.Instance, NullLogger<OptionsTradingService>.Instance);

    private async Task<Guid> UserAsync(string broker, bool paperPair = false, bool livePair = false)
    {
        var id = Guid.NewGuid();
        await keys.SaveAsync(id, new UserApiKeys
        {
            UserId = id,
            DefaultBroker = broker,
            AlpacaApiKeyId = paperPair ? "PKPAPER" : null,
            AlpacaApiSecretKey = paperPair ? "paper-secret" : null,
            AlpacaLiveApiKeyId = livePair ? "AKLIVE" : null,
            AlpacaLiveApiSecretKey = livePair ? "live-secret" : null,
        });
        return id;
    }

    /// <summary>A key service whose every lookup throws (a DB outage).</summary>
    private UserKeyService ThrowingKeys() =>
        new(new ThrowingFactory(), new EphemeralDataProtectionProvider(), vault.Vault, NullLogger<UserKeyService>.Instance);

    // ── OptionsTradingService.ResolveBrokerAsync ───────────────────────────────

    [Test]
    public async Task Resolve_SandboxMode_IsSandbox_EvenWithAlpacaReady()
    {
        var user = await UserAsync("alpaca", paperPair: true, livePair: true);
        var r = await options.ResolveBrokerAsync(user, OptionsBrokerMode.Sandbox);
        Assert.Multiple(() =>
        {
            Assert.That(r.Client, Is.SameAs(options.Sandbox));
            Assert.That(r.Mode, Is.EqualTo(OptionsBrokerMode.Sandbox));
            Assert.That(r.IsAlpaca, Is.False);
            Assert.That(r.FallbackReason, Is.Null);
        });
    }

    [Test]
    public async Task Resolve_NotOptedIntoAlpaca_FallsBackToSandboxWithAReason()
    {
        var user = await UserAsync("sandbox", paperPair: true, livePair: true);
        foreach (var mode in new[] { OptionsBrokerMode.Paper, OptionsBrokerMode.Live })
        {
            var r = await options.ResolveBrokerAsync(user, mode);
            Assert.Multiple(() =>
            {
                Assert.That(r.Client, Is.SameAs(options.Sandbox), mode.ToString());
                Assert.That(r.Mode, Is.EqualTo(OptionsBrokerMode.Sandbox));
                Assert.That(r.IsAlpaca, Is.False);
                Assert.That(r.FallbackReason, Does.Contain("routing isn't enabled"));
            });
        }
    }

    [Test]
    public async Task Resolve_OptedInButMissingThePair_FallsBackToSandboxNamingTheMode()
    {
        var user = await UserAsync("alpaca", paperPair: true);
        var live = await options.ResolveBrokerAsync(user, OptionsBrokerMode.Live);
        var other = await UserAsync("Alpaca", livePair: true);
        var paper = await options.ResolveBrokerAsync(other, OptionsBrokerMode.Paper);

        Assert.Multiple(() =>
        {
            Assert.That(live.IsAlpaca, Is.False);
            Assert.That(live.FallbackReason, Is.EqualTo("no live Alpaca key pair is configured"));
            Assert.That(paper.IsAlpaca, Is.False);
            Assert.That(paper.FallbackReason, Is.EqualTo("no paper Alpaca key pair is configured"));
        });
    }

    [Test]
    public async Task Resolve_LegacySqlSecretColumns_AreNeverUsed()
    {
        var user = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserApiKeys.Add(new UserApiKeys
            {
                UserId = user, DefaultBroker = "alpaca",
                AlpacaApiKeyId = "PKFROMSQL", AlpacaApiSecretKey = "sql-secret",
                AlpacaLiveApiKeyId = "AKFROMSQL", AlpacaLiveApiSecretKey = "sql-live-secret",
            });
            await db.SaveChangesAsync();
        }

        var r = await options.ResolveBrokerAsync(user, OptionsBrokerMode.Paper);
        Assert.Multiple(() =>
        {
            Assert.That(r.IsAlpaca, Is.False, "Vault is the only source of an Alpaca pair (IP-T330)");
            Assert.That(r.FallbackReason, Is.EqualTo("no paper Alpaca key pair is configured"));
        });
    }

    [Test]
    public async Task Resolve_CompleteVaultPair_ResolvesAlpaca_AndCachesTheClientPerMode()
    {
        var user = await UserAsync("alpaca", paperPair: true, livePair: true);

        var paper1 = await options.ResolveBrokerAsync(user, OptionsBrokerMode.Paper);
        var live1 = await options.ResolveBrokerAsync(user, OptionsBrokerMode.Live);
        vault.Vault.RemoveAlpaca(user, isPaper: true);
        var paper2 = await options.ResolveBrokerAsync(user, OptionsBrokerMode.Paper);

        Assert.Multiple(() =>
        {
            Assert.That(paper1.IsAlpaca, Is.True);
            Assert.That(paper1.Mode, Is.EqualTo(OptionsBrokerMode.Paper));
            Assert.That(paper1.FallbackReason, Is.Null);
            Assert.That(paper1.Client, Is.InstanceOf<AlpacaBrokerClient>());
            Assert.That(((AlpacaBrokerClient)paper1.Client).IsPaper, Is.True);
            Assert.That(((AlpacaBrokerClient)live1.Client).IsPaper, Is.False);
            Assert.That(live1.Client, Is.Not.SameAs(paper1.Client));
            Assert.That(paper2.Client, Is.SameAs(paper1.Client), "cached for the circuit");
        });
    }

    // ── Risk-free rate ─────────────────────────────────────────────────────────

    [Test]
    public async Task RiskFreeRate_DefaultsTo4Percent_AndRoundTrips()
    {
        Assert.That(await options.GetRiskFreeRateAsync(), Is.EqualTo(0.04m));
        await options.SetRiskFreeRateAsync(0.0525m);
        Assert.Multiple(async () =>
        {
            Assert.That(await options.GetRiskFreeRateAsync(), Is.EqualTo(0.0525m));
            Assert.That(await new SettingsRepository(factory).GetAsync(OptionsTradingService.RiskFreeRateKey), Is.EqualTo("0.0525"));
        });
    }

    [Test]
    public async Task RiskFreeRate_SettingsFailure_FallsBackToDefault()
    {
        await using var broken = Options(keys, new SettingsRepository(new ThrowingFactory()));
        Assert.That(await broken.GetRiskFreeRateAsync(), Is.EqualTo(OptionsTradingService.DefaultRiskFreeRate));
    }

    // ── Bullish claims ─────────────────────────────────────────────────────────

    [Test]
    public async Task RecentBullishClaims_FilterByTickerSentimentAndDate_OrderedBySignificance()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = await factory.CreateDbContextAsync())
        {
            void Add(string ticker, string sentiment, int daysAgo, double? sig, string summary) =>
                db.ResearchClaims.Add(new ResearchClaim
                {
                    Ticker = ticker, Sentiment = sentiment, ArticleDate = today.AddDays(-daysAgo),
                    SignificanceScore = sig, ClaimSummary = summary,
                });
            Add("NVDA", "Bullish", 1, 40, "mid");
            Add("NVDA", "Bullish", 2, 90, "top");
            Add("NVDA", "Bullish", 3, null, "unscored");
            Add("NVDA", "Bearish", 1, 99, "bearish");
            Add("NVDA", "Bullish", 30, 95, "too old");
            Add("AMD", "Bullish", 1, 99, "other ticker");
            await db.SaveChangesAsync();
        }

        var claims = await options.GetRecentBullishClaimsAsync(" nvda ", DateTime.UtcNow.AddDays(-7));

        Assert.Multiple(() =>
        {
            Assert.That(claims.Select(c => c.Headline), Is.EqualTo(new[] { "top", "mid", "unscored" }));
            Assert.That(claims[0].Ticker, Is.EqualTo("NVDA"));
            Assert.That(claims[0].SignificanceScore, Is.EqualTo(90));
            Assert.That(claims[2].SignificanceScore, Is.Zero);
        });
    }

    [Test]
    public async Task RecentBullishClaims_CapsAt10()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 15; i++)
                db.ResearchClaims.Add(new ResearchClaim
                {
                    Ticker = "NVDA", Sentiment = "Bullish", ArticleDate = DateOnly.FromDateTime(DateTime.UtcNow), SignificanceScore = i,
                });
            await db.SaveChangesAsync();
        }
        Assert.That(await options.GetRecentBullishClaimsAsync("NVDA", DateTime.UtcNow.AddDays(-1)), Has.Count.EqualTo(10));
    }

    // ── AccountSummaryService ──────────────────────────────────────────────────

    [Test]
    public async Task Summary_NotOptedIn_ShowsNoAccountData_EvenWithVaultPairs()
    {
        var user = await UserAsync("sandbox", paperPair: true, livePair: true);
        var (paper, live) = await new AccountSummaryService(keys).GetSummaryAsync(user);

        Assert.Multiple(() =>
        {
            foreach (var s in new[] { paper, live })
            {
                Assert.That(s.Configured, Is.False, "an un-opted-in key is not 'configured'");
                Assert.That(s.Error, Is.Null);
                Assert.That(s.Cash, Is.Null);
                Assert.That(s.PortfolioValue, Is.Null);
                Assert.That(s.PositionCount, Is.Zero);
            }
            Assert.That(paper.IsPaper, Is.True);
            Assert.That(live.IsPaper, Is.False);
        });
    }

    [Test]
    public async Task Summary_OptedInWithoutKeys_IsAnEmptyCard()
    {
        var user = await UserAsync("alpaca");
        var (paper, live) = await new AccountSummaryService(keys).GetSummaryAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(paper.Configured, Is.False);
            Assert.That(paper.Error, Is.Null);
            Assert.That(live.Configured, Is.False);
            Assert.That(live.Cash, Is.Null);
        });
    }

    [Test]
    public async Task Summary_LegacySqlSecrets_AreNotAnAccount()
    {
        var user = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserApiKeys.Add(new UserApiKeys { UserId = user, DefaultBroker = "alpaca", AlpacaApiKeyId = "PKSQL", AlpacaApiSecretKey = "sql-secret" });
            await db.SaveChangesAsync();
        }
        var (paper, _) = await new AccountSummaryService(keys).GetSummaryAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(paper.Configured, Is.False);
            Assert.That(paper.Cash, Is.Null);
        });
    }

    [Test]
    public async Task Summary_KeyLookupThrows_DegradesToAnInlineError()
    {
        var (paper, live) = await new AccountSummaryService(ThrowingKeys()).GetSummaryAsync(Guid.NewGuid());
        Assert.Multiple(() =>
        {
            Assert.That(paper.Error, Does.StartWith("Could not load account info"));
            Assert.That(live.Error, Does.StartWith("Could not load account info"));
            Assert.That(paper.IsPaper, Is.True);
            Assert.That(live.IsPaper, Is.False);
            Assert.That(paper.Configured, Is.False);
        });
    }

    [Test]
    public async Task Detail_ExplainsWhyThereIsNoAccount()
    {
        var notOpted = await UserAsync("sandbox", paperPair: true);
        var noKeys = await UserAsync("alpaca");
        var svc = new AccountSummaryService(keys);

        var a = await svc.GetDetailAsync(notOpted, isPaper: true);
        var b = await svc.GetDetailAsync(noKeys, isPaper: false);
        var c = await new AccountSummaryService(ThrowingKeys()).GetDetailAsync(noKeys, isPaper: true);

        Assert.Multiple(() =>
        {
            Assert.That(a.Error, Does.Contain("Alpaca routing is off"));
            Assert.That(a.Configured, Is.False);
            Assert.That(a.Account, Is.Empty);
            Assert.That(b.Error, Does.Contain("No live Alpaca keys in Vault"));
            Assert.That(b.Positions, Is.Empty);
            Assert.That(c.Error, Does.StartWith("Could not load account keys"));
        });
    }

    private sealed class ThrowingFactory : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new InvalidOperationException("database unavailable");
    }
}
