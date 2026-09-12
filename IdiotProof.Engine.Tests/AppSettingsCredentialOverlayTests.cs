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
}
