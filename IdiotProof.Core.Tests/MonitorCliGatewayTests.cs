using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T95: the Monitor operator CLI's <c>flatten</c> and <c>test-order</c> go through the
/// TransactionGateway. Flatten keeps the Monitor's exit shape (DirectionalOrders.Exit) as a
/// person-asked Flatten intent; test-order is a $1 buy the Risk Guardian can size, cancelled
/// through the gateway too; Live waits for the operator to type the code back
/// (<see cref="GatewayConsole"/>), and no code means nothing is sent.
/// </summary>
[TestFixture]
public sealed class MonitorCliGatewayTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private Broker paper = null!, live = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private TransactionGateway gateway = null!;
    private readonly List<string> printed = [];

    [SetUp]
    public void SetUp()
    {
        paper = new Broker(BrokerType.Alpaca, isPaper: true);
        live = new Broker(BrokerType.Alpaca, isPaper: false);
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"monitor-cli-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        gateway = Build(new SwitchableMarketDataFeed());
        printed.Clear();
    }

    private TransactionGateway Build(IMarketDataFeed feed) => new(
        new Routing { [BrokerMode.Sandbox] = new Broker(BrokerType.Sandbox, true), [BrokerMode.Paper] = paper, [BrokerMode.Live] = live },
        feed,
        new RiskGuardianService(db),
        new ConfirmationStore(db, TimeProvider.System),
        new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance),
        NullLogger<TransactionGateway>.Instance);

    [Test]
    public async Task Flatten_IsTheMonitorsExitShape_AsAPersonAskedExit()
    {
        var intent = MonitorOrders.ManualFlatten(Owner, Guid.NewGuid(), "Paper", "NVDA", TradeDirection.Short, 4, 50m, extendedHours: true);
        var expected = DirectionalOrders.Exit("NVDA", TradeDirection.Short, 4, 50m, true);

        var outcome = await GatewayConsole.SubmitAsync(gateway, intent, "monitor-cli", () => null, printed.Add);

        Assert.That(intent.Origin, Is.EqualTo(OrderOrigin.Flatten));
        Assert.That(intent.Purpose, Is.EqualTo(OrderPurpose.Exit));
        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        var sent = paper.Placed.Single();
        Assert.That((sent.Side, sent.Quantity, sent.LimitPrice, sent.PositionIntent, sent.ExtendedHours),
            Is.EqualTo((expected.Side, expected.Quantity, expected.LimitPrice, expected.PositionIntent, expected.ExtendedHours)));
    }

    [Test]
    public async Task Flatten_OnMockData_StillGoesOut()
    {
        var mockGateway = Build(new MockDataFeed());
        var intent = MonitorOrders.ManualFlatten(Owner, Guid.NewGuid(), "Paper", "NVDA", TradeDirection.Long, 4, 50m, false);

        var outcome = await GatewayConsole.SubmitAsync(mockGateway, intent, "monitor-cli", () => null, printed.Add);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), "a held position must always be flatten-able");
    }

    [Test]
    public async Task LiveFlatten_WaitsForTheTypedCode_ThenPlacesOnce()
    {
        var intent = MonitorOrders.ManualFlatten(Owner, Guid.NewGuid(), "Live", "NVDA", TradeDirection.Long, 4, 50m, false);
        string? code = null;

        var outcome = await GatewayConsole.SubmitAsync(gateway, intent, "monitor-cli",
            () => code = printed.Select(p => System.Text.RegularExpressions.Regex.Match(p, @"code ([A-Z0-9]{6})")).First(m => m.Success).Groups[1].Value,
            printed.Add);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        Assert.That(code, Is.Not.Null);
        Assert.That(printed.Any(p => p.Contains("LIVE — real money")), Is.True, "the operator saw the preview and the warning");
        Assert.That(live.Placed, Has.Count.EqualTo(1));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("WRONG1")]
    public async Task LiveFlatten_WithoutTheRightCode_SendsNothing(string? typed)
    {
        var intent = MonitorOrders.ManualFlatten(Owner, Guid.NewGuid(), "Live", "NVDA", TradeDirection.Long, 4, 50m, false);

        var outcome = await GatewayConsole.SubmitAsync(gateway, intent, "monitor-cli", () => typed, printed.Add);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.ConfirmationRequired));
        Assert.That(outcome.Message, Does.Contain("othing was sent"));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task TestOrder_PassesTheGuardian_ThenCancelsThroughTheGateway()
    {
        var placed = await GatewayConsole.SubmitAsync(gateway, MonitorOrders.TestOrder(Owner, BrokerMode.Paper, "aapl"), "monitor-cli", () => null, printed.Add);
        var cancel = await GatewayConsole.SubmitAsync(gateway,
            MonitorOrders.CancelTestOrder(Owner, BrokerMode.Paper, "aapl", placed.Order!.BrokerOrderId), "monitor-cli", () => null, printed.Add);

        Assert.That(placed.Status, Is.EqualTo(GatewayStatus.Placed), MonitorOrders.WhyNot(placed));
        Assert.That(paper.Placed.Single().LimitPrice, Is.EqualTo(MonitorOrders.TestOrderLimit));
        Assert.That(paper.Placed.Single().Symbol, Is.EqualTo("AAPL"));
        Assert.That(cancel.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(paper.Cancelled, Is.EqualTo(new[] { placed.Order.BrokerOrderId }));
    }

    [Test]
    public async Task TestOrder_OnMockData_IsRefusedForARealAccount()
    {
        var outcome = await GatewayConsole.SubmitAsync(Build(new MockDataFeed()), MonitorOrders.TestOrder(Owner, BrokerMode.Paper, "AAPL"),
            "monitor-cli", () => null, printed.Add);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(paper.Placed, Is.Empty);
    }

    private sealed class Routing : Dictionary<BrokerMode, IBrokerClient>, IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(this[mode]);
    }

    private sealed class Broker(BrokerType type, bool isPaper) : IBrokerClient
    {
        public List<OrderRequest> Placed { get; } = [];
        public List<string> Cancelled { get; } = [];
        public BrokerType BrokerType => type;
        public bool IsPaper => isPaper;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            Placed.Add(request);
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = $"ord-{Placed.Count}" });
        }
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
        {
            Cancelled.Add(orderId);
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });
        }
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }
}
