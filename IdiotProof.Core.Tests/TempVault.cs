using MindAttic.Vault.Credentials;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// A throwaway MindAttic.Vault (Brokers + Tokens buckets) in a temp directory, so key tests never
/// read or write the real <c>%APPDATA%\MindAttic</c> Vault. Dispose deletes it.
/// </summary>
internal sealed class TempVault : IDisposable
{
    public TempVault(Guid? owner = null)
    {
        Root = Path.Combine(Path.GetTempPath(), $"ip-vault-{Guid.NewGuid():N}");
        Brokers = new BrokerCredentialStore(Path.Combine(Root, "Brokers"));
        Tokens = new TokenStore(Path.Combine(Root, "Tokens"));
        Owner = owner;
        Vault = new AccountSecretVault(Brokers, Tokens, configuration: null, () => Owner);
    }

    public string Root { get; }

    public BrokerCredentialStore Brokers { get; }

    public TokenStore Tokens { get; }

    public Guid? Owner { get; set; }

    public AccountSecretVault Vault { get; }

    /// <summary>Every byte the Vault files hold, for "the secret is (not) in here" assertions.</summary>
    public string AllText() =>
        Directory.Exists(Root)
            ? string.Join("\n", Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Select(File.ReadAllText))
            : "";

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}
