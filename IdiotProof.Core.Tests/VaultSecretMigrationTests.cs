using IdiotProof.Core.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T330's one-time move of legacy SQL secrets into Vault, on an in-memory database and a
/// temp-directory Vault: copy when Vault lacks it, verify equality when both have it, clear the
/// SQL column only after that, leave conflicts and undecryptable rows untouched, change nothing on
/// a dry run, and find nothing to do the second time.
/// </summary>
[TestFixture]
public sealed class VaultSecretMigrationTests
{
    private TempVault vault = null!;
    private IDbContextFactory<AppDbContext> factory = null!;
    private UserKeyService keys = null!;
    private VaultSecretMigration migration = null!;
    private readonly Guid owner = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        vault = new TempVault(owner);
        factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"migrate-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        keys = new UserKeyService(factory, new EphemeralDataProtectionProvider(), vault.Vault, NullLogger<UserKeyService>.Instance);
        migration = new VaultSecretMigration(factory, keys, NullLogger<VaultSecretMigration>.Instance);
    }

    [TearDown]
    public void TearDown() => vault.Dispose();

    private async Task SeedLegacyAsync(Guid userId, string? paperId = null, string? paperSecret = null, string? claude = null, string? rawClaude = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.UserApiKeys.Add(new UserApiKeys
        {
            UserId = userId,
            DefaultBroker = "alpaca",
            AlpacaApiKeyId = paperId is null ? null : keys.ProtectLegacy(paperId),
            AlpacaApiSecretKey = paperSecret is null ? null : keys.ProtectLegacy(paperSecret),
            ClaudeApiKey = rawClaude ?? (claude is null ? null : keys.ProtectLegacy(claude)),
        });
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<string>> LegacyColumnsAsync(Guid userId) => await keys.GetLegacyDbSecretColumnsAsync(userId);

    [Test]
    public async Task DryRun_ReportsButChangesNothing()
    {
        var user = Guid.NewGuid();
        await SeedLegacyAsync(user, "PKDRYRUN", "dry-secret", "sk-ant-dry");

        var report = await migration.RunAsync(apply: false);

        Assert.Multiple(() =>
        {
            Assert.That(report.Applied, Is.False);
            Assert.That(report.Items.Select(i => (i.Secret, i.Action)), Is.EquivalentTo(new[]
            {
                ("alpaca-paper", VaultMigrationAction.CopyToVault),
                ("claude", VaultMigrationAction.CopyToVault),
            }));
            Assert.That(report.Items.All(i => !i.Done));
            Assert.That(vault.AllText(), Is.Empty, "a dry run never writes Vault");
        });
        Assert.That(await LegacyColumnsAsync(user), Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Apply_CopiesIntoTheUsersOwnEntries_ThenClearsSql()
    {
        var user = Guid.NewGuid();
        await SeedLegacyAsync(user, "PKNONOWNER", "non-owner-secret", "sk-ant-nonowner");
        await SeedLegacyAsync(owner, "PKOWNERKEY", "owner-secret");

        var report = await migration.RunAsync(apply: true);

        Assert.Multiple(() =>
        {
            Assert.That(report.RowsCleared, Is.EqualTo(2));
            Assert.That(report.Items.All(i => i.Done && i.Action == VaultMigrationAction.CopyToVault));
            Assert.That(vault.Brokers.GetBrokerCreds($"idiotproof-{user:N}-alpaca-paper")!.Secret, Is.EqualTo("non-owner-secret"));
            Assert.That(vault.Brokers.GetBrokerCreds("alpaca-paper")!.ApiKey, Is.EqualTo("PKOWNERKEY"));
            Assert.That(vault.Tokens.Get($"idiotproof-{user:N}-claude"), Is.EqualTo("sk-ant-nonowner"));
        });
        Assert.That(await LegacyColumnsAsync(user), Is.Empty);
        Assert.That(await LegacyColumnsAsync(owner), Is.Empty);
        Assert.That((await keys.GetOrCreateAsync(user)).AlpacaApiSecretKey, Is.EqualTo("non-owner-secret"));
        Assert.That((await keys.GetOrCreateAsync(user)).DefaultBroker, Is.EqualTo("alpaca"), "non-secret settings survive");
    }

    [Test]
    public async Task VaultAlreadyEqual_JustClearsSql()
    {
        vault.Vault.SetAlpaca(owner, isPaper: true, "PKSAME", "same-secret");
        await SeedLegacyAsync(owner, "PKSAME", "same-secret");

        var report = await migration.RunAsync(apply: true);

        Assert.That(report.Items.Single().Action, Is.EqualTo(VaultMigrationAction.AlreadyInVault));
        Assert.That(await LegacyColumnsAsync(owner), Is.Empty);
        Assert.That(vault.Brokers.GetBrokerCreds("alpaca-paper")!.Secret, Is.EqualTo("same-secret"));
    }

    [Test]
    public async Task VaultDiffers_IsAConflict_AndNothingIsTouched()
    {
        vault.Vault.SetAlpaca(owner, isPaper: true, "PKVAULTVALUE", "vault-secret");
        await SeedLegacyAsync(owner, "PKSQLVALUE", "sql-secret");

        var report = await migration.RunAsync(apply: true);

        Assert.Multiple(() =>
        {
            Assert.That(report.Conflicts, Is.EqualTo(1));
            Assert.That(report.Items.Single().Done, Is.False);
            Assert.That(vault.Brokers.GetBrokerCreds("alpaca-paper")!.ApiKey, Is.EqualTo("PKVAULTVALUE"), "Vault is not overwritten");
        });
        Assert.That(await LegacyColumnsAsync(owner), Has.Count.EqualTo(2), "SQL is not cleared");
    }

    [Test]
    public async Task UndecryptableCiphertext_IsReported_AndLeftInPlace()
    {
        var user = Guid.NewGuid();
        await SeedLegacyAsync(user, rawClaude: "CfDJ8-not-really-ciphertext");

        var report = await migration.RunAsync(apply: true);

        Assert.That(report.Undecryptable, Is.EqualTo(1));
        Assert.That(await LegacyColumnsAsync(user), Is.EqualTo(new[] { "ClaudeApiKey" }));
    }

    [Test]
    public async Task SecondRun_FindsNothingToDo()
    {
        await SeedLegacyAsync(Guid.NewGuid(), "PKTWICE", "twice", "sk-ant-twice");
        await migration.RunAsync(apply: true);

        var again = await migration.RunAsync(apply: true);

        Assert.That(again.Items, Is.Empty);
        Assert.That(again.RowsCleared, Is.Zero);
    }
}
