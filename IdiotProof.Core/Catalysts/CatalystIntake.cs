using System.Globalization;
using System.Text.Json;

namespace IdiotProof.Core.Catalysts;

/// <summary>One item on its way into <see cref="Data.CatalystEvent"/>: what the source said and when this process saw it.</summary>
public sealed record CatalystIntake(
    string Source,
    string ExternalId,
    DateTime PublishedUtc,
    DateTime ReceivedUtc,
    IReadOnlyList<string> Symbols,
    string Headline,
    string? Summary,
    string? Url,
    string? SourceName,
    string? FormType = null,
    IReadOnlyList<string>? Items = null,
    bool Backfilled = false)
{
    public const string AlpacaNews = "alpaca-news";
    public const string Edgar = "edgar";
}

/// <summary>Parses the news stream's <c>{"T":"n",...}</c> messages and the REST <c>news[]</c> items (same shape).</summary>
public static class CatalystNewsMessage
{
    public static bool IsNews(JsonElement e) => e.ValueKind == JsonValueKind.Object && Str(e, "T") == "n";

    public static CatalystIntake? Parse(JsonElement e, DateTime receivedUtc, bool backfilled = false)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var id = e.TryGetProperty("id", out var idEl)
            ? idEl.ValueKind == JsonValueKind.Number ? idEl.GetRawText() : idEl.GetString()
            : null;
        var headline = Str(e, "headline");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(headline)) return null;

        var symbols = e.TryGetProperty("symbols", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim().ToUpperInvariant())
                .Where(x => x.Length > 0).Distinct().ToList()
            : [];
        var published = Time(e, "created_at") ?? Time(e, "updated_at") ?? receivedUtc;
        return new CatalystIntake(
            CatalystIntake.AlpacaNews, id, published, backfilled ? published : receivedUtc, symbols, headline.Trim(),
            Str(e, "summary"), Str(e, "url"), Str(e, "source") ?? Str(e, "author"), Backfilled: backfilled);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? Time(JsonElement e, string name) =>
        Str(e, name) is { } t && DateTime.TryParse(t, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
}
