namespace IdiotProof.Evidence.Gappers;

/// <summary>Amendment 12's per-session spread multipliers on prior-days Corwin-Schultz; null means that session was rejected.</summary>
public sealed record GapperSessionMultipliers(double? Premarket, double? Regular)
{
    public static readonly TimeSpan PremarketStartEt = new(4, 0, 0);
    public static readonly TimeSpan RegularEndEt = new(16, 0, 0);

    /// <summary>The multiplier for a leg filled at <paramref name="legUtc"/>: premarket 04:00-09:29 ET, regular 09:30-15:59 ET; null outside both or when that session was rejected.</summary>
    public double? For(DateTime legUtc)
    {
        var t = MinuteBarMath.ToEastern(legUtc).TimeOfDay;
        if (t >= PremarketStartEt && t < MinuteBarMath.RthOpenEt) return Premarket;
        if (t >= MinuteBarMath.RthOpenEt && t < RegularEndEt) return Regular;
        return null;
    }

    /// <summary>The round trip's effective multiplier: half the spread paid on each side, so the mean of the two legs' multipliers.</summary>
    public double? RoundTrip(DateTime entryUtc, DateTime exitUtc) =>
        For(entryUtc) is { } entry && For(exitUtc) is { } exit ? (entry + exit) / 2 : null;
}

/// <summary>
/// Amendment 12 (IP-T625): the frozen cost model with its spread multiplier replaced per trade by the mean of the entry
/// and exit sessions' quoted-NBBO multipliers. Only for trading tests registered after amendment 12 that name it.
/// </summary>
public static class GapperQuotedSpread
{
    /// <summary>The A12 cost of a round trip whose legs fill at the given times; null when either leg's session has no multiplier.</summary>
    public static GapperCostEstimate? Estimate(GapperCostInputs inputs, GapperSessionMultipliers multipliers, DateTime entryUtc, DateTime exitUtc) =>
        multipliers.RoundTrip(entryUtc, exitUtc) is { } m ? GapperCostModel.Estimate(inputs with { PremarketSpreadMultiplier = m }) : null;

    /// <summary>
    /// A simulated trade re-priced under A12 once its exit is known (net = gross - A12 round trip). The frozen ladder exits
    /// on price, so the exit itself never changes. Null when either leg's session has no multiplier.
    /// </summary>
    public static GapperRuleTrade? Reprice(GapperRuleTrade trade, GapperCostInputs inputs, GapperSessionMultipliers multipliers) =>
        Estimate(inputs, multipliers, trade.EntryUtc, trade.ExitUtc) is { } cost
            ? trade with { RoundTripCost = cost.RoundTrip, NetReturn = trade.GrossReturn - cost.RoundTrip }
            : null;
}
