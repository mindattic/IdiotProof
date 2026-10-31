using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Operations.Keys;

namespace IdiotProof.Operations.Tests.Keys;

/// <summary>`keys status` (IP-T246): what resolves from where, never a secret.</summary>
public class KeysStatusOperationTests
{
    private KeysTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new KeysTestKit();

    [TearDown]
    public void TearDown() => kit.Dispose();

    [Test]
    public async Task ShowsVaultEntries_KeyIdPrefixes_AndNeverASecret()
    {
        await kit.Keys.SaveAsync(KeysTestKit.Owner, new UserApiKeys
        {
            UserId = KeysTestKit.Owner, DefaultBroker = "alpaca",
            AlpacaApiKeyId = "PKSTATUSPAPER123", AlpacaApiSecretKey = "status-paper-secret-value",
            ClaudeApiKey = "sk-ant-status-claude-secret",
        });

        var result = await new KeysStatusOperation().ExecuteAsync(new NoArgs(), kit.Context());
        var everything = result.Human + JsonSerializer.Serialize(result.Data);

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("Brokers/alpaca-paper").And.Contain("PKST..."));
            Assert.That(result.Human, Does.Contain("Brokers/alpaca-live"));
            Assert.That(result.Human, Does.Contain($"Tokens/idiotproof-{KeysTestKit.Owner:N}-claude"));
            Assert.That(result.Human, Does.Contain("you (canonical alpaca-paper/alpaca-live)"));
            Assert.That(everything, Does.Not.Contain("status-paper-secret-value"));
            Assert.That(everything, Does.Not.Contain("PKSTATUSPAPER123"));
            Assert.That(everything, Does.Not.Contain("status-claude-secret"));
        });
    }

    [Test]
    public async Task FlagsLegacySqlCopies_AndPointsAtTheMigration()
    {
        await using (var db = await kit.Db.CreateDbContextAsync())
        {
            db.UserApiKeys.Add(new UserApiKeys { UserId = KeysTestKit.Owner, ClaudeApiKey = kit.Keys.ProtectLegacy("sk-ant-legacy-value") });
            await db.SaveChangesAsync();
        }

        var result = await new KeysStatusOperation().ExecuteAsync(new NoArgs(), kit.Context());

        Assert.That(result.Human, Does.Contain("keys migrate-to-vault"));
        Assert.That(result.Human, Does.Contain("sandbox (opt in"));
        Assert.That(result.Human + JsonSerializer.Serialize(result.Data), Does.Not.Contain("sk-ant-legacy-value"));
    }

    [Test]
    public async Task NonOwner_SeesTheirOwnPerUserEntries()
    {
        var other = Guid.NewGuid();

        var result = await new KeysStatusOperation().ExecuteAsync(new NoArgs(), kit.Context(owner: other));

        Assert.That(result.Human, Does.Contain($"Brokers/idiotproof-{other:N}-alpaca-paper"));
    }
}
