using IdiotProof.Core.Data;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Crypto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Crypto;

/// <summary>
/// crypto buy / sell / positions (IP-T315) through the real TransactionGateway over EF InMemory with a
/// fake broker per mode; the reference price and positions come from a scripted Alpaca. Nothing real.
/// </summary>
public class CryptoTradingOperationTests
{
    private FakeBroker paper = null!, live = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private ConfirmationStore confirmations = null!;
    private CryptoAlpacaHandler alpaca = null!;

    [SetUp]
    public void SetUp()
    {
        paper = new FakeBroker(isPaper: true);
        live = new FakeBroker(isPaper: false);
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"crypto-trading-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        confirmations = new ConfirmationStore(db, TimeProvider.System);
        alpaca = new CryptoAlpacaHandler()
            .On("/v1beta3/crypto/us/latest/trades", """{"trades":{"BTC/USD":{"t":"2026-10-30T13:59:59Z","p":70000,"s":0.01}}}""");
    }

    [TearDown]
    public void TearDown() => alpaca.Dispose();

    private OperationContext Context(string? code = null)
    {
        var routing = new FakeRouting { [BrokerMode.Paper] = paper, [BrokerMode.Live] = live };
        var audit = new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance);
        var gateway = new TransactionGateway(routing, new SwitchableMarketDataFeed(), new RiskGuardianService(db), confirmations, audit,
            NullLogger<TransactionGateway>.Instance);
        return CryptoAlpacaFactory.Context(alpaca, s => s.AddSingleton(gateway).AddSingleton(confirmations).AddSingleton(audit), code);
    }

    [Test]
    public async Task Buy_Paper_SendsAFractionalGtcCryptoOrder_SizedForRiskByTheRealQuantity()
    {
        // 0.001 BTC at the 70,000 quote with a 3% stop risks $2.10 (x1.5 slippage = $3.15): well inside
        // the default $100 per trade. Rounded up to one whole coin it would be a $3,150 veto.
        var result = await new CryptoBuyOperation().ExecuteAsync(new CryptoOrderArgs { Pair = "btc-usd", Quantity = 0.001m, Stop = 67900m }, Context());

        Assert.That(result.IsOk, Is.True, result.Message + "\n" + result.Human);
        var order = paper.Placed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(order.Symbol, Is.EqualTo("BTC/USD"));
            Assert.That(order.AssetClass, Is.EqualTo(AssetClass.Crypto));
            Assert.That(order.FractionalQuantity, Is.EqualTo(0.001m));
            Assert.That(order.Type, Is.EqualTo(OrderType.Market));
            Assert.That(order.TimeInForce, Is.EqualTo("GTC"));
            Assert.That(result.Human, Does.Contain("est. cost  $70.00"));
        });
    }

    [Test]
    public async Task Buy_WithoutAStop_IsVetoed()
    {
        var result = await new CryptoBuyOperation().ExecuteAsync(new CryptoOrderArgs { Pair = "BTC/USD", Quantity = 0.001m }, Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(paper.Placed, Is.Empty);
    }

    [Test]
    public async Task Buy_DayTimeInForce_IsRefusedByTheGateway()
    {
        var result = await new CryptoBuyOperation().ExecuteAsync(
            new CryptoOrderArgs { Pair = "BTC/USD", Quantity = 0.001m, Stop = 67900m, TimeInForce = "day" }, Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Human, Does.Contain("GTC or IOC"));
        Assert.That(paper.Placed, Is.Empty);
    }

    [Test]
    public async Task Sell_Live_WaitsForTheCode_ThenSellsByNotionalWithALimit()
    {
        var args = new CryptoOrderArgs { Pair = "BTC/USD", Notional = 100m, Limit = 71000m, TimeInForce = "ioc", Mode = BrokerMode.Live };

        var first = await new CryptoSellOperation().ExecuteAsync(args, Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(live.Placed, Is.Empty);

        var second = await new CryptoSellOperation().ExecuteAsync(args, Context(first.Confirmation!.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        var order = live.Placed.Single();
        Assert.That((order.Side, order.Notional, order.Type, order.LimitPrice, order.TimeInForce), Is.EqualTo((OrderSide.Sell, 100m, OrderType.Limit, 71000m, "IOC")));
    }

    [Test]
    public void Sandbox_IsRefused() =>
        Assert.ThrowsAsync<OperationException>(() => new CryptoBuyOperation().ExecuteAsync(
            new CryptoOrderArgs { Pair = "BTC/USD", Quantity = 1m, Mode = BrokerMode.Sandbox }, Context()));

    [Test]
    public async Task Positions_ShowOnlyCryptoHoldings()
    {
        alpaca.On("/v2/positions", """
            [{"symbol":"AAPL","asset_class":"us_equity","qty":"10","market_value":"2300"},
             {"symbol":"BTCUSD","asset_class":"crypto","qty":"0.0123","avg_entry_price":"65000","current_price":"70000","market_value":"861","cost_basis":"799.5","unrealized_pl":"61.5","unrealized_plpc":"0.0769","unrealized_intraday_pl":"5.25"}]
            """);

        var result = await new CryptoPositionsOperation().ExecuteAsync(new AccountScopeArgs { Mode = AccountScope.Paper }, Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("BTC/USD").And.Contain("0.0123").And.Contain("+$61.50").And.Contain("+7.69%").And.Not.Contain("AAPL"));
    }

    private sealed class FakeBroker(bool isPaper) : IBrokerClient
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
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = $"ord-{Placed.Count}" });
        }
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }

    private sealed class FakeRouting : Dictionary<BrokerMode, IBrokerClient>, IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(this[mode]);
    }
}
