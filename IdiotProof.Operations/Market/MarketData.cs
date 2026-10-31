using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Market;

/// <summary>
/// Shared plumbing for the <c>market ...</c> operations that read Alpaca's Market Data API: the
/// client (owner's keys, else the Hub's), the stock feed tier, symbol lists, ET date ranges,
/// JSON field readers and the number/time formats every market table uses.
/// </summary>
internal static partial class MarketData
{
    /// <summary>Env knob for the stock data tier (mirrors TradingServiceRegistration.AlpacaFeedEnvVar).</summary>
    public const string FeedEnvVar = "IDIOTPROOF_ALPACA_FEED";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The Market Data client for this call: the owner's paper keys when usable, else the Hub's.</summary>
    public static Task<AlpacaApi> OpenAsync(OperationContext ctx) =>
        ctx.Get<AlpacaApiFactory>().ForDataAsync(ctx.OwnerId, ctx.CancellationToken);

    /// <summary><c>sip</c> (default, Algo Trader Plus) or whatever IDIOTPROOF_ALPACA_FEED says (e.g. <c>iex</c>).</summary>
    public static string Feed =>
        Environment.GetEnvironmentVariable(FeedEnvVar) is { Length: > 0 } v ? v.Trim().ToLowerInvariant() : "sip";

    /// <summary>Upper-cased, de-duplicated symbols; commas inside one token split too.</summary>
    public static List<string> Symbols(IEnumerable<string>? raw) =>
        [.. (raw ?? []).SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => s.ToUpperInvariant()).Where(s => s.Length > 0).Distinct()];

    /// <summary>Query dictionary helper: <c>Q(("symbols", "AAPL"), ("feed", "sip"))</c>.</summary>
    public static Dictionary<string, string?> Q(params (string Key, string? Value)[] pairs)
    {
        var q = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs) q[k] = v;
        return q;
    }

    // ── time ──

    /// <summary>The UTC instant an ET calendar date starts (midnight ET).</summary>
    public static DateTime EtDayStartUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), MarketTime.Eastern);

    /// <summary>RFC-3339 UTC with a Z, the form Alpaca's <c>start</c>/<c>end</c> accept.</summary>
    public static string Rfc3339(DateTime utc) => EasternClock.AsUtc(utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv);

    /// <summary>
    /// <c>--from</c>/<c>--to</c> as an ET-day range in UTC: from = midnight ET of <paramref name="from"/>,
    /// to = midnight ET after <paramref name="to"/>, capped at now. Null when the range is inverted.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc)? Range(DateOnly? from, DateOnly? to, DateTime nowUtc, TimeSpan defaultLookback)
    {
        var end = to is { } t ? EtDayStartUtc(t.AddDays(1)) : nowUtc;
        if (end > nowUtc) end = nowUtc;
        var start = from is { } f ? EtDayStartUtc(f) : end - defaultLookback;
        return start >= end ? null : (start, end);
    }

    /// <summary>
    /// Parses Alpaca's RFC-3339 timestamps (nanosecond fractions included) to UTC. Null when absent
    /// or unreadable.
    /// </summary>
    public static DateTime? Time(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? ParseTime(v.GetString()) : null;

    public static DateTime? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = LongFraction().Replace(text, "$1");
        return DateTimeOffset.TryParse(trimmed, Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto.UtcDateTime : null;
    }

    /// <summary>The ET instant with its offset, for JSON payloads.</summary>
    public static DateTimeOffset? EtOffset(DateTime? utc) => utc is { } u ? EasternClock.ToEasternOffset(u) : null;

    /// <summary><c>2026-10-30 9:31:05.123 AM ET</c>: tick precision for trades and quotes.</summary>
    public static string TickTime(DateTime? utc) =>
        utc is { } u ? EasternClock.ToEastern(u).ToString("yyyy-MM-dd h:mm:ss.fff tt", Inv) + " ET" : "-";

    /// <summary><c>2026-10-30 9:31 AM ET</c>, or "-".</summary>
    public static string Et(DateTime? utc) => utc is { } u ? EasternClock.Format(u) : "-";

    // ── JSON readers ──

    public static decimal? Dec(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)
            ? d : null;

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    /// <summary>The string items of an array property (trade/quote condition codes), else empty.</summary>
    public static string[] Strings(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
            : [];

    /// <summary>The property bag of an object keyed by symbol (<c>{"trades":{"AAPL":{...}}}</c>).</summary>
    public static Dictionary<string, JsonElement> BySymbol(JsonElement root, string? property)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var bag = property is null ? root : Obj(root, property);
        if (bag.ValueKind == JsonValueKind.Object)
            foreach (var p in bag.EnumerateObject()) map[p.Name] = p.Value;
        return map;
    }

    // ── formats ──

    /// <summary>A price: <c>$1,234.56</c>, four decimals under a dollar; "-" when absent.</summary>
    public static string Px(decimal? v) => v is { } p ? Fmt.Money(p, Math.Abs(p) < 1 && p != 0 ? 4 : 2) : "-";

    /// <summary>A forex rate, five significant decimals: <c>151.23400</c>.</summary>
    public static string Rate(decimal? v) => v is { } r ? r.ToString(r >= 10 ? "0.000" : "0.00000", Inv) : "-";

    public static string Signed(decimal? v) => v is { } m ? Fmt.SignedMoney(m, Math.Abs(m) < 1 && m != 0 ? 4 : 2) : "-";

    public static string Pct(decimal? part, decimal? whole) =>
        part is { } p && whole is { } w && w != 0 ? Fmt.Percent(p / w, 2, signed: true) : "-";

    public static string Size(decimal? v) => v is { } s ? Fmt.Number(s) : "-";

    /// <summary><c>1.25M</c>, <c>830.4K</c>, <c>2.1B</c>.</summary>
    public static string Compact(decimal? value)
    {
        if (value is not { } v) return "-";
        var a = Math.Abs(v);
        return a switch
        {
            >= 1_000_000_000m => (v / 1_000_000_000m).ToString("0.##", Inv) + "B",
            >= 1_000_000m => (v / 1_000_000m).ToString("0.##", Inv) + "M",
            >= 1_000m => (v / 1_000m).ToString("0.#", Inv) + "K",
            _ => v.ToString("0.##", Inv),
        };
    }

    [GeneratedRegex(@"(\.\d{7})\d+")]
    private static partial Regex LongFraction();
}
