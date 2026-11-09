using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// One long exit plan for the alarm ride (IP-T483, explore alarm-ride), first hit wins. Everything still open sells at the open
/// of the first real bar at or after <see cref="SellByEt"/>. Optional legs:
/// a take-profit limit <see cref="TakeProfitPct"/> over entry (filled at the target only when a bar's high trades through it);
/// a stop <see cref="StopPct"/> under entry (filled at the worse of the stop and the bar's open);
/// a trailing stop <see cref="TrailPct"/> under the highest high since entry, armed once a high reached <see cref="TrailActivatePct"/>
/// over entry, which replaces the initial stop once armed;
/// half sold by a limit <see cref="HalfTargetPct"/> over entry (filled at the level only when a bar's high trades through it).
/// Clock signals (a time stop, a velocity death) are passed per trade as a sell time, because they do not depend on the price legs.
/// </summary>
public sealed record GapperLongPlan(string Name, TimeSpan SellByEt, double? TakeProfitPct = null, double? StopPct = null,
    double? TrailActivatePct = null, double? TrailPct = null, double? HalfTargetPct = null);

/// <param name="GrossReturn">Size-weighted long return before cost over every leg: sum of share x (fill / entry - 1).</param>
/// <param name="ExitReason">What closed the last of the position: stop, trail, target, signal (time stop or velocity) or sell-by.</param>
/// <param name="TargetHit">The take-profit filled (or, for a plan without one, the half target filled).</param>
/// <param name="FinalExitUtc">Start of the bar the last leg filled in.</param>
/// <param name="MaxFavorable">Highest high while any of the position was open, over entry, minus 1.</param>
public sealed record GapperLongPathResult(double GrossReturn, string ExitReason, bool TargetHit, DateTime FinalExitUtc, double MaxFavorable);

/// <summary>
/// Replays a long along its minute bars under a <see cref="GapperLongPlan"/>. Pure, conservative fills on minute bars only:
/// inside one bar the adverse legs (stop, trail) are checked before the favourable ones; the trailing level uses the highest
/// high of the bars before the current one; a signal sell fills at the open of the first real bar starting at or after the
/// signal (a later bar is a halt fill); with no bar left the last close is used. On a passive entry's own bar no target can
/// fill (the high may have printed before the limit filled) but the stop can (pessimistic).
/// </summary>
public static class GapperLongPath
{
    /// <summary>Real bars (volume and an open), ordered by start.</summary>
    public static List<Candle> Real(IReadOnlyList<Candle> dayBars) => GapperShortPath.Real(dayBars);

    /// <summary>
    /// A passive limit buy at <paramref name="limit"/>, working from <paramref name="decisionUtc"/> for
    /// <paramref name="windowMinutes"/>: fills on the first bar starting in the window whose low trades at or below the limit,
    /// at the better of the limit and that bar's open. Null when nothing trades there.
    /// </summary>
    public static GapperEntry? PassiveLimit(List<Candle> real, DateTime decisionUtc, decimal limit, int windowMinutes = 5)
    {
        if (limit <= 0) return null;
        var giveUp = decisionUtc.AddMinutes(windowMinutes);
        foreach (var b in real)
        {
            if (b.StartUtc < decisionUtc) continue;
            if (b.StartUtc >= giveUp) return null;
            if (b.Low <= limit) return new GapperEntry(b.StartUtc, Math.Min(b.Open, limit));
        }
        return null;
    }

    /// <summary>
    /// The first minute end after entry, strictly before <paramref name="untilUtc"/>, at which the shared
    /// <see cref="VelocityExitRule"/> says the run died; null when it never does.
    /// </summary>
    public static DateTime? VelocityDeath(List<Candle> real, DateTime entryUtc, decimal entry, DateTime untilUtc, VelocityExitRule rule)
    {
        var since = real.Where(b => b.StartUtc >= entryUtc && b.StartUtc < untilUtc).ToList();
        for (int i = 0; i < since.Count; i++)
        {
            var now = since[i].EndUtc;
            if (now >= untilUtc) break;
            if (rule.Evaluate(since.Take(i + 1).ToList(), entry, now).Exit) return now;
        }
        return null;
    }

    /// <param name="real">The day's real bars from <see cref="Real"/>.</param>
    /// <param name="entryUtc">Start of the entry bar.</param>
    /// <param name="signalUtc">An earlier clock sell (time stop, velocity death); null for none.</param>
    /// <param name="targetsOnEntryBar">False for a passive entry: no target fills inside the entry bar.</param>
    /// <returns>Null when the entry is at or after the sell-by time or the price is not positive.</returns>
    public static GapperLongPathResult? Simulate(GapperLongPlan plan, List<Candle> real, DateOnly dateEt, DateTime entryUtc, decimal entry,
        DateTime? signalUtc = null, bool targetsOnEntryBar = true)
    {
        if (entry <= 0 || real.Count == 0) return null;
        var clockUtc = MinuteBarMath.ToUtc(dateEt, plan.SellByEt);
        if (entryUtc >= clockUtc) return null;
        var sellUtc = signalUtc is { } sig && sig < clockUtc ? sig : clockUtc;
        string sellReason = sellUtc < clockUtc ? "signal" : "sell-by";
        decimal? target = plan.TakeProfitPct is { } tp ? entry * (1m + (decimal)(tp / 100.0)) : null;
        decimal? stop = plan.StopPct is { } sp && sp > 0 ? entry * (1m - (decimal)(sp / 100.0)) : null;
        decimal? arm = plan.TrailActivatePct is { } ta ? entry * (1m + (decimal)(ta / 100.0)) : null;
        decimal? half = plan.HalfTargetPct is { } hp ? entry * (1m + (decimal)(hp / 100.0)) : null;

        double remaining = 1, gross = 0;
        decimal high = entry;
        bool armed = false, halfDone = false, targetHit = false;
        void Sell(double share, decimal fill)
        {
            gross += share * (double)(fill / entry - 1m);
            remaining -= share;
        }

        foreach (var b in real)
        {
            if (b.StartUtc < entryUtc) continue;
            if (b.StartUtc >= sellUtc)
            {
                Sell(remaining, b.Open);
                return Done(sellReason, b.StartUtc);
            }
            bool entryBar = b.StartUtc == entryUtc;
            if (!armed && stop is { } st && b.Low <= st)
            {
                Sell(remaining, Math.Min(st, b.Open));
                return Done("stop", b.StartUtc);
            }
            if (armed && plan.TrailPct is { } tr && high * (1m - (decimal)(tr / 100.0)) is var trail && b.Low <= trail)
            {
                Sell(remaining, Math.Min(trail, b.Open));
                return Done("trail", b.StartUtc);
            }
            if (b.High > high) high = b.High;
            if (arm is { } a && high >= a) armed = true;
            if (entryBar && !targetsOnEntryBar) continue;
            if (half is { } hl && !halfDone && b.High > hl)
            {
                Sell(0.5, hl);
                halfDone = true;
                if (target is null) targetHit = true;
            }
            if (target is { } tl && b.High > tl)
            {
                Sell(remaining, tl);
                targetHit = true;
                return Done("target", b.StartUtc);
            }
        }

        var lastBar = real.LastOrDefault(b => b.StartUtc >= entryUtc) ?? real[^1];
        Sell(remaining, lastBar.Close);
        return Done(sellReason + " (no later bar: last close)", lastBar.StartUtc);

        GapperLongPathResult Done(string reason, DateTime at) => new(gross, reason, targetHit, at, (double)(high / entry - 1m));
    }
}
