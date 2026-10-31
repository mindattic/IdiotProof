using System.Net;
using System.Text;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Operations.Tests.Orders;

/// <summary>
/// The orders family's test rig: the real TransactionGateway over EF InMemory with a fake broker per
/// mode (what mutations reach), and an <see cref="AlpacaApiFactory"/> over scripted handlers (what reads
/// see). Nothing reaches a real account.
/// </summary>
internal sealed class OrdersTestKit
{
    public static readonly Guid Owner = Guid.NewGuid();

    public FakeBroker Sandbox { get; } = new(BrokerType.Sandbox, isPaper: true);
    public FakeBroker Paper { get; } = new(BrokerType.Alpaca, isPaper: true);
    public FakeBroker Live { get; } = new(BrokerType.Alpaca, isPaper: false);
    public ScriptedFactory Alpaca { get; } = new();
    public decimal? Quote { get; set; } = 100m;

    private readonly IDbContextFactory<AppDbContext> db = new ServiceCollection()
        .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"orders-ops-{Guid.NewGuid():N}"))
        .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();

    private readonly ConfirmationStore confirmations;

    public OrdersTestKit() => confirmations = new ConfirmationStore(db, TimeProvider.System);

    public OperationContext Context(string? code = null, DateTime? nowUtc = null)
    {
        var routing = new FakeRouting { [BrokerMode.Sandbox] = Sandbox, [BrokerMode.Paper] = Paper, [BrokerMode.Live] = Live };
        var audit = new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance);
        var feed = new QuoteFeed(this);
        var gateway = new TransactionGateway(routing, feed, new RiskGuardianService(db), confirmations, audit, NullLogger<TransactionGateway>.Instance);
        var services = new ServiceCollection()
            .AddSingleton<IOrderRouting>(routing)
            .AddSingleton<IMarketDataFeed>(feed)
            .AddSingleton(gateway)
            .AddSingleton(confirmations)
            .AddSingleton(audit)
            .AddSingleton<AlpacaApiFactory>(Alpaca)
            .BuildServiceProvider();
        return new OperationContext
        {
            Services = services, OwnerId = Owner, ConfirmationCode = code, Width = 200,
            Clock = new Fixtures.FixedClock(nowUtc ?? new DateTime(2026, 10, 30, 18, 0, 0, DateTimeKind.Utc)),
        };
    }

    /// <summary>An Alpaca order body in the API's own shape (numbers as strings).</summary>
    public static string OrderJson(string id, string symbol = "NVDA", string side = "buy", string type = "limit", string qty = "10",
        string? limit = "100", string status = "new", string? orderClass = null, string legs = "", string? clientId = null,
        string submitted = "2026-10-30T14:05:00Z", string? filledAt = null, string filledQty = "0", string? avg = null, string? trailPercent = null) =>
        $$"""
        {"id":"{{id}}","client_order_id":"{{clientId ?? "c-" + id}}","symbol":"{{symbol}}","asset_class":"us_equity","side":"{{side}}",
         "order_type":"{{type}}","type":"{{type}}","qty":"{{qty}}","filled_qty":"{{filledQty}}","filled_avg_price":{{(avg is null ? "null" : $"\"{avg}\"")}},
         "limit_price":{{(limit is null ? "null" : $"\"{limit}\"")}},"stop_price":null,"trail_percent":{{(trailPercent is null ? "null" : $"\"{trailPercent}\"")}},
         "time_in_force":"day","status":"{{status}}","order_class":"{{orderClass ?? ""}}","extended_hours":false,
         "submitted_at":"{{submitted}}","filled_at":{{(filledAt is null ? "null" : $"\"{filledAt}\"")}},
         "legs":{{(legs.Length == 0 ? "null" : "[" + legs + "]")}}}
        """;

    internal sealed class FakeBroker(BrokerType type, bool isPaper) : IBrokerClient
    {
        public List<OrderRequest> Placed { get; } = [];
        public List<string> Cancelled { get; } = [];
        public List<(string Id, OrderRequest Changes)> Replaced { get; } = [];
        public List<string?> CancelAllFor { get; } = [];
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
        public Task<OrderResult> ReplaceOrderAsync(string orderId, OrderRequest changes, CancellationToken ct = default)
        {
            Replaced.Add((orderId, changes));
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId + "-r" });
        }
        public Task<OrderResult> CancelAllOrdersAsync(string? symbol = null, CancellationToken ct = default)
        {
            CancelAllFor.Add(symbol);
            return Task.FromResult(new OrderResult { IsSuccess = true, Message = "2 order(s) cancelled." });
        }
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }

    private sealed class FakeRouting : Dictionary<BrokerMode, IBrokerClient>, IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(this[mode]);
    }

    /// <summary>A real (non-Mock) feed whose latest price is the kit's <see cref="Quote"/>.</summary>
    private sealed class QuoteFeed(OrdersTestKit kit) : IMarketDataFeed
    {
        public string FeedName => "Test";
        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult(kit.Quote is { } q ? new LatestPrice(symbol, q, DateTime.UtcNow, "Test") : null);
    }

    /// <summary>Replies by "METHOD /path" (query ignored); records every request with its query. Unscripted routes 404.</summary>
    internal sealed class Scripted : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Routes { get; } = [];
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            Calls.Add(key + request.RequestUri.Query);
            var (status, body) = Routes.TryGetValue(key, out var r) ? r : (HttpStatusCode.NotFound, """{"code":40410000,"message":"order not found"}""");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    internal sealed class ScriptedFactory() : AlpacaApiFactory(null!, null!, null!)
    {
        public Scripted Paper { get; } = new();
        public Scripted Live { get; } = new();

        public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default) =>
            mode == BrokerMode.Sandbox
                ? throw new AlpacaAccessException("Sandbox is not Alpaca.")
                : Task.FromResult(new AlpacaApi("k", "s", mode == BrokerMode.Paper, mode == BrokerMode.Paper ? Paper : Live));
    }
}
