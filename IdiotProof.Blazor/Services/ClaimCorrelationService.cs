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

            // Examples in nearest-first order, not arbitrary DB order
            var claimsById = claims.ToDictionary(c => c.Id);
            var examples = nearIds
                .Take(3)
                .Where(x => claimsById.ContainsKey(x.ClaimId))
                .Select(x => claimsById[x.ClaimId].ClaimSummary)
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

            var idSet   = claimIds.ToHashSet();
            var targets = allVectors.Where(v => idSet.Contains(v.ClaimId)).ToList();

            if (targets.Count == 0) return result;

            // For each target, search ALL vectors except self (not just non-targets).
            // Other portents from the same batch are valid historical neighbours.
            var allNearIds       = new HashSet<Guid>();
            var targetNeighbours = new Dictionary<Guid, List<(Guid ClaimId, int Dist)>>();

            foreach (var t in targets)
            {
                var near = allVectors
                    .Where(v => v.ClaimId != t.ClaimId)   // exclude only self
                    .Select(v => (v.ClaimId, Dist: ClaimVectorService.HammingDistance(t.LshSignature, v.LshSignature)))
                    .Where(x => x.Dist <= maxHamming)
                    .OrderBy(x => x.Dist)
                    .Take(MaxResultClaims)
                    .ToList();

                targetNeighbours[t.ClaimId] = near;
                foreach (var n in near) allNearIds.Add(n.ClaimId);
            }

            if (allNearIds.Count == 0) return result;

            var claims = await db.ResearchClaims
                .Where(c => allNearIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

            foreach (var (targetId, near) in targetNeighbours)
            {
                var neighbourClaims = near
                    .Where(x => claims.ContainsKey(x.ClaimId))
                    .Select(x => (x.Dist, Claim: claims[x.ClaimId]))
                    .ToList();

                var withOutcomes = neighbourClaims.Where(x => x.Claim.OutcomePctChange.HasValue).ToList();
                var bullish  = withOutcomes.Count(x => x.Claim.OutcomePctChange > 0);
                var bearish  = withOutcomes.Count(x => x.Claim.OutcomePctChange < 0);
                var avgChange = withOutcomes.Count > 0
                    ? withOutcomes.Average(x => (double)x.Claim.OutcomePctChange!.Value)
                    : (double?)null;

                // Nearest-first examples (near is already sorted by Dist ascending)
                var examples = neighbourClaims
                    .Take(3)
                    .Select(x => x.Claim.ClaimSummary)
                    .ToList();

                result[targetId] = new CorrelationResult(
                    SimilarCount:         near.Count,
                    WithOutcomes:         withOutcomes.Count,
                    AvgPctChange:         avgChange,
                    BullishCount:         bullish,
                    BearishCount:         bearish,
                    BestHammingDistance:  near.Count > 0 ? near.Min(x => x.Dist) : 64,
                    ExampleSummaries:     examples);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ClaimCorrelation batch failed");
        }

        return result;
    }
}
