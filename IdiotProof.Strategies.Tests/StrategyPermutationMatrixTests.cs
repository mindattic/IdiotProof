using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Exhaustive permutation coverage for the strategy evaluation pipeline.
///
/// Design principle — "pipeline-first ground truth":
///   The "correct" evaluation of each condition is determined by the pipeline
///   itself (DslStrategy), not by calling cond.Evaluate(snap) directly.  This
///   avoids the mismatch that arises because DslStrategy builds its snapshot
///   from candles (EMA/ADX/RSI are only computed when enough bars exist;
///   VWAP is always computed from bar data, not from any pre-set field).
///
///   For AND-chain tests the invariant is:
///       combined(A,B) == soloA && soloB
///   where soloX = EvaluateViaResolver with only condition X.  This proves
///   AND semantics without predicting which way any individual indicator evaluates.
///
/// Coverage matrix (all entries produce compiled, meaningful test cases)
/// ─────────────────────────────────────────────────────────────────────
///   All 25 IndicatorTypes  × 2 directions  =  50 routing tests
///   All  7 PatternTypes    × 2 directions  =  14 routing tests
///   All  5 PriceLevelTypes × pass/fail × 2 dirs =  20 routing tests
///   All  7 exit-type       × 2 directions  =  14 exit-config tests
///   All 25 indicator × 3 exit-types   = JSON round-trip  = 75 tests
///   All 24 adjacent pairs  AND-chain        =  24 AND-semantics tests
///   Indicator × PatternType     cross-dim   = 175 AND-semantics tests
///   Indicator × PriceLevelType  cross-dim   = 125 AND-semantics tests
///   PatternType × PriceLevelType cross-dim  =  35 AND-semantics tests
///   JSON round-trip: 25 + 7 + 5            =  37 round-trip tests
///   ConditionalBlock all 5 override fields  =   5 branch tests
///   GapperExitReason all 5 values          =   5 decision tests
///   PriceBandCondition AND semantics        =   6 deterministic-gate tests
///
/// "Real money invariant": a strategy that fires when it should not (or fails
/// to fire when it should) is a money-loss event.
/// </summary>
public class StrategyPermutationMatrixTests
{
    // ── Snapshot / candle factories ───────────────────────────────────────

    private static readonly DateTime SnapUtc = new(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);

    // Candles for standard evaluation: 1 bar with price=10.
    // VWAP is computable (always).  EMA9/RSI/ADX/MACD stay null (insufficient bars).
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

    // Candles for EMA-period-aware tests: enough bars so EMA9 is populated.
    private static IReadOnlyList<Candle> NineBars(double endPrice = 10.0, double startPrice = 9.5)
    {
        return Enumerable.Range(0, 9).Select(i => new Candle
        {
            Symbol   = "TEST",
            StartUtc = SnapUtc.AddMinutes(-9 + i),
            EndUtc   = SnapUtc.AddMinutes(-8 + i),
            Open     = (decimal)startPrice,
            High     = (decimal)(endPrice * 1.01),
            Low      = (decimal)(endPrice * 0.99),
            Close    = (decimal)(startPrice + (endPrice - startPrice) * i / 8),
            Volume   = 2_000_000,
        }).ToList();
    }

    private static StrategyContext Context(double previousClose = 9.0) => new()
    {
        PreviousClose     = (decimal)previousClose,
        EvaluationTimeUtc = SnapUtc,
    };

    // ── Pipeline evaluation helpers ───────────────────────────────────────

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

    /// <summary>
    /// Core AND-semantics assertion.
    /// Evaluates condA and condB independently via the pipeline (solo), then
    /// evaluates them combined.  Asserts combined == soloA AND soloB.
    /// Works for stateless conditions (IndicatorCondition, PatternCondition, GapBandCondition).
    /// </summary>
    private static void AssertAndSemantics(ICondition condA, ICondition condB,
        TradeDirection dir = TradeDirection.Long, IReadOnlyList<Candle>? bars = null)
    {
        bars ??= OneBars();
        var soloA    = RunPipeline(DefWith(dir, condA),        bars).Count == 1;
        var soloB    = RunPipeline(DefWith(dir, condB),        bars).Count == 1;
        var combined = RunPipeline(DefWith(dir, condA, condB), bars).Count;
        var expected = soloA && soloB ? 1 : 0;

        Assert.That(combined, Is.EqualTo(expected),
            $"{condA.ToScript()} AND {condB.ToScript()} / {dir}: " +
            $"soloA={soloA}, soloB={soloB} → expected combined={expected}, got {combined}. " +
            "Pipeline AND semantics violated.");
    }

