using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Composition tree tests: OR, NOT, double-NOT, 3-way AND, and mixed AND(OR,C).
///
/// These prove the pipeline correctly handles non-trivial boolean trees —
/// not just flat AND-chains of atomic conditions.  Every test uses the
/// "pipeline-first ground truth" pattern: solo results are obtained via
/// DslStrategy.Evaluate, then combined results are compared against the
/// expected logical outcome.
///
/// Stateless conditions (IndicatorCondition, PatternCondition,
/// PriceBandCondition, GapBandCondition) are safe to share across
/// evaluation calls; stateful PriceLevelCondition uses fresh instances.
///
/// Coverage
/// ────────
///   OR-semantics explicit (PriceBandCondition) ......... 4 tests
///   NOT-semantics explicit (PriceBandCondition) ........ 4 tests
///   OR-semantics all adjacent indicator pairs ......... 24 tests
///   OR-semantics all 7 PatternType pairs ............... 6 tests
///   NOT-semantics all 25 IndicatorTypes ............... 25 tests
///   Double-NOT identity for all 25 indicators ......... 25 tests
///   NOT-semantics all 7 PatternTypes ................... 7 tests
///   3-way AND-chain all adjacent triples .............. 23 tests
///   4-condition AND all adjacent quads ................ 22 tests
///   Mixed AND(OR(A,B), C) with PriceBandCondition ...... 3 tests
///   Mixed NOT-in-AND chain ............................ 3 tests
///   GapBandCondition routing ........................... 4 tests
///   PriceBandCondition × IndicatorCondition OR ........ 25 tests
/// </summary>
public class StrategyCompositionTreeTests
{
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

    private static StrategyContext Ctx(double previousClose = 9.0) => new()
    {
        PreviousClose     = (decimal)previousClose,
        EvaluationTimeUtc = SnapUtc,
    };

    private static int RunSolo(ICondition cond, TradeDirection dir = TradeDirection.Long)
    {
        var def = new StrategyDefinition
        {
            Symbol = "TEST", Direction = dir, StopLossPercent = 5, Quantity = 1,
        };
        def.EntryConditions.Add(cond);
        return new DslStrategy(def).Evaluate("TEST", OneBars(), Ctx()).Count;
    }

    private static int RunMulti(TradeDirection dir, params ICondition[] conditions)
    {
        var def = new StrategyDefinition
        {
            Symbol = "TEST", Direction = dir, StopLossPercent = 5, Quantity = 1,
        };
        foreach (var c in conditions)
            def.EntryConditions.Add(c);
        return new DslStrategy(def).Evaluate("TEST", OneBars(), Ctx()).Count;
    }

    // ── Deterministic OR semantics (PriceBandCondition) ───────────────────

    [Test]
    public void Or_BothFail_DoesNotFire()
    {
        // Both bands exclude price=10
        var or = new OrCondition(new PriceBandCondition(50, 100), new PriceBandCondition(200, 300));
        Assert.That(RunSolo(or), Is.EqualTo(0), "OR(fail, fail) must not fire");
    }

    [Test]
    public void Or_LeftPassRightFail_Fires()
    {
        var or = new OrCondition(new PriceBandCondition(0, 100), new PriceBandCondition(200, 300));
        Assert.That(RunSolo(or), Is.EqualTo(1), "OR(pass, fail) must fire");
    }

    [Test]
    public void Or_LeftFailRightPass_Fires()
    {
        var or = new OrCondition(new PriceBandCondition(200, 300), new PriceBandCondition(0, 100));
        Assert.That(RunSolo(or), Is.EqualTo(1), "OR(fail, pass) must fire");
    }

    [Test]
    public void Or_BothPass_Fires()
    {
        var or = new OrCondition(new PriceBandCondition(0, 100), new PriceBandCondition(5, 15));
        Assert.That(RunSolo(or), Is.EqualTo(1), "OR(pass, pass) must fire");
    }

    // ── Deterministic NOT semantics (PriceBandCondition) ─────────────────

    [Test]
    public void Not_OfPass_DoesNotFire()
    {
        var not = new NotCondition(new PriceBandCondition(0, 100));
        Assert.That(RunSolo(not), Is.EqualTo(0), "NOT(pass) must not fire");
    }

    [Test]
    public void Not_OfFail_Fires()
    {
        var not = new NotCondition(new PriceBandCondition(50, 100));
        Assert.That(RunSolo(not), Is.EqualTo(1), "NOT(fail) must fire");
    }

