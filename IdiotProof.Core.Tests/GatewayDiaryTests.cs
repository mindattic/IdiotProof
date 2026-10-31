using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Ledger;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T93: what the gateway writes besides the order. A person's accepted order opens (entry) or
/// closes (exit) a TradeDiary row; strategy and Monitor orders leave the diary to the Monitor;
/// refused or unconfirmed orders write no diary; every outcome's audit row names the Hub command
/// it ran under and is stamped with that command's ledger id.
/// </summary>
[TestFixture]
public sealed class GatewayDiaryTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private IDbContextFactory<AppDbContext> db = null!;
    private Broker paper = null!, live = null!;
    private TransactionGateway gateway = null!;

    [SetUp]
    public void SetUp()
    {
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"gateway-diary-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        paper = new Broker(isPaper: true);
        live = new Broker(isPaper: false);
        gateway = new TransactionGateway(
            new Routing { [BrokerMode.Paper] = paper, [BrokerMode.Live] = live, [BrokerMode.Sandbox] = paper },
            new SwitchableMarketDataFeed(), new RiskGuardianService(db), new ConfirmationStore(db, TimeProvider.System),
            new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance), NullLogger<TransactionGateway>.Instance,
            new TradeDiaryRepository(db));
    }

    private static OrderIntent Buy(BrokerMode mode = BrokerMode.Paper, OrderOrigin origin = OrderOrigin.Manual, Guid? strategy = null,
        decimal? stop = 98m) => new()
    {
        OwnerUserId = Owner, Mode = mode, Purpose = OrderPurpose.Entry, Origin = origin, StrategyId = strategy,
        Order = new OrderRequest { Symbol = "nvda", Quantity = 10, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 100m },
        StopLossPrice = stop, ReferencePrice = 100m,
    };

    private static OrderIntent Sell(decimal? limit, int qty = 10, BrokerMode mode = BrokerMode.Paper) => new()
    {
        OwnerUserId = Owner, Mode = mode, Purpose = OrderPurpose.Exit, Origin = OrderOrigin.Manual,
        Order = new OrderRequest
        {
            Symbol = "NVDA", Quantity = qty, Side = OrderSide.Sell, Type = limit is null ? OrderType.Market : OrderType.Limit, LimitPrice = limit,
        },
    };

    private async Task<List<TradeDiaryEntry>> Diary()
    {
        await using var ctx = await db.CreateDbContextAsync();
        return await ctx.TradeDiary.AsNoTracking().ToListAsync();
    }

    [Test]
    public async Task ManualEntry_OpensADiaryRow_ManualExit_ClosesItWithPnL()
    {
        await gateway.SubmitAsync(Buy());
        var open = (await Diary()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(open.Status, Is.EqualTo(TradeDiaryStatus.Open));
            Assert.That(open.StrategyId, Is.EqualTo(Guid.Empty));
            Assert.That(open.Symbol, Is.EqualTo("NVDA"));
            Assert.That(open.Direction, Is.EqualTo("Long"));
            Assert.That(open.IsPaper, Is.True);
            Assert.That(open.EntryPrice, Is.EqualTo(100m));
            Assert.That(open.Quantity, Is.EqualTo(10));
            Assert.That(open.EntryOrderId, Is.EqualTo("ord-1"));
            Assert.That(open.StopLossPrice, Is.EqualTo(98m));
            Assert.That(open.StrategyTitle, Does.StartWith("manual"));
        });

        await gateway.SubmitAsync(Sell(limit: 103m));

        var closed = (await Diary()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(closed.Status, Is.EqualTo(TradeDiaryStatus.Closed));
            Assert.That(closed.ExitPrice, Is.EqualTo(103m));
            Assert.That(closed.ExitOrderId, Is.EqualTo("ord-2"));
            Assert.That(closed.RealizedPnL, Is.EqualTo(30m));
            Assert.That(closed.ReturnPercent, Is.EqualTo(3m));
        });
    }

    [Test]
    public async Task MarketExit_ClosesTheRow_WithoutGuessingThePnL()
    {
        await gateway.SubmitAsync(Buy());
        await gateway.SubmitAsync(Sell(limit: null));

        var row = (await Diary()).Single();
        Assert.That(row.Status, Is.EqualTo(TradeDiaryStatus.Closed));
        Assert.That(row.RealizedPnL, Is.Null);
    }

    [Test]
    public async Task PartialExit_LeavesTheRowOpen()
    {
        await gateway.SubmitAsync(Buy());
        await gateway.SubmitAsync(Sell(limit: 103m, qty: 4));

        Assert.That((await Diary()).Single().Status, Is.EqualTo(TradeDiaryStatus.Open));
    }

    [Test]
    public async Task MonitorAndStrategyOrders_LeaveTheDiaryToTheMonitor()
    {
        await gateway.SubmitAsync(Buy(origin: OrderOrigin.Monitor), new GatewayOptions { LlmVerdict = (true, "ok") });
        await gateway.SubmitAsync(Buy(strategy: Guid.NewGuid()));

        Assert.That(paper.Placed, Has.Count.EqualTo(2));
        Assert.That(await Diary(), Is.Empty);
    }

    [Test]
    public async Task RefusedPreviewedOrUnconfirmed_WriteNoDiary()
    {
        await gateway.SubmitAsync(Buy(stop: null));        // Blocked
        await gateway.PreviewAsync(Buy());                 // Previewed
        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live)); // ConfirmationRequired
        paper.Refuse = "no";
        await gateway.SubmitAsync(Buy());                  // Rejected

        Assert.That(await Diary(), Is.Empty);

        await gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "cli");
        var row = (await Diary()).Single();
        Assert.That(row.IsPaper, Is.False, "a confirmed Live order is recorded as real money");
    }

    [Test]
    public async Task EveryAuditRow_NamesTheHubCommand_AndTheLedgerRowIsStampedOnIt()
    {
        CommandScope.Begin("orders place", "mcp");
        await gateway.SubmitAsync(Buy());
        await gateway.SubmitAsync(Buy(stop: null));
        var ledgerId = await new CommandLedgerRepository(db, NullLogger<CommandLedgerRepository>.Instance)
            .AppendAsync(new CommandLedgerEntry { OwnerUserId = Owner, Operation = "orders place", Caller = "mcp", Status = "Ok" });

        await using var ctx = await db.CreateDbContextAsync();
        var rows = ctx.AuditLogs.ToList();
        Assert.That(rows, Has.Count.EqualTo(2));
        foreach (var row in rows)
        {
            var json = System.Text.Json.JsonDocument.Parse(row.DataJson!).RootElement;
            Assert.That(json.GetProperty("command_ledger_id").GetInt64(), Is.EqualTo(ledgerId));
            Assert.That(json.GetProperty("command").GetProperty("operation").GetString(), Is.EqualTo("orders place"));
        }
    }

    private sealed class Routing : Dictionary<BrokerMode, IBrokerClient>, IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(this[mode]);
    }

    private sealed class Broker(bool isPaper) : IBrokerClient
    {
        private int next;
        public List<OrderRequest> Placed { get; } = [];
        public string? Refuse { get; set; }
        public BrokerType BrokerType => BrokerType.Alpaca;
        public bool IsPaper => isPaper;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            if (Refuse is not null) return Task.FromResult(new OrderResult { IsSuccess = false, Message = Refuse });
            Placed.Add(request);
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = $"ord-{++next}" });
        }
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }
}
