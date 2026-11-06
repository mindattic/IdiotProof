using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using IdiotProof.Core.Services;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>What a filing's SGML header says: when EDGAR accepted it (the real publish instant) and, for an 8-K, its item codes.</summary>
public sealed record EdgarFilingHeader(DateTime? AcceptanceUtc, IReadOnlyList<string> Items, string? SubmissionType);

/// <summary>
/// Fetches and parses <c>{accession}-index-headers.html</c> from the EDGAR archive. The feed and the
/// full-text search carry only the filing date; <c>ACCEPTANCE-DATETIME</c> (ET, to the second) is
/// what the latency and the point-in-time snapshots need.
/// </summary>
public sealed partial class EdgarFilingHeaders(IHttpClientFactory httpFactory, EdgarRateGate gate, ILogger<EdgarFilingHeaders> logger)
{
    private static readonly Dictionary<string, string> ItemTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Entry into a Material Definitive Agreement"] = "1.01",
        ["Termination of a Material Definitive Agreement"] = "1.02",
        ["Bankruptcy or Receivership"] = "1.03",
        ["Completion of Acquisition or Disposition of Assets"] = "2.01",
        ["Results of Operations and Financial Condition"] = "2.02",
        ["Creation of a Direct Financial Obligation or an Obligation under an Off-Balance Sheet Arrangement of a Registrant"] = "2.03",
        ["Triggering Events That Accelerate or Increase a Direct Financial Obligation or an Obligation under an Off-Balance Sheet Arrangement"] = "2.04",
        ["Costs Associated with Exit or Disposal Activities"] = "2.05",
        ["Material Impairments"] = "2.06",
        ["Notice of Delisting or Failure to Satisfy a Continued Listing Rule or Standard; Transfer of Listing"] = "3.01",
        ["Unregistered Sales of Equity Securities"] = "3.02",
        ["Material Modification to Rights of Security Holders"] = "3.03",
        ["Changes in Registrant's Certifying Accountant"] = "4.01",
        ["Non-Reliance on Previously Issued Financial Statements or a Related Audit Report or Completed Interim Review"] = "4.02",
        ["Changes in Control of Registrant"] = "5.01",
        ["Departure of Directors or Certain Officers; Election of Directors; Appointment of Certain Officers: Compensatory Arrangements of Certain Officers"] = "5.02",
        ["Amendments to Articles of Incorporation or Bylaws; Change in Fiscal Year"] = "5.03",
        ["Temporary Suspension of Trading Under Registrant's Employee Benefit Plans"] = "5.04",
        ["Amendment to Registrant's Code of Ethics, or Waiver of a Provision of the Code of Ethics"] = "5.05",
        ["Change in Shell Company Status"] = "5.06",
        ["Submission of Matters to a Vote of Security Holders"] = "5.07",
        ["Shareholder Director Nominations"] = "5.08",
        ["Regulation FD Disclosure"] = "7.01",
        ["Other Events"] = "8.01",
        ["Financial Statements and Exhibits"] = "9.01",
    };

    public static string Url(string cik, string accession) =>
        $"https://www.sec.gov/Archives/edgar/data/{EdgarTickerMap.Normalize(cik)}/{accession.Replace("-", "")}/{accession}-index-headers.html";

    public async Task<EdgarFilingHeader?> FetchAsync(string cik, string accession, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cik) || string.IsNullOrWhiteSpace(accession)) return null;
        var url = Url(cik, accession);
        try
        {
            var body = await gate.RunAsync(async c =>
            {
                var http = httpFactory.CreateClient("edgar");
                using var resp = await http.GetAsync(url, c);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsStringAsync(c);
            }, ct);
            return body is null ? null : Parse(body);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "EDGAR header fetch failed for {Url}", url);
            return null;
        }
    }

    /// <summary>Reads the SGML header whether its tags are raw or HTML-escaped; item codes from <c>&lt;ITEMS&gt;</c> or the standard item titles.</summary>
    public static EdgarFilingHeader Parse(string headersHtml)
    {
        var text = WebUtility.HtmlDecode(headersHtml);
        DateTime? accepted = null;
        if (AcceptancePattern().Match(text) is { Success: true } m
            && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var et))
            accepted = EasternTime.ToUtc(et);

        var items = new List<string>();
        foreach (Match code in ItemsPattern().Matches(text))
            if (!items.Contains(code.Groups[1].Value)) items.Add(code.Groups[1].Value);
        foreach (Match info in ItemInfoPattern().Matches(text))
        {
            var title = info.Groups[1].Value.Trim();
            if (ItemTitles.TryGetValue(title, out var code) && !items.Contains(code)) items.Add(code);
        }
        var type = SubmissionTypePattern().Match(text) is { Success: true } t ? t.Groups[1].Value.Trim() : null;
        return new EdgarFilingHeader(accepted, items, type);
    }

    [GeneratedRegex(@"<ACCEPTANCE-DATETIME>\s*(\d{14})", RegexOptions.IgnoreCase)]
    private static partial Regex AcceptancePattern();

    [GeneratedRegex(@"<ITEMS>\s*(\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemsPattern();

    [GeneratedRegex(@"ITEM INFORMATION:\s*([^\r\n<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemInfoPattern();

    [GeneratedRegex(@"CONFORMED SUBMISSION TYPE:\s*([^\r\n<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SubmissionTypePattern();
}
