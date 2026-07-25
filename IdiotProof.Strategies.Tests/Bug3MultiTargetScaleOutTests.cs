using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Characterization tests for Bug 3: multi-target scale-out exits (T2, T3, …) are
/// silently dropped in the live exit path.
///
/// ROOT CAUSE
/// ----------
/// <see cref="GapperExitEvaluator.Evaluate"/> only checks the single
/// <c>StrategyDefinition.TakeProfitPrice</c> field (T1).  The full scale-out
/// ladder is in <c>StrategyDefinition.TakeProfitTargets</c> — populated by
/// <c>StrategyBuilder.TakeProfit(t1, t2, t3)</c> — but the exit evaluator
/// never reads <c>TakeProfitTargets</c>.  T2/T3 therefore have zero effect on
/// live exit timing.
///
/// A backtester that uses <c>TradeSignal.Targets</c> (populated by
/// <see cref="DslStrategy.Evaluate"/>) sees all three targets; a live Monitor
/// using <c>GapperExitEvaluator</c> sees only T1.  The live path and the
/// backtester diverge: live runs are more aggressive (T1 exits the full
/// position rather than scaling out).
///
/// WHY NOT FIXED YET
/// -----------------
/// Proper partial-sell scale-out requires:
///   (a) fractional-position tracking (current code models qty as a whole int),
///   (b) multiple partial broker orders with quantity split across targets,
///   (c) an updated ConditionProgress + position book that reflects the remainder.
/// This is a non-trivial architectural change; it is deferred.  These tests
/// document the KNOWN BEHAVIOR so regressions are visible and the gap is not
/// silently re-introduced.
///
/// VERIFIED IN THESE TESTS
/// -----------------------
/// - GapperExitEvaluator with TakeProfit(t1, t2, t3): exits at T1, not T2/T3.
/// - GapperExitEvaluator ignores TakeProfitTargets entirely.
/// - DslStrategy.Evaluate (the entry + signal path) DOES populate all targets
///   in TradeSignal.Targets — so the backtester gets the full ladder.
/// </summary>
public class Bug3MultiTargetScaleOutTests
{
    private static readonly DateTime EntryUtc = new(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NowUtc   = new(2026, 7, 17, 9, 5, 0, DateTimeKind.Utc);

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

    // ── GapperExitEvaluator only checks T1 (TakeProfitPrice) ─────────────

    [Test]
    public void GapperExit_TakeProfitPrice_ExitsAtT1_FullPosition()
    {
        // Strategy: T1=10.5, T2=11.0, T3=12.0
        // Price closes at T1 exactly — exits the FULL position (no partial scale-out).
        var def = Stock.Ticker("T")
            .Long()
            .StopLossPercent(5)
            .TakeProfit(10.5, 11.0, 12.0)  // T1=10.5, T2=11.0, T3=12.0
            .Build();

        var candles = new[] { PostEntry(10.55, 10.5) };

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc);

        // Bug 3 characterization: GapperExitEvaluator fires at TakeProfitPrice (T1)
        // and exits the FULL position — T2/T3 in TakeProfitTargets are ignored.
        Assert.That(result, Is.Not.Null,
            "GapperExitEvaluator must fire when close reaches TakeProfitPrice (T1)");
        Assert.That(result!.Reason, Is.EqualTo(GapperExitReason.TargetHit),
            "exit at T1 is recorded as TargetHit");
    }

    [Test]
    public void GapperExit_DoesNotReadTakeProfitTargets_BelowTakeProfitPrice_DoesNotExit()
    {
        // Strategy: TakeProfitPrice = 11.0 (T1), TakeProfitTargets has a partial at 10.5.
        // Price closes at 10.6 — above the partial-ladder entry but below TakeProfitPrice.
        // Confirms GapperExitEvaluator ignores TakeProfitTargets entries.
        var def = Stock.Ticker("T")
            .Long()
            .StopLossPercent(5)
            .Build();

        def.TakeProfitPrice = 11.0;
        def.TakeProfitTargets.Add(new TakeProfitTarget { Price = 10.5, PercentToSell = 50, Label = "T1" });
        def.TakeProfitTargets.Add(new TakeProfitTarget { Price = 11.0, PercentToSell = 50, Label = "T2" });

        var candles = new[] { PostEntry(10.7, 10.6) };  // close=10.6 — above T1-partial but below TakeProfitPrice=11.0

        var result = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc);

