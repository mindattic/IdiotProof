using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Closes the loop the rest of the research pipeline assumes exists: for every claim old
/// enough that its predicted price impact has had time to play out, looks up what the stock
/// actually did and records it (<see cref="ResearchClaim.PriceAtClaim"/>/<see cref="ResearchClaim.PriceAtOutcome"/>/
/// <see cref="ResearchClaim.OutcomePctChange"/>). Without this, <see cref="ClaimCorrelationService"/>'s
/// historical-outcome matching and <see cref="SourceTrustScore.ConfidencePct"/> have nothing to
/// read — every claim's outcome fields stay null forever and <see cref="SignificanceScorer"/>'s
/// history/source bonuses silently sit at zero. This is what actually tests whether "the news
/// and the price are inter-related" instead of just assuming it.
/// </summary>
public sealed class OutcomeBackfillService(
    IHttpClientFactory httpFactory,
    AppSettings settings,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<OutcomeBackfillService> logger)
{
    private const string DataBase = "https://data.alpaca.markets/";

    /// <summary>One bar's date + closing price — all this service needs from a bar.</summary>
    private sealed record DailyClose(DateOnly Date, decimal Close);

    /// <summary>
    /// Backfills outcomes for claims old enough that <paramref name="outcomeWindowDays"/> has
    /// elapsed since <see cref="ResearchClaim.ArticleDate"/>. Macro claims (no single ticker) are
    /// skipped. Claims whose outcome window hasn't fully elapsed yet, or whose ticker has no
    /// trading data covering the window, are left for a future pass rather than guessed at.
    /// Returns the count of claims successfully backfilled.
    /// </summary>
    public async Task<int> BackfillAsync(
        int minWaitDays = 5,
        int outcomeWindowDays = 7,
        int maxCandidates = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId)) return 0;

        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-minWaitDays);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.ResearchClaims
            .Where(c => !c.IsMacro && c.Ticker != "" && c.OutcomeDate == null && c.ArticleDate <= cutoff)
            .OrderBy(c => c.Ticker).ThenBy(c => c.ArticleDate)
            .Take(maxCandidates)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var backfilled = 0;
        var trustDeltas = new Dictionary<string, (int PortentsRealized, int ImmediateCorrect)>();

        foreach (var group in candidates.GroupBy(c => c.Ticker))
        {
            List<DailyClose> bars;
            try
            {
                var earliestArticleDate = group.Min(c => c.ArticleDate);
                bars = await FetchDailyClosesAsync(group.Key, earliestArticleDate, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OutcomeBackfillService: bars fetch failed for {Ticker}", group.Key);
                continue;
            }
            if (bars.Count == 0) continue;

            foreach (var claim in group)
            {
                var outcomeTargetDate = claim.ArticleDate.AddDays(outcomeWindowDays);
                if (outcomeTargetDate > DateOnly.FromDateTime(DateTime.UtcNow)) continue; // window hasn't elapsed yet

                var priceAtClaim   = FindCloseOnOrAfter(bars, claim.ArticleDate);
                var priceAtOutcome = FindCloseOnOrAfter(bars, outcomeTargetDate);
                if (priceAtClaim is null || priceAtOutcome is null) continue; // no trading data covering this window yet

                var pctChange = (priceAtOutcome.Close - priceAtClaim.Close) / priceAtClaim.Close * 100m;

                claim.PriceAtClaim     = priceAtClaim.Close;
                claim.PriceAtOutcome   = priceAtOutcome.Close;
                claim.OutcomeDate      = priceAtOutcome.Date;
                claim.OutcomePctChange = pctChange;

                var directionMatched = claim.Sentiment switch
                {
                    "Bullish" => pctChange > 0.5m,
                    "Bearish" => pctChange < -0.5m,
                    _         => false, // Neutral calls aren't counted toward correctness either way
                };

                if (!claim.HasHappenedAlready)
                {
                    claim.Status = directionMatched ? "Realized" : "Disproven";
                }

                if (claim.Sentiment != "Neutral")
                {
                    var delta = trustDeltas.TryGetValue(claim.SourceName, out var d) ? d : (0, 0);
                    trustDeltas[claim.SourceName] = claim.HasHappenedAlready
                        ? (delta.Item1, delta.Item2 + (directionMatched ? 1 : 0))
                        : (delta.Item1 + (directionMatched ? 1 : 0), delta.Item2);
                }

                backfilled++;
            }
        }

        foreach (var (sourceName, delta) in trustDeltas)
        {
            var trust = await db.SourceTrustScores.FindAsync([sourceName], ct);
            if (trust is null) continue; // ingestion always creates one before a claim can exist; defensive only
            trust.PortentsRealized += delta.PortentsRealized;
            trust.ImmediateCorrect += delta.ImmediateCorrect;
            trust.LastUpdated       = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("OutcomeBackfillService: backfilled {Count}/{Candidates} claims", backfilled, candidates.Count);
        return backfilled;
    }

    private static DailyClose? FindCloseOnOrAfter(List<DailyClose> bars, DateOnly target) =>
        bars.Where(b => b.Date >= target).OrderBy(b => b.Date).FirstOrDefault();

    private async Task<List<DailyClose>> FetchDailyClosesAsync(string ticker, DateOnly start, CancellationToken ct)
    {
        var end = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var url = $"{DataBase}v2/stocks/{Uri.EscapeDataString(ticker)}/bars" +
                  $"?timeframe=1Day&start={start:yyyy-MM-dd}&end={end}&limit=1000&adjustment=split&feed=iex";

        using var client = httpFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("APCA-API-KEY-ID", settings.AlpacaApiKeyId);
        req.Headers.Add("APCA-API-SECRET-KEY", settings.AlpacaApiSecretKey);

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogDebug("Alpaca bars returned {Status} for {Ticker}", resp.StatusCode, ticker);
            return [];
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("bars", out var bars) || bars.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<DailyClose>();
        foreach (var bar in bars.EnumerateArray())
        {
            if (!bar.TryGetProperty("t", out var t) || !bar.TryGetProperty("c", out var c)) continue;
            if (!t.TryGetDateTime(out var dt) || !c.TryGetDecimal(out var close)) continue;
            results.Add(new DailyClose(DateOnly.FromDateTime(dt), close));
        }
        return results;
    }
}
