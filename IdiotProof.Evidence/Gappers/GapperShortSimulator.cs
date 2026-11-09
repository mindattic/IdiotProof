using System.Globalization;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>How a short entry handles Rule 201 when it is in force on D (methods-execution.md 5).</summary>
public enum ShortSsrHandling
{
    /// <summary>SSR is ignored (reference only: a real short under SSR cannot hit the bid).</summary>
    Ignore,
    /// <summary>Under SSR, enter at the open of the first bar from the decision whose open is above the prior bar's close (an uptick), within the wait window.</summary>
    Uptick,
    /// <summary>Under SSR, no trade.</summary>
    Skip,
}

/// <summary>
/// One short exit: cover at the open of the first bar at or after <see cref="CoverByEt"/>; optionally a stop at entry x (1 + s)
/// that fills at the worse of the stop and the bar's open; optionally a VWAP reclaim (a one-minute close above the 04:00-anchored
/// session VWAP after a close at or below it) that covers at the next bar's open.
/// </summary>
public sealed record GapperShortExit(TimeSpan CoverByEt, double? StopPct, bool VwapReclaim)
{
    public string Name => string.Create(CultureInfo.InvariantCulture,
        $"cover {CoverByEt:hh\\:mm}{(StopPct is { } s ? $", stop +{s:0.#}%" : ", no stop")}{(VwapReclaim ? ", VWAP reclaim" : "")}");
}

/// <param name="ExitReason">stop, vwap-reclaim or cover-by (with "(no later bar: last close)" when the day ran out).</param>
/// <param name="GapFill">The cover filled at a bar open worse than its level: a stop gapped through, or a signal/clock fill after a missing minute (a halt fill).</param>
/// <param name="GrossReturn">Short return before cost: entry / exit price change, (entry - exit) / entry.</param>
/// <param name="MaxAdverse">Maximum adverse excursion: the highest price traded from the entry bar to the cover (the cover fill included) over entry, minus 1 (0 when it never traded above entry).</param>
public sealed record GapperShortTrade(DateTime EntryUtc, decimal Entry, DateTime ExitUtc, decimal Exit, string ExitReason, double GrossReturn, bool GapFill, double MaxAdverse = 0);

/// <summary>The fade-short signal inputs at the decision time, from bars ending at or before it only.</summary>
/// <param name="PmHighGapPct">Premarket high (bars 04:00-09:29) over the prior close, %.</param>
/// <param name="GapPct">Close as of the decision over the prior close, %.</param>
/// <param name="ExtensionPct">Close as of the decision over the 09:30 open, %.</param>
/// <param name="RthVolume">Shares printed from 09:30 to the decision (the participation base).</param>
/// <param name="CumVolume">Shares printed from 04:00 to the decision (the cost model's ADV term).</param>
public sealed record FadeShortSignal(double PmHighGapPct, double GapPct, double ExtensionPct, decimal Open0930, decimal Close, decimal PmHigh, double RthVolume, double CumVolume);

/// <summary>
/// The costed fade short (IP-T476, explore fade-short): entry, exit and the short-only charges, on minute bars. Pure.
/// Fill rules follow the long simulators': a signal at a minute end fills at the open of the first real bar starting at or
/// after it (a later bar is a halt fill, the first post-halt open); a stop is pessimistic on gaps, filling at the stop or the
/// bar's open, whichever is higher.
/// </summary>
public static class GapperShortSimulator
{
    /// <summary>How long an SSR uptick entry waits after the decision before giving up.</summary>
    public const int UptickWaitMinutes = 30;
    /// <summary>Borrow fees accrue on an actual/360 basis; an intraday short is charged one day.</summary>
    public const int BorrowDayCount = 360;

    /// <summary>
    /// The signal inputs at <paramref name="decisionUtc"/> from bars ending by it; null without a 09:30 bar, a premarket bar or a
    /// prior close. The premarket high is the high of bars starting 04:00-09:29 ET.
    /// </summary>
    public static FadeShortSignal? Signal(IReadOnlyList<Candle> dayBars, DateOnly dateEt, decimal prevClose, DateTime decisionUtc)
    {
        if (prevClose <= 0) return null;
        var start = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        var open = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.RthOpenEt);
        decimal pmHigh = 0, close = 0, open0930 = 0;
        double rthVol = 0, cumVol = 0;
        bool anyPm = false, anyClose = false;
        foreach (var b in dayBars.Where(b => b.Volume > 0 && b.Open > 0).OrderBy(b => b.StartUtc))
        {
            if (b.StartUtc < start || b.EndUtc > decisionUtc) continue;
            cumVol += (double)b.Volume;
            if (b.StartUtc < open) { pmHigh = Math.Max(pmHigh, b.High); anyPm = true; }
            else
            {
                if (open0930 == 0) open0930 = b.Open;
                rthVol += (double)b.Volume;
            }
            close = b.Close;
            anyClose = true;
        }
        if (!anyPm || !anyClose || open0930 <= 0) return null;
        return new FadeShortSignal(
            (double)(pmHigh / prevClose - 1m) * 100, (double)(close / prevClose - 1m) * 100, (double)(close / open0930 - 1m) * 100,
            open0930, close, pmHigh, rthVol, cumVol);
    }

    /// <summary>
    /// The short entry after a decision at <paramref name="decisionUtc"/>: the open of the first real bar starting at or after it.
    /// Under SSR with <see cref="ShortSsrHandling.Uptick"/>, the first such bar within <paramref name="waitMinutes"/> whose open is
    /// above the previous real bar's close; with <see cref="ShortSsrHandling.Skip"/>, none.
    /// </summary>
    public static GapperEntry? Entry(IReadOnlyList<Candle> dayBars, DateTime decisionUtc, bool ssrActive, ShortSsrHandling ssr, int waitMinutes = UptickWaitMinutes)
    {
        var real = dayBars.Where(b => b.Volume > 0 && b.Open > 0).OrderBy(b => b.StartUtc).ToList();
        int first = real.FindIndex(b => b.StartUtc >= decisionUtc);
        if (first < 0) return null;
        if (!ssrActive || ssr == ShortSsrHandling.Ignore) return new GapperEntry(real[first].StartUtc, real[first].Open);
        if (ssr == ShortSsrHandling.Skip) return null;
        var giveUp = decisionUtc.AddMinutes(waitMinutes);
        for (int i = Math.Max(1, first); i < real.Count && real[i].StartUtc < giveUp; i++)
            if (real[i].Open > real[i - 1].Close) return new GapperEntry(real[i].StartUtc, real[i].Open);
        return null;
    }

    /// <summary>Replays one short from its entry to the exit; null when the entry is at or after the cover time.</summary>
    public static GapperShortTrade? Simulate(GapperShortExit exit, IReadOnlyList<Candle> dayBars, DateOnly dateEt, DateTime entryBarStartUtc, decimal entryPrice)
    {
        if (entryPrice <= 0) return null;
        var real = dayBars.Where(b => b.Volume > 0 && b.Open > 0).OrderBy(b => b.StartUtc).ToList();
        var clockUtc = MinuteBarMath.ToUtc(dateEt, exit.CoverByEt);
        if (real.Count == 0 || entryBarStartUtc >= clockUtc) return null;
        decimal? stop = exit.StopPct is { } s ? entryPrice * (1m + (decimal)(s / 100.0)) : null;

        // Session VWAP through each bar (04:00 anchored), and whether the last close before the entry bar sat above it.
        var sessionStart = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        decimal pv = 0, vol = 0;
        bool? above = null;
        for (int i = 0; i < real.Count; i++)
        {
            var b = real[i];
            if (b.StartUtc < sessionStart) continue;
            pv += (b.High + b.Low + b.Close) / 3m * b.Volume;
            vol += b.Volume;
            bool nowAbove = vol > 0 && b.Close > pv / vol;
            if (b.StartUtc < entryBarStartUtc) { above = nowAbove; continue; }
            if (b.StartUtc >= clockUtc) break;
            if (stop is { } st && b.High >= st)
            {
                var fill = Math.Max(st, b.Open);
                return Adverse(Trade(entryBarStartUtc, entryPrice, b.StartUtc, fill, "stop", b.Open > st), real, entryBarStartUtc);
            }
            if (exit.VwapReclaim && nowAbove && above == false)
                return Adverse(Fill(real, b.EndUtc, "vwap-reclaim", entryBarStartUtc, entryPrice), real, entryBarStartUtc);
            above = nowAbove;
        }
        return Adverse(Fill(real, clockUtc, $"cover-by {exit.CoverByEt:hh\\:mm}", entryBarStartUtc, entryPrice), real, entryBarStartUtc);
    }

    /// <summary>
    /// Fills in the maximum adverse excursion: the highest high of the bars the short was open through (the entry bar up to,
    /// not including, the cover bar) and the cover fill itself (a stop fills inside its own bar, at or above the stop).
    /// </summary>
    private static GapperShortTrade Adverse(GapperShortTrade trade, List<Candle> real, DateTime entryUtc)
    {
        decimal high = Math.Max(trade.Entry, trade.Exit);
        foreach (var b in real)
            if (b.StartUtc >= entryUtc && b.StartUtc < trade.ExitUtc) high = Math.Max(high, b.High);
        return trade with { MaxAdverse = trade.Entry > 0 ? (double)(high / trade.Entry - 1m) : 0 };
    }

    /// <summary>Shares after rounding a notional down to whole 100-share lots (Alpaca sells hard-to-borrow locates in round lots); 0 when under one lot.</summary>
    public static long RoundLotShares(double notionalUsd, double price, int lot = 100) =>
        price > 0 && notionalUsd > 0 ? (long)Math.Floor(notionalUsd / price / lot + 1e-9) * lot : 0;

    /// <summary>
    /// A seeded uniform draw in [0, 1) per symbol-day, the same for every configuration, so a locate-failure stress at rate q drops
    /// the same names (draw &lt; q) everywhere and a 60% stress drops a superset of the 30% one.
    /// </summary>
    public static double LocateDraw(string symbol, DateOnly dateEt, int seed = 1337)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL ^ (ulong)seed;
            foreach (char ch in symbol) { h ^= ch; h *= 1099511628211UL; }
            h ^= (ulong)dateEt.DayNumber; h *= 1099511628211UL;
            h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
            return (h >> 11) * (1.0 / (1UL << 53));
        }
    }

    /// <summary>The borrow fee as a fraction of the short's notional: APR x days held / 360, at least one day.</summary>
    public static double BorrowCost(double apr, int daysHeld = 1) => apr * Math.Max(1, daysHeld) / BorrowDayCount;

    /// <summary>
    /// Capital tied up per dollar of short notional (Alpaca, methods-execution.md 5): under $5 the greater of $2.50 per share or
    /// 100%; from $5 the greater of $5.00 per share or the Reg T 50% initial requirement.
    /// </summary>
    public static double MarginFraction(double price)
    {
        if (!(price > 0)) return double.NaN;
        return price < 5 ? Math.Max(2.50 / price, 1.0) : Math.Max(5.00 / price, 0.50);
    }

    private static GapperShortTrade Fill(List<Candle> real, DateTime signalUtc, string reason, DateTime entryUtc, decimal entry)
    {
        var bar = real.FirstOrDefault(b => b.StartUtc >= signalUtc);
        if (bar is null)
        {
            var last = real.LastOrDefault(b => b.StartUtc >= entryUtc) ?? real[^1];
            return Trade(entryUtc, entry, last.StartUtc, last.Close, reason + " (no later bar: last close)", true);
        }
        return Trade(entryUtc, entry, bar.StartUtc, bar.Open, reason, bar.StartUtc > signalUtc);
    }

    private static GapperShortTrade Trade(DateTime entryUtc, decimal entry, DateTime exitUtc, decimal exit, string reason, bool gap) =>
        new(entryUtc, entry, exitUtc, exit, reason, entry > 0 ? (double)(1m - exit / entry) : 0, gap);
}
