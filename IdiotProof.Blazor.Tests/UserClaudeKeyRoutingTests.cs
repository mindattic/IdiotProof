using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The Describe tab (StrategyScriptGenerator), the Gapper transcript reader (GapperInterpreter) and
/// the Research page (ResearchService → CatalystExtractor) call Claude with the signed-in user's own
/// key, then the host key, else they say no key is configured. A fake Anthropic transport records the
/// key each call carried; fakes stand in for the user-key store and the signed-in principal.
/// </summary>
[TestFixture]
public sealed class UserClaudeKeyRoutingTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob   = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid(); // no key of her own
    private const string AliceKey = "sk-ant-alice";
    private const string BobKey   = "sk-ant-bob";
    private const string HostKey  = "sk-ant-host";

    private static UserClaudeKeyResolver Resolver(string? hostKey) => new(
        (id, _) => Task.FromResult(new UserApiKeys
        {
            UserId = id,
            ClaudeApiKey = id == Alice ? AliceKey : id == Bob ? BobKey : null,
        }),
        () => hostKey, () => false, NullLogger<UserClaudeKeyResolver>.Instance);

    private static ClaimsPrincipal SignedIn(Guid id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    // ── Who is asking decides the key ──

    [Test]
    public async Task SignedInKey_IsResolvedPerCall_ForWhoeverIsSignedIn()
    {
        ClaimsPrincipal current = SignedIn(Alice);
        var signedIn = new SignedInClaudeKey(() => Task.FromResult(current), Resolver(HostKey));

        var asAlice = await signedIn.ResolveAsync();
        current = SignedIn(Bob);
        var asBob = await signedIn.ResolveAsync();
        current = SignedIn(Carol);
        var asCarol = await signedIn.ResolveAsync();
        current = Anonymous();
        var anonymous = await signedIn.ResolveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(asAlice.ClaudeApiKey, Is.EqualTo(AliceKey));
            Assert.That(asBob.ClaudeApiKey, Is.EqualTo(BobKey), "never Alice's key, even on the same instance");
            Assert.That(asCarol.ClaudeApiKey, Is.EqualTo(HostKey));
            Assert.That(asCarol.KeySource, Is.EqualTo("host"));
            Assert.That(anonymous.ClaudeApiKey, Is.EqualTo(HostKey));
        });
    }

    // ── Describe tab ──

    [Test]
    public async Task Describe_CallsClaudeWithTheUsersOwnKey()
    {
        var vendor = new FakeAnthropic("""Stock.Ticker("NVDA").IsAboveVwap().Long().Build()""");
        var generator = new StrategyScriptGenerator(Legion(vendor), new AppSettings(), NullLogger<StrategyScriptGenerator>.Instance);

        var alice = await generator.GenerateAsync("buy above vwap", "NVDA", await KeyFor(Alice));
        var bob   = await generator.GenerateAsync("buy above vwap", "NVDA", await KeyFor(Bob));

        Assert.That(alice.Success && bob.Success, Is.True);
        Assert.That(vendor.Keys, Is.EqualTo(new[] { AliceKey, BobKey }));
    }

    [Test]
    public async Task Describe_WithNoUserOrHostKey_SaysNoKeyConfigured_AndCallsNothing()
    {
        var vendor = new FakeAnthropic("unused");
        var generator = new StrategyScriptGenerator(Legion(vendor), new AppSettings(), NullLogger<StrategyScriptGenerator>.Instance);

        var result = await generator.GenerateAsync("buy above vwap", "NVDA", await KeyFor(Carol, hostKey: null));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo(UserClaudeKeyResolver.NoKeyMessage));
        Assert.That(vendor.Keys, Is.Empty);
    }

    // ── Gapper transcript reader ──

    [Test]
    public async Task GapperTranscript_CallsClaudeWithTheUsersOwnKey_ThenTheHostKey()
    {
        var vendor = new FakeAnthropic("[]");
        var interpreter = new GapperInterpreter(Legion(vendor), new AppSettings(),
            new GapperProfileService(new NoWebRoot(), NullLogger<GapperProfileService>.Instance),
            NullLogger<GapperInterpreter>.Instance);

        await interpreter.InterpretAsync("ADVB gapping 30% premarket", await KeyFor(Bob));
        await interpreter.InterpretAsync("ADVB gapping 30% premarket", await KeyFor(Carol));
        var none = await interpreter.InterpretAsync("ADVB gapping 30% premarket", await KeyFor(Carol, hostKey: null));

        Assert.That(vendor.Keys, Is.EqualTo(new[] { BobKey, HostKey }));
        Assert.That(none.Error, Is.EqualTo(UserClaudeKeyResolver.NoKeyMessage));
    }

    // ── Research page ──

    [Test]
    public async Task ResearchAnalysis_ExtractsWithTheUsersOwnKey()
    {
        var vendor = new FakeAnthropic("""{"ticker":"NVDA","catalysts":[],"source_assessment":"Editorial","source_tier":2}""");
        var legion = Legion(vendor);
        var db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"research-{Guid.NewGuid():N}"))
            .BuildServiceProvider();
        var research = new ResearchService(
            new CatalystExtractor(legion, new AppSettings(), NullLogger<CatalystExtractor>.Instance),
            edgar: null!, usSpends: null!, alpacaNews: null!,
            db.GetRequiredService<IServiceScopeFactory>(),
            db.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            NullLogger<ResearchService>.Instance);

        var aliceKey = (await KeyFor(Alice)).ClaudeApiKey;
        await research.AnalyzeArticleAsync("NVDA", "NVDA wins a contract.", "Reuters", "https://example.com/a",
            2, DateOnly.FromDateTime(DateTime.UtcNow), default, aliceKey);

        Assert.That(vendor.Keys, Is.EqualTo(new[] { AliceKey }));
    }

    // ── Helpers ──

    private static Task<SignalVotingCredentials> KeyFor(Guid user, string? hostKey = HostKey) =>
        new SignedInClaudeKey(() => Task.FromResult(SignedIn(user)), Resolver(hostKey)).ResolveAsync();

    private static LegionClient Legion(FakeAnthropic vendor) => new(new HttpClient(vendor), options: null);

    private sealed class NoWebRoot : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), $"no-webroot-{Guid.NewGuid():N}");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "IdiotProof.Blazor";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }

    /// <summary>Anthropic-shaped responder that records the x-api-key of every call.</summary>
    private sealed class FakeAnthropic(string reply) : HttpMessageHandler
    {
        public List<string> Keys { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Keys) Keys.Add(request.Headers.TryGetValues("x-api-key", out var k) ? k.Single() : "");
            var payload = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = reply } }, stop_reason = "end_turn" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
