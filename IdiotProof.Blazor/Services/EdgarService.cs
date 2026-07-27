using System.Text.Json;

namespace IdiotProof.Blazor.Services;

public record EdgarFiling(
    string   FormType,
    string   FilingDate,
    string   EntityName,
    string   AccessionNumber,
    string   BrowseUrl,
    string?  DocumentFileName = null,
    string?  IssuerCik        = null,
    string[]? Items           = null
);

/// <summary>
/// Fetches SEC EDGAR filings (8-K material events, Form 4 insider transactions)
/// via the EDGAR full-text search API — a Tier 1 primary source with zero noise.
/// SEC requires a descriptive User-Agent header; configured on the named client.
/// </summary>
public sealed class EdgarService(IHttpClientFactory httpFactory, ILogger<EdgarService> logger)
{
    private const string SearchBase = "https://efts.sec.gov/LATEST/search-index";

    public async Task<List<EdgarFiling>> GetRecentFilingsAsync(
        string ticker,
        string forms,
        int daysBack = 30,
        CancellationToken ct = default)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-daysBack)).ToString("yyyy-MM-dd");
        var end   = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var url   = $"{SearchBase}?q=%22{Uri.EscapeDataString(ticker)}%22&forms={forms}&dateRange=custom&startdt={start}&enddt={end}";

        try
        {
            var http = httpFactory.CreateClient("edgar");
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("EDGAR returned {Status} for {Ticker}", resp.StatusCode, ticker);
                return [];
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("hits", out var hits)) return [];
            if (!hits.TryGetProperty("hits", out var arr)) return [];

            var results = new List<EdgarFiling>();
            foreach (var hit in arr.EnumerateArray())
            {
                if (!hit.TryGetProperty("_source", out var src)) continue;

                // "_id" is "{accession-with-dashes}:{filename}", e.g.
                // "0001140361-26-025620:form4.xml" — split to recover both.
                var hitId      = hit.TryGetProperty("_id", out var id) ? id.GetString() ?? "" : "";
                var colonIdx   = hitId.IndexOf(':');
                var accession  = colonIdx > 0 ? hitId[..colonIdx] : hitId;
                var docFile    = colonIdx > 0 ? hitId[(colonIdx + 1)..] : null;

                var formType  = src.TryGetProperty("form",        out var ft) ? ft.GetString() ?? "" : "";
                var fileDate  = src.TryGetProperty("file_date",   out var fd) ? fd.GetString() ?? "" : "";
                var entity    = src.TryGetProperty("display_names", out var dn) && dn.GetArrayLength() > 0
                    ? dn[dn.GetArrayLength() - 1].GetString() ?? ""
                    : "";

                // "ciks" lists every party on the filing (reporting owner first,
                // issuer last for ownership forms; the sole entity for 8-Ks) —
                // the last CIK is the one whose EDGAR archive folder holds the
                // document, verified against a live Form 4/8-K fetch.
                string? issuerCik = null;
                if (src.TryGetProperty("ciks", out var ciksEl) && ciksEl.GetArrayLength() > 0)
                    issuerCik = ciksEl[ciksEl.GetArrayLength() - 1].GetString();

                string[]? items = null;
                if (src.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                    items = itemsEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();

                // A real per-filing URL (not a generic ticker/form search page) —
                // this doubles as the dedup key ResearchService checks before
                // re-extracting a filing it has already ingested, so it must be
                // unique per accession, not shared across every filing of the
                // same form type for this ticker.
                var browseUrl = !string.IsNullOrWhiteSpace(issuerCik)
                    ? $"https://www.sec.gov/Archives/edgar/data/{issuerCik.TrimStart('0')}/{accession.Replace("-", "")}/{accession}-index.htm"
                    : $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={Uri.EscapeDataString(ticker)}&type={formType}&dateb=&owner=include&count=10";

                results.Add(new EdgarFiling(
                    FormType:       formType,
                    FilingDate:     fileDate,
                    EntityName:     entity,
                    AccessionNumber: accession,
                    BrowseUrl:      browseUrl,
                    DocumentFileName: docFile,
                    IssuerCik:      issuerCik,
                    Items:          items
                ));
            }
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "EDGAR fetch failed for {Ticker} forms={Forms}", ticker, forms);
            return [];
        }
    }

    /// <summary>
    /// Fetches the raw text of a filing's primary document (Form 4 XML, 8-K HTML
    /// exhibit, etc.) straight from the EDGAR archive — the actual disclosure
    /// content, not just full-text-search metadata. Returns null on any failure
    /// (fails closed: callers fall back to metadata-only text).
    /// </summary>
    public async Task<string?> GetFilingDocumentAsync(
        EdgarFiling filing, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filing.IssuerCik) || string.IsNullOrWhiteSpace(filing.DocumentFileName))
            return null;

        var cikNoLeadingZeros  = filing.IssuerCik.TrimStart('0');
        var accessionNoDashes  = filing.AccessionNumber.Replace("-", "");
        var url = $"https://www.sec.gov/Archives/edgar/data/{cikNoLeadingZeros}/{accessionNoDashes}/{filing.DocumentFileName}";

        try
        {
            var http = httpFactory.CreateClient("edgar");
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogDebug("EDGAR document fetch returned {Status} for {Url}", resp.StatusCode, url);
                return null;
            }
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "EDGAR document fetch failed for {Url}", url);
            return null;
        }
    }
}
