using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Short-direction variants of all Long-only cross-dimension and composition
/// tests in StrategyPermutationMatrixTests and StrategyCompositionTreeTests.
///
/// Rationale: direction is a first-class dimension in every strategy.
/// The existing AND/OR/NOT/triple/quad/cross-dim tests only assert Long
/// semantics (the AssertAndSemantics helper defaults to Long).  A broken
/// Short-direction path could silently allow a buy signal to fire as a
/// sell (or vice versa), or suppress a valid Short entry.  These tests
/// close that gap by running the same pipeline-first ground truth with
/// TradeDirection.Short as the direction.
///
/// Coverage (all Short direction)
/// ──────────────────────────────
///   Adjacent AND pairs          24 tests
///   Indicator × Pattern        175 tests
///   Indicator × PriceLevel     125 tests
///   Pattern × PriceLevel        35 tests
///   Adjacent OR pairs           24 tests
///   NOT inversion               25 tests
///   Double-NOT identity         25 tests
///   3-way AND triples           23 tests
///   4-way AND quads             22 tests
///   AlwaysPass OR Indicator     25 tests
/// Total:                       503 tests
/// </summary>
public class StrategyShortDirectionPermutationTests
{
    // ── Shared infrastructure (mirrors StrategyPermutationMatrixTests) ────

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
        StrategyDefinition def, IReadOnlyList<Candle>? bars = null, double previousClose = 9.0)
    {
        bars ??= OneBars();
        return new DslStrategy(def).Evaluate("TEST", bars, Context(previousClose));
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

    private static void AssertAndSemantics(ICondition condA, ICondition condB,
        TradeDirection dir, IReadOnlyList<Candle>? bars = null)
    {
        bars ??= OneBars();
        var soloA    = RunPipeline(DefWith(dir, condA),        bars).Count == 1;
        var soloB    = RunPipeline(DefWith(dir, condB),        bars).Count == 1;
        var combined = RunPipeline(DefWith(dir, condA, condB), bars).Count;
        var expected = soloA && soloB ? 1 : 0;
        Assert.That(combined, Is.EqualTo(expected),
            $"[Short] AND({condA.GetType().Name},{condB.GetType().Name}): " +
            $"soloA={soloA} soloB={soloB} expected={expected} actual={combined}");
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

    // ── Short AND adjacent pairs (24 tests) ─────────────────────────────

    private static IEnumerable<TestCaseData> ShortAdjacentPairCases()
    {
        var all = AllIndicatorConditions();
        for (int i = 0; i < all.Length - 1; i++)
            yield return new TestCaseData(all[i], all[i + 1])
                .SetName($"Short_AndChain_{(IndicatorType)i}_And_{(IndicatorType)(i + 1)}");
    }

    [TestCaseSource(nameof(ShortAdjacentPairCases))]
    public void Short_TwoIndicatorAndChain_CombinedMatchesSoloAnd(ICondition condA, ICondition condB)
        => AssertAndSemantics(condA, condB, TradeDirection.Short);

    // ── Short Indicator × Pattern (175 tests) ───────────────────────────

    private static IEnumerable<TestCaseData> ShortIndicatorPatternCases()
    {
        var indicators = AllIndicatorConditions();
        var patterns   = Enum.GetValues<PatternType>();
        int idx = 0;
        foreach (var ind in indicators)
        {
            var iType = (IndicatorType)idx++;
            foreach (var pat in patterns)
                yield return new TestCaseData(ind, pat)
                    .SetName($"Short_Cross_Ind_{iType}_Pat_{pat}");
        }
    }

    [TestCaseSource(nameof(ShortIndicatorPatternCases))]
    public void Short_IndicatorAndPattern_AndSemanticsHold(ICondition indCond, PatternType pattern)
    {
        var patCond = new PatternCondition(pattern);
        AssertAndSemantics(indCond, patCond, TradeDirection.Short);
    }

    // ── Short Indicator × PriceLevel (125 tests) ────────────────────────

    private static IEnumerable<TestCaseData> ShortIndicatorPriceLevelCases()
    {
        var indicators = AllIndicatorConditions();
        int idx = 0;
        foreach (var ind in indicators)
        {
            var iType = (IndicatorType)idx++;
            foreach (var (lvlType, level) in PriceLevelPassCases())
                yield return new TestCaseData(iType, lvlType, ind, level)
                    .SetName($"Short_Cross_Ind_{iType}_Lvl_{lvlType}");
        }
    }

    [TestCaseSource(nameof(ShortIndicatorPriceLevelCases))]
    public void Short_IndicatorAndPriceLevel_AndSemanticsHold(
        IndicatorType _, PriceLevelType lvlType, ICondition indCond, double level)
    {
        var priceCond = new PriceLevelCondition(lvlType, level);
        AssertAndSemantics(indCond, priceCond, TradeDirection.Short);
    }

    // ── Short Pattern × PriceLevel (35 tests) ───────────────────────────

    private static IEnumerable<TestCaseData> ShortPatternPriceLevelCases()
    {
        foreach (var pat in Enum.GetValues<PatternType>())
            foreach (var (lvlType, level) in PriceLevelPassCases())
                yield return new TestCaseData(pat, lvlType, level)
                    .SetName($"Short_Cross_Pat_{pat}_Lvl_{lvlType}");
    }

    [TestCaseSource(nameof(ShortPatternPriceLevelCases))]
    public void Short_PatternAndPriceLevel_AndSemanticsHold(
        PatternType pattern, PriceLevelType lvlType, double level)
    {
        AssertAndSemantics(new PatternCondition(pattern), new PriceLevelCondition(lvlType, level), TradeDirection.Short);
    }

    // ── Short OR adjacent pairs (24 tests) ──────────────────────────────

    private static IEnumerable<TestCaseData> ShortAdjacentOrPairCases()
    {
        var all = AllIndicatorConditions();
        for (int i = 0; i < all.Length - 1; i++)
        {
            var iA = (IndicatorType)i;
            var iB = (IndicatorType)(i + 1);
            yield return new TestCaseData(all[i], all[i + 1])
                .SetName($"Short_Or_{iA}_And_{iB}");
        }
    }

    [TestCaseSource(nameof(ShortAdjacentOrPairCases))]
    public void Short_OrCondition_PipelineMatchesOrSemantics(ICondition condA, ICondition condB)
    {
        var dir      = TradeDirection.Short;
        var bars     = OneBars();
        var soloA    = RunPipeline(DefWith(dir, condA), bars).Count == 1;
        var soloB    = RunPipeline(DefWith(dir, condB), bars).Count == 1;
        var expected = soloA || soloB ? 1 : 0;
        var actual   = RunPipeline(DefWith(dir, new OrCondition(condA, condB)), bars).Count;
        Assert.That(actual, Is.EqualTo(expected),
            $"[Short] OR semantics: soloA={soloA} soloB={soloB}");
    }

    // ── Short NOT inversion (25 tests) ──────────────────────────────────

    private static IEnumerable<TestCaseData> ShortNotCases()
    {
        var all = AllIndicatorConditions();
        int idx = 0;
        foreach (var cond in all)
            yield return new TestCaseData((IndicatorType)idx++, cond)
                .SetName($"Short_Not_{(IndicatorType)(idx - 1)}");
    }

    [TestCaseSource(nameof(ShortNotCases))]
    public void Short_NotCondition_InvertsIndicatorEvaluation(IndicatorType ind, ICondition cond)
    {
        var dir      = TradeDirection.Short;
        var bars     = OneBars();
        var soloFires = RunPipeline(DefWith(dir, cond), bars).Count == 1;
        var notFires  = RunPipeline(DefWith(dir, new NotCondition(cond)), bars).Count == 1;
        Assert.That(notFires, Is.EqualTo(!soloFires),
            $"[Short] NOT({ind}): original={soloFires} negated={notFires}");
    }

    // ── Short Double-NOT identity (25 tests) ─────────────────────────────

    private static IEnumerable<TestCaseData> ShortDoubleNotCases()
    {
        var all = AllIndicatorConditions();
        int idx = 0;
        foreach (var cond in all)
            yield return new TestCaseData((IndicatorType)idx++, cond)
                .SetName($"Short_DoubleNot_{(IndicatorType)(idx - 1)}");
    }

    [TestCaseSource(nameof(ShortDoubleNotCases))]
    public void Short_DoubleNotCondition_IsIdentity(IndicatorType ind, ICondition cond)
    {
        var dir      = TradeDirection.Short;
        var bars     = OneBars();
        var soloFires   = RunPipeline(DefWith(dir, cond), bars).Count;
        var doubleNot   = RunPipeline(DefWith(dir, new NotCondition(new NotCondition(cond))), bars).Count;
        Assert.That(doubleNot, Is.EqualTo(soloFires),
            $"[Short] NOT(NOT({ind})): should equal solo={soloFires}");
    }

    // ── Short 3-way AND triples (23 tests) ──────────────────────────────

    private static IEnumerable<TestCaseData> ShortTripleCases()
    {
        var all = AllIndicatorConditions();
        for (int i = 0; i < all.Length - 2; i++)
        {
            var ta = (IndicatorType)i;
            var tb = (IndicatorType)(i + 1);
            var tc = (IndicatorType)(i + 2);
            yield return new TestCaseData(all[i], all[i + 1], all[i + 2])
                .SetName($"Short_And3_{ta}_And_{tb}_And_{tc}");
        }
    }

    [TestCaseSource(nameof(ShortTripleCases))]
    public void Short_ThreeConditionAndChain_MatchesSoloAndSemantics(
        ICondition condA, ICondition condB, ICondition condC)
    {
        var dir      = TradeDirection.Short;
        var bars     = OneBars();
        var soloA    = RunPipeline(DefWith(dir, condA),                bars).Count == 1;
        var soloB    = RunPipeline(DefWith(dir, condB),                bars).Count == 1;
        var soloC    = RunPipeline(DefWith(dir, condC),                bars).Count == 1;
        var combined = RunPipeline(DefWith(dir, condA, condB, condC),  bars).Count;
        var expected = soloA && soloB && soloC ? 1 : 0;
        Assert.That(combined, Is.EqualTo(expected),
            $"[Short] AND3: soloA={soloA} soloB={soloB} soloC={soloC}");
    }

    // ── Short 4-way AND quads (22 tests) ────────────────────────────────

    private static IEnumerable<TestCaseData> ShortQuadCases()
    {
        var all = AllIndicatorConditions();
        for (int i = 0; i < all.Length - 3; i++)
        {
            var ta = (IndicatorType)i;
            var tb = (IndicatorType)(i + 1);
            var tc = (IndicatorType)(i + 2);
            var td = (IndicatorType)(i + 3);
            yield return new TestCaseData(all[i], all[i + 1], all[i + 2], all[i + 3])
                .SetName($"Short_And4_{ta}_And_{tb}_And_{tc}_And_{td}");
        }
    }

    [TestCaseSource(nameof(ShortQuadCases))]
    public void Short_FourConditionAndChain_MatchesSoloAndSemantics(
        ICondition condA, ICondition condB, ICondition condC, ICondition condD)
    {
        var dir      = TradeDirection.Short;
        var bars     = OneBars();
        var soloA    = RunPipeline(DefWith(dir, condA), bars).Count == 1;
        var soloB    = RunPipeline(DefWith(dir, condB), bars).Count == 1;
        var soloC    = RunPipeline(DefWith(dir, condC), bars).Count == 1;
        var soloD    = RunPipeline(DefWith(dir, condD), bars).Count == 1;
        var combined = RunPipeline(DefWith(dir, condA, condB, condC, condD), bars).Count;
        var expected = soloA && soloB && soloC && soloD ? 1 : 0;
        Assert.That(combined, Is.EqualTo(expected),
            $"[Short] AND4: soloA={soloA} soloB={soloB} soloC={soloC} soloD={soloD}");
    }

    // ── Short AlwaysPass OR Indicator (25 tests) ─────────────────────────

    private static IEnumerable<TestCaseData> ShortPriceBandOrIndicatorCases()
    {
        var all = AllIndicatorConditions();
        int idx = 0;
        foreach (var cond in all)
            yield return new TestCaseData((IndicatorType)idx++, cond)
                .SetName($"Short_PriceBandOr_{(IndicatorType)(idx - 1)}");
    }

    [TestCaseSource(nameof(ShortPriceBandOrIndicatorCases))]
    public void Short_AlwaysPass_Or_Indicator_AlwaysFires(IndicatorType ind, ICondition cond)
    {
        var dir    = TradeDirection.Short;
        var bars   = OneBars();
        var always = new PriceBandCondition(0, 1000);
        // OR(alwaysPass, anything) must always fire — the always-pass side ensures 1 signal.
        var actual = RunPipeline(DefWith(dir, new OrCondition(always, cond)), bars).Count;
        Assert.That(actual, Is.EqualTo(1),
            $"[Short] OR(AlwaysPass, {ind}) must always fire — alwaysPass guarantees the OR side");
    }
}
