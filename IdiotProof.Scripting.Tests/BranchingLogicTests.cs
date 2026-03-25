using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Scripting.Tests;

public class BranchingLogicTests
{
    // ========================================
    // BUILDER API TESTS
    // ========================================

    [Fact]
    public void Then_PopsLastCondition_FromEntryConditions()
    {
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        // IsAboveVwap was popped into the conditional block
        Assert.Single(strategy.EntryConditions); // only Breakout remains
        Assert.IsType<PatternCondition>(strategy.EntryConditions[0]);
    }

    [Fact]
    public void Then_CreatesConditionalBlock_WithOneBranch()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .EndIf()
            .Build();

        Assert.Single(strategy.ConditionalBlocks);
        Assert.Single(strategy.ConditionalBlocks[0].Branches);
        Assert.True(strategy.HasBranching);
    }

    [Fact]
    public void Then_WithoutPrecedingCondition_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Stock.Ticker("AAPL")
                .Then(b => b.Long()));
    }

    [Fact]
    public void ThenElse_CreatesTwoBranches()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        var block = Assert.Single(strategy.ConditionalBlocks);
        Assert.Equal(2, block.Branches.Count);
        Assert.NotNull(block.Branches[0].Condition); // Then has condition
        Assert.Null(block.Branches[1].Condition);     // Else has no condition
    }

    [Fact]
    public void ThenElseIfElse_CreatesThreeBranches()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .ElseIf(c => c.IsRsiOversold(), b => b.Long().TakeProfit(150))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        var block = Assert.Single(strategy.ConditionalBlocks);
        Assert.Equal(3, block.Branches.Count);
        Assert.NotNull(block.Branches[0].Condition);
        Assert.NotNull(block.Branches[1].Condition);
        Assert.Null(block.Branches[2].Condition);
    }

    [Fact]
    public void MultipleElseIf_ChainsCorrectly()
    {
        var strategy = Stock.Ticker("TSLA")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(250))
            .ElseIf(c => c.IsRsiOversold(), b => b.Long().TakeProfit(230))
            .ElseIf(c => c.IsMacdBearish(), b => b.Short().TakeProfit(200))
            .Else(b => b.Long().StopLossPercent(5))
            .Build();

        var block = Assert.Single(strategy.ConditionalBlocks);
        Assert.Equal(4, block.Branches.Count);
    }

    [Fact]
    public void EndIf_ReturnsToStrategyBuilder_ForContinuedChaining()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .EndIf()
            .StopLoss(145)
            .Repeat()
            .Build();

        Assert.Equal(145, strategy.StopLossPrice);
        Assert.True(strategy.ShouldRepeat);
        Assert.True(strategy.HasBranching);
    }

    [Fact]
    public void ChainingAfterElse_ContinuesOnStrategyBuilder()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .Else(b => b.Short())
            .StopLoss(145)
            .Build();

        Assert.Equal(145, strategy.StopLossPrice);
    }

    // ========================================
    // BRANCH OVERRIDES TESTS
    // ========================================

    [Fact]
    public void BranchBuilder_Long_SetsDirection()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.Equal(TradeDirection.Long, overrides.Direction);
    }

    [Fact]
    public void BranchBuilder_Short_SetsDirection()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Short())
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.Equal(TradeDirection.Short, overrides.Direction);
    }

    [Fact]
    public void BranchBuilder_TakeProfit_SetsSingleTarget()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.Equal(160, overrides.TakeProfitPrice);
    }

    [Fact]
    public void BranchBuilder_TakeProfit_MultipleTargets()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160, 170, 180))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.Equal(3, overrides.TakeProfitTargets.Count);
        Assert.Equal(160, overrides.TakeProfitTargets[0].Price);
        Assert.Equal(170, overrides.TakeProfitTargets[1].Price);
        Assert.Equal(180, overrides.TakeProfitTargets[2].Price);
    }

    [Fact]
    public void BranchBuilder_StopLoss_SetsPrice()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().StopLoss(140))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.Equal(140, overrides.StopLossPrice);
    }

    [Fact]
    public void BranchBuilder_TrailingStopLoss_SetsPercent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TrailingStopLoss(2.5))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.Equal(2.5, overrides.TrailingStopPercent);
    }

    [Fact]
    public void BranchBuilder_AddsEntryConditions()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().HoldsAbove(148))
            .EndIf()
            .Build();

        var overrides = strategy.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.Single(overrides.EntryConditions);
        Assert.IsType<PriceLevelCondition>(overrides.EntryConditions[0]);
    }

    // ========================================
    // CONDITION FACTORY TESTS
    // ========================================

    [Fact]
    public void ConditionFactory_CreatesIndicatorConditions()
    {
        var factory = new ConditionFactory();

        Assert.IsType<IndicatorCondition>(factory.IsAboveVwap());
        Assert.IsType<IndicatorCondition>(factory.IsBelowVwap());
        Assert.IsType<IndicatorCondition>(factory.IsEmaAbove(9));
        Assert.IsType<IndicatorCondition>(factory.IsEmaBelow(21));
        Assert.IsType<IndicatorCondition>(factory.IsDiPositive());
        Assert.IsType<IndicatorCondition>(factory.IsDiNegative());
        Assert.IsType<IndicatorCondition>(factory.IsAdxAbove(25));
        Assert.IsType<IndicatorCondition>(factory.IsRsiOversold());
        Assert.IsType<IndicatorCondition>(factory.IsRsiOverbought());
        Assert.IsType<IndicatorCondition>(factory.IsMacdBullish());
        Assert.IsType<IndicatorCondition>(factory.IsMacdBearish());
        Assert.IsType<IndicatorCondition>(factory.IsGapUp());
        Assert.IsType<IndicatorCondition>(factory.IsGapDown());
        Assert.IsType<IndicatorCondition>(factory.IsVolumeAbove(2.0));
    }

    [Fact]
    public void ConditionFactory_CreatesPriceLevelConditions()
    {
        var factory = new ConditionFactory();

        Assert.IsType<PriceLevelCondition>(factory.HoldsAbove(150));
        Assert.IsType<PriceLevelCondition>(factory.HoldsBelow(150));
        Assert.IsType<PriceLevelCondition>(factory.IsNear(150));
        Assert.IsType<PriceLevelCondition>(factory.BreaksAbove(150));
        Assert.IsType<PriceLevelCondition>(factory.BreaksBelow(150));
    }

    [Fact]
    public void ConditionFactory_CreatesPatternConditions()
    {
        var factory = new ConditionFactory();

        Assert.IsType<PatternCondition>(factory.Breakout(150));
        Assert.IsType<PatternCondition>(factory.Pullback());
    }

    // ========================================
    // EVALUATION TESTS
    // ========================================

    [Fact]
    public void ConditionalBlock_Evaluate_ReturnsFirstMatchingBranch()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        var aboveVwap = new IndicatorSnapshot { Price = 155, Vwap = 150 };
        var result = strategy.ConditionalBlocks[0].Evaluate(aboveVwap);

        Assert.NotNull(result);
        Assert.Equal(TradeDirection.Long, result.Overrides.Direction);
        Assert.Equal(160, result.Overrides.TakeProfitPrice);
    }

    [Fact]
    public void ConditionalBlock_Evaluate_FallsToElse_WhenIfFails()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .Build();

        var belowVwap = new IndicatorSnapshot { Price = 145, Vwap = 150 };
        var result = strategy.ConditionalBlocks[0].Evaluate(belowVwap);

        Assert.NotNull(result);
        Assert.Null(result.Condition); // Else branch
        Assert.Equal(TradeDirection.Short, result.Overrides.Direction);
        Assert.Equal(140, result.Overrides.TakeProfitPrice);
    }

    [Fact]
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

        Assert.NotNull(result);
        Assert.NotNull(result.Condition); // ElseIf, not Else
        Assert.Equal(150, result.Overrides.TakeProfitPrice);
    }

    [Fact]
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

        Assert.Null(result);
    }

    [Fact]
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

        Assert.NotNull(result);
        Assert.Equal(160, result.Overrides.TakeProfitPrice); // Then branch, not ElseIf
    }

    // ========================================
    // APPLY OVERRIDES TESTS
    // ========================================

    [Fact]
    public void ApplyTo_OverridesDirection()
    {
        var strategy = Stock.Ticker("AAPL")
            .Long()
            .Build();

        var overrides = new StrategyOverrides { Direction = TradeDirection.Short };
        overrides.ApplyTo(strategy);

        Assert.Equal(TradeDirection.Short, strategy.Direction);
    }

    [Fact]
    public void ApplyTo_OverridesTakeProfit()
    {
        var strategy = Stock.Ticker("AAPL")
            .TakeProfit(160)
            .Build();

        var overrides = new StrategyOverrides { TakeProfitPrice = 170 };
        overrides.ApplyTo(strategy);

        Assert.Equal(170, strategy.TakeProfitPrice);
    }

    [Fact]
    public void ApplyTo_OverridesStopLoss()
    {
        var strategy = Stock.Ticker("AAPL")
            .StopLoss(140)
            .Build();

        var overrides = new StrategyOverrides { StopLossPrice = 135 };
        overrides.ApplyTo(strategy);

        Assert.Equal(135, strategy.StopLossPrice);
    }

    [Fact]
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

        Assert.Equal(TradeDirection.Short, strategy.Direction);
        Assert.Equal(160, strategy.TakeProfitPrice); // unchanged
        Assert.Equal(140, strategy.StopLossPrice);   // unchanged
    }

    [Fact]
    public void ApplyTo_AddsEntryConditions()
    {
        var strategy = Stock.Ticker("AAPL")
            .Breakout(150)
            .Build();

        var overrides = new StrategyOverrides();
        overrides.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));
        overrides.ApplyTo(strategy);

        Assert.Equal(2, strategy.EntryConditions.Count);
    }

    // ========================================
    // TOSCRIPT SERIALIZATION TESTS
    // ========================================

    [Fact]
    public void ToScript_IncludesBranching()
    {
        var script = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .Else(b => b.Short().TakeProfit(140))
            .StopLoss(145)
            .ToScript();

        Assert.Contains("Then(", script);
        Assert.Contains("Else(", script);
        Assert.Contains("StopLoss(145)", script);
    }

    [Fact]
    public void ToScript_IncludesElseIf()
    {
        var script = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(160))
            .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(140))
            .Else(b => b.Long().TakeProfit(150))
            .ToScript();

        Assert.Contains("Then(", script);
        Assert.Contains("ElseIf(", script);
        Assert.Contains("Else(", script);
    }

    // ========================================
    // DELEGATE METHODS ON CONDITIONALBUILDER
    // ========================================

    [Fact]
    public void ConditionalBuilder_StopLoss_DelegatesToParent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .StopLoss(140)
            .Build();

        Assert.Equal(140, strategy.StopLossPrice);
    }

    [Fact]
    public void ConditionalBuilder_TrailingStopLoss_DelegatesToParent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .TrailingStopLoss(3)
            .Build();

        Assert.Equal(3, strategy.TrailingStopPercent);
    }

    [Fact]
    public void ConditionalBuilder_Repeat_DelegatesToParent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .Repeat()
            .Build();

        Assert.True(strategy.ShouldRepeat);
    }

    [Fact]
    public void ConditionalBuilder_Build_DelegatesToParent()
    {
        var strategy = Stock.Ticker("AAPL")
            .IsAboveVwap()
            .Then(b => b.Long())
            .Build();

        Assert.Equal("AAPL", strategy.Symbol);
        Assert.True(strategy.HasBranching);
    }

    // ========================================
    // INTEGRATION / REALISTIC SCENARIOS
    // ========================================

    [Fact]
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
        Assert.Equal("ERNA", strategy.Symbol);
        Assert.Equal(2, strategy.EntryConditions.Count); // Breakout + Pullback (VWAP was popped)
        Assert.Equal(0.46, strategy.StopLossPrice);
        Assert.True(strategy.ShouldRepeat);

        var block = Assert.Single(strategy.ConditionalBlocks);
        Assert.Equal(3, block.Branches.Count);

        // Then branch: Long with multi-target
        Assert.Equal(TradeDirection.Long, block.Branches[0].Overrides.Direction);
        Assert.Equal(2, block.Branches[0].Overrides.TakeProfitTargets.Count);

        // ElseIf branch: Short
        Assert.Equal(TradeDirection.Short, block.Branches[1].Overrides.Direction);

        // Else branch: Long conservative
        Assert.Equal(TradeDirection.Long, block.Branches[2].Overrides.Direction);
        Assert.Equal(0.55, block.Branches[2].Overrides.TakeProfitPrice);

        // Evaluate with above VWAP data
        var aboveVwap = new IndicatorSnapshot { Price = 0.54, Vwap = 0.50 };
        var match = block.Evaluate(aboveVwap);
        Assert.Equal(TradeDirection.Long, match!.Overrides.Direction);

        // Evaluate with below VWAP data
        var belowVwap = new IndicatorSnapshot { Price = 0.48, Vwap = 0.50 };
        match = block.Evaluate(belowVwap);
        Assert.Equal(TradeDirection.Short, match!.Overrides.Direction);
    }

    [Fact]
    public void FullStrategy_MomentumWithAdxFilter()
    {
        var strategy = Stock.Ticker("TSLA")
            .IsAdxAbove(25)
            .Then(b => b.Long().TakeProfit(250).TrailingStopLoss(2))
            .ElseIf(c => c.IsRsiOversold(35), b => b.Long().TakeProfit(230).StopLoss(210))
            .ElseIf(c => c.IsMacdBearish(), b => b.Short().TakeProfit(200).StopLoss(240))
            .Else(b => b.Long().StopLossPercent(5))
            .Build();

        Assert.True(strategy.HasBranching);
        var block = Assert.Single(strategy.ConditionalBlocks);
        Assert.Equal(4, block.Branches.Count);

        // Strong trend: ADX > 25
        var trending = new IndicatorSnapshot { Price = 235, Adx = 30, PlusDI = 28, MinusDI = 15 };
        var match = block.Evaluate(trending);
        Assert.Equal(TradeDirection.Long, match!.Overrides.Direction);
        Assert.Equal(250, match.Overrides.TakeProfitPrice);

        // Weak trend, oversold RSI
        var oversold = new IndicatorSnapshot { Price = 220, Adx = 15, Rsi = 28 };
        match = block.Evaluate(oversold);
        Assert.Equal(230, match!.Overrides.TakeProfitPrice);
        Assert.Equal(210, match.Overrides.StopLossPrice);

        // Weak trend, not oversold, MACD bearish
        var bearish = new IndicatorSnapshot
        {
            Price = 225, Adx = 15, Rsi = 50,
            MacdLine = -0.5, SignalLine = 0.5
        };
        match = block.Evaluate(bearish);
        Assert.Equal(TradeDirection.Short, match!.Overrides.Direction);
        Assert.Equal(200, match.Overrides.TakeProfitPrice);

        // Nothing matches first three — falls to Else
        var neutral = new IndicatorSnapshot
        {
            Price = 225, Adx = 15, Rsi = 50,
            MacdLine = 1.0, SignalLine = 0.5
        };
        match = block.Evaluate(neutral);
        Assert.Null(match!.Condition); // Else
        Assert.Equal(5, match.Overrides.StopLossPercent);
    }
}
