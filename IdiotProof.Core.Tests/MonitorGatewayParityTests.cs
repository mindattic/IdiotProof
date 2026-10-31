using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Monitor;
using IdiotProof.Shared.Risk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T94: the Monitor's entries and exits now reach the broker through the TransactionGateway.
/// These pin behavior parity with the direct-to-broker path it replaced: the exact order shapes
/// (DirectionalOrders), Live fires without a confirm code (activating the strategy in Live was
/// the confirmed act), the Mock-feed interlock on real entries only, exits that skip the LLM
/// panel, the same Risk Guardian verdict the Monitor used to compute itself, and an ambiguous
/// broker throw reported as such so the Monitor reconciles instead of re-firing.
/// </summary>
[TestFixture]
public sealed class MonitorGatewayParityTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid StrategyId = Guid.NewGuid();

    private Broker sandbox = null!, paper = null!, live = null!;
    private Routing routing = null!;
    private IDbContextFactory<AppDbContext> db = null!;

    [SetUp]
    public void SetUp()
    {
        sandbox = new Broker(BrokerType.Sandbox, isPaper: true);
        paper = new Broker(BrokerType.Alpaca, isPaper: true);
        live = new Broker(BrokerType.Alpaca, isPaper: false);
        routing = new Routing { [BrokerMode.Sandbox] = sandbox, [BrokerMode.Paper] = paper, [BrokerMode.Live] = live };
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"monitor-parity-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private TransactionGateway Gateway(IMarketDataFeed? feed = null) => new(
        routing,
        feed ?? new SwitchableMarketDataFeed(),
        new RiskGuardianService(db),
        new ConfirmationStore(db, TimeProvider.System),
        new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance),
        NullLogger<TransactionGateway>.Instance);

    private static readonly GatewayOptions Approved = new() { Caller = "monitor", LlmVerdict = (true, "3 of 3 approved") };

    private static OrderIntent Entry(string mode, TradeDirection direction = TradeDirection.Long, int qty = 10,
        decimal price = 100m, decimal? stop = null, bool ext = false) =>
        MonitorOrders.Entry(Owner, StrategyId, mode, "NVDA", direction, qty, price,
            stop ?? (direction == TradeDirection.Long ? 98m : 102m), null, ext, "test");

    private static OrderIntent Exit(string mode, TradeDirection direction = TradeDirection.Long, int qty = 10) =>
        MonitorOrders.Exit(Owner, StrategyId, mode, "NVDA", direction, qty, 100m, extendedHours: false, "test exit");

    [TestCase(TradeDirection.Long, true)]
    [TestCase(TradeDirection.Long, false)]
    [TestCase(TradeDirection.Short, true)]
    [TestCase(TradeDirection.Short, false)]
    public async Task Entry_PlacesExactlyTheDirectionalOrder_ItAlwaysPlaced(TradeDirection direction, bool ext)
    {
        var expected = DirectionalOrders.Entry("NVDA", direction, 10, 100m, ext);

        var outcome = await Gateway().SubmitAsync(Entry("Paper", direction, ext: ext), Approved);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        var sent = paper.Placed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Symbol, Is.EqualTo(expected.Symbol));
            Assert.That(sent.Quantity, Is.EqualTo(expected.Quantity));
            Assert.That(sent.Side, Is.EqualTo(expected.Side));
            Assert.That(sent.Type, Is.EqualTo(expected.Type));
            Assert.That(sent.LimitPrice, Is.EqualTo(expected.LimitPrice));
            Assert.That(sent.TimeInForce, Is.EqualTo(expected.TimeInForce));
            Assert.That(sent.ExtendedHours, Is.EqualTo(expected.ExtendedHours));
            Assert.That(sent.PositionIntent, Is.EqualTo(expected.PositionIntent));
        });
    }

    [TestCase(TradeDirection.Long)]
    [TestCase(TradeDirection.Short)]
    public async Task Exit_PlacesExactlyTheDirectionalExit_AlwaysToClose(TradeDirection direction)
    {
        var expected = DirectionalOrders.Exit("NVDA", direction, 7, 100m, false);

        var outcome = await Gateway().SubmitAsync(Exit("Paper", direction, 7), new GatewayOptions { Caller = "monitor" });

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        var sent = paper.Placed.Single();
        Assert.That((sent.Side, sent.Quantity, sent.LimitPrice, sent.PositionIntent),
            Is.EqualTo((expected.Side, expected.Quantity, expected.LimitPrice, expected.PositionIntent)));
    }

    [Test]
    public async Task LiveStrategy_FiresWithoutAConfirmCode_ActivationWasTheYes()
    {
        var entry = await Gateway().SubmitAsync(Entry("Live"), Approved);
        var exit = await Gateway().SubmitAsync(Exit("Live"), new GatewayOptions { Caller = "monitor" });

        Assert.That(entry.Status, Is.EqualTo(GatewayStatus.Placed), entry.Message);
        Assert.That(exit.Status, Is.EqualTo(GatewayStatus.Placed), exit.Message);
        Assert.That(entry.ConfirmationCode, Is.Null);
        Assert.That(live.Placed, Has.Count.EqualTo(2));
        await using var ctx = await db.CreateDbContextAsync();
        Assert.That(ctx.PendingConfirmations.Count(), Is.Zero, "no code was ever issued for the Monitor");
    }

    [Test]
    public async Task VoterPanelOff_StillFires_AsBefore()
    {
        var outcome = await Gateway().SubmitAsync(Entry("Paper"),
            new GatewayOptions { Caller = "monitor", LlmVerdict = (true, MonitorOrders.PanelOffDetail) });

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
    }

    [Test]
    public async Task NoPanelVerdict_IsBlocked_FailClosed()
    {
        var outcome = await Gateway().SubmitAsync(Entry("Paper"), new GatewayOptions { Caller = "monitor" });

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(paper.Placed, Is.Empty);
    }

    [Test]
    public async Task MockFeed_BlocksRealEntries_ButNotSandboxEntries_NorRealExits()
    {
        var gateway = Gateway(new MockDataFeed());

        var realEntry = await gateway.SubmitAsync(Entry("Paper"), Approved);
        var liveEntry = await gateway.SubmitAsync(Entry("Live"), Approved);
        var simEntry = await gateway.SubmitAsync(Entry("Sandbox"), Approved);
        var realExit = await gateway.SubmitAsync(Exit("Paper"), new GatewayOptions { Caller = "monitor" });

        Assert.Multiple(() =>
        {
            Assert.That(realEntry.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(liveEntry.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(simEntry.Status, Is.EqualTo(GatewayStatus.Placed));
            Assert.That(realExit.Status, Is.EqualTo(GatewayStatus.Placed), "a held position must always be flatten-able");
            Assert.That(MonitorOrders.WhyNot(realEntry), Does.StartWith("data:"));
            Assert.That(live.Placed, Is.Empty);
        });
    }

    [Test]
    public async Task Exit_NeverWaitsOnTheVoterPanel()
    {
        var outcome = await Gateway().SubmitAsync(Exit("Paper"), new GatewayOptions { Caller = "monitor", LlmVerdict = (false, "would have vetoed") });

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(outcome.Verdicts.Any(v => v.Gate == "llm"), Is.False);
    }

    /// <summary>
    /// The Risk Guardian sees the setup the Monitor used to build itself: entry at the signal price
    /// (not the +0.2% marketable limit), the strategy's stop, the fire's quantity. Each case is
    /// judged both ways and must agree — including a stop that is too tight at the signal price
    /// but would look wide enough measured from the buffered limit.
    /// </summary>
    [TestCase(TradeDirection.Long, 10, 100, 98)]     // approved
    [TestCase(TradeDirection.Long, 10, 100, 99.6)]   // 0.4% — too tight (0.6% from the limit would pass)
    [TestCase(TradeDirection.Long, 500, 100, 98)]    // past the per-trade cap
    [TestCase(TradeDirection.Long, 10, 100, 101)]    // stop on the wrong side
    [TestCase(TradeDirection.Short, 10, 100, 102)]   // approved short
    [TestCase(TradeDirection.Short, 10, 100, 100.4)] // too tight short
    public async Task RiskGuardianVerdict_MatchesTheMonitorsOwnSetup(TradeDirection direction, int qty, decimal price, decimal stop)
    {
        var old = new TradeSetup
        {
            Symbol = "NVDA", Direction = direction, EntryPrice = price, EntryType = OrderType.Limit, StopLoss = stop,
            TakeProfit = direction == TradeDirection.Long ? price + (price - stop) : price - (stop - price),
            Quantity = qty,
        };
        var expected = new RiskGuardian(new RiskGuardianConfig()).ValidateTrade(old).IsApproved;

        var outcome = await Gateway().SubmitAsync(Entry("Sandbox", direction, qty, price, stop), Approved);

        Assert.That(outcome.Status == GatewayStatus.Placed, Is.EqualTo(expected), MonitorOrders.WhyNot(outcome));
    }

    [Test]
    public async Task BrokerThrow_IsReportedAsAmbiguous_SoTheMonitorReconciles()
    {
        paper.Throw = true;

        var outcome = await Gateway().SubmitAsync(Entry("Paper"), Approved);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Rejected));
        Assert.That(outcome.BrokerThrew, Is.True);
        Assert.That(outcome.Order!.Message, Does.Contain("connection reset"));
    }

    [Test]
    public async Task CleanBrokerRefusal_IsNotAThrow()
    {
        paper.Refuse = "insufficient buying power";

        var outcome = await Gateway().SubmitAsync(Entry("Paper"), Approved);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Rejected));
        Assert.That(outcome.BrokerThrew, Is.False);
    }

    [Test]
    public async Task LiveStrategy_WithoutLiveKeys_IsRefused_NotQuietlySimulated()
    {
        routing[BrokerMode.Live] = sandbox; // what UserBrokerResolver hands back without live keys

        var entry = await Gateway().SubmitAsync(Entry("Live"), Approved);
        var exit = await Gateway().SubmitAsync(Exit("Live"), new GatewayOptions { Caller = "monitor" });

        Assert.That(entry.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(exit.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(sandbox.Placed, Is.Empty);
    }

    [Test]
    public void StrategyMode_ParsesToTheRoute_UnknownIsSandbox()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Entry("Live").Mode, Is.EqualTo(BrokerMode.Live));
            Assert.That(Entry("paper").Mode, Is.EqualTo(BrokerMode.Paper));
            Assert.That(Entry("Sandbox").Mode, Is.EqualTo(BrokerMode.Sandbox));
            Assert.That(Entry("garbage").Mode, Is.EqualTo(BrokerMode.Sandbox));
            Assert.That(Entry("Paper").Origin, Is.EqualTo(OrderOrigin.Monitor));
            Assert.That(Entry("Paper").StrategyId, Is.EqualTo(StrategyId));
        });
    }

    private sealed class Routing : Dictionary<BrokerMode, IBrokerClient>, IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(this[mode]);
    }

    private sealed class Broker(BrokerType type, bool isPaper) : IBrokerClient
    {
        public List<OrderRequest> Placed { get; } = [];
        public string? Refuse { get; set; }
        public bool Throw { get; set; }
        public BrokerType BrokerType => type;
        public bool IsPaper => isPaper;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            if (Throw) throw new HttpRequestException("connection reset");
            if (Refuse is not null) return Task.FromResult(new OrderResult { IsSuccess = false, Message = Refuse });
            Placed.Add(request);
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = $"ord-{Placed.Count}" });
        }
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }
}