    [Test]
    public void Not_Not_OfPass_Fires()
    {
        var notnot = new NotCondition(new NotCondition(new PriceBandCondition(0, 100)));
        Assert.That(RunSolo(notnot), Is.EqualTo(1), "NOT(NOT(pass)) must fire (double negation)");
    }

    [Test]
    public void Not_Not_OfFail_DoesNotFire()
    {
        var notnot = new NotCondition(new NotCondition(new PriceBandCondition(50, 100)));
        Assert.That(RunSolo(notnot), Is.EqualTo(0), "NOT(NOT(fail)) must not fire (double negation)");
    }

    // ── OR-semantics all adjacent indicator pairs (24 tests) ─────────────

    private static IEnumerable<TestCaseData> AdjacentOrPairCases()
    {
        var indicators = AllIndicatorConditions().ToArray();
        for (var i = 0; i < indicators.Length - 1; i++)
        {
            var (typeA, condA) = indicators[i];
            var (typeB, condB) = indicators[i + 1];
            yield return new TestCaseData(typeA, typeB, condA, condB)
                .SetName($"Or_{typeA}_And_{typeB}");
        }
    }

    [TestCaseSource(nameof(AdjacentOrPairCases))]
    public void OrCondition_PipelineMatchesExpectedOrSemantics(
        IndicatorType _, IndicatorType __, ICondition condA, ICondition condB)
    {
        var soloA    = RunSolo(condA) == 1;
        var soloB    = RunSolo(condB) == 1;
        var expected = soloA || soloB ? 1 : 0;
        var actual   = RunSolo(new OrCondition(condA, condB));

        Assert.That(actual, Is.EqualTo(expected),
            $"OR({condA.ToScript()}, {condB.ToScript()}): solo=({soloA}, {soloB}) expected OR={expected}");
    }

    // ── OR-semantics adjacent Pattern pairs (6 tests) ─────────────────────

    private static IEnumerable<TestCaseData> AdjacentPatternOrPairCases()
    {
        var patterns = Enum.GetValues<PatternType>();
        for (var i = 0; i < patterns.Length - 1; i++)
        {
            var a = new PatternCondition(patterns[i]);
            var b = new PatternCondition(patterns[i + 1]);
            yield return new TestCaseData(patterns[i], patterns[i + 1], a, b)
                .SetName($"Or_Pattern_{patterns[i]}_And_{patterns[i + 1]}");
        }
    }

    [TestCaseSource(nameof(AdjacentPatternOrPairCases))]
    public void OrCondition_PatternPair_PipelineMatchesOrSemantics(
        PatternType _, PatternType __, ICondition a, ICondition b)
    {
        var soloA    = RunSolo(a) == 1;
        var soloB    = RunSolo(b) == 1;
        var expected = soloA || soloB ? 1 : 0;
        var actual   = RunSolo(new OrCondition(a, b));

        Assert.That(actual, Is.EqualTo(expected),
            $"OR({a.ToScript()}, {b.ToScript()}): expected {expected}");
    }

    // ── NOT-semantics all 25 IndicatorTypes (25 tests) ────────────────────

    private static IEnumerable<TestCaseData> NotIndicatorCases()
    {
        foreach (var (ind, cond) in AllIndicatorConditions())
            yield return new TestCaseData(ind, cond).SetName($"Not_{ind}");
    }

    [TestCaseSource(nameof(NotIndicatorCases))]
    public void NotCondition_InvertsIndicatorEvaluation(IndicatorType _, ICondition cond)
    {
        var solo     = RunSolo(cond);
        var notted   = RunSolo(new NotCondition(cond));
        var expected = solo == 1 ? 0 : 1;

        Assert.That(notted, Is.EqualTo(expected),
            $"NOT({cond.ToScript()}): solo={solo}, NOT expected={expected}");
    }

    // ── Double-NOT identity for all 25 indicators (25 tests) ─────────────

    private static IEnumerable<TestCaseData> DoubleNotCases()
    {
        foreach (var (ind, cond) in AllIndicatorConditions())
            yield return new TestCaseData(ind, cond).SetName($"DoubleNot_{ind}");
    }

    [TestCaseSource(nameof(DoubleNotCases))]
    public void DoubleNotCondition_IsIdentity(IndicatorType _, ICondition cond)
    {
        var solo      = RunSolo(cond);
        var doubleNot = RunSolo(new NotCondition(new NotCondition(cond)));

        Assert.That(doubleNot, Is.EqualTo(solo),
            $"NOT(NOT({cond.ToScript()})): must equal solo={solo}");
    }

