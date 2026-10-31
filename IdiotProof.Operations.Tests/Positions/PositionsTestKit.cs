using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using IdiotProof.Core.Data;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Positions;

/// <summary>
/// The world a positions operation sees, all in memory: an owner, EF InMemory strategies and
/// diary rows, a scripted Alpaca Trading API per account (only GETs are answered; anything else
/// fails the test, so nothing could ever mutate a real account), and a real
/// <see cref="TransactionGateway"/> whose broker route is a recording fake.
/// </summary>
internal sealed class PositionsKit
{
    public Guid Owner { get; } = Guid.NewGuid();

    public IDbContextFactory<AppDbContext> Db { get; }

    /// <summary>The JSON array <c>GET /v2/positions</c> answers, per account.</summary>
    public Dictionary<BrokerMode, string> Held { get; } = new() { [BrokerMode.Paper] = "[]", [BrokerMode.Live] = "[]" };

    /// <summary>Accounts whose keys are "missing" (the factory refuses them).</summary>
    public HashSet<BrokerMode> Refused { get; } = [];

    /// <summary>Every request that reached the scripted Alpaca API.</summary>
    public List<(BrokerMode Mode, HttpMethod Method, string Path)> Requests { get; } = [];

    /// <summary>Where the gateway routes; records what it was asked to place.</summary>
    public Dictionary<BrokerMode, RecordingBroker> Brokers { get; } = new()
    {
        [BrokerMode.Sandbox] = new RecordingBroker(BrokerType.Sandbox, isPaper: true),
        [BrokerMode.Paper] = new RecordingBroker(BrokerType.Alpaca, isPaper: true),
        [BrokerMode.Live] = new RecordingBroker(BrokerType.Alpaca, isPaper: false),
    };

    private readonly IServiceProvider services;

