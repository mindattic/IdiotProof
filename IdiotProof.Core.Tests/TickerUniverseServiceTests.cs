using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

[TestFixture]
public sealed class TickerUniverseServiceTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;

    private const string AssetListResponse = """
        [
          {"symbol":"AAPL","exchange":"NASDAQ","tradable":true,"status":"active"},
          {"symbol":"OTCX","exchange":"OTC","tradable":true,"status":"active"},
          {"symbol":"HALT","exchange":"NYSE","tradable":false,"status":"active"},
          {"symbol":"IBM","exchange":"NYSE","tradable":true,"status":"active"}
        ]
        """;

    private const string PricesResponse = """
        {"trades":{"AAPL":{"p":214.32},"IBM":{"p":231.10}}}
        """;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [TearDown]
    public void TearDown()
    {
        using var db = factory.CreateDbContext();
        db.TrackedTickers.RemoveRange(db.TrackedTickers);
        db.SaveChanges();
    }

    private static AppSettings KeyedSettings() => new()
    {
        AlpacaApiKeyId = "key",
        AlpacaApiSecretKey = "secret",
        AlpacaIsPaper = true,
    };

    [Test]
    public async Task RefreshIfStaleAsync_NoApiKey_NoOpsWithoutError()
    {
        var handler = new RoutingHandler(AssetListResponse, PricesResponse);
        var svc = new TickerUniverseService(new StubHttpClientFactory(handler),
            new AppSettings(), factory, NullLogger<TickerUniverseService>.Instance);

        await svc.RefreshIfStaleAsync(TimeSpan.FromHours(24));

        Assert.That(handler.CallCount, Is.EqualTo(0));
        Assert.That(await svc.GetUniverseAsync(), Is.Empty);
    }

    [Test]
    public async Task RefreshIfStaleAsync_FiltersToTradableNasdaqNyseOnly()
    {
        var handler = new RoutingHandler(AssetListResponse, PricesResponse);
        var svc = new TickerUniverseService(new StubHttpClientFactory(handler),
            KeyedSettings(), factory, NullLogger<TickerUniverseService>.Instance);

        await svc.RefreshIfStaleAsync(TimeSpan.FromHours(24));
        var universe = await svc.GetUniverseAsync();

        Assert.That(universe.Select(t => t.Symbol), Is.EquivalentTo(new[] { "AAPL", "IBM" }));
    }

    [Test]
    public async Task RefreshIfStaleAsync_PopulatesLastPriceFromBatchedTradesLookup()
    {
        var handler = new RoutingHandler(AssetListResponse, PricesResponse);
        var svc = new TickerUniverseService(new StubHttpClientFactory(handler),
            KeyedSettings(), factory, NullLogger<TickerUniverseService>.Instance);

        await svc.RefreshIfStaleAsync(TimeSpan.FromHours(24));
        var byId = await svc.GetBySymbolsAsync(["AAPL", "IBM"]);

        Assert.Multiple(() =>
        {
            Assert.That(byId["AAPL"].LastPrice, Is.EqualTo(214.32m));
            Assert.That(byId["IBM"].LastPrice, Is.EqualTo(231.10m));
        });
    }

    [Test]
    public async Task RefreshIfStaleAsync_FreshData_SkipsRefetch()
    {
        var handler = new RoutingHandler(AssetListResponse, PricesResponse);
        var svc = new TickerUniverseService(new StubHttpClientFactory(handler),
            KeyedSettings(), factory, NullLogger<TickerUniverseService>.Instance);

        await svc.RefreshIfStaleAsync(TimeSpan.FromHours(24));
        var callsAfterFirst = handler.CallCount;

        await svc.RefreshIfStaleAsync(TimeSpan.FromHours(24));
        Assert.That(handler.CallCount, Is.EqualTo(callsAfterFirst)); // no new HTTP calls — data still fresh
    }

    [Test]
    public async Task GetBySymbolsAsync_UnknownSymbol_IsAbsentFromResult()
    {
        var handler = new RoutingHandler(AssetListResponse, PricesResponse);
        var svc = new TickerUniverseService(new StubHttpClientFactory(handler),
            KeyedSettings(), factory, NullLogger<TickerUniverseService>.Instance);

        await svc.RefreshIfStaleAsync(TimeSpan.FromHours(24));
        var byId = await svc.GetBySymbolsAsync(["NOPE"]);

        Assert.That(byId, Is.Empty);
    }

    private sealed class RoutingHandler(string assetListBody, string pricesBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            var body = request.RequestUri!.AbsoluteUri.Contains("/v2/assets") ? assetListBody : pricesBody;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    // Mirrors the real IHttpClientFactory contract: CreateClient() returns a fresh
    // HttpClient wrapper per call (safe for the "using var client = ..." disposal
    // pattern TickerUniverseService uses), backed by the SAME shared handler so
    // call-count tracking still works across multiple CreateClient() calls.
    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
