using System.Text.Json;

namespace IdiotProof.Core.Services;

/// <summary>
/// The set of symbols Alpaca will trade right now: active <c>us_equity</c> assets with <c>tradable: true</c>, from one
/// <c>/v2/assets</c> call, cached process-wide for <see cref="MaxAge"/>. Gapper candidate paths (formation universe,
/// auto-gapper) use it to drop names Alpaca refuses even though they still print on Nasdaq; on 2026-11-10 that was
/// 29 listed symbols such as OMH, HOLO, MLGO and NXTT (IP-T494, IP-T495). Fails open: when the fetch fails the caller
/// gets null and keeps its list, because Alpaca rejects an order for a blocked name anyway.
/// </summary>
public static class AlpacaTradableAssets
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    private const string PaperTradingBase = "https://paper-api.alpaca.markets/";

    private static readonly SemaphoreSlim gate = new(1, 1);
    private static DateTime cachedUtc;
    private static HashSet<string>? cached;

    /// <summary>A test seam for the fetch: (keyId, secret) → the assets JSON array body.</summary>
    internal static Func<string, string, CancellationToken, Task<string>> FetchBody { get; set; } = FetchFromAlpacaAsync;

    /// <summary>The tradable set, or null when it cannot be fetched (the caller then filters nothing).</summary>
    public static async Task<HashSet<string>?> GetAsync(string keyId, string secret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret)) return null;
        await gate.WaitAsync(ct);
        try
        {
            if (cached is not null && DateTime.UtcNow - cachedUtc < MaxAge) return cached;
            var body = await FetchBody(keyId, secret, ct);
            using var doc = JsonDocument.Parse(body);
            var set = Parse(doc.RootElement);
            if (set.Count == 0) return cached;
            cached = set;
            cachedUtc = DateTime.UtcNow;
            return cached;
        }
        catch (OperationCanceledException) { throw; }
        catch { return cached; }
        finally { gate.Release(); }
    }

    /// <summary>Active, tradable symbols from an <c>/v2/assets</c> array, upper-cased.</summary>
    public static HashSet<string> Parse(JsonElement root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Array) return set;
        foreach (var a in root.EnumerateArray())
        {
            if (!a.TryGetProperty("tradable", out var t) || t.ValueKind != JsonValueKind.True) continue;
            if (a.TryGetProperty("status", out var s) && s.GetString() != "active") continue;
            var sym = a.TryGetProperty("symbol", out var y) ? y.GetString() : null;
            if (!string.IsNullOrWhiteSpace(sym)) set.Add(sym.Trim().ToUpperInvariant());
        }
        return set;
    }

    /// <summary>Drops cached state (tests).</summary>
    internal static void Reset() { cached = null; cachedUtc = default; }

    private static async Task<string> FetchFromAlpacaAsync(string keyId, string secret, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(PaperTradingBase), Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", keyId);
        http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", secret);
        using var resp = await http.GetAsync("v2/assets?asset_class=us_equity&status=active", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
