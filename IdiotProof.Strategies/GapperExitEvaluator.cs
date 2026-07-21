using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies;

/// <summary>
/// Why an open gapper position should be closed right now (or null = keep holding).
/// </summary>
public enum GapperExitReason
{
    /// <summary>Hard SellBy time reached — always flat before the bell.</summary>
    SellByTime,

    /// <summary>
    /// Momentum rollover: price gave back the configured % of the entry→peak run.
    /// </summary>
    PeakGiveback,

    /// <summary>Hard stop: price fell StopLossPercent below entry.</summary>
    StopLoss,

    /// <summary>Trailing stop: price fell TrailingStopPercent below the peak.</summary>
    TrailingStop,

    /// <summary>Primary take-profit target reached.</summary>
    TargetHit
}

/// <summary>The evaluator's verdict for one tick.</summary>
public sealed record GapperExitDecision(GapperExitReason Reason, double CurrentPrice, double PeakPrice, string Detail);

/// <summary>
/// Pure exit logic for a held gapper position (RFC 0002 §D3). Given the
/// strategy definition, the entry fill, and the bars seen since entry, decides
/// whether the position must be sold this tick. No I/O, no clocks — the caller
/// supplies "now" — so the whole sell-off brain is unit-testable.
///
/// Rule order (first match wins):
///   1. SellBy — hard ET flatten time (never hold into the bell).
///   2. StopLossPercent — hard stop below entry.
///   3. TrailingStopPercent — % off the high-water mark, active from entry.
///   4. PeakGiveback — armed from PeakGivebackArmTime (default: immediately);
///      sells once price gives back N% of the run from entry to peak. The
///      giveback is proportional to the run, so the exit self-scales to the
///      gapper's own momentum: a runner that went +40% tolerates a deeper
///      absolute pullback than one that went +4%.
/// </summary>
public static class GapperExitEvaluator
{
    /// <summary>
    /// Evaluates the exit rules for an open long position.
    /// </summary>
    /// <param name="def">Parsed strategy definition (SellBy/PeakGiveback/stops read from here).</param>
    /// <param name="entryPrice">Actual fill price of the entry.</param>
    /// <param name="entryUtc">Fill time (UTC). Bars at or before this instant are ignored.</param>
    /// <param name="candles">Bars covering the period since entry (extra history is fine).</param>
    /// <param name="nowUtc">Evaluation instant (UTC).</param>
    public static GapperExitDecision? Evaluate(
        StrategyDefinition def,
        double entryPrice,
        DateTime entryUtc,
        IReadOnlyList<Candle> candles,
        DateTime nowUtc)
    {
        if (entryPrice <= 0 || candles.Count == 0)
            return null;

        // High-water mark since entry; the entry price is the floor of the peak.
        var peak = entryPrice;
        double current = entryPrice;
        foreach (var c in candles)
        {
            if (c.EndUtc <= entryUtc) continue;
            if ((double)c.High > peak) peak = (double)c.High;
            current = (double)c.Close;
        }

        var nowEt = MarketTime.ToEasternTimeOfDay(nowUtc);

        // 1. Hard sell-by time — never hold into the bell. Also fires when the
        //    position has outlived its entry's ET day entirely: an exit that
        //    kept failing (rejections, market closed) rolls past midnight, and
        //    the plain time-of-day check would then WAIT until the sell-by
        //    time next day (nowEt 04:00 < 09:28) — holding an unwanted
        //    overnight position for hours with only the stop active. A
        //    sell-by position that survived to another day flattens at the
        //    first evaluated instant instead.
        if (def.ExitTime is { } sellBy)
        {
            var heldPastItsDay = EasternDate(nowUtc) > EasternDate(entryUtc);
            if (nowEt >= sellBy || heldPastItsDay)
                return new GapperExitDecision(GapperExitReason.SellByTime, current, peak,
                    heldPastItsDay
                        ? $"Position outlived its {sellBy:hh\\:mm} ET sell-by day — flattening at the first opportunity."
                        : $"Sell-by {sellBy:hh\\:mm} ET reached — flattening before the bell.");
        }

        // 2. Hard stop below entry — percent or absolute, whichever is set.
        if (def.StopLossPercent is { } slPct && current <= entryPrice * (1 - slPct / 100.0))
            return new GapperExitDecision(GapperExitReason.StopLoss, current, peak,
                $"Price {current:F2} breached the {slPct:F1}% hard stop below entry {entryPrice:F2}.");
        if (def.StopLossPrice is { } slPrice && current <= slPrice)
            return new GapperExitDecision(GapperExitReason.StopLoss, current, peak,
                $"Price {current:F2} breached the hard stop at {slPrice:F2}.");

        // 3. Trailing stop off the peak.
        if (def.TrailingStopPercent is { } tslPct && peak > entryPrice
            && current <= peak * (1 - tslPct / 100.0))
            return new GapperExitDecision(GapperExitReason.TrailingStop, current, peak,
                $"Price {current:F2} fell {tslPct:F1}% off the {peak:F2} high-water mark.");

        // 4. Primary take-profit target (non-gapper strategies mostly).
        if (def.TakeProfitPrice is { } tp && current >= tp)
            return new GapperExitDecision(GapperExitReason.TargetHit, current, peak,
                $"Price {current:F2} reached the {tp:F2} take-profit target.");

        // 4b. Prior-high-of-day target: sell into the high formed BEFORE entry
        //     (the earlier HOD/resistance) — the double-bottom "sell approaching
        //     the earlier high" exit. Approaches = within 0.3%.
        if (def.ExitAtPriorHigh)
        {
            double priorHigh = 0;
            foreach (var c in candles)
                if (c.EndUtc <= entryUtc && (double)c.High > priorHigh) priorHigh = (double)c.High;
            if (priorHigh > entryPrice && current >= priorHigh * 0.997)
                return new GapperExitDecision(GapperExitReason.TargetHit, current, peak,
                    $"Price {current:F2} approached the prior HOD {priorHigh:F2} — taking profit into resistance.");
        }

        // 5. Momentum rollover — armed from the configured ET time (or always).
        if (def.PeakGivebackPercent is { } giveback)
        {
            var armed = def.PeakGivebackArmTime is not { } arm || nowEt >= arm;
            var run = peak - entryPrice;
            if (armed && run > 0)
            {
                var floor = peak - run * (giveback / 100.0);
                if (current <= floor)
                    return new GapperExitDecision(GapperExitReason.PeakGiveback, current, peak,
                        $"Gave back {giveback:F0}% of the {entryPrice:F2}→{peak:F2} run (floor {floor:F2}) — momentum rolled over.");
            }
        }

        return null;
    }

