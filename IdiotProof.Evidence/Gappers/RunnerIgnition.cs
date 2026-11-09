using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// One symbol-day on a 04:00-16:00 ET minute grid (IP-T484, explore watchlist): index m is the bar starting at
/// 04:00 + m minutes. Missing minutes (no real bar) have NaN prices and zero volume; <see cref="Cff"/> carries the
/// last close forward and <see cref="Vwap"/> the 04:00-anchored session VWAP (typical price) through the end of
/// each minute. Pure.
/// </summary>
public sealed class RunnerGrid
{
    public const int Minutes = 720;
    public static readonly TimeSpan StartEt = new(4, 0, 0);
    /// <summary>Grid index of the 09:30 bar.</summary>
    public const int RthOpen = 330;

    public float[] O { get; } = new float[Minutes];
    public float[] H { get; } = new float[Minutes];
    public float[] L { get; } = new float[Minutes];
    public float[] C { get; } = new float[Minutes];
    public float[] V { get; } = new float[Minutes];
    public float[] Cff { get; } = new float[Minutes];
    public float[] Vwap { get; } = new float[Minutes];
    public int First { get; private set; } = -1;
    public int Last { get; private set; } = -1;

    public bool Real(int m) => m >= 0 && m < Minutes && !float.IsNaN(O[m]);

    public static int MinuteOf(DateTime startUtc) => (int)Math.Floor((MinuteBarMath.ToEastern(startUtc).TimeOfDay - StartEt).TotalMinutes);

    /// <summary>The grid of the day's real bars (volume and a positive open) starting 04:00..15:59 ET; null when there is none.</summary>
    public static RunnerGrid? Build(IReadOnlyList<Candle> bars)
    {
        var g = new RunnerGrid();
        Array.Fill(g.O, float.NaN); Array.Fill(g.H, float.NaN); Array.Fill(g.L, float.NaN); Array.Fill(g.C, float.NaN);
        foreach (var b in bars)
        {
            if (b.Volume <= 0 || b.Open <= 0) continue;
            int m = MinuteOf(b.StartUtc);
            if (m < 0 || m >= Minutes) continue;
            g.O[m] = (float)b.Open; g.H[m] = (float)b.High; g.L[m] = (float)b.Low; g.C[m] = (float)b.Close; g.V[m] = (float)b.Volume;
        }
        double pv = 0, vol = 0;
        float last = float.NaN;
        for (int m = 0; m < Minutes; m++)
        {
            if (!float.IsNaN(g.O[m]))
            {
                if (g.First < 0) g.First = m;
                g.Last = m;
                last = g.C[m];
                pv += (g.H[m] + g.L[m] + g.C[m]) / 3.0 * g.V[m];
                vol += g.V[m];
            }
            g.Cff[m] = last;
            g.Vwap[m] = vol > 0 ? (float)(pv / vol) : float.NaN;
        }
        return g.First < 0 ? null : g;
    }
}

/// <summary>What a day did on the grid: the run, its timing and the top footprint (descriptive, IP-T484).</summary>
/// <param name="Gain">Highest high 04:00-16:00 over the adjusted prior close, minus 1.</param>
/// <param name="Hod">Grid minute of the first bar that printed the day's high.</param>
/// <param name="Fade">Last close over the day's high, minus 1.</param>
/// <param name="FirstGap">First bar's open over the prior close, minus 1 (the 04:00 gap, OMH day 2's +102%).</param>
/// <param name="Climax">Grid minute of the largest-volume bar.</param>
/// <param name="LowerHighAfter">Minutes from the high to the confirmation (two bars each side) of the first lower pivot high after it; -1 none.</param>
/// <param name="VwapLossAfter">Minutes from the high to the first close under the session VWAP; -1 none.</param>
public sealed record RunnerDayFacts(double Gain, int Hod, double Fade, double FirstGap, double PmGain, int[] FirstTouch, int Climax, int LowerHighAfter, int VwapLossAfter, double DayVolume);

/// <summary>An ignition minute (bars ending by its end): close, the gap of the last close before it, volume over the trailing base.</summary>
public readonly record struct RunnerIgnitionEvent(int Minute, float Close, float GapBefore, float VolMultiple);

public enum RunnerCooling { None, Velocity, VwapLoss }

