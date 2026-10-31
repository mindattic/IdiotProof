using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>
/// options buy / sell / close (IP-T194/T195/T196) through the real TransactionGateway over EF InMemory,
/// with a fake broker per mode (level, positions, placed orders). Nothing reaches a real account.
/// </summary>
public class OptionsTradingOperationTests
{
    private const string Call = "AAPL261219C00150000";
    private static readonly Guid Owner = Guid.NewGuid();

    private FakeBroker sandbox = null!, paper = null!, live = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private ConfirmationStore confirmations = null!;

    [SetUp]
    public void SetUp()
    {
        sandbox = new FakeBroker(BrokerType.Sandbox, isPaper: true);
        paper = new FakeBroker(BrokerType.Alpaca, isPaper: true);
        live = new FakeBroker(BrokerType.Alpaca, isPaper: false);
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"options-ops-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        confirmations = new ConfirmationStore(db, TimeProvider.System);
    }

    private OperationContext Context(string? code = null)
    {
        var routing = new FakeRouting { [BrokerMode.Sandbox] = sandbox, [BrokerMode.Paper] = paper, [BrokerMode.Live] = live };
        var audit = new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance);
        var gateway = new TransactionGateway(routing, new SwitchableMarketDataFeed(), new RiskGuardianService(db), confirmations, audit,
            NullLogger<TransactionGateway>.Instance);
        var services = new ServiceCollection()
            .AddSingleton<IOrderRouting>(routing)
            .AddSingleton(gateway)
            .AddSingleton(confirmations)
            .AddSingleton(audit)
            .BuildServiceProvider();
        return new OperationContext { Services = services, OwnerId = Owner, ConfirmationCode = code };
    }

    // ------------------------------------------------------------------ buy (IP-T194)

    [Test]
    public async Task Buy_Paper_PlacesABuyToOpenLimitForWholeContracts()
    {
        var result = await new OptionsBuyOperation().ExecuteAsync(new OptionsOrderArgs { Occ = Call.ToLowerInvariant(), Contracts = 2, Limit = 3.20m, Mode = BrokerMode.Paper }, Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var order = paper.Placed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(order.Symbol, Is.EqualTo(Call));
            Assert.That(order.AssetClass, Is.EqualTo(AssetClass.Option));
            Assert.That(order.Option!.Strike, Is.EqualTo(150m));
            Assert.That(order.Quantity, Is.EqualTo(2));
            Assert.That(order.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(order.Type, Is.EqualTo(OrderType.Limit));
            Assert.That(order.PositionIntent, Is.EqualTo("buy_to_open"));
            Assert.That(order.TimeInForce, Is.EqualTo("DAY"));
            Assert.That(result.Human, Does.Contain("est. cost  $640.00"));
        });
    }

    [Test]
    public async Task Buy_AtLevelOne_IsBlockedBeforeTheGateway()
    {
        paper.Level = 1;

        var result = await new OptionsBuyOperation().ExecuteAsync(new OptionsOrderArgs { Occ = Call, Contracts = 1, Mode = BrokerMode.Paper }, Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("level 1"));
        Assert.That(paper.Placed, Is.Empty);
    }

    [Test]
    public async Task Buy_Live_NeedsACode_ThenTheSameCallWithTheCodePlacesIt()
    {
        var args = new OptionsOrderArgs { Occ = Call, Contracts = 1, Limit = 3m, Mode = BrokerMode.Live };

        var first = await new OptionsBuyOperation().ExecuteAsync(args, Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(live.Placed, Is.Empty);

        var second = await new OptionsBuyOperation().ExecuteAsync(args, Context(first.Confirmation!.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        Assert.That(live.Placed.Single().PositionIntent, Is.EqualTo("buy_to_open"));
    }

    [Test]
    public void Buy_RejectsANonOccSymbol()
    {
        Assert.ThrowsAsync<OperationException>(() =>
            new OptionsBuyOperation().ExecuteAsync(new OptionsOrderArgs { Occ = "AAPL", Contracts = 1 }, Context()));
    }

    // ------------------------------------------------------------------ sell (IP-T195)

    [Test]
    public async Task Sell_DefaultsToSellToClose_AnExitAtAnyLevelAboveZero()
    {
        sandbox.Level = 1;

        var result = await new OptionsSellOperation().ExecuteAsync(new OptionsSellArgs { Occ = Call, Contracts = 2, Limit = 4.10m }, Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var order = sandbox.Placed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(order.Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(order.PositionIntent, Is.EqualTo("sell_to_close"));
            Assert.That(result.Human, Does.Contain("exit"));
        });
    }

    [Test]
    public async Task Sell_Open_WritesSellToOpen_AndLevelZeroBlocksIt()
    {
        var written = await new OptionsSellOperation().ExecuteAsync(new OptionsSellArgs { Occ = Call, Contracts = 1, Open = true, Mode = BrokerMode.Paper }, Context());
        Assert.That(written.IsOk, Is.True, written.Message);
        Assert.That(paper.Placed.Single().PositionIntent, Is.EqualTo("sell_to_open"));

        paper.Level = 0;
        var blocked = await new OptionsSellOperation().ExecuteAsync(new OptionsSellArgs { Occ = Call, Contracts = 1, Open = true, Mode = BrokerMode.Paper }, Context());
        Assert.That(blocked.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(paper.Placed, Has.Count.EqualTo(1));
    }

    // ------------------------------------------------------------------ close (IP-T196)

    [Test]
    public async Task Close_ALong_SellsEveryContractToClose()
    {
        paper.Positions.Add(new Position { Symbol = Call, Quantity = 3m });

        var result = await new OptionsCloseOperation().ExecuteAsync(new OptionsCloseArgs { Occ = Call, Mode = BrokerMode.Paper }, Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var order = paper.Placed.Single();
        Assert.That((order.Side, order.Quantity, order.PositionIntent, order.Type), Is.EqualTo((OrderSide.Sell, 3, "sell_to_close", OrderType.Market)));
    }

    [Test]
    public async Task Close_AShort_BuysToClose_AndLiveWaitsForTheCode()
    {
        live.Positions.Add(new Position { Symbol = Call, Quantity = -2m });
        var args = new OptionsCloseArgs { Occ = Call, Limit = 1.25m, Mode = BrokerMode.Live };

        var first = await new OptionsCloseOperation().ExecuteAsync(args, Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(live.Placed, Is.Empty);

        var second = await new OptionsCloseOperation().ExecuteAsync(args, Context(first.Confirmation!.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        var order = live.Placed.Single();
        Assert.That((order.Side, order.Quantity, order.PositionIntent), Is.EqualTo((OrderSide.Buy, 2, "buy_to_close")));
    }

    [Test]
    public async Task Close_WithNoPosition_IsBlocked()
    {
        var result = await new OptionsCloseOperation().ExecuteAsync(new OptionsCloseArgs { Occ = Call }, Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("No open"));
        Assert.That(sandbox.Placed, Is.Empty);
    }

    // ------------------------------------------------------------------ fakes

    private sealed class FakeBroker(BrokerType type, bool isPaper) : IBrokerClient
    {
        public List<OrderRequest> Placed { get; } = [];
        public List<Position> Positions { get; } = [];
        public int Level { get; set; } = 3;
        public BrokerType BrokerType => type;
        public bool IsPaper => isPaper;
        public bool IsConnected => true;
        public bool SupportsOptions => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            Placed.Add(request);
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = $"ord-{Placed.Count}" });
        }
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>(Positions);
        public Task<OptionsAccountInfo> GetOptionsAccountAsync(CancellationToken ct = default) =>
            Task.FromResult(new OptionsAccountInfo(Level, Level, null));
    }

    private sealed class FakeRouting : Dictionary<BrokerMode, IBrokerClient>, IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(this[mode]);
    }
}