    /// <summary>
    /// Mirror of <see cref="Evaluate"/> for a SHORT position (entered by selling
    /// at <paramref name="entryPrice"/>; profit when price falls). Everything is
    /// inverted: the low-water mark (trough) is the run's extreme, the hard/
    /// trailing stops sit ABOVE entry (a rise is the loss), the take-profit is
    /// below, and the giveback measures a bounce back UP off the trough. SellBy
    /// is unchanged (flatten before the bell either way). Kept as a separate
    /// method so the many long-only callers are untouched.
    /// </summary>
    public static GapperExitDecision? EvaluateShort(
        StrategyDefinition def,
        double entryPrice,
        DateTime entryUtc,
        IReadOnlyList<Candle> candles,
        DateTime nowUtc)
    {
        if (entryPrice <= 0 || candles.Count == 0)
            return null;

        // Low-water mark since entry; entry is the ceiling of the trough.
        var trough = entryPrice;
        double current = entryPrice;
        foreach (var c in candles)
        {
            if (c.EndUtc <= entryUtc) continue;
            if ((double)c.Low < trough) trough = (double)c.Low;
            current = (double)c.Close;
        }

        var nowEt = MarketTime.ToEasternTimeOfDay(nowUtc);

        // 1. Hard cover-by time — never hold into the bell.
        if (def.ExitTime is { } sellBy)
        {
            var heldPastItsDay = EasternDate(nowUtc) > EasternDate(entryUtc);
            if (nowEt >= sellBy || heldPastItsDay)
                return new GapperExitDecision(GapperExitReason.SellByTime, current, trough,
                    heldPastItsDay
                        ? $"Short outlived its {sellBy:hh\\:mm} ET cover-by day — flattening at the first opportunity."
                        : $"Cover-by {sellBy:hh\\:mm} ET reached — flattening before the bell.");
        }

        // 2. Hard stop ABOVE entry — a rise is the short's loss.
        if (def.StopLossPercent is { } slPct && current >= entryPrice * (1 + slPct / 100.0))
            return new GapperExitDecision(GapperExitReason.StopLoss, current, trough,
                $"Price {current:F2} rose past the {slPct:F1}% hard stop above short entry {entryPrice:F2}.");
        if (def.StopLossPrice is { } slPrice && current >= slPrice)
            return new GapperExitDecision(GapperExitReason.StopLoss, current, trough,
                $"Price {current:F2} rose past the hard stop at {slPrice:F2}.");

        // 3. Trailing stop above the trough (bounce off the low-water mark).
        if (def.TrailingStopPercent is { } tslPct && trough < entryPrice
            && current >= trough * (1 + tslPct / 100.0))
            return new GapperExitDecision(GapperExitReason.TrailingStop, current, trough,
                $"Price {current:F2} bounced {tslPct:F1}% off the {trough:F2} low-water mark.");

        // 4. Primary take-profit target (below entry for a short).
        if (def.TakeProfitPrice is { } tp && current <= tp)
            return new GapperExitDecision(GapperExitReason.TargetHit, current, trough,
                $"Price {current:F2} reached the {tp:F2} take-profit target.");

        // 5. Momentum rollover — gave back N% of the entry→trough down-move.
        if (def.PeakGivebackPercent is { } giveback)
        {
            var armed = def.PeakGivebackArmTime is not { } arm || nowEt >= arm;
            var run = entryPrice - trough;
            if (armed && run > 0)
            {
                var ceiling = trough + run * (giveback / 100.0);
                if (current >= ceiling)
                    return new GapperExitDecision(GapperExitReason.PeakGiveback, current, trough,
                        $"Bounced back {giveback:F0}% of the {entryPrice:F2}→{trough:F2} drop (ceiling {ceiling:F2}) — cover.");
            }
        }

        return null;
    }

    private static DateOnly EasternDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern));
}
