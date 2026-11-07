using IdiotProof.Evidence.Economics;

namespace IdiotProof.Evidence.Gappers;

/// <summary>The frozen cost model's inputs for one fill (protocol §Cost model).</summary>
public sealed record GapperCostInputs(
    double? CorwinSchultzSpread,
    double PremarketSpreadMultiplier,
    double PmCumVol,
    double? Sigma20,
    double EntryPrice,
    double TradeNotionalUsd,
    double ImpactConstant,
    double RoundTripFloor);

/// <summary>What one round trip costs under the frozen model, every term as a fraction of price.</summary>
public sealed record GapperCostEstimate(double Spread, double ImpactPerSide, double RoundTrip, bool Floored, bool SpreadMissing);

/// <summary>
/// Spread = Corwin-Schultz over the prior days times the once-calibrated premarket multiplier, half
/// paid per side; impact = square-root law per side with the premarket cumulative volume as the ADV
/// and the 20-session daily volatility; round trip = max(spread + 2 x impact, floor). A missing
/// spread estimate contributes nothing (the floor still applies) and is flagged.
/// </summary>
public static class GapperCostModel
{
    public static GapperCostEstimate Estimate(GapperCostInputs c)
    {
        double spread = c.CorwinSchultzSpread is { } cs && cs >= 0 && double.IsFinite(cs) ? cs * c.PremarketSpreadMultiplier : 0;
        double shares = c.EntryPrice > 0 ? c.TradeNotionalUsd / c.EntryPrice : 0;
        double impact = c.Sigma20 is { } sigma && sigma > 0
            ? TradingCosts.SquareRootImpact(shares, c.PmCumVol, sigma, c.ImpactConstant)
            : 0;
        double raw = spread + 2 * impact;
        double roundTrip = Math.Max(raw, c.RoundTripFloor);
        return new GapperCostEstimate(spread, impact, roundTrip, roundTrip > raw, c.CorwinSchultzSpread is null);
    }

    /// <summary>Standard deviation of daily log returns over the last <paramref name="sessions"/> closes (ascending); null with fewer than three closes.</summary>
    public static double? Sigma(IReadOnlyList<decimal> closes, int sessions = 20)
    {
        var list = closes.Where(c => c > 0).TakeLast(sessions + 1).ToList();
        if (list.Count < 3) return null;
        var r = new List<double>(list.Count - 1);
        for (int i = 1; i < list.Count; i++) r.Add(Math.Log((double)list[i] / (double)list[i - 1]));
        return EvidenceStats.StdDev(r);
    }
}
