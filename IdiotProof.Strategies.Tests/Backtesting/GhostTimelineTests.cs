using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using static IdiotProof.Strategies.Tests.Backtesting.BacktestTestData;

namespace IdiotProof.Strategies.Tests.Backtesting;

/// <summary>The ghost replay timeline (IP-US-G2..G4, IP-T388).</summary>
public class GhostTimelineTests
{
    // If price >= 20: stop 5 / target 30, else: stop 9 / target 11.
    private static StrategyDefinition Branched() =>
        Stock.Ticker("TST")
            .IsPriceBetween(1, 100)
            .IsPriceBetween(20, 100)
            .Then(b => b.StopLoss(5).TakeProfit(30))
            .Else(b => b.StopLoss(9).TakeProfit(11))
            .Build();

    private static List<Candle> FlipDay() =>
    [
        Bar(0, 25m, 25.5m, 24.8m, 25m),     // If (>=20): enter @25
        Bar(1, 25m, 25.1m, 24.9m, 25m),     // If, holding
        Bar(2, 25m, 25.1m, 10.5m, 10.6m),   // drops under 20: Else now active (trade keeps If levels)
        Bar(3, 10.6m, 11m, 10.5m, 10.8m),   // Else
        Bar(4, 10.8m, 22m, 10.7m, 21m),     // back above 20: If
    ];

    [Test]
    public void Forks_AreRecorded_AtTheBarsWhereTheTakenBranchChanges()
    {
        var ghost = GhostTimeline.Run(Branched(), FlipDay());

        Assert.That(ghost.Frames, Has.Count.EqualTo(5));
        Assert.That(ghost.Forks.Select(f => f.Index), Is.EqualTo(new[] { 0, 2, 4 }));
        Assert.Multiple(() =>
        {
            Assert.That(ghost.Forks[0].FromBranch, Is.EqualTo(-1), "the first resolution has no previous branch");
            Assert.That(ghost.Forks[0].ToBranch, Is.EqualTo(0));
            Assert.That(ghost.Forks[1].FromBranch, Is.EqualTo(0));
            Assert.That(ghost.Forks[1].ToBranch, Is.EqualTo(1));
            Assert.That(ghost.Forks[2].ToBranch, Is.EqualTo(0));
            Assert.That(ghost.Frames.Select(f => f.BranchTaken[0]), Is.EqualTo(new[] { 0, 0, 1, 1, 0 }));
        });
    }

    [Test]
    public void Fork_ShowsEveryBranch_PassFail_Taken_LevelsAndIndicators()
    {
        var fork = GhostTimeline.Run(Branched(), FlipDay()).Forks[1]; // bar 2, close 10.6
        var ifBranch = fork.Branches[0];
        var elseBranch = fork.Branches[1];
        Assert.Multiple(() =>
        {
            Assert.That(ifBranch.Label, Is.EqualTo("If"));
            Assert.That(ifBranch.Condition, Does.Contain("20"));
            Assert.That(ifBranch.Passed, Is.False);
            Assert.That(ifBranch.Taken, Is.False);
            Assert.That(ifBranch.Stop, Is.EqualTo(5m));
            Assert.That(ifBranch.Target, Is.EqualTo(30m));
            Assert.That(elseBranch.Label, Is.EqualTo("Else"));
            Assert.That(elseBranch.Condition, Is.Null);
            Assert.That(elseBranch.Passed, Is.True);
            Assert.That(elseBranch.Taken, Is.True);
            Assert.That(elseBranch.Stop, Is.EqualTo(9m));
            Assert.That(fork.Indicators["Price"], Is.EqualTo(10.6).Within(1e-9));
        });
    }

