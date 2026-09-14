using IdiotProof.Engine.Settings;
using MindAttic.Vault.Credentials;

namespace IdiotProof.Engine.Tests;

[TestFixture]
public class AppSettingsCredentialOverlayTests
{
    private const string EnvVar = LlmCredentialStore.DirectoryEnvVar;

    private string? original;
    private DirectoryInfo? tmp;

    [SetUp]
    public void SetUp()
    {
        original = Environment.GetEnvironmentVariable(EnvVar);
        tmp = Directory.CreateTempSubdirectory("idiotproof-vault-tests-");
        Environment.SetEnvironmentVariable(EnvVar, tmp.FullName);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(EnvVar, original);
        try { tmp?.Delete(recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Test]
    public void OverlayFromMindAtticCredentials_Prefers_Own_Scoped_Key_Over_Shared()
    {
        var store = new LlmCredentialStore(tmp!.FullName);
        store.SetKey("claude", "shared-key");
        store.SetKey("idiotproof-claude", "own-key");

        var settings = new AppSettings();
        settings.OverlayFromMindAtticCredentials();

        Assert.That(settings.ClaudeApiKey, Is.EqualTo("own-key"));
    }

    [Test]
    public void OverlayFromMindAtticCredentials_Falls_Back_To_Shared_Key_When_No_Own_Key()
    {
        var store = new LlmCredentialStore(tmp!.FullName);
        store.SetKey("claude", "shared-key");

        var settings = new AppSettings();
        settings.OverlayFromMindAtticCredentials();

        Assert.That(settings.ClaudeApiKey, Is.EqualTo("shared-key"));
    }

    [Test]
    public void OverlayFromMindAtticCredentials_Leaves_ClaudeApiKey_Unset_When_Neither_Key_Present()
    {
        var settings = new AppSettings();
        settings.OverlayFromMindAtticCredentials();

        Assert.That(settings.ClaudeApiKey, Is.Empty);
    }

    // Regression test for the bug this class was refactored to fix: ClaudeApiKey
    // used to be populated exactly once (via OverlayFromMindAtticCredentials at
    // startup) and cached, so a key written to Vault afterwards — e.g. from the
    // Blazor Settings/API Keys page — was invisible until the whole process
    // restarted. ClaudeApiKey is now a live pass-through that re-resolves from
    // the Vault-backed LlmCredentialStore on every read, with no caching and no
    // need to reload/reconstruct the AppSettings instance.
    [Test]
    public void ClaudeApiKey_Reflects_Vault_Write_On_Next_Read_Without_Reloading_AppSettings()
    {
        var settings = new AppSettings();
        Assert.That(settings.ClaudeApiKey, Is.Empty, "no key written yet");

        // Simulate the Blazor Settings/API Keys page's write-through path
        // (ApiKeys.razor -> PersistClaudeKeyToVault) firing *after* `settings`
        // was already constructed (e.g. the long-lived DI singleton) — without
        // touching `settings` at all, and without calling
        // OverlayFromMindAtticCredentials() again.
        var store = new LlmCredentialStore(tmp!.FullName);
        new AppScopedCredentialStore(AppSettings.AppId, store).SetKey("claude", "own-key-written-later");

        // Same AppSettings instance, no reconstruction, no re-overlay — the
        // very next read must already see the newly written key. This is the
        // regression this fix addresses: ClaudeApiKey used to be cached once
        // at startup, so this write would previously be invisible until the
        // whole process restarted.
        Assert.That(settings.ClaudeApiKey, Is.EqualTo("own-key-written-later"));

        // A second write, still on the same instance, is picked up just as
        // live — this isn't a one-time refresh, every read re-resolves.
        new AppScopedCredentialStore(AppSettings.AppId, store).SetKey("claude", "own-key-rotated-again");
        Assert.That(settings.ClaudeApiKey, Is.EqualTo("own-key-rotated-again"));
    }

}
