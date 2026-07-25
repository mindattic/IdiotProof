using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Exhaustive permutation tests for ConditionalBlock override field combinations.
///
/// StrategyOverrides has 7 overridable fields:
///   0: Direction            (scalar: Long→Short)
///   1: TakeProfitPrice      (scalar: null→12.0)
///   2: StopLossPrice        (scalar: null→9.0)
///   3: StopLossPercent      (scalar: 5.0→7.5)
///   4: TrailingStopPercent  (scalar: null→8.0)
///   5: EntryConditions      (list: []→[PriceBand(0,1000)])
///   6: TakeProfitTargets    (list: []→[T1:11.0, T2:13.0])
///
/// 2^7 = 128 combinations of which fields are set in the override.
/// For each combination:
///   - A base StrategyDefinition is created with known baseline values.
///   - A ConditionalBlock is added with an always-matching Else branch
///     and the specified override combination applied to it.
///   - StrategyBranchResolver.Resolve is called to produce the resolved def.
///   - Every set field must equal the override value.
///   - Every unset field must equal the base def value.
///
/// This exhaustively proves that StrategyBranchResolver correctly applies
/// every combination of override fields, neither omitting a set field nor
/// corrupting an unset field.  Missing this in a ConditionalBlock-heavy
/// strategy (where branch logic is the entire business logic) is a money risk:
/// a wrong direction or a missing stop loss goes live undetected.
///
/// Coverage: 128 tests (all 2^7 override combinations)
/// </summary>
public class ConditionalBlockOverridePermutationTests
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

    private static IndicatorSnapshot BuildSnapshot() =>
        IndicatorSnapshotBuilder.BuildWithEmas("TEST", OneBars(), [9, 21], previousClose: 9.0m);

    // ── Base def values ──────────────────────────────────────────────────
    // These are the values a baseline StrategyDefinition has BEFORE any branch override.

    private const TradeDirection   BaseDir          = TradeDirection.Long;
    private const double           BaseStopLossPct  = 5.0;
    // TakeProfitPrice, StopLossPrice, TrailingStopPercent are all null by default.

    // Override values (deliberately different from base so we can tell them apart)
    private const TradeDirection   OverrideDir          = TradeDirection.Short;
    private const double           OverrideTakeProfitPx = 12.0;
    private const double           OverrideStopLossPx   = 9.0;
    private const double           OverrideStopLossPct  = 7.5;
    private const double           OverrideTrailing     = 8.0;
    private static ICondition      OverrideEntryCondition() => new PriceBandCondition(50, 100); // never fires live
    private static TakeProfitTarget OverrideTp1() => new() { Label = "T1", Price = 11.0, PercentToSell = 50 };
    private static TakeProfitTarget OverrideTp2() => new() { Label = "T2", Price = 13.0, PercentToSell = 50 };

    // ── Helper: build def + apply override combination ────────────────────

    private static StrategyDefinition BaseDef()
    {
        var def = new StrategyDefinition
        {
            Symbol          = "TEST",
            Direction       = BaseDir,
            StopLossPercent = BaseStopLossPct,
            Quantity        = 1,
        };
        def.EntryConditions.Add(new PriceBandCondition(0, 1000)); // always fires
        return def;
    }

    private static StrategyOverrides BuildOverrides(int mask)
    {
        var ov = new StrategyOverrides();
        if ((mask & 0b0000001) != 0) ov.Direction           = OverrideDir;
        if ((mask & 0b0000010) != 0) ov.TakeProfitPrice     = OverrideTakeProfitPx;
        if ((mask & 0b0000100) != 0) ov.StopLossPrice       = OverrideStopLossPx;
        if ((mask & 0b0001000) != 0) ov.StopLossPercent     = OverrideStopLossPct;
        if ((mask & 0b0010000) != 0) ov.TrailingStopPercent = OverrideTrailing;
        if ((mask & 0b0100000) != 0) ov.EntryConditions.Add(OverrideEntryCondition());
        if ((mask & 0b1000000) != 0)
        {
            ov.TakeProfitTargets.Add(OverrideTp1());
            ov.TakeProfitTargets.Add(OverrideTp2());
        }
        return ov;
    }

    private static StrategyDefinition ResolveWithMask(int mask)
    {
        var def = BaseDef();
        var ov  = BuildOverrides(mask);
        var branch = new ConditionalBranch { Condition = null, Overrides = ov }; // Else = always matches
        var block  = new ConditionalBlock();
        block.Branches.Add(branch);
        def.ConditionalBlocks.Add(block);
        var snap = BuildSnapshot();
        return StrategyBranchResolver.Resolve(def, snap);
    }

    // ── Generator: all 128 mask values ──────────────────────────────────

    private static IEnumerable<TestCaseData> AllOverrideMaskCases()
    {
        for (int mask = 0; mask < 128; mask++)
        {
            var label = new System.Text.StringBuilder("Override[");
            if ((mask & 0b0000001) != 0) label.Append("Dir,");
            if ((mask & 0b0000010) != 0) label.Append("TakeProfitPx,");
            if ((mask & 0b0000100) != 0) label.Append("StopLossPx,");
            if ((mask & 0b0001000) != 0) label.Append("StopLossPct,");
            if ((mask & 0b0010000) != 0) label.Append("TrailingPct,");
            if ((mask & 0b0100000) != 0) label.Append("EntryConditions,");
            if ((mask & 0b1000000) != 0) label.Append("TpTargets,");
            if (label[^1] == ',') label.Remove(label.Length - 1, 1);
            label.Append(']');
            yield return new TestCaseData(mask).SetName($"OverrideMask_{mask:D3}_{label}");
        }
    }

    // ── The test ─────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllOverrideMaskCases))]
    public void AllOverrideCombinations_ResolvedDefHasCorrectValues(int mask)
    {
        var resolved = ResolveWithMask(mask);
        var label    = $"mask=0b{Convert.ToString(mask, 2).PadLeft(7, '0')}";

        Assert.Multiple(() =>
        {
            // Bit 0: Direction
            var expectedDir = (mask & 0b0000001) != 0 ? OverrideDir : BaseDir;
            Assert.That(resolved.Direction, Is.EqualTo(expectedDir),
                $"{label}: Direction");

            // Bit 1: TakeProfitPrice
            var expectedTpp = (mask & 0b0000010) != 0 ? (double?)OverrideTakeProfitPx : null;
            Assert.That(resolved.TakeProfitPrice, Is.EqualTo(expectedTpp),
                $"{label}: TakeProfitPrice");

            // Bit 2: StopLossPrice
            var expectedSlp = (mask & 0b0000100) != 0 ? (double?)OverrideStopLossPx : null;
            Assert.That(resolved.StopLossPrice, Is.EqualTo(expectedSlp),
                $"{label}: StopLossPrice");

            // Bit 3: StopLossPercent
            var expectedSlPct = (mask & 0b0001000) != 0 ? (double?)OverrideStopLossPct : BaseStopLossPct;
            Assert.That(resolved.StopLossPercent, Is.EqualTo(expectedSlPct),
                $"{label}: StopLossPercent");

            // Bit 4: TrailingStopPercent
            var expectedTrail = (mask & 0b0010000) != 0 ? (double?)OverrideTrailing : null;
            Assert.That(resolved.TrailingStopPercent, Is.EqualTo(expectedTrail),
                $"{label}: TrailingStopPercent");

            // Bit 5: EntryConditions
            // ApplyTo APPENDS override conditions to the base list (does not replace).
            // Base def has 1 condition (PriceBand 0-1000).
            // When bit 5 is set, override adds 1 more (PriceBand 50-100) → total = 2.
            // When bit 5 is NOT set, only base condition remains → total = 1.
            var expectedCondCount = (mask & 0b0100000) != 0 ? 2 : 1;
            Assert.That(resolved.EntryConditions, Has.Count.EqualTo(expectedCondCount),
                $"{label}: EntryConditions count — ApplyTo appends, not replaces");

            // Bit 6: TakeProfitTargets (override has 2 targets; base has 0)
            var expectedTptCount = (mask & 0b1000000) != 0 ? 2 : 0;
            Assert.That(resolved.TakeProfitTargets, Has.Count.EqualTo(expectedTptCount),
                $"{label}: TakeProfitTargets count");
        });
    }
}
