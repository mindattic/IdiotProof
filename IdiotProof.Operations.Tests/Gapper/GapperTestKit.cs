using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Catalogs;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Replay;
using IdiotProof.Operations.Tests.Market;
using MindAttic.Legion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>
/// The world a gapper operation sees, all in memory: an owner, EF InMemory strategies, scans,
/// settings and audit rows, the real profile catalog, a scripted Alpaca Market Data host (movers),
/// the deterministic <see cref="MockDataFeed"/> (or a scripted one), a temp-dir run store and a
/// Legion client whose "model" is a canned reply — nothing reaches a network or a real account.
/// </summary>
internal sealed class GapperKit : IDisposable
{
    public Guid Owner { get; } = Guid.NewGuid();

    public IDbContextFactory<AppDbContext> Db { get; }

    public ScriptedMarketHandler Market { get; } = new();

    public FakeModel Model { get; } = new();

    public AppSettings Settings { get; } = new();

    public string RunDir { get; } = Path.Combine(Path.GetTempPath(), "ip-gapper-tests-" + Guid.NewGuid().ToString("N"));

    public IServiceProvider Services { get; }

    public GapperKit(IMarketDataFeed? feed = null)
    {
        var sc = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"gapper-{Guid.NewGuid():N}"));
        sc.AddSingleton(TimeProvider.System);
        sc.AddSingleton<IStaticCatalogSource>(StaticCatalogSource.Default);
        sc.AddSingleton<GapperProfileService>();
        sc.AddSingleton<StrategyRepository>();
        sc.AddSingleton<ConditionProgressRepository>();
        sc.AddSingleton<ConfirmationStore>();
        sc.AddSingleton<AuditLogRepository>();
        sc.AddSingleton<SettingsRepository>();
        sc.AddSingleton(Settings);
        sc.AddSingleton(feed ?? new MockDataFeed());
        sc.AddSingleton<AlpacaApiFactory>(new ScriptedMarketFactory(Market));
        sc.AddSingleton(new SavedRunStore(RunDir));
        sc.AddSingleton(new LegionClient(new HttpClient(Model), options: null));
        sc.AddSingleton<GapperInterpreter>();
        sc.AddSingleton<IClaudeKeyContext>(new KeyContext());
        Services = sc.BuildServiceProvider();
        Db = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    public OperationContext Context(string? confirm = null, DateTimeOffset? now = null) => new()
    {
        Services = Services,
        OwnerId = Owner,
        ConfirmationCode = confirm,
        Caller = OperationCaller.Cli,
        Clock = new Fixtures.FixedClock(now ?? new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.Zero)),
    };

    /// <summary>A gapper row exactly as the queue path writes it (factory canon), optionally active.</summary>
    public async Task<Strategy> AddGapperAsync(string symbol, bool active = false, BrokerMode mode = BrokerMode.Paper, int positionQty = 0, string? author = null, Guid? owner = null)
    {
        var p = Services.GetRequiredService<GapperProfileService>().GetById("classic-gapper")!.Clone();
        var builder = IdiotProof.Scripting.GapperScriptFactory.Compose(symbol, p);
        var s = new Strategy
        {
            OwnerUserId = owner ?? Owner, Title = $"{symbol} Gapper — Classic Gapper", Symbol = symbol,
            ScriptText = builder.ToScript(), ScriptJson = IdiotProof.Scripting.StrategyJson.Serialize(builder.Build()),
            IsActive = active, BrokerMode = mode.ToString(), PositionQty = positionQty, Author = author,
            LastEntryPrice = positionQty == 0 ? null : 10m,
        };
        await using var db = await Db.CreateDbContextAsync();
        db.Strategies.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    /// <summary>A plain (non-gapper) strategy.</summary>
    public async Task<Strategy> AddPlainAsync(string symbol, bool active)
    {
        var s = new Strategy
        {
            OwnerUserId = Owner, Title = $"{symbol} plain", Symbol = symbol,
            ScriptText = $"Stock.Ticker(\"{symbol}\").IsAboveVwap().Long().StopLoss(1).TakeProfit(2).Build()",
            IsActive = active, BrokerMode = "Paper",
        };
        await using var db = await Db.CreateDbContextAsync();
        db.Strategies.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public async Task<List<Strategy>> StrategiesAsync()
    {
        await using var db = await Db.CreateDbContextAsync();
        return await db.Strategies.AsNoTracking().ToListAsync();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(RunDir)) Directory.Delete(RunDir, true); } catch (IOException) { }
        (Services as IDisposable)?.Dispose();
    }

    /// <summary>The Anthropic-Messages-shaped reply every LLM call gets; records the requests.</summary>
    internal sealed class FakeModel : HttpMessageHandler
    {
        public string Reply { get; set; } = "[]";

        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            var payload = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = Reply } }, stop_reason = "end_turn" });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class KeyContext : IClaudeKeyContext
    {
        public Task<SignalVotingCredentials> ResolveAsync(CancellationToken ct = default) =>
            Task.FromResult(new SignalVotingCredentials(false, "sk-test", "claude-sonnet-5", "test"));
    }
}
