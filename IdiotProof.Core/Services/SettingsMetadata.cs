using System.Globalization;

using IdiotProof.Core.Data;
namespace IdiotProof.Core.Services;

/// <summary>The value type a <see cref="SettingsKv"/> key holds.</summary>
public enum SettingKind
{
    Int,
    Decimal,
    Bool,
    Text,
    Json,
    Timestamp,
}

/// <summary>
/// One known <c>SettingsKv</c> key: what it means, its type, range, default, and whether a person
/// may change it (internal keys are bookkeeping the app writes for itself). A key ending in
/// <c>.*</c> is a family (<c>decay.be-bex.disarmed.&lt;strategy id&gt;</c>).
/// </summary>
public sealed record SettingDefinition(
    string Key, SettingKind Kind, string Description, string? Default = null,
    decimal? Min = null, decimal? Max = null, bool Internal = false)
{
    public bool Matches(string key) =>
        Key.EndsWith(".*", StringComparison.Ordinal)
            ? key.StartsWith(Key[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(Key, key, StringComparison.OrdinalIgnoreCase);

    /// <summary>The value normalized for storage, or the reason it is not acceptable.</summary>
    public (string? Value, string? Error) Validate(string raw)
    {
        var v = raw.Trim();
        switch (Kind)
        {
            case SettingKind.Int:
                if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return (null, $"{Key} takes a whole number.");
                return RangeError(i) is { } ie ? (null, ie) : (i.ToString(CultureInfo.InvariantCulture), null);
            case SettingKind.Decimal:
                if (!decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return (null, $"{Key} takes a number (invariant culture, e.g. 0.045).");
                return RangeError(d) is { } de ? (null, de) : (d.ToString(CultureInfo.InvariantCulture), null);
            case SettingKind.Bool:
                return v.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" or "on" => ("true", null),
                    "false" or "0" or "no" or "off" => ("false", null),
                    _ => (null, $"{Key} takes true or false."),
                };
            default:
                return (v, null);
        }
    }

    private string? RangeError(decimal value) =>
        (Min is { } min && value < min) || (Max is { } max && value > max)
            ? $"{Key} must be between {Min?.ToString(CultureInfo.InvariantCulture) ?? "-inf"} and {Max?.ToString(CultureInfo.InvariantCulture) ?? "inf"}."
            : null;
}

/// <summary>
/// The registry of runtime settings stored in <c>SettingsKv</c> (IP-T250): every key the code
/// reads, typed and ranged, so <c>idiotproof settings set</c> can refuse a typo or a nonsense
/// value instead of writing something the reader silently ignores. Add a row here when a new
/// <see cref="SettingsRepository"/> key is introduced.
/// </summary>
public static class SettingsMetadata
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(OptionsTradingService.RiskFreeRateKey, SettingKind.Decimal,
            "Annual risk-free rate for options greeks and implied volatility (0.04 = 4%).",
            OptionsTradingService.DefaultRiskFreeRate.ToString(CultureInfo.InvariantCulture), Min: 0m, Max: 0.25m),
        new("decay.be-bex.lookbackdays", SettingKind.Int,
            "BE/BEX decay scanner: trading days of history compared.", "20", Min: 1m, Max: 250m),
        new("decay.be-bex.threshold", SettingKind.Decimal,
            "BE/BEX decay scanner: decay percent at which the BE long is disarmed.", "8", Min: 0.1m, Max: 100m),
        new("decay.be-bex.latest", SettingKind.Json, "BE/BEX decay scanner: its latest reading (written by the scanner).", Internal: true),
        new("decay.be-bex.disarmed.*", SettingKind.Bool, "BE/BEX decay scanner: it disarmed this strategy itself (written by the scanner).", Internal: true),
        new("ui.logAlertLastSeen", SettingKind.Timestamp, "When the log-alert badge was last cleared (written by the web app).", Internal: true),
    ];

    /// <summary>The definition covering <paramref name="key"/>, or null for an unknown key.</summary>
    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => d.Matches(key.Trim()));
}
