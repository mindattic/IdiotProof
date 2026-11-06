using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// CIK → ticker from the SEC's <c>company_tickers.json</c>, refreshed daily and held in memory
/// (an issuer with several share classes maps to the first ticker listed). Through the "edgar"
/// named client and the <see cref="EdgarRateGate"/> like every other SEC call.
/// </summary>
public sealed class EdgarTickerMap(IHttpClientFactory httpFactory, EdgarRateGate gate, TimeProvider time, ILogger<EdgarTickerMap> logger)
{
    public const string Url = "https://www.sec.gov/files/company_tickers.json";

    public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly SemaphoreSlim refresh = new(1, 1);
    private Dictionary<string, string> byCik = new();
    private DateTime loadedUtc;

    public int Count => byCik.Count;

    public DateTime? LoadedUtc => loadedUtc == default ? null : loadedUtc;

    public async Task<string?> TickerForCikAsync(string cik, CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct);
        return byCik.TryGetValue(Normalize(cik), out var t) ? t : null;
    }

    public async Task EnsureFreshAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (loadedUtc != default && now - loadedUtc < Ttl) return;
        await refresh.WaitAsync(ct);
        try
        {
            now = time.GetUtcNow().UtcDateTime;
            if (loadedUtc != default && now - loadedUtc < Ttl) return;
            var json = await gate.RunAsync(async c =>
            {
                var http = httpFactory.CreateClient("edgar");
                using var resp = await http.GetAsync(Url, c);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync(c);
            }, ct);
            var map = Parse(json);
            if (map.Count > 0)
            {
                byCik = map;
                loadedUtc = now;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "EDGAR company_tickers.json refresh failed; keeping {Count} cached entries.", byCik.Count);
            if (byCik.Count > 0) loadedUtc = now;
        }
        finally { refresh.Release(); }
    }

    /// <summary>The file is <c>{"0":{"cik_str":320193,"ticker":"AAPL","title":"Apple Inc."},...}</c>.</summary>
    public static Dictionary<string, string> Parse(string json)
    {
        var map = new Dictionary<string, string>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            var e = entry.Value;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("cik_str", out var cikEl) || !e.TryGetProperty("ticker", out var tEl)) continue;
            var cik = cikEl.ValueKind == JsonValueKind.Number ? cikEl.GetRawText() : cikEl.GetString() ?? "";
            var ticker = tEl.GetString()?.Trim().ToUpperInvariant() ?? "";
            if (cik.Length == 0 || ticker.Length == 0) continue;
            map.TryAdd(Normalize(cik), ticker);
        }
        return map;
    }

    public static string Normalize(string cik) => cik.Trim().TrimStart('0') is { Length: > 0 } n ? n : "0";
}