    [Test]
    public void Frames_RecordConditions_PositionStopAndEvents()
    {
        var ghost = GhostTimeline.Run(Branched(), FlipDay());
        var first = ghost.Frames[0];
        Assert.Multiple(() =>
        {
            Assert.That(first.Conditions, Is.Not.Empty);
            Assert.That(first.Conditions.All(c => c.Passed), Is.True);
            Assert.That(first.AllSatisfied, Is.True);
            Assert.That(first.PositionShares, Is.GreaterThan(0), "entered on bar 0");
            Assert.That(first.ActiveStop, Is.EqualTo(5m), "the If branch's stop");
            Assert.That(first.Events.Any(e => e.Kind == "entry"), Is.True);
            Assert.That(ghost.Frames[2].ActiveStop, Is.EqualTo(5m), "an open trade keeps its entry branch's stop after the flip");
            Assert.That(ghost.Frames[^1].Events.Any(e => e.Kind == "exit"), Is.True, "closed at end of session");
            Assert.That(ghost.Frames[^1].PositionShares, Is.EqualTo(0));
        });
    }

    [Test]
    public void Frames_RecordFailingConditions_WhenTheSetupIsNotMet()
    {
        var def = Stock.Ticker("TST").IsPriceBetween(50, 100).StopLoss(1).TakeProfit(200).Build();
        var ghost = GhostTimeline.Run(def, FlipDay());
        Assert.That(ghost.Frames.All(f => !f.AllSatisfied && f.PositionShares == 0), Is.True);
        Assert.That(ghost.Frames[0].Conditions.Single().Passed, Is.False);
        Assert.That(ghost.Forks, Is.Empty, "no If() blocks, no forks");
    }

    [Test]
    public void BranchTree_CountsBarsAndTrades_AndRoundTripsThroughJson()
    {
        var tree = GhostTimeline.Run(Branched(), FlipDay()).Tree;
        Assert.That(tree.Blocks, Has.Count.EqualTo(1));
        Assert.That(tree.Blocks[0].Branches[0].BarsTaken, Is.EqualTo(3));
        Assert.That(tree.Blocks[0].Branches[1].BarsTaken, Is.EqualTo(2));
        Assert.That(tree.Blocks[0].Branches[0].TradesEntered, Is.EqualTo(1));

        var back = BranchTree.FromJson(tree.ToJson());
        Assert.That(back.ToJson(), Is.EqualTo(tree.ToJson()));
        Assert.That(back.Blocks[0].Branches[1].Overrides, Is.EqualTo(tree.Blocks[0].Branches[1].Overrides));
        Assert.That(back.Date, Is.EqualTo(tree.Date));
    }

    [Test]
    public void BranchTree_ExportsAMermaidDiagram_WithUntakenBranchesDashed()
    {
        var candles = new List<Candle> { Bar(0, 25m, 25.5m, 24.8m, 25m), Bar(1, 25m, 25.5m, 24.8m, 25m) };
        var mermaid = GhostTimeline.Run(Branched(), candles).Tree.ToMermaid();
        Assert.That(mermaid, Does.StartWith("flowchart TD"));
        Assert.That(mermaid, Does.Contain("b0 -->|"), "the taken If branch is solid");
        Assert.That(mermaid, Does.Contain("b0 -.->|\"otherwise\"|"), "the untaken Else branch is dashed");
    }

    [Test]
    public void Comparison_SummarizesRunsSideBySide()
    {
        var a = GhostTimeline.Run(Branched(), FlipDay());
        var b = GhostTimeline.Run(Branched(), [Bar(0, 10m, 10.2m, 9.8m, 10m), Bar(1, 10m, 11.5m, 9.9m, 11.2m)]);
        var rows = GhostComparison.Summarize([a, b]);
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].BranchesUsed, Is.EquivalentTo(new[] { "#1 If", "#1 Else" }));
        Assert.That(rows[1].BranchesUsed, Is.EquivalentTo(new[] { "#1 Else" }));
        Assert.That(rows[1].Trades, Is.EqualTo(1));
        Assert.That(rows[1].PnL, Is.GreaterThan(0m));
    }
}
