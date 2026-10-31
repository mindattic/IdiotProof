using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using MindAttic.Vault.Credentials;

namespace IdiotProof.Blazor.Services;

/// <summary>A single-value account secret kept in the Vault Tokens bucket.</summary>
public enum AccountToken
{
    /// <summary>The user's own Claude API key (LLM voting on their strategies).</summary>
    Claude,

    /// <summary>Alpaca Connect OAuth access token.</summary>
    AlpacaOAuthAccess,

    /// <summary>Alpaca Connect OAuth refresh token.</summary>
    AlpacaOAuthRefresh,
}

/// <summary>
/// The one home for every account secret (IP-T330): Alpaca paper/live key pairs, the Alpaca
/// OAuth tokens and each user's Claude key live in MindAttic.Vault, never in SQL.
/// <list type="bullet">
/// <item>Alpaca pairs go in the <c>Brokers</c> bucket (<c>%APPDATA%\MindAttic\Brokers\providers.json</c>).
/// The machine owner keeps the canonical <c>alpaca-paper</c>/<c>alpaca-live</c> entries (the same
/// ones the Hub's own market-data keys come from); every other user gets entries of their own,
/// <c>idiotproof-{userId:N}-alpaca-paper|live</c>, so one user can never resolve another's account.</item>
/// <item>Claude keys and OAuth tokens go in the <c>Tokens</c> bucket (<c>tokens.json</c>) as
/// <c>idiotproof-{userId:N}-claude|alpaca-oauth-access|alpaca-oauth-refresh</c>, for every user.</item>
/// </list>
/// Reads use the bucket file when it exists (it is the writable, always-current local truth); a
/// host with no bucket file (Azure: env vars / Key Vault) reads the same names from
/// <c>MindAttic:Vault:&lt;Bucket&gt;:&lt;entry&gt;</c> in configuration. Values are never logged.
/// </summary>
public sealed class AccountSecretVault
{
    private readonly BrokerCredentialStore brokers;
    private readonly TokenStore tokens;
    private readonly IConfiguration? configuration;
    private readonly Func<Guid?> owner;

    /// <summary>The production vault: the real APPDATA buckets, owner from <see cref="VaultOwner"/>.</summary>
    public AccountSecretVault(IConfiguration configuration)
        : this(BrokerCredentialStore.Default, TokenStore.ForBucket("Tokens"), configuration, () => VaultOwner.Resolve(configuration)) { }

    /// <summary>Explicit stores (tests point these at a temp directory; never at the real Vault).</summary>
    public AccountSecretVault(BrokerCredentialStore brokers, TokenStore tokens, IConfiguration? configuration, Func<Guid?> owner)
    {
        this.brokers = brokers;
        this.tokens = tokens;
        this.configuration = configuration;
        this.owner = owner;
    }

    /// <summary>The machine owner whose Alpaca pairs are the canonical <c>alpaca-paper|live</c> entries; null when unknown.</summary>
    public Guid? Owner => owner();

    /// <summary>Where the Brokers bucket lives on disk (for status output; never the values).</summary>
    public string BrokersFile => brokers.ProvidersFilePath;

    /// <summary>Where the Tokens bucket lives on disk.</summary>
    public string TokensFile => tokens.TokensFilePath;

    /// <summary>The Brokers entry name holding <paramref name="userId"/>'s paper or live pair.</summary>
    public static string AlpacaEntryName(Guid userId, bool isPaper, Guid? owner) =>
        owner == userId
            ? isPaper ? "alpaca-paper" : "alpaca-live"
            : $"idiotproof-{userId:N}-alpaca-{(isPaper ? "paper" : "live")}";

    /// <summary>The Tokens entry name holding one of <paramref name="userId"/>'s single-value secrets.</summary>
    public static string TokenEntryName(Guid userId, AccountToken token) => token switch
    {
        AccountToken.Claude => $"idiotproof-{userId:N}-claude",
        AccountToken.AlpacaOAuthAccess => $"idiotproof-{userId:N}-alpaca-oauth-access",
        AccountToken.AlpacaOAuthRefresh => $"idiotproof-{userId:N}-alpaca-oauth-refresh",
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };

    /// <summary>This user's paper or live entry name under the current owner.</summary>
    public string AlpacaEntry(Guid userId, bool isPaper) => AlpacaEntryName(userId, isPaper, Owner);

