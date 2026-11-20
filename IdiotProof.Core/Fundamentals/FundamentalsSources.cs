using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IdiotProof.Core.Services;

namespace IdiotProof.Core.Fundamentals;

/// <summary>
/// Shared plumbing for the HTTP vendors: the Vault key gate, one request at a time per source with a
/// minimum spacing, 429 back-off through <see cref="HttpRateLimitRetry"/> (Retry-After honored), and
/// error messages that carry the status code only (never the URL: two vendors put the key in it).
/// </summary>
public abstract class HttpFundamentalsSource(FundamentalsKeys keys, Func<HttpClient> http, TimeProvider clock) : IFundamentalsSource
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime lastRequestUtc = DateTime.MinValue;

    public abstract string Name { get; }
    public abstract string DisplayName { get; }
    public abstract string VaultEntry { get; }
    public abstract string Provides { get; }

    /// <summary>Smallest gap between two requests to this vendor.</summary>
    protected virtual TimeSpan MinSpacing => TimeSpan.FromMilliseconds(250);

    protected TimeProvider Clock => clock;

    public bool IsConfigured => keys.Get(VaultEntry) is not null;

    public string Status => IsConfigured ? "configured" : $"no key in Vault: {VaultEntry}";

    public async Task<FundamentalsSnapshot?> FetchAsync(string symbol, CancellationToken ct)
    {
        var key = keys.Get(VaultEntry) ?? throw new FundamentalsSourceException($"{DisplayName} is off: no key in Vault: {VaultEntry}");
        return await FetchCoreAsync(symbol.Trim().ToUpperInvariant(), key, ct);
    }

    protected abstract Task<FundamentalsSnapshot?> FetchCoreAsync(string symbol, string key, CancellationToken ct);

    /// <summary>
    /// GETs <paramref name="build"/>'s request; the body on success, null on 404, else a
    /// <see cref="FundamentalsSourceException"/> naming <paramref name="what"/> and the status code.
    /// </summary>
    protected async Task<string?> GetAsync(string what, Func<HttpRequestMessage> build, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var wait = lastRequestUtc + MinSpacing - clock.GetUtcNow().UtcDateTime;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            var client = http();
            HttpResponseMessage resp;
            try
            {
                resp = await HttpRateLimitRetry.SendAsync(() => client.SendAsync(build(), ct), ct);
            }
            catch (HttpRequestException ex)
            {
                throw new FundamentalsSourceException($"{DisplayName} {what}: request failed ({ex.HttpRequestError})");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new FundamentalsSourceException($"{DisplayName} {what}: timed out");
            }
            finally
            {
                lastRequestUtc = clock.GetUtcNow().UtcDateTime;
            }

            using (resp)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound) return null;
                if ((int)resp.StatusCode == 429)
                    throw new FundamentalsSourceException($"{DisplayName} {what}: still rate-limited (429) after {HttpRateLimitRetry.MaxRetries} retries");
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new FundamentalsSourceException($"{DisplayName} {what}: {(int)resp.StatusCode} (key rejected, or the plan does not include this endpoint)");
                if (!resp.IsSuccessStatusCode)
                    throw new FundamentalsSourceException($"{DisplayName} {what}: HTTP {(int)resp.StatusCode}");
                return await resp.Content.ReadAsStringAsync(ct);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    protected DateOnly TodayEt() =>
        DateOnly.FromDateTime(global::IdiotProof.Evidence.Gappers.MinuteBarMath.ToEastern(clock.GetUtcNow().UtcDateTime));

    // ── JSON helpers: vendors send numbers as numbers or as strings ──

    protected static double? Number(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    protected static long? Count(JsonElement obj, string name) =>
        Number(obj, name) is { } d && d >= 0 && d < 9e17 ? (long)Math.Round(d) : null;

    protected static DateOnly? Date(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return null;
        var s = v.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateOnly.TryParseExact(s.Length >= 10 ? s[..10] : s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    protected static double? Ratio(long? numerator, long? denominator) =>
        numerator is { } n && denominator is { } d && d > 0 ? Math.Round(100.0 * n / d, 2) : null;
}

/// <summary>
/// Financial Modeling Prep (stable API): <c>GET /stable/shares-float?symbol=X&amp;apikey=KEY</c> returns
/// <c>[{symbol, date, freeFloat, floatShares, outstandingShares}]</c>. FMP's stable API has no
/// documented short-interest endpoint, so the short fields stay null for this source.
/// Vault entry: <c>fmp</c>.
/// </summary>
public sealed class FmpFundamentalsSource(FundamentalsKeys keys, Func<HttpClient> http, TimeProvider clock)
    : HttpFundamentalsSource(keys, http, clock)
{
    public const string Entry = "fmp";
    private const string Base = "https://financialmodelingprep.com/stable/";

    public override string Name => Entry;
    public override string DisplayName => "Financial Modeling Prep";
    public override string VaultEntry => Entry;
    public override string Provides => "shares outstanding, float (no short interest)";

    protected override async Task<FundamentalsSnapshot?> FetchCoreAsync(string symbol, string key, CancellationToken ct)
    {
        var body = await GetAsync("shares-float", () => new HttpRequestMessage(HttpMethod.Get,
            $"{Base}shares-float?symbol={Uri.EscapeDataString(symbol)}&apikey={Uri.EscapeDataString(key)}"), ct);
        if (body is null) return null;

        using var doc = Parse(body);
        var root = doc.RootElement;
        // An error arrives as an object ({"Error Message": "..."}), never echoed (it can quote the request).
        if (root.ValueKind == JsonValueKind.Object)
            throw new FundamentalsSourceException($"{DisplayName} shares-float: the API returned an error object (key or plan?)");
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;

        var row = root[0];
        var floatShares = Count(row, "floatShares");
        var outstanding = Count(row, "outstandingShares");
        var asOf = Date(row, "date");
        return new FundamentalsSnapshot
        {
            Symbol = symbol,
            Source = Name,
            FetchedUtc = Clock.GetUtcNow().UtcDateTime,
            SharesOutstanding = outstanding,
            SharesOutstandingAsOf = asOf,
            FloatShares = floatShares,
            FloatAsOf = asOf,
            FloatPercentOfOutstanding = Number(row, "freeFloat") is { } ff ? Math.Round(ff, 2) : Ratio(floatShares, outstanding),
            Notes = ["FMP stable API has no short-interest endpoint; short fields not available from this source."],
        };
    }

    private JsonDocument Parse(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new FundamentalsSourceException($"{DisplayName}: response was not JSON"); }
    }
}

/// <summary>
/// Polygon.io (Massive): <c>GET /v3/reference/tickers/{T}</c> (share-class and weighted shares
/// outstanding), <c>GET /stocks/vX/float?ticker=T</c> (free float, free-float percent, effective
/// date) and <c>GET /stocks/v1/short-interest?ticker=T</c> (FINRA short interest, days to cover,
/// settlement date). The key goes in an <c>Authorization: Bearer</c> header, never the URL. A
/// sub-endpoint the plan lacks becomes a note; only all three failing is an error.
/// Vault entry: <c>polygon</c>.
/// </summary>
public sealed class PolygonFundamentalsSource(FundamentalsKeys keys, Func<HttpClient> http, TimeProvider clock)
    : HttpFundamentalsSource(keys, http, clock)
{
    public const string Entry = "polygon";
    private const string Base = "https://api.polygon.io";

    public override string Name => Entry;
    public override string DisplayName => "Polygon";
    public override string VaultEntry => Entry;
    public override string Provides => "shares outstanding, float, short interest, days to cover";

    // The free tier allows 5 requests a minute; HttpRateLimitRetry handles a 429 beyond that.
    protected override TimeSpan MinSpacing => TimeSpan.FromMilliseconds(250);

    protected override async Task<FundamentalsSnapshot?> FetchCoreAsync(string symbol, string key, CancellationToken ct)
    {
        var notes = new List<string>();
        var failures = 0;
        var t = Uri.EscapeDataString(symbol);

        HttpRequestMessage Req(string pathAndQuery)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, Base + pathAndQuery);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return r;
        }

        async Task<JsonDocument?> Try(string what, string pathAndQuery)
        {
            try
            {
                var body = await GetAsync(what, () => Req(pathAndQuery), ct);
                if (body is null) { notes.Add($"{what}: not found"); return null; }
                return JsonDocument.Parse(body);
            }
            catch (FundamentalsSourceException ex) { failures++; notes.Add(ex.Message); return null; }
            catch (JsonException) { failures++; notes.Add($"{what}: response was not JSON"); return null; }
        }

        long? outstanding = null, floatShares = null, shortInterest = null;
        double? floatPct = null, daysToCover = null;
        DateOnly? floatAsOf = null, shortAsOf = null;
        var known = false;

        using (var details = await Try("ticker details", $"/v3/reference/tickers/{t}"))
        {
            if (details?.RootElement.TryGetProperty("results", out var r) == true && r.ValueKind == JsonValueKind.Object)
            {
                known = true;
                outstanding = Count(r, "share_class_shares_outstanding") ?? Count(r, "weighted_shares_outstanding");
            }
        }

        using (var fl = await Try("float", $"/stocks/vX/float?ticker={t}&limit=1"))
        {
            if (fl?.RootElement.TryGetProperty("results", out var rs) == true && rs.ValueKind == JsonValueKind.Array && rs.GetArrayLength() > 0)
            {
                known = true;
                var row = rs[0];
                floatShares = Count(row, "free_float");
                floatPct = Number(row, "free_float_percent");
                floatAsOf = Date(row, "effective_date");
            }
        }

        using (var si = await Try("short interest", $"/stocks/v1/short-interest?ticker={t}&sort=settlement_date.desc&limit=1"))
        {
            if (si?.RootElement.TryGetProperty("results", out var rs) == true && rs.ValueKind == JsonValueKind.Array && rs.GetArrayLength() > 0)
            {
                known = true;
                var row = rs[0];
                shortInterest = Count(row, "short_interest");
                daysToCover = Number(row, "days_to_cover");
                shortAsOf = Date(row, "settlement_date");
            }
        }

        if (failures == 3) throw new FundamentalsSourceException(string.Join("; ", notes));
        if (!known) return null;
        return new FundamentalsSnapshot
        {
            Symbol = symbol,
            Source = Name,
            FetchedUtc = Clock.GetUtcNow().UtcDateTime,
            SharesOutstanding = outstanding,
            SharesOutstandingAsOf = outstanding is null ? null : TodayEt(),
            FloatShares = floatShares,
            FloatAsOf = floatAsOf,
            FloatPercentOfOutstanding = floatPct ?? Ratio(floatShares, outstanding),
            ShortInterest = shortInterest,
            ShortInterestAsOf = shortAsOf,
            ShortPercentOfFloat = Ratio(shortInterest, floatShares),
            DaysToCover = daysToCover is { } d ? Math.Round(d, 2) : null,
            Notes = notes,
        };
    }
}

