using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

public sealed record EdgarPollSummary(int Entries, int Candidates, int Unmapped, int Inserted, int Duplicates, int Failed);

/// <summary>
/// One pass over EDGAR's latest-filings Atom feeds for 8-K, 4, S-1 and 424B (IP-T443): keeps the
/// forms the catalyst model cares about, maps CIK to ticker, reads each new filing's acceptance
/// time and 8-K items from its header, and ingests it with Source "edgar". The feed's <c>type</c>
/// filter is a prefix match (424B covers 424B3/4/5), so the exact allow-list is applied here.
/// </summary>
public sealed class EdgarLatestFilingsPoller(
    IHttpClientFactory httpFactory,
    EdgarRateGate gate,
    EdgarTickerMap tickers,
    EdgarFilingHeaders headers,
    IDbContextFactory<AppDbContext> dbFactory,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<EdgarLatestFilingsPoller> logger)
{
    public static readonly string[] FeedTypes = ["8-K", "4", "S-1", "424B"];

    private readonly HashSet<string> seen = [];
    private readonly Queue<string> seenOrder = new();

    public static string FeedUrl(string type) =>
        $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcurrent&type={Uri.EscapeDataString(type)}&count=100&output=atom";

    public static bool Accepts(string formType)
    {
        var form = formType.Trim().ToUpperInvariant();
        if (form.EndsWith("/A")) form = form[..^2];
        return form is "8-K" or "4" or "S-1" || form.StartsWith("424B");
    }

    public async Task<EdgarPollSummary> PollOnceAsync(CancellationToken ct = default)
    {
        var all = new List<EdgarAtomEntry>();
        foreach (var type in FeedTypes)
        {
            var xml = await gate.RunAsync(async c =>
            {
                var http = httpFactory.CreateClient("edgar");
                using var resp = await http.GetAsync(FeedUrl(type), c);
                return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(c) : null;
            }, ct);
            if (xml is null) continue;
            all.AddRange(EdgarAtomFeed.Parse(xml));
        }

        var candidates = EdgarAtomFeed.OnePerAccession(all.Where(e => Accepts(e.FormType)));
        int unmapped = 0, inserted = 0, duplicates = 0, failed = 0, fresh = 0;
        foreach (var entry in candidates)
        {
            if (seen.Contains(entry.AccessionNumber)) continue;
            fresh++;
            try
            {
                if (await AlreadyStoredAsync(entry.AccessionNumber, ct)) { Remember(entry.AccessionNumber); duplicates++; continue; }
                var ticker = entry.Cik.Length > 0 ? await tickers.TickerForCikAsync(entry.Cik, ct) : null;
                if (ticker is null) { Remember(entry.AccessionNumber); unmapped++; continue; }

                var header = await headers.FetchAsync(entry.Cik, entry.AccessionNumber, ct);
                var published = header?.AcceptanceUtc ?? entry.UpdatedUtc ?? time.GetUtcNow().UtcDateTime;
                var items = header?.Items is { Count: > 0 } i ? i : null;
                var intake = new CatalystIntake(
                    CatalystIntake.Edgar, entry.AccessionNumber, published, time.GetUtcNow().UtcDateTime, [ticker],
                    Headline(entry, items), entry.Summary, entry.IndexUrl, "edgar", entry.FormType, items);

                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<CatalystIngestor>().IngestAsync(intake, ct);
                Remember(entry.AccessionNumber);
                if (result.Outcome == CatalystIngestOutcome.Inserted) inserted++; else duplicates++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "EDGAR filing {Accession} ({Form}) failed to ingest.", entry.AccessionNumber, entry.FormType);
            }
        }
        return new EdgarPollSummary(all.Count, fresh, unmapped, inserted, duplicates, failed);
    }

    public static string Headline(EdgarAtomEntry entry, IReadOnlyList<string>? items) =>
        $"{entry.FormType}: {entry.CompanyName}" + (items is { Count: > 0 } ? $" (items {string.Join(", ", items)})" : "");

    private async Task<bool> AlreadyStoredAsync(string accession, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CatalystEvents.AnyAsync(e => e.Source == CatalystIntake.Edgar && e.ExternalId == accession, ct);
    }

    private void Remember(string accession)
    {
        if (!seen.Add(accession)) return;
        seenOrder.Enqueue(accession);
        while (seenOrder.Count > 5000) seen.Remove(seenOrder.Dequeue());
    }
}
