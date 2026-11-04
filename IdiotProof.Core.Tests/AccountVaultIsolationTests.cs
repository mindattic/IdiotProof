using IdiotProof.Core.Services;
using MindAttic.Vault.Paths;
using NUnit.Framework;

namespace IdiotProof.Core.Tests;

[TestFixture]
public class AccountVaultIsolationTests
{
    private static Func<string, string?> Env(string? vaultDir) =>
        name => name == AccountSecretVault.IsolatedDirectoryEnvVar ? vaultDir : null;

    [Test]
    public void FakeLlmSeam_InDevelopment_IsolatesTheVault_EvenWithoutTheEnvVar()
    {
        var dir = AccountSecretVault.ResolveIsolatedDirectory(isDevelopment: true, useFakeLlm: true, Env(null));
        Assert.That(dir, Is.Not.Null);
        Assert.That(dir, Does.StartWith(Path.GetTempPath()));
    }

    [Test]
    public void ExplicitEnvVar_WinsInDevelopment()
    {
        Assert.That(AccountSecretVault.ResolveIsolatedDirectory(true, false, Env(@"C:\tmp\vault")), Is.EqualTo(@"C:\tmp\vault"));
    }

    [Test]
    public void Production_AlwaysUsesTheRealVault_EvenWhenTheEnvVarIsSet()
    {
        Assert.That(AccountSecretVault.ResolveIsolatedDirectory(false, true, Env(@"C:\tmp\vault")), Is.Null);
    }

    [Test]
    public void PlainDevelopment_WithoutTheSeam_UsesTheRealVault()
    {
        Assert.That(AccountSecretVault.ResolveIsolatedDirectory(true, false, Env(null)), Is.Null);
    }

    [Test]
    public void IsolatedVault_WritesUnderItsDirectory_NeverUnderTheRealRoamingRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ip-t394-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vault = AccountSecretVault.Isolated(dir, configuration: null);
            var user = Guid.NewGuid();
            vault.SetAlpaca(user, isPaper: true, "PKTEST", "SECRET");
            vault.SetToken(user, AccountToken.Claude, "sk-test");

            var realRoot = Path.GetFullPath(VaultPaths.RoamingRoot);
            Assert.That(Path.GetFullPath(vault.BrokersFile), Does.StartWith(Path.GetFullPath(dir)));
            Assert.That(Path.GetFullPath(vault.TokensFile), Does.StartWith(Path.GetFullPath(dir)));
            Assert.That(Path.GetFullPath(vault.BrokersFile), Does.Not.StartWith(realRoot));
            Assert.That(File.Exists(vault.BrokersFile), Is.True);
            Assert.That(File.Exists(vault.TokensFile), Is.True);
            Assert.That(vault.GetAlpaca(user, true), Is.EqualTo(("PKTEST", "SECRET")));
            Assert.That(vault.GetToken(user, AccountToken.Claude), Is.EqualTo("sk-test"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