/// <summary>
/// Finviz Elite screener export: <c>GET https://elite.finviz.com/export.ashx?v=131&amp;t=SYM&amp;auth=TOKEN</c>
/// (the Ownership view) returns a CSV whose header names the columns; they are matched by name
/// (Shares Outstanding / Outstanding, Shares Float / Float, Short Float / Float Short, Short Ratio,
/// Short Interest), not by position. Finviz exports share counts in millions; a value under 100,000 is
/// read as millions. Finviz gives no as-of dates (short data refreshes twice a month), so those stay null.
/// Vault entry: <c>finviz-elite</c>.
/// </summary>
public sealed class FinvizEliteFundamentalsSource(FundamentalsKeys keys, Func<HttpClient> http, TimeProvider clock)
    : HttpFundamentalsSource(keys, http, clock)
{
    public const string Entry = "finviz-elite";
    private const string Base = "https://elite.finviz.com/export.ashx";

    public override string Name => Entry;
    public override string DisplayName => "Finviz Elite";
    public override string VaultEntry => Entry;
    public override string Provides => "shares outstanding, float, short % of float, short ratio (no as-of dates)";

    protected override TimeSpan MinSpacing => TimeSpan.FromSeconds(1);

    protected override async Task<FundamentalsSnapshot?> FetchCoreAsync(string symbol, string key, CancellationToken ct)
    {
        var body = await GetAsync("export", () => new HttpRequestMessage(HttpMethod.Get,
            $"{Base}?v=131&t={Uri.EscapeDataString(symbol)}&auth={Uri.EscapeDataString(key)}"), ct);
        if (body is null) return null;
        if (body.TrimStart().StartsWith('<'))
            throw new FundamentalsSourceException($"{DisplayName} export: returned a web page, not CSV (auth token rejected or expired?)");

        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return null;
        var header = SplitCsv(lines[0]);
        var row = lines.Skip(1).Select(SplitCsv)
            .FirstOrDefault(r => Cell(header, r, "Ticker") is { } tk && tk.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (row is null) return null;

        var notes = new List<string>();
        long? Shares(params string[] names)
        {
            var raw = Cell(header, row, names);
            if (!TryNumber(raw, out var v)) return null;
            if (v < 100_000) { v *= 1_000_000; if (!notes.Contains(MillionsNote)) notes.Add(MillionsNote); }
            return (long)Math.Round(v);
        }

        var outstanding = Shares("Shares Outstanding", "Outstanding");
        var floatShares = Shares("Shares Float", "Float");
        var shortInterest = Shares("Short Interest");
        var shortPct = TryNumber(Cell(header, row, "Short Float", "Float Short", "Short Float %"), out var sp) ? sp : (double?)null;
        var shortRatio = TryNumber(Cell(header, row, "Short Ratio"), out var sr) ? sr : (double?)null;
        var today = TodayEt();
        return new FundamentalsSnapshot
        {
            Symbol = symbol,
            Source = Name,
            FetchedUtc = Clock.GetUtcNow().UtcDateTime,
            SharesOutstanding = outstanding,
            SharesOutstandingAsOf = outstanding is null ? null : today,
            FloatShares = floatShares,
            FloatAsOf = floatShares is null ? null : today,
            FloatPercentOfOutstanding = Ratio(floatShares, outstanding),
            ShortInterest = shortInterest,
            ShortPercentOfFloat = shortPct ?? Ratio(shortInterest, floatShares),
            DaysToCover = shortRatio,
            Notes = [.. notes, "Finviz gives no short-interest settlement date (refreshes twice a month)."],
        };
    }

    private const string MillionsNote = "Finviz share counts read as millions.";

    private static string? Cell(IReadOnlyList<string> header, IReadOnlyList<string> row, params string[] names)
    {
        foreach (var name in names)
        {
            for (var i = 0; i < header.Count; i++)
                if (header[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i < row.Count ? row[i] : null;
        }
        return null;
    }

    private static bool TryNumber(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "-") return false;
        var s = raw.Trim().TrimEnd('%').Replace(",", "");
        double scale = 1;
        if (s.EndsWith('M')) { scale = 1_000_000; s = s[..^1]; }
        else if (s.EndsWith('B')) { scale = 1_000_000_000; s = s[..^1]; }
        else if (s.EndsWith('K')) { scale = 1_000; s = s[..^1]; }
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
        value = v * scale;
        return true;
    }

    /// <summary>One CSV line: commas outside double quotes split; "" inside quotes is a quote.</summary>
    internal static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var sb = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }
}
