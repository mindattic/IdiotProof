using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Llm;
using IdiotProof.Operations.Tests.Indicators;
using IdiotProof.Operations.Tests.Keys;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;
using MindAttic.Vault.Credentials;
using LlmVotingService = IdiotProof.Core.Services.LlmVotingService;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Llm;

/// <summary>
/// llm panel|vote|keys|voting|threshold (IP-T221..IP-T225). The voter panel is Legion's real one
/// over a fake vendor transport, keys live in a throwaway Vault, and the host's LLM keyring is
/// pointed at an empty temp folder, so nothing leaves the process or reads the machine's keys.
/// </summary>
[NonParallelizable] // points the process-wide LLM keyring variable at a temp folder
public class LlmOperationsTests
{
    private const string OwnerKey = "sk-ant-owner-secret-0123456789";

    private KeysTestKit kit = null!;
    private FakeVendors vendors = null!;
    private AppSettings settings = null!;
    private string? savedKeyring;
    private string keyringDir = null!;

    [SetUp]
    public void SetUp()
    {
        savedKeyring = Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar);
        keyringDir = Path.Combine(Path.GetTempPath(), $"ip-llm-keyring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyringDir);
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, keyringDir);

        kit = new KeysTestKit();
        vendors = new FakeVendors();
        settings = new AppSettings { LlmConsensusThreshold = 0.66m, LlmVotingEnabled = false };
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = false,
            AllowedProviderIds = new HashSet<string>(["claude", "openai"], StringComparer.OrdinalIgnoreCase),
        };
        kit.Services.AddSingleton(settings);
        kit.Services.AddSingleton(sp => new UserClaudeKeyResolver(sp.GetRequiredService<UserKeyService>(), settings, NullLogger<UserClaudeKeyResolver>.Instance));
        kit.Services.AddSingleton(LlmVotingService.Create(new HttpClient(vendors), cfg, NullLoggerFactory.Instance));
        kit.Services.AddSingleton<StrategyRepository>();
        kit.Services.AddSingleton<IMarketDataFeed>(new StepFeed());
    }

    [TearDown]
    public void TearDown()
    {
        kit.Dispose();
        vendors.Dispose();
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, savedKeyring);
        try { Directory.Delete(keyringDir, recursive: true); } catch { /* best effort */ }
    }

    private OperationContext Context() => kit.Context();

    private async Task SetOwnerKeyAsync(bool voting = false)
    {
        var keys = await kit.Keys.GetOrCreateAsync(KeysTestKit.Owner);
        keys.ClaudeApiKey = OwnerKey;
        keys.LlmVotingEnabled = voting;
        await kit.Keys.SaveAsync(KeysTestKit.Owner, keys);
    }

    private static JsonElement Data(OperationResult r) => JsonSerializer.SerializeToElement(r.Data, OperationJson.Options);

    [Test]
    public async Task Panel_ListsSeatsLensesKeysAndThreshold_WithoutTheKey()
    {
        await SetOwnerKeyAsync(voting: true);

        var result = await new LlmPanelOperation().ExecuteAsync(new NoArgs(), Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var view = (LlmPanelView)result.Data!;
        Assert.Multiple(() =>
        {
            Assert.That(view.Voters.Select(v => v.Provider), Is.EqualTo(new[] { "claude" }));
            Assert.That(view.Voters[0].Lens, Is.EqualTo("Risk Manager"));
            Assert.That(view.Voters[0].HasKey, Is.True);
            Assert.That(view.Voters[0].KeySource, Is.EqualTo("owner"));
            Assert.That(view.ConfiguredWithoutKey, Is.EqualTo(new[] { "openai" }));
            Assert.That(view.Threshold, Is.EqualTo(0.66m));
            Assert.That(view.GateActive, Is.True);
            Assert.That(result.Render(OutputMode.Human) + result.Render(OutputMode.Json), Does.Not.Contain(OwnerKey));
        });
    }

    [Test]
    public async Task Keys_SaysWhereTheKeyResolves_NeverTheKey()
    {
        var none = await new LlmKeysOperation().ExecuteAsync(new NoArgs(), Context());
        Assert.That(((LlmKeyStatus)none.Data!).ResolvedFrom, Is.EqualTo("none"));
        Assert.That(((LlmKeyStatus)none.Data!).HostKeyAvailable, Is.False, "the host keyring points at an empty folder");

        await SetOwnerKeyAsync();
        var owned = await new LlmKeysOperation().ExecuteAsync(new NoArgs(), Context());

        var status = (LlmKeyStatus)owned.Data!;
        Assert.Multiple(() =>
        {
            Assert.That(status.ResolvedFrom, Is.EqualTo("owner"));
            Assert.That(status.OwnerKeyStored, Is.True);
            Assert.That(status.GateActive, Is.False, "voting is off for the owner and the host");
            Assert.That(owned.Render(OutputMode.Human) + owned.Render(OutputMode.Json), Does.Not.Contain(OwnerKey).And.Not.Contain("0123456789"));
        });
    }

    [Test]
    public async Task VotingToggle_WritesOnlyTheOwnersFlag()
    {
        await SetOwnerKeyAsync();

        var on = await new LlmVotingEnableOperation().ExecuteAsync(new NoArgs(), Context());
        Assert.That(on.IsOk, Is.True, on.Message);
        Assert.That(Data(on).GetProperty("gate_active").GetBoolean(), Is.True);
        Assert.That((await kit.Keys.GetOrCreateAsync(KeysTestKit.Owner)).LlmVotingEnabled, Is.True);
        Assert.That((await kit.Keys.GetOrCreateAsync(KeysTestKit.Owner)).ClaudeApiKey, Is.EqualTo(OwnerKey), "the key is untouched");

        settings.LlmVotingEnabled = true;
        var off = await new LlmVotingDisableOperation().ExecuteAsync(new NoArgs(), Context());
        Assert.That((await kit.Keys.GetOrCreateAsync(KeysTestKit.Owner)).LlmVotingEnabled, Is.False);
        Assert.That(off.Human, Does.Contain("host has voting on"));
        Assert.That(Data(off).GetProperty("gate_active").GetBoolean(), Is.True, "a user can't remove a host-wide gate");
    }

    [Test]
    public async Task Threshold_IsSavedForTheMonitor_AndRefusesBelowAHalf()
    {
        var shown = await new LlmThresholdOperation().ExecuteAsync(new LlmThresholdArgs(), Context());
        Assert.That(shown.Human, Does.Contain("66%"));

        var set = await new LlmThresholdOperation().ExecuteAsync(new LlmThresholdArgs { Value = 75m }, Context());
        Assert.That(set.IsOk, Is.True, set.Message);
        Assert.That(await LlmPanelSettings.ThresholdAsync(kit.Db, 0.66m), Is.EqualTo(0.75m), "what the Monitor's next vote reads");
        Assert.That(settings.LlmConsensusThreshold, Is.EqualTo(0.75m));

        var low = await new LlmThresholdOperation().ExecuteAsync(new LlmThresholdArgs { Value = 0.4m }, Context());
        Assert.That(low.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task Vote_IsADryRunOnTheRealPanel_AndPlacesNothing()
    {
        await SetOwnerKeyAsync(voting: false);
        var def = ScriptParser.ParseScript("Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(95).TakeProfit(130)")!;
        await using (var db = await kit.Db.CreateDbContextAsync())
        {
            db.Strategies.Add(new Strategy
            {
                OwnerUserId = KeysTestKit.Owner, Title = "NVDA vwap", Symbol = "NVDA", ScriptText = def.ToScript(),
                ScriptJson = StrategyJson.Serialize(def), BrokerMode = "Paper",
            });
            await db.SaveChangesAsync();
        }

        var ctx = new OperationContext
        {
            Services = kit.Provider, OwnerId = KeysTestKit.Owner,
            Clock = new Fixtures.FixedClock(new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc)),
        };
        var result = await new LlmVoteOperation().ExecuteAsync(new LlmVoteArgs { Strategy = "NVDA" }, ctx);

        Assert.That(result.IsOk, Is.True, result.Message);
        var data = Data(result);
        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("consensus").GetString(), Is.EqualTo("Approve"));
            Assert.That(data.GetProperty("votes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(data.GetProperty("dry_run").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("stop").GetDecimal(), Is.EqualTo(95m));
            Assert.That(vendors.Keys, Is.EqualTo(new[] { OwnerKey }), "voted on the owner's key");
            Assert.That(vendors.Bodies.Single(), Does.Contain("NVDA").And.Contain("Dry-run vote"));
            Assert.That(result.Human, Does.Contain("APPROVE").And.Contain("voting is off").And.Contain("nothing was placed"));
            Assert.That(result.Render(OutputMode.Json), Does.Not.Contain(OwnerKey));
        });
    }

    [Test]
    public async Task Vote_WithoutAClaudeKey_IsBlocked()
    {
        var ctx = Context();
        await using (var db = await kit.Db.CreateDbContextAsync())
        {
            db.Strategies.Add(new Strategy { OwnerUserId = KeysTestKit.Owner, Title = "t", Symbol = "NVDA", ScriptText = "Ticker(\"NVDA\").IsAboveVwap().StopLoss(95)" });
            await db.SaveChangesAsync();
        }

        var result = await new LlmVoteOperation().ExecuteAsync(new LlmVoteArgs { Strategy = "NVDA" }, ctx);

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(vendors.Bodies, Is.Empty);
    }

    /// <summary>Answers Anthropic's wire shape with an Approve ballot, recording keys and bodies.</summary>
    private sealed class FakeVendors : HttpMessageHandler
    {
        public List<string> Keys { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Bodies)
            {
                Bodies.Add(body);
                Keys.Add(request.Headers.TryGetValues("x-api-key", out var k) ? k.Single() : request.Headers.Authorization?.Parameter ?? "");
            }
            var ballot = JsonSerializer.Serialize(new { decision = "Approve", reasoning = "Clean reclaim of VWAP with a defined stop.", confidence = 8 });
            var payload = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = ballot } }, stop_reason = "end_turn" });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }
}
