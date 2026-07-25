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
        DateTime nowUtc,
        IReadOnlyList<Candle>? dailyCandles = null)
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

        // 3. Trailing stop off the peak. Trails from entry price when the
        //    trade has never moved in favour — no dead-zone at the open.
        if (def.TrailingStopPercent is { } tslPct
            && current <= peak * (1 - tslPct / 100.0))
            return new GapperExitDecision(GapperExitReason.TrailingStop, current, peak,
                $"Price {current:F2} fell {tslPct:F1}% off the {peak:F2} high-water mark.");

        // 4. Primary take-profit target (non-gapper strategies mostly).
        if (def.TakeProfitPrice is { } tp && current >= tp)
            return new GapperExitDecision(GapperExitReason.TargetHit, current, peak,
                $"Price {current:F2} reached the {tp:F2} take-profit target.");
        if (def.TakeProfitPercent is { } tpPct && current >= entryPrice * (1 + tpPct / 100.0))
            return new GapperExitDecision(GapperExitReason.TargetHit, current, peak,
                $"Price {current:F2} hit the {tpPct:F1}% take-profit target.");

        // 4b. Prior-high-of-day target: sell into the high formed BEFORE entry
        //     (the earlier HOD/resistance) — the double-bottom "sell approaching
        //     the earlier high" exit. Approaches = within 0.3%.
        if (def.ExitAtPriorHigh)
        {
            double priorHigh = 0;
            foreach (var c in candles)
                if (c.EndUtc <= entryUtc && (double)c.High > priorHigh) priorHigh = (double)c.High;
            // Only treat the prior HOD as a target if it's a MEANINGFUL move above
            // entry (>= 0.6%). A HOD sitting just above entry would insta-fire and
            // produce scalp churn; below the threshold the trade rides on trail/stop.
            if (priorHigh >= entryPrice * 1.006 && current >= priorHigh * 0.997)
                return new GapperExitDecision(GapperExitReason.TargetHit, current, peak,
                    $"Price {current:F2} approached the prior HOD {priorHigh:F2} — taking profit into resistance.");
        }

        // 4c. Rolling N-day high — exit when price recovers to within bufferPct%
        //     of the highest daily high over the last N trading days. This is
        //     the "sell when it reattains its 20-day high" exit; the daily bars
        //     are fetched fresh by the Monitor each tick so the target rolls forward
        //     automatically as the window advances.
        if (def.RollingHighDays is { } rhDays && dailyCandles is { Count: > 0 })
        {
            var buffer = def.RollingHighBuffer ?? 2.5;
            var lookback = Math.Min(rhDays, dailyCandles.Count);
            var rollingHigh = 0.0;
            for (var i = dailyCandles.Count - lookback; i < dailyCandles.Count; i++)
                if ((double)dailyCandles[i].High > rollingHigh) rollingHigh = (double)dailyCandles[i].High;
            if (rollingHigh > 0 && current >= rollingHigh * (1 - buffer / 100.0))
                return new GapperExitDecision(GapperExitReason.TargetHit, current, peak,
                    $"Price {current:F2} reached the {rhDays}-day rolling high {rollingHigh:F2} (within {buffer:F1}% — selling).");
        }

        // 4d. Rolling N-day LOW — exit (cut loss) when price falls within bufferPct%
        //     above the N-day rolling low. This is "support failure": the stock has
        //     given up the same low it held for the last N days — take the loss before
        //     it gets worse. Evaluated against daily bars fetched by the Monitor.
        if (def.RollingLowDays is { } rlDays && dailyCandles is { Count: > 0 })
        {
            var buffer = def.RollingLowBuffer ?? 2.5;
            var lookback = Math.Min(rlDays, dailyCandles.Count);
            var rollingLow = double.MaxValue;
            for (var i = dailyCandles.Count - lookback; i < dailyCandles.Count; i++)
                if ((double)dailyCandles[i].Low < rollingLow) rollingLow = (double)dailyCandles[i].Low;
            if (rollingLow < double.MaxValue && current <= rollingLow * (1 + buffer / 100.0))
                return new GapperExitDecision(GapperExitReason.StopLoss, current, peak,
                    $"Price {current:F2} fell to the {rlDays}-day rolling low {rollingLow:F2} (within {buffer:F1}% — support failure, cutting loss).");
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
        DateTime nowUtc,
        IReadOnlyList<Candle>? dailyCandles = null)
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
        //    Trails from entry price when the trade never moved in favour.
        if (def.TrailingStopPercent is { } tslPct
            && current >= trough * (1 + tslPct / 100.0))
            return new GapperExitDecision(GapperExitReason.TrailingStop, current, trough,
                $"Price {current:F2} bounced {tslPct:F1}% off the {trough:F2} low-water mark.");

        // 4. Primary take-profit target (below entry for a short).
        if (def.TakeProfitPrice is { } tp && current <= tp)
            return new GapperExitDecision(GapperExitReason.TargetHit, current, trough,
                $"Price {current:F2} reached the {tp:F2} take-profit target.");
        if (def.TakeProfitPercent is { } tpPct && current <= entryPrice * (1 - tpPct / 100.0))
            return new GapperExitDecision(GapperExitReason.TargetHit, current, trough,
                $"Short: price {current:F2} hit the {tpPct:F1}% take-profit target.");

        // 5. Rolling N-day HIGH — for a short, recovery toward the N-day high is
        //    the stop-loss; checked before PeakGiveback so explicit stops win.
        if (def.RollingHighDays is { } rhDays && dailyCandles is { Count: > 0 })
        {
            var buffer = def.RollingHighBuffer ?? 2.5;
            var lookback = Math.Min(rhDays, dailyCandles.Count);
            var rollingHigh = 0.0;
            for (var i = dailyCandles.Count - lookback; i < dailyCandles.Count; i++)
                if ((double)dailyCandles[i].High > rollingHigh) rollingHigh = (double)dailyCandles[i].High;
            if (rollingHigh > 0 && current >= rollingHigh * (1 - buffer / 100.0))
                return new GapperExitDecision(GapperExitReason.StopLoss, current, trough,
                    $"Short: price {current:F2} recovered to the {rhDays}-day rolling high {rollingHigh:F2} (within {buffer:F1}% — covering loss).");
        }

        // 6. Rolling N-day LOW — for a short, falling toward the N-day low is the
        //    profit target; checked before PeakGiveback so explicit targets win.
        if (def.RollingLowDays is { } rlDays && dailyCandles is { Count: > 0 })
        {
            var buffer = def.RollingLowBuffer ?? 2.5;
            var lookback = Math.Min(rlDays, dailyCandles.Count);
            var rollingLow = double.MaxValue;
            for (var i = dailyCandles.Count - lookback; i < dailyCandles.Count; i++)
                if ((double)dailyCandles[i].Low < rollingLow) rollingLow = (double)dailyCandles[i].Low;
            if (rollingLow < double.MaxValue && current <= rollingLow * (1 + buffer / 100.0))
                return new GapperExitDecision(GapperExitReason.TargetHit, current, trough,
                    $"Short: price {current:F2} reached the {rlDays}-day rolling low {rollingLow:F2} (within {buffer:F1}% — taking profit).");
        }

        // 7. Momentum rollover — gave back N% of the entry→trough down-move.
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
