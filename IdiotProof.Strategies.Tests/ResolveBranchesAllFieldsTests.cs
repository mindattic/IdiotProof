using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Proves that <see cref="StrategyBranchResolver.Resolve"/> copies every field of the
/// base <see cref="StrategyDefinition"/> into the cloned result, including the
/// nine fields that were missing before Bug 1 was fixed.  A ConditionalBlock
/// with a non-matching condition is added so the clone path is exercised; the
/// base values must survive unchanged when no branch fires.
///
/// Regression guard: any future refactor of StrategyBranchResolver that drops even
/// one field will fail here long before it reaches real money.
/// </summary>
public class ResolveBranchesAllFieldsTests
{
    private static StrategyDefinition CallResolveBranches(StrategyDefinition def, IndicatorSnapshot snap)
        => StrategyBranchResolver.Resolve(def, snap);

    private static IndicatorSnapshot EmptySnap() => new()
    {
        Symbol    = "TEST",
        Timestamp = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc),
        Price     = 10.0,
        Vwap      = 9.0,  // price > vwap → IsAboveVwap passes
    };

    /// <summary>
    /// Builds a StrategyDefinition with every one of the 9 previously-dropped
    /// fields set to a non-zero, non-default value that would be detectable as
    /// wrong if it came back as 0/false/null.
    /// </summary>
    private static StrategyDefinition FullDef()
    {
        var def = Stock.Ticker("TEST")
            .Long()
            .StopLossPercent(5)
            .TakeProfit(15.0)
            .Build();

        // 9 fields that were missing from the clone (the bug)
        def.ExitAtPriorHigh        = true;
        def.RollingHighDays        = 3;
        def.RollingHighBuffer      = 0.25;
        def.RollingLowDays         = 5;
        def.RollingLowBuffer       = 0.10;
        def.EntryRollingLowDays    = 7;
        def.EntryRollingLowBuffer  = 0.15;
        def.EntryRollingHighDays   = 10;
        def.EntryRollingHighBuffer = 0.20;

        // Other important fields that were already being copied but should
        // still round-trip correctly
        def.PeakGivebackPercent = 25;
        def.PeakGivebackArmTime = new TimeSpan(9, 15, 0);
        def.TrailingStopPercent = 8;
        def.NotionalAmount      = 2500m;
        def.IsAutonomous        = true;
        def.IsAdaptive          = true;
        def.ShouldRepeat        = false;

        // A ConditionalBlock with a condition that never matches (price must be
        // above $99,999) so no branch fires and the base values are what the
        // resolved clone must carry.
        // NOTE: StrategyOverrides only supports Direction, EntryConditions,
        // TakeProfitPrice/Targets, StopLossPrice/Percent, TrailingStopPercent —
        // the 9 "formerly dropped" exit fields cannot be overridden; they must
        // be preserved from the base definition via the clone copy lines.
        var neverMatch = new ConditionalBlock();
        neverMatch.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 99_999.0),
            Overrides = new StrategyOverrides
            {
                // Deliberately different values for override-able fields —
                // if these land in the clone it means the wrong branch fired.
                Direction       = TradeDirection.Short,
                StopLossPercent = 99,
                TakeProfitPrice = 999,
            },
        });
        def.ConditionalBlocks.Add(neverMatch);

        return def;
    }

    [Test]
    public void ResolveBranches_NoMatchingBranch_ClonesAllNineFormerlyDroppedFields()
    {
        var def = FullDef();
        var resolved = CallResolveBranches(def, EmptySnap());

        Assert.Multiple(() =>
        {
            Assert.That(resolved.ExitAtPriorHigh,        Is.True,      "ExitAtPriorHigh");
            Assert.That(resolved.RollingHighDays,        Is.EqualTo(3),    "RollingHighDays");
            Assert.That(resolved.RollingHighBuffer,      Is.EqualTo(0.25), "RollingHighBuffer");
            Assert.That(resolved.RollingLowDays,         Is.EqualTo(5),    "RollingLowDays");
            Assert.That(resolved.RollingLowBuffer,       Is.EqualTo(0.10), "RollingLowBuffer");
            Assert.That(resolved.EntryRollingLowDays,    Is.EqualTo(7),    "EntryRollingLowDays");
            Assert.That(resolved.EntryRollingLowBuffer,  Is.EqualTo(0.15), "EntryRollingLowBuffer");
            Assert.That(resolved.EntryRollingHighDays,   Is.EqualTo(10),   "EntryRollingHighDays");
            Assert.That(resolved.EntryRollingHighBuffer, Is.EqualTo(0.20), "EntryRollingHighBuffer");
        });
    }

    [Test]
    public void ResolveBranches_NoMatchingBranch_ClonesAllOtherExitFields()
    {
        var def = FullDef();
        var resolved = CallResolveBranches(def, EmptySnap());

        Assert.Multiple(() =>
        {
            Assert.That(resolved.PeakGivebackPercent,  Is.EqualTo(25),           "PeakGivebackPercent");
            Assert.That(resolved.PeakGivebackArmTime,  Is.EqualTo(new TimeSpan(9, 15, 0)), "PeakGivebackArmTime");
            Assert.That(resolved.TrailingStopPercent,  Is.EqualTo(8),            "TrailingStopPercent");
            Assert.That(resolved.StopLossPercent,      Is.EqualTo(5),            "StopLossPercent");
            Assert.That(resolved.TakeProfitPrice,      Is.EqualTo(15.0),         "TakeProfitPrice");
        });
    }

    [Test]
    public void ResolveBranches_NoMatchingBranch_ClonesIdentityAndSizingFields()
    {
        var def = FullDef();
        var resolved = CallResolveBranches(def, EmptySnap());

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Id,             Is.EqualTo(def.Id),        "Id");
            Assert.That(resolved.Symbol,         Is.EqualTo("TEST"),        "Symbol");
            Assert.That(resolved.Direction,      Is.EqualTo(TradeDirection.Long), "Direction");
            Assert.That(resolved.NotionalAmount, Is.EqualTo(2500m),         "NotionalAmount");
            Assert.That(resolved.IsAutonomous,   Is.True,                   "IsAutonomous");
            Assert.That(resolved.IsAdaptive,     Is.True,                   "IsAdaptive");
            Assert.That(resolved.ShouldRepeat,   Is.False,                  "ShouldRepeat");
        });
    }

    [Test]
    public void ResolveBranches_NoMatchingBranch_EntryConditionsAreCopied()
    {
        var def = Stock.Ticker("TEST")
            .IsAboveVwap()
            .IsAdxAbove(25)
            .Long()
            .Build();

        // non-matching block
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 1_000_000),
            Overrides = new StrategyOverrides(),
        });
        def.ConditionalBlocks.Add(block);

        var resolved = CallResolveBranches(def, EmptySnap());

        Assert.That(resolved.EntryConditions, Has.Count.EqualTo(def.EntryConditions.Count),
            "all base entry conditions must survive into the clone");
    }

    [Test]
    public void ResolveBranches_MatchingBranch_OverridesWinOverBaseValues()
    {
        // StrategyOverrides only exposes Direction/entries/take-profit/stop fields.
        // The 9 exit-only fields (ExitAtPriorHigh, Rolling*) are not overrideable
        // per branch — they are fixed on the base StrategyDefinition.
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();

        var matchBlock = new ConditionalBlock();
        matchBlock.Branches.Add(new ConditionalBranch
        {
            // Price > 0 → always matches when price is positive
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0),
            Overrides = new StrategyOverrides
            {
                Direction        = TradeDirection.Short,
                StopLossPercent  = 12,
                TakeProfitPrice  = 8.0,
                TrailingStopPercent = 15,
            },
        });
        def.ConditionalBlocks.Add(matchBlock);

        var snap = EmptySnap();
        snap.Price = 10.0;
        var resolved = CallResolveBranches(def, snap);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Direction,          Is.EqualTo(TradeDirection.Short), "branch override: Direction");
            Assert.That(resolved.StopLossPercent,    Is.EqualTo(12),                   "branch override: StopLossPercent");
            Assert.That(resolved.TakeProfitPrice,    Is.EqualTo(8.0),                  "branch override: TakeProfitPrice");
            Assert.That(resolved.TrailingStopPercent, Is.EqualTo(15),                  "branch override: TrailingStopPercent");
        });
    }

    [Test]
    public void ResolveBranches_NoConditionalBlocks_ReturnsBaseDef_ByReference()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        Assert.That(def.ConditionalBlocks, Is.Empty);

        var resolved = CallResolveBranches(def, EmptySnap());

        // Early-return path: when there are no ConditionalBlocks, ResolveBranches
        // returns the base definition unchanged (not a clone).
        Assert.That(ReferenceEquals(resolved, def), Is.True,
            "fast-path: no ConditionalBlocks means no clone overhead — must return base reference");
    }

    [Test]
    public void ResolveBranches_TakeProfitTargetsAreCopied()
    {
        var def = Stock.Ticker("TEST")
            .Long()
            .StopLoss(9.0)
            .TakeProfit(11.0, 13.0, 15.0)
            .Build();

        var neverBlock = new ConditionalBlock();
        neverBlock.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 1_000_000),
            Overrides = new StrategyOverrides(),
        });
        def.ConditionalBlocks.Add(neverBlock);

        var resolved = CallResolveBranches(def, EmptySnap());

        Assert.That(resolved.TakeProfitTargets.Select(t => t.Price),
            Is.EqualTo(def.TakeProfitTargets.Select(t => t.Price)),
            "multi-target scale-out ladder must survive branch resolution");
    }
}