/// <summary>
/// A long ride exit plan, first hit wins: initial stop <see cref="StopPct"/> under entry; once a high reached
/// <see cref="TrailArmPct"/> over entry, a trailing stop <see cref="TrailPct"/> under the highest high of the bars
/// before the current one replaces it; optional half sold at <see cref="HalfPct"/>; optional cooling signal once armed
/// (velocity death: the 3-minute close-to-close velocity under 0 %/min; VWAP loss: a one-minute close under the
/// session VWAP), sold at the next real bar's open; everything out at the open of the first real bar at or after
/// <see cref="SellByMinute"/> (15:55).
/// </summary>
public sealed record RunnerExitPlan(string Name, double StopPct, double TrailArmPct, double TrailPct, double? HalfPct, RunnerCooling Cooling, int SellByMinute = 715);

/// <param name="Gross">Size-weighted gross return over every leg.</param>
/// <param name="ExitMinute">Grid minute of the bar the last leg filled in.</param>
/// <param name="Reason">0 stop, 1 trail, 2 cooling, 3 sell-by, 4 last close.</param>
public readonly record struct RunnerTradeResult(float Gross, int ExitMinute, byte Reason, float MaxFavorable);

/// <summary>Ignition detection, day facts and the long ride on a <see cref="RunnerGrid"/> (IP-T484). Pure.</summary>
public static class RunnerIgnition
{
    public const int TrailingMinutes = 30;
    public const double BreakPct = 0.02;

    public static readonly double[] Levels = [0.5, 1.0, 3.0];

    public static RunnerDayFacts Facts(RunnerGrid g, double prevClose)
    {
        double maxHigh = 0, pmHigh = 0, dayVol = 0;
        int hod = -1, climax = -1;
        float maxVol = -1;
        var touch = new int[Levels.Length];
        Array.Fill(touch, -1);
        for (int m = g.First; m <= g.Last; m++)
        {
            if (!g.Real(m)) continue;
            dayVol += g.V[m];
            if (g.H[m] > maxHigh) { maxHigh = g.H[m]; hod = m; }
            if (m < RunnerGrid.RthOpen && g.H[m] > pmHigh) pmHigh = g.H[m];
            if (g.V[m] > maxVol) { maxVol = g.V[m]; climax = m; }
            for (int k = 0; k < Levels.Length; k++)
                if (touch[k] < 0 && prevClose > 0 && g.H[m] >= prevClose * (1 + Levels[k])) touch[k] = m;
        }
        int lowerHigh = -1, vwapLoss = -1;
        // First lower pivot high after the high: a real bar whose high tops the two real bars on each side.
        var real = new List<int>();
        for (int m = hod + 1; m <= g.Last; m++) if (g.Real(m)) real.Add(m);
        for (int i = 2; i < real.Count - 2 && lowerHigh < 0; i++)
        {
            float h = g.H[real[i]];
            if (h >= maxHigh) continue;
            if (g.H[real[i - 1]] <= h && g.H[real[i - 2]] <= h && g.H[real[i + 1]] <= h && g.H[real[i + 2]] <= h) lowerHigh = real[i + 2] - hod;
        }
        for (int m = hod + 1; m <= g.Last && vwapLoss < 0; m++)
            if (g.Real(m) && g.C[m] < g.Vwap[m]) vwapLoss = m - hod;
        double gain = prevClose > 0 && maxHigh > 0 ? maxHigh / prevClose - 1 : double.NaN;
        double fade = maxHigh > 0 ? g.C[g.Last] / maxHigh - 1 : double.NaN;
        double firstGap = prevClose > 0 ? g.O[g.First] / prevClose - 1 : double.NaN;
        double pmGain = prevClose > 0 && pmHigh > 0 ? pmHigh / prevClose - 1 : double.NaN;
        return new RunnerDayFacts(gain, hod, fade, firstGap, pmGain, touch, climax, lowerHigh, vwapLoss, dayVol);
    }

    /// <summary>
    /// Every ignition minute m (from bars ending by its end): a real bar after the day's first, whose volume is at least
    /// <paramref name="k"/> x max(the trailing 30-clock-minute mean volume, missing minutes as 0; <paramref name="minuteFloor"/>),
    /// whose close is at least 2% over the session high of the bars before it, and whose dollar volume is at least
    /// <paramref name="dollarFloor"/>. Ordered by minute.
    /// </summary>
    public static List<RunnerIgnitionEvent> Ignitions(RunnerGrid g, double prevClose, double k, double minuteFloor, double dollarFloor)
    {
        var list = new List<RunnerIgnitionEvent>();
        double window = 0, hiBefore = double.NaN;
        for (int m = g.First; m <= g.Last; m++)
        {
            if (m - TrailingMinutes - 1 >= 0) window -= g.V[m - TrailingMinutes - 1];
            if (m - 1 >= 0) window += g.V[m - 1];
            if (!g.Real(m)) continue;
            if (!double.IsNaN(hiBefore))
            {
                double baseVol = Math.Max(window / TrailingMinutes, minuteFloor);
                if (g.V[m] >= k * baseVol && g.C[m] >= (1 + BreakPct) * hiBefore && g.C[m] * (double)g.V[m] >= dollarFloor)
                {
                    float prev = g.Cff[m - 1];
                    list.Add(new RunnerIgnitionEvent(m, g.C[m], prevClose > 0 && !float.IsNaN(prev) ? (float)(prev / prevClose - 1) : float.NaN, (float)(g.V[m] / baseVol)));
                }
            }
            hiBefore = double.IsNaN(hiBefore) ? g.H[m] : Math.Max(hiBefore, g.H[m]);
        }
        return list;
    }

