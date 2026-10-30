using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Core.Catalogs;
using IdiotProof.Core.Hosting;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// AddIdiotProofCore is the one graph every front door registers (HOUSE-LAW-6). It must validate
/// on build (every registered service constructible, no scoped service captured by a singleton)
/// and resolve the services the Hub, the Monitor and the web app all depend on.
/// </summary>
[TestFixture]
public sealed class CoreServiceRegistrationTests
{
    private string root = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"core-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private ServiceProvider Build(CoreHostOptions options)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeyRingPath"] = Path.Combine(root, "keys"),
            })
            .Build();
        // What every host already supplies: configuration, logging and (web/Hub) routing.
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddRouting()
            .AddIdiotProofCore(configuration, new WebStorageProvider(root), options)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Test]
    public void AddIdiotProofCore_ValidatesOnBuild_ForWebAndConsoleHosts()
    {
        Assert.DoesNotThrow(() => Build(new CoreHostOptions()).Dispose());
        Assert.DoesNotThrow(() => Build(new CoreHostOptions { PinDevKeyRing = true }).Dispose());
    }

    [Test]
    public async Task AddIdiotProofCore_ResolvesTheSharedServices()
    {
        // Async disposal: OptionsTradingService owns an IAsyncDisposable broker client.
        await using var sp = Build(new CoreHostOptions { PinDevKeyRing = true });
        await using var scope = sp.CreateAsyncScope();

        Assert.Multiple(() =>
        {
            Assert.That(sp.GetService<IDbContextFactory<AppDbContext>>(), Is.Not.Null);
            Assert.That(sp.GetService<AppSettings>(), Is.Not.Null);
            Assert.That(sp.GetService<IStaticCatalogSource>(), Is.Not.Null);
            Assert.That(sp.GetService<StrategyRepository>(), Is.Not.Null);
            Assert.That(sp.GetService<UserClaudeKeyResolver>(), Is.Not.Null);
            Assert.That(sp.GetService<RiskGuardianService>(), Is.Not.Null);
            Assert.That(sp.GetService<TradeDiaryRepository>(), Is.Not.Null);
            Assert.That(sp.GetService<GapperInterpreter>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetService<OptionsTradingService>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetService<ResearchService>(), Is.Not.Null);
            Assert.That(sp.GetService<IdiotProofDatabase>()?.ConnectionString, Is.Not.Empty);
        });
    }

    [Test]
    public void AddIdiotProofTrading_ValidatesOnBuild_AndRoutesBrokersPerStrategy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = Path.Combine(root, "keys") })
            .Build();
        using var sp = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddRouting()
            .AddIdiotProofCore(configuration, new WebStorageProvider(root), new CoreHostOptions { PinDevKeyRing = true })
            .AddIdiotProofTrading()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.That(sp.GetService<UserBrokerResolver>(), Is.Not.Null);
    }

    [Test]
    public void CreateRouter_WithoutKeysOrOptIn_IsSandbox_AndFeedIsMock()
    {
        // IP-LAW-3 / IP-LAW-9: no keys → Sandbox broker and Mock data, never a live account.
        var savedBroker = Environment.GetEnvironmentVariable(TradingServiceRegistration.BrokerEnvVar);
        var savedFeed = Environment.GetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(TradingServiceRegistration.BrokerEnvVar, "alpaca");
            Environment.SetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar, null);
            var bare = new AppSettings();

            var router = TradingServiceRegistration.CreateRouter(bare);

            Assert.That(router.GetActiveBroker().BrokerType, Is.EqualTo(BrokerType.Sandbox),
                "asking for alpaca without registered keys falls back to Sandbox");
            Assert.That(TradingServiceRegistration.CreateFeed(bare), Is.InstanceOf<MockDataFeed>());
        }
        finally
        {
            Environment.SetEnvironmentVariable(TradingServiceRegistration.BrokerEnvVar, savedBroker);
            Environment.SetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar, savedFeed);
        }
    }

    [Test]
    public void SharedKeyRing_PrefersAzure_ThenExplicitPath_ThenPinnedDevRing_ElseLibraryDefault()
    {
        static IConfiguration Config(params (string Key, string Value)[] pairs) =>
            new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => KeyValuePair.Create(p.Key, (string?)p.Value))).Build();

        Assert.Multiple(() =>
        {
            Assert.That(CoreServiceRegistration.SharedKeyRing(Config(), pinDevKeyRing: false), Is.Null,
                "web host with no ring configured keeps the library default (fail-closed in production)");
            Assert.That(CoreServiceRegistration.SharedKeyRing(Config(), pinDevKeyRing: true), Is.Not.Null);
            Assert.That(CoreServiceRegistration.SharedKeyRing(Config(("DataProtection:KeyRingPath", root)), false), Is.Not.Null);
            Assert.That(CoreServiceRegistration.SharedKeyRing(Config(
                ("DataProtection:AzureBlobUri", "https://example.blob.core.windows.net/dp/keys.xml"),
                ("DataProtection:KeyVaultKeyUri", "https://example.vault.azure.net/keys/dp")), false), Is.Not.Null);
        });
    }
}