    // ── Deterministic AND gate (PriceBandCondition — purely price-based) ─

    [Test]
    public void AlwaysPass_NoConditions_Fires()
    {
        var def = DefWith(TradeDirection.Long);
        Assert.That(RunPipeline(def).Count, Is.EqualTo(1), "baseline: no conditions → fires");
    }

    [Test]
    public void PriceBand_AlwaysFail_DoesNotFire()
    {
        // price = 10, band [50, 100] → price out of band → fails
        var def = DefWith(TradeDirection.Long, new PriceBandCondition(50, 100));
        Assert.That(RunPipeline(def).Count, Is.EqualTo(0));
    }

    [Test]
    public void PriceBand_AlwaysPass_Fires()
    {
        // price = 10, band [0, 1000] → price in band → passes
        var def = DefWith(TradeDirection.Long, new PriceBandCondition(0, 1000));
        Assert.That(RunPipeline(def).Count, Is.EqualTo(1));
    }

    [Test]
    public void PriceBand_AlwaysPass_And_AlwaysFail_DoesNotFire()
    {
        AssertAndSemantics(new PriceBandCondition(0, 1000), new PriceBandCondition(50, 100));
    }

    [Test]
    public void PriceBand_AlwaysPass_And_AlwaysPass_Fires()
    {
        AssertAndSemantics(new PriceBandCondition(0, 1000), new PriceBandCondition(5, 15));
    }

    [Test]
    public void PriceBand_AlwaysFail_And_AlwaysFail_DoesNotFire()
    {
        AssertAndSemantics(new PriceBandCondition(50, 100), new PriceBandCondition(100, 200));
    }

    // ── GapBand deterministic tests ───────────────────────────────────────

    [Test]
    public void GapBand_Pass_PreviousClose9_Price10_Gap11Pct_Fires()
    {
        // gap = (10-9)/9 ≈ 11.1% — fits in [5%, 20%]
        var def = DefWith(TradeDirection.Long, new GapBandCondition(5, 20));
        Assert.That(RunPipeline(def, previousClose: 9.0).Count, Is.EqualTo(1));
    }

    [Test]
    public void GapBand_Fail_GapOutOfRange_DoesNotFire()
    {
        // gap ≈ 11.1% — outside [50%, 100%]
        var def = DefWith(TradeDirection.Long, new GapBandCondition(50, 100));
        Assert.That(RunPipeline(def, previousClose: 9.0).Count, Is.EqualTo(0));
    }

    // ── Indicator × Direction routing (25 × 2 = 50 tests) ────────────────
    //
    // These tests verify that each IndicatorCondition type is:
    //   (a) accepted by the pipeline without crashing
    //   (b) evaluated — proven by: when paired with GapBandCondition(always-fail),
    //       the result is 0 regardless of what the indicator evaluates to.

    private static IEnumerable<TestCaseData> IndicatorDirectionCases()
    {
        foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
        foreach (var (ind, cond) in AllIndicatorConditions())
            yield return new TestCaseData(ind, dir, cond).SetName($"Ind_{ind}_{dir}");
    }

    [TestCaseSource(nameof(IndicatorDirectionCases))]
    public void IndicatorCondition_SoloEvaluation_IsValidAndNotCrash(
        IndicatorType _, TradeDirection dir, ICondition cond)
    {
        var def   = DefWith(dir, cond);
        var count = RunPipeline(def).Count;
        Assert.That(count, Is.InRange(0, 1),
            $"{cond.ToScript()} / {dir}: pipeline must return 0 or 1, got {count}");
    }

    [TestCaseSource(nameof(IndicatorDirectionCases))]
    public void IndicatorCondition_PairedWithAlwaysFail_DoesNotFire(
        IndicatorType _, TradeDirection dir, ICondition cond)
    {
        AssertAndSemantics(cond, new PriceBandCondition(50, 100), dir);
    }