    /// <summary>Marketable: the open of the first real bar starting after the ignition minute, within <paramref name="maxWait"/> minutes.</summary>
    public static (int Minute, double Fill)? MarketEntry(RunnerGrid g, int ignition, int maxWait = 30)
    {
        for (int m = ignition + 1; m <= Math.Min(g.Last, ignition + maxWait); m++)
            if (g.Real(m)) return (m, g.O[m]);
        return null;
    }

    /// <summary>A limit <paramref name="discount"/> under the ignition close, working <paramref name="window"/> minutes: the first real bar whose low trades through it, at the better of the limit and its open.</summary>
    public static (int Minute, double Fill)? PassiveEntry(RunnerGrid g, int ignition, double discount = 0.01, int window = 5)
    {
        double limit = g.C[ignition] * (1 - discount);
        for (int m = ignition + 1; m <= Math.Min(g.Last, ignition + window); m++)
            if (g.Real(m) && g.L[m] <= limit) return (m, Math.Min(g.O[m], limit));
        return null;
    }

    /// <summary>
    /// Replays a long from <paramref name="entryMinute"/> at <paramref name="entry"/> under <paramref name="plan"/>. Conservative minute-bar
    /// fills: the adverse legs (stop, trail) are checked before the favourable ones inside a bar; the trail uses the highest high of the
    /// bars before the current one; a stop or trail fills at the worse of its level and the bar's open (a halt or gap through it fills at
    /// the open); the half fills at its level only when a bar's high trades through it, never inside a passive entry's own bar. Null when
    /// the entry is at or after the sell-by minute.
    /// </summary>
    public static RunnerTradeResult? Simulate(RunnerGrid g, int entryMinute, double entry, RunnerExitPlan plan, bool passiveEntry)
    {
        if (entry <= 0 || entryMinute >= plan.SellByMinute) return null;
        double stop = entry * (1 - plan.StopPct / 100), arm = entry * (1 + plan.TrailArmPct / 100), trailK = 1 - plan.TrailPct / 100;
        double? half = plan.HalfPct is { } hp ? entry * (1 + hp / 100) : null;
        double remaining = 1, gross = 0, high = entry;
        bool armed = false, halfDone = false;
        int sellAt = plan.SellByMinute;
        byte sellReason = 3;
        void Sell(double share, double fill) { gross += share * (fill / entry - 1); remaining -= share; }
        RunnerTradeResult Done(int m, byte reason) => new((float)gross, m, reason, (float)(high / entry - 1));

        for (int m = entryMinute; m <= g.Last; m++)
        {
            if (!g.Real(m)) continue;
            if (m >= sellAt) { Sell(remaining, g.O[m]); return Done(m, sellReason); }
            if (!armed && g.L[m] <= stop) { Sell(remaining, Math.Min(stop, g.O[m])); return Done(m, 0); }
            if (armed && g.L[m] <= high * trailK) { double tr = high * trailK; Sell(remaining, Math.Min(tr, g.O[m])); return Done(m, 1); }
            if (g.H[m] > high) high = g.H[m];
            if (high >= arm) armed = true;
            bool targets = !(passiveEntry && m == entryMinute);
            if (targets && half is { } hl && !halfDone && g.H[m] > hl) { Sell(0.5, hl); halfDone = true; }
            if (armed && plan.Cooling != RunnerCooling.None && sellAt == plan.SellByMinute)
            {
                bool cool = plan.Cooling switch
                {
                    RunnerCooling.Velocity => m >= 3 && g.Cff[m - 3] is var c0 && !float.IsNaN(c0) && c0 > 0 && (g.C[m] / c0 - 1) * 100 / 3 < 0,
                    RunnerCooling.VwapLoss => !float.IsNaN(g.Vwap[m]) && g.C[m] < g.Vwap[m],
                    _ => false,
                };
                if (cool) { sellAt = m + 1; sellReason = 2; }
            }
        }
        Sell(remaining, g.C[g.Last]);
        return Done(g.Last, 4);
    }
}
