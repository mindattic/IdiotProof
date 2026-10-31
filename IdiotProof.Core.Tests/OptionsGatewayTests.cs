using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T96: the web Options ticket places through the TransactionGateway via
/// <see cref="OptionsTradingService.PlaceOrderAsync"/>. The order goes to exactly the account
/// the page resolved, is validated and audited, Live is confirmed by the password elevation
/// (checked server-side for the owner) instead of a code, and opening vs closing tickets are
/// entries vs exits.
/// </summary>
[TestFixture]
public sealed class OptionsGatewayTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private IDbContextFactory<AppDbContext> db = null!;
    private LiveModeElevationService elevation = null!;
    private OptionsTradingService service = null!;
    private Broker paper = null!, live = null!;

    [SetUp]
    public void SetUp()
    {
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"options-gateway-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        elevation = new LiveModeElevationService(db, null!, NullLogger<LiveModeElevationService>.Instance);
        service = new OptionsTradingService(null!, null!, null!, db, null!,
            new RiskGuardianService(db), new ConfirmationStore(db, TimeProvider.System),
            new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance),
            NullLogger<TransactionGateway>.Instance, NullLogger<OptionsTradingService>.Instance);
        paper = new Broker(isPaper: true);
        live = new Broker(isPaper: false);
    }

    [TearDown]
    public async Task TearDown() => await service.DisposeAsync();

    private static readonly OptionContract Call = new()
    {
        OccSymbol = "NVDA261120C00100000", UnderlyingSymbol = "NVDA", Strike = 100m,
        Expiration = new DateOnly(2026, 11, 20), Right = OptionRight.Call, Multiplier = 100,
    };

    private static OrderRequest Ticket(OrderSide side = OrderSide.Buy, string positionIntent = "buy_to_open") => new()
    {
        Symbol = Call.OccSymbol, AssetClass = AssetClass.Option, Option = Call, Quantity = 2, Side = side,
        Type = OrderType.Limit, LimitPrice = 3.10m, TimeInForce = "DAY", PositionIntent = positionIntent,
    };

    private Task<GatewayOutcome> Place(IBrokerClient client, OptionsBrokerMode mode, OrderRequest order, IMarketDataFeed? feed = null) =>
        service.PlaceThroughGatewayAsync(Owner, new OptionsBrokerResolution(client, mode, true, null), order,
            feed ?? new SwitchableMarketDataFeed(), elevation, CancellationToken.None);

    [Test]
    public async Task PaperTicket_IsPlacedOnThePagesAccount_AndAudited()
    {
        var outcome = await Place(paper, OptionsBrokerMode.Paper, Ticket());

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        Assert.That(paper.Placed.Single().Option!.OccSymbol, Is.EqualTo(Call.OccSymbol));
        Assert.That(outcome.Verdicts.Single(v => v.Gate == "risk").Detail, Does.Contain("manual options ticket"));
        await using var ctx = await db.CreateDbContextAsync();
        Assert.That(ctx.AuditLogs.Any(a => a.Category == "order" && a.DataJson!.Contains("\"caller\":\"blazor\"")), Is.True);
    }

    [Test]
    public async Task LiveTicket_WithoutPasswordElevation_IsBlocked_AndIssuesNoCode()
    {
        var outcome = await Place(live, OptionsBrokerMode.Live, Ticket());

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(outcome.ConfirmationCode, Is.Null);
        Assert.That(outcome.Verdicts.Last().Gate, Is.EqualTo("confirm"));
        Assert.That(live.Placed, Is.Empty);
        await using var ctx = await db.CreateDbContextAsync();
        Assert.That(ctx.PendingConfirmations.Count(), Is.Zero);
    }

    [Test]
    public async Task LiveTicket_WhileElevated_IsPlaced_AndTheAuditSaysHowItWasConfirmed()
    {
        elevation.Elevate(Owner);

        var outcome = await Place(live, OptionsBrokerMode.Live, Ticket());

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        Assert.That(live.Placed, Has.Count.EqualTo(1));
        await using var ctx = await db.CreateDbContextAsync();
        Assert.That(ctx.AuditLogs.Any(a => a.DataJson!.Contains("password elevation")), Is.True);
    }

    [Test]
    public async Task AnotherUsersElevation_DoesNotUnlockLive()
    {
        elevation.Elevate(Guid.NewGuid());

        var outcome = await Place(live, OptionsBrokerMode.Live, Ticket());

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task RevokedElevation_BlocksTheNextLiveTicket()
    {
        elevation.Elevate(Owner);
        await Place(live, OptionsBrokerMode.Live, Ticket());
        elevation.Revoke(Owner);

        var second = await Place(live, OptionsBrokerMode.Live, Ticket());

        Assert.That(second.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(live.Placed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task MalformedTicket_NeverReachesTheBroker()
    {
        var noContract = Ticket();
        var bad = new OrderRequest
        {
            Symbol = noContract.Symbol, AssetClass = AssetClass.Option, Quantity = 2, Side = OrderSide.Buy,
            Type = OrderType.Limit, LimitPrice = 3.10m, TimeInForce = "DAY", PositionIntent = "buy_to_open",
        };

        var outcome = await Place(paper, OptionsBrokerMode.Paper, bad);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(paper.Placed, Is.Empty);
    }

    [Test]
    public async Task OpeningTicket_OnSyntheticData_IsRefused_ButClosingOneGoesOut()
    {
        var opening = await Place(paper, OptionsBrokerMode.Paper, Ticket(), new MockDataFeed());
        var closing = await Place(paper, OptionsBrokerMode.Paper, Ticket(OrderSide.Sell, "sell_to_close"), new MockDataFeed());

        Assert.That(opening.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(closing.Status, Is.EqualTo(GatewayStatus.Placed), closing.Message);
    }

    [Test]
    public void Intent_MapsModeAndPurpose()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OptionsTradingService.ToIntent(Owner, OptionsBrokerMode.Live, Ticket()).Mode, Is.EqualTo(BrokerMode.Live));
            Assert.That(OptionsTradingService.ToIntent(Owner, OptionsBrokerMode.Sandbox, Ticket()).Mode, Is.EqualTo(BrokerMode.Sandbox));
            Assert.That(OptionsTradingService.ToIntent(Owner, OptionsBrokerMode.Paper, Ticket()).Purpose, Is.EqualTo(OrderPurpose.Entry));
            Assert.That(OptionsTradingService.ToIntent(Owner, OptionsBrokerMode.Paper, Ticket(OrderSide.Sell, "sell_to_close")).Purpose, Is.EqualTo(OrderPurpose.Exit));
            Assert.That(OptionsTradingService.ToIntent(Owner, OptionsBrokerMode.Paper, Ticket()).Origin, Is.EqualTo(OrderOrigin.Options));
        });
    }

    private sealed class Broker(bool isPaper) : IBrokerClient
    {
        public List<OrderRequest> Placed { get; } = [];
        public BrokerType BrokerType => BrokerType.Alpaca;
        public bool IsPaper => isPaper;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            Placed.Add(request);
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = $"opt-{Placed.Count}", Message = "accepted" });
        }
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }
}