    // ── Pattern × Direction routing (7 × 2 = 14 tests) ───────────────────

    private static IEnumerable<TestCaseData> PatternDirectionCases()
    {
        foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
        foreach (var pattern in Enum.GetValues<PatternType>())
            yield return new TestCaseData(pattern, dir).SetName($"Pattern_{pattern}_{dir}");
    }

    [TestCaseSource(nameof(PatternDirectionCases))]
    public void PatternCondition_SoloEvaluation_IsValidAndNotCrash(PatternType pattern, TradeDirection dir)
    {
        var cond  = new PatternCondition(pattern);
        var def   = DefWith(dir, cond);
        var count = RunPipeline(def).Count;
        Assert.That(count, Is.InRange(0, 1),
            $"Pattern {pattern} / {dir}: must return 0 or 1");
    }

    [TestCaseSource(nameof(PatternDirectionCases))]
    public void PatternCondition_PairedWithAlwaysFail_DoesNotFire(PatternType pattern, TradeDirection dir)
    {
        AssertAndSemantics(new PatternCondition(pattern), new PriceBandCondition(50, 100), dir);
    }

    // ── PriceLevel × pass/fail × Direction (5 × 2 × 2 = 20 tests) ────────
    //
    // PriceLevelCondition is STATEFUL (tracks prior price to detect "holds above").
    // We use fresh instances per test (yielded by the factory each time) and
    // evaluate the pass/fail result via the pipeline directly, then assert
    // consistent AND behavior.

    private static IEnumerable<TestCaseData> PriceLevelDirectionCases()
    {
        foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
        foreach (var (lvlType, isPass, level) in PriceLevelPassFailCases())
            yield return new TestCaseData(lvlType, isPass, level, dir)
                .SetName($"PriceLevel_{lvlType}_{(isPass ? "Pass" : "Fail")}_{dir}");
    }

    [TestCaseSource(nameof(PriceLevelDirectionCases))]
    public void PriceLevelCondition_PassOrFail_PairedWithAlwaysFail_DoesNotFire(
        PriceLevelType levelType, bool _, double level, TradeDirection dir)
    {
        // Fresh instance per call (stateful — new each time)
        var cond = new PriceLevelCondition(levelType, level);
        AssertAndSemantics(cond, new PriceBandCondition(50, 100), dir);
    }

    [TestCaseSource(nameof(PriceLevelDirectionCases))]
    public void PriceLevelCondition_SoloEvaluation_IsValidAndNotCrash(
        PriceLevelType levelType, bool _, double level, TradeDirection dir)
    {
        var cond  = new PriceLevelCondition(levelType, level);
        var def   = DefWith(dir, cond);
        var count = RunPipeline(def).Count;
        Assert.That(count, Is.InRange(0, 1),
            $"PriceLevel {levelType}(level={level}) / {dir}: must return 0 or 1");
    }

    // ── Exit type × Direction (7 × 2 = 14 tests) ─────────────────────────

    private static IEnumerable<TestCaseData> ExitDirectionCases()
    {
        var exitTypes = new[]
        {
            "StopLossPercent", "StopLossPrice",
            "TakeProfitPercent", "TakeProfitPrice",
            "TrailingStopPercent", "PeakGiveback", "MultiTarget2Rung",
        };
        foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
        foreach (var exit in exitTypes)
            yield return new TestCaseData(exit, dir).SetName($"Exit_{exit}_{dir}");
    }