    public PositionsKit()
    {
        var sc = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"positions-{Guid.NewGuid():N}"));
        sc.AddSingleton(TimeProvider.System);
        sc.AddSingleton<StrategyRepository>();
        sc.AddSingleton<TradeDiaryRepository>();
        sc.AddSingleton<ConfirmationStore>();
        sc.AddSingleton<AuditLogRepository>();
        sc.AddSingleton<RiskGuardianService>();
        sc.AddSingleton<IMarketDataFeed>(new QuietFeed());
        sc.AddSingleton<IOrderRouting>(new Routing(this));
        sc.AddSingleton<TransactionGateway>();
        sc.AddSingleton<AlpacaApiFactory>(new Factory(this));
        services = sc.BuildServiceProvider();
        Db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    public OperationContext Context(string? confirm = null) => new()
    {
        Services = services,
        OwnerId = Owner,
        ConfirmationCode = confirm,
        Caller = OperationCaller.Cli,
    };

    /// <summary>One Alpaca position element (numbers as strings, as Alpaca sends them).</summary>
    public static string Position(string symbol, decimal qty, decimal avg = 10m, decimal price = 11m, string assetClass = "us_equity", decimal? available = null) =>
        $$"""{"symbol":"{{symbol}}","qty":"{{qty}}","qty_available":"{{available ?? qty}}","side":"{{(qty < 0 ? "short" : "long")}}","avg_entry_price":"{{avg}}","current_price":"{{price}}","lastday_price":"{{price - 0.5m}}","market_value":"{{qty * price}}","cost_basis":"{{qty * avg}}","unrealized_pl":"{{qty * (price - avg)}}","unrealized_plpc":"{{(price - avg) / avg}}","unrealized_intraday_pl":"{{qty * 0.5m}}","asset_class":"{{assetClass}}","exchange":"NASDAQ"}""";

    public static string Array(params string[] elements) => "[" + string.Join(",", elements) + "]";

    public async Task<Strategy> AddStrategyAsync(string symbol, BrokerMode mode, int positionQty, bool active = true, string? title = null)
    {
        var s = new Strategy
        {
            OwnerUserId = Owner, Title = title ?? $"{symbol} test", Symbol = symbol,
            ScriptText = $"Stock.Ticker(\"{symbol}\").Long().StopLoss(1).TakeProfit(2).Build()",
            IsActive = active, BrokerMode = mode.ToString(), PositionQty = positionQty,
            LastEntryPrice = positionQty == 0 ? null : 10m,
            EntryFilledUtc = positionQty == 0 ? null : new DateTime(2026, 10, 29, 13, 31, 0, DateTimeKind.Utc),
        };
        await using var db = await Db.CreateDbContextAsync();
        db.Strategies.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public async Task AddDiaryAsync(Strategy s, bool isPaper, string status = TradeDiaryStatus.Open)
    {
        await using var db = await Db.CreateDbContextAsync();
        db.TradeDiary.Add(new TradeDiaryEntry
        {
            StrategyId = s.Id, OwnerUserId = Owner, StrategyTitle = s.Title, Symbol = s.Symbol, Broker = "Alpaca", IsPaper = isPaper,
            EntryUtc = new DateTime(2026, 10, 29, 13, 31, 0, DateTimeKind.Utc), EntryPrice = 10m, Quantity = s.PositionQty,
            StopLossPrice = 9.5m, Status = status,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Answers only GETs on /v2/positions[/symbol]; a 404 for a symbol the account doesn't hold.</summary>
    private sealed class Handler(PositionsKit kit, BrokerMode mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (kit.Requests) kit.Requests.Add((mode, request.Method, path));
            if (request.Method != HttpMethod.Get)
                return Reply(HttpStatusCode.InternalServerError, """{"code":1,"message":"tests never mutate an Alpaca account"}""");
            var json = kit.Held[mode];
            if (path == "/v2/positions") return Reply(HttpStatusCode.OK, json);
            if (path.StartsWith("/v2/positions/", StringComparison.Ordinal))
            {
                var symbol = Uri.UnescapeDataString(path["/v2/positions/".Length..]);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (string.Equals(e.GetProperty("symbol").GetString()!.Replace("/", ""), symbol, StringComparison.OrdinalIgnoreCase))
                        return Reply(HttpStatusCode.OK, e.GetRawText());
                return Reply(HttpStatusCode.NotFound, """{"code":40410000,"message":"position does not exist"}""");
            }
            return Reply(HttpStatusCode.NotFound, """{"code":40410000,"message":"no such route in the test"}""");
        }

        private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class Factory(PositionsKit kit) : AlpacaApiFactory(null!, null!, null!)
    {
        public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default)
        {
            if (mode == BrokerMode.Sandbox) throw new AlpacaAccessException("Sandbox is IdiotProof's simulator, not an Alpaca account.");
            if (kit.Refused.Contains(mode)) throw new AlpacaAccessException($"No {mode.ToString().ToLowerInvariant()} Alpaca keys configured.");
            return Task.FromResult(new AlpacaApi("k", "s", mode == BrokerMode.Paper, new Handler(kit, mode)));
        }

        public override Task<AlpacaApi> ForDataAsync(Guid? ownerUserId = null, CancellationToken ct = default) =>
            ForOwnerAsync(ownerUserId ?? Guid.Empty, BrokerMode.Paper, ct);
    }

    private sealed class Routing(PositionsKit kit) : IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) =>
            Task.FromResult<IBrokerClient>(kit.Brokers[mode]);
    }

    /// <summary>A real (non-Mock) feed with no quotes.</summary>
    private sealed class QuietFeed : IMarketDataFeed
    {
        public string FeedName => "Test";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
    }
}

/// <summary>A broker that records what the gateway asked it to place (and never talks to anyone).</summary>
internal sealed class RecordingBroker(BrokerType type, bool isPaper) : IBrokerClient
{
    public List<OrderRequest> Placed { get; } = [];
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

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
        Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });

    /// <summary>Every cancel-all the gateway asked for (null = every symbol).</summary>
    public List<string?> CancelledAll { get; } = [];

    public Task<OrderResult> CancelAllOrdersAsync(string? symbol = null, CancellationToken ct = default)
    {
        CancelledAll.Add(symbol);
        return Task.FromResult(new OrderResult { IsSuccess = true, Message = "2 orders cancelled" });
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Position>>([]);
}
