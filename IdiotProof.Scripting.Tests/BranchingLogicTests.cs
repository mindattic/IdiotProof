using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Scripting.Tests;

public class BranchingLogicTests
{
    // ========================================
    // BUILDER API TESTS
    // ========================================

    [Test]
    public void Then_PopsLastCondition_FromEntryConditions()
    {
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        // IsAboveVwap was popped into the conditional block
        Assert.That(strategy.EntryConditions, Has.Count.EqualTo(1)); // only Breakout remains
        Assert.That(strategy.EntryConditions[0], Is.InstanceOf<PatternCondition>());
    }

    [Test]
    public void Then_CreatesConditionalBlock_WithOneBranch()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .EndIf()
            .Build();

        Assert.That(strategy.ConditionalBlocks, Has.Count.EqualTo(1));
        Assert.That(strategy.ConditionalBlocks[0].Branches, Has.Count.EqualTo(1));
        Assert.That(strategy.HasBranching, Is.True);
    }

    [Test]
    public void Then_WithoutPrecedingCondition_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Stock.Ticker("AAPL")
                .Then(b => b.Long()));
    }

    [Test]
    public void ThenElse_CreatesTwoBranches()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        Assert.That(strategy.ConditionalBlocks, Has.Count.EqualTo(1));
        var block = strategy.ConditionalBlocks[0];
        Assert.That(block.Branches.Count, Is.EqualTo(2));
        Assert.That(block.Branches[0].Condition, Is.Not.Null); // Then has condition
        Assert.That(block.Branches[1].Condition, Is.Null);     // Else has no condition
    }

    [Test]
    public void ThenElseIfElse_CreatesThreeBranches()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .ElseIf(c => c.IsRsiOversold(), b => b.Long().TakeProfit(150))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        Assert.That(strategy.ConditionalBlocks, Has.Count.EqualTo(1));
        var block = strategy.ConditionalBlocks[0];
        Assert.That(block.Branches.Count, Is.EqualTo(3));
        Assert.That(block.Branches[0].Condition, Is.Not.Null);
        Assert.That(block.Branches[1].Condition, Is.Not.Null);
        Assert.That(block.Branches[2].Condition, Is.Null);
    }

    [Test]
    public void MultipleElseIf_ChainsCorrectly()
    {
        var strategy = Stock.Ticker("TSLA")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(250))
            .ElseIf(c => c.IsRsiOversold(), b => b.Long().TakeProfit(230))
            .ElseIf(c => c.IsMacdBearish(), b => b.Short().TakeProfit(200))
            .Else(b => b.Long().StopLossPercent(5))
            .Build();

        Assert.That(strategy.ConditionalBlocks, Has.Count.EqualTo(1));
        var block = strategy.ConditionalBlocks[0];
        Assert.That(block.Branches.Count, Is.EqualTo(4));
    }

    [Test]
    public void EndIf_ReturnsToStrategyBuilder_ForContinuedChaining()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .EndIf()
            .StopLoss(145)
            .Repeat()
            .Build();

        Assert.That(strategy.StopLossPrice, Is.EqualTo(145));
        Assert.That(strategy.ShouldRepeat, Is.True);
        Assert.That(strategy.HasBranching, Is.True);
    }

    [Test]
    public void ChainingAfterElse_ContinuesOnStrategyBuilder()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .Else(b => b.Short())
            .StopLoss(145)
            .Build();

        Assert.That(strategy.StopLossPrice, Is.EqualTo(145));
    }

    // ========================================
    // BRANCH OVERRIDES TESTS
    // ========================================

    [Test]
    public void BranchBuilder_Long_SetsDirection()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.That(overrides.Direction, Is.EqualTo(TradeDirection.Long));
    }

    [Test]
    public void BranchBuilder_Short_SetsDirection()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Short())
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.That(overrides.Direction, Is.EqualTo(TradeDirection.Short));
    }

    [Test]
    public void BranchBuilder_TakeProfit_SetsSingleTarget()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.That(overrides.TakeProfitPrice, Is.EqualTo(160));
    }

    [Test]
    public void BranchBuilder_TakeProfit_MultipleTargets()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160, 170, 180))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.That(overrides.TakeProfitTargets.Count, Is.EqualTo(3));
        Assert.That(overrides.TakeProfitTargets[0].Price, Is.EqualTo(160));
        Assert.That(overrides.TakeProfitTargets[1].Price, Is.EqualTo(170));
        Assert.That(overrides.TakeProfitTargets[2].Price, Is.EqualTo(180));
    }

    [Test]
    public void BranchBuilder_StopLoss_SetsPrice()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().StopLoss(140))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.That(overrides.StopLossPrice, Is.EqualTo(140));
    }

    [Test]
    public void BranchBuilder_TrailingStopLoss_SetsPercent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TrailingStopLoss(2.5))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.That(overrides.TrailingStopPercent, Is.EqualTo(2.5));
    }

    [Test]
    public void BranchBuilder_AddsEntryConditions()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().HoldsAbove(148))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.That(overrides.EntryConditions, Has.Count.EqualTo(1));
        Assert.That(overrides.EntryConditions[0], Is.InstanceOf<PriceLevelCondition>());
    }

    // ========================================
    // CONDITION FACTORY TESTS
    // ========================================

    [Test]
    public void ConditionFactory_CreatesIndicatorConditions()
    {
        var factory = new ConditionFactory();

        Assert.That(factory.IsAboveVwap(),     Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsBelowVwap(),     Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsEmaAbove(9),     Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsEmaBelow(21),    Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsDiPositive(),    Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsDiNegative(),    Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsAdxAbove(25),    Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsRsiOversold(),   Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsRsiOverbought(), Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsMacdBullish(),   Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsMacdBearish(),   Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsGapUp(),         Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsGapDown(),       Is.InstanceOf<IndicatorCondition>());
        Assert.That(factory.IsVolumeAbove(2.0), Is.InstanceOf<IndicatorCondition>());
    }

    [Test]
    public void ConditionFactory_CreatesPriceLevelConditions()
    {
        var factory = new ConditionFactory();

        Assert.That(factory.HoldsAbove(150),  Is.InstanceOf<PriceLevelCondition>());
        Assert.That(factory.HoldsBelow(150),  Is.InstanceOf<PriceLevelCondition>());
        Assert.That(factory.IsNear(150),      Is.InstanceOf<PriceLevelCondition>());
        Assert.That(factory.BreaksAbove(150), Is.InstanceOf<PriceLevelCondition>());
        Assert.That(factory.BreaksBelow(150), Is.InstanceOf<PriceLevelCondition>());
    }

    [Test]
    public void ConditionFactory_CreatesPatternConditions()
    {
        var factory = new ConditionFactory();

        Assert.That(factory.Breakout(150), Is.InstanceOf<PatternCondition>());
        Assert.That(factory.Pullback(),    Is.InstanceOf<PatternCondition>());
    }

    // ========================================
    // EVALUATION TESTS
    // ========================================

    [Test]
    public void ConditionalBlock_Evaluate_ReturnsFirstMatchingBranch()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        var aboveVwap = new IndicatorSnapshot { Price = 155, Vwap = 150 };
        var result = strategy.ConditionalBlocks[0].Evaluate(aboveVwap);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Overrides.Direction, Is.EqualTo(TradeDirection.Long));
        Assert.That(result.Overrides.TakeProfitPrice, Is.EqualTo(160));
    }

    [Test]
    public void ConditionalBlock_Evaluate_FallsToElse_WhenIfFails()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        var belowVwap = new IndicatorSnapshot { Price = 145, Vwap = 150 };
        var result = strategy.ConditionalBlocks[0].Evaluate(belowVwap);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Condition, Is.Null); // Else branch
        Assert.That(result.Overrides.Direction, Is.EqualTo(TradeDirection.Short));
        Assert.That(result.Overrides.TakeProfitPrice, Is.EqualTo(140));
    }

    [Test]
    public void ConditionalBlock_Evaluate_MatchesElseIf_WhenIfFails()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .ElseIf(c => c.IsRsiOversold(40), b => b.Long().TakeProfit(150))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        // Below VWAP but RSI is oversold (35 < 40)
        var snapshot = new IndicatorSnapshot { Price = 145, Vwap = 150, Rsi = 35 };
        var result = strategy.ConditionalBlocks[0].Evaluate(snapshot);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Condition, Is.Not.Null); // ElseIf, not Else
        Assert.That(result.Overrides.TakeProfitPrice, Is.EqualTo(150));
    }

    [Test]
    public void ConditionalBlock_Evaluate_ReturnsNull_WhenNoBranchMatches()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .ElseIf(c => c.IsRsiOversold(), b => b.Long().TakeProfit(150))
            .EndIf()
            .Build();

        // Below VWAP and RSI is 50 (not oversold)
        var snapshot = new IndicatorSnapshot { Price = 145, Vwap = 150, Rsi = 50 };
        var result = strategy.ConditionalBlocks[0].Evaluate(snapshot);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConditionalBlock_Evaluate_FirstMatchWins()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .ElseIf(c => c.IsMacdBullish(), b => b.Long().TakeProfit(155))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        // Above VWAP AND MACD bullish — first branch should win
        var snapshot = new IndicatorSnapshot
        {
            Price = 155, Vwap = 150,
            MacdLine = 1.5, SignalLine = 1.0
        };
        var result = strategy.ConditionalBlocks[0].Evaluate(snapshot);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Overrides.TakeProfitPrice, Is.EqualTo(160)); // Then branch, not ElseIf
    }

    // ========================================
    // APPLY OVERRIDES TESTS
    // ========================================

    [Test]
    public void ApplyTo_OverridesDirection()
    {
        var strategy = Stock.Ticker("AAPL")
            .Long()
            .Build();

        var overrides = new StrategyOverrides { Direction = TradeDirection.Short };
        overrides.ApplyTo(strategy);

        Assert.That(strategy.Direction, Is.EqualTo(TradeDirection.Short));
    }

    [Test]
    public void ApplyTo_OverridesTakeProfit()
    {
        var strategy = Stock.Ticker("AAPL")
            .TakeProfit(160)
            .Build();

        var overrides = new StrategyOverrides { TakeProfitPrice = 170 };
        overrides.ApplyTo(strategy);

        Assert.That(strategy.TakeProfitPrice, Is.EqualTo(170));
    }

    [Test]
    public void ApplyTo_OverridesStopLoss()
    {
        var strategy = Stock.Ticker("AAPL")
            .StopLoss(140)
            .Build();

        var overrides = new StrategyOverrides { StopLossPrice = 135 };
        overrides.ApplyTo(strategy);

        Assert.That(strategy.StopLossPrice, Is.EqualTo(135));
    }

    [Test]
    public void ApplyTo_LeavesUnsetPropertiesAlone()
    {
        var strategy = Stock.Ticker("AAPL")
            .Long()
            .TakeProfit(160)
            .StopLoss(140)
            .Build();

        // Only override direction, leave exits alone
        var overrides = new StrategyOverrides { Direction = TradeDirection.Short };
        overrides.ApplyTo(strategy);

        Assert.That(strategy.Direction, Is.EqualTo(TradeDirection.Short));
        Assert.That(strategy.TakeProfitPrice, Is.EqualTo(160)); // unchanged
        Assert.That(strategy.StopLossPrice, Is.EqualTo(140));   // unchanged
    }

    [Test]
    public void ApplyTo_AddsEntryConditions()
    {
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Build();

        var overrides = new StrategyOverrides();
        overrides.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));
        overrides.ApplyTo(strategy);

        Assert.That(strategy.EntryConditions.Count, Is.EqualTo(2));
    }

    // ========================================
    // TOSCRIPT SERIALIZATION TESTS
    // ========================================

    [Test]
    public void ToScript_IncludesBranching()
    {
        var script = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .StopLoss(145)
            .ToScript();

        Assert.That(script, Does.Contain("Then("));
        Assert.That(script, Does.Contain("Else("));
        Assert.That(script, Does.Contain("StopLoss(145)"));
    }

    [Test]
    public void ToScript_IncludesElseIf()
    {
        var script = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(140))
            .Else(b => b.Long().TakeProfit(150))
            .ToScript();

        Assert.That(script, Does.Contain("Then("));
        Assert.That(script, Does.Contain("ElseIf("));
        Assert.That(script, Does.Contain("Else("));
    }

    // ========================================
    // DELEGATE METHODS ON CONDITIONALBUILDER
    // ========================================

    [Test]
    public void ConditionalBuilder_StopLoss_DelegatesToParent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .StopLoss(140)
            .Build();

        Assert.That(strategy.StopLossPrice, Is.EqualTo(140));
    }

    [Test]
    public void ConditionalBuilder_TrailingStopLoss_DelegatesToParent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .TrailingStopLoss(3)
            .Build();

        Assert.That(strategy.TrailingStopPercent, Is.EqualTo(3));
    }

    [Test]
    public void ConditionalBuilder_Repeat_DelegatesToParent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .Repeat()
            .Build();

        Assert.That(strategy.ShouldRepeat, Is.True);
    }

    [Test]
    public void ConditionalBuilder_Build_DelegatesToParent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .Build();

        Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
        Assert.That(strategy.HasBranching, Is.True);
    }

    // ========================================
    // INTEGRATION / REALISTIC SCENARIOS
    // ========================================

    [Test]
    public void FullStrategy_VwapDirectionalPlay()
    {
        var strategy = Stock.Ticker("ERNA")
            .Breakout(0.52)
            .Pullback()
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(0.66, 0.88))
            .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(0.40))
            .Else(b => b.Long().TakeProfit(0.55))
            .StopLoss(0.46)
            .Repeat()
            .Build();

        // Verify structure
        Assert.That(strategy.Symbol, Is.EqualTo("ERNA"));
        Assert.That(strategy.EntryConditions.Count, Is.EqualTo(2)); // Breakout + Pullback (VWAP was popped)
        Assert.That(strategy.StopLossPrice, Is.EqualTo(0.46));
        Assert.That(strategy.ShouldRepeat, Is.True);

        Assert.That(strategy.ConditionalBlocks, Has.Count.EqualTo(1));
        var block = strategy.ConditionalBlocks[0];
        Assert.That(block.Branches.Count, Is.EqualTo(3));

        // Then branch: Long with multi-target
        Assert.That(block.Branches[0].Overrides.Direction, Is.EqualTo(TradeDirection.Long));
        Assert.That(block.Branches[0].Overrides.TakeProfitTargets.Count, Is.EqualTo(2));

        // ElseIf branch: Short
        Assert.That(block.Branches[1].Overrides.Direction, Is.EqualTo(TradeDirection.Short));

        // Else branch: Long conservative
        Assert.That(block.Branches[2].Overrides.Direction, Is.EqualTo(TradeDirection.Long));
        Assert.That(block.Branches[2].Overrides.TakeProfitPrice, Is.EqualTo(0.55));

        // Evaluate with above VWAP data
        var aboveVwap = new IndicatorSnapshot { Price = 0.54, Vwap = 0.50 };
        var match = block.Evaluate(aboveVwap);
        Assert.That(match!.Overrides.Direction, Is.EqualTo(TradeDirection.Long));

        // Evaluate with below VWAP data
        var belowVwap = new IndicatorSnapshot { Price = 0.48, Vwap = 0.50 };
        match = block.Evaluate(belowVwap);
        Assert.That(match!.Overrides.Direction, Is.EqualTo(TradeDirection.Short));
    }

    [Test]
    public void FullStrategy_MomentumWithAdxFilter()
    {
        var strategy = Stock.Ticker("TSLA")
            .IsAdxAbove(25)
            .Then(b => b.Long().TakeProfit(250).TrailingStopLoss(2))
            .ElseIf(c => c.IsRsiOversold(35), b => b.Long().TakeProfit(230).StopLoss(210))
            .ElseIf(c => c.IsMacdBearish(), b => b.Short().TakeProfit(200).StopLoss(240))
            .Else(b => b.Long().StopLossPercent(5))
            .Build();

        Assert.That(strategy.HasBranching, Is.True);
        Assert.That(strategy.ConditionalBlocks, Has.Count.EqualTo(1));
        var block = strategy.ConditionalBlocks[0];
        Assert.That(block.Branches.Count, Is.EqualTo(4));

        // Strong trend: ADX > 25
        var trending = new IndicatorSnapshot { Price = 235, Adx = 30, PlusDI = 28, MinusDI = 15 };
        var match = block.Evaluate(trending);
        Assert.That(match!.Overrides.Direction, Is.EqualTo(TradeDirection.Long));
        Assert.That(match.Overrides.TakeProfitPrice, Is.EqualTo(250));

        // Weak trend, oversold RSI
        var oversold = new IndicatorSnapshot { Price = 220, Adx = 15, Rsi = 28 };
        match = block.Evaluate(oversold);
        Assert.That(match!.Overrides.TakeProfitPrice, Is.EqualTo(230));
        Assert.That(match.Overrides.StopLossPrice, Is.EqualTo(210));

        // Weak trend, not oversold, MACD bearish
        var bearish = new IndicatorSnapshot
        {
            Price = 225, Adx = 15, Rsi = 50,
            MacdLine = -0.5, SignalLine = 0.5
        };
        match = block.Evaluate(bearish);
        Assert.That(match!.Overrides.Direction, Is.EqualTo(TradeDirection.Short));
        Assert.That(match.Overrides.TakeProfitPrice, Is.EqualTo(200));

        // Nothing matches first three — falls to Else
        var neutral = new IndicatorSnapshot
        {
            Price = 225, Adx = 15, Rsi = 50,
            MacdLine = 1.0, SignalLine = 0.5
        };
        match = block.Evaluate(neutral);
        Assert.That(match!.Condition, Is.Null); // Else
        Assert.That(match.Overrides.StopLossPercent, Is.EqualTo(5));
    }
}
