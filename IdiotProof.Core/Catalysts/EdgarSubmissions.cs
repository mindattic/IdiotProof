using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>One filing from an issuer's EDGAR submissions history.</summary>
public sealed record SubmissionFiling(string AccessionNumber, string FormType, DateOnly FiledDate, DateTime? AcceptedUtc, IReadOnlyList<string> Items);

/// <summary>An older page of the submissions history (<c>filings.files</c>) and the filing dates it spans.</summary>
public sealed record SubmissionPage(string Name, DateOnly From, DateOnly To);

/// <summary>
/// An issuer's complete filing list from <c>https://data.sec.gov/submissions/CIK##########.json</c> (IP-T454):
/// the recent block (at least a year or 1,000 filings) plus the older pages it names, every form type, each with
/// its filing date, 8-K item codes and acceptance time. <c>acceptanceDateTime</c> is true UTC (checked against
/// the SGML header's ET acceptance time on a stored Form 4: 2021-11-12 18:36:04 ET = 23:36:04Z). Through the
/// "edgar" named client and <see cref="EdgarRateGate"/> like every other SEC call. Read-only.
/// </summary>
public sealed class EdgarSubmissions(IHttpClientFactory httpFactory, EdgarRateGate gate, ILogger<EdgarSubmissions> logger)
{
    public static string Url(string cik) => $"https://data.sec.gov/submissions/CIK{long.Parse(EdgarTickerMap.Normalize(cik), CultureInfo.InvariantCulture):D10}.json";

    /// <summary>
    /// Every filing the issuer made with a filing date in [from, to], reading only the older pages that overlap the
    /// window. Null when the SEC did not answer (a 404, an unknown CIK, is an empty list).
    /// </summary>
    public async Task<List<SubmissionFiling>?> GetAsync(string cik, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (status, json) = await GetJsonAsync(Url(cik), ct).ConfigureAwait(false);
        if (status == System.Net.HttpStatusCode.NotFound) return [];
        if (json is null) return null;
        var (filings, pages) = Parse(json);
        foreach (var page in pages.Where(p => p.To >= from && p.From <= to))
        {
            var (pageStatus, pageJson) = await GetJsonAsync("https://data.sec.gov/submissions/" + page.Name, ct).ConfigureAwait(false);
            if (pageJson is null)
            {
                if (pageStatus == System.Net.HttpStatusCode.NotFound) continue;
                return null; // an incomplete history must not be marked covered
            }
            filings.AddRange(Parse(pageJson).Filings);
        }
        return filings.Where(f => f.FiledDate >= from && f.FiledDate <= to).DistinctBy(f => f.AccessionNumber).OrderBy(f => f.FiledDate).ToList();
    }

    private async Task<(System.Net.HttpStatusCode? Status, string? Json)> GetJsonAsync(string url, CancellationToken ct)
    {
        System.Net.HttpStatusCode? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var (status, body) = await gate.RunAsync(async c =>
                {
                    var http = httpFactory.CreateClient("edgar");
                    using var resp = await http.GetAsync(url, c).ConfigureAwait(false);
                    return (resp.StatusCode, resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(c).ConfigureAwait(false) : null);
                }, ct).ConfigureAwait(false);
                if (body is not null || status == System.Net.HttpStatusCode.NotFound) return (status, body);
                last = status;
                logger.LogDebug("EDGAR submissions {Url} attempt {Attempt}: {Status}", url, attempt, status);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogDebug("EDGAR submissions {Url} attempt {Attempt}: {Message}", url, attempt, ex.Message);
            }
            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct).ConfigureAwait(false);
        }
        return (last, null);
    }

    /// <summary>
    /// Parse a submissions document: the main file (<c>filings.recent</c> and <c>filings.files</c>) or an older page
    /// (the same column arrays at the root). Pure.
    /// </summary>
    public static (List<SubmissionFiling> Filings, List<SubmissionPage> Pages) Parse(string json)
    {
        var filings = new List<SubmissionFiling>();
        var pages = new List<SubmissionPage>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var columns = root;
        if (root.TryGetProperty("filings", out var f))
        {
            if (!f.TryGetProperty("recent", out columns)) columns = default;
            if (f.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                foreach (var p in files.EnumerateArray())
                    if (Str(p, "name") is { Length: > 0 } name && DateOnly.TryParse(Str(p, "filingFrom"), CultureInfo.InvariantCulture, out var a)
                        && DateOnly.TryParse(Str(p, "filingTo"), CultureInfo.InvariantCulture, out var b))
                        pages.Add(new(name, a, b));
        }
        if (columns.ValueKind != JsonValueKind.Object) return (filings, pages);
        var acc = Arr(columns, "accessionNumber"); var forms = Arr(columns, "form"); var dates = Arr(columns, "filingDate");
        var accepted = Arr(columns, "acceptanceDateTime"); var items = Arr(columns, "items");
        for (int i = 0; i < acc.Count; i++)
        {
            if (acc[i].Length == 0 || !DateOnly.TryParse(dates.ElementAtOrDefault(i), CultureInfo.InvariantCulture, out var filed)) continue;
            DateTime? at = DateTime.TryParse(accepted.ElementAtOrDefault(i), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null;
            var codes = (items.ElementAtOrDefault(i) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filings.Add(new(acc[i], forms.ElementAtOrDefault(i) ?? "", filed, at, codes));
        }
        return (filings, pages);
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static List<string> Arr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString()).ToList()
            : [];
}