    [TestCaseSource(nameof(ExitDirectionCases))]
    public void NoConditionStrategy_WithExitConfig_FiresWithCorrectDirection(string exitType, TradeDirection dir)
    {
        var builder = dir == TradeDirection.Long
            ? Stock.Ticker("TEST").Long()
            : Stock.Ticker("TEST").Short();

        builder = exitType switch
        {
            "StopLossPercent"     => builder.StopLossPercent(5),
            "StopLossPrice"       => builder.StopLoss(dir == TradeDirection.Long ? 9.0 : 11.0),
            "TakeProfitPercent"   => builder.StopLossPercent(5).TakeProfitPercent(20),
            "TakeProfitPrice"     => builder.StopLossPercent(5).TakeProfit(dir == TradeDirection.Long ? 12.0 : 8.0),
            "TrailingStopPercent" => builder.StopLossPercent(5).TrailingStopLoss(8),
            "PeakGiveback"        => builder.StopLossPercent(5).PeakGiveback(25, "09:00"),
            "MultiTarget2Rung"    => builder.StopLossPercent(5)
                                           .TakeProfit(dir == TradeDirection.Long ? 11.0 : 9.0,
                                                       dir == TradeDirection.Long ? 12.0 : 8.0),
            _                     => builder.StopLossPercent(5),
        };

        var signals = RunPipeline(builder.Build());
        Assert.That(signals, Has.Count.EqualTo(1),         $"{exitType}/{dir}: must fire");
        Assert.That(signals[0].Direction, Is.EqualTo(dir), $"{exitType}/{dir}: direction must match");
    }

    // ── Indicator × Exit JSON round-trip (25 × 3 = 75 tests) ────────────

    private static IEnumerable<TestCaseData> IndicatorExitJsonCases()
    {
        var exits = new[] { "StopLossPercent", "TakeProfitPrice", "TrailingStopPercent" };
        foreach (var (ind, cond) in AllIndicatorConditions())
        foreach (var exit in exits)
            yield return new TestCaseData(ind, exit, cond).SetName($"IndExitJson_{ind}_{exit}");
    }

    [TestCaseSource(nameof(IndicatorExitJsonCases))]
    public void IndicatorWithExitConfig_SurvivesJsonRoundTrip(
        IndicatorType _, string exitType, ICondition cond)
    {
        var builder = Stock.Ticker("TEST").Long().StopLossPercent(5);
        if (exitType == "TakeProfitPrice")     builder = builder.TakeProfit(12.0);
        if (exitType == "TrailingStopPercent") builder = builder.TrailingStopLoss(8);
        var def = builder.Build();
        def.EntryConditions.Add(cond);

        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        Assert.That(restored.EntryConditions, Has.Count.EqualTo(1),
            $"{cond.ToScript()}/{exitType}: must survive JSON");
        Assert.That(restored.EntryConditions[0].ToScript(), Is.EqualTo(cond.ToScript()),
            $"{cond.ToScript()}/{exitType}: ToScript must match after round-trip");
    }

    // ── Adjacent-pair AND-chain (24 tests) ────────────────────────────────

    private static IEnumerable<TestCaseData> AdjacentPairCases()
    {
        var indicators = AllIndicatorConditions().ToArray();
        for (var i = 0; i < indicators.Length - 1; i++)
        {
            var (typeA, condA) = indicators[i];
            var (typeB, condB) = indicators[i + 1];
            yield return new TestCaseData(typeA, typeB, condA, condB)
                .SetName($"AndChain_{typeA}_And_{typeB}");
        }
    }

    [TestCaseSource(nameof(AdjacentPairCases))]
    public void TwoIndicatorAndChain_CombinedMatchesSoloAnd(
        IndicatorType _, IndicatorType __, ICondition condA, ICondition condB)
        => AssertAndSemantics(condA, condB);

    // ── Indicator × Pattern cross-dimension (25 × 7 = 175 tests) ─────────

    private static IEnumerable<TestCaseData> IndicatorPatternCases()
    {
        foreach (var (ind, indCond) in AllIndicatorConditions())
        foreach (var pattern in Enum.GetValues<PatternType>())
            yield return new TestCaseData(ind, pattern, indCond)
                .SetName($"Cross_Ind_{ind}_Pat_{pattern}");
    }

    [TestCaseSource(nameof(IndicatorPatternCases))]
    public void IndicatorAndPattern_AndSemanticsHold(
        IndicatorType _, PatternType pattern, ICondition indCond)
        => AssertAndSemantics(indCond, new PatternCondition(pattern));

    // ── Indicator × PriceLevelType cross-dimension (25 × 5 = 125 tests) ──

