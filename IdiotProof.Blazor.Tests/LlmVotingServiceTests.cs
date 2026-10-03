using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Services;
using IdiotProof.Models;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;
using LlmVotingService = IdiotProof.Blazor.Services.LlmVotingService;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The LLM gate (IP-LAW-1) on MindAttic.Legion's voter panel. The panel runs for real
/// (Legion's LlmVotingService + LlmVotingProvider + LegionClient); only the HTTP transport is a
/// fake that answers in each vendor's wire shape, so nothing leaves the process.
/// </summary>
[TestFixture]
public sealed class LlmVotingServiceTests
{
    private const string OwnerKey  = "sk-ant-owner-key";
    private const string OpenAiKey = "sk-openai-test-key";

    // ── Fail-closed defaults ──

    [Test]
    public void LlmVotingResult_DefaultConsensus_IsAbstain_NotApprove()
    {
        // Approve is enum zero: an uninitialized result (panel unavailable,
        // zero votes) must never read as an approval on the money path.
        Assert.That(new LlmVotingResult().Consensus, Is.EqualTo(VoteDecision.Abstain));
    }

    [Test]
    public void MapDecision_OnlyExactApproveOrReject_Count()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LlmVotingService.MapDecision("Approve"), Is.EqualTo(VoteDecision.Approve));
            Assert.That(LlmVotingService.MapDecision(" reject "), Is.EqualTo(VoteDecision.Reject));
            Assert.That(LlmVotingService.MapDecision("Abstain"), Is.EqualTo(VoteDecision.Abstain));
            Assert.That(LlmVotingService.MapDecision("Approve, probably"), Is.EqualTo(VoteDecision.Abstain));
            Assert.That(LlmVotingService.MapDecision(""), Is.EqualTo(VoteDecision.Abstain));
            Assert.That(LlmVotingService.MapDecision(null), Is.EqualTo(VoteDecision.Abstain));
        });
    }

    // ── Consensus over counted votes ──

    [Test]
    public void CalculateConsensus_AllApprove_ConsensusApprove()
    {
        var result = ResultWith((VoteDecision.Approve, 80m), (VoteDecision.Approve, 90m), (VoteDecision.Approve, 70m));
        LlmVotingService.CalculateConsensus(result, 0.66m);
        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Approve));
        Assert.That(result.ConsensusConfidence, Is.EqualTo(80m).Within(0.01m));
    }

    [Test]
    public void CalculateConsensus_AllReject_ConsensusReject()
    {
        var result = ResultWith((VoteDecision.Reject, 60m), (VoteDecision.Reject, 70m));
        LlmVotingService.CalculateConsensus(result, 0.66m);
        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Reject));
    }

    [Test]
    public void CalculateConsensus_BelowThreshold_ConsensusAbstain()
    {
        // 2 of 4 approve = 0.5 < 0.66; 1 of 4 reject.
        var result = ResultWith((VoteDecision.Approve, 80m), (VoteDecision.Approve, 80m),
            (VoteDecision.Reject, 60m), (VoteDecision.Abstain, 50m));
        LlmVotingService.CalculateConsensus(result, 0.66m);
        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Abstain));
    }

    [Test]
    public void CalculateConsensus_NoVotes_StaysAbstain()
    {
        var result = new LlmVotingResult();
        LlmVotingService.CalculateConsensus(result, 0.66m);
        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Abstain));
    }

    // ── Panel composition from legion.json ──

    [Test]
    public void ShippedLegionJson_DeclaresTheFourVoterPanel_WithClaudeAsJudge()
    {
        var cfg = ShippedConfig();
        Assert.That(cfg.AllowedProviderIds, Is.EquivalentTo(new[] { "claude", "openai", "gemini", "deepseek" }));
        Assert.That(cfg.JudgeProviderId, Is.EqualTo("claude"));
    }

    [Test]
    public void BuildPanel_SeatsClaudeOnTheOwnersKey_PlusEveryKeyedLegionVoter()
    {
        var cfg = ShippedConfig();
        cfg.ApiKeys["openai"] = OpenAiKey;
        cfg.ApiKeys["mistral"] = "not-on-the-panel"; // not in legion.json → never seated
        var svc = LlmVotingService.Create(new HttpClient(new FakeVendors((_, _) => "{}")), cfg, NullLoggerFactory.Instance);

        var panel = svc.BuildPanel(OwnerKey, "claude-sonnet-5");

        Assert.That(panel.Select(v => v.ProviderId), Is.EqualTo(new[] { "claude", "openai" }));
        Assert.That(panel[0].ApiKeyOverride, Is.EqualTo(OwnerKey));
        Assert.That(panel[0].ModelOverride, Is.EqualTo("claude-sonnet-5"));
        Assert.That(panel[1].ApiKeyOverride, Is.Null, "non-Claude voters use the shared keyring / legion.json keys");
        Assert.That(panel[0].Name, Does.Contain("Risk Manager"));
        Assert.That(panel[1].Name, Does.Contain("Momentum Trader"));
    }

    // ── The real Legion panel over a fake transport ──

    [Test]
    public async Task Vote_PanelApproves_ConsensusApprove_AndClaudeVotesOnTheOwnersKey()
    {
        var vendors = new FakeVendors((_, _) => Ballot("Approve", 8));
        var svc = Service(vendors, openAi: true);

        var result = await svc.VoteOnSignalAsync(Signal(), [], Creds(OwnerKey), 0.66m);

        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Approve));
        Assert.That(result.Votes.Select(v => v.ModelId), Is.EquivalentTo(new[] { "claude", "openai" }));
        Assert.That(result.ConsensusConfidence, Is.EqualTo(80m));
        Assert.That(vendors.KeysSeen["api.anthropic.com"], Is.EqualTo(new[] { OwnerKey }));
        Assert.That(vendors.KeysSeen["api.openai.com"], Is.EqualTo(new[] { OpenAiKey }));
        Assert.That(vendors.Bodies.All(b => b.Contains("TRADE SIGNAL") && b.Contains("NVDA")), Is.True,
            "every voter sees the signal context");
    }

    [Test]
    public async Task Vote_SplitPanel_FailsClosedToAbstain()
    {
        var vendors = new FakeVendors((host, _) => host == "api.anthropic.com" ? Ballot("Approve", 9) : Ballot("Reject", 7));
        var result = await Service(vendors, openAi: true).VoteOnSignalAsync(Signal(), [], Creds(OwnerKey), 0.66m);

        Assert.That(result.Votes, Has.Count.EqualTo(2));
        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Abstain));
    }

    [Test]
    public async Task Vote_UnparseableBallot_NeverApproves()
    {
        // Legion marks an off-ballot / malformed answer as a failed seat; with no
        // surviving provider to refill from, the panel has zero votes.
        var vendors = new FakeVendors((_, _) => """{"Decision":"Approve","confidence":10}""");
        var result = await Service(vendors, openAi: false).VoteOnSignalAsync(Signal(), [], Creds(OwnerKey), 0.66m);

        Assert.That(result.Votes, Is.Empty);
        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Abstain));
    }

    [Test]
    public async Task Vote_DisabledOrUnkeyed_SkipsThePanel()
    {
        var vendors = new FakeVendors((_, _) => Ballot("Approve", 9));
        var svc = Service(vendors, openAi: true);

        var disabled = await svc.VoteOnSignalAsync(Signal(), [], new SignalVotingCredentials(false, OwnerKey, null, "owner"), 0.66m);
        var unkeyed  = await svc.VoteOnSignalAsync(Signal(), [], new SignalVotingCredentials(true, null, null, "none"), 0.66m);

        Assert.That(disabled.Votes, Is.Empty);
        Assert.That(unkeyed.Votes, Is.Empty);
        Assert.That(vendors.Bodies, Is.Empty, "no vendor call is made");
    }

    // ── Helpers ──

    private static LlmVotingService Service(FakeVendors vendors, bool openAi)
    {
        var cfg = ShippedConfig();
        if (openAi) cfg.ApiKeys["openai"] = OpenAiKey;
        return LlmVotingService.Create(new HttpClient(vendors), cfg, NullLoggerFactory.Instance);
    }

    /// <summary>The repo's legion.json, sandboxed from the machine's shared keyring.</summary>
    private static VotingConfiguration ShippedConfig()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "repo root (IdiotProof.slnx) not found");
        var legion = LegionConfig.LoadFromFile(Path.Combine(dir!.FullName, LegionConfig.FileName));
        Assert.That(legion, Is.Not.Null, "legion.json must parse");
        var cfg = LlmVotingService.BuildConfiguration(legion);
        cfg.UseSharedCredentials = false;
        return cfg;
    }

    private static SignalVotingCredentials Creds(string key) => new(true, key, null, "owner");

    private static TradeSignal Signal() => new()
    {
        Symbol = "NVDA", Direction = TradeDirection.Long, SuggestedEntry = 100m, SuggestedStop = 98m,
        Targets = [104m], StrategyName = "test", Reason = "All 3 conditions met", GeneratedUtc = DateTime.UtcNow,
    };

    private static string Ballot(string decision, int confidence) =>
        JsonSerializer.Serialize(new { decision, reasoning = $"{decision} because the test says so", confidence });

    private static LlmVotingResult ResultWith(params (VoteDecision decision, decimal confidence)[] votes) => new()
    {
        Votes = votes.Select((v, i) => new LlmVote { PersonaName = $"voter{i}", Decision = v.decision, Confidence = v.confidence }).ToList(),
    };

    /// <summary>
    /// Answers Anthropic (content[].text) and OpenAI-compatible (choices[].message.content) calls with
    /// <c>reply(host, body)</c>, recording the key each vendor was called with.
    /// </summary>
    private sealed class FakeVendors(Func<string, string, string> reply) : HttpMessageHandler
    {
        public Dictionary<string, List<string>> KeysSeen { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var host = request.RequestUri!.Host;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var key = request.Headers.TryGetValues("x-api-key", out var k) ? k.Single()
                : request.Headers.Authorization?.Parameter ?? "";
            lock (Bodies)
            {
                Bodies.Add(body);
                if (!KeysSeen.TryGetValue(host, out var list)) KeysSeen[host] = list = new();
                list.Add(key);
            }

            var text = reply(host, body);
            var payload = host == "api.anthropic.com"
                ? JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = "end_turn" })
                : JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = text } } } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }
}
