using System.Net;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Keys;

namespace IdiotProof.Operations.Tests.Keys;

/// <summary>`keys probe` (IP-T249): one read-only account call per stored pair, against scripted hosts.</summary>
public class KeysProbeOperationTests
{
    private KeysTestKit kit = null!;

    [SetUp]
    public async Task SetUp()
    {
        kit = new KeysTestKit();
        await kit.Keys.SaveAsync(KeysTestKit.Owner, new UserApiKeys
        {
            UserId = KeysTestKit.Owner,
            AlpacaApiKeyId = "PKPROBEPAPER1234", AlpacaApiSecretKey = "probe-paper-secret",
            AlpacaLiveApiKeyId = "AKPROBELIVE12345", AlpacaLiveApiSecretKey = "probe-live-secret",
        });
    }

    [TearDown]
    public void TearDown() => kit.Dispose();

    [Test]
    public async Task Both_RoundTrip_WithMaskedAccountNumbers()
    {
        kit.Alpaca.Paper.On("GET /v2/account", """{"account_number":"PA3PG9SP46WU","status":"ACTIVE","trading_blocked":false}""");
        kit.Alpaca.Live.On("GET /v2/account", """{"account_number":"252673877","status":"ACTIVE","trading_blocked":true}""");

        var result = await new KeysProbeOperation().ExecuteAsync(new KeysProbeArgs(), kit.Context());
        var everything = result.Human + JsonSerializer.Serialize(result.Data);

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("***46WU").And.Contain("***3877").And.Contain("YES"));
            Assert.That(everything, Does.Not.Contain("PA3PG9SP46WU").And.Not.Contain("252673877"));
            Assert.That(everything, Does.Not.Contain("probe-paper-secret").And.Not.Contain("PKPROBEPAPER1234"));
            Assert.That(kit.Alpaca.Paper.Requests.Concat(kit.Alpaca.Live.Requests).All(r => r.Method == HttpMethod.Get));
            Assert.That(result.Human, Does.Contain("Routing is Sandbox"));
        });
    }

    [Test]
    public async Task RejectedKey_FailsWithAlpacasReason()
    {
        kit.Alpaca.Live.On("GET /v2/account", """{"code":40110000,"message":"request is not authorized"}""", HttpStatusCode.Unauthorized);

        var result = await new KeysProbeOperation().ExecuteAsync(new KeysProbeArgs { Mode = AccountScope.Live }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("live: 401"));
        Assert.That(kit.Alpaca.Probed, Is.EqualTo(new[] { ("AKPROBELIVE12345", false) }));
    }

    [Test]
    public async Task NoKeys_SaysSo_WithoutCallingAlpaca()
    {
        var stranger = Guid.NewGuid();

        var result = await new KeysProbeOperation().ExecuteAsync(new KeysProbeArgs(), kit.Context(owner: stranger));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.That(result.Human, Does.Contain("no keys in Vault"));
        Assert.That(kit.Alpaca.Probed, Is.Empty);
    }
}
