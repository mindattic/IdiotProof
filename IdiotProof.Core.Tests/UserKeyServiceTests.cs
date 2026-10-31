using IdiotProof.Core.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T330: account secrets live only in MindAttic.Vault. Saves write the Vault (per-user entry
/// names, the owner keeping alpaca-paper/alpaca-live), the SQL row keeps only non-secret choices,
/// and reads never fall back to SQL. Runs on an in-memory database and a temp-directory Vault.
/// </summary>
[TestFixture]
public sealed class UserKeyServiceTests
{
    private TempVault vault = null!;
    private IDbContextFactory<AppDbContext> factory = null!;
    private UserKeyService svc = null!;

    [SetUp]
    public void SetUp()
    {
        vault = new TempVault();
        factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"keys-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        svc = new UserKeyService(factory, new EphemeralDataProtectionProvider(), vault.Vault, NullLogger<UserKeyService>.Instance);
    }

    [TearDown]
    public void TearDown() => vault.Dispose();

    [Test]
    public async Task SaveAndReload_RoundTripsEverySecretThroughVault()
    {
        var userId = Guid.NewGuid();
        await svc.SaveAsync(userId, new UserApiKeys
        {
            UserId                  = userId,
            AlpacaApiKeyId          = "PKALPACAPAPERID",
            AlpacaApiSecretKey      = "alpaca-secret-xyz",
            AlpacaLiveApiKeyId      = "AKALPACALIVEID",
            AlpacaLiveApiSecretKey  = "alpaca-live-secret",
            ClaudeApiKey            = "sk-ant-abc",
            AlpacaOAuthAccessToken  = "oauth-access",
            AlpacaOAuthRefreshToken = "oauth-refresh",
            ClaudeModel             = "claude-sonnet-5",
            LlmVotingEnabled        = true,
            DefaultBroker           = "alpaca",
            DefaultDataFeed         = "Alpaca",
        });

        var reloaded = await svc.GetOrCreateAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.AlpacaApiKeyId, Is.EqualTo("PKALPACAPAPERID"));
            Assert.That(reloaded.AlpacaApiSecretKey, Is.EqualTo("alpaca-secret-xyz"));
            Assert.That(reloaded.AlpacaLiveApiKeyId, Is.EqualTo("AKALPACALIVEID"));
            Assert.That(reloaded.AlpacaLiveApiSecretKey, Is.EqualTo("alpaca-live-secret"));
            Assert.That(reloaded.ClaudeApiKey, Is.EqualTo("sk-ant-abc"));
            Assert.That(reloaded.AlpacaOAuthAccessToken, Is.EqualTo("oauth-access"));
            Assert.That(reloaded.AlpacaOAuthRefreshToken, Is.EqualTo("oauth-refresh"));
            Assert.That(reloaded.DefaultBroker, Is.EqualTo("alpaca"));
            Assert.That(reloaded.DefaultDataFeed, Is.EqualTo("Alpaca"));
            Assert.That(reloaded.LlmVotingEnabled, Is.True);
        });
    }

    [Test]
    public async Task Save_NeverWritesASecretToSql()
    {
        var userId = Guid.NewGuid();
        await svc.SaveAsync(userId, new UserApiKeys
        {
            UserId = userId, AlpacaApiKeyId = "PKNOTINSQL", AlpacaApiSecretKey = "secret-not-in-sql",
            ClaudeApiKey = "sk-ant-not-in-sql", AlpacaOAuthAccessToken = "tok", DefaultBroker = "alpaca",
        });

        await using var db = await factory.CreateDbContextAsync();
        var raw = await db.UserApiKeys.AsNoTracking().SingleAsync(k => k.UserId == userId);
        Assert.Multiple(() =>
        {
            Assert.That(UserKeyService.LegacySecretColumns(raw), Is.Empty, "no secret column may hold anything");
            Assert.That(raw.DefaultBroker, Is.EqualTo("alpaca"), "the non-secret opt-in still lives in SQL");
            Assert.That(vault.AllText(), Does.Contain("secret-not-in-sql").And.Contain("sk-ant-not-in-sql"));
        });
    }

    [Test]
    public async Task Owner_KeepsTheCanonicalEntries_OtherUsersGetTheirOwn()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        vault.Owner = owner;

        await svc.SaveAsync(owner, new UserApiKeys { UserId = owner, AlpacaApiKeyId = "PKOWNER", AlpacaApiSecretKey = "owner-secret" });
        await svc.SaveAsync(other, new UserApiKeys { UserId = other, AlpacaApiKeyId = "PKOTHER", AlpacaApiSecretKey = "other-secret" });

        Assert.Multiple(() =>
        {
            Assert.That(vault.Brokers.GetBrokerCreds("alpaca-paper")!.ApiKey, Is.EqualTo("PKOWNER"));
            Assert.That(vault.Brokers.GetBrokerCreds($"idiotproof-{other:N}-alpaca-paper")!.ApiKey, Is.EqualTo("PKOTHER"));
            Assert.That(vault.Brokers.GetBrokerCreds("alpaca-paper")!.BaseUrl, Is.EqualTo("https://paper-api.alpaca.markets"));
        });
        Assert.That((await svc.GetOrCreateAsync(other)).AlpacaApiKeyId, Is.EqualTo("PKOTHER"), "never the owner's pair");
        Assert.That((await svc.GetOrCreateAsync(Guid.NewGuid())).AlpacaApiKeyId, Is.Null, "a stranger resolves nothing");
    }

    [Test]
    public async Task NoOwnerKnown_EvenTheCanonicalEntryIsNotHandedOut()
    {
        vault.Brokers.SetBrokerCreds("alpaca-paper", new("PKCANON", "canon-secret", null));
        Assert.That((await svc.GetOrCreateAsync(Guid.NewGuid())).AlpacaApiKeyId, Is.Null);
    }

    [Test]
    public async Task ResavingUnrelatedField_DoesNotWipeOtherKeys()
    {
        var userId = Guid.NewGuid();
        await svc.SaveAsync(userId, new UserApiKeys { UserId = userId, AlpacaApiKeyId = "PKKEEPME", AlpacaApiSecretKey = "keep" });

        var current = await svc.GetOrCreateAsync(userId);
        current.ClaudeApiKey = "sk-ant-new";
        await svc.SaveAsync(userId, current);

        var reloaded = await svc.GetOrCreateAsync(userId);
        Assert.That(reloaded.AlpacaApiKeyId, Is.EqualTo("PKKEEPME"), "an unrelated save must not wipe the Alpaca key");
        Assert.That(reloaded.ClaudeApiKey, Is.EqualTo("sk-ant-new"));
    }

    [Test]
    public async Task BlankSecrets_RemoveTheVaultEntries()
    {
        var userId = Guid.NewGuid();
        await svc.SaveAsync(userId, new UserApiKeys { UserId = userId, AlpacaApiKeyId = "PKGONE", AlpacaApiSecretKey = "gone", ClaudeApiKey = "sk-ant-gone" });

        var current = await svc.GetOrCreateAsync(userId);
        current.AlpacaApiKeyId = current.AlpacaApiSecretKey = current.ClaudeApiKey = null;
        await svc.SaveAsync(userId, current);

        Assert.That(vault.AllText(), Does.Not.Contain("PKGONE").And.Not.Contain("sk-ant-gone"));
    }

    [Test]
    public void HalfAnAlpacaPair_IsRefused()
    {
        var userId = Guid.NewGuid();
        Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(userId, new UserApiKeys { UserId = userId, AlpacaApiKeyId = "PKHALF" }));
    }

    [Test]
    public async Task LegacySqlSecrets_AreNeverRead()
    {
        var userId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserApiKeys.Add(new UserApiKeys
            {
                UserId = userId, DefaultBroker = "alpaca",
                AlpacaApiKeyId = svc.ProtectLegacy("PKLEGACY"), AlpacaApiSecretKey = svc.ProtectLegacy("legacy-secret"),
                ClaudeApiKey = svc.ProtectLegacy("sk-ant-legacy"),
            });
            await db.SaveChangesAsync();
        }

        var keys = await svc.GetOrCreateAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(keys.AlpacaApiKeyId, Is.Null);
            Assert.That(keys.ClaudeApiKey, Is.Null);
            Assert.That(keys.DefaultBroker, Is.EqualTo("alpaca"));
        });
        Assert.That(await svc.GetLegacyDbSecretColumnsAsync(userId), Is.EquivalentTo(new[] { "AlpacaApiKeyId", "AlpacaApiSecretKey", "ClaudeApiKey" }));
    }

    [Test]
    public async Task SavePreferences_LeavesUnmigratedLegacyColumnsAlone()
    {
        var userId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserApiKeys.Add(new UserApiKeys { UserId = userId, ClaudeApiKey = svc.ProtectLegacy("sk-ant-legacy") });
            await db.SaveChangesAsync();
        }

        await svc.SetDefaultBrokerAsync(userId, "alpaca");

        Assert.That(await svc.GetLegacyDbSecretColumnsAsync(userId), Is.EqualTo(new[] { "ClaudeApiKey" }),
            "only the migration may clear a legacy column, after Vault has the value");
    }

    [Test]
    public void VaultOwner_ReadsConfigThenCliFile()
    {
        var id = Guid.NewGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IdiotProof:VaultOwner"] = id.ToString() }).Build();
        Assert.That(VaultOwner.Resolve(config, Path.Combine(vault.Root, "missing.json")), Is.EqualTo(id));

        Directory.CreateDirectory(vault.Root);
        var cli = Path.Combine(vault.Root, "cli.json");
        var saved = Guid.NewGuid();
        File.WriteAllText(cli, $$"""{"owner":"{{saved}}"}""");
        if (Environment.GetEnvironmentVariable("IDIOTPROOF_OWNER") is null)
            Assert.That(VaultOwner.Resolve(new ConfigurationBuilder().Build(), cli), Is.EqualTo(saved));
    }

    [Test]
    public void NoBucketFile_ReadsTheSameNamesFromConfiguration()
    {
        var owner = Guid.NewGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MindAttic:Vault:Brokers:alpaca-live:apiKey"] = "AKFROMENV",
            ["MindAttic:Vault:Brokers:alpaca-live:secret"] = "env-secret",
            [$"MindAttic:Vault:Tokens:idiotproof-{owner:N}-claude"] = "sk-ant-env",
        }).Build();
        var cloud = new AccountSecretVault(vault.Brokers, vault.Tokens, config, () => owner);

        Assert.That(cloud.GetAlpaca(owner, isPaper: false), Is.EqualTo(("AKFROMENV", "env-secret")));
        Assert.That(cloud.GetToken(owner, AccountToken.Claude), Is.EqualTo("sk-ant-env"));
    }
}
