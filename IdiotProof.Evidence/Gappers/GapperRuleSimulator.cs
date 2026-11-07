using System.Globalization;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>One cell of the H12 grid: the 08:30 pass thresholds and the exit ladder knobs.</summary>
public sealed record GapperRuleConfig(double PmVolRatioMin, double V3MinPctPerMin, int VelocityDeathWindow, double StopLossPct, TimeSpan SellByEt, bool VwapLossExit)
{
    public string Label => string.Create(CultureInfo.InvariantCulture,
        $"pmVolRatio>={PmVolRatioMin} v3>={V3MinPctPerMin} death{VelocityDeathWindow}m stop{StopLossPct}% sellBy{SellByEt:hh\\:mm} vwap{(VwapLossExit ? "on" : "off")}");

    /// <summary>The pass at the decision time: both thresholds met on the snapshot's own readings; a missing reading never passes.</summary>
    public bool Passes(double? volRatioTod, double? velocity3m) =>
        volRatioTod is { } v && v >= PmVolRatioMin && velocity3m is { } s && s >= V3MinPctPerMin;
}

/// <summary>Exit-rule constants the simulator shares with the frozen labels: the velocity-death arm threshold and bar count, the VWAP run length.</summary>
public sealed record GapperRuleExitParameters(double ArmThresholdPctPerMin = 0.5, int ConsecutiveNegativeBars = 2, int VwapMinConsecutiveAbove = 3);

public sealed record GapperRuleTrade(DateTime EntryUtc, decimal Entry, DateTime ExitUtc, decimal Exit, string ExitReason, double GrossReturn, double RoundTripCost, double NetReturn, bool HaltFill);

/// <summary>
/// Replays one H12 configuration on one symbol-day after the pass: entry at the open of the first bar
/// starting at or after the pass instant (the pass bar is never traded), then the first of: stop
/// (intrabar, at the stop price or the bar's open if it gapped through), the sell-by time, a VWAP loss
/// (close under the 04:00-anchored VWAP after the required run above it, when enabled), or velocity
/// death (the frozen label definition: two consecutive negative v3 readings after v3 exceeded the arm
/// threshold since entry). A signal fills at the open of the next real bar; when that bar is later
/// than the next minute the fill is a halt fill (bar-gap proxy). Pure.
/// </summary>
public static class GapperRuleSimulator
{
    public static GapperRuleTrade? Simulate(GapperRuleConfig config, IReadOnlyList<Candle> dayBars, DateOnly dateEt, DateTime passUtc, double roundTripCost, GapperRuleExitParameters? exits = null)
    {
        var x = exits ?? new GapperRuleExitParameters();
        var real = dayBars.Where(b => b.Volume > 0 && b.Open > 0).OrderBy(b => b.StartUtc).ToList();
        var entryBar = real.FirstOrDefault(b => b.StartUtc >= passUtc);
        if (entryBar is null) return null;
        var sessionStart = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        var sellBy = MinuteBarMath.ToUtc(dateEt, config.SellByEt);
        var close = MinuteBarMath.ToUtc(dateEt, new TimeSpan(16, 0, 0));
        var filled = MinuteBarMath.ForwardFill(dayBars, sessionStart, close);
        decimal entry = entryBar.Open;
        decimal stopPrice = entry * (1m - (decimal)(config.StopLossPct / 100.0));

        decimal pv = 0, vol = 0;
        int above = 0;
        foreach (var b in filled)
        {
            if (b.EndUtc > entryBar.StartUtc) break;
            pv += (b.High + b.Low + b.Close) / 3m * b.Volume;
            vol += b.Volume;
            if (vol > 0) above = b.Close > pv / vol ? above + 1 : b.Close < pv / vol ? 0 : above;
        }

        bool armed = false;
        int negatives = 0;
        foreach (var b in filled)
        {
            if (b.StartUtc < entryBar.StartUtc) continue;
            if (b.StartUtc >= sellBy) return Fill(config, real, b.StartUtc, "sell-by", entry, entryBar.StartUtc, roundTripCost);
            if (b.StartUtc > entryBar.StartUtc && b.Low <= stopPrice && b.Volume > 0)
            {
                var fill = b.Open <= stopPrice ? b.Open : stopPrice;
                return Trade(entry, entryBar.StartUtc, b.StartUtc, fill, "stop", roundTripCost, false);
            }

            pv += (b.High + b.Low + b.Close) / 3m * b.Volume;
            vol += b.Volume;
            if (vol > 0)
            {
                var vwap = pv / vol;
                if (b.Close < vwap)
                {
                    if (config.VwapLossExit && above >= x.VwapMinConsecutiveAbove && b.StartUtc > entryBar.StartUtc)
                        return Fill(config, real, b.EndUtc, "vwap-loss", entry, entryBar.StartUtc, roundTripCost);
                    above = 0;
                }
                else if (b.Close > vwap) above++;
            }

            if (b.StartUtc == entryBar.StartUtc) continue;
            var v = MinuteBarMath.Velocity(filled, b.EndUtc, config.VelocityDeathWindow);
            if (v is null) { negatives = 0; continue; }
            if (v > x.ArmThresholdPctPerMin) armed = true;
            negatives = v < 0 ? negatives + 1 : 0;
            if (armed && negatives >= x.ConsecutiveNegativeBars)
                return Fill(config, real, b.EndUtc, "velocity-death", entry, entryBar.StartUtc, roundTripCost);
        }
        var last = real.LastOrDefault(b => b.StartUtc >= entryBar.StartUtc && b.EndUtc <= close) ?? entryBar;
        return Trade(entry, entryBar.StartUtc, last.StartUtc, last.Close, "session-end", roundTripCost, false);
    }

    private static GapperRuleTrade Fill(GapperRuleConfig config, List<Candle> real, DateTime signalUtc, string reason, decimal entry, DateTime entryUtc, double cost)
    {
        var bar = real.FirstOrDefault(b => b.StartUtc >= signalUtc);
        if (bar is null)
        {
            var last = real.Last(b => b.StartUtc >= entryUtc);
            return Trade(entry, entryUtc, last.StartUtc, last.Close, reason + " (no later bar: last close)", cost, true);
        }
        bool halt = bar.StartUtc > signalUtc;
        return Trade(entry, entryUtc, bar.StartUtc, bar.Open, reason, cost, halt);
    }

    private static GapperRuleTrade Trade(decimal entry, DateTime entryUtc, DateTime exitUtc, decimal exit, string reason, double cost, bool halt)
    {
        double gross = entry > 0 ? (double)(exit / entry - 1m) : 0;
        return new GapperRuleTrade(entryUtc, entry, exitUtc, exit, reason, gross, cost, gross - cost, halt);
    }
}
