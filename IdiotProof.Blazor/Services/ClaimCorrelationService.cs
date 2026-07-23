using System.Text.Json;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

public sealed record CorrelationResult(
    int          SimilarCount,
    int          WithOutcomes,
    double?      AvgPctChange,
    int          BullishCount,
    int          BearishCount,
    int          BestHammingDistance,
    List<string> ExampleSummaries);

/// <summary>
/// Finds ResearchClaims whose LSH signatures are close in Hamming space to a
/// target claim's signature, then aggregates their recorded outcomes.
/// This surfaces the answer to: "when signals that look like this one appeared
/// in the past, what happened to the stock?"
///
/// The 64-bit signature from ClaimVectorService encodes a claim's 20-dimensional
/// feature vector; Hamming distance is a fast proxy for cosine similarity in
/// the high-dimensional feature space.
/// </summary>
public sealed class ClaimCorrelationService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<ClaimCorrelationService> logger)
{
    private const int DefaultMaxHamming = 20;  // out of 64 bits — ~70% similarity
    private const int MaxResultClaims   = 20;

    public static readonly CorrelationResult Empty =
        new(0, 0, null, 0, 0, 0, []);

    /// <summary>
    /// Finds claims similar to <paramref name="claimId"/> and aggregates their
    /// recorded outcomes. Returns <see cref="Empty"/> if no vector exists yet
    /// (vector scoring is async and may not have completed).
    /// </summary>
    public async Task<CorrelationResult> FindSimilarOutcomesAsync(
        Guid claimId,
        int  maxHamming = DefaultMaxHamming,
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var target = await db.ResearchClaimVectors.FindAsync([claimId], ct);
            if (target is null) return Empty;

            var allVectors = await db.ResearchClaimVectors
                .Where(v => v.ClaimId != claimId)
                .Select(v => new { v.ClaimId, v.LshSignature })
                .ToListAsync(ct);

            if (allVectors.Count == 0) return Empty;

            var targetSig = target.LshSignature;
            var nearIds = allVectors
                .Select(v => (v.ClaimId, Dist: ClaimVectorService.HammingDistance(targetSig, v.LshSignature)))
                .Where(x => x.Dist <= maxHamming)
                .OrderBy(x => x.Dist)
                .Take(MaxResultClaims)
                .ToList();

            if (nearIds.Count == 0) return Empty;

            var ids    = nearIds.Select(x => x.ClaimId).ToHashSet();
            var claims = await db.ResearchClaims
                .Where(c => ids.Contains(c.Id))
                .ToListAsync(ct);

            var withOutcomes = claims.Where(c => c.OutcomePctChange.HasValue).ToList();
            var bullish  = withOutcomes.Count(c => c.OutcomePctChange > 0);
            var bearish  = withOutcomes.Count(c => c.OutcomePctChange < 0);
            var avgChange = withOutcomes.Count > 0
                ? withOutcomes.Average(c => (double)c.OutcomePctChange!.Value)
                : (double?)null;

            var examples = claims
                .Take(3)
                .Select(c => c.ClaimSummary)
                .ToList();

            return new CorrelationResult(
                SimilarCount:         nearIds.Count,
                WithOutcomes:         withOutcomes.Count,
                AvgPctChange:         avgChange,
                BullishCount:         bullish,
                BearishCount:         bearish,
                BestHammingDistance:  nearIds.Min(x => x.Dist),
                ExampleSummaries:     examples);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ClaimCorrelation failed for claim {Id}", claimId);
            return Empty;
        }
    }

    /// <summary>
    /// Batch variant: returns a dictionary of claimId → correlation for a set of claims.
    /// Uses a single DB round-trip for all vectors, then filters per claim.
    /// </summary>
    public async Task<Dictionary<Guid, CorrelationResult>> FindSimilarOutcomesBatchAsync(
        IReadOnlyList<Guid> claimIds,
        int maxHamming = DefaultMaxHamming,
        CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, CorrelationResult>();
        if (claimIds.Count == 0) return result;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var allVectors = await db.ResearchClaimVectors
                .Select(v => new { v.ClaimId, v.LshSignature })
                .ToListAsync(ct);

            if (allVectors.Count == 0) return result;

            var idSet    = claimIds.ToHashSet();
            var targets  = allVectors.Where(v => idSet.Contains(v.ClaimId)).ToList();
            var others   = allVectors.Where(v => !idSet.Contains(v.ClaimId)).ToList();

            if (targets.Count == 0) return result;

            // Find near-neighbours for every target
            var allNearIds = new HashSet<Guid>();
            var targetNeighbours = new Dictionary<Guid, List<Guid>>();

            foreach (var t in targets)
            {
                var near = others
                    .Select(v => (v.ClaimId, Dist: ClaimVectorService.HammingDistance(t.LshSignature, v.LshSignature)))
                    .Where(x => x.Dist <= maxHamming)
                    .OrderBy(x => x.Dist)
                    .Take(MaxResultClaims)
                    .ToList();

                targetNeighbours[t.ClaimId] = near.Select(x => x.ClaimId).ToList();
                foreach (var n in near) allNearIds.Add(n.ClaimId);
            }

            if (allNearIds.Count == 0) return result;

            var claims = await db.ResearchClaims
                .Where(c => allNearIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

            foreach (var (targetId, nearList) in targetNeighbours)
            {
                var neighbourClaims = nearList
                    .Where(id => claims.ContainsKey(id))
                    .Select(id => claims[id])
                    .ToList();

                var withOutcomes = neighbourClaims.Where(c => c.OutcomePctChange.HasValue).ToList();
                var bullish  = withOutcomes.Count(c => c.OutcomePctChange > 0);
                var bearish  = withOutcomes.Count(c => c.OutcomePctChange < 0);
                var avgChange = withOutcomes.Count > 0
                    ? withOutcomes.Average(c => (double)c.OutcomePctChange!.Value)
                    : (double?)null;

                // Recompute best Hamming distance for this target
                var targetSig = allVectors.First(v => v.ClaimId == targetId).LshSignature;
                var bestDist  = others
                    .Where(v => nearList.Contains(v.ClaimId))
                    .Select(v => ClaimVectorService.HammingDistance(targetSig, v.LshSignature))
                    .DefaultIfEmpty(64)
                    .Min();

                result[targetId] = new CorrelationResult(
                    SimilarCount:         nearList.Count,
                    WithOutcomes:         withOutcomes.Count,
                    AvgPctChange:         avgChange,
                    BullishCount:         bullish,
                    BearishCount:         bearish,
                    BestHammingDistance:  bestDist,
                    ExampleSummaries:     neighbourClaims.Take(3).Select(c => c.ClaimSummary).ToList());
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ClaimCorrelation batch failed");
        }

        return result;
    }
}
