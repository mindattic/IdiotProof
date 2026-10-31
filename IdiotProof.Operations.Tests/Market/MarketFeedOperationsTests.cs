using IdiotProof.Core.Data;
using IdiotProof.DataFeeds;
using IdiotProof.Operations.Market;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Market;

/// <summary>market feed status|switch (IP-T205): runtime feed switching, and never Mock under a real strategy (IP-LAW-9).</summary>
[NonParallelizable] // the switch writes the process-wide IDIOTPROOF_ALPACA_FEED knob
public class MarketFeedOperationsTests
{
    private string? savedTier;
    private SwitchableMarketDataFeed feed = null!;
    private AlpacaDataFeed alpaca = null!;
    private ServiceProvider services = null!;

    [SetUp]
    public void SetUp()
    {
        savedTier = Environment.GetEnvironmentVariable(MarketData.FeedEnvVar);
        alpaca = new AlpacaDataFeed("k", "s", "sip"); // never called: the operations only read and set its tier
        feed = new SwitchableMarketDataFeed("Alpaca");
        feed.Register(alpaca);
        feed.Register(new MockDataFeed());
        services = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"feed-{Guid.NewGuid():N}"))
            .AddSingleton<StrategyRepository>()
            .AddSingleton<IMarketDataFeed>(feed)
            .BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        Environment.SetEnvironmentVariable(MarketData.FeedEnvVar, savedTier);
        await alpaca.DisposeAsync();
        await services.DisposeAsync();
    }

    private static readonly Guid Owner = Guid.NewGuid();

    private OperationContext Context() => new() { Services = services, OwnerId = Owner };

    private async Task AddActiveAsync(string symbol, string mode)
    {
        await using var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        db.Strategies.Add(new Strategy { OwnerUserId = Owner, Title = symbol + " test", Symbol = symbol, IsActive = true, BrokerMode = mode });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Status_ReportsTheActiveFeedTierAndStrategiesPerMode()
    {
        await AddActiveAsync("INTC", "Paper");
        await AddActiveAsync("NVDA", "Sandbox");

        var result = await new MarketFeedStatusOperation().ExecuteAsync(new NoArgs(), Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var state = (MarketFeedState)result.Data!;
        Assert.Multiple(() =>
        {
            Assert.That(state.Active, Is.EqualTo("Alpaca"));
            Assert.That(state.AlpacaTier, Is.EqualTo("sip"));
            Assert.That(state.Synthetic, Is.False);
            Assert.That(state.Registered, Is.EqualTo(new[] { "Alpaca", "Mock" }));
            Assert.That(state.ActiveStrategiesByMode["Paper"], Is.EqualTo(1));
            Assert.That(result.Human, Does.Contain("1 Paper, 1 Sandbox"));
        });
    }

    [Test]
    public async Task SwitchToMock_IsRefusedWhileAPaperOrLiveStrategyIsActive()
    {
        await AddActiveAsync("INTC", "Live");

        var result = await new MarketFeedSwitchOperation().ExecuteAsync(new MarketFeedSwitchArgs { Feed = "mock" }, Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("IP-LAW-9").And.Contain("INTC"));
        Assert.That(feed.IsSynthetic(), Is.False, "the feed must not have moved");
    }

    [Test]
    public async Task SwitchToMock_WithOnlySandboxActive_ThenBackToIex()
    {
        await AddActiveAsync("NVDA", "Sandbox");

        var toMock = await new MarketFeedSwitchOperation().ExecuteAsync(new MarketFeedSwitchArgs { Feed = "mock" }, Context());
        Assert.That(toMock.IsOk, Is.True, toMock.Message);
        Assert.That(feed.IsSynthetic(), Is.True);
        Assert.That(feed.FeedName, Is.EqualTo("Mock"), "the Monitor's IP-LAW-9 guard keys off FeedName");

        var toIex = await new MarketFeedSwitchOperation().ExecuteAsync(new MarketFeedSwitchArgs { Feed = "iex" }, Context());
        Assert.That(toIex.IsOk, Is.True, toIex.Message);
        Assert.Multiple(() =>
        {
            Assert.That(feed.IsSynthetic(), Is.False);
            Assert.That(alpaca.FeedTier, Is.EqualTo("iex"));
            Assert.That(MarketData.Feed, Is.EqualTo("iex"), "market ... operations follow the switch");
        });
    }

    [Test]
    public async Task UnknownFeed_IsAUsageError()
    {
        var result = await new MarketFeedSwitchOperation().ExecuteAsync(new MarketFeedSwitchArgs { Feed = "polygon" }, Context());
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
