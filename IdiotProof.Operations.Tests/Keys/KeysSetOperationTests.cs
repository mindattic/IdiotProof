using System.Net;
using System.Text.Json;
using IdiotProof.Operations.Keys;

namespace IdiotProof.Operations.Tests.Keys;

/// <summary>`keys set` (IP-T247): prefix check, read-only probe, then the temp Vault; never the real Vault, never SQL.</summary>
public class KeysSetOperationTests
{
    private const string Account = """{"account_number":"PA3PG9SP46WU","status":"ACTIVE"}""";

    private KeysTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new KeysTestKit();

    [TearDown]
    public void TearDown() => kit.Dispose();

    [Test]
    public async Task PaperPair_IsProbedThenStoredInTheOwnersCanonicalEntry()
    {
        kit.Alpaca.Paper.On("GET /v2/account", Account);

        var result = await new KeysSetOperation().ExecuteAsync(
            new KeysSetArgs { Target = "alpaca-paper", KeyId = "PKNEWPAPERKEY123", Secret = "new-paper-secret-value" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok), result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(kit.Brokers.GetBrokerCreds("alpaca-paper")!.Secret, Is.EqualTo("new-paper-secret-value"));
            Assert.That(kit.Alpaca.Probed, Is.EqualTo(new[] { ("PKNEWPAPERKEY123", true) }));
            Assert.That(kit.Alpaca.Paper.Requests.Single().Method, Is.EqualTo(HttpMethod.Get), "the probe only reads");
            Assert.That(result.Human, Does.Contain("***46WU").And.Contain("keys opt-in alpaca"));
            Assert.That(result.Human + JsonSerializer.Serialize(result.Data), Does.Not.Contain("new-paper-secret-value").And.Not.Contain("PA3PG9SP46WU"));
        });
    }

    [Test]
    public async Task NonOwner_GetsTheirOwnEntry()
    {
        var other = Guid.NewGuid();
        kit.Alpaca.Live.On("GET /v2/account", Account);

        await new KeysSetOperation().ExecuteAsync(
            new KeysSetArgs { Target = "alpaca-live", KeyId = "AKOTHERLIVEKEY12", Secret = "other-live-secret" }, kit.Context(owner: other));

        Assert.That(kit.Brokers.GetBrokerCreds($"idiotproof-{other:N}-alpaca-live")!.ApiKey, Is.EqualTo("AKOTHERLIVEKEY12"));
        Assert.That(kit.Brokers.GetBrokerCreds("alpaca-live"), Is.Null, "never the owner's entry");
    }

    [Test]
    public async Task WrongPrefix_IsRefused_BeforeAnyProbe()
    {
        var result = await new KeysSetOperation().ExecuteAsync(
            new KeysSetArgs { Target = "alpaca-paper", KeyId = "AKLIVEKEYINPAPER", Secret = "x-secret" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(kit.Alpaca.Probed, Is.Empty);
        Assert.That(kit.VaultText(), Is.Empty);
    }

    [Test]
    public async Task RejectedPair_IsNotStored()
    {
        kit.Alpaca.Paper.On("GET /v2/account", """{"code":40110000,"message":"request is not authorized"}""", HttpStatusCode.Unauthorized);

        var result = await new KeysSetOperation().ExecuteAsync(
            new KeysSetArgs { Target = "alpaca-paper", KeyId = "PKREVOKEDKEY1234", Secret = "revoked" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("401").And.Contain("Nothing was stored"));
        Assert.That(kit.VaultText(), Is.Empty);
    }

    [Test]
    public async Task Force_SkipsPrefixAndProbe()
    {
        var result = await new KeysSetOperation().ExecuteAsync(
            new KeysSetArgs { Target = "alpaca-paper", KeyId = "ODDKEYID", Secret = "odd-secret", Force = true }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.That(kit.Alpaca.Probed, Is.Empty);
        Assert.That(kit.Brokers.GetBrokerCreds("alpaca-paper")!.ApiKey, Is.EqualTo("ODDKEYID"));
    }

    [Test]
    public async Task Claude_GoesToTheUsersTokensEntry()
    {
        var result = await new KeysSetOperation().ExecuteAsync(
            new KeysSetArgs { Target = "claude", Secret = "sk-ant-claude-for-owner" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.That(kit.Tokens.Get($"idiotproof-{KeysTestKit.Owner:N}-claude"), Is.EqualTo("sk-ant-claude-for-owner"));
        Assert.That(result.Human, Does.Not.Contain("sk-ant-claude-for-owner"));
    }
}
