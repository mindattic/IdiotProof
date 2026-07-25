using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Every exit path in GapperExitEvaluator — both Evaluate (long) and EvaluateShort.
/// Covers rule ordering, edge cases (entry==peak, zero bars, etc.), and verifies
/// that the fixed TakeProfitPercent path and EvaluateShort rule ordering are correct.
/// </summary>
public class GapperExitEvaluatorAllPathsTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DateTime Utc(int h, int m = 0) => new(2026, 7, 17, h, m, 0, DateTimeKind.Utc);

    // Eastern offset on 2026-07-17 (EDT) = UTC-4, so:
    //   04:00 ET = 08:00 UTC, 09:15 ET = 13:15 UTC, 09:28 ET = 13:28 UTC
    private static readonly DateTime EntryUtc = Utc(8, 30);  // 04:30 ET

    private static Candle C(double open, double high, double low, double close,
                             DateTime? startUtc = null, DateTime? endUtc = null)
    {
        var start = startUtc ?? EntryUtc.AddMinutes(-1);
        var end   = endUtc   ?? EntryUtc.AddMinutes(30);
        return new Candle
        {
            Symbol = "T", Open = (decimal)open, High = (decimal)high,
            Low = (decimal)low, Close = (decimal)close,
            Volume = 10_000, StartUtc = start, EndUtc = end,
        };
    }

    private static Candle PostEntry(double high, double close, int minutesAfter = 5) =>
        C(close, high, close * 0.99, close,
          EntryUtc.AddMinutes(minutesAfter), EntryUtc.AddMinutes(minutesAfter + 1));

    private static Candle DailyC(double high, double low) =>
        new() { Symbol = "T", Open = (decimal)high, High = (decimal)high,
                Low = (decimal)low, Close = (decimal)((high + low) / 2),
                StartUtc = Utc(0), EndUtc = Utc(23, 59) };

    private static StrategyDefinition Def(
        double? slPct = null, double? slPrice = null,
        double? tslPct = null, double? tpPrice = null, double? tpPct = null,
        double? giveback = null, string? armTime = null, string? sellBy = null,
        bool priorHigh = false,
        int? rollingHighDays = null, double? rollingHighBuffer = null,
        int? rollingLowDays = null, double? rollingLowBuffer = null)
    {
        var b = Stock.Ticker("T").Long();
        if (slPct.HasValue)            b.StopLossPercent(slPct.Value);
        if (slPrice.HasValue)          b.StopLoss(slPrice.Value);
        if (tslPct.HasValue)           b.TrailingStopLoss(tslPct.Value);
        if (tpPrice.HasValue)          b.TakeProfit(tpPrice.Value);
        if (tpPct.HasValue)            b.TakeProfitPercent(tpPct.Value);
        if (giveback.HasValue)         b.PeakGiveback(giveback.Value, armTime);
        if (sellBy is not null)        b.SellBy(sellBy);
        if (priorHigh)                 b.ExitAtPriorHigh();
        if (rollingHighDays.HasValue)  b.ExitAtRollingHigh(rollingHighDays.Value, rollingHighBuffer ?? 2.5);
        if (rollingLowDays.HasValue)   b.ExitAtRollingLow(rollingLowDays.Value, rollingLowBuffer ?? 2.5);
        return b.Build();
    }

    // ── LONG — guard rails ─────────────────────────────────────────────────

    [Test]
    public void Evaluate_ZeroCandles_ReturnsNull()
    {
        var def = Def(slPct: 5);
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, [], DateTime.UtcNow);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_ZeroEntryPrice_ReturnsNull()
    {
        var candles = new[] { PostEntry(11, 10.5) };
        var result = GapperExitEvaluator.Evaluate(Def(slPct: 5), 0, EntryUtc, candles, DateTime.UtcNow);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_AllBarsBeforeEntry_ReturnsNull()
    {
        // All candles ended before entry — peak stays at entry, current = entry, no exit fires
        var preEntry = new[] { C(10, 10, 9.5, 9.8, Utc(8, 0), Utc(8, 29)) };
        var result = GapperExitEvaluator.Evaluate(Def(slPct: 5), 10.0, EntryUtc, preEntry, Utc(8, 30));
        Assert.That(result, Is.Null);
    }

    // ── LONG — 1. SellBy ────────────────────────────────────────────────────

    [Test]
    public void Evaluate_SellByReached_FiresSellByTime()
    {
        var candles = new[] { PostEntry(11, 10.5) };
        // nowUtc = 13:28 UTC = 09:28 ET; sellBy = "09:28"
        var result = GapperExitEvaluator.Evaluate(Def(sellBy: "09:28"), 10.0, EntryUtc, candles, Utc(13, 28));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.SellByTime));
        });
    }

    [Test]
    public void Evaluate_SellByNotYetReached_DoesNotFire()
    {
        var candles = new[] { PostEntry(10.5, 10.3) }; // no other exits armed
        // nowUtc = 09:00 UTC = 05:00 ET; sellBy = "09:28" ET
        var result = GapperExitEvaluator.Evaluate(Def(sellBy: "09:28"), 10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_PositionOutlivedItsDay_FiresSellByTime()
    {
        var candles = new[] { PostEntry(10.5, 10.3) };
        // Entry on 2026-07-17, nowUtc is 2026-07-18 — past its day
        var nextDay = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var result = GapperExitEvaluator.Evaluate(Def(sellBy: "09:28"), 10.0, EntryUtc, candles, nextDay);
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.SellByTime));
    }

    // ── LONG — 2. StopLoss ──────────────────────────────────────────────────

    [Test]
    public void Evaluate_StopLossPercent_BreachedBelow_FiresStopLoss()
    {
        // Entry 10.0, stop 5% → floor 9.50. Close = 9.40 → fire.
        var candles = new[] { PostEntry(10.2, 9.40) };
        var result = GapperExitEvaluator.Evaluate(Def(slPct: 5), 10.0, EntryUtc, candles, Utc(10));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.StopLoss));
        });
    }

    [Test]
    public void Evaluate_StopLossPercent_AtExactFloor_Fires()
    {
        var candles = new[] { PostEntry(10.2, 9.50) };
        var result = GapperExitEvaluator.Evaluate(Def(slPct: 5), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    [Test]
    public void Evaluate_StopLossPercent_AboveFloor_DoesNotFire()
    {
        var candles = new[] { PostEntry(10.2, 9.60) };
        var result = GapperExitEvaluator.Evaluate(Def(slPct: 5), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_StopLossPrice_BreachedBelow_FiresStopLoss()
    {
        var candles = new[] { PostEntry(10.2, 8.90) };
        var result = GapperExitEvaluator.Evaluate(Def(slPrice: 9.0), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    // ── LONG — 3. TrailingStop ──────────────────────────────────────────────

    [Test]
    public void Evaluate_TrailingStop_FiredWhenPriceFallsOffPeak()
    {
        // Peak reached 12.0, current 11.4 = 5% off peak → fire at 3%.
        var candles = new List<Candle>
        {
            PostEntry(12.0, 12.0, minutesAfter: 5),
            PostEntry(12.0, 11.4, minutesAfter: 10),
        };
        var result = GapperExitEvaluator.Evaluate(Def(tslPct: 3), 10.0, EntryUtc, candles, Utc(10));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TrailingStop));
        });
    }

    [Test]
    public void Evaluate_TrailingStop_NotFiredWhenWithinTolerance()
    {
        // Peak 12.0, current 11.7 = 2.5% off peak; trailing stop 3% → should NOT fire
        var candles = new List<Candle>
        {
            PostEntry(12.0, 12.0, minutesAfter: 5),
            PostEntry(12.0, 11.7, minutesAfter: 10),
        };
        var result = GapperExitEvaluator.Evaluate(Def(tslPct: 3), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_TrailingStop_TrailsFromEntryWhenNoRun()
    {
        // No up-move since entry: peak == entry == 10.0; trailing stop at 3% → floor 9.70
        var candles = new[] { PostEntry(10.0, 9.65) };
        var result = GapperExitEvaluator.Evaluate(Def(tslPct: 3), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TrailingStop));
    }

    // ── LONG — 4. TakeProfitPrice ───────────────────────────────────────────

    [Test]
    public void Evaluate_TakeProfitPrice_ReachedOrAbove_FiresTargetHit()
    {
        var candles = new[] { PostEntry(12.1, 12.0) };
        var result = GapperExitEvaluator.Evaluate(Def(tpPrice: 12.0), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void Evaluate_TakeProfitPrice_BelowTarget_DoesNotFire()
    {
        var candles = new[] { PostEntry(11.9, 11.8) };
        var result = GapperExitEvaluator.Evaluate(Def(tpPrice: 12.0), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }

    // ── LONG — 4b. TakeProfitPercent (was: CRITICAL BUG) ────────────────────

    [Test]
    public void Evaluate_TakeProfitPercent_TriggersWhenGainMeetsThreshold()
    {
        // Entry 10.0, TakeProfitPercent 10 → target 11.0; current 11.0 → fire.
        var candles = new[] { PostEntry(11.0, 11.0) };
        var result = GapperExitEvaluator.Evaluate(Def(tpPct: 10), 10.0, EntryUtc, candles, Utc(10));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null,
                "TakeProfitPercent must fire when gain reaches threshold (was a critical bug where it never evaluated)");
            Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit));
        });
    }

    [Test]
    public void Evaluate_TakeProfitPercent_BelowThreshold_DoesNotFire()
    {
        // Entry 10.0, TakeProfitPercent 10 → target 11.0; current 10.8 → no fire.
        var candles = new[] { PostEntry(10.9, 10.8) };
        var result = GapperExitEvaluator.Evaluate(Def(tpPct: 10), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_TakeProfitPercent_Above100Percent_Works()
    {
        // 120% gain: entry 10.0, target 22.0; current 22.0
        var candles = new[] { PostEntry(22.0, 22.0) };
        var result = GapperExitEvaluator.Evaluate(Def(tpPct: 120), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    // ── LONG — 4c. ExitAtPriorHigh ─────────────────────────────────────────

    [Test]
    public void Evaluate_ExitAtPriorHigh_ApproachesPriorHod_Fires()
    {
        // Prior high = 12.0 (bar before entry); current 11.97 = 99.75% of 12.0 → within 0.3%
        var priorBar = C(10, 12.0, 10, 11.5, Utc(7, 0), Utc(8, 0));
        var postBar  = PostEntry(11.97, 11.97);
        var result = GapperExitEvaluator.Evaluate(Def(priorHigh: true), 10.0, EntryUtc,
            new[] { priorBar, postBar }, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void Evaluate_ExitAtPriorHigh_PriorHighTooCloseToEntry_DoesNotFire()
    {
        // Prior high 10.04 = only 0.4% above entry — below the 0.6% minimum, so skip
        var priorBar = C(10, 10.04, 9.9, 10.0, Utc(7), Utc(8));
        var postBar  = PostEntry(10.04, 10.03);
        var result = GapperExitEvaluator.Evaluate(Def(priorHigh: true), 10.0, EntryUtc,
            new[] { priorBar, postBar }, Utc(10));
        Assert.That(result, Is.Null);
    }

    // ── LONG — 4d. RollingHighDays ──────────────────────────────────────────

    [Test]
    public void Evaluate_RollingHighDays_PriceNearRollingHigh_Fires()
    {
        var candles = new[] { PostEntry(20, 19.6) };
        var daily   = new[] { DailyC(20, 15), DailyC(18, 14) };
        // Rolling high = 20.0; current 19.6 = 98% of 20.0; within 2.5% buffer → fire
        var result = GapperExitEvaluator.Evaluate(Def(rollingHighDays: 2), 10.0, EntryUtc,
            candles, Utc(10), daily);
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void Evaluate_RollingHighDays_NoDailyCandles_DoesNotFire()
    {
        var candles = new[] { PostEntry(20, 19.6) };
        var result = GapperExitEvaluator.Evaluate(Def(rollingHighDays: 2), 10.0, EntryUtc,
            candles, Utc(10), null);
        Assert.That(result, Is.Null, "no dailyCandles → rolling high exit must be skipped");
    }

    // ── LONG — 4e. RollingLowDays ───────────────────────────────────────────

    [Test]
    public void Evaluate_RollingLowDays_PriceNearRollingLow_FiresStopLoss()
    {
        var candles = new[] { PostEntry(10, 9.75) };
        var daily   = new[] { DailyC(15, 9.6), DailyC(14, 9.8) };
        // Rolling low = 9.6; current 9.75 = 101.6% of 9.6; within 2.5% buffer → fire stop
        var result = GapperExitEvaluator.Evaluate(Def(rollingLowDays: 2), 10.0, EntryUtc,
            candles, Utc(10), daily);
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    // ── LONG — 5. PeakGiveback ──────────────────────────────────────────────

    [Test]
    public void Evaluate_PeakGiveback_GivesBackConfiguredPercent_Fires()
    {
        // Entry 10.0, peak 14.0 → run = 4.0; 25% giveback = 1.0 → floor 13.0
        // Current = 12.9 → below floor → fire
        var candles = new List<Candle>
        {
            PostEntry(14.0, 14.0, minutesAfter: 5),
            PostEntry(14.0, 12.9, minutesAfter: 10),
        };
        var result = GapperExitEvaluator.Evaluate(Def(giveback: 25), 10.0, EntryUtc, candles, Utc(10));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.PeakGiveback));
        });
    }

    [Test]
    public void Evaluate_PeakGiveback_AboveFloor_DoesNotFire()
    {
        var candles = new List<Candle>
        {
            PostEntry(14.0, 14.0, minutesAfter: 5),
            PostEntry(14.0, 13.1, minutesAfter: 10), // above 13.0 floor
        };
        var result = GapperExitEvaluator.Evaluate(Def(giveback: 25), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_PeakGiveback_BeforeArmTime_DoesNotFire()
    {
        var candles = new List<Candle>
        {
            PostEntry(14.0, 14.0, minutesAfter: 5),
            PostEntry(14.0, 12.9, minutesAfter: 10),
        };
        // nowUtc = 09:00 UTC = 05:00 ET; armTime = "09:15" ET → not yet armed
        var result = GapperExitEvaluator.Evaluate(Def(giveback: 25, armTime: "09:15"),
            10.0, EntryUtc, candles, Utc(9, 0));
        Assert.That(result, Is.Null, "PeakGiveback must not fire before arm time");
    }

    [Test]
    public void Evaluate_PeakGiveback_AfterArmTime_Fires()
    {
        var candles = new List<Candle>
        {
            PostEntry(14.0, 14.0, minutesAfter: 5),
            PostEntry(14.0, 12.9, minutesAfter: 10),
        };
        // nowUtc = 13:20 UTC = 09:20 ET; armTime = "09:15" ET → armed
        var result = GapperExitEvaluator.Evaluate(Def(giveback: 25, armTime: "09:15"),
            10.0, EntryUtc, candles, Utc(13, 20));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.PeakGiveback));
    }

    [Test]
    public void Evaluate_PeakGiveback_EntryEqualsCurrentWithNoRun_DoesNotFire()
    {
        // run = 0 → skip giveback (guard against divide-by-semantics)
        var candles = new[] { PostEntry(10.0, 10.0) };
        var result = GapperExitEvaluator.Evaluate(Def(giveback: 25), 10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null, "zero run → PeakGiveback must not fire");
    }

    // ── LONG — rule priority (SellBy wins over all) ─────────────────────────

    [Test]
    public void Evaluate_RuleOrder_SellByWinsOverAllOtherExits()
    {
        // Everything else fires too: stop, trailing, tp, giveback — but SellBy must win
        var candles = new List<Candle>
        {
            PostEntry(14.0, 14.0, minutesAfter: 5),
            PostEntry(14.0, 9.0, minutesAfter: 10), // stops and giveback all triggered
        };
        var def = Def(slPct: 5, tslPct: 3, tpPrice: 12.0, tpPct: 10, giveback: 25,
                      sellBy: "09:28");
        // nowUtc = 13:28 UTC = 09:28 ET
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, Utc(13, 28));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.SellByTime),
            "SellBy is rule 1 — must fire before any other exit");
    }

    // ── SHORT — guard rails ─────────────────────────────────────────────────

    [Test]
    public void EvaluateShort_ZeroCandles_ReturnsNull()
    {
        var def = Def(slPct: 5);
        var result = GapperExitEvaluator.EvaluateShort(def, 10.0, EntryUtc, [], DateTime.UtcNow);
        Assert.That(result, Is.Null);
    }

    // ── SHORT — 1. SellBy ───────────────────────────────────────────────────

    [Test]
    public void EvaluateShort_SellByReached_Fires()
    {
        var candles = new[] { PostEntry(10.5, 9.5) };
        var result = GapperExitEvaluator.EvaluateShort(Def(sellBy: "09:28"),
            10.0, EntryUtc, candles, Utc(13, 28));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.SellByTime));
    }

    // ── SHORT — 2. StopLoss (price RISES) ───────────────────────────────────

    [Test]
    public void EvaluateShort_StopLossPercent_PriceRises_Fires()
    {
        // Short entry 10.0, stop 5% → ceiling 10.50; current 10.55 → fire
        var candles = new[] { PostEntry(10.55, 10.55) };
        var result = GapperExitEvaluator.EvaluateShort(Def(slPct: 5),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    [Test]
    public void EvaluateShort_StopLossPrice_PriceRisesToStop_Fires()
    {
        var candles = new[] { PostEntry(11.0, 11.0) };
        var result = GapperExitEvaluator.EvaluateShort(Def(slPrice: 11.0),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    [Test]
    public void EvaluateShort_StopLoss_PriceBelow_DoesNotFire()
    {
        var candles = new[] { PostEntry(10.0, 9.5) };
        var result = GapperExitEvaluator.EvaluateShort(Def(slPct: 5),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }

    // ── SHORT — 3. TrailingStop (bounce off trough) ─────────────────────────

    [Test]
    public void EvaluateShort_TrailingStop_BounceOffTrough_Fires()
    {
        // Trough reached 8.0; current 8.25 = 3.1% above 8.0; trailing stop 3% → fire
        var candles = new List<Candle>
        {
            PostEntry(10.0, 8.0,  minutesAfter: 5),
            PostEntry(10.0, 8.25, minutesAfter: 10),
        };
        var result = GapperExitEvaluator.EvaluateShort(Def(tslPct: 3),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TrailingStop));
    }

    [Test]
    public void EvaluateShort_TrailingStop_WithinTolerance_DoesNotFire()
    {
        var candles = new List<Candle>
        {
            PostEntry(10.0, 8.0,  minutesAfter: 5),
            PostEntry(10.0, 8.15, minutesAfter: 10), // only 1.9% off trough
        };
        var result = GapperExitEvaluator.EvaluateShort(Def(tslPct: 3),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }

    // ── SHORT — 4. TakeProfitPrice ──────────────────────────────────────────

    [Test]
    public void EvaluateShort_TakeProfitPrice_BelowTarget_Fires()
    {
        var candles = new[] { PostEntry(10.0, 8.0) };
        var result = GapperExitEvaluator.EvaluateShort(Def(tpPrice: 8.0),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    // ── SHORT — 4b. TakeProfitPercent (inverted) ────────────────────────────

    [Test]
    public void EvaluateShort_TakeProfitPercent_TriggersWhenPriceFalls()
    {
        // Entry 10.0, TakeProfitPercent 10 → target 9.0 (10% down); current 9.0 → fire
        var candles = new[] { PostEntry(10.0, 9.0) };
        var result = GapperExitEvaluator.EvaluateShort(Def(tpPct: 10),
            10.0, EntryUtc, candles, Utc(10));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null,
                "Short TakeProfitPercent must fire when price falls by the configured %");
            Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit));
        });
    }

    [Test]
    public void EvaluateShort_TakeProfitPercent_PriceNotYetFallen_DoesNotFire()
    {
        // Current 9.2 = only 8% below entry; target 10% → no fire
        var candles = new[] { PostEntry(10.0, 9.2) };
        var result = GapperExitEvaluator.EvaluateShort(Def(tpPct: 10),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }

    // ── SHORT — 5. RollingHighDays (stop for short) ─────────────────────────

    [Test]
    public void EvaluateShort_RollingHighDays_PriceRecoveresToHigh_FiresStopLoss()
    {
        // Short entered at 10.0; rolling high = 20.0; current 19.6 = within 2.5% buffer → stop
        var candles = new[] { PostEntry(10.0, 19.6) };
        var daily   = new[] { DailyC(20.0, 15.0) };
        var result = GapperExitEvaluator.EvaluateShort(Def(rollingHighDays: 1),
            10.0, EntryUtc, candles, Utc(10), daily);
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    // ── SHORT — 6. RollingLowDays (target for short) ────────────────────────

    [Test]
    public void EvaluateShort_RollingLowDays_PriceFallsToLow_FiresTargetHit()
    {
        // Short entered at 10.0; rolling low = 9.6; current 9.75 within 2.5% → target hit
        var candles = new[] { PostEntry(10.0, 9.75) };
        var daily   = new[] { DailyC(15.0, 9.6) };
        var result = GapperExitEvaluator.EvaluateShort(Def(rollingLowDays: 1),
            10.0, EntryUtc, candles, Utc(10), daily);
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    // ── SHORT — rule ordering: rolling exits fire BEFORE PeakGiveback ────────

    [Test]
    public void EvaluateShort_RollingHighFiresBeforePeakGiveback()
    {
        // Both RollingHighDays (stop) and PeakGiveback apply.
        // RollingHighDays is rule 5, PeakGiveback is rule 7 → RollingHigh must win.
        var candles = new List<Candle>
        {
            PostEntry(10.0, 8.0,  minutesAfter: 5),  // trough at 8
            PostEntry(10.0, 19.6, minutesAfter: 10), // recovering toward rolling high AND past giveback ceiling
        };
        var daily = new[] { DailyC(20.0, 15.0) };

        var def = Def(rollingHighDays: 1, giveback: 25); // giveback ceiling: 8 + (10-8)*0.25 = 8.5
        // current 19.6: giveback fires at 8.5, rolling high fires at 19.5 (within 2.5% of 20)
        // Since current >> giveback ceiling AND near rolling high, we just need rolling high wins
        var result = GapperExitEvaluator.EvaluateShort(def,
            10.0, EntryUtc, candles, Utc(10), daily);
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.StopLoss),
            "Rolling high stop (rule 5) must fire before PeakGiveback (rule 7)");
    }

    // ── SHORT — 7. PeakGiveback ─────────────────────────────────────────────

    [Test]
    public void EvaluateShort_PeakGiveback_BouncedBackFromTrough_Fires()
    {
        // Short entry 10.0, trough 8.0 → run = 2.0; 25% giveback = 0.5 → ceiling 8.5
        // Current 8.6 → above ceiling → fire (cover)
        var candles = new List<Candle>
        {
            PostEntry(10.0, 8.0,  minutesAfter: 5),
            PostEntry(10.0, 8.6, minutesAfter: 10),
        };
        var result = GapperExitEvaluator.EvaluateShort(Def(giveback: 25),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.PeakGiveback));
    }

    [Test]
    public void EvaluateShort_PeakGiveback_StillFalling_DoesNotFire()
    {
        var candles = new List<Candle>
        {
            PostEntry(10.0, 8.0, minutesAfter: 5),
            PostEntry(10.0, 8.3, minutesAfter: 10), // 8.3 < 8.5 ceiling
        };
        var result = GapperExitEvaluator.EvaluateShort(Def(giveback: 25),
            10.0, EntryUtc, candles, Utc(10));
        Assert.That(result, Is.Null);
    }
}
