using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>If/ElseIf/Else in text IdiotScript (IP-T396): text ↔ JSON ↔ text is lossless, and malformed blocks are diagnosed.</summary>
public class ScriptBranchRoundTripTests
{
    private static string Lossless(StrategyDefinition def, bool expectClean = true)
    {
        var json = StrategyJson.Serialize(def);
        var text = def.ToScript();
        var parsed = ScriptParser.ParseScript(text);
        Assert.That(parsed, Is.Not.Null, text);
        Assert.That(StrategyJson.Serialize(parsed!), Is.EqualTo(json), "JSON → text → JSON changed:\n" + text);
        Assert.That(parsed!.ToScript(), Is.EqualTo(text), "text → text changed");
        if (expectClean)
            Assert.That(ScriptDiagnostics.Analyze(text).Where(d => d.IsError), Is.Empty, text);
        return text;
    }

    private static ConditionalBranch Branch(ICondition? condition, StrategyOverrides overrides) =>
        new() { Condition = condition, Overrides = overrides };

    private static StrategyDefinition WithBlock(StrategyDefinition def, params ConditionalBranch[] branches)
    {
        var block = new ConditionalBlock();
        block.Branches.AddRange(branches);
        def.ConditionalBlocks.Add(block);
        return def;
    }

    private static StrategyDefinition Base() =>
        Stock.Ticker("AAPL").IsPriceBetween(1, 100000).Long().StopLossPercent(3).TakeProfitPercent(2).Build();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("IdiotProof.slnx not found above the test directory.");
    }

    [Test]
    public void GhostReplayFixture_RoundTripsThroughText()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "IdiotProof.Cypress", "cypress", "fixtures", "ghost-branched-strategy.json"));
        var def = StrategyJson.Deserialize(json);
        Assert.That(def.ConditionalBlocks, Has.Count.EqualTo(1));
        var text = Lossless(def);
        Assert.That(text, Does.Contain(".If(IsAboveVwap())"));
        Assert.That(text, Does.Contain(".EndIf()"));
    }

    private static IEnumerable<TestCaseData> BranchedDefinitions()
    {
        yield return new TestCaseData(WithBlock(Base(),
            Branch(Conditions.IsAboveVwap.And(Conditions.IsAboveEma(9)),
                new StrategyOverrides { Direction = TradeDirection.Long, TakeProfitPrice = 12, StopLossPercent = 2 }),
            Branch(Conditions.IsBelowVwap.Or(Conditions.IsGapUp(5).Not()),
                new StrategyOverrides { Direction = TradeDirection.Short, StopLossPrice = 149.5 }),
            Branch(null, new StrategyOverrides { TrailingStopPercent = 0.5 }))).SetName("Composite conditions, three branches");

        var guarded = new StrategyOverrides { StopLossPercent = 1 };
        guarded.EntryConditions.Add(Conditions.NotFallingKnife(0.4, 30));
        guarded.EntryConditions.Add(Conditions.LeaderConfirms(3, 0.2).And(Conditions.NotBullTrap(0.3, 50)));
        yield return new TestCaseData(WithBlock(Base(),
            Branch(Conditions.NotLeaderDiverging(2, 0.1), guarded),
            Branch(Conditions.IsRsiOversold(25), new StrategyOverrides()))).SetName("Trap and leader guards inside branches, empty body");

        var custom = new StrategyOverrides { TakeProfitPrice = 10 };
        custom.TakeProfitTargets.Add(new TakeProfitTarget { Price = 10.5, PercentToSell = 25, Label = "A" });
        custom.TakeProfitTargets.Add(new TakeProfitTarget { Price = 11.25, PercentToSell = 75, Label = "B" });
        yield return new TestCaseData(WithBlock(Base(), Branch(Conditions.IsMacdBullish, custom))).SetName("Custom targets keep percents and labels");

        var shorthand = new StrategyOverrides { TakeProfitPrice = 152 };
        shorthand.TakeProfitTargets.Add(new TakeProfitTarget { Price = 152, PercentToSell = 33, Label = "T1" });
        shorthand.TakeProfitTargets.Add(new TakeProfitTarget { Price = 158, PercentToSell = 33, Label = "T2" });
        shorthand.TakeProfitTargets.Add(new TakeProfitTarget { Price = 160, PercentToSell = 34, Label = "T3" });
        yield return new TestCaseData(WithBlock(Base(), Branch(Conditions.IsAboveVwap, shorthand))).SetName("Three-target shorthand");

        var noPrice = new StrategyOverrides();
        noPrice.TakeProfitTargets.Add(new TakeProfitTarget { Price = 5, PercentToSell = 50, Label = "T1" });
        noPrice.TakeProfitTargets.Add(new TakeProfitTarget { Price = 6, PercentToSell = 50, Label = "T2" });
        yield return new TestCaseData(WithBlock(Base(), Branch(Conditions.IsAboveVwap, noPrice))).SetName("Targets without a single target price");

        yield return new TestCaseData(WithBlock(Base(), Branch(null, new StrategyOverrides { StopLossPrice = 9 })))
            .SetName("Block that is only an Else");

        yield return new TestCaseData(WithBlock(Base())).SetName("Empty block");

        var looped = Stock.Ticker("ABC")
            .While().IsAboveVwap().EndWhile()
            .ExitWhenFalse()
            .BreaksAbove(10.5)
            .RequireEntryWindow("09:30", "11:00")
            .Long()
            .StopLoss(10)
            .ForEach(11, 11.5, 12).TakeProfitEach()
            .Repeat(2)
            .Build();
        WithBlock(looped,
            Branch(new TimeWindowCondition(TimeSpan.FromHours(9.5), TimeSpan.FromHours(10)), new StrategyOverrides { StopLossPrice = 10.25 }),
            Branch(new GapBandCondition(3, 20).And(new PriceBandCondition(1, 5)), new StrategyOverrides { Direction = TradeDirection.Short }));
        WithBlock(looped,
            Branch(new PriceLevelCondition(PriceLevelType.HoldsAbove, 10.5), new StrategyOverrides { TrailingStopPercent = 1.5 }));
        yield return new TestCaseData(looped).SetName("Two blocks alongside While, ForEach ladder and Repeat(n)");

        var composedBase = Stock.Ticker("NVDA").Name("Branchy").RequireAdxAbove(20).Long().StopLoss(1).Build();
        composedBase.EntryConditions.Add(Conditions.IsAboveVwap.And(Conditions.IsAboveEma(9)).Or(Conditions.IsGapUp(5).Not()));
        yield return new TestCaseData(WithBlock(composedBase,
            Branch(Conditions.IsAboveVwap, new StrategyOverrides { Direction = TradeDirection.Long, TakeProfitPrice = 12, StopLossPercent = 3 }),
            Branch(null, new StrategyOverrides { Direction = TradeDirection.Short }))).SetName("Composed top-level entry condition plus a block");

        yield return new TestCaseData(Stock.Ticker("NVDA")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(152, 158).StopLossPercent(2))
            .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(141).StopLoss(149.5))
            .Else(b => b.Long().HoldsAbove(140))
            .StopLoss(1)
            .Build()).SetName("C# builder form");
    }

    [TestCaseSource(nameof(BranchedDefinitions))]
    public void BranchedStrategy_RoundTripsLosslessly(StrategyDefinition def) => Lossless(def);

    [Test]
    public void UnreachableElseIfAfterElse_StillRoundTrips_ButIsDiagnosed()
    {
        var def = WithBlock(Base(),
            Branch(Conditions.IsAboveVwap, new StrategyOverrides { StopLossPercent = 1 }),
            Branch(null, new StrategyOverrides { StopLossPercent = 2 }),
            Branch(Conditions.IsBelowVwap, new StrategyOverrides { StopLossPercent = 3 }));
        var text = Lossless(def, expectClean: false);
        Assert.That(ScriptDiagnostics.Analyze(text).Select(d => d.Code), Does.Contain("IPS012"));
    }

    [Test]
    public void BuilderFormText_WithAndWithoutLambdas_ParsesLikeTheBuilder()
    {
        var expected = StrategyJson.Serialize(Stock.Ticker("X")
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(5).StopLoss(3))
            .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(2))
            .Else(b => b.Long())
            .StopLoss(1)
            .Build());

        var plain = "Ticker(\"X\")\n.IsAboveVwap()\n.Then(Long().TakeProfit(5).StopLoss(3))\n.ElseIf(IsBelowVwap(), Short().TakeProfit(2))\n.Else(Long())\n.StopLoss(1)";
        var lambdas = "Stock.Ticker(\"X\")\n    .IsAboveVwap()\n    .Then(b => b.Long().TakeProfit(5).StopLoss(3))\n    .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(2))\n    .Else(b => b.Long())\n    .StopLoss(1)\n    .Build();";

        Assert.Multiple(() =>
        {
            Assert.That(StrategyJson.Serialize(ScriptParser.ParseScript(plain)!), Is.EqualTo(expected));
            Assert.That(StrategyJson.Serialize(ScriptParser.ParseScript(lambdas)!), Is.EqualTo(expected));
            Assert.That(ScriptDiagnostics.Analyze(lambdas), Is.Empty);
        });
    }

    [Test]
    public void BranchBodies_NoLongerLeakIntoTheBaseStrategy()
    {
        var def = ScriptParser.ParseScript("Ticker(\"X\").IsAboveVwap().Then(b => b.Short().TakeProfit(5)).StopLoss(1)")!;
        Assert.Multiple(() =>
        {
            Assert.That(def.Direction, Is.EqualTo(TradeDirection.Long), "the branch's Short() stays in the branch");
            Assert.That(def.TakeProfitPrice, Is.Null);
            Assert.That(def.StopLossPrice, Is.EqualTo(1));
            Assert.That(def.EntryConditions, Is.Empty, "the condition before Then() became the branch's if");
            Assert.That(def.ConditionalBlocks.Single().Branches.Single().Overrides.Direction, Is.EqualTo(TradeDirection.Short));
        });
    }

    [Test]
    public void BuilderFormBlock_EndsAtTheNextOrdinaryVerb()
    {
        var def = ScriptParser.ParseScript("Ticker(\"X\").IsAboveVwap().Then(StopLoss(1)).ElseIf(IsBelowVwap(), StopLoss(2)).StopLoss(3).IsMacdBullish()")!;
        Assert.Multiple(() =>
        {
            Assert.That(def.ConditionalBlocks.Single().Branches, Has.Count.EqualTo(2));
            Assert.That(def.StopLossPrice, Is.EqualTo(3));
            Assert.That(def.EntryConditions.Single().ToScript(), Is.EqualTo("IsMacdBullish()"));
        });
    }

    [Test]
    public void UnreadableIfCondition_SkipsItsBranch_NeverBecomesUnconditional()
    {
        var text = "Ticker(\"X\")\n.If(IsAboveVwapp())\n.Then(StopLoss(1))\n.EndIf()\n.Long().StopLoss(2)";
        var def = ScriptParser.ParseScript(text)!;
        Assert.Multiple(() =>
        {
            Assert.That(def.ConditionalBlocks.Single().Branches, Is.Empty, "a typo'd condition must not make its branch always apply");
            var d = ScriptDiagnostics.Analyze(text).Single(x => x.Code == "IPS001");
            Assert.That((d.Line, d.Column, d.Suggestion), Is.EqualTo((2, 5, "IsAboveVwap")));
        });
    }

    [Test]
    public void TopLevelAndOrNot_ComposeWithThePreviousCondition()
    {
        var def = ScriptParser.ParseScript("Ticker(\"X\").IsAboveVwap().And(IsAboveEma(9)).IsGapUp(5).Not().Long().StopLoss(1)")!;
        Assert.That(def.EntryConditions.Select(c => c.ToScript()),
            Is.EqualTo(new[] { "IsAboveVwap().And(IsAboveEma(9))", "IsGapUp(5).Not()" }));
    }

    [Test]
    public void ConditionsCatalogPropertyForm_ParsesInsideIf()
    {
        var def = ScriptParser.ParseScript("Ticker(\"X\").If(Conditions.IsAboveVwap.And(Conditions.IsAboveEma(9))).Then(Short()).EndIf().StopLoss(1)")!;
        Assert.That(def.ConditionalBlocks.Single().Branches.Single().Condition!.ToScript(), Is.EqualTo("IsAboveVwap().And(IsAboveEma(9))"));
    }

    private static ScriptDiagnostic Only(string text, string code) =>
        ScriptDiagnostics.Analyze(text).Single(d => d.Code == code);

    [Test]
    public void ElseBeforeIf_IsReportedAtItsLineAndColumn()
    {
        var d = Only("Ticker(\"X\")\n.IsAboveVwap()\n  .Else(StopLoss(2))\n.If(IsBelowVwap()).Then(StopLoss(1)).EndIf()", "IPS011");
        Assert.That((d.Line, d.Column, d.IsError), Is.EqualTo((3, 4, true)));
        Assert.That(d.Excerpt, Does.Contain("^^^^"));
    }

    [Test]
    public void MissingEndIf_PointsAtTheIf()
    {
        var d = Only("Ticker(\"X\")\n.If(IsAboveVwap())\n.Then(StopLoss(1))\n.Long()", "IPS013");
        Assert.That((d.Line, d.Column), Is.EqualTo((2, 2)));
    }

    [Test]
    public void ElseIfAfterElse_IsReported()
    {
        var d = Only("Ticker(\"X\")\n.If(IsAboveVwap())\n.Then(StopLoss(1))\n.Else(StopLoss(2))\n.ElseIf(IsBelowVwap(), StopLoss(3))\n.EndIf()", "IPS012");
        Assert.That((d.Line, d.Column), Is.EqualTo((5, 2)));
    }

    [Test]
    public void IfWithoutThen_IsReported()
    {
        var d = Only("Ticker(\"X\")\n.If(IsAboveVwap())\n.Long()\n.EndIf()", "IPS014");
        Assert.That((d.Line, d.Column), Is.EqualTo((2, 2)));
    }

    [Test]
    public void ElseIfWithoutABody_IsReported()
    {
        var d = Only("Ticker(\"X\")\n.If(IsAboveVwap()).Then(StopLoss(1))\n.ElseIf(IsBelowVwap())\n.EndIf()", "IPS016");
        Assert.That(d.Line, Is.EqualTo(3));
    }

    [Test]
    public void MistypedEndIf_GetsADidYouMean()
    {
        var d = ScriptDiagnostics.Analyze("Ticker(\"X\").If(IsAboveVwap()).Then(StopLoss(1)).EndIff()").First(x => x.Code == "IPS001");
        Assert.That(d.Suggestion, Is.EqualTo("EndIf"));
    }

    [Test]
    public void GeneratorPromptBranchExample_ParsesCleanToTheIntendedBlock()
    {
        var text = "Stock.Ticker(\"ABC\")\n    .BreaksAbove(10.50)\n    .Long()\n    .StopLossPercent(2)\n    .If(IsAboveVwap())\n" +
                   "        .Then(StopLossPercent(1).TakeProfit(12))\n        .ElseIf(IsBelowVwap().And(IsRsiOversold(30)), StopLossPercent(2))\n" +
                   "        .Else(TrailingStopLoss(1))\n        .EndIf()\n    .Build();";
        Assert.That(ScriptDiagnostics.Analyze(text), Is.Empty);
        var def = ScriptParser.ParseScript(text)!;
        var branches = def.ConditionalBlocks.Single().Branches;
        Assert.Multiple(() =>
        {
            Assert.That(branches.Select(b => b.Condition?.ToScript()),
                Is.EqualTo(new[] { "IsAboveVwap()", "IsBelowVwap().And(IsRsiOversold(30))", null }));
            Assert.That(branches[0].Overrides.TakeProfitPrice, Is.EqualTo(12));
            Assert.That(branches[2].Overrides.TrailingStopPercent, Is.EqualTo(1));
            Assert.That(def.EntryConditions.Single().ToScript(), Is.EqualTo("BreaksAbove(10.5)"));
            Assert.That(def.StopLossPercent, Is.EqualTo(2));
        });
    }

    [Test]
    public void WellFormedExplicitBlock_HasNoDiagnostics()
    {
        var text = "Ticker(\"X\")\n.IsPriceBetween(1, 50)\n.If(IsAboveVwap().And(NotFallingKnife(0.4, 30)))\n    .Then(Long().TakeProfit(5, 6).StopLoss(3))\n    .ElseIf(IsBelowVwap(), Short().AddTarget(2, 100, \"T1\"))\n    .Else(TrailingStopLoss(1))\n    .EndIf()\n.While().IsAboveVwap().EndWhile()\n.Long().StopLoss(1)";
        Assert.That(ScriptDiagnostics.Analyze(text), Is.Empty);
    }
}
