using System.Runtime.CompilerServices;
using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Operations.Replay;
using IdiotProof.Operations.Tests.Market;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Replay;

/// <summary>
/// The world a replay / backtest / scan / dataset operation sees, all in memory: EF InMemory
/// (strategies, replay runs, feature rows, diary), a feed that serves <see cref="MockDataFeed"/>'s
/// deterministic bars under a non-Mock name (GAP* symbols gap every premarket), a scripted Alpaca
/// Market Data host for the movers screener, and temp folders for pages, CSVs and saved runs.
/// </summary>
internal sealed class ReplayWorld : IDisposable
{
    /// <summary>Fri 2026-07-17 — a GAP* gap day in the Mock feed.</summary>
    public static readonly DateOnly Day = new(2026, 7, 17);

    public Guid Owner { get; } = Guid.NewGuid();

    public IDbContextFactory<AppDbContext> Db { get; }

    public ScriptedMarketHandler Market { get; } = new();

    public string Temp { get; } = Path.Combine(Path.GetTempPath(), "ip-replay-tests-" + Guid.NewGuid().ToString("N"));

    public string Pages => Path.Combine(Temp, "replays");

    public IServiceProvider Services { get; }

    public ReplayWorld(IMarketDataFeed? feed = null)
    {
        var sc = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"replay-{Guid.NewGuid():N}"));
        sc.AddSingleton(TimeProvider.System);
        sc.AddSingleton<StrategyRepository>();
        sc.AddSingleton(feed ?? new NamedMockFeed());
        sc.AddSingleton<AlpacaApiFactory>(new ScriptedMarketFactory(Market));
        sc.AddSingleton(new SavedRunStore(Path.Combine(Temp, "runs")));
        Services = sc.BuildServiceProvider();
        Db = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    /// <summary>Sat 2026-07-18 noon ET, the day after <see cref="Day"/>.</summary>
    public OperationContext Context(DateTimeOffset? now = null) => new()
    {
        Services = Services,
        OwnerId = Owner,
        Caller = OperationCaller.Cli,
        Clock = new Fixtures.FixedClock(now ?? new DateTimeOffset(2026, 7, 18, 16, 0, 0, TimeSpan.Zero)),
    };

    /// <summary>A gapper for <paramref name="symbol"/> tuned to fire on the Mock gap day (wide price band, 8% stop).</summary>
    public async Task<Strategy> AddGapperAsync(string symbol = "GAPT", bool active = true)
    {
        var p = new GapperProfile
        {
            Id = "test", Name = "Test", MinGapPercent = 5, MinVolumeRatio = 2, MinPrice = 1, MaxPrice = 5000,
            EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00", StopLossPercent = 8, PeakGivebackPercent = 25,
            ArmExitAtEt = "09:12", SellByEt = "09:28", DefaultNotional = 1000m,
        };
        var b = GapperScriptFactory.Compose(symbol, p);
        var s = new Strategy
        {
            OwnerUserId = Owner, Title = $"{symbol} test gapper", Symbol = symbol, ScriptText = b.ToScript(),
            ScriptJson = StrategyJson.Serialize(b.Build()), IsActive = active, BrokerMode = "Paper",
        };
        await using var db = await Db.CreateDbContextAsync();
        db.Strategies.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public async Task<List<ReplayRun>> RunsAsync()
    {
        await using var db = await Db.CreateDbContextAsync();
        return await db.ReplayRuns.AsNoTracking().OrderBy(r => r.GeneratedUtc).ToListAsync();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Temp)) Directory.Delete(Temp, true); } catch (IOException) { }
        (Services as IDisposable)?.Dispose();
    }

    /// <summary>MockDataFeed's deterministic bars, served under the name "Alpaca" (the replay ops refuse the Mock feed itself).</summary>
    internal sealed class NamedMockFeed : IMarketDataFeed
    {
        private readonly MockDataFeed inner = new();

        public string FeedName => "Alpaca";

        public List<string> Requested { get; } = [];

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            lock (Requested) Requested.Add(symbol);
            await foreach (var c in inner.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, candleSize, ct)) yield return c;
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => inner.GetLatestPriceAsync(symbol, ct);
    }
}
