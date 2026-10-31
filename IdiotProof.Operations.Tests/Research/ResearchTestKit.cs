using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Identity;
using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Tests.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindAttic.Legion;

namespace IdiotProof.Operations.Tests.Research;

/// <summary>
/// The world a research operation sees, all in memory: EF InMemory research tables, the real
/// correlation/scoring/backfill/universe/extraction services over a scripted HTTP handler (Alpaca
/// data routes) and a scripted LLM, and a fake scan pass. Nothing reaches SEC, EDGAR, the Federal
/// Register, Alpaca or an LLM.
/// </summary>
internal sealed class ResearchKit
{
    public Guid Owner { get; } = Guid.NewGuid();

    public IDbContextFactory<AppDbContext> Db { get; }

    /// <summary>Alpaca (and any other HTTP) replies, by "METHOD /path".</summary>
    public RouteHandler Http { get; } = new();

    /// <summary>What the scripted LLM answers (the CatalystExtractor JSON).</summary>
    public ScriptedLlm Llm { get; } = new();

    public FakeScanPass Scan { get; } = new();

    public AppSettings Settings { get; } = new() { AlpacaApiKeyId = "k", AlpacaApiSecretKey = "s" };

    private readonly IServiceProvider services;

    public ResearchKit()
    {
        var sc = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"research-{Guid.NewGuid():N}"));
        sc.AddSingleton(Settings);
        sc.AddSingleton<IHttpClientFactory>(new ScriptedHttpFactory(Http));
        sc.AddSingleton<ClaimCorrelationService>();
        sc.AddSingleton<SignificanceScorer>();
        sc.AddSingleton<OutcomeBackfillService>();
        sc.AddSingleton<TickerUniverseService>();
        sc.AddSingleton(new LegionClient(new HttpClient(Llm), options: null));
        sc.AddSingleton<CatalystExtractor>();
        sc.AddSingleton<AlpacaNewsService>();
        sc.AddSingleton<IResearchScanPass>(Scan);
        sc.AddSingleton<IClaudeKeyContext>(new TestClaudeKey());
        services = sc.BuildServiceProvider();
        Db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    public OperationContext Context(Guid? owner = null) => new()
    {
        Services = services,
        OwnerId = owner ?? Owner,
        Caller = OperationCaller.Cli,
    };

    public async Task<ResearchClaim> AddClaimAsync(
        string ticker = "NVDA", string summary = "Signed a $2B supply contract", double? score = 50,
        string sentiment = "Bullish", string magnitude = "High", bool happened = true, string type = "Contract",
        DateTime? createdUtc = null, bool macro = false, string[]? affects = null, string source = "Reuters",
        DateOnly? articleDate = null, decimal? outcomePct = null, string status = "Realized", string? trigger = null)
    {
        var c = new ResearchClaim
        {
            Ticker = macro ? "" : ticker, ClaimSummary = summary, SignificanceScore = score, Sentiment = sentiment,
            Magnitude = magnitude, HasHappenedAlready = happened, ClaimType = type, SourceName = source, SourceTier = 2,
            CreatedUtc = createdUtc ?? DateTime.UtcNow.AddHours(-2),
            ArticleDate = articleDate ?? DateOnly.FromDateTime((createdUtc ?? DateTime.UtcNow).AddDays(-1)),
            IsMacro = macro, AffectedTickersJson = affects is null ? null : JsonSerializer.Serialize(affects),
            OutcomePctChange = outcomePct, Status = happened ? status : "Pending", PendingTrigger = trigger,
            TriggerConfidence = happened ? null : "High", SourceUrl = $"https://news.example/{Guid.NewGuid():N}",
        };
        await using var db = await Db.CreateDbContextAsync();
        db.ResearchClaims.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public async Task AddAsync(params object[] rows)
    {
        await using var db = await Db.CreateDbContextAsync();
        foreach (var r in rows) db.Add(r);
        await db.SaveChangesAsync();
    }

    private sealed class TestClaudeKey : IClaudeKeyContext
    {
        public Task<SignalVotingCredentials> ResolveAsync(CancellationToken ct = default) =>
            Task.FromResult(new SignalVotingCredentials(false, "owner-key", null, "owner"));
    }
}

/// <summary>Every named client terminates in the one scripted handler.</summary>
internal sealed class ScriptedHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>An Anthropic-shaped LLM reply carrying <see cref="Reply"/>; records each request body.</summary>
internal sealed class ScriptedLlm : HttpMessageHandler
{
    public string Reply { get; set; } = """
        {"ticker":"NVDA","catalysts":[
          {"summary":"Preliminary $5B data-center order pending signature","type":"Contract","sentiment":"Bullish",
           "magnitude":"High","has_happened_already":false,"pending_trigger":"Contract signature","expected_timeline":"weeks",
           "trigger_confidence":"High","mechanism":"adds a large multi-year revenue stream once signed"}],
         "source_assessment":"Editorial","source_tier":2}
        """;

    public List<string> Requests { get; } = [];

    /// <summary>Each request's headers as "name=value;..." (the API key travels in a header).</summary>
    public List<string> Headers { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Headers.Add(string.Join(";", request.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value))));
        Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        var payload = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = Reply } }, stop_reason = "end_turn" });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }
}

/// <summary>A scan pass that records what it was asked and returns a canned run (no EDGAR, no news).</summary>
internal sealed class FakeScanPass : IResearchScanPass
{
    public ScanPassOptions? Last { get; private set; }

    public Task<ScanRun> RunAsync(ScanPassOptions options, CancellationToken ct = default)
    {
        Last = options;
        var started = DateTime.UtcNow.AddSeconds(-4);
        return Task.FromResult(new ScanRun
        {
            StartedUtc = started, CompletedUtc = started.AddSeconds(4), TickersScanned = options.Tickers?.Count ?? 12,
            UniverseSize = options.Tickers?.Count ?? 5000, ClaimsFound = 3, Notes = " index-events (+1)",
        });
    }
}
