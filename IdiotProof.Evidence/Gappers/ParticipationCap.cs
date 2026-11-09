namespace IdiotProof.Evidence.Gappers;

/// <summary>How many shares one gapper trade may take, and which limit bound it.</summary>
/// <param name="Shares">Whole shares allowed (0 when skipped).</param>
/// <param name="NotionalUsd">Shares times price.</param>
/// <param name="FullSize">The requested notional fits under both participation limits.</param>
/// <param name="Skip">The capped order is under the minimum notional, so no trade.</param>
/// <param name="Binding">"notional", "recent volume" or "exit window".</param>
public sealed record ParticipationSize(long Shares, double NotionalUsd, bool FullSize, bool Skip, string Binding);

/// <summary>
/// The participation cap (strategy-refined.md 2.3, methods-execution.md 4.1/4.3):
/// <c>shares = floor(min(N / price, p x V5, p x Vexit))</c>, where V5 is the shares traded in the 5 minutes before the
/// order and Vexit the forecast shares in the exit window, from the time-of-day profile:
/// <c>Vexit = cumVol(t) x (F(to) - F(from)) / F(t)</c>. The exit side usually binds: the whole position is sold in a
/// few minutes that every other gapper trader is selling into too. A capped order under <c>minNotionalUsd</c> is
/// skipped. Pure.
/// </summary>
public static class ParticipationCap
{
    public const double DefaultParticipation = 0.05;
    public const double DefaultMinNotionalUsd = 200;

    public static ParticipationSize Size(double price, double notionalUsd, double recentShares, double? exitWindowShares,
        double participation = DefaultParticipation, double minNotionalUsd = DefaultMinNotionalUsd)
    {
        if (participation is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(participation), "Participation must be in (0, 1].");
        if (!(price > 0) || !(notionalUsd > 0)) return new ParticipationSize(0, 0, false, true, "notional");
        double wanted = notionalUsd / price;
        double cap = wanted;
        string binding = "notional";
        double recent = Math.Max(0, recentShares) * participation;
        if (recent < cap) { cap = recent; binding = "recent volume"; }
        if (exitWindowShares is { } exit && double.IsFinite(exit))
        {
            double e = Math.Max(0, exit) * participation;
            if (e < cap) { cap = e; binding = "exit window"; }
        }
        long shares = (long)Math.Floor(cap + 1e-9);
        long full = (long)Math.Floor(wanted + 1e-9);
        double notional = shares * price;
        bool skip = notional < minNotionalUsd;
        return new ParticipationSize(skip ? 0 : shares, skip ? 0 : notional, shares >= full && full > 0, skip, binding);
    }

    /// <summary>Forecast shares in [fromEt, toEt] from the cumulative volume at t and the profile's cumulative fractions; null when F(t) is zero.</summary>
    public static double? ForecastWindowVolume(double cumVolAtT, double fractionAtT, double fractionFrom, double fractionTo)
    {
        if (!(fractionAtT > 0) || !(cumVolAtT >= 0)) return null;
        return cumVolAtT * Math.Max(0, fractionTo - fractionFrom) / fractionAtT;
    }

    public static double? ForecastWindowVolume(TimeOfDayProfile profile, int advDecile, double cumVolAtT, TimeSpan tEt, TimeSpan fromEt, TimeSpan toEt) =>
        ForecastWindowVolume(cumVolAtT, profile.FractionAt(advDecile, tEt), profile.FractionAt(advDecile, fromEt), profile.FractionAt(advDecile, toEt));
}
