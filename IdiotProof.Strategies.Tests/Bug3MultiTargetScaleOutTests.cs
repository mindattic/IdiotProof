using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Tests for Bug 3 (FIXED): multi-target scale-out exits (T2, T3, …) are now
/// honored by <see cref="GapperExitEvaluator"/>.
///
/// ROOT CAUSE (was)
/// ----------------
/// GapperExitEvaluator only read <c>TakeProfitPrice</c> (T1) and ignored
/// <c>TakeProfitTargets</c> entirely.  A strategy authored as
/// <c>.TakeProfit(10.5, 11.0, 12.0)</c> exited 100% of the position at T1
/// instead of selling 33% at T1, 33% at T2, and 34% at T3.
///
/// FIX
/// ---
/// GapperExitEvaluator.Evaluate / EvaluateShort now accept <c>initialQty</c>
/// and <c>currentQty</c> parameters.  When <c>TakeProfitTargets</c> is
/// non-empty and <c>initialQty &gt; 0</c> the evaluator:
///   1. Finds all targets whose price has been reached by the current close.
///   2. Accumulates the <c>PercentToSell</c> for those targets.
///   3. Computes how many shares SHOULD have been sold (based on cumulative %).
///   4. Subtracts shares ALREADY sold (<c>initialQty - currentQty</c>).
///   5. Returns a <see cref="GapperExitDecision"/> with <c>QuantityToSell</c> set
///      for a partial exit, or null for the final exit rung.
///
/// <see cref="StrategyRepository.RecordPartialExitAsync"/> (new) reduces
/// <c>PositionQty</c> without zeroing it, keeping <c>EntryFilledUtc</c> intact
/// so the next tick continues managing the remaining shares.
/// MonitorWorker detects a partial exit via <c>decision.QuantityToSell.HasValue</c>
/// and routes to <c>RecordPartialExitAsync</c> instead of <c>RecordExitFillAsync</c>.
/// </summary>
public class Bug3MultiTargetScaleOutTests
{
    private static readonly DateTime EntryUtc = new(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NowUtc   = new(2026, 7, 17, 9, 10, 0, DateTimeKind.Utc);

    private static Candle PostEntry(double high, double close, int minutesAfter = 5) => new()
    {
        Symbol   = "T",
        Open     = (decimal)close,
        High     = (decimal)high,
        Low      = (decimal)(close * 0.99),
        Close    = (decimal)close,
        Volume   = 1_000_000,
        StartUtc = EntryUtc.AddMinutes(minutesAfter),
        EndUtc   = EntryUtc.AddMinutes(minutesAfter + 1),
    };

    // ── Multi-target partial scale-out (the fixed behavior) ───────────────

    [Test]
    public void MultiTarget_PriceAtT1_ReturnsPartialDecision_33Percent()
    {
        // Strategy: T1=10.5 (33%), T2=11.0 (33%), T3=12.0 (34%).
        // Price closes at T1; 100 shares held, none sold yet.
        // Expected: sell 33 shares (33% of 100), 67 remain.
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(10.5, 11.0, 12.0).Build();
        var candles = new[] { PostEntry(10.55, 10.5) };

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc,
            initialQty: 100, currentQty: 100);

        Assert.That(result,                   Is.Not.Null,                              "must fire at T1");
        Assert.That(result!.Reason,           Is.EqualTo(GapperExitReason.TargetHit),   "reason = TargetHit");
        Assert.That(result.QuantityToSell,    Is.EqualTo(33),                           "sell 33% of 100 = 33 shares at T1");
    }

    [Test]
    public void MultiTarget_PriceAtT2_After_T1_AlreadySold_Sells_T2_Rung()
    {
        // T1 already sold (33 of 100). Price now at T2 (11.0).
        // Expected: sell another 33 shares (cumulative 66%; 33 already sold → sell 33 more).
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(10.5, 11.0, 12.0).Build();
        var candles = new[] { PostEntry(11.1, 11.0) };

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc,
            initialQty: 100, currentQty: 67); // 33 already sold

