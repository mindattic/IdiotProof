using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Orchestrates multi-source catalyst ingestion and portent tracking.
/// Sources: SEC EDGAR (Tier 1), USASpending.gov (Tier 1), Alpaca News (Tier 2 —
/// aggregates Benzinga, Reuters, Yahoo Finance, etc.), and manual article paste.
/// Every saved claim is asynchronously scored on 20 financial dimensions and
/// given a 64-bit LSH signature for cross-claim correlation queries.
/// </summary>
public sealed class ResearchService(
    CatalystExtractor                extractor,
    EdgarService                     edgar,
    UsSpendsService                  usSpends,
    AlpacaNewsService                alpacaNews,
    IServiceScopeFactory             scopeFactory,
    IDbContextFactory<AppDbContext>  dbFactory,
    ILogger<ResearchService>         logger)
{
    // ── Public entry points ───────────────────────────────────────────────

    /// <summary>
    /// Analyse a single article or filing. Extracts catalysts + portents via LLM,
    /// persists them, and returns the saved claims.
    /// </summary>
    public async Task<List<ResearchClaim>> AnalyzeArticleAsync(
        string  ticker,
        string  articleText,
        string  sourceName,
        string? sourceUrl,
        int     sourceTier,
        DateOnly articleDate,
        CancellationToken ct = default)
    {
        var extraction = await extractor.ExtractAsync(ticker, articleText, sourceName, ct);
        if (extraction is null || extraction.Catalysts.Count == 0) return [];

        // Allow LLM to downgrade (higher number = worse tier) but never upgrade the caller's claim.
        // e.g. caller says Tier 2, LLM recognises promotional content → Tier 3 wins.
        var effectiveTier = Math.Clamp(Math.Max(sourceTier, extraction.SourceTierSuggestion), 1, 3);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var claims = new List<ResearchClaim>();

        foreach (var c in extraction.Catalysts)
        {
            var claim = new ResearchClaim
            {
                Ticker             = ticker.ToUpperInvariant(),
                SourceName         = sourceName,
                SourceUrl          = sourceUrl,
                SourceTier         = effectiveTier,
                ArticleDate        = articleDate,
                ClaimSummary       = c.Summary,
                ClaimType          = c.Type,
                Sentiment          = c.Sentiment,
                Magnitude          = c.Magnitude,
                HasHappenedAlready = c.HasHappenedAlready,
                PendingTrigger     = c.PendingTrigger,
                ExpectedTimeline   = c.ExpectedTimeline,
                TriggerConfidence  = c.TriggerConfidence,
                Status             = c.HasHappenedAlready ? "Realized" : "Pending",
                LlmAnswer          = extraction.SimpleAnswer,
                RawArticleSnippet  = articleText.Length > 500 ? articleText[..500] : articleText,
            };
            db.ResearchClaims.Add(claim);
            claims.Add(claim);
        }

        await BumpSourceTrustAsync(db, sourceName, effectiveTier, claims.Count, ct);
        await db.SaveChangesAsync(ct);

        // Fire-and-forget: score each claim on 20 dimensions + compute LSH signature.
        // Creates its own DI scope so the Scoped ClaimVectorService is independent
        // of the request scope that's about to be disposed.
        foreach (var c in claims)
        {
            var claimId      = c.Id;
            var claimTicker  = c.Ticker;
            var claimSummary = c.ClaimSummary;
            var claimType    = c.ClaimType;
            var sentiment    = c.Sentiment;
            var magnitude    = c.Magnitude;
            var isPortent    = !c.HasHappenedAlready;

            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope   = scopeFactory.CreateAsyncScope();
                    var vecSvc = scope.ServiceProvider.GetRequiredService<ClaimVectorService>();
                    await vecSvc.ComputeAndSaveAsync(
                        claimId, claimTicker, claimSummary, claimType, sentiment, magnitude, isPortent,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Vector scoring failed for claim {Id}", claimId);
                }
            });
        }

        logger.LogInformation("Research: {Count} claims for {Ticker} from {Source}", claims.Count, ticker, sourceName);
        return claims;
    }

    /// <summary>
    /// Auto-ingest from Tier 1 primary sources (EDGAR 8-K, Form 4, USASpending).
    /// Returns all claims extracted. Pass companyName for USASpending contract search.
    /// </summary>
    public async Task<List<ResearchClaim>> FetchPrimarySourcesAsync(
        string  ticker,
        string? companyName,
        int     daysBack = 30,
        CancellationToken ct = default)
    {
        var all = new List<ResearchClaim>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 8-K material event filings
        foreach (var filing in await edgar.GetRecentFilingsAsync(ticker, "8-K", daysBack, ct))
        {
            var text = BuildEdgarText(filing);
            all.AddRange(await AnalyzeArticleAsync(
                ticker, text, "SEC EDGAR 8-K", filing.BrowseUrl, sourceTier: 1,
                DateOnly.TryParse(filing.FilingDate, out var d) ? d : today, ct));
        }

        // Form 4 insider transactions
        foreach (var filing in await edgar.GetRecentFilingsAsync(ticker, "4", daysBack, ct))
        {
            var text = $"SEC Form 4 (insider transaction) filed {filing.FilingDate} by {filing.EntityName}. " +
                       "A director, officer, or 10%+ shareholder changed their beneficial ownership.";
            all.AddRange(await AnalyzeArticleAsync(
                ticker, text, "SEC EDGAR Form 4", filing.BrowseUrl, sourceTier: 1,
                DateOnly.TryParse(filing.FilingDate, out var d) ? d : today, ct));
        }

        // USASpending.gov government contracts
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            foreach (var award in await usSpends.GetRecentAwardsAsync(companyName, daysBack, ct))
            {
                var text = $"Government contract award to {award.RecipientName}: " +
                           $"{award.Description ?? "See award record"}. " +
                           $"Amount: ${award.Amount:N0}. Award ID: {award.AwardId}. Date: {award.Date}.";
                var awardUrl = string.IsNullOrEmpty(award.AwardId)
                    ? "https://www.usaspending.gov/search"
                    : $"https://www.usaspending.gov/award/{award.AwardId}/";
                all.AddRange(await AnalyzeArticleAsync(
                    ticker, text, "USASpending.gov", awardUrl, sourceTier: 1,
                    DateOnly.TryParse(award.Date, out var d) ? d : today, ct));
            }
        }

        // Alpaca News — aggregates Benzinga, Reuters, Yahoo Finance and others.
        // Tier 2 (editorial) by default; the LLM may downgrade promotional items.
        foreach (var article in await alpacaNews.GetNewsAsync(ticker, daysBack, ct))
        {
            var text = string.IsNullOrWhiteSpace(article.Summary)
                ? article.Headline
                : $"{article.Headline}\n\n{article.Summary}";
            var articleDate = DateOnly.FromDateTime(article.PublishedAt);
            all.AddRange(await AnalyzeArticleAsync(
                ticker, text, article.Source, article.Url, sourceTier: 2, articleDate, ct));
        }

        return all;
    }

    // ── Queries ───────────────────────────────────────────────────────────

    public async Task<List<ResearchClaim>> GetPortentsAsync(string ticker, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ResearchClaims
            .Where(c => c.Ticker == ticker.ToUpperInvariant() && !c.HasHappenedAlready && c.Status == "Pending")
            .OrderByDescending(c => c.CreatedUtc)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<List<ResearchClaim>> GetClaimsAsync(string ticker, int days = 90, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = DateTime.UtcNow.AddDays(-days);
        return await db.ResearchClaims
            .Where(c => c.Ticker == ticker.ToUpperInvariant() && c.CreatedUtc >= since)
            .OrderByDescending(c => c.CreatedUtc)
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<List<SourceTrustScore>> GetTopSourcesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SourceTrustScores
            .Where(s => s.TotalClaims >= 2)
            .OrderByDescending(s => s.TotalClaims)
            .Take(30)
            .ToListAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string BuildEdgarText(EdgarFiling f) =>
        $"SEC {f.FormType} filing by {f.EntityName}, filed {f.FilingDate}. " +
        $"Form type: {f.FormType}. Accession: {f.AccessionNumber}. " +
        "This is a primary SEC filing representing a material event the company is legally required to disclose.";

    private static async Task BumpSourceTrustAsync(
        AppDbContext db, string sourceName, int tier, int claimCount, CancellationToken ct = default)
    {
        var score = await db.SourceTrustScores.FindAsync([sourceName], ct);
        if (score is null)
        {
            score = new SourceTrustScore { SourceName = sourceName, SourceTier = tier };
            db.SourceTrustScores.Add(score);
        }
        score.TotalClaims += claimCount;
        score.LastUpdated  = DateTime.UtcNow;
    }
}
