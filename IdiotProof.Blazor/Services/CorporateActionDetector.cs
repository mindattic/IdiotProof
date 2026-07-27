namespace IdiotProof.Blazor.Services;

/// <summary>
/// Classifies 8-K filings by their SEC "items" codes so the research pipeline only
/// pays for a real document fetch on the highest-value events (reverse/forward stock
/// splits, M&amp;A, material agreements) instead of every routine 8-K. Filings with
/// unknown/missing item codes fail toward fetching more, not less — no silent gaps.
/// </summary>
public sealed record CorporateActionResult(EdgarFiling Filing, string Text, string Reason, bool IsHighPriority);

public sealed class CorporateActionDetector(EdgarService edgar, ILogger<CorporateActionDetector> logger)
{
    private const int MaxFetchedTextLength = 6000;

    // Item codes worth an extra HTTP fetch — the ones that most often carry a
    // reverse/forward split, an acquisition, or another market-moving mechanism.
    private static readonly HashSet<string> HighPriorityItems =
        new(StringComparer.OrdinalIgnoreCase) { "1.01", "2.01", "3.02", "3.03", "5.03" };

    private static readonly Dictionary<string, string> ItemDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1.01"] = "material agreement entered",
        ["2.01"] = "acquisition or disposition of assets completed",
        ["3.02"] = "unregistered equity sale",
        ["3.03"] = "material modification to security holder rights",
        ["5.03"] = "charter amendment (articles/bylaws)",
    };

    /// <summary>
    /// Classifies each 8-K filing and, for high-priority ones, fetches the real
    /// primary-document text. Low-priority filings (and any fetch failure) fall
    /// back to the existing boilerplate-style summary text.
    /// </summary>
    public async Task<List<CorporateActionResult>> DetectAsync(
        IEnumerable<EdgarFiling> eightKFilings, CancellationToken ct = default)
    {
        var results = new List<CorporateActionResult>();

        foreach (var filing in eightKFilings)
        {
            var matched = filing.Items is { Length: > 0 }
                ? filing.Items.Where(HighPriorityItems.Contains).ToArray()
                : [];

            // Unknown/missing item shape is treated as high priority — fail
            // toward fetching more information, not less.
            var isUnknownShape = filing.Items is null || filing.Items.Length == 0;
            var isHighPriority = isUnknownShape || matched.Length > 0;

            if (!isHighPriority)
            {
                var lowPriReason = $"Items {string.Join(", ", filing.Items!)}: no high-value trigger codes present; skipping document fetch";
                results.Add(new CorporateActionResult(filing, BuildBoilerplateText(filing), lowPriReason, false));
                continue;
            }

            var reason = BuildHighPriorityReason(filing.Items, matched, isUnknownShape);

            string? fetched = null;
            try
            {
                fetched = await edgar.GetFilingDocumentAsync(filing, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "GetFilingDocumentAsync threw for {Accession}", filing.AccessionNumber);
            }

            if (!string.IsNullOrWhiteSpace(fetched))
            {
                var text = fetched.Length > MaxFetchedTextLength ? fetched[..MaxFetchedTextLength] : fetched;
                results.Add(new CorporateActionResult(filing, text, reason, true));
            }
            else
            {
                logger.LogDebug("Document fetch failed for {Accession}; falling back to boilerplate", filing.AccessionNumber);
                results.Add(new CorporateActionResult(
                    filing, BuildBoilerplateText(filing), reason + " (document fetch failed; using boilerplate text)", true));
            }
        }

        return results;
    }

    private static string BuildHighPriorityReason(string[]? items, string[] matched, bool isUnknownShape)
    {
        if (isUnknownShape)
            return "8-K item codes unavailable on this filing — fetching full document as a precaution rather than risk a silent gap";

        // 3.03 + 5.03 together is the classic reverse/forward stock split signature.
        if (matched.Contains("3.03") && matched.Contains("5.03"))
        {
            var extras = matched.Where(i => i is not ("3.03" or "5.03")).ToArray();
            var suffix = extras.Length > 0
                ? $" (also: {string.Join(", ", extras.Select(i => $"{i} {Describe(i)}"))})"
                : "";
            return "Item 3.03 + 5.03: material modification to security holder rights + charter amendment " +
                   $"— classic reverse/forward stock split signature{suffix}";
        }

        return $"Item {string.Join(" + ", matched)}: {string.Join("; ", matched.Select(i => $"{i} {Describe(i)}"))}";
    }

    private static string Describe(string item) =>
        ItemDescriptions.TryGetValue(item, out var desc) ? desc : "high-priority corporate event";

    private static string BuildBoilerplateText(EdgarFiling f) =>
        $"SEC {f.FormType} filing by {f.EntityName}, filed {f.FilingDate}. " +
        $"Form type: {f.FormType}. Accession: {f.AccessionNumber}. " +
        "This is a primary SEC filing representing a material event the company is legally required to disclose.";
}
