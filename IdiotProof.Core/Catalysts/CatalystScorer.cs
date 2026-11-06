using System.Globalization;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// Every component of one catalyst score:
/// <c>Score = clamp((TypePrior × Magnitude × SourceWeight + NoveltyBonus) × Freshness, 0, 100)</c>.
/// </summary>
public sealed record CatalystScoreBreakdown(
    CatalystPreClass PreClass,
    double TypePrior,
    string MagnitudeLabel,
    double Magnitude,
    int SourceTier,
    double SourceWeight,
    int NoveltyCount,
    double NoveltyBonus,
    double AgeMinutes,
    double Freshness,
    double Score,
    int Direction)
{
    public string Describe() => string.Format(CultureInfo.InvariantCulture,
        "({0:0.#} [{1}] × {2:0.##} [{3}] × {4:0.##} [tier {5}] + {6:0.##} [novelty {7}]) × {8:0.###} [{9:0} min] = {10:0.#}, direction {11:+#;-#;0}",
        TypePrior, PreClass, Magnitude, MagnitudeLabel, SourceWeight, SourceTier, NoveltyBonus, NoveltyCount, Freshness, AgeMinutes, Score, Direction);
}

/// <summary>
/// The pure 0-100 catalyst score (IP-T443) with weights from <c>catalyst-scoring.json</c>.
/// Direction is carried beside the score, never folded into it, so a dilution event scores high
/// and bearish rather than low.
/// </summary>
public static class CatalystScorer
{
    public static (double Score, int Direction) Score(CatalystPreClass preClass, int direction, string? llmMagnitude,
        int sourceTier, int noveltyCount, double ageMinutes) =>
        Score(CatalystCatalogs.ScoringWeights, preClass, direction, llmMagnitude, sourceTier, noveltyCount, ageMinutes);

    public static (double Score, int Direction) Score(CatalystScoringWeights w, CatalystPreClass preClass, int direction,
        string? llmMagnitude, int sourceTier, int noveltyCount, double ageMinutes)
    {
        var b = Explain(w, preClass, direction, llmMagnitude, sourceTier, noveltyCount, ageMinutes);
        return (b.Score, b.Direction);
    }

    public static CatalystScoreBreakdown Explain(CatalystPreClass preClass, int direction, string? llmMagnitude,
        int sourceTier, int noveltyCount, double ageMinutes) =>
        Explain(CatalystCatalogs.ScoringWeights, preClass, direction, llmMagnitude, sourceTier, noveltyCount, ageMinutes);

    public static CatalystScoreBreakdown Explain(CatalystScoringWeights w, CatalystPreClass preClass, int direction,
        string? llmMagnitude, int sourceTier, int noveltyCount, double ageMinutes)
    {
        var typePrior = TypePrior(w, preClass);
        var label = string.IsNullOrWhiteSpace(llmMagnitude) ? "Unknown" : llmMagnitude.Trim();
        var magnitude = Magnitude(w, label);
        var sourceWeight = SourceWeight(w, sourceTier);
        var novelty = NoveltyBonus(w, noveltyCount);
        var freshness = Freshness(w, ageMinutes);
        var score = Combine(typePrior, magnitude, sourceWeight, novelty, freshness);
        return new(preClass, typePrior, label, magnitude, sourceTier, sourceWeight, noveltyCount, novelty,
            Math.Max(0, ageMinutes), freshness, score, Math.Sign(direction));
    }

    public static double Combine(double typePrior, double magnitude, double sourceWeight, double noveltyBonus, double freshness) =>
        Math.Clamp((typePrior * magnitude * sourceWeight + noveltyBonus) * freshness, 0, 100);

    public static double TypePrior(CatalystScoringWeights w, CatalystPreClass preClass) =>
        w.TypePriors.TryGetValue(preClass.ToString(), out var p) ? p
        : w.TypePriors.TryGetValue(nameof(CatalystPreClass.Other), out var other) ? other : 0;

    public static double Magnitude(CatalystScoringWeights w, string label) =>
        w.Magnitude.TryGetValue(label, out var m) ? m
        : w.Magnitude.TryGetValue("Unknown", out var unknown) ? unknown : 0.6;

    public static double SourceWeight(CatalystScoringWeights w, int sourceTier) =>
        w.SourceWeights.TryGetValue(sourceTier.ToString(CultureInfo.InvariantCulture), out var s) ? s
        : w.SourceWeights.Count == 0 ? 1.0 : w.SourceWeights.Values.Min();

    /// <summary>A quiet name's first item earns the full bonus; the bonus shrinks with every earlier item in the window.</summary>
    public static double NoveltyBonus(CatalystScoringWeights w, int noveltyCount) =>
        w.NoveltyBonusMax / (1 + Math.Max(0, noveltyCount));

    /// <summary>Exponential decay with the catalog's half-life, floored so an old event still ranks above none.</summary>
    public static double Freshness(CatalystScoringWeights w, double ageMinutes)
    {
        if (w.FreshnessHalfLifeMinutes <= 0) return 1.0;
        var decay = Math.Pow(2, -Math.Max(0, ageMinutes) / w.FreshnessHalfLifeMinutes);
        return Math.Max(Math.Clamp(w.FreshnessFloor, 0, 1), decay);
    }
}
