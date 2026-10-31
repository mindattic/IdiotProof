using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Keys;

namespace IdiotProof.Operations.Tests.Keys;

/// <summary>`keys migrate-to-vault` (IP-T330) over an in-memory database and a temp Vault.</summary>
public class KeysMigrateOperationTests
{
    private KeysTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new KeysTestKit();

    [TearDown]
    public void TearDown() => kit.Dispose();

    private async Task SeedAsync(Guid user, string keyId, string secret)
    {
        await using var db = await kit.Db.CreateDbContextAsync();
        db.UserApiKeys.Add(new UserApiKeys
        {
            UserId = user, DefaultBroker = "alpaca",
            AlpacaApiKeyId = kit.Keys.ProtectLegacy(keyId), AlpacaApiSecretKey = kit.Keys.ProtectLegacy(secret),
        });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task DryRun_ShowsThePlan_WithoutValues_AndChangesNothing()
    {
        await SeedAsync(KeysTestKit.Owner, "PKOWNERLEGACY1", "owner-legacy-secret");

        var result = await new KeysMigrateOperation().ExecuteAsync(new KeysMigrateArgs(), kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.That(result.Human, Does.Contain("alpaca-paper").And.Contain("copy-to-vault").And.Contain("Dry run"));
        Assert.That(result.Human + JsonSerializer.Serialize(result.Data), Does.Not.Contain("owner-legacy-secret").And.Not.Contain("PKOWNERLEGACY1"));
        Assert.That(kit.VaultText(), Is.Empty);
    }

    [Test]
    public async Task Apply_MovesIntoVault_AndClearsSql()
    {
        await SeedAsync(KeysTestKit.Owner, "PKOWNERLEGACY1", "owner-legacy-secret");

        var result = await new KeysMigrateOperation().ExecuteAsync(new KeysMigrateArgs { Apply = true }, kit.Context());

        Assert.That(result.Human, Does.Contain("cleared legacy secret columns on 1 row"));
        Assert.That(kit.Brokers.GetBrokerCreds("alpaca-paper")!.Secret, Is.EqualTo("owner-legacy-secret"));
        Assert.That(await kit.Keys.GetLegacyDbSecretColumnsAsync(KeysTestKit.Owner), Is.Empty);

        var again = await new KeysMigrateOperation().ExecuteAsync(new KeysMigrateArgs { Apply = true }, kit.Context());
        Assert.That(again.Human, Does.StartWith("Nothing to migrate"));
    }
}