        Assert.That(result,                Is.Not.Null,                           "must fire at T2");
        Assert.That(result!.QuantityToSell, Is.EqualTo(33),                       "sell T2 rung = 33 shares");
    }

    [Test]
    public void MultiTarget_PriceAtT3_After_T1_And_T2_Sold_FullExit()
    {
        // T1 + T2 already sold (66 of 100). Price now at T3 (12.0).
        // Expected: full exit of remaining 34 shares, QuantityToSell = null (full exit).
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(10.5, 11.0, 12.0).Build();
        var candles = new[] { PostEntry(12.1, 12.0) };

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc,
            initialQty: 100, currentQty: 34); // 66 already sold

        Assert.That(result,                Is.Not.Null,                           "must fire at T3");
        Assert.That(result!.QuantityToSell, Is.Null,                              "final rung → full exit (QuantityToSell = null)");
        Assert.That(result.Reason,          Is.EqualTo(GapperExitReason.TargetHit), "reason = TargetHit");
    }

    [Test]
    public void MultiTarget_PriceSkipsAllThree_InOneTick_FullExitForAllRemaining()
    {
        // Price jumps past T1, T2, and T3 all at once in the first tick after entry.
        // No prior sells. Expected: sell all 100 shares (cumulative 100%, final exit).
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(10.5, 11.0, 12.0).Build();
        var candles = new[] { PostEntry(13.0, 12.5) };

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc,
            initialQty: 100, currentQty: 100);

        Assert.That(result,                Is.Not.Null,                           "must fire when all three targets exceeded");
        Assert.That(result!.QuantityToSell, Is.Null,                              "all targets hit → full exit");
        Assert.That(result.Reason,          Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void MultiTarget_T1_NotYetReached_NoDecision()
    {
        // Price is below T1. No exit should be signaled.
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(10.5, 11.0, 12.0).Build();
        var candles = new[] { PostEntry(10.3, 10.2) };

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc,
            initialQty: 100, currentQty: 100);

        Assert.That(result, Is.Null, "no exit when price is below T1");
    }

    [Test]
    public void MultiTarget_AlreadySoldAtThisRung_NoDoubleSell()
    {
        // On the same tick after T1 was sold, price hasn't moved yet.
        // T1 hit, 33 shares sold → currentQty = 67.
        // On the NEXT tick, price is still at 10.5 (T1). Should NOT signal another sell.
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(10.5, 11.0, 12.0).Build();
        var candles = new[] { PostEntry(10.55, 10.5) };

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc,
            initialQty: 100, currentQty: 67); // 33 already sold at T1

        // T1 already accounts for 33% of 100 = 33 shares sold.
        // alreadySold = 100-67 = 33. shouldHaveSold = 33. toSellNow = 0.
        Assert.That(result, Is.Null, "rung already sold — must not double-sell on the next tick");
    }

    [Test]
    public void MultiTarget_TwoRungs_NotionalSized_CorrectQuantities()
    {
        // Two-target strategy: T1=11.0 (50%), T2=12.0 (50%) with notional sizing.
        // 50 shares held (e.g., $500 notional at $10 entry).
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(11.0, 12.0).Build();
        var candles = new[] { PostEntry(11.1, 11.0) };

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc,
            initialQty: 50, currentQty: 50);

        Assert.That(result,                Is.Not.Null,     "fires at T1");
        Assert.That(result!.QuantityToSell, Is.EqualTo(25), "50% of 50 = 25 shares at T1");
    }

    // ── Fallback: unknown qty → full exit behavior preserved ──────────────

    [Test]
    public void MultiTarget_InitialQtyUnknown_FallsBackToTakeProfitPrice()
    {
        // When initialQty = 0 (unknown), the multi-target logic is skipped.
        // The evaluator falls back to checking TakeProfitPrice (T1).
        // This preserves backward compatibility for callers that don't pass qty.
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(10.5, 11.0, 12.0).Build();
        var candles = new[] { PostEntry(10.6, 10.5) };

        // No initialQty/currentQty → defaults 0 → fallback to TakeProfitPrice check
        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc);

        Assert.That(result,                Is.Not.Null,                              "still fires at TakeProfitPrice fallback");
        Assert.That(result!.QuantityToSell, Is.Null,                                 "no partial when qty unknown → full exit");
        Assert.That(result.Reason,          Is.EqualTo(GapperExitReason.TargetHit));
    }

    // ── Short multi-target (price falls to each rung) ─────────────────────

    [Test]
    public void ShortMultiTarget_PriceAtT1_ReturnsPartialDecision()
    {
        // Short strategy: T1=9.5 (50%), T2=9.0 (50%). Entry at 10.0.
        // Price closes at T1=9.5. 100 shares short held, none covered yet.
        var def = Stock.Ticker("T").Short().StopLossPercent(5).Build();
        def.TakeProfitTargets.Add(new TakeProfitTarget { Price = 9.5, PercentToSell = 50, Label = "T1" });
        def.TakeProfitTargets.Add(new TakeProfitTarget { Price = 9.0, PercentToSell = 50, Label = "T2" });

        var candles = new[] { PostEntry(9.8, 9.5) }; // close at 9.5

        var result = GapperExitEvaluator.EvaluateShort(def, 10.0, EntryUtc, candles, NowUtc,
            initialQty: 100, currentQty: 100);

        Assert.That(result,                Is.Not.Null,                            "fires at T1 for short");
        Assert.That(result!.QuantityToSell, Is.EqualTo(50),                        "50% of 100 = 50 shares covered at T1");
        Assert.That(result.Reason,          Is.EqualTo(GapperExitReason.TargetHit));
    }

    // ── DslStrategy signal carries full target ladder ─────────────────────

    [Test]
    public void DslStrategy_MultiTarget_PopulatesAllTargetsOnSignal()
    {
        var start = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
        var candles = new List<Candle>
        {
            new() { Symbol = "T", StartUtc = start, EndUtc = start.AddMinutes(1),
                    Open = 10m, High = 10.5m, Low = 9.9m, Close = 10m, Volume = 2_000_000 },
        };
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(11.0, 12.0, 14.0).Build();

        var signals = new DslStrategy(def).Evaluate("T", candles, new StrategyContext());

        Assert.That(signals, Has.Count.EqualTo(1));
        Assert.That(signals[0].Targets, Is.EqualTo(new[] { 11.0m, 12.0m, 14.0m }),
            "all three take-profit rungs must appear on the TradeSignal");
    }

    // ── JSON round-trip ───────────────────────────────────────────────────

    [Test]
    public void MultiTarget_RoundTripsViaJson()
    {
        var def = Stock.Ticker("T").Long().StopLossPercent(5)
            .TakeProfit(11.0, 13.0, 16.0).Build();
        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));

        Assert.Multiple(() =>
        {
            Assert.That(restored.TakeProfitPrice, Is.EqualTo(11.0),
                "TakeProfitPrice (T1) survives JSON round-trip");
            Assert.That(restored.TakeProfitTargets.Select(t => t.Price),
                Is.EqualTo(new[] { 11.0, 13.0, 16.0 }),
                "full scale-out ladder survives JSON round-trip");
            Assert.That(restored.TakeProfitTargets.Select(t => t.PercentToSell),
                Is.EqualTo(new[] { 33, 33, 34 }),
                "PercentToSell values survive JSON round-trip");
        });
    }
}
