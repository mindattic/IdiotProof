using System.Text.Json;

namespace IdiotProof.Blazor.Services;

public record EdgarFiling(
    string FormType,
    string FilingDate,
    string EntityName,
    string AccessionNumber,
    string BrowseUrl
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

                var accession = hit.TryGetProperty("_id", out var id) ? id.GetString() ?? "" : "";
                var formType  = src.TryGetProperty("form_type",   out var ft) ? ft.GetString() ?? "" : "";
                var fileDate  = src.TryGetProperty("file_date",   out var fd) ? fd.GetString() ?? "" : "";
                var entity    = src.TryGetProperty("entity_name", out var en) ? en.GetString() ?? "" : "";

                results.Add(new EdgarFiling(
                    FormType:       formType,
                    FilingDate:     fileDate,
                    EntityName:     entity,
                    AccessionNumber: accession,
                    BrowseUrl:      $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={Uri.EscapeDataString(ticker)}&type={formType}&dateb=&owner=include&count=10"
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
}
