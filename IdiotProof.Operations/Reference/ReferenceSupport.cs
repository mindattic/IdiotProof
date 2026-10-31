using System.Globalization;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;

namespace IdiotProof.Operations.Reference;

/// <summary>Which Alpaca Trading API account a reference read or watchlist call goes to.</summary>
public enum AlpacaAccount
{
    Paper,
    Live,
}

/// <summary>Shared plumbing for the reference-data operations: clients and tolerant JSON reads.</summary>
internal static class ReferenceSupport
{
    /// <summary>The owner's Trading API client for <paramref name="account"/>; caller disposes.</summary>
    public static Task<AlpacaApi> TradingAsync(OperationContext ctx, AlpacaAccount account) =>
        ctx.Get<AlpacaApiFactory>().ForOwnerAsync(ctx.RequireOwner(),
            account == AlpacaAccount.Live ? BrokerMode.Live : BrokerMode.Paper, ctx.CancellationToken);

    /// <summary>A Market Data API client (owner's keys, else the Hub's); caller disposes.</summary>
    public static Task<AlpacaApi> DataAsync(OperationContext ctx) =>
        ctx.Get<AlpacaApiFactory>().ForDataAsync(ctx.OwnerId, ctx.CancellationToken);

    public static string? Str(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
            _ => null,
        };
    }

    public static bool? Bool(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
            _ => null,
        };
    }

    public static decimal? Dec(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    /// <summary>An ISO instant from Alpaca as <c>2026-10-30 9:31 AM ET</c>; "-" when absent.</summary>
    public static string Instant(this JsonElement e, string name)
    {
        var text = e.Str(name);
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? Rendering.Fmt.Et(t)
            : "-";
    }

    public static IEnumerable<string> Strings(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
            : [];

    public static IEnumerable<JsonElement> Items(this JsonElement e, string? property = null)
    {
        var arr = property is null ? e : e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var p) ? p : default;
        return arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray() : [];
    }

    public static string YesNo(bool? value) => value switch { true => "yes", false => "no", _ => "-" };

    public static string Csv(IEnumerable<string>? values)
    {
        var list = (values ?? []).SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        return list.Count == 0 ? "" : string.Join(",", list);
    }

    /// <summary>"Showing 50 of 11,234 matching (12,001 returned by Alpaca)."</summary>
    public static string Totals(string noun, int shown, int matched, int total) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Showing {shown:N0} of {matched:N0} matching {noun} ({total:N0} returned by Alpaca){(shown < matched ? "; narrow with --search or raise --limit" : "")}.");
}