    // ── NOT-semantics all 7 PatternTypes (7 tests) ────────────────────────

    private static IEnumerable<TestCaseData> NotPatternCases()
    {
        foreach (var p in Enum.GetValues<PatternType>())
            yield return new TestCaseData(p).SetName($"Not_Pattern_{p}");
    }

    [TestCaseSource(nameof(NotPatternCases))]
    public void NotCondition_InvertsPatternEvaluation(PatternType pattern)
    {
        var cond     = new PatternCondition(pattern);
        var solo     = RunSolo(cond);
        var notted   = RunSolo(new NotCondition(cond));
        var expected = solo == 1 ? 0 : 1;

        Assert.That(notted, Is.EqualTo(expected),
            $"NOT(Pattern {pattern}): solo={solo}, NOT expected={expected}");
    }

    // ── 3-way AND-chain all adjacent triples (23 tests) ──────────────────

    private static IEnumerable<TestCaseData> AdjacentTripleCases()
    {
        var indicators = AllIndicatorConditions().ToArray();
        for (var i = 0; i < indicators.Length - 2; i++)
        {
            var (ta, ca) = indicators[i];
            var (tb, cb) = indicators[i + 1];
            var (tc, cc) = indicators[i + 2];
            yield return new TestCaseData(ta, tb, tc, ca, cb, cc)
                .SetName($"And3_{ta}_And_{tb}_And_{tc}");
        }
    }

    [TestCaseSource(nameof(AdjacentTripleCases))]
    public void ThreeConditionAndChain_MatchesSoloAndSemantics(
        IndicatorType _, IndicatorType __, IndicatorType ___,
        ICondition condA, ICondition condB, ICondition condC)
    {
        var soloA    = RunSolo(condA) == 1;
        var soloB    = RunSolo(condB) == 1;
        var soloC    = RunSolo(condC) == 1;
        var expected = soloA && soloB && soloC ? 1 : 0;
        var actual   = RunMulti(TradeDirection.Long, condA, condB, condC);

        Assert.That(actual, Is.EqualTo(expected),
            $"{condA.ToScript()} AND {condB.ToScript()} AND {condC.ToScript()}: " +
            $"solo=({soloA},{soloB},{soloC}) expected={expected}");
    }

    // ── 4-condition AND-chain all adjacent quads (22 tests) ───────────────

    private static IEnumerable<TestCaseData> AdjacentQuadCases()
    {
        var indicators = AllIndicatorConditions().ToArray();
        for (var i = 0; i < indicators.Length - 3; i++)
        {
            var (ta, ca) = indicators[i];
            var (tb, cb) = indicators[i + 1];
            var (tc, cc) = indicators[i + 2];
            var (td, cd) = indicators[i + 3];
            yield return new TestCaseData(ta, tb, tc, td, ca, cb, cc, cd)
                .SetName($"And4_{ta}_And_{tb}_And_{tc}_And_{td}");
        }
    }

    [TestCaseSource(nameof(AdjacentQuadCases))]
    public void FourConditionAndChain_MatchesSoloAndSemantics(
        IndicatorType _, IndicatorType __, IndicatorType ___, IndicatorType ____,
        ICondition condA, ICondition condB, ICondition condC, ICondition condD)
    {
        var soloA    = RunSolo(condA) == 1;
        var soloB    = RunSolo(condB) == 1;
        var soloC    = RunSolo(condC) == 1;
        var soloD    = RunSolo(condD) == 1;
        var expected = soloA && soloB && soloC && soloD ? 1 : 0;
        var actual   = RunMulti(TradeDirection.Long, condA, condB, condC, condD);

        Assert.That(actual, Is.EqualTo(expected),
            $"4-AND: solo=({soloA},{soloB},{soloC},{soloD}) expected={expected}");
    }

    // ── Mixed AND(OR(A,B), C) composition (3 tests) ───────────────────────

    [Test]
    public void Mixed_And_Or_Pass_Pass_And_Pass_Fires()
    {
        // AND(OR(always-pass, always-fail), always-pass) → (true OR false) AND true = true
        var orCond  = new OrCondition(new PriceBandCondition(0, 100), new PriceBandCondition(50, 100));
        var andGate = new PriceBandCondition(0, 100);
        Assert.That(RunMulti(TradeDirection.Long, orCond, andGate), Is.EqualTo(1),
            "AND(OR(pass,fail), pass) must fire");
    }

