using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Exhaustive coverage of <see cref="GapperExitEvaluator"/> exit rule priority
/// order for both long and short directions.
///
/// Priority order (long), mapped to <see cref="GapperExitReason"/> values:
///   1. SellByTime          → GapperExitReason.SellByTime
///   2. StopLossPercent      → GapperExitReason.StopLoss
///   3. StopLossPrice        → GapperExitReason.StopLoss
///   4. TrailingStop         → GapperExitReason.TrailingStop
///   5. TakeProfitPrice      → GapperExitReason.TargetHit
///   6. TakeProfitPercent    → GapperExitReason.TargetHit
///   7. ExitAtPriorHigh      → GapperExitReason.TargetHit
///   8. RollingHighDays      → GapperExitReason.TargetHit
///   9. PeakGiveback         → GapperExitReason.PeakGiveback
///
/// Key invariant: a higher-priority rule always preempts a lower-priority rule
/// that also triggers in the same tick.
///
/// All times: 2026-07-17, EDT = UTC-4. Entry at 04:30 ET = 08:30 UTC.
/// </summary>
public class ExitRuleOrderingAllCasesTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static DateTime Utc(int h, int m = 0) =>
        new(2026, 7, 17, h, m, 0, DateTimeKind.Utc);

    private static readonly DateTime EntryUtc = Utc(8, 30); // 04:30 ET

    private static Candle PostEntry(double high, double close, int minutesAfter = 5)
    {
        var start = EntryUtc.AddMinutes(minutesAfter);
        return new Candle
        {
            Symbol = "T",
            Open = (decimal)close, High = (decimal)high,
            Low = (decimal)(close * 0.99), Close = (decimal)close,
            Volume = 1_000_000,
            StartUtc = start, EndUtc = start.AddMinutes(1),
        };
    }

    private static Candle DailyC(double high, double low) => new()
    {
        Symbol = "T",
        Open = (decimal)high, High = (decimal)high,
        Low = (decimal)low, Close = (decimal)((high + low) / 2),
        StartUtc = Utc(0), EndUtc = Utc(23, 59),
    };

    private static StrategyDefinition Def(
        double? stopPct      = null,
        double? stopPrice    = null,
        double? trailPct     = null,
        double? targetPrice  = null,
        double? targetPct    = null,
        double? giveback     = null,
        string? armTime      = null,
        string? sellBy       = null,
        bool exitAtPriorHigh = false)
    {
        var b = Stock.Ticker("T").Long();
        if (stopPct.HasValue)      b.StopLossPercent(stopPct.Value);
        if (stopPrice.HasValue)    b.StopLoss(stopPrice.Value);
        if (trailPct.HasValue)     b.TrailingStopLoss(trailPct.Value);
        if (targetPrice.HasValue)  b.TakeProfit(targetPrice.Value);
        if (targetPct.HasValue)    b.TakeProfitPercent(targetPct.Value);
        if (giveback.HasValue)     b.PeakGiveback(giveback.Value, armTime);
        if (sellBy is not null)    b.SellBy(sellBy);
        if (exitAtPriorHigh)       b.ExitAtPriorHigh();
        return b.Build();
    }

    // ── Rule 1: SellByTime ────────────────────────────────────────────────

    [Test]
    public void SellByTime_PastSellByEt_Fires()
    {
        var def = Def(sellBy: "09:28");
        var candles = new[] { PostEntry(10.5, 10.3) };
        // 13:29 UTC = 09:29 ET — past the 09:28 sell-by
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(13, 29));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.SellByTime));
    }

    [Test]
    public void SellByTime_BeforeSellBy_DoesNotFire()
    {
        var def = Def(sellBy: "09:28");
        var candles = new[] { PostEntry(10.5, 10.3) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(12, 0));
        Assert.That(result?.Reason, Is.Not.EqualTo(GapperExitReason.SellByTime));
    }

    // ── Rule 2: StopLossPercent ───────────────────────────────────────────

    [Test]
    public void StopLossPercent_PriceBelowStop_Fires()
    {
        // Entry 10.0, stop 5% → stop at 9.50; close 9.40 < 9.50
        var def = Def(stopPct: 5);
        var candles = new[] { PostEntry(9.60, 9.40) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    [Test]
    public void StopLossPercent_PriceAboveStop_DoesNotFire()
    {
        var def = Def(stopPct: 5);
        var candles = new[] { PostEntry(10.5, 10.2) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.Not.EqualTo(GapperExitReason.StopLoss));
    }

    [Test]
    public void StopLossPercent_AtExactBoundary_Fires()
    {
        // Close = entry * (1 - stopPct/100) = exactly at stop level
        var def = Def(stopPct: 5);
        var candles = new[] { PostEntry(9.52, 9.50) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.StopLoss),
            "stop is triggered at exactly the stop level");
    }

    // ── Rule 3: StopLossPrice (absolute) ─────────────────────────────────

    [Test]
    public void StopLossPrice_PriceBelowLevel_Fires()
    {
        var def = Def(stopPrice: 9.50);
        var candles = new[] { PostEntry(9.60, 9.40) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    // ── Rule 4: TrailingStop ──────────────────────────────────────────────

    [Test]
    public void TrailingStop_GivebackPastTrail_Fires()
    {
        // Peak = 12.0; trail 10% → trail level = 12.0 * 0.90 = 10.80; close 10.5 < 10.80
        var def = Def(trailPct: 10, stopPct: 3);
        var candles = new[]
        {
            PostEntry(12.0, 12.0, minutesAfter: 10), // build peak
            PostEntry(11.0, 10.5, minutesAfter: 20), // drop through trail
        };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.TrailingStop));
    }

    [Test]
    public void TrailingStop_DropsFromEntryButNotEnough_DoesNotFire()
    {
        // Peak = 10.5; trail 10% → trail level = 10.5 * 0.90 = 9.45; close 10.0 > 9.45
        var def = Def(trailPct: 10, stopPct: 3);
        var candles = new[] { PostEntry(10.5, 10.0) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.Not.EqualTo(GapperExitReason.TrailingStop));
    }

    // ── Rule 5: TakeProfitPrice ───────────────────────────────────────────

    [Test]
    public void TakeProfitPrice_HighReachesTarget_Fires()
    {
        // High = 12.5 exceeds target 12.0
        var def = Def(targetPrice: 12.0, stopPct: 5);
        var candles = new[] { PostEntry(12.5, 12.2, minutesAfter: 30) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void TakeProfitPrice_PriceBelowTarget_DoesNotFire()
    {
        var def = Def(targetPrice: 12.0, stopPct: 5);
        var candles = new[] { PostEntry(11.5, 11.2, minutesAfter: 30) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.Not.EqualTo(GapperExitReason.TargetHit));
    }

    // ── Rule 6: TakeProfitPercent ─────────────────────────────────────────

    [Test]
    public void TakeProfitPercent_CloseReachesTarget_Fires()
    {
        // GapperExitEvaluator evaluates TakeProfitPercent against the CLOSE price.
        // 20% gain: 10.0 * 1.20 = 12.0; close 12.0 = exactly target → fires.
        var def = Def(targetPct: 20, stopPct: 5);
        var candles = new[] { PostEntry(12.1, 12.0, minutesAfter: 30) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void TakeProfitPercent_CloseBelowTarget_DoesNotFire()
    {
        // 10% target = 11.0; close 10.8 < 11.0
        var def = Def(targetPct: 10, stopPct: 5);
        var candles = new[] { PostEntry(10.9, 10.8) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result, Is.Null);
    }

    // ── Rule 7: ExitAtPriorHigh ───────────────────────────────────────────
    // ExitAtPriorHigh uses the highest HIGH from bars BEFORE EntryUtc that are
    // included in the candles list (not dailyCandles). The bar must be within
    // a proximity threshold (~0.3%) of the prior high.

    private static Candle PreEntryBar(double high, double close, int minsBeforeEntry = 30) =>
        new()
        {
            Symbol = "T",
            Open = (decimal)close, High = (decimal)high,
            Low = (decimal)(close * 0.99), Close = (decimal)close,
            Volume = 1_000_000,
            StartUtc = EntryUtc.AddMinutes(-minsBeforeEntry),
            EndUtc   = EntryUtc.AddMinutes(-minsBeforeEntry + 1),
        };

    [Test]
    public void ExitAtPriorHigh_ApproachesPriorHigh_Fires()
    {
        // Prior bar (before entry) has high=12.0; current 11.97 is 99.75% of 12.0
        // — within the ~0.3% proximity threshold.
        var def      = Def(exitAtPriorHigh: true);
        var priorBar = PreEntryBar(12.0, 11.5);
        var postBar  = PostEntry(11.97, 11.97);
        var result   = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc,
                           new[] { priorBar, postBar }, Utc(10));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void ExitAtPriorHigh_Disabled_DoesNotFire()
    {
        var def      = Def(); // exitAtPriorHigh not set
        var priorBar = PreEntryBar(12.0, 11.5);
        var postBar  = PostEntry(11.97, 11.97);
        var result   = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc,
                           new[] { priorBar, postBar }, Utc(10));
        Assert.That(result, Is.Null,
            "ExitAtPriorHigh disabled must not trigger even when price approaches prior-session high");
    }

    // ── Rule 10: PeakGiveback ─────────────────────────────────────────────

    [Test]
    public void PeakGiveback_MomentumRolls_Fires()
    {
        // Entry = 10.0; peak = 12.0; giveback 25% → exit when close < 10.0 + 0.75*(12-10) = 11.50
        var def = Def(giveback: 25, armTime: "04:30", stopPct: 3);
        var candles = new[]
        {
            PostEntry(12.0, 12.0, minutesAfter: 5),   // peak at 12.0
            PostEntry(11.4, 11.3, minutesAfter: 60),  // close 11.3 < exit level 11.5 → fires
        };
        // 13:00 UTC = 09:00 ET — well past arm time 04:30 ET
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(13, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.PeakGiveback));
    }

    [Test]
    public void PeakGiveback_BeforeArmTime_DoesNotFire()
    {
        // armTime 09:15 ET = 13:15 UTC; evaluate at 09:00 UTC (05:00 ET)
        var def = Def(giveback: 25, armTime: "09:15", stopPct: 3);
        var candles = new[] { PostEntry(12.0, 11.0) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.Not.EqualTo(GapperExitReason.PeakGiveback),
            "PeakGiveback must not fire before its arm time");
    }

    // ── Priority ordering ─────────────────────────────────────────────────

    [Test]
    public void Priority_SellByTime_BeatsStopLoss_WhenBothWouldFire()
    {
        // SellBy past + price below stop → SellByTime wins (Rule 1 > Rule 2)
        var def = Def(stopPct: 5, sellBy: "04:35");
        var candles = new[] { PostEntry(9.5, 9.3, minutesAfter: 10) };
        // 08:40 UTC = 04:40 ET > sellBy 04:35
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(8, 40));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.SellByTime),
            "SellByTime (Rule 1) must preempt StopLoss (Rule 2)");
    }

    [Test]
    public void Priority_StopLoss_BeatsTakeProfitTarget_WhenBothWouldFire()
    {
        // Both stop and target set. Candle: high hits target, close hits stop.
        // entry=10, stop=5% (→9.50), target=10.5. High=10.6 (target), close=9.4 (stop).
        var def = Def(stopPct: 5, targetPrice: 10.5);
        var candles = new[] { PostEntry(high: 10.6, close: 9.4, minutesAfter: 10) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        // StopLoss is Rule 2, TakeProfitPrice is Rule 5 — stop wins when close < stop
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.StopLoss),
            "StopLoss (Rule 2) preempts TargetHit (Rule 5) when close is below stop level");
    }

    [Test]
    public void Priority_TrailingStop_BeatsPeakGiveback_WhenBothWouldFire()
    {
        // Both trailing stop and peak giveback set, both would fire on a big drop.
        var def = Def(trailPct: 20, giveback: 10, armTime: "04:30", stopPct: 3);
        var candles = new[]
        {
            PostEntry(14.0, 14.0, minutesAfter: 5),   // peak = 14.0
            PostEntry(10.5, 10.5, minutesAfter: 70),  // close = 10.5 < trail=11.2 AND < giveback level
        };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(13, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.TrailingStop),
            "TrailingStop (Rule 4) preempts PeakGiveback (Rule 10)");
    }

    // ── Short direction ───────────────────────────────────────────────────

    [Test]
    public void Short_SellByTime_Fires()
    {
        var def = Def(sellBy: "04:35");
        def.Direction = TradeDirection.Short;
        var candles = new[] { PostEntry(9.5, 9.5, minutesAfter: 10) };
        var result = GapperExitEvaluator.EvaluateShort(def, 10.0, EntryUtc, candles, Utc(8, 40));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.SellByTime));
    }

    [Test]
    public void Short_StopLossPercent_PriceRisesAboveStop_Fires()
    {
        // Short entry at 10.0, stop 5% above → 10.5; close 10.6 > 10.5
        var def = Def(stopPct: 5);
        def.Direction = TradeDirection.Short;
        var candles = new[] { PostEntry(10.7, 10.6, minutesAfter: 10) };
        var result = GapperExitEvaluator.EvaluateShort(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    [Test]
    public void Short_TakeProfitPrice_PriceDropsBelowTarget_Fires()
    {
        // Short entry at 10.0, target 8.0; low hits 7.9 → fires
        var def = Def(targetPrice: 8.0, stopPct: 5);
        def.Direction = TradeDirection.Short;
        var candles = new[] { PostEntry(9.8, 7.9, minutesAfter: 30) };
        var result = GapperExitEvaluator.EvaluateShort(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void Short_TrailingStop_PeakTroughReboundsPastTrail_Fires()
    {
        // Short "peak" = trough (lowest close). Trough = 7.0.
        // Trail 20% above trough → 7.0 * 1.20 = 8.40; close 8.5 > 8.4 → fires
        var def = Def(trailPct: 20, stopPct: 8);
        def.Direction = TradeDirection.Short;
        var candles = new[]
        {
            PostEntry(9.5, 7.0, minutesAfter: 10),  // new trough
            PostEntry(8.5, 8.5, minutesAfter: 20),  // rebound through trail
        };
        var result = GapperExitEvaluator.EvaluateShort(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.TrailingStop));
    }

    [Test]
    public void Short_PeakGiveback_ShortRallyFromTrough_Fires()
    {
        // Entry = 10.0; trough = 7.0; giveback 25% from peak-gain perspective
        // "giveback" for short: close rises 25% of the drop: exit = 7.0 + 0.25*(10.0-7.0) = 7.75
        var def = Def(giveback: 25, armTime: "04:30", stopPct: 5);
        def.Direction = TradeDirection.Short;
        var candles = new[]
        {
            PostEntry(9.5, 7.0, minutesAfter: 5),   // trough at 7.0
            PostEntry(8.0, 7.9, minutesAfter: 60),  // close 7.9 > giveback level 7.75 → fires
        };
        var result = GapperExitEvaluator.EvaluateShort(def, 10.0, EntryUtc, candles, Utc(13, 0));
        Assert.That(result?.Reason, Is.EqualTo(GapperExitReason.PeakGiveback));
    }

    // ── No-exit baseline ──────────────────────────────────────────────────

    [Test]
    public void Long_NothingTriggered_ReturnsNull()
    {
        // Price drifted up; all levels comfortable
        var def = Def(stopPct: 5, targetPrice: 15.0, trailPct: 10, sellBy: "09:29");
        var candles = new[] { PostEntry(10.5, 10.3) };
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result, Is.Null,
            "when nothing triggers, Evaluate must return null (still holding)");
    }

    [Test]
    public void Short_NothingTriggered_ReturnsNull()
    {
        var def = Def(stopPct: 5, targetPrice: 8.0, sellBy: "09:29");
        def.Direction = TradeDirection.Short;
        var candles = new[] { PostEntry(9.9, 9.8) };
        var result = GapperExitEvaluator.EvaluateShort(def, 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_ZeroCandles_ReturnsNull()
    {
        var def = Def(stopPct: 5);
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, [], Utc(9, 0));
        Assert.That(result, Is.Null);
    }
}
