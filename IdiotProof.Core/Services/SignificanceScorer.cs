using System.Text.Json;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Computes the 0-100 <see cref="ResearchClaim.SignificanceScore"/> that drives the
/// Research tab's ranked feed. Blends LLM-assessed magnitude/confidence with historical
/// outcome correlation (<see cref="ClaimCorrelationService"/>), source track record
/// (<see cref="SourceTrustScore"/>), watchlist membership, and recency decay so the feed
/// can simply <c>ORDER BY SignificanceScore DESC</c> instead of a manual search box.
/// </summary>
public sealed class SignificanceScorer(
    IDbContextFactory<AppDbContext> dbFactory,
    ClaimCorrelationService correlationSvc,
    ILogger<SignificanceScorer> logger)
{
    // ---- Pure math (independently unit-testable, no DB/network) ----

    public static double MagnitudeScore(string magnitude) => magnitude switch
    {
        "High"   => 100,
        "Medium" => 60,
        "Low"    => 20,
        _        => 20,
    };

    public static double ConfidenceMultiplier(bool hasHappened, string? triggerConfidence)
    {
        if (hasHappened) return 1.0;

        return triggerConfidence switch
        {
            "High"   => 1.0,
            "Medium" => 0.7,
            "Low"    => 0.4,
            _        => 0.85,
        };
    }

    public static double HistoryBonus(int withOutcomes, int bullish, int bearish)
    {
        if (withOutcomes == 0) return 0;

        var consistencyRatio = Math.Abs(bullish - bearish) / (double)withOutcomes;
        return Math.Min(30, withOutcomes * 3) * consistencyRatio;
    }

    public static double SourceBonus(double? confidencePct) =>
        confidencePct is null ? 0 : (confidencePct.Value - 50) / 50 * 10;

    public static double WatchlistBonus(bool isMatch) => isMatch ? 8.0 : 0.0;

    public static double RecencyMultiplier(double daysOld) =>
        Math.Max(0.5, 1.0 - daysOld / 30.0);

    public static double Combine(
        double magnitudeScore,
        double confidenceMultiplier,
        double historyBonus,
        double sourceBonus,
        double watchlistBonus,
        double recencyMultiplier)
    {
        var raw = (magnitudeScore * confidenceMultiplier + historyBonus + sourceBonus + watchlistBonus)
            * recencyMultiplier;
        return Math.Clamp(raw, 0, 100);
    }

    /// <summary>
    /// True if <paramref name="claim"/> touches any symbol in <paramref name="watchlistSymbols"/> —
    /// its own <see cref="ResearchClaim.Ticker"/> for a single-name claim, or any ticker parsed out
    /// of <see cref="ResearchClaim.AffectedTickersJson"/> for a macro claim. Case-insensitive.
    /// </summary>
    internal static bool IsWatchlistMatch(ResearchClaim claim, IReadOnlyCollection<string> watchlistSymbols)
    {
        if (watchlistSymbols.Count == 0) return false;

        var watchlist = watchlistSymbols
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (watchlist.Count == 0) return false;

        if (!claim.IsMacro)
            return !string.IsNullOrWhiteSpace(claim.Ticker) && watchlist.Contains(claim.Ticker);

        foreach (var ticker in ParseAffectedTickers(claim.AffectedTickersJson))
        {
            if (watchlist.Contains(ticker)) return true;
        }

        return false;
    }

    /// <summary>Best-effort JSON string[] parse; tolerates null/malformed input by returning empty.</summary>
    internal static IReadOnlyList<string> ParseAffectedTickers(string? affectedTickersJson)
    {
        if (string.IsNullOrWhiteSpace(affectedTickersJson)) return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(affectedTickersJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ---- Orchestration (DB-touching) ----

    /// <summary>
    /// Loads <paramref name="claimId"/> plus its source trust score and historical
    /// correlation, then computes its significance score. Does not persist.
    /// </summary>
    public async Task<double> ScoreAsync(
        Guid claimId,
        IReadOnlyCollection<string> watchlistSymbols,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var claim = await db.ResearchClaims.FindAsync([claimId], ct)
            ?? throw new InvalidOperationException($"ResearchClaim {claimId} not found");

        var trustScore = await db.SourceTrustScores.FindAsync([claim.SourceName], ct);
        var correlation = await correlationSvc.FindSimilarOutcomesAsync(claimId, ct: ct);

        return ComputeScore(claim, trustScore?.ConfidencePct, correlation, watchlistSymbols);
    }

    /// <summary>
    /// Scores each claim in <paramref name="claimIds"/> and persists
    /// <see cref="ResearchClaim.SignificanceScore"/> in a single batch save. Failures on
    /// an individual claim are logged and skipped rather than aborting the whole batch.
    /// Returns the count of claims successfully scored and updated.
    /// </summary>
    public async Task<int> ScoreAndPersistBatchAsync(
        IEnumerable<Guid> claimIds,
        IReadOnlyCollection<string> watchlistSymbols,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var updated = 0;

        foreach (var claimId in claimIds)
        {
            try
            {
                var claim = await db.ResearchClaims.FindAsync([claimId], ct);
                if (claim is null)
                {
                    logger.LogWarning("SignificanceScorer: claim {Id} not found, skipping", claimId);
                    continue;
                }

                var trustScore = await db.SourceTrustScores.FindAsync([claim.SourceName], ct);
                var correlation = await correlationSvc.FindSimilarOutcomesAsync(claimId, ct: ct);

                claim.SignificanceScore = ComputeScore(claim, trustScore?.ConfidencePct, correlation, watchlistSymbols);
                updated++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SignificanceScorer: failed to score claim {Id}, skipping", claimId);
            }
        }

        await db.SaveChangesAsync(ct);
        return updated;
    }

    private static double ComputeScore(
        ResearchClaim claim,
        double? confidencePct,
        CorrelationResult correlation,
        IReadOnlyCollection<string> watchlistSymbols)
    {
        var magnitudeScore = MagnitudeScore(claim.Magnitude);
        var confidenceMultiplier = ConfidenceMultiplier(claim.HasHappenedAlready, claim.TriggerConfidence);
        var historyBonus = HistoryBonus(correlation.WithOutcomes, correlation.BullishCount, correlation.BearishCount);
        var sourceBonus = SourceBonus(confidencePct);
        var watchlistBonus = WatchlistBonus(IsWatchlistMatch(claim, watchlistSymbols));
        var daysOld = (DateTime.UtcNow - claim.CreatedUtc).TotalDays;
        var recencyMultiplier = RecencyMultiplier(daysOld);

        return Combine(magnitudeScore, confidenceMultiplier, historyBonus, sourceBonus, watchlistBonus, recencyMultiplier);
    }
}