    [Test]
    public void Mixed_And_Or_Fail_Fail_And_Pass_DoesNotFire()
    {
        // AND(OR(always-fail, always-fail), always-pass) → false AND true = false
        var orCond  = new OrCondition(new PriceBandCondition(50, 100), new PriceBandCondition(100, 200));
        var andGate = new PriceBandCondition(0, 100);
        Assert.That(RunMulti(TradeDirection.Long, orCond, andGate), Is.EqualTo(0),
            "AND(OR(fail,fail), pass) must not fire");
    }

    [Test]
    public void Mixed_And_Or_Pass_And_Fail_DoesNotFire()
    {
        // AND(OR(always-pass, always-pass), always-fail) → true AND false = false
        var orCond  = new OrCondition(new PriceBandCondition(0, 100), new PriceBandCondition(5, 15));
        var andGate = new PriceBandCondition(50, 100);
        Assert.That(RunMulti(TradeDirection.Long, orCond, andGate), Is.EqualTo(0),
            "AND(OR(pass,pass), fail) must not fire");
    }

    // ── NOT-in-AND chain (3 tests) ────────────────────────────────────────

    [Test]
    public void Not_In_And_Chain_BothNot_Fires_WhenBothCondsFail()
    {
        // NOT(fail) AND NOT(fail) = true AND true = true
        var notA = new NotCondition(new PriceBandCondition(50, 100));
        var notB = new NotCondition(new PriceBandCondition(100, 200));
        Assert.That(RunMulti(TradeDirection.Long, notA, notB), Is.EqualTo(1),
            "NOT(fail) AND NOT(fail) must fire");
    }

    [Test]
    public void Not_In_And_Chain_FirstNotFires_SecondPassBlocks()
    {
        // NOT(fail) AND pass — wait, "pass" here means the condition passes but we want blockage
        // Let me use: NOT(fail) AND fail = true AND false = false
        var notA = new NotCondition(new PriceBandCondition(50, 100));
        var failB = new PriceBandCondition(50, 100);
        Assert.That(RunMulti(TradeDirection.Long, notA, failB), Is.EqualTo(0),
            "NOT(fail) AND fail must not fire");
    }

    [Test]
    public void Not_In_And_Chain_FirstFails_BlocksRest()
    {
        // NOT(pass) AND anything = false AND ... = false
        var notPassA = new NotCondition(new PriceBandCondition(0, 100));
        var passB    = new PriceBandCondition(0, 100);
        Assert.That(RunMulti(TradeDirection.Long, notPassA, passB), Is.EqualTo(0),
            "NOT(pass) AND pass must not fire");
    }

    // ── GapBandCondition routing (4 tests) ────────────────────────────────

    [Test]
    public void GapBand_InRange_Fires()
    {
        // gap = (10-9)/9 ≈ 11.1% — fits [5%, 20%]
        var def = new StrategyDefinition
        {
            Symbol = "TEST", Direction = TradeDirection.Long, StopLossPercent = 5, Quantity = 1,
        };
        def.EntryConditions.Add(new GapBandCondition(5, 20));
        Assert.That(new DslStrategy(def).Evaluate("TEST", OneBars(), Ctx(9.0)).Count, Is.EqualTo(1));
    }

    [Test]
    public void GapBand_OutOfRange_DoesNotFire()
    {
        var def = new StrategyDefinition
        {
            Symbol = "TEST", Direction = TradeDirection.Long, StopLossPercent = 5, Quantity = 1,
        };
        def.EntryConditions.Add(new GapBandCondition(50, 100));
        Assert.That(new DslStrategy(def).Evaluate("TEST", OneBars(), Ctx(9.0)).Count, Is.EqualTo(0));
    }

    [Test]
    public void GapBand_PreviousCloseAbsent_FailsClosed()
    {
        var def = new StrategyDefinition
        {
            Symbol = "TEST", Direction = TradeDirection.Long, StopLossPercent = 5, Quantity = 1,
        };
        def.EntryConditions.Add(new GapBandCondition(5, 20));
        // No PreviousClose in context → gap unknown → fail closed
        Assert.That(new DslStrategy(def).Evaluate("TEST", OneBars(), new StrategyContext()).Count, Is.EqualTo(0));
    }

