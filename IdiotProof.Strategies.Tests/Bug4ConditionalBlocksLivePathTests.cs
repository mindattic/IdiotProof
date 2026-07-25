using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Tests for Bug 4 (FIXED): <see cref="MonitorWorker"/> previously evaluated
/// <c>def.EntryConditions</c> directly and silently ignored all
/// <c>ConditionalBlocks</c> authored in the UI.  Branches therefore only
/// worked in the backtester, not in live trading.
///
/// FIX
/// ---
/// <c>StrategyBranchResolver.Resolve</c> was extracted from the private
/// <c>DslStrategy.ResolveBranches</c> so both <see cref="DslStrategy.Evaluate"/>
/// and <c>MonitorWorker.EvaluateOneAsync</c> call the same resolver.
///
/// WHAT THESE TESTS VERIFY
/// -----------------------
/// 1. <see cref="StrategyBranchResolver.Resolve"/> applies branch overrides when
///    the branch condition is met — the resolved definition differs from the base.
/// 2. Direction override via a branch changes the resolved direction.
/// 3. EntryConditions override via a branch replaces the condition list.
/// 4. StopLoss/TakeProfit overrides survive through the resolver.
/// 5. A non-matching branch leaves the base definition unchanged.
/// 6. Multiple blocks: each contributes its first-matching branch independently.
/// 7. <see cref="DslStrategy.Evaluate"/> honors branches in the signal it emits
///    (direction on the signal reflects the branch override, not the base).
/// </summary>
public class Bug4ConditionalBlocksLivePathTests
{
    private static IndicatorSnapshot Snap(double price = 10.0, double vwap = 9.0) => new()
    {
        Symbol    = "TEST",
        Timestamp = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc),
        Price     = price,
        Vwap      = vwap,
    };

    private static IReadOnlyList<Candle> Bars(double price = 10.0)
    {
        var start = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
        return
        [
            new Candle
            {
                Symbol = "TEST", StartUtc = start, EndUtc = start.AddMinutes(1),
                Open = (decimal)price, High = (decimal)(price * 1.02),
                Low = (decimal)(price * 0.99), Close = (decimal)price,
                Volume = 2_000_000,
            },
        ];
    }

    // ── StrategyBranchResolver.Resolve: branch-match behavior ────────────

    [Test]
    public void Resolve_MatchingBranch_ChangesDirection()
    {
        // Base = Long; branch (price > 0, always true) overrides to Short.
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0),
            Overrides = new StrategyOverrides { Direction = TradeDirection.Short },
        });
        def.ConditionalBlocks.Add(block);

        var resolved = StrategyBranchResolver.Resolve(def, Snap(price: 10.0));

        Assert.That(resolved.Direction, Is.EqualTo(TradeDirection.Short),
            "matching branch must override Direction");
        Assert.That(def.Direction,      Is.EqualTo(TradeDirection.Long),
            "base definition must not be mutated");
    }

    [Test]
    public void Resolve_NonMatchingBranch_DoesNotChangeBaseDefinition()
    {
        // Branch condition: price > $99,999 — never matches at $10.
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 99_999),
            Overrides = new StrategyOverrides { Direction = TradeDirection.Short },
        });
        def.ConditionalBlocks.Add(block);

        var resolved = StrategyBranchResolver.Resolve(def, Snap(price: 10.0));

        Assert.That(resolved.Direction, Is.EqualTo(TradeDirection.Long),
            "non-matching branch must not change Direction");
    }

    [Test]
    public void Resolve_MatchingBranch_ChangesStopAndTarget()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).TakeProfit(12.0).Build();
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0),
            Overrides = new StrategyOverrides
            {
                StopLossPercent  = 8,
                TakeProfitPrice  = 15.0,
                TrailingStopPercent = 3,
            },
        });
        def.ConditionalBlocks.Add(block);

        var resolved = StrategyBranchResolver.Resolve(def, Snap());

        Assert.Multiple(() =>
        {
            Assert.That(resolved.StopLossPercent,    Is.EqualTo(8),    "StopLossPercent overridden by branch");
            Assert.That(resolved.TakeProfitPrice,    Is.EqualTo(15.0), "TakeProfitPrice overridden by branch");
            Assert.That(resolved.TrailingStopPercent, Is.EqualTo(3),   "TrailingStopPercent overridden by branch");
        });
    }

    [Test]
    public void Resolve_MultipleBlocks_EachAppliesIndependently()
    {
        // Two blocks: block 1 overrides Direction, block 2 overrides StopLossPercent.
        // Both branch conditions always match.
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();

        var block1 = new ConditionalBlock();
        block1.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0),
            Overrides = new StrategyOverrides { Direction = TradeDirection.Short },
        });

        var block2 = new ConditionalBlock();
        block2.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0),
            Overrides = new StrategyOverrides { StopLossPercent = 12 },
        });

        def.ConditionalBlocks.Add(block1);
        def.ConditionalBlocks.Add(block2);

        var resolved = StrategyBranchResolver.Resolve(def, Snap());

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Direction,       Is.EqualTo(TradeDirection.Short), "block 1 override applied");
            Assert.That(resolved.StopLossPercent, Is.EqualTo(12),                   "block 2 override applied");
        });
    }

    [Test]
    public void Resolve_FirstMatchingBranchWins_SecondBranchIgnored()
    {
        // Two branches in one block: both conditions match (price > 0).
        // Only the first-matching branch's overrides apply per ConditionalBlock semantics.
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0),
            Overrides = new StrategyOverrides { StopLossPercent = 20 },
        });
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0),
            Overrides = new StrategyOverrides { StopLossPercent = 99 },
        });
        def.ConditionalBlocks.Add(block);

        var resolved = StrategyBranchResolver.Resolve(def, Snap());

        Assert.That(resolved.StopLossPercent, Is.EqualTo(20),
            "first matching branch in a ConditionalBlock wins; second is not evaluated");
    }

    // ── DslStrategy.Evaluate: branch override flows through to the signal ─

    [Test]
    public void DslStrategy_BranchOverridesDirection_SignalReflectsResolvedDirection()
    {
        // Base = Long; branch (price > 0, always true) overrides to Short.
        // The emitted TradeSignal must have Direction = Short.
        // (Bug 4 fix: before the fix, the Monitor would evaluate the base Long
        // conditions and emit Long — the branch override was silently discarded.)
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0),
            Overrides = new StrategyOverrides { Direction = TradeDirection.Short },
        });
        def.ConditionalBlocks.Add(block);

        var signals = new DslStrategy(def).Evaluate("TEST", Bars(), new StrategyContext());

        Assert.That(signals, Has.Count.EqualTo(1), "strategy must fire");
        Assert.That(signals[0].Direction, Is.EqualTo(TradeDirection.Short),
            "TradeSignal.Direction must reflect the branch override, not the base definition");
    }

    [Test]
    public void DslStrategy_BranchAddsCondition_FiresOnlyWhenBranchConditionAlsoPasses()
    {
        // Base: no entry conditions (fires immediately).
        // Branch (always matches): overrides EntryConditions to [price > $99,999].
        // Result: strategy must NOT fire at price=$10, because the branch's
        // injected condition blocks it.
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        Assert.That(def.EntryConditions, Is.Empty, "pre-condition: base has no entry conditions");

        var block = new ConditionalBlock();
        var overrides = new StrategyOverrides();
        overrides.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.HoldsAbove, 99_999));
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 0), // always matches
            Overrides = overrides,
        });
        def.ConditionalBlocks.Add(block);

        var signals = new DslStrategy(def).Evaluate("TEST", Bars(price: 10.0), new StrategyContext());

        Assert.That(signals, Is.Empty,
            "branch-injected entry condition (price > $99,999) must block the fire at $10");
    }

    [Test]
    public void DslStrategy_NoBranch_OriginalConditionsUsed()
    {
        // Sanity check: a strategy with no ConditionalBlocks uses the base
        // EntryConditions exactly, and the fast-path (no clone) is exercised.
        // No entry conditions = setup-only strategy that fires immediately.
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        Assert.That(def.ConditionalBlocks, Is.Empty, "pre-condition: no branches");

        var signals = new DslStrategy(def).Evaluate("TEST", Bars(price: 10.0), new StrategyContext());

        Assert.That(signals, Has.Count.EqualTo(1),
            "setup-only strategy with no ConditionalBlocks must fire on any bar");
    }
}
