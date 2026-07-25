using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Entry condition × Exit configuration × Direction pipeline matrix.
///
/// Proves that adding an exit configuration to a StrategyDefinition does NOT
/// alter the entry-condition evaluation path.  A broken exit config that
/// corrupts EntryConditions, TakeProfitPrice, TrailingStopPercent, etc. could
/// cause a valid entry to be silently suppressed or an invalid entry to fire.
///
/// For each (indicator × exit config × direction) tuple:
///   1. Run the pipeline with a minimal definition (entry only, no explicit exit).
///   2. Run the pipeline with the same entry + the exit configuration applied.
///   3. Assert both produce the same number of signals.
///
/// This tests the full serialization-deserialization round-trip IS NOT needed
/// here (that is covered by StrategyPermutationMatrixTests).  We test the
/// LIVE StrategyDefinition path — the object in memory that MonitorWorker uses.
///
/// Coverage
/// ────────
///   25 indicators × 6 exit configs × 2 directions = 300 tests
/// </summary>
public class StrategyEntryExitMatrixTests
{
    // ── Infrastructure ───────────────────────────────────────────────────

    private static readonly DateTime SnapUtc = new(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<Candle> OneBars(double price = 10.0) =>
    [
        new Candle
        {
            Symbol   = "TEST",
            StartUtc = SnapUtc.AddMinutes(-1),
            EndUtc   = SnapUtc,
            Open     = (decimal)(price * 0.99),
            High     = (decimal)(price * 1.02),
            Low      = (decimal)(price * 0.98),
            Close    = (decimal)price,
            Volume   = 2_000_000,
        },
    ];

    private static StrategyContext Context(double previousClose = 9.0) => new()
    {
        PreviousClose     = (decimal)previousClose,
        EvaluationTimeUtc = SnapUtc,
    };

    private static IReadOnlyList<TradeSignal> RunPipeline(
        StrategyDefinition def, IReadOnlyList<Candle>? bars = null)
    {
        bars ??= OneBars();
        return new DslStrategy(def).Evaluate("TEST", bars, Context());
    }

    /// Minimal definition — entry condition only, no explicit exit (besides StopLoss).
    private static StrategyDefinition BaseDefWith(TradeDirection dir, ICondition cond)
    {
        var def = new StrategyDefinition
        {
            Symbol          = "TEST",
            Direction       = dir,
            StopLossPercent = 5,
            Quantity        = 1,
        };
        def.EntryConditions.Add(cond);
        return def;
    }

    /// Same entry condition plus one exit configuration applied by mutation.
    private static StrategyDefinition WithExit(TradeDirection dir, ICondition cond, string exitKey)
    {
        var def = BaseDefWith(dir, cond);
        switch (exitKey)
        {
            case "TakeProfit":
                def.TakeProfitPrice = 12.0;
                break;
            case "TakeProfitMulti":
                def.TakeProfitTargets.Add(new TakeProfitTarget { Price = 11.0, PercentToSell = 50, Label = "T1" });
                def.TakeProfitTargets.Add(new TakeProfitTarget { Price = 13.0, PercentToSell = 50, Label = "T2" });
                break;
            case "SellBy":
                def.ExitTime = new TimeSpan(9, 29, 0);
                break;
            case "TrailingStop":
                def.TrailingStopPercent = 8;
                break;
            case "PeakGiveback":
                def.PeakGivebackPercent = 25;
                def.PeakGivebackArmTime = new TimeSpan(9, 15, 0);
                break;
            case "Combo":
                def.TakeProfitPrice = 12.0;
                def.ExitTime        = new TimeSpan(9, 29, 0);
                break;
        }
        return def;
    }

    private static ICondition[] AllIndicatorConditions() =>
    [
        new IndicatorCondition(IndicatorType.VwapAbove),
        new IndicatorCondition(IndicatorType.VwapBelow),
        new IndicatorCondition(IndicatorType.VwapReclaim),
        new IndicatorCondition(IndicatorType.VwapLoss),
        new IndicatorCondition(IndicatorType.EmaAbove, 9),
        new IndicatorCondition(IndicatorType.EmaBelow, 9),
        new IndicatorCondition(IndicatorType.BetweenEma, 9, 21),
        new IndicatorCondition(IndicatorType.EmaStack),
        new IndicatorCondition(IndicatorType.ReclaimEma, 9),
        new IndicatorCondition(IndicatorType.DiPositive),
        new IndicatorCondition(IndicatorType.DiNegative),
        new IndicatorCondition(IndicatorType.AdxAbove, 25),
        new IndicatorCondition(IndicatorType.RsiOversold, 30),
        new IndicatorCondition(IndicatorType.RsiOverbought, 70),
        new IndicatorCondition(IndicatorType.RsiBullishDivergence),
        new IndicatorCondition(IndicatorType.RsiBearishDivergence),
        new IndicatorCondition(IndicatorType.HigherLow),
        new IndicatorCondition(IndicatorType.LowerHigh),
        new IndicatorCondition(IndicatorType.MacdBullish),
        new IndicatorCondition(IndicatorType.MacdBearish),
        new IndicatorCondition(IndicatorType.GapUp, 5),
        new IndicatorCondition(IndicatorType.GapDown, 5),
        new IndicatorCondition(IndicatorType.VolumeAbove, 1),
        new IndicatorCondition(IndicatorType.AtSupport, 2),
        new IndicatorCondition(IndicatorType.AtResistance, 2),
    ];

    private static readonly string[] ExitKeys =
    [
        "TakeProfit", "TakeProfitMulti", "SellBy", "TrailingStop", "PeakGiveback", "Combo",
    ];

    // ── Generator ────────────────────────────────────────────────────────

    private static IEnumerable<TestCaseData> EntryExitDirectionCases()
    {
        var indicators = AllIndicatorConditions();
        int idx = 0;
        foreach (var cond in indicators)
        {
            var iType = (IndicatorType)idx++;
            foreach (var exitKey in ExitKeys)
                foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
                    yield return new TestCaseData(iType, cond, exitKey, dir)
                        .SetName($"EntryExit_{iType}_{exitKey}_{dir}");
        }
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(EntryExitDirectionCases))]
    public void EntryCondition_WithExitConfig_FiresSameAsEntryAlone(
        IndicatorType ind, ICondition cond, string exitKey, TradeDirection dir)
    {
        // Determine expected outcome using pipeline-first ground truth (entry only).
        var baseDef     = BaseDefWith(dir, cond);
        var expectedCnt = RunPipeline(baseDef).Count;

        // Apply exit config on top of the same entry condition.
        // Fresh cond instance needed — create a new one by re-creating from the indicator type.
        // (IndicatorCondition is immutable so sharing is safe; PriceLevelCondition is stateful
        //  but not used here — all conds are IndicatorConditions which have no state.)
        var withExitDef = WithExit(dir, cond, exitKey);
        var actualCnt   = RunPipeline(withExitDef).Count;

        Assert.That(actualCnt, Is.EqualTo(expectedCnt),
            $"[{dir}] {ind} + exit={exitKey}: entry evaluation must not be affected by exit config " +
            $"(base={expectedCnt} withExit={actualCnt})");
    }
}
