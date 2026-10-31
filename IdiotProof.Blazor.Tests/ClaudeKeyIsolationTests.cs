using System.Security.Claims;
using Bunit;
using IdiotProof.Blazor.Components.Pages;
using IdiotProof.Core.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Vault.Credentials;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// A Claude key saved on the API Keys page belongs to that user alone: it lands in their own Vault
/// Tokens entry (IP-T330) and nowhere else — not in the Vault LLM keyring (the host key every user falls
/// back to) and not in any other user's resolution. Drives the real page through bUnit with an
/// in-memory database and a throwaway Vault LLM directory.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class ClaudeKeyIsolationTests
{
    private const string AliceKey = "sk-ant-alice-only";

    private string vaultDir = null!;
    private string? previousVaultEnv;
    private string accountVaultDir = null!;

    /// <summary>A throwaway account-secret Vault (Brokers + Tokens), never the real APPDATA one.</summary>
    private AccountSecretVault NewAccountVault() => new(
        new BrokerCredentialStore(Path.Combine(accountVaultDir, "Brokers")),
        new TokenStore(Path.Combine(accountVaultDir, "Tokens")),
        configuration: null, () => null);

    [SetUp]
    public void SetUp()
    {
        // Point the Vault LLM keyring (what AppSettings.ClaudeApiKey reads) at an empty temp dir.
        vaultDir = Path.Combine(Path.GetTempPath(), $"ip-llm-vault-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        previousVaultEnv = Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar);
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, vaultDir);
        accountVaultDir = Path.Combine(Path.GetTempPath(), $"ip-account-vault-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, previousVaultEnv);
        try { Directory.Delete(vaultDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(accountVaultDir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public async Task SavingAClaudeKey_StoresItOnlyForThatUser_NeverForAnotherUserOrTheHost()
    {
        var alice = Guid.NewGuid();
        var bob   = Guid.NewGuid();

        var dbServices = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"keys-{Guid.NewGuid():N}"))
            .BuildServiceProvider();
        var userKeys = new UserKeyService(
            dbServices.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            new EphemeralDataProtectionProvider(),
            NewAccountVault(),
            NullLogger<UserKeyService>.Instance);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(userKeys);
        ctx.Services.AddHttpClient();
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.AddAuthorization().SetAuthorized("alice@example.com")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, alice.ToString()));

        // Alice types her key on the API Keys page and presses Save All.
        var page = ctx.Render<ApiKeys>();
        page.Find("#claude-key").Change(AliceKey);
        await page.Find("button[aria-label='Save all API keys and connection settings']").ClickAsync(new());
        page.WaitForAssertion(() => Assert.That(page.Markup, Does.Contain("Keys saved to Vault.")));

        // Her own Vault entry has it, and the page never echoes it back.
        Assert.That((await userKeys.GetOrCreateAsync(alice)).ClaudeApiKey, Is.EqualTo(AliceKey));
        Assert.That(File.ReadAllText(Path.Combine(accountVaultDir, "Tokens", "tokens.json")), Does.Contain($"idiotproof-{alice:N}-claude"));
        Assert.That(page.Markup, Does.Not.Contain(AliceKey), "a stored key is never rendered back into the page");

        // The host key did not change: nothing was written to the Vault LLM keyring.
        var settings = new AppSettings();
        Assert.That(settings.ClaudeApiKey, Is.Empty, "a user's key must never become the host key");
        Assert.That(Directory.EnumerateFiles(vaultDir, "*", SearchOption.AllDirectories), Is.Empty,
            "the API Keys page wrote to the Vault LLM keyring");

        // Bob gets nothing of Alice's: no key of his own, no host key → none.
        var resolver = new UserClaudeKeyResolver(userKeys, settings, NullLogger<UserClaudeKeyResolver>.Instance);
        var bobCreds = await resolver.ResolveAsync(bob);
        Assert.That(bobCreds.ClaudeApiKey, Is.Null);
        Assert.That(bobCreds.KeySource, Is.EqualTo("none"));

        // And Alice's own strategies vote on her key.
        var aliceCreds = await resolver.ResolveAsync(alice);
        Assert.That(aliceCreds.ClaudeApiKey, Is.EqualTo(AliceKey));
        Assert.That(aliceCreds.KeySource, Is.EqualTo("owner"));
    }

    [Test]
    public void ApiKeysPage_SaysTheKeyIsForThisAccountOnly()
    {
        using var ctx = new BunitContext();
        var dbServices = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"keys-{Guid.NewGuid():N}"))
            .BuildServiceProvider();
        ctx.Services.AddSingleton(new UserKeyService(dbServices.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            new EphemeralDataProtectionProvider(), NewAccountVault(), NullLogger<UserKeyService>.Instance));
        ctx.Services.AddHttpClient();
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.AddAuthorization().SetAuthorized("bob@example.com")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var page = ctx.Render<ApiKeys>();

        Assert.That(page.Find("#claude-key-scope").TextContent, Does.Contain("for your account only"));
    }
}