        // GapperExitEvaluator only checks TakeProfitPrice (11.0); 10.6 doesn't trigger it.
        // TakeProfitTargets[0].Price=10.5 is IGNORED by the live exit evaluator (Bug 3).
        Assert.That(result, Is.Null,
            "GapperExitEvaluator must NOT fire at a TakeProfitTargets partial level — " +
            "it only reads TakeProfitPrice; Bug 3 means partial-exit is unsupported live");
    }

    // ── DslStrategy.Evaluate populates full target ladder in the signal ───

    [Test]
    public void DslStrategy_MultiTarget_PopulatesAllTargetsOnSignal()
    {
        // The SIGNAL (entry) path carries all three targets.
        // This diverges from the live exit path — backtester honors all; Monitor exits at T1 only (Bug 3).
        var start = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
        var candles = new List<Candle>
        {
            new()
            {
                Symbol = "T", StartUtc = start, EndUtc = start.AddMinutes(1),
                Open = 10m, High = 10.5m, Low = 9.9m, Close = 10m, Volume = 2_000_000,
            },
        };

        var def = Stock.Ticker("T")
            .Long()
            .StopLossPercent(5)
            .TakeProfit(11.0, 12.0, 14.0)
            .Build();

        var signals = new DslStrategy(def).Evaluate("T", candles, new StrategyContext());

        Assert.That(signals, Has.Count.EqualTo(1), "strategy must fire");
        var sig = signals[0];

        // The entry signal carries all three rungs — backtester can honor them.
        Assert.That(sig.Targets, Has.Count.EqualTo(3),
            "DslStrategy.Evaluate must include all three TakeProfit rungs in TradeSignal.Targets");
        Assert.That(sig.Targets, Is.EqualTo(new[] { 11.0m, 12.0m, 14.0m }),
            "T1/T2/T3 must appear in order on the signal");
    }

    [Test]
    public void DslStrategy_MultiTarget_T1AlsoSetOnTakeProfitPrice()
    {
        // StrategyBuilder.TakeProfit(t1, t2, t3) sets TakeProfitPrice = t1 AND
        // populates TakeProfitTargets.  T1 is reachable by GapperExitEvaluator
        // while T2/T3 are not (Bug 3: live exit sees T1 only).
        var def = Stock.Ticker("T")
            .Long()
            .StopLossPercent(5)
            .TakeProfit(11.0, 12.0, 14.0)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(def.TakeProfitPrice,   Is.EqualTo(11.0),     "TakeProfitPrice (T1) — readable by GapperExitEvaluator");
            Assert.That(def.TakeProfitTargets, Has.Count.EqualTo(3), "TakeProfitTargets — NOT read by GapperExitEvaluator (Bug 3)");
            Assert.That(def.TakeProfitTargets.Select(t => t.Price),
                Is.EqualTo(new[] { 11.0, 12.0, 14.0 }),
                "full scale-out ladder is stored in TakeProfitTargets");
        });
    }

    // ── Round-trip: multi-target ladder survives JSON serialization ───────

    [Test]
    public void MultiTarget_RoundTripsViaJson()
    {
        var def = Stock.Ticker("T")
            .Long()
            .StopLossPercent(5)
            .TakeProfit(11.0, 13.0, 16.0)
            .Build();

        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));

        Assert.Multiple(() =>
        {
            Assert.That(restored.TakeProfitPrice, Is.EqualTo(11.0),
                "TakeProfitPrice (T1) must survive JSON round-trip");
            Assert.That(restored.TakeProfitTargets.Select(t => t.Price),
                Is.EqualTo(new[] { 11.0, 13.0, 16.0 }),
                "full three-rung ladder must survive JSON round-trip");
        });
    }

    // ── Proof of divergence: backtester signal has 3 targets; GapperExit sees 1 ──

    [Test]
    public void LiveExitVsBacktester_Divergence_LiveExitsEntirePositionAtT1()
    {
        // At T1 close, GapperExitEvaluator fires a FULL exit (Bug 3 — no partial scale-out).
        // A backtester using TradeSignal.Targets would scale out 33% at T1 and hold T2/T3.
        var def = Stock.Ticker("T")
            .Long()
            .StopLossPercent(5)
            .TakeProfit(11.0, 13.0, 15.0)
            .Build();

        var candles = new[] { PostEntry(11.1, 11.0, minutesAfter: 30) };

        var exitDecision = GapperExitEvaluator.Evaluate(def, 10.0, EntryUtc, candles, NowUtc);

        // Live path: full exit at T1.
        Assert.That(exitDecision?.Reason, Is.EqualTo(GapperExitReason.TargetHit),
            "live exit fires at T1 close");

        // Entry signal: all three targets available for a backtester.
        var start = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
        var entryCandleList = new List<Candle>
        {
            new() { Symbol = "T", StartUtc = start, EndUtc = start.AddMinutes(1),
                    Open = 10m, High = 10.5m, Low = 9.9m, Close = 10m, Volume = 2_000_000 },
        };
        var signals = new DslStrategy(def).Evaluate("T", entryCandleList, new StrategyContext());
        Assert.That(signals[0].Targets, Has.Count.EqualTo(3),
            "backtester signal carries T1/T2/T3 — DIVERGES from live exit (Bug 3)");
    }
}
