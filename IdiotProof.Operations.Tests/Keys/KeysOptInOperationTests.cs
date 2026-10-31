using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Keys;

namespace IdiotProof.Operations.Tests.Keys;

/// <summary>`keys opt-in alpaca|off` (IP-T248): the DefaultBroker consent switch, nothing else.</summary>
public class KeysOptInOperationTests
{
    private KeysTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new KeysTestKit();

    [TearDown]
    public void TearDown() => kit.Dispose();

    [Test]
    public async Task OptIn_FlipsRouting_AndSaysWhichKeysAreMissing()
    {
        var result = await new KeysOptInOperation().ExecuteAsync(new KeysOptInArgs { Broker = "alpaca" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.That(result.Human, Does.Contain("sandbox -> alpaca").And.Contain("No paper keys in Vault"));
        Assert.That((await kit.Keys.GetOrCreateAsync(KeysTestKit.Owner)).DefaultBroker, Is.EqualTo("alpaca"));
    }

    [Test]
    public async Task Off_GoesBackToSandbox_AndKeepsTheKeys()
    {
        await kit.Keys.SaveAsync(KeysTestKit.Owner, new UserApiKeys
        {
            UserId = KeysTestKit.Owner, DefaultBroker = "alpaca", AlpacaApiKeyId = "PKKEEPTHISKEY123", AlpacaApiSecretKey = "keep",
        });

        var result = await new KeysOptInOperation().ExecuteAsync(new KeysOptInArgs { Broker = "off" }, kit.Context());

        var keys = await kit.Keys.GetOrCreateAsync(KeysTestKit.Owner);
        Assert.That(result.Human, Does.Contain("alpaca -> sandbox"));
        Assert.That(keys.DefaultBroker, Is.EqualTo("Sandbox"));
        Assert.That(keys.AlpacaApiKeyId, Is.EqualTo("PKKEEPTHISKEY123"));
    }

    [Test]
    public async Task SameState_IsANoOp_AndJunkIsAUsageError()
    {
        var same = await new KeysOptInOperation().ExecuteAsync(new KeysOptInArgs { Broker = "off" }, kit.Context());
        var junk = await new KeysOptInOperation().ExecuteAsync(new KeysOptInArgs { Broker = "ibkr" }, kit.Context());

        Assert.That(same.Human, Is.EqualTo("Routing is already sandbox."));
        Assert.That(junk.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