    [Test]
    public void GapBand_Not_GapBand_InvertsResult()
    {
        // NOT(GapBand(5,20)) when gap=11.1% → NOT(true) = false
        var notGap = new NotCondition(new GapBandCondition(5, 20));
        var def    = new StrategyDefinition
        {
            Symbol = "TEST", Direction = TradeDirection.Long, StopLossPercent = 5, Quantity = 1,
        };
        def.EntryConditions.Add(notGap);
        Assert.That(new DslStrategy(def).Evaluate("TEST", OneBars(), Ctx(9.0)).Count, Is.EqualTo(0),
            "NOT(GapBand that passes) must not fire");
    }

    // ── PriceBandCondition OR IndicatorCondition (25 tests) ───────────────

    private static IEnumerable<TestCaseData> PriceBandOrIndicatorCases()
    {
        foreach (var (ind, cond) in AllIndicatorConditions())
            yield return new TestCaseData(ind, cond).SetName($"PriceBandOr_{ind}");
    }

    [TestCaseSource(nameof(PriceBandOrIndicatorCases))]
    public void AlwaysPass_Or_Indicator_AlwaysFires(IndicatorType _, ICondition indCond)
    {
        // OR(always-pass, X) is always true regardless of X
        var orCond = new OrCondition(new PriceBandCondition(0, 1000), indCond);
        Assert.That(RunSolo(orCond), Is.EqualTo(1),
            $"OR(always-pass, {indCond.ToScript()}) must always fire");
    }

    // ── Condition factory ─────────────────────────────────────────────────

    private static IEnumerable<(IndicatorType type, ICondition cond)> AllIndicatorConditions()
    {
        yield return (IndicatorType.VwapAbove,            new IndicatorCondition(IndicatorType.VwapAbove));
        yield return (IndicatorType.VwapBelow,            new IndicatorCondition(IndicatorType.VwapBelow));
        yield return (IndicatorType.VwapReclaim,          new IndicatorCondition(IndicatorType.VwapReclaim));
        yield return (IndicatorType.VwapLoss,             new IndicatorCondition(IndicatorType.VwapLoss));
        yield return (IndicatorType.EmaAbove,             new IndicatorCondition(IndicatorType.EmaAbove, 9));
        yield return (IndicatorType.EmaBelow,             new IndicatorCondition(IndicatorType.EmaBelow, 9));
        yield return (IndicatorType.BetweenEma,           new IndicatorCondition(IndicatorType.BetweenEma, 9, 21));
        yield return (IndicatorType.EmaStack,             new IndicatorCondition(IndicatorType.EmaStack));
        yield return (IndicatorType.ReclaimEma,           new IndicatorCondition(IndicatorType.ReclaimEma, 9));
        yield return (IndicatorType.DiPositive,           new IndicatorCondition(IndicatorType.DiPositive));
        yield return (IndicatorType.DiNegative,           new IndicatorCondition(IndicatorType.DiNegative));
        yield return (IndicatorType.AdxAbove,             new IndicatorCondition(IndicatorType.AdxAbove, 25));
        yield return (IndicatorType.RsiOversold,          new IndicatorCondition(IndicatorType.RsiOversold, 30));
        yield return (IndicatorType.RsiOverbought,        new IndicatorCondition(IndicatorType.RsiOverbought, 70));
        yield return (IndicatorType.RsiBullishDivergence, new IndicatorCondition(IndicatorType.RsiBullishDivergence));
        yield return (IndicatorType.RsiBearishDivergence, new IndicatorCondition(IndicatorType.RsiBearishDivergence));
        yield return (IndicatorType.HigherLow,            new IndicatorCondition(IndicatorType.HigherLow));
        yield return (IndicatorType.LowerHigh,            new IndicatorCondition(IndicatorType.LowerHigh));
        yield return (IndicatorType.MacdBullish,          new IndicatorCondition(IndicatorType.MacdBullish));
        yield return (IndicatorType.MacdBearish,          new IndicatorCondition(IndicatorType.MacdBearish));
        yield return (IndicatorType.GapUp,                new IndicatorCondition(IndicatorType.GapUp, 5));
        yield return (IndicatorType.GapDown,              new IndicatorCondition(IndicatorType.GapDown, 5));
        yield return (IndicatorType.VolumeAbove,          new IndicatorCondition(IndicatorType.VolumeAbove, 1));
        yield return (IndicatorType.AtSupport,            new IndicatorCondition(IndicatorType.AtSupport, 2));
        yield return (IndicatorType.AtResistance,         new IndicatorCondition(IndicatorType.AtResistance, 2));
    }
}
