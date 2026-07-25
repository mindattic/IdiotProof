using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// 3-way AND composition matrix: Indicator × Pattern × PriceLevel × Direction.
///
/// The existing cross-dimension tests cover all PAIRS:
///   Indicator × Pattern     (175 Long + 175 Short = 350)
///   Indicator × PriceLevel  (125 Long + 125 Short = 250)
///   Pattern  × PriceLevel   ( 35 Long +  35 Short =  70)
///
/// This file closes the 3-way gap: Indicator × Pattern × PriceLevel.
/// 25 indicators × 7 patterns × 5 price levels × 2 directions = 1,750 tests.
///
/// Why this matters: pair coverage proves AND semantics hold for any two
/// conditions, but DOES NOT prove they hold for a three-way AND — a bug in
/// DslStrategy's evaluation loop could short-circuit after two conditions,
/// never evaluating the third.  The 3-way matrix makes any such truncation
/// audible.
///
/// Test pattern:
///   soloA = RunPipeline(DefWith(dir, indicator))
///   soloB = RunPipeline(DefWith(dir, pattern))
///   soloC = RunPipeline(DefWith(dir, priceLevelCondition₁))
///   combined = RunPipeline(DefWith(dir, indicator, pattern, priceLevelCondition₂))
///   Assert combined == (soloA &amp;&amp; soloB &amp;&amp; soloC) ? 1 : 0
///
/// PriceLevelCondition is STATEFUL — a fresh instance is created for soloC
/// and a separate fresh instance for the combined run.
///
/// Coverage: 25 × 7 × 5 × 2 = 1,750 tests
/// </summary>
public class StrategyThreeWayAndMatrixTests
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

    private static StrategyDefinition DefWith(TradeDirection dir, params ICondition[] conditions)
    {
        var def = new StrategyDefinition
        {
            Symbol          = "TEST",
            Direction       = dir,
            StopLossPercent = 5,
            Quantity        = 1,
        };
        foreach (var c in conditions)
            def.EntryConditions.Add(c);
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

    private static IEnumerable<(PriceLevelType type, double level)> PriceLevelPassCases()
    {
        yield return (PriceLevelType.HoldsAbove, 9.0);
        yield return (PriceLevelType.HoldsBelow, 11.0);
        yield return (PriceLevelType.Near, 10.0);
        yield return (PriceLevelType.BreaksAbove, 9.5);
        yield return (PriceLevelType.BreaksBelow, 10.5);
    }

    // ── Generator: 25 × 7 × 5 × 2 = 1,750 ──────────────────────────────

    private static IEnumerable<TestCaseData> ThreeWayAndCases()
    {
        var indicators = AllIndicatorConditions();
        int iIdx = 0;
        foreach (var indCond in indicators)
        {
            var iType = (IndicatorType)iIdx++;
            foreach (var pat in Enum.GetValues<PatternType>())
            {
                foreach (var (lvlType, level) in PriceLevelPassCases())
                {
                    foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
                    {
                        yield return new TestCaseData(iType, indCond, pat, lvlType, level, dir)
                            .SetName($"And3_{iType}_{pat}_{lvlType}_{dir}");
                    }
                }
            }
        }
    }

    // ── Test ─────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(ThreeWayAndCases))]
    public void ThreeWayAnd_Indicator_Pattern_PriceLevel_AndSemanticsHold(
        IndicatorType iType, ICondition indCond, PatternType pat,
        PriceLevelType lvlType, double level, TradeDirection dir)
    {
        var patCond = new PatternCondition(pat);
        // Fresh PriceLevelCondition instances — stateful, must not share across pipeline calls.
        var priceSolo     = new PriceLevelCondition(lvlType, level);
        var priceCombined = new PriceLevelCondition(lvlType, level);

        var bars   = OneBars();
        var soloA  = RunPipeline(DefWith(dir, indCond),     bars).Count == 1;
        var soloB  = RunPipeline(DefWith(dir, patCond),     bars).Count == 1;
        var soloC  = RunPipeline(DefWith(dir, priceSolo),   bars).Count == 1;
        var combined = RunPipeline(DefWith(dir, indCond, patCond, priceCombined), bars).Count;
        var expected = soloA && soloB && soloC ? 1 : 0;

        Assert.That(combined, Is.EqualTo(expected),
            $"[{dir}] AND3({iType},{pat},{lvlType}@{level}): " +
            $"soloA={soloA} soloB={soloB} soloC={soloC} expected={expected} actual={combined}");
    }
}
