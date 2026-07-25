using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Exhaustive all-subset AND-semantics tests for indicator conditions.
///
/// The existing adjacent-pair and adjacent-triple/quad tests only cover
/// NEIGHBORING positions in the indicator array (indices 0-1, 1-2, …).
/// They do NOT prove that, e.g., conditions [3, 17, 22] AND correctly,
/// or that any 4-combination of 25 indicators evaluates correctly.
///
/// This file tests EVERY POSSIBLE SUBSET of size 2, 3, and 4 from the
/// 25 indicator conditions, in both directions.
///
///   C(25,2) = 300 pairs   × 2 dirs =    600 tests
///   C(25,3) = 2,300 triples × 2 dirs =  4,600 tests
///   C(25,4) = 12,650 quads × 2 dirs = 25,300 tests
///   ──────────────────────────────────────────────
///   Total                            = 30,500 tests
///
/// Performance optimization — solo-result cache:
///   DslStrategy's evaluation of each indicator with 1 bar is deterministic
///   and pure (no side effects).  The solo result for each of 25 indicators
///   × 2 directions is precomputed ONCE at class initialization (50 pipeline
///   calls total).  Each test then makes exactly ONE pipeline call (the
///   N-condition combined run) and compares against the cached expected value.
///   This keeps total pipeline calls at 50 + 600 + 4,600 + 25,300 = 30,550,
///   completing the full 30,500-test suite in seconds.
///
/// What this proves:
///   If DslStrategy had a bug that stops evaluating conditions before
///   exhausting EntryConditions (e.g. a range-check error, an early-return
///   after index 3, or a condition-list corruption), at least one of these
///   30,500 tests would detect it — because every indicator appears in
///   multiple non-adjacent subsets where it is the ONLY failing condition.
/// </summary>
public class StrategyAllSubsetAndTests
{
    // ── Infrastructure ───────────────────────────────────────────────────

    private static readonly DateTime SnapUtc = new(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<Candle> OneBars() =>
    [
        new Candle
        {
            Symbol   = "TEST",
            StartUtc = SnapUtc.AddMinutes(-1),
            EndUtc   = SnapUtc,
            Open     = 9.9m,
            High     = 10.2m,
            Low      = 9.8m,
            Close    = 10.0m,
            Volume   = 2_000_000,
        },
    ];

    private static StrategyContext Context() => new()
    {
        PreviousClose     = 9.0m,
        EvaluationTimeUtc = SnapUtc,
    };

    private static IReadOnlyList<TradeSignal> RunPipeline(StrategyDefinition def)
        => new DslStrategy(def).Evaluate("TEST", OneBars(), Context());

    private static StrategyDefinition DefWith(TradeDirection dir, IEnumerable<ICondition> conditions)
    {
        var def = new StrategyDefinition
        {
            Symbol          = "TEST",
            Direction       = dir,
            StopLossPercent = 5,
            Quantity        = 1,
        };
        foreach (var c in conditions) def.EntryConditions.Add(c);
        return def;
    }

    // ── Indicator condition catalog ───────────────────────────────────────

    private static readonly ICondition[] AllConds =
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

    private static readonly int N = AllConds.Length; // 25

    // ── Solo-result cache (50 pipeline calls, computed once) ─────────────
    // True if the indicator fires when evaluated alone in a 1-bar pipeline.

    private static readonly bool[] LongSoloFires;
    private static readonly bool[] ShortSoloFires;

    static StrategyAllSubsetAndTests()
    {
        LongSoloFires  = AllConds.Select(c => RunPipeline(DefWith(TradeDirection.Long,  [c])).Count == 1).ToArray();
        ShortSoloFires = AllConds.Select(c => RunPipeline(DefWith(TradeDirection.Short, [c])).Count == 1).ToArray();
    }

    // ── Combination generator ────────────────────────────────────────────

    /// Enumerate all C(n, k) subsets of {0, 1, …, n-1} in lexicographic order.
    private static IEnumerable<int[]> Subsets(int n, int k)
    {
        var c = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])c.Clone();
            int i = k - 1;
            while (i >= 0 && c[i] == i + n - k) i--;
            if (i < 0) yield break;
            c[i]++;
            for (int j = i + 1; j < k; j++) c[j] = c[j - 1] + 1;
        }
    }

    // ── Generator: all C(25,2) × 2 dirs = 600 pair tests ────────────────

    private static IEnumerable<TestCaseData> AllPairCases()
    {
        foreach (var s in Subsets(N, 2))
            foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
                yield return new TestCaseData(s[0], s[1], dir)
                    .SetName($"AllPair_{s[0]}_{s[1]}_{dir}");
    }

    [TestCaseSource(nameof(AllPairCases))]
    public void AllPairs_AndSemanticsHold(int a, int b, TradeDirection dir)
    {
        var fires    = dir == TradeDirection.Long ? LongSoloFires : ShortSoloFires;
        var expected = fires[a] && fires[b] ? 1 : 0;
        var actual   = RunPipeline(DefWith(dir, [AllConds[a], AllConds[b]])).Count;
        Assert.That(actual, Is.EqualTo(expected),
            $"[{dir}] AND({a},{b}): soloA={fires[a]} soloB={fires[b]}");
    }

    // ── Generator: all C(25,3) × 2 dirs = 4,600 triple tests ────────────

    private static IEnumerable<TestCaseData> AllTripleCases()
    {
        foreach (var s in Subsets(N, 3))
            foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
                yield return new TestCaseData(s[0], s[1], s[2], dir)
                    .SetName($"AllTriple_{s[0]}_{s[1]}_{s[2]}_{dir}");
    }

    [TestCaseSource(nameof(AllTripleCases))]
    public void AllTriples_AndSemanticsHold(int a, int b, int c, TradeDirection dir)
    {
        var fires    = dir == TradeDirection.Long ? LongSoloFires : ShortSoloFires;
        var expected = fires[a] && fires[b] && fires[c] ? 1 : 0;
        var actual   = RunPipeline(DefWith(dir, [AllConds[a], AllConds[b], AllConds[c]])).Count;
        Assert.That(actual, Is.EqualTo(expected),
            $"[{dir}] AND({a},{b},{c}): soloA={fires[a]} soloB={fires[b]} soloC={fires[c]}");
    }

    // ── Generator: all C(25,4) × 2 dirs = 25,300 quad tests ─────────────

    private static IEnumerable<TestCaseData> AllQuadCases()
    {
        foreach (var s in Subsets(N, 4))
            foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
                yield return new TestCaseData(s[0], s[1], s[2], s[3], dir)
                    .SetName($"AllQuad_{s[0]}_{s[1]}_{s[2]}_{s[3]}_{dir}");
    }

    [TestCaseSource(nameof(AllQuadCases))]
    public void AllQuads_AndSemanticsHold(int a, int b, int c, int d, TradeDirection dir)
    {
        var fires    = dir == TradeDirection.Long ? LongSoloFires : ShortSoloFires;
        var expected = fires[a] && fires[b] && fires[c] && fires[d] ? 1 : 0;
        var actual   = RunPipeline(DefWith(dir, [AllConds[a], AllConds[b], AllConds[c], AllConds[d]])).Count;
        Assert.That(actual, Is.EqualTo(expected),
            $"[{dir}] AND({a},{b},{c},{d}): " +
            $"soloA={fires[a]} soloB={fires[b]} soloC={fires[c]} soloD={fires[d]}");
    }
}
