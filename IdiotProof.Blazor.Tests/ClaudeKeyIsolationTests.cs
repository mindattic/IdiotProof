using System.Security.Claims;
using Bunit;
using IdiotProof.Blazor.Components.Pages;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Vault.Credentials;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// A Claude key saved on the API Keys page belongs to that user alone: it lands on their encrypted
/// UserApiKeys row and nowhere else — not in the Vault LLM keyring (the host key every user falls
/// back to) and not in any other user's resolution. Drives the real page through bUnit with an
/// in-memory database and a throwaway Vault LLM directory.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class ClaudeKeyIsolationTests
{
    private const string AliceKey = "sk-ant-alice-only";

    private string vaultDir = null!;
    private string? previousVaultEnv;

    [SetUp]
    public void SetUp()
    {
        // Point the Vault LLM keyring (what AppSettings.ClaudeApiKey reads) at an empty temp dir.
        vaultDir = Path.Combine(Path.GetTempPath(), $"ip-llm-vault-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultDir);
        previousVaultEnv = Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar);
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, vaultDir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, previousVaultEnv);
        try { Directory.Delete(vaultDir, recursive: true); } catch { /* best effort */ }
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
        page.WaitForAssertion(() => Assert.That(page.Markup, Does.Contain("Keys saved successfully.")));

        // Her own row has it.
        Assert.That((await userKeys.GetOrCreateAsync(alice)).ClaudeApiKey, Is.EqualTo(AliceKey));

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
            new EphemeralDataProtectionProvider(), NullLogger<UserKeyService>.Instance));
        ctx.Services.AddHttpClient();
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.AddAuthorization().SetAuthorized("bob@example.com")
            .SetClaims(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var page = ctx.Render<ApiKeys>();

        Assert.That(page.Find("#claude-key-scope").TextContent, Does.Contain("for your account only"));
    }
}
