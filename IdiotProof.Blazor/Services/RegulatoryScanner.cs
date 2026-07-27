using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Polls the Federal Register's public API for SEC "Self-Regulatory Organizations"
/// notices — exchange rule filings (Nasdaq/NYSE listing-standard changes, fee
/// schedules, etc.) that aren't about any single company, so they never show up
/// in the per-ticker EDGAR sweep. Most SRO notices are routine fee-schedule
/// paperwork; an LLM triage step drops the non-substantive ones rather than
/// flooding the feed. Substantive rule changes persist as macro
/// <see cref="ResearchClaim"/> rows (<c>IsMacro = true</c>, <c>Ticker = ""</c>)
/// with a best-effort affected-ticker list derived from <see cref="TrackedTicker"/>
/// market value — honest about the gap when that data isn't populated yet,
/// rather than fabricating a ticker list.
/// </summary>
public sealed class RegulatoryScanner(
    LegionClient legion,
    AppSettings appSettings,
    IHttpClientFactory httpFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<RegulatoryScanner> logger)
{
    private const string DocumentsUrl = "https://www.federalregister.gov/api/v1/documents.json";

    private const string SystemPrompt = """
        You are a financial intelligence analyst reviewing SEC Federal Register notices
        about stock exchange rule changes (Self-Regulatory Organization filings). Most of
        these are routine (fee schedule tweaks, minor procedural amendments) and should be
        marked NOT substantive. A notice IS substantive when it changes a listing standard,
        trading rule, or other requirement that could move prices or force delistings for a
        meaningful set of issuers — e.g. a continued-listing market-value/price threshold,
        a new disclosure requirement, a trading-halt rule change.

        TONE — sober equity-research desk, not a press release: state facts plainly, no
        hype, no clickbait. "mechanism" must state the causal link plainly: why this rule
        change could move affected issuers' prices, and through what mechanism (forced
        delisting, compliance cost, trading-halt risk, etc.).

        Respond ONLY with valid JSON matching this schema exactly:
        {
          "is_substantive": true,
          "summary": "concise description of what changed, under 160 chars",
          "mechanism": "plain statement of why/how this affects issuer prices",
          "affected_description": "e.g. 'Nasdaq Capital Market issuers' or 'all NYSE-listed companies'",
          "exchange": "Nasdaq|NYSE|Other|null",
          "threshold_value": 5000000,
          "threshold_description": "e.g. 'Market Value of Listed Securities (MVLS)', null if no numeric threshold",
          "expected_timeline": "e.g. 'immediate', 'phased over 12 months', null if unclear",
          "sentiment": "Bullish|Bearish|Neutral",
          "magnitude": "High|Medium|Low"
        }

        If not substantive, set is_substantive to false and leave other fields as best-effort
        or null — the caller will discard non-substantive notices entirely.
        Return ONLY the JSON object. No markdown, no commentary.
        """;

    private sealed record FederalRegisterDoc(
        string Title, string DocumentNumber, string HtmlUrl, string? Excerpts, DateOnly PublicationDate);

    private sealed record RuleAssessment(
        bool IsSubstantive, string Summary, string Mechanism, string AffectedDescription,
        string? Exchange, decimal? ThresholdValue, string? ThresholdDescription,
        string? ExpectedTimeline, string Sentiment, string Magnitude);

    /// <summary>
    /// Fetches SRO notices published since <paramref name="sinceUtc"/>, triages each
    /// through the LLM, and persists the substantive ones as macro claims. Already-seen
    /// notices (by <see cref="ResearchClaim.SourceUrl"/>) are skipped. Returns the count
    /// of new claims persisted. Never throws — failures are logged and treated as zero
    /// new claims so one bad pass doesn't abort the caller's scan.
    /// </summary>
    public async Task<int> ScanAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        List<FederalRegisterDoc> docs;
        try
        {
            docs = await FetchNoticesAsync(sinceUtc, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RegulatoryScanner: Federal Register fetch failed");
            return 0;
        }

        if (docs.Count == 0) return 0;

        var persisted = 0;
        foreach (var doc in docs)
        {
            try
            {
                if (await AlreadySeenAsync(doc.HtmlUrl, ct)) continue;

                var assessment = await AssessAsync(doc, ct);
                if (assessment is null || !assessment.IsSubstantive) continue;

                await PersistAsync(doc, assessment, ct);
                persisted++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RegulatoryScanner: failed processing notice {DocNumber}", doc.DocumentNumber);
            }
        }

        return persisted;
    }

    private async Task<bool> AlreadySeenAsync(string htmlUrl, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ResearchClaims.AnyAsync(c => c.IsMacro && c.SourceUrl == htmlUrl, ct);
    }

    private async Task<List<FederalRegisterDoc>> FetchNoticesAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(sinceUtc);
        var url = $"{DocumentsUrl}?conditions%5Bagencies%5D%5B%5D=securities-and-exchange-commission" +
                  "&conditions%5Bterm%5D=Self-Regulatory+Organizations" +
                  "&conditions%5Btype%5D%5B%5D=NOTICE" +
                  "&order=newest&per_page=40";

        var http = httpFactory.CreateClient();
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Federal Register returned {Status}", resp.StatusCode);
            return [];
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var docs = new List<FederalRegisterDoc>();
        foreach (var item in results.EnumerateArray())
        {
            var title = Str(item, "title");
            // Safety net beyond the query's own term/agency filter — only true SRO notices.
            if (!title.StartsWith("Self-Regulatory Organizations", StringComparison.OrdinalIgnoreCase)) continue;

            var pubDateStr = Str(item, "publication_date");
            if (!DateOnly.TryParse(pubDateStr, out var pubDate) || pubDate < since) continue;

            docs.Add(new FederalRegisterDoc(
                Title: title,
                DocumentNumber: Str(item, "document_number"),
                HtmlUrl: Str(item, "html_url"),
                Excerpts: NullStr(item, "excerpts"),
                PublicationDate: pubDate));
        }

        return docs;
    }

    private async Task<RuleAssessment?> AssessAsync(FederalRegisterDoc doc, CancellationToken ct)
    {
        var userMsg = $"Title: {doc.Title}\nDocument: {doc.DocumentNumber}\nPublished: {doc.PublicationDate}\n\n" +
                      $"Excerpt:\n{doc.Excerpts ?? "(no excerpt available)"}";

        try
        {
            var raw = await legion.CallAsync(
                providerId:   "claude-api",
                apiKey:       appSettings.ClaudeApiKey,
                model:        "claude-haiku-4-5-20251001",
                systemPrompt: SystemPrompt,
                userMessage:  userMsg,
                maxTokens:    1000,
                temperature:  0.2,
                ct:           ct);

            var json  = raw.Trim();
            var start = json.IndexOf('{');
            var end   = json.LastIndexOf('}');
            if (start < 0 || end < 0) return null;
            json = json[start..(end + 1)];

            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;

            var isSubstantive = root.TryGetProperty("is_substantive", out var s) && s.ValueKind == JsonValueKind.True;
            if (!isSubstantive) return new RuleAssessment(false, "", "", "", null, null, null, null, "Neutral", "Low");

            decimal? threshold = null;
            if (root.TryGetProperty("threshold_value", out var tv) && tv.ValueKind == JsonValueKind.Number)
                threshold = tv.GetDecimal();

            return new RuleAssessment(
                IsSubstantive:        true,
                Summary:              Str(root, "summary"),
                Mechanism:            Str(root, "mechanism"),
                AffectedDescription:  Str(root, "affected_description", "market-wide"),
                Exchange:             NullStr(root, "exchange"),
                ThresholdValue:       threshold,
                ThresholdDescription: NullStr(root, "threshold_description"),
                ExpectedTimeline:     NullStr(root, "expected_timeline"),
                Sentiment:            Str(root, "sentiment", "Neutral"),
                Magnitude:            Str(root, "magnitude", "Low"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RegulatoryScanner: LLM assessment failed for {DocNumber}", doc.DocumentNumber);
            return null;
        }
    }

    private async Task PersistAsync(FederalRegisterDoc doc, RuleAssessment a, CancellationToken ct)
    {
        var affectedTickers = await FindAffectedTickersAsync(a, ct);
        var affectedJson = affectedTickers.Count > 0
            ? JsonSerializer.Serialize(affectedTickers)
            : null;

        // Honest about the gap rather than a fabricated ticker list — SharesOutstanding
        // is a documented future enhancement (see TickerUniverseService), so a threshold
        // screen usually can't resolve real tickers yet.
        var affectsClause = affectedTickers.Count > 0
            ? $"{affectedTickers.Count} tracked tickers ({a.AffectedDescription})"
            : a.ThresholdValue.HasValue
                ? $"{a.AffectedDescription} — full enumeration requires shares-outstanding data not yet populated"
                : a.AffectedDescription;

        var timing = string.IsNullOrWhiteSpace(a.ExpectedTimeline) ? "already priced in" : a.ExpectedTimeline;
        var composed = $"{a.Summary}. Affects {affectsClause} because {a.Mechanism}. Expected impact: {timing}.";

        var claim = new ResearchClaim
        {
            Ticker             = "",
            IsMacro            = true,
            AffectedTickersJson = affectedJson,
            SourceName         = "Federal Register / SEC",
            SourceUrl          = doc.HtmlUrl,
            SourceTier         = 1,
            ArticleDate        = doc.PublicationDate,
            ClaimSummary       = a.Summary,
            ClaimType          = "Regulatory",
            Sentiment          = a.Sentiment,
            Magnitude          = a.Magnitude,
            HasHappenedAlready = true, // an approved/published rule notice has already occurred
            ExpectedTimeline   = a.ExpectedTimeline,
            Status             = "Realized",
            LlmAnswer          = composed,
            RawArticleSnippet  = doc.Excerpts,
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ResearchClaims.Add(claim);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<string>> FindAffectedTickersAsync(RuleAssessment a, CancellationToken ct)
    {
        if (a.ThresholdValue is not { } threshold || string.IsNullOrWhiteSpace(a.Exchange)) return [];
        if (!a.Exchange.Contains("Nasdaq", StringComparison.OrdinalIgnoreCase)
            && !a.Exchange.Contains("NYSE", StringComparison.OrdinalIgnoreCase))
            return [];

        var exchangeCode = a.Exchange.Contains("Nasdaq", StringComparison.OrdinalIgnoreCase) ? "NASDAQ" : "NYSE";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TrackedTickers
            .Where(t => t.Exchange == exchangeCode
                        && t.SharesOutstanding != null
                        && t.LastPrice != null
                        && t.LastPrice.Value * t.SharesOutstanding.Value < threshold)
            .Select(t => t.Symbol)
            .ToListAsync(ct);
    }

    private static string   Str(JsonElement e, string key, string fallback = "")
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static string?  NullStr(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