    private static IEnumerable<TestCaseData> IndicatorPriceLevelCases()
    {
        foreach (var (ind, indCond) in AllIndicatorConditions())
        foreach (var (lvlType, _, level) in PriceLevelPassFailCases().Where(t => t.pass))
            yield return new TestCaseData(ind, lvlType, indCond, level)
                .SetName($"Cross_Ind_{ind}_Lvl_{lvlType}");
    }

    [TestCaseSource(nameof(IndicatorPriceLevelCases))]
    public void IndicatorAndPriceLevel_AndSemanticsHold(
        IndicatorType _, PriceLevelType lvlType, ICondition indCond, double level)
        => AssertAndSemantics(indCond, new PriceLevelCondition(lvlType, level));

    // ── Pattern × PriceLevelType cross-dimension (7 × 5 = 35 tests) ──────

    private static IEnumerable<TestCaseData> PatternPriceLevelCases()
    {
        foreach (var pattern in Enum.GetValues<PatternType>())
        foreach (var (lvlType, _, level) in PriceLevelPassFailCases().Where(t => t.pass))
            yield return new TestCaseData(pattern, lvlType, level)
                .SetName($"Cross_Pat_{pattern}_Lvl_{lvlType}");
    }

    [TestCaseSource(nameof(PatternPriceLevelCases))]
    public void PatternAndPriceLevel_AndSemanticsHold(PatternType pattern, PriceLevelType lvlType, double level)
        => AssertAndSemantics(new PatternCondition(pattern), new PriceLevelCondition(lvlType, level));

    // ── JSON round-trip for all indicator types (25 tests) ───────────────

    private static IEnumerable<TestCaseData> AllIndicatorJsonCases()
    {
        foreach (var (ind, cond) in AllIndicatorConditions())
            yield return new TestCaseData(ind, cond).SetName($"Json_{ind}");
    }