    /// <summary>The user's paper or live pair; both null when Vault has no complete pair.</summary>
    public (string? KeyId, string? Secret) GetAlpaca(Guid userId, bool isPaper)
    {
        var entry = AlpacaEntry(userId, isPaper);
        if (brokers.ProvidersFileExists())
        {
            var creds = brokers.GetBrokerCreds(entry);
            return creds is null ? (null, null) : (creds.ApiKey.Trim(), creds.Secret.Trim());
        }
        var section = configuration?.GetSection($"MindAttic:Vault:Brokers:{entry}");
        var keyId = section?["apiKey"];
        var secret = section?["secret"];
        return string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret) ? (null, null) : (keyId.Trim(), secret.Trim());
    }

    /// <summary>Writes the user's pair to the Brokers bucket (paper or live base URL recorded alongside).</summary>
    public void SetAlpaca(Guid userId, bool isPaper, string keyId, string secret)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("An Alpaca pair needs both a key id and a secret.");
        brokers.SetBrokerCreds(AlpacaEntry(userId, isPaper),
            new BrokerCredentialStore.BrokerCreds(keyId.Trim(), secret.Trim(),
                isPaper ? "https://paper-api.alpaca.markets" : "https://api.alpaca.markets"));
    }

    /// <summary>Removes the user's pair. True when an entry was there.</summary>
    public bool RemoveAlpaca(Guid userId, bool isPaper)
    {
        var entry = AlpacaEntry(userId, isPaper);
        var all = brokers.LoadAllRaw();
        if (!all.Remove(entry)) return false;
        brokers.SaveAllRaw(all);
        DeleteBackup(brokers.ProvidersFilePath);
        return true;
    }

    /// <summary>A single-value secret, or null.</summary>
    public string? GetToken(Guid userId, AccountToken token)
    {
        var name = TokenEntryName(userId, token);
        if (File.Exists(tokens.TokensFilePath)) return Blank(tokens.Get(name));
        return Blank(configuration?[$"MindAttic:Vault:Tokens:{name}"]);
    }

    /// <summary>Sets (or, for a blank value, removes) a single-value secret.</summary>
    public void SetToken(Guid userId, AccountToken token, string? value)
    {
        var name = TokenEntryName(userId, token);
        if (!string.IsNullOrWhiteSpace(value)) tokens.Set(name, value.Trim());
        else if (tokens.Remove(name)) DeleteBackup(tokens.TokensFilePath);
    }

    /// <summary>
    /// Vault's atomic write keeps the previous file as <c>.bak</c>; after a removal that copy still
    /// holds the removed secret, so it goes too.
    /// </summary>
    private static void DeleteBackup(string file)
    {
        try { File.Delete(file + ".bak"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>
/// Who owns this machine's canonical Vault Alpaca entries (<c>alpaca-paper|live</c>). The first
/// of: config <c>IdiotProof:VaultOwner</c>, env <c>IDIOTPROOF_OWNER</c>, config
/// <c>Hub:DefaultOwner</c>, the CLI's saved owner (<c>%APPDATA%\MindAttic\IdiotProof\cli.json</c>,
/// written by <c>idiotproof users use</c>). With none of those every user, the owner included,
/// gets per-user entries: nobody is ever handed the canonical pair by guesswork.
/// </summary>
public static class VaultOwner
{
    /// <summary>The CLI's saved-owner file.</summary>
    public static string CliConfigFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "IdiotProof", "cli.json");

    public static Guid? Resolve(IConfiguration? configuration, string? cliConfigFile = null)
    {
        foreach (var candidate in new[]
                 {
                     configuration?["IdiotProof:VaultOwner"],
                     Environment.GetEnvironmentVariable("IDIOTPROOF_OWNER"),
                     configuration?["Hub:DefaultOwner"],
                 })
            if (Guid.TryParse(candidate, out var id)) return id;
        return FromCliConfig(cliConfigFile ?? CliConfigFile);
    }

    private static Guid? FromCliConfig(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            return JsonNode.Parse(File.ReadAllText(file)) is JsonObject o
                   && Guid.TryParse(o["owner"]?.GetValue<string>(), out var id) ? id : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
