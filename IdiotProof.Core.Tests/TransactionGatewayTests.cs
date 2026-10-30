using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The transaction gateway is the one road to a broker. One test per rule: malformed orders never
/// leave, Live never silently degrades to paper, Mock data never opens a real position (IP-LAW-9),
/// entries need a stop and room under the Risk Guardian (IP-LAW-2), automated entries need the
/// voter panel (IP-LAW-1), exits always pass, and real money from a person waits for a code that
/// replays exactly what was previewed.
/// </summary>
[TestFixture]
public sealed class TransactionGatewayTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private FakeBroker sandbox = null!, paper = null!, live = null!;
    private FakeRouting routing = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private ConfirmationStore confirmations = null!;
    private FakeTimeProvider clock = null!;

    [SetUp]
    public void SetUp()
    {
        sandbox = new FakeBroker(BrokerType.Sandbox, isPaper: true);
        paper = new FakeBroker(BrokerType.Alpaca, isPaper: true);
        live = new FakeBroker(BrokerType.Alpaca, isPaper: false);
        routing = new FakeRouting { [BrokerMode.Sandbox] = sandbox, [BrokerMode.Paper] = paper, [BrokerMode.Live] = live };
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"gateway-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        confirmations = new ConfirmationStore(db, clock);
    }

    private TransactionGateway Gateway(IMarketDataFeed? feed = null) => new(
        routing,
        feed ?? new SwitchableMarketDataFeed(), // any feed that isn't MockDataFeed counts as real data
        new RiskGuardianService(db),
        confirmations,
        new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance),
        NullLogger<TransactionGateway>.Instance);

    private static OrderIntent Buy(BrokerMode mode, decimal? stop = 98m, int qty = 10, OrderOrigin origin = OrderOrigin.Manual,
        OrderPurpose purpose = OrderPurpose.Entry) => new()
    {
        OwnerUserId = Owner, Mode = mode, Purpose = purpose, Origin = origin,
        Order = new OrderRequest { Symbol = "NVDA", Quantity = qty, Side = purpose == OrderPurpose.Entry ? OrderSide.Buy : OrderSide.Sell, Type = OrderType.Limit, LimitPrice = 100m },
        StopLossPrice = stop, ReferencePrice = 100m,
    };

    [Test]
    public async Task Sandbox_ManualEntry_IsPlacedOnTheSimulator()
    {
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(sandbox.Placed, Has.Count.EqualTo(1));
        Assert.That(outcome.RoutedTo, Is.EqualTo("Sandbox (simulated)"));
    }

    [Test]
    public async Task MalformedOrder_NeverReachesABroker()
    {
        var both = Buy(BrokerMode.Sandbox) with { Order = new OrderRequest { Symbol = "NVDA", Quantity = 5, Notional = 500m, Side = OrderSide.Buy, Type = OrderType.Market } };

        var outcome = await Gateway().SubmitAsync(both);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(sandbox.Placed, Is.Empty);
    }

    [Test]
    public async Task Entry_WithoutAStop_IsVetoed()
    {
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox, stop: null));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(outcome.Verdicts.Single(v => v.Gate == "risk").Passed, Is.False);
        Assert.That(sandbox.Placed, Is.Empty);
    }

    [Test]
    public async Task Entry_RiskingMoreThanTheCap_IsVetoed()
    {
        // $10 of risk per share × 50 shares = $500, five times the default $100 per-trade cap.
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox, stop: 90m, qty: 50));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(outcome.Message, Does.StartWith("Risk Guardian veto"));
    }

    [Test]
    public async Task Exit_AlwaysPasses_EvenWithoutAStop()
    {
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox, stop: null, purpose: OrderPurpose.Exit));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed));
    }

    [Test]
    public async Task MockFeed_RefusesRealEntries_ButAllowsRealExitsAndSandbox()
    {
        var gateway = Gateway(new MockDataFeed());

        var realEntry = await gateway.SubmitAsync(Buy(BrokerMode.Paper));
        var realExit = await gateway.SubmitAsync(Buy(BrokerMode.Paper, purpose: OrderPurpose.Exit));
        var simulated = await gateway.SubmitAsync(Buy(BrokerMode.Sandbox));

        Assert.Multiple(() =>
        {
            Assert.That(realEntry.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(realExit.Status, Is.EqualTo(GatewayStatus.Placed));
            Assert.That(simulated.Status, Is.EqualTo(GatewayStatus.Placed));
            Assert.That(paper.Placed, Has.Count.EqualTo(1), "only the exit reached the paper account");
        });
    }

    [Test]
    public async Task Live_ThatResolvesToPaperOrSandbox_IsRefused_NeverDowngraded()
    {
        routing[BrokerMode.Live] = paper;

        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Live));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(paper.Placed, Is.Empty);
    }

    [Test]
    public async Task AutomatedEntry_NeedsAnApprovingVoterPanel()
    {
        var gateway = Gateway();

        var noVerdict = await gateway.SubmitAsync(Buy(BrokerMode.Sandbox, origin: OrderOrigin.Monitor));
        var rejected = await gateway.SubmitAsync(Buy(BrokerMode.Sandbox, origin: OrderOrigin.Monitor), new GatewayOptions { LlmVerdict = (false, "2 of 3 rejected") });
        var approved = await gateway.SubmitAsync(Buy(BrokerMode.Sandbox, origin: OrderOrigin.Monitor), new GatewayOptions { LlmVerdict = (true, "3 of 3 approved") });

        Assert.Multiple(() =>
        {
            Assert.That(noVerdict.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(rejected.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(approved.Status, Is.EqualTo(GatewayStatus.Placed));
        });
    }

    [Test]
    public async Task ManualLive_IssuesACode_ThenConfirmPlacesExactlyThatOrderOnce()
    {
        var gateway = Gateway();

        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live));
        Assert.That(pending.Status, Is.EqualTo(GatewayStatus.ConfirmationRequired));
        Assert.That(live.Placed, Is.Empty, "nothing moves before the yes");

        var confirmed = await gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "cli");
        Assert.That(confirmed!.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(live.Placed.Single().Quantity, Is.EqualTo(10));

        var again = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "cli"));
        Assert.That(again!.Failure, Is.EqualTo(ConfirmationFailure.AlreadyUsed));
        Assert.That(live.Placed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Confirm_WithADifferentOrderThanPreviewed_IsTampered_AndKeepsTheCode()
    {
        var gateway = Gateway();
        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live));

        var swapped = Assert.ThrowsAsync<ConfirmationRefusedException>(() =>
            gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "mcp", Buy(BrokerMode.Live, qty: 500).Hash()));
        var original = await gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "mcp", Buy(BrokerMode.Live).Hash());

        Assert.That(swapped!.Failure, Is.EqualTo(ConfirmationFailure.Tampered));
        Assert.That(original!.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(live.Placed.Single().Quantity, Is.EqualTo(10));
    }

    [Test]
    public async Task ManualLive_ExpiredCode_PlacesNothing()
    {
        var gateway = Gateway();
        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live));
        clock.Advance(ConfirmationStore.Lifetime + TimeSpan.FromSeconds(1));

        var refused = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "cli"));

        Assert.That(refused!.Failure, Is.EqualTo(ConfirmationFailure.Expired));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task Preview_RunsEveryGate_PlacesNothing_IssuesNoCode()
    {
        var outcome = await Gateway().PreviewAsync(Buy(BrokerMode.Live));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Previewed));
        Assert.That(outcome.ConfirmationCode, Is.Null);
        Assert.That(outcome.Verdicts.Select(v => v.Gate), Is.EqualTo(new[] { "order", "route", "risk" }));
        Assert.That(outcome.Preview(), Does.Contain("Alpaca LIVE").And.Contain("worst case"));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task BrokerRefusal_IsRejected_AndAudited()
    {
        sandbox.Refuse = "insufficient buying power";

        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Rejected));
        await using var ctx = await db.CreateDbContextAsync();
        Assert.That(ctx.AuditLogs.Any(a => a.Category == "order" && a.Message.Contains("Rejected")), Is.True);
    }

    private sealed class FakeRouting : Dictionary<BrokerMode, IBrokerClient>, IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(this[mode]);
    }

    private sealed class FakeBroker(BrokerType type, bool isPaper) : IBrokerClient
    {
        public List<OrderRequest> Placed { get; } = [];
        public string? Refuse { get; set; }
        public BrokerType BrokerType => type;
        public bool IsPaper => isPaper;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
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
