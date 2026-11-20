using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// First-seen bookkeeping for <see cref="SocialSignal"/> rows (IP-T602): the first reading of a source/channel/symbol in
/// the ET day, carried on every later reading that day, recovered from SQL after a restart.
/// </summary>
public sealed class SocialSignalWriter(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, DateTime> firstSeen = new(StringComparer.OrdinalIgnoreCase);
    private DateTime day;

    public async Task<int> WriteAsync(IReadOnlyList<SocialSignal> rows, DateTime nowUtc, CancellationToken ct)
    {
        if (rows.Count == 0) return 0;
        var dayStart = EasternTime.ToUtc(EasternTime.FromUtc(nowUtc).Date);
        lock (gate)
        {
            if (day != dayStart) { firstSeen.Clear(); day = dayStart; }
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var row in rows)
        {
            var key = $"{row.Source}|{row.Channel}|{row.Symbol}";
            DateTime? known;
            lock (gate) known = firstSeen.TryGetValue(key, out var k) ? k : null;
            if (known is null)
            {
                var source = row.Source;
                var channel = row.Channel;
                var symbol = row.Symbol;
                known = await db.SocialSignals.Where(s => s.Source == source && s.Channel == channel && s.Symbol == symbol && s.ObservedUtc >= dayStart)
                    .Select(s => (DateTime?)s.FirstSeenUtc).MinAsync(ct) ?? row.ObservedUtc;
                lock (gate) firstSeen[key] = known.Value;
            }
            row.FirstSeenUtc = known.Value;
            db.SocialSignals.Add(row);
        }
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}

/// <summary>
/// StockTwits (IP-T602), public API with no key: the trending-symbols list (rank and trending score) and, for up to
/// <see cref="SymbolsPerPass"/> symbols a pass (trending ones and today's catalyst symbols, least recently read first),
/// how many messages the symbol's stream holds from the last <see cref="WindowMinutes"/> minutes. The stream returns at
/// most 30 messages, so a count of 30 inside the window is marked capped.
/// </summary>
public sealed class StockTwitsPoller(
    IHttpClientFactory httpFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    SocialSignalWriter writer,
    TimeProvider time,
    ILogger<StockTwitsPoller> logger)
{
    public const string TrendingSource = "stocktwits-trending";
    public const string MessagesSource = "stocktwits-messages";
    public const int SymbolsPerPass = 15;
    public const int WindowMinutes = 60;
    public static readonly TimeSpan SymbolInterval = TimeSpan.FromMinutes(10);
    public const string StreamUrl = "https://api.stocktwits.com/api/2/streams/symbol/{0}.json";

    private readonly Lock gate = new();
    private readonly Dictionary<string, DateTime> lastRead = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SourcePollSummary> PollOnceAsync(CatalystSourceConfig source, CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient(WireFeedPoller.HttpClientName);
        var now = time.GetUtcNow().UtcDateTime;
        var trendingUrl = source.Urls.FirstOrDefault() ?? CatalystSources.Find(CatalystSources.StockTwits)!.DefaultUrls[0];
        using var trendingResp = await HttpRateLimitRetry.SendAsync(() => http.GetAsync(trendingUrl, ct), ct);
        if (!trendingResp.IsSuccessStatusCode) throw new HttpRequestException($"StockTwits trending answered {(int)trendingResp.StatusCode}.");
        var trending = ParseTrending(await trendingResp.Content.ReadAsStringAsync(ct));
        var rows = trending.Select(t => new SocialSignal
        {
            Source = TrendingSource, Symbol = t.Symbol, ObservedUtc = now, WindowMinutes = 5, Count = 1, Rank = t.Rank, Score = t.Score,
        }).ToList();

        // Per-symbol message volume: trending symbols and today's catalyst symbols, least recently read first.
        var dayStart = EasternTime.ToUtc(EasternTime.FromUtc(now).Date);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var catalystSymbols = await db.CatalystEvents.AsNoTracking().Where(e => e.ReceivedUtc >= dayStart && !e.Backfilled)
                .OrderByDescending(e => e.ReceivedUtc).Select(e => e.PrimarySymbol).Take(200).ToListAsync(ct);
            var candidates = trending.Select(t => t.Symbol).Concat(catalystSymbols).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> pick;
            lock (gate)
            {
                pick = candidates.Where(s => !lastRead.TryGetValue(s, out var at) || now - at >= SymbolInterval)
                    .OrderBy(s => lastRead.TryGetValue(s, out var at) ? at : DateTime.MinValue).Take(SymbolsPerPass).ToList();
            }
            var limited = false;
            foreach (var symbol in pick)
            {
                using var resp = await HttpRateLimitRetry.SendAsync(() => http.GetAsync(string.Format(CultureInfo.InvariantCulture, StreamUrl, Uri.EscapeDataString(symbol)), ct), ct);
                lock (gate) lastRead[symbol] = now;
                if ((int)resp.StatusCode == 429) { limited = true; break; }
                if (resp.StatusCode == HttpStatusCode.NotFound) continue;
                if (!resp.IsSuccessStatusCode) { logger.LogDebug("StockTwits stream {Symbol} answered {Status}.", symbol, (int)resp.StatusCode); continue; }
                var (count, capped) = CountMessages(await resp.Content.ReadAsStringAsync(ct), now, WindowMinutes);
                if (count > 0)
                    rows.Add(new SocialSignal { Source = MessagesSource, Symbol = symbol, ObservedUtc = now, WindowMinutes = WindowMinutes, Count = count, Capped = capped });
            }
            var written = await writer.WriteAsync(rows, now, ct);
            return new SourcePollSummary(trending.Count, 0, 0, 0, 0, written, 0, 0,
                $"{trending.Count} trending, {pick.Count} stream(s) read, {written} signal row(s)" + (limited ? "; rate-limited (429), rest next pass" : ""));
        }
    }

    public sealed record Trending(string Symbol, int? Rank, double? Score);

    /// <summary>US equities from <c>trending/symbols.json</c> (crypto ".X" symbols and non-US listings dropped).</summary>
    public static IReadOnlyList<Trending> ParseTrending(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("symbols", out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        var list = new List<Trending>();
        var i = 0;
        foreach (var s in arr.EnumerateArray())
        {
            i++;
            var sym = Str(s, "symbol")?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(sym) || sym.Contains('.') && sym.EndsWith(".X", StringComparison.Ordinal) || sym.Length > 16) continue;
            var region = Str(s, "region");
            if (region is not null && !string.Equals(region, "US", StringComparison.OrdinalIgnoreCase)) continue;
            var cls = Str(s, "instrument_class");
            if (cls is not null && cls.Contains("crypto", StringComparison.OrdinalIgnoreCase)) continue;
            int? rank = s.TryGetProperty("rank", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : i;
            double? score = s.TryGetProperty("trending_score", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : null;
            list.Add(new Trending(sym, rank, score));
        }
        return list;
    }

    /// <summary>Messages created in the last <paramref name="windowMinutes"/>; capped when every returned message is inside it.</summary>
    public static (int Count, bool Capped) CountMessages(string json, DateTime nowUtc, int windowMinutes)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("messages", out var arr) || arr.ValueKind != JsonValueKind.Array) return (0, false);
        var from = nowUtc.AddMinutes(-windowMinutes);
        int total = 0, inside = 0;
        foreach (var m in arr.EnumerateArray())
        {
            total++;
            if (Str(m, "created_at") is { } t && DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) && at >= from)
                inside++;
        }
        return (inside, total >= 30 && inside == total);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Reddit mention counts (IP-T602): the newest 100 posts of each configured subreddit through app-only OAuth (Reddit's
/// public JSON answers 403 to servers), counting posts from the last <see cref="WindowMinutes"/> minutes that name a
/// symbol as a $cashtag or as a bare 3-5 letter capitalized word that is a known SEC ticker and not a common word.
/// Credentials come from the Vault entries <c>reddit-client-id</c> / <c>reddit-client-secret</c>; never logged.
/// </summary>
public sealed partial class RedditPoller(
    IHttpClientFactory httpFactory,
    CatalystSourceKeys keys,
    EdgarTickerMap tickers,
    SocialSignalWriter writer,
    TimeProvider time,
    ILogger<RedditPoller> logger)
{
    public const string Source = "reddit";
    public const int WindowMinutes = 60;
    public const string TokenUrl = "https://www.reddit.com/api/v1/access_token";
    public const string ListingUrl = "https://oauth.reddit.com/r/{0}/new?limit=100&raw_json=1";

    private readonly Lock gate = new();
    private string? token;
    private DateTime tokenExpiresUtc;

    public async Task<SourcePollSummary> PollOnceAsync(IReadOnlyList<string> subreddits, CancellationToken ct = default)
    {
        var bearer = await TokenAsync(ct);
        var http = httpFactory.CreateClient(WireFeedPoller.HttpClientName);
        var now = time.GetUtcNow().UtcDateTime;
        var rows = new List<SocialSignal>();
        int posts = 0;
        var notes = new List<string>();
        foreach (var sub in subreddits)
        {
            using var resp = await HttpRateLimitRetry.SendAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, string.Format(CultureInfo.InvariantCulture, ListingUrl, Uri.EscapeDataString(sub)));
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                return http.SendAsync(req, ct);
            }, ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized) { lock (gate) token = null; notes.Add($"r/{sub}: 401 (token renewed next pass)"); continue; }
            if (!resp.IsSuccessStatusCode) { notes.Add($"r/{sub}: {(int)resp.StatusCode}"); continue; }
            var recent = ParseListing(await resp.Content.ReadAsStringAsync(ct), now.AddMinutes(-WindowMinutes));
            posts += recent.Count;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var text in recent)
                foreach (var symbol in await SymbolsAsync(text, ct))
                    counts[symbol] = counts.GetValueOrDefault(symbol) + 1;
            rows.AddRange(counts.Select(c => new SocialSignal
            {
                Source = Source, Channel = sub.ToLowerInvariant(), Symbol = c.Key, ObservedUtc = now, WindowMinutes = WindowMinutes, Count = c.Value, Capped = recent.Count >= 100,
            }));
        }
        var written = await writer.WriteAsync(rows, now, ct);
        return new SourcePollSummary(posts, 0, 0, 0, 0, written, 0, 0,
            $"{subreddits.Count} subreddit(s), {posts} post(s) in the last {WindowMinutes} min, {written} symbol mention row(s)" + (notes.Count > 0 ? "; " + string.Join("; ", notes) : ""));
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        lock (gate)
        {
            if (token is not null && time.GetUtcNow().UtcDateTime < tokenExpiresUtc) return token;
        }
        var id = keys.Get(CatalystSources.RedditClientIdEntry) ?? throw new InvalidOperationException($"no key in Vault: {CatalystSources.RedditClientIdEntry}");
        var secret = keys.Get(CatalystSources.RedditClientSecretEntry) ?? throw new InvalidOperationException($"no key in Vault: {CatalystSources.RedditClientSecretEntry}");
        var http = httpFactory.CreateClient(WireFeedPoller.HttpClientName);
        using var resp = await HttpRateLimitRetry.SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{id}:{secret}")));
            return http.SendAsync(req, ct);
        }, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Reddit token request answered {(int)resp.StatusCode} (check the Vault entries {CatalystSources.RedditClientIdEntry} / {CatalystSources.RedditClientSecretEntry}).");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var access = doc.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access)) throw new HttpRequestException("Reddit token response had no access_token.");
        var seconds = doc.RootElement.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 3600;
        lock (gate)
        {
            token = access;
            tokenExpiresUtc = time.GetUtcNow().UtcDateTime.AddSeconds(Math.Max(60, seconds - 120));
        }
        logger.LogDebug("Reddit app-only token renewed.");
        return access;
    }

    /// <summary>Title + body of each post created at or after <paramref name="fromUtc"/>.</summary>
    public static IReadOnlyList<string> ParseListing(string json, DateTime fromUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) return [];
        var list = new List<string>();
        foreach (var c in children.EnumerateArray())
        {
            if (!c.TryGetProperty("data", out var d)) continue;
            var created = d.TryGetProperty("created_utc", out var cu) && cu.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds((long)cu.GetDouble()).UtcDateTime : (DateTime?)null;
            if (created is null || created < fromUtc) continue;
            var title = d.TryGetProperty("title", out var t) ? t.GetString() : null;
            var body = d.TryGetProperty("selftext", out var b) ? b.GetString() : null;
            list.Add($"{title}\n{body}");
        }
        return list;
    }

    /// <summary>Distinct symbols a post names: every $CASHTAG that is a known ticker, and bare capitalized 3-5 letter known tickers that are not common words.</summary>
    public async Task<IReadOnlyList<string>> SymbolsAsync(string text, CancellationToken ct)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Cashtag().Matches(text))
        {
            var s = m.Groups[1].Value;
            if (!found.Contains(s) && await tickers.CikForTickerAsync(s, ct) is not null) found.Add(s);
        }
        foreach (Match m in BareCaps().Matches(text))
        {
            var s = m.Value;
            if (found.Contains(s) || CommonWords.Contains(s)) continue;
            if (await tickers.CikForTickerAsync(s, ct) is not null) found.Add(s);
        }
        return found.ToList();
    }

    /// <summary>Capitalized words that are also tickers but on Reddit almost always mean the word.</summary>
    public static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "THE", "AND", "FOR", "ARE", "BUT", "NOT", "YOU", "ALL", "ANY", "CAN", "HAS", "HER", "WAS", "ONE", "OUR", "OUT", "DAY", "GET", "HIM", "HIS", "HOW",
        "MAN", "NEW", "NOW", "OLD", "SEE", "TWO", "WAY", "WHO", "BOY", "DID", "ITS", "LET", "PUT", "SAY", "SHE", "TOO", "USE", "BIG", "RUN", "TOP", "LOW",
        "HIGH", "GOOD", "BEST", "REAL", "LOVE", "HOLD", "MOON", "PUMP", "DUMP", "YOLO", "LONG", "CALL", "CALLS", "PUTS", "BUY", "SELL", "GAIN", "LOSS",
        "CEO", "CFO", "CTO", "IPO", "ETF", "FDA", "SEC", "EPS", "ATH", "ATL", "IMO", "LOL", "OTC", "USA", "GDP", "CPI", "FED", "FOMC", "NYSE", "DD", "WSB",
        "API", "AI", "EV", "IRS", "FBI", "CIA", "NASA", "LMAO", "WTF", "OMG", "EDIT", "TLDR", "TL", "DR", "PSA", "AMA", "YTD", "EOD", "EOW", "AH", "PM",
        "RSI", "MACD", "EMA", "SMA", "VWAP", "IV", "OI", "ITM", "OTM", "ATM", "FOMO", "HODL", "BTC", "ETH", "NFT", "USD", "CAD", "EUR", "GBP", "JPY",
        "UK", "EU", "CEO", "NFA", "TA", "FA", "PT", "SI", "FTD", "FTDS", "RH", "IBKR", "SPAC", "REIT", "ESG", "SAAS", "LLC", "INC", "LTD", "CORP",
        "JUST", "LIKE", "THIS", "THAT", "WITH", "FROM", "HAVE", "WILL", "WHAT", "WHEN", "THEY", "BEEN", "MORE", "SOME", "VERY", "ALSO", "BACK", "NEXT",
        "OPEN", "PLAY", "MOVE", "NEWS", "INFO", "HUGE", "RIP", "FREE", "SAFE", "FAST", "LIFE", "CASH", "FUND", "PLAN", "CARE", "WORK", "TIME", "MIND",
        "ELON", "TRUMP", "BIDEN", "GO", "ON", "IT", "BE", "SO", "AM", "UP", "OR", "AT", "NO", "ME", "MY", "WE", "US", "IF", "IN", "OF", "TO", "DO", "AN",
    };

    [GeneratedRegex(@"(?<![A-Za-z0-9])\$([A-Z]{1,5})(?![A-Za-z])")]
    private static partial Regex Cashtag();

    [GeneratedRegex(@"(?<![A-Za-z0-9$])[A-Z]{3,5}(?![A-Za-z0-9])")]
    private static partial Regex BareCaps();
}
