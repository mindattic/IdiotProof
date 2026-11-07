using System.Text.Json;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// Reads the <c>ExtraJson</c> column of a snapshot row: the registry's literature add-ons plus the
/// protocol features keyed by <see cref="GapperExtraKeys"/>, all numeric, so the study rows and the
/// live recorder's rows share one shape and a re-run of <c>snapshot</c> refreshes them in place.
/// </summary>
public sealed class GapperSnapshotExtras
{
    public const string TradesRoc = GapperExtraKeys.TradesRoc;
    public const string CatalystSentiment = GapperExtraKeys.CatalystSentiment;
    public const string Form4BuysD30 = GapperExtraKeys.Form4BuysD30;
    public const string Form4BuyDollarsD30 = GapperExtraKeys.Form4BuyDollarsD30;
    public const string EightKItemCount = GapperExtraKeys.EightKItemCount;

    private readonly Dictionary<string, double> values;

    private GapperSnapshotExtras(Dictionary<string, double> values) => this.values = values;

    public static readonly GapperSnapshotExtras Empty = new(new Dictionary<string, double>(StringComparer.Ordinal));

    public IReadOnlyCollection<string> Keys => values.Keys;

    public static string Write(GapperFormationSnapshot snap) => JsonSerializer.Serialize(snap.Extra);

    public static GapperSnapshotExtras Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Empty;
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var d) && double.IsFinite(d)) map[p.Name] = d;
            return new GapperSnapshotExtras(map);
        }
        catch (JsonException) { return Empty; }
    }

    public double? Number(string key) => values.TryGetValue(key, out var d) ? d : null;

    public bool Has(string key) => values.ContainsKey(key);

    /// <summary>The latest 8-K's item codes, comma-joined; null when no 8-K sat inside the lookback.</summary>
    public string? EightKItems() => GapperExtraKeys.EightKItems(values);
}
