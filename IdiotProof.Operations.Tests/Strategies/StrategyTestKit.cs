using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Trading;
using IdiotProof.Engine.Settings;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>
/// The world a strategy operation sees, all in memory: an owner, EF InMemory strategies,
/// condition progress, audit, diary and command-ledger rows, a real
/// <see cref="ConfirmationStore"/>, and a <see cref="StrategyScriptGenerator"/> whose Legion
/// client terminates in <see cref="FakeLlmHandler"/> — no test ever reaches a real LLM or broker.
/// </summary>
internal sealed class StrategyKit
{
    public const string Script = "Stock.Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(170).TakeProfit(190).Build()";

    public Guid Owner { get; } = Guid.NewGuid();

    public IDbContextFactory<AppDbContext> Db { get; }

    public StrategyRepository Repo => services.GetRequiredService<StrategyRepository>();

    /// <summary>How many times the fake LLM was called.</summary>
    public int LlmCalls => llm.Calls;

    private readonly CountingLlm llm = new();
    private readonly IServiceProvider services;

    public StrategyKit()
    {
        var sc = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"strategies-{Guid.NewGuid():N}"));
        sc.AddSingleton(TimeProvider.System);
        sc.AddSingleton<StrategyRepository>();
        sc.AddSingleton<ConditionProgressRepository>();
        sc.AddSingleton<ConfirmationStore>();
        sc.AddSingleton<AuditLogRepository>();
        sc.AddSingleton<TradeDiaryRepository>();
        sc.AddSingleton(new StrategyScriptGenerator(new LegionClient(new HttpClient(llm), options: null), new AppSettings(),
            NullLogger<StrategyScriptGenerator>.Instance));
        sc.AddSingleton<IClaudeKeyContext>(new TestClaudeKey());
        services = sc.BuildServiceProvider();
        Db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    public OperationContext Context(string? confirm = null, Guid? owner = null) => new()
    {
        Services = services,
        OwnerId = owner ?? Owner,
        ConfirmationCode = confirm,
        Caller = OperationCaller.Cli,
    };

    /// <summary>Adds a strategy row with a canon (or the given raw canon/text for quarantine cases).</summary>
    public async Task<Strategy> AddAsync(string symbol = "NVDA", string? script = null, BrokerMode mode = BrokerMode.Sandbox,
        bool active = false, int positionQty = 0, string? title = null, string? rawJson = null, bool noCanon = false, Guid? owner = null)
    {
        var text = script ?? Script.Replace("NVDA", symbol);
        var s = new Strategy
        {
            OwnerUserId = owner ?? Owner, Title = title ?? $"{symbol} test", Symbol = symbol, ScriptText = text,
            ScriptJson = noCanon ? null : rawJson ?? StrategyJson.Serialize(ScriptParser.ParseScript(text)!),
            IsActive = active, BrokerMode = mode.ToString(), PositionQty = positionQty,
            LastEntryPrice = positionQty == 0 ? null : 175m,
        };
        await using var db = await Db.CreateDbContextAsync();
        db.Strategies.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public async Task<Strategy> ReloadAsync(Guid id)
    {
        await using var db = await Db.CreateDbContextAsync();
        return await db.Strategies.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    public async Task<List<Strategy>> AllAsync()
    {
        await using var db = await Db.CreateDbContextAsync();
        return await db.Strategies.AsNoTracking().ToListAsync();
    }

    public async Task AddAuditAsync(string category, string message, DateTime utc, Guid? owner = null)
    {
        await using var db = await Db.CreateDbContextAsync();
        db.AuditLogs.Add(new AuditLog { Category = category, Message = message, TimestampUtc = utc, UserId = owner ?? Owner });
        await db.SaveChangesAsync();
    }

    public async Task AddProgressAsync(Guid strategyId, int passed, int total, string? failing, decimal? price = null)
    {
        await services.GetRequiredService<ConditionProgressRepository>().UpsertAsync(strategyId, passed, total, failing, lastPrice: price);
    }

    private sealed class TestClaudeKey : IClaudeKeyContext
    {
        public Task<SignalVotingCredentials> ResolveAsync(CancellationToken ct = default) =>
            Task.FromResult(new SignalVotingCredentials(false, "test-key", null, "test"));
    }

    /// <summary>The house fake LLM, counted.</summary>
    private sealed class CountingLlm : DelegatingHandler
    {
        public int Calls;

        public CountingLlm() : base(new FakeLlmHandler()) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return base.SendAsync(request, ct);
        }
    }
}
