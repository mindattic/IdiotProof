using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Tests.Account;
using MindAttic.Vault.Credentials;

namespace IdiotProof.Operations.Tests.Keys;

/// <summary>
/// Keys/settings/users operation harness: an in-memory database plus a throwaway MindAttic.Vault
/// (Brokers + Tokens buckets in a temp directory), so no test ever reads or writes the real
/// <c>%APPDATA%\MindAttic</c> Vault or LocalDB. Dispose deletes the temp Vault.
/// </summary>
internal sealed class KeysTestKit : IDisposable
{
    public static readonly Guid Owner = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public KeysTestKit(Guid? vaultOwner = null)
    {
        VaultRoot = Path.Combine(Path.GetTempPath(), $"ip-ops-vault-{Guid.NewGuid():N}");
        Brokers = new BrokerCredentialStore(Path.Combine(VaultRoot, "Brokers"));
        Tokens = new TokenStore(Path.Combine(VaultRoot, "Tokens"));
        var owner = vaultOwner ?? Owner;
        Vault = new AccountSecretVault(Brokers, Tokens, configuration: null, () => owner);
        Services.AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"keys-ops-{Guid.NewGuid():N}"));
        Services.AddSingleton(Vault);
        Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        Services.AddSingleton(sp => new UserKeyService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<IDataProtectionProvider>(),
            Vault, NullLogger<UserKeyService>.Instance));
        Services.AddSingleton<AlpacaApiFactory>(Alpaca);
        Services.AddSingleton(sp => new VaultSecretMigration(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<UserKeyService>(),
            NullLogger<VaultSecretMigration>.Instance));
    }

    public string VaultRoot { get; }

    /// <summary>Scripted Alpaca hosts for key probes; nothing reaches the network.</summary>
    public ScriptedKeyFactory Alpaca { get; } = new();

    public BrokerCredentialStore Brokers { get; }

    public TokenStore Tokens { get; }

    public AccountSecretVault Vault { get; }

    /// <summary>Register more before the first <see cref="Context"/> call.</summary>
    public ServiceCollection Services { get; } = new();

    private IServiceProvider? provider;

    public IServiceProvider Provider => provider ??= Services.BuildServiceProvider();

    public UserKeyService Keys => Provider.GetRequiredService<UserKeyService>();

    public IDbContextFactory<AppDbContext> Db => Provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    /// <summary>Every byte the temp Vault holds.</summary>
    public string VaultText() =>
        Directory.Exists(VaultRoot)
            ? string.Join("\n", Directory.EnumerateFiles(VaultRoot, "*", SearchOption.AllDirectories).Select(File.ReadAllText))
            : "";

    public OperationContext Context(Guid? owner = null, string? confirm = null, int? width = 140) => new()
    {
        OwnerId = owner ?? Owner,
        Services = Provider,
        ConfirmationCode = confirm,
        Width = width,
    };

    public void Dispose()
    {
        try { Directory.Delete(VaultRoot, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Hands back clients over scripted handlers and records which key ids were probed.</summary>
internal sealed class ScriptedKeyFactory() : AlpacaApiFactory(null!, null!, null!)
{
    public RouteHandler Paper { get; } = new();

    public RouteHandler Live { get; } = new();

    public List<(string KeyId, bool IsPaper)> Probed { get; } = [];

    public override AlpacaApi ForKeys(string keyId, string secret, bool isPaper)
    {
        Probed.Add((keyId, isPaper));
        return new AlpacaApi(keyId, secret, isPaper, isPaper ? Paper : Live);
    }
}
