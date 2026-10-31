using System.Globalization;
using System.Text.Json;
using IdiotProof.Brokers;

namespace IdiotProof.Operations.Crypto;

/// <summary>
/// Shared plumbing for the crypto operations: pair normalization (<c>btc-usd</c> → <c>BTC/USD</c>),
/// the v1beta3 US crypto Market Data paths, multi-symbol paging, and Alpaca's RFC 3339
/// timestamps (which carry nanoseconds .NET cannot parse directly).
/// </summary>
public static class CryptoData
{
    /// <summary>The Market Data root every crypto read hangs off.</summary>
    public const string Root = "/v1beta3/crypto/us";

    private static readonly string[] Quotes = ["USDT", "USDC", "USD", "BTC"];

    /// <summary>
    /// True when <paramref name="symbol"/> names a crypto pair: it carries a slash or dash
    /// (<c>BTC/USD</c>, <c>eth-usd</c>). A bare <c>BTCUSD</c> is ambiguous and stays a stock.
    /// </summary>
    public static bool IsPair(string? symbol) =>
        !string.IsNullOrWhiteSpace(symbol) && (symbol.Contains('/') || symbol.Contains('-'));

    /// <summary>
    /// <c>BTC/USD</c> from <c>btc/usd</c>, <c>BTC-USD</c> or <c>BTCUSD</c>; null when it is not a pair.
    /// </summary>
    public static string? Normalize(string? symbol)
    {
        var s = (symbol ?? "").Trim().ToUpperInvariant().Replace('-', '/');
        if (s.Length == 0) return null;
        if (s.Contains('/'))
        {
            var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 && parts.All(p => p.All(char.IsLetterOrDigit)) ? $"{parts[0]}/{parts[1]}" : null;
        }
        foreach (var quote in Quotes)
            if (s.Length > quote.Length && s.EndsWith(quote, StringComparison.Ordinal) && s.All(char.IsLetterOrDigit))
                return $"{s[..^quote.Length]}/{quote}";
        return null;
    }

    /// <summary>The normalized pair or a usage error naming the expected form.</summary>
    public static string RequirePair(string? symbol, string example) =>
        Normalize(symbol) ?? throw OperationException.Usage($"'{symbol}' is not a crypto pair; use BASE/QUOTE, e.g. `{example}`.");

    /// <summary>
    /// Follows <c>next_page_token</c> on a multi-symbol crypto endpoint (<c>/bars</c>, <c>/trades</c>,
    /// <c>/quotes</c>) and returns the <paramref name="items"/> array for <paramref name="pair"/>, oldest first.
    /// </summary>
    public static async Task<List<JsonElement>> PagedAsync(AlpacaApi api, string items, string pair,
        Dictionary<string, string?> query, int cap, CancellationToken ct)
    {
        var q = new Dictionary<string, string?>(query) { ["symbols"] = pair };
        var all = new List<JsonElement>();
        for (int page = 0; page < 50 && all.Count < cap; page++)
        {
            var root = await api.GetDataAsync($"{Root}/{items}", q, ct);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(items, out var bySymbol) &&
                bySymbol.ValueKind == JsonValueKind.Object && bySymbol.TryGetProperty(pair, out var list) && list.ValueKind == JsonValueKind.Array)
                all.AddRange(list.EnumerateArray());
            if (!root.TryGetProperty("next_page_token", out var token) || token.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(token.GetString()))
                break;
            q["page_token"] = token.GetString();
        }
        return all;
    }

    /// <summary>
    /// The object for <paramref name="pair"/> under <paramref name="property"/> on a latest/snapshot
    /// endpoint, e.g. <c>{"quotes":{"BTC/USD":{...}}}</c>; null when Alpaca has nothing for it.
    /// </summary>
    public static JsonElement? ForPair(JsonElement root, string property, string pair) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var bySymbol) &&
        bySymbol.ValueKind == JsonValueKind.Object && bySymbol.TryGetProperty(pair, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    /// <summary>An RFC 3339 timestamp as UTC; fractions beyond 100 ns (Alpaca sends nanoseconds) are dropped.</summary>
    public static DateTime Time(JsonElement e, string property = "t")
    {
        if (!e.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.String) return default;
        var s = v.GetString()!;
        var dot = s.IndexOf('.');
        if (dot >= 0)
        {
            var end = dot + 1;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            if (end - dot - 1 > 7) s = s[..(dot + 8)] + s[end..];
        }
        return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime;
    }

    /// <summary>A numeric property, or null.</summary>
    public static decimal? Num(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetDecimal(out var d) ? d : (decimal)v.GetDouble(),
                JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                _ => null,
            }
            : null;

    /// <summary>A string property, or null.</summary>
    public static string? Str(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    /// <summary>A UTC instant as <c>10/30 9:31:07 AM ET</c> (seconds matter for ticks).</summary>
    public static string EtSeconds(DateTime utc) =>
        EasternClock.ToEastern(utc).ToString("M/d h:mm:ss tt", CultureInfo.InvariantCulture) + " ET";

    /// <summary>A crypto price: whole cents above $1, else enough digits to see the move.</summary>
    public static string Price(decimal? v) => v is not { } p ? "-"
        : p >= 1000 ? p.ToString("#,0.00", CultureInfo.InvariantCulture)
        : p >= 1 ? p.ToString("0.00##", CultureInfo.InvariantCulture)
        : p.ToString("0.00######", CultureInfo.InvariantCulture);

    /// <summary>A fractional crypto size (up to 9 decimals, trailing zeros dropped).</summary>
    public static string Size(decimal? v) => v is { } s ? s.ToString("#,0.#########", CultureInfo.InvariantCulture) : "-";

    /// <summary>RFC 3339 for a query parameter.</summary>
    public static string Rfc3339(DateTime utc) => EasternClock.AsUtc(utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
