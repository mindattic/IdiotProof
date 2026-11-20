using MindAttic.Vault.Credentials;

namespace IdiotProof.Core.Fundamentals;

/// <summary>
/// One vendor's float / short-interest / shares-outstanding read for a symbol (IP-T603). Every
/// value is nullable: a vendor (or the plan the key is on) may not carry a field. Percentages are
/// in percent units (12.5 = 12.5%). Each value carries its own as-of date when the vendor gives one;
/// a null as-of on a non-null value means "current as of <see cref="FetchedUtc"/>", never point-in-time.
/// </summary>
public sealed record FundamentalsSnapshot
{
    public required string Symbol { get; init; }

    /// <summary>The source's name (<c>fmp</c>, <c>polygon</c>, <c>finviz-elite</c>).</summary>
    public required string Source { get; init; }

    public required DateTime FetchedUtc { get; init; }

    public long? SharesOutstanding { get; init; }
    public DateOnly? SharesOutstandingAsOf { get; init; }

    /// <summary>Freely tradable shares (vendor's own float definition).</summary>
    public long? FloatShares { get; init; }
    public DateOnly? FloatAsOf { get; init; }

    /// <summary>Float as a percent of shares outstanding.</summary>
    public double? FloatPercentOfOutstanding { get; init; }

    /// <summary>Shares sold short (FINRA bi-monthly report for US listings).</summary>
    public long? ShortInterest { get; init; }

    /// <summary>The short-interest settlement date (or the vendor's as-of).</summary>
    public DateOnly? ShortInterestAsOf { get; init; }

    /// <summary>Short interest as a percent of float.</summary>
    public double? ShortPercentOfFloat { get; init; }

    /// <summary>Days to cover (short interest over average daily volume; Finviz calls it Short Ratio).</summary>
    public double? DaysToCover { get; init; }

    /// <summary>What the source could not supply and why (a sub-endpoint the plan lacks, a unit assumption).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>True when the vendor knew the symbol but returned none of the values.</summary>
    public bool IsEmpty => SharesOutstanding is null && FloatShares is null && ShortInterest is null
                           && ShortPercentOfFloat is null && DaysToCover is null;
}

/// <summary>
/// The seam every float / short-interest vendor sits behind (IP-T603). A source is off until its
/// key is in MindAttic.Vault; it then fetches one symbol per call and never caches (the
/// <see cref="FundamentalsService"/> does, once per ET day).
/// </summary>
public interface IFundamentalsSource
{
    /// <summary>Lower-case id used on the CLI (<c>--source</c>) and as the cache folder.</summary>
    string Name { get; }

    /// <summary>Human name of the vendor.</summary>
    string DisplayName { get; }

    /// <summary>The Vault Tokens entry holding the key.</summary>
    string VaultEntry { get; }

    /// <summary>What the source can return, for status output.</summary>
    string Provides { get; }

    /// <summary>True when the key resolves.</summary>
    bool IsConfigured { get; }

    /// <summary>"configured" or "no key in Vault: &lt;entry&gt;".</summary>
    string Status { get; }

    /// <summary>
    /// The vendor's current values for <paramref name="symbol"/>; null when the vendor does not know
    /// the symbol. Throws <see cref="FundamentalsSourceException"/> on a missing key or a failed call
    /// (the message never contains the key or a keyed URL).
    /// </summary>
    Task<FundamentalsSnapshot?> FetchAsync(string symbol, CancellationToken ct);
}

/// <summary>A source failure whose message is safe to show (no key, no keyed URL).</summary>
public sealed class FundamentalsSourceException(string message) : Exception(message);

/// <summary>
/// Resolves a vendor key from MindAttic.Vault: the per-user Tokens bucket
/// (<c>%APPDATA%\MindAttic\Tokens\tokens.json</c>) first, then configuration
/// <c>MindAttic:Vault:Tokens:&lt;entry&gt;</c> (a host with no bucket file: Azure env vars / Key Vault),
/// the same two places <see cref="Services.AccountSecretVault"/> reads. Values are never logged or printed.
/// </summary>
public sealed class FundamentalsKeys(TokenStore? tokens, IConfiguration? configuration)
{
    /// <summary>The production resolver: the real Tokens bucket plus configuration.</summary>
    public static FundamentalsKeys Default(IConfiguration? configuration) => new(SafeTokens(), configuration);

    /// <summary>Where the Tokens bucket lives (for status output), or null when it could not be opened.</summary>
    public string? TokensFile => tokens?.TokensFilePath;

    public string? Get(string entry)
    {
        string? value = null;
        try
        {
            if (tokens is not null && File.Exists(tokens.TokensFilePath)) value = tokens.Get(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            value = null;
        }
        if (string.IsNullOrWhiteSpace(value)) value = configuration?[$"MindAttic:Vault:Tokens:{entry}"];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static TokenStore? SafeTokens()
    {
        try { return TokenStore.ForBucket("Tokens"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }
}