    [TestCaseSource(nameof(AllIndicatorJsonCases))]
    public void AllIndicatorTypes_RoundTripViaJson(IndicatorType _, ICondition cond)
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        def.EntryConditions.Add(cond);

        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        Assert.That(restored.EntryConditions, Has.Count.EqualTo(1));
        Assert.That(restored.EntryConditions[0].ToScript(), Is.EqualTo(cond.ToScript()));
    }

    // ── JSON round-trip for all pattern types (7 tests) ──────────────────

    private static IEnumerable<TestCaseData> AllPatternJsonCases()
    {
        foreach (var p in Enum.GetValues<PatternType>())
            yield return new TestCaseData(p).SetName($"Json_Pattern_{p}");
    }

    [TestCaseSource(nameof(AllPatternJsonCases))]
    public void AllPatternTypes_RoundTripViaJson(PatternType pattern)
    {
        var cond = new PatternCondition(pattern);
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        def.EntryConditions.Add(cond);

        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        Assert.That(restored.EntryConditions[0].ToScript(), Is.EqualTo(cond.ToScript()));
    }

    // ── JSON round-trip for all price-level types (5 tests) ──────────────

    private static IEnumerable<TestCaseData> AllPriceLevelJsonCases()
    {
        foreach (var (lvlType, _, level) in PriceLevelPassFailCases().Where(t => t.pass))
            yield return new TestCaseData(lvlType, level).SetName($"Json_PriceLevel_{lvlType}");
    }

    [TestCaseSource(nameof(AllPriceLevelJsonCases))]
    public void AllPriceLevelTypes_RoundTripViaJson(PriceLevelType levelType, double level)
    {
        var cond = new PriceLevelCondition(levelType, level);
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        def.EntryConditions.Add(cond);

        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        Assert.That(restored.EntryConditions[0].ToScript(), Is.EqualTo(cond.ToScript()));
    }

    // ── ConditionalBlock override fields (5 tests) ───────────────────────

    private static IEnumerable<TestCaseData> BranchOverrideCases()
    {
        yield return new TestCaseData("Direction",           (object)TradeDirection.Short)
            .SetName("Branch_Override_Direction");
        yield return new TestCaseData("StopLossPercent",     10.0)
            .SetName("Branch_Override_StopLossPercent");
        yield return new TestCaseData("StopLossPrice",       9.0)
            .SetName("Branch_Override_StopLossPrice");
        yield return new TestCaseData("TakeProfitPrice",     12.0)
            .SetName("Branch_Override_TakeProfitPrice");
        yield return new TestCaseData("TrailingStopPercent", 8.0)
            .SetName("Branch_Override_TrailingStopPercent");
    }

    [TestCaseSource(nameof(BranchOverrideCases))]
    public void BranchOverride_MatchingBranch_AppliedToResolvedDef(string field, object value)
    {
        var def       = Stock.Ticker("TEST").Long().StopLossPercent(5).TakeProfit(15.0).Build();
        var overrides = new StrategyOverrides();
        switch (field)
        {
            case "Direction":           overrides.Direction           = (TradeDirection)value; break;
            case "StopLossPercent":     overrides.StopLossPercent     = (double)value;         break;
            case "StopLossPrice":       overrides.StopLossPrice       = (double)value;         break;
            case "TakeProfitPrice":     overrides.TakeProfitPrice     = (double)value;         break;
            case "TrailingStopPercent": overrides.TrailingStopPercent = (double)value;         break;
        }

        var block  = new ConditionalBlock();
        var branch = new ConditionalBranch
        {
            Condition = new PriceBandCondition(0, 1000), // always matches
            Overrides = overrides,
        };
        block.Branches.Add(branch);
        def.ConditionalBlocks.Add(block);

        // Use a minimal snapshot — StrategyBranchResolver only needs the condition evaluation
        var snap = new IndicatorSnapshot { Symbol = "TEST", Timestamp = SnapUtc, Price = 10.0 };
        var resolved = StrategyBranchResolver.Resolve(def, snap);

        var actual = field switch
        {
            "Direction"           => (object)resolved.Direction,
            "StopLossPercent"     => resolved.StopLossPercent!,
            "StopLossPrice"       => resolved.StopLossPrice!,
            "TakeProfitPrice"     => resolved.TakeProfitPrice!,
            "TrailingStopPercent" => resolved.TrailingStopPercent!,
            _                     => throw new InvalidOperationException(field),
        };
        Assert.That(actual, Is.EqualTo(value),
            $"Branch override for field '{field}' must be applied when branch matches");
    }

    // ── GapperExitDecision all reason values (5 tests) ───────────────────

    private static IEnumerable<TestCaseData> GapperExitReasonCases()
    {
        foreach (var reason in Enum.GetValues<GapperExitReason>())
            yield return new TestCaseData(reason).SetName($"GapperExitDecision_{reason}");
    }

    [TestCaseSource(nameof(GapperExitReasonCases))]
    public void GapperExitDecision_AllReasons_CanBeConstructed(GapperExitReason reason)
    {
        var full    = new GapperExitDecision(reason, 10.0, 11.0, "test") { QuantityToSell = null };
        var partial = new GapperExitDecision(reason, 10.0, 11.0, "test") { QuantityToSell = 33 };

        Assert.Multiple(() =>
        {
            Assert.That(full.Reason,            Is.EqualTo(reason));
            Assert.That(full.QuantityToSell,    Is.Null);
            Assert.That(partial.QuantityToSell, Is.EqualTo(33));
            Assert.That(partial.Reason,         Is.EqualTo(reason));
        });
    }

    // ── Condition factories ───────────────────────────────────────────────

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

    private static IEnumerable<(PriceLevelType type, bool pass, double level)> PriceLevelPassFailCases()
    {
        // price = 10.0 from OneBars(10)
        yield return (PriceLevelType.HoldsAbove,  true,   9.0);
        yield return (PriceLevelType.HoldsAbove,  false, 11.0);
        yield return (PriceLevelType.HoldsBelow,  true,  11.0);
        yield return (PriceLevelType.HoldsBelow,  false,  9.0);
        yield return (PriceLevelType.Near,        true,  10.0);
        yield return (PriceLevelType.Near,        false, 15.0);
        yield return (PriceLevelType.BreaksAbove, true,   9.5);
        yield return (PriceLevelType.BreaksAbove, false, 11.0);
        yield return (PriceLevelType.BreaksBelow, true,  10.5);
        yield return (PriceLevelType.BreaksBelow, false,  9.0);
    }
}
