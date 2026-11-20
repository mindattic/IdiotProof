using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using IdiotProof.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// One pass over a press-release wire's feed(s) (IP-T602): a conditional GET (ETag / Last-Modified, so an unchanged
/// feed costs a 304), 429 backoff through <see cref="HttpRateLimitRetry"/>, then every new item with a US ticker goes
/// through <see cref="CatalystIngestor"/> stamped with the instant we saw it. An item another source already carried
/// becomes a sighting on that story, which is how "who knew first" is measured.
/// </summary>
public sealed class WireFeedPoller(
    IHttpClientFactory httpFactory,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<WireFeedPoller> logger)
{
    public const string HttpClientName = "catalyst-wires";

    /// <summary>An item first seen this long after its own publish time is remembered but not ingested (a fresh start reads the whole feed).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    private readonly Lock gate = new();
    private readonly Dictionary<string, (string? ETag, DateTimeOffset? LastModified)> validators = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (HashSet<string> Set, Queue<string> Order)> seen = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SourcePollSummary> PollOnceAsync(CatalystSourceConfig source, CancellationToken ct = default)
    {
        if (source.Urls.Count == 0) throw new InvalidOperationException($"{source.Info.DisplayName} has no feed URL (set {source.Info.UrlKey}).");
        int itemsSeen = 0, inserted = 0, merged = 0, duplicates = 0, noSymbol = 0, notModified = 0, failed = 0, stale = 0;
        var errors = new List<string>();
        foreach (var url in source.Urls)
        {
            var xml = await FetchAsync(url, ct);
            if (xml.Status == HttpStatusCode.NotModified) { notModified++; continue; }
            if (xml.Body is null)
            {
                errors.Add($"{(int)xml.Status} from {Host(url)}");
                continue;
            }

            foreach (var item in WireFeed.Parse(xml.Body))
            {
                var id = ExternalId(item);
                if (!Remember(source.Info.Name, id)) continue;
                itemsSeen++;
                var now = time.GetUtcNow().UtcDateTime;
                var published = item.PublishedUtc is { } p && p <= now.AddMinutes(5) ? p : now;
                if (now - published > MaxAge) { stale++; continue; }
                var tickers = WireFeed.Tickers(item);
                if (tickers.Count == 0) { noSymbol++; continue; }
                try
                {
                    var intake = new CatalystIntake(source.Info.Name, id, published, now, tickers, item.Title,
                        item.Summary, item.Link, source.Info.Name);
                    using var scope = scopes.CreateScope();
                    var result = await scope.ServiceProvider.GetRequiredService<CatalystIngestor>().IngestAsync(intake, ct);
                    switch (result.Outcome)
                    {
                        case CatalystIngestOutcome.Inserted: inserted++; break;
                        case CatalystIngestOutcome.Merged: merged++; break;
                        case CatalystIngestOutcome.NoSymbol: noSymbol++; break;
                        default: duplicates++; break;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    Forget(source.Info.Name, id);
                    logger.LogWarning(ex, "Wire item {Source}/{Id} failed to ingest.", source.Info.Name, id);
                }
            }
        }
        if (errors.Count == source.Urls.Count && errors.Count > 0)
            throw new HttpRequestException($"{source.Info.DisplayName}: {string.Join("; ", errors)}");
        var summary = $"{itemsSeen} new item(s): {inserted} stored, {merged} merged into an existing story, {duplicates} duplicate(s), {noSymbol} without a US ticker" +
                      (stale > 0 ? $", {stale} older than {MaxAge.TotalHours:0} h" : "") + (notModified > 0 ? $", {notModified} feed(s) unchanged" : "") +
                      (failed > 0 ? $", {failed} failed" : "") + (errors.Count > 0 ? $"; {string.Join("; ", errors)}" : "");
        return new SourcePollSummary(itemsSeen, inserted, merged, duplicates, noSymbol, 0, notModified, failed, summary);
    }

    private async Task<(HttpStatusCode Status, string? Body)> FetchAsync(string url, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        (string? ETag, DateTimeOffset? LastModified) v;
        lock (gate) v = validators.TryGetValue(url, out var known) ? known : default;
        using var resp = await HttpRateLimitRetry.SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8");
            if (v.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag)) req.Headers.IfNoneMatch.Add(tag);
            if (v.LastModified is { } lm) req.Headers.IfModifiedSince = lm;
            return http.SendAsync(req, ct);
        }, ct);
        if (resp.StatusCode == HttpStatusCode.NotModified) return (resp.StatusCode, null);
        if (!resp.IsSuccessStatusCode) return (resp.StatusCode, null);
        var body = await resp.Content.ReadAsStringAsync(ct);
        lock (gate) validators[url] = (resp.Headers.ETag?.ToString(), resp.Content.Headers.LastModified);
        return (resp.StatusCode, body);
    }

    /// <summary>The item's own id, hashed when it is too long for the 64-character column (PR Newswire's guid is the release URL).</summary>
    public static string ExternalId(WireFeedItem item)
    {
        var id = item.Id.Trim();
        if (id.Length <= 64) return id;
        return "sha1:" + Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(id)));
    }

    private bool Remember(string source, string id)
    {
        lock (gate)
        {
            if (!seen.TryGetValue(source, out var s)) seen[source] = s = (new HashSet<string>(StringComparer.Ordinal), new Queue<string>());
            if (!s.Set.Add(id)) return false;
            s.Order.Enqueue(id);
            while (s.Order.Count > 5000) s.Set.Remove(s.Order.Dequeue());
            return true;
        }
    }

    private void Forget(string source, string id)
    {
        lock (gate)
        {
            if (seen.TryGetValue(source, out var s)) s.Set.Remove(id);
        }
    }

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
}
