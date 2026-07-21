using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Monitor;

/// <summary>
/// Alpaca-native gapper scanner (CLI: <c>scan</c>). Pulls the day's top movers
/// straight from the Market Data API (<c>/v1beta1/screener/stocks/movers</c>) —
/// no HTML scraping — filters to the gapper band (price + % change), then drives
/// <see cref="StrategyReplay"/> for each survivor with a gapper profile applied
/// on the fly. Each replay persists to SQL and renders into the archive, so a
/// single <c>scan</c> populates the whole board:
///   /idiotproof/replays/&lt;ticker&gt;/&lt;stamp&gt;/ for every morning gapper.
///
/// Discovery is real-time (movers is a live endpoint), so scan replays TODAY on
/// the free delayed-SIP feed. Options: --top N, --min-gap %, --min-price,
/// --max-price, --profile &lt;id&gt;, --feed, --user.
/// </summary>
public static class StrategyScanner
{
    private const string DataBaseUrl = "https://data.alpaca.markets/";

    public static async Task<int> RunAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("scan requires --user <guid> (or a single user).");

        var keys = sp.GetRequiredService<UserKeyService>();
        var k = await keys.GetOrCreateAsync(userId.Value);
        if (string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
            return Fail($"user {userId} has no Alpaca data keys — set them first (set-keys).");

        int top      = ParseInt(opt.GetValueOrDefault("top"), 10, 1, 25);
        int screen   = ParseInt(opt.GetValueOrDefault("screen"), 50, 1, 50); // movers to pull before filtering
        double minGap = ParseDbl(opt.GetValueOrDefault("min-gap"), 5);
        double minPx  = ParseDbl(opt.GetValueOrDefault("min-price"), 1);
        double maxPx  = ParseDbl(opt.GetValueOrDefault("max-price"), 50);
        var profile   = opt.GetValueOrDefault("profile", "classic-gapper");

        // ── Discover: top gainers from Alpaca's screener ──
        List<(string Symbol, double Percent, double Price)> gainers;
        try { gainers = await FetchMoversAsync(k.AlpacaApiKeyId!, k.AlpacaApiSecretKey!, screen); }
        catch (Exception ex) { return Fail($"movers screen failed: {ex.Message}"); }

        var picks = gainers
            .Where(g => g.Percent >= minGap && g.Price >= minPx && g.Price <= maxPx)
            .OrderByDescending(g => g.Percent)
            .Take(top)
            .ToList();

        Console.WriteLine($"scan: {gainers.Count} gainers screened → {picks.Count} in band " +
                          $"(gap≥{minGap}%, ${minPx}-{maxPx}), profile={profile}");
        if (picks.Count == 0)
            return Fail("no gappers matched the band — widen --min-gap / price range, or the market is quiet.");

        // Movers are LIVE (today), so replay TODAY's ET session unless the
        // caller pinned a specific --date. (Without this the replay would
        // default to the previous trading day and mismatch the gappers.)
        var todayEt = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketTime.Eastern)).ToString("yyyy-MM-dd");
        var dateEt = opt.GetValueOrDefault("date", todayEt);

        // ── Replay each survivor through the existing harness (SQL + pages) ──
        int ok = 0, fired = 0;
        foreach (var g in picks)
        {
            Console.WriteLine($"── {g.Symbol}  +{g.Percent:0.#}% @ ${g.Price:0.##} ──");
            var one = new Dictionary<string, string>(opt, StringComparer.OrdinalIgnoreCase)
            {
                ["symbol"] = g.Symbol,
                ["profile"] = profile,
                ["date"] = dateEt,
                ["user"] = userId.Value.ToString(),
            };
            try
            {
                var rc = await StrategyReplay.RunAsync(sp, one);
                if (rc == 0) ok++;
            }
            catch (Exception ex) { Console.Error.WriteLine($"   {g.Symbol} replay error: {ex.Message}"); }
        }

        // Count how many of this scan's tickers fired (from SQL).
        var dbf = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var syms = picks.Select(p => p.Symbol).ToList();
        await using (var db = await dbf.CreateDbContextAsync())
            fired = await db.ReplayRuns.CountAsync(r => syms.Contains(r.Symbol) && r.Fired);

        Console.WriteLine($"\nscan done — {ok}/{picks.Count} replayed. Publish: " +
                          "cd MindAttic.Deploy && npm run deploy -- --site idiotproof-replays");
        return 0;
    }

    /// <summary>
    /// Top gainers from Alpaca's live movers screener. Shared with
    /// <see cref="AutoGapperScanner"/> so both the CLI scan and the automated
    /// 3:55 AM job discover gappers through the exact same endpoint.
    /// </summary>
    internal static async Task<List<(string Symbol, double Percent, double Price)>> FetchMoversAsync(string key, string secret, int top)
    {
        using var http = new HttpClient { BaseAddress = new Uri(DataBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", key);
        http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", secret);
        using var resp = await http.GetAsync($"v1beta1/screener/stocks/movers?top={top}");
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var list = new List<(string, double, double)>();
        if (doc.RootElement.TryGetProperty("gainers", out var g) && g.ValueKind == JsonValueKind.Array)
            foreach (var el in g.EnumerateArray())
            {
                var sym = el.TryGetProperty("symbol", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(sym)) continue;
                var pct = el.TryGetProperty("percent_change", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;
                var px  = el.TryGetProperty("price", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetDouble() : 0;
                list.Add((sym!.ToUpperInvariant(), pct, px));
            }
        return list;
    }

    // ── helpers ──
    private static async Task<Guid?> ResolveUserAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        if (opt.TryGetValue("user", out var u) && Guid.TryParse(u, out var gid)) return gid;
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var ids = db.AuthUsers.Select(x => x.Id).Take(2).ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    private static int ParseInt(string? s, int def, int min, int max) =>
        int.TryParse(s, out var v) ? Math.Clamp(v, min, max) : def;
    private static double ParseDbl(string? s, double def) =>
        double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";
    private static int Fail(string s) { Console.Error.WriteLine("ERROR: " + s); return 1; }
}
