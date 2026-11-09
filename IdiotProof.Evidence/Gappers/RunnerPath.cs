namespace IdiotProof.Evidence.Gappers;

/// <param name="Gross">Size-weighted gross return over every leg.</param>
/// <param name="ExitDay">Index into the session list of the session the last leg filled in.</param>
/// <param name="ExitMinute">Grid minute of the bar the last leg filled in.</param>
/// <param name="Reason">0 stop, 1 trail, 2 cooling, 3 sell-by, 4 last close.</param>
public readonly record struct RunnerPathResult(float Gross, int ExitDay, int ExitMinute, byte Reason, float MaxFavorable);

/// <summary>
/// The long ride of <see cref="RunnerIgnition.Simulate"/> over more than one session (IP-T490, explore split-days): the
/// sessions' 04:00-16:00 grids in order, already on one price basis (<see cref="Rebase"/>), a position held overnight
/// from one grid's last bar to the next grid's first. Same fill rules, first hit wins: initial stop, a trailing stop once
/// armed (the highest high of earlier bars, across sessions), an optional half, optional cooling once armed (sold at the
/// next real bar's open, the next session's first bar when the signal is on a session's last bar), and the sell-by minute
/// on <c>lastDay</c>. A stop gapped through overnight fills at the next session's first open. Pure.
/// </summary>
public static class RunnerPath
{
    /// <summary>Puts a grid on another price basis in place: prices times <paramref name="factor"/>, volume divided by it.</summary>
    public static void Rebase(RunnerGrid g, double factor)
    {
        if (factor == 1 || !(factor > 0)) return;
        float f = (float)factor;
        for (int m = 0; m < RunnerGrid.Minutes; m++)
        {
            g.O[m] *= f; g.H[m] *= f; g.L[m] *= f; g.C[m] *= f; g.Cff[m] *= f; g.Vwap[m] *= f;
            g.V[m] /= f;
        }
    }

    /// <summary>
    /// A long bought at <paramref name="entry"/> in session <paramref name="entryDay"/> at <paramref name="entryMinute"/>, held under
    /// <paramref name="plan"/> through session <paramref name="lastDay"/>, where everything still open sells at the plan's sell-by
    /// minute. A null session (no bars) is skipped. Null when the entry is at or after the sell-by minute of the last session.
    /// </summary>
    public static RunnerPathResult? Simulate(IReadOnlyList<RunnerGrid?> days, int entryDay, int entryMinute, double entry, RunnerExitPlan plan, int lastDay)
    {
        if (entry <= 0 || entryDay < 0 || entryDay >= days.Count || lastDay < entryDay) return null;
        lastDay = Math.Min(lastDay, days.Count - 1);
        if (entryDay == lastDay && entryMinute >= plan.SellByMinute) return null;
        double stop = entry * (1 - plan.StopPct / 100), arm = entry * (1 + plan.TrailArmPct / 100), trailK = 1 - plan.TrailPct / 100;
        double? half = plan.HalfPct is { } hp ? entry * (1 + hp / 100) : null;
        double remaining = 1, gross = 0, high = entry;
        bool armed = false, halfDone = false, cooled = false;
        int sellDay = lastDay, sellMinute = plan.SellByMinute;
        byte sellReason = 3;
        int lastSeenDay = -1, lastSeenMinute = -1;
        void Sell(double share, double fill) { gross += share * (fill / entry - 1); remaining -= share; }
        RunnerPathResult Done(int d, int m, byte reason) => new((float)gross, d, m, reason, (float)(high / entry - 1));

        for (int d = entryDay; d <= lastDay; d++)
        {
            var g = days[d];
            if (g is null) continue;
            int start = d == entryDay ? entryMinute : g.First;
            for (int m = Math.Max(0, start); m <= g.Last; m++)
            {
                if (!g.Real(m)) continue;
                lastSeenDay = d; lastSeenMinute = m;
                if (d > sellDay || (d == sellDay && m >= sellMinute)) { Sell(remaining, g.O[m]); return Done(d, m, sellReason); }
                if (!armed && g.L[m] <= stop) { Sell(remaining, Math.Min(stop, g.O[m])); return Done(d, m, 0); }
                if (armed && g.L[m] <= high * trailK) { double tr = high * trailK; Sell(remaining, Math.Min(tr, g.O[m])); return Done(d, m, 1); }
                if (g.H[m] > high) high = g.H[m];
                if (high >= arm) armed = true;
                if (half is { } hl && !halfDone && g.H[m] > hl) { Sell(0.5, hl); halfDone = true; }
                if (armed && plan.Cooling != RunnerCooling.None && !cooled)
                {
                    bool cool = plan.Cooling switch
                    {
                        RunnerCooling.Velocity => m >= 3 && g.Cff[m - 3] is var c0 && !float.IsNaN(c0) && c0 > 0 && (g.C[m] / c0 - 1) * 100 / 3 < 0,
                        RunnerCooling.VwapLoss => !float.IsNaN(g.Vwap[m]) && g.C[m] < g.Vwap[m],
                        _ => false,
                    };
                    if (cool) { cooled = true; sellDay = d; sellMinute = m + 1; sellReason = 2; }
                }
            }
        }
        if (lastSeenDay < 0) return null;
        Sell(remaining, days[lastSeenDay]!.C[lastSeenMinute]);
        return Done(lastSeenDay, lastSeenMinute, 4);
    }
}
