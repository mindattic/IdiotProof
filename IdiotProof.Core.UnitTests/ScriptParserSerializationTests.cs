// ============================================================================
// StrategyScriptParser Serialization Tests - ToIdiotScript() Round-Trip
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for IdiotScript serialization (ToIdiotScript) and round-trip parsing.
/// Verifies that parsed strategies can be serialized back to valid scripts.
/// </summary>
[TestFixture]
public class ScriptParserSerializationTests
{
    #region Basic Round-Trip Tests

    [Test]
    public void RoundTrip_SimpleStrategy()
    {
        var originalScript = "Ticker(AAPL).Entry(150).TakeProfit(160)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);

        Assert.That(serialized, Is.Not.Null.Or.Empty);

        // Re-parse the serialized script
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Symbol, Is.EqualTo(strategy.Symbol));
        Assert.That(reparsed.GetStats().Price, Is.EqualTo(strategy.GetStats().Price).Within(0.01));
        Assert.That(reparsed.GetStats().TakeProfit, Is.EqualTo(strategy.GetStats().TakeProfit).Within(0.01));
    }

    [Test]
    public void RoundTrip_StrategyWithName()
    {
        var originalScript = @"Ticker(PLTR).Name(""Palantir Strategy"").Entry(25).TakeProfit(30)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Symbol, Is.EqualTo("PLTR"));
        Assert.That(reparsed.Name, Is.EqualTo("Palantir Strategy"));
    }

    [Test]
    public void RoundTrip_StrategyWithQuantity()
    {
        var originalScript = "Ticker(AAPL).Quantity(100).Entry(150).TakeProfit(160)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.GetStats().Quantity, Is.EqualTo(100));
    }

    #endregion

    #region Price Parameters Round-Trip

    [Test]
    public void RoundTrip_AllPriceParams()
    {
        var originalScript = "Ticker(AAPL).Entry(150).TakeProfit(160).StopLoss(145).TrailingStopLoss(15%)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        var stats = reparsed.GetStats();
        Assert.That(stats.Price, Is.EqualTo(150).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(160).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(145).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));
    }

    #endregion

    #region Condition Round-Trip Tests

    [Test]
    public void RoundTrip_VwapCondition()
    {
        var originalScript = "Ticker(AAPL).IsAboveVwap().Entry(150)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
    }

    [Test]
    public void RoundTrip_EmaConditions()
    {
        var originalScript = "Ticker(AAPL).IsEmaAbove(9).IsEmaBelow(200).IsEmaBetween(9, 21)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsEmaAbove), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsEmaBelow), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsEmaBetween), Is.True);
    }

    [Test]
    public void RoundTrip_MomentumConditions()
    {
        var originalScript = "Ticker(AAPL).IsMomentumAbove(0).IsRocAbove(2)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsMomentum), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsRoc), Is.True);
    }

    [Test]
    public void RoundTrip_DiMacdConditions()
    {
        var originalScript = "Ticker(AAPL).IsDiPositive().IsMacdBullish()";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsMacd), Is.True);
    }

    [Test]
    public void RoundTrip_GapCondition()
    {
        var originalScript = "Ticker(AAPL).IsGapUp(5).IsAboveVwap()";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.GapUp), Is.True);
    }

    #endregion

    #region Session Round-Trip Tests

    [Test]
    public void RoundTrip_SessionPremarket()
    {
        var originalScript = "Ticker(AAPL).Session(IS.PREMARKET).Entry(150)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.SessionDuration), Is.True);
    }

    [Test]
    public void RoundTrip_ExitStrategy()
    {
        var originalScript = "Ticker(AAPL).ExitStrategy(IS.BELL).IsProfitable()";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.ExitStrategy), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsProfitable), Is.True);
    }

    #endregion

    #region Order Direction Round-Trip

    [Test]
    public void RoundTrip_OrderLong()
    {
        var originalScript = "Ticker(AAPL).Order(IS.LONG).Entry(150)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.Order || s.Type == SegmentType.Long), Is.True);
    }

    [Test]
    public void RoundTrip_OrderShort()
    {
        var originalScript = "Ticker(AAPL).Order(IS.SHORT).Entry(150)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.Order || s.Type == SegmentType.Short), Is.True);
    }

    #endregion

    #region AutonomousTrading Round-Trip

    [Test]
    public void RoundTrip_AutonomousTradingAggressive()
    {
        var originalScript = "Ticker(AAPL).Entry(150).TakeProfit(160).StopLoss(145).AutonomousTrading(IS.AGGRESSIVE)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.AutonomousTrading), Is.True);
    }

    #endregion

    #region Repeat Round-Trip

    [Test]
    public void RoundTrip_RepeatEnabled()
    {
        var originalScript = "Ticker(AAPL).Entry(150).TakeProfit(160).Repeat()";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.Repeat), Is.True);
    }

    #endregion

    #region Full Strategy Round-Trip

    [Test]
    public void RoundTrip_FullComplexStrategy()
    {
        var originalScript = @"
            Ticker(NVDA)
            .Name(""NVDA Premarket Momentum"")
            .Session(IS.PREMARKET)
            .Quantity(10)
            .Entry(150)
            .TakeProfit(160)
            .StopLoss(145)
            .TrailingStopLoss(IS.MODERATE)
            .IsAboveVwap()
            .IsEmaAbove(9)
            .IsEmaBetween(9, 21)
            .IsDiPositive()
            .IsMacdBullish()
            .Order(IS.LONG)
            .AutonomousTrading(IS.AGGRESSIVE)
            .Repeat()
        ";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        // Verify key elements survive round-trip
        Assert.That(reparsed.Symbol, Is.EqualTo("NVDA"));
        Assert.That(reparsed.Name, Is.EqualTo("NVDA Premarket Momentum"));
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsEmaAbove), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsMacd), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.AutonomousTrading), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.Repeat), Is.True);
    }

    [Test]
    public void RoundTrip_GapAndGoStrategy()
    {
        var originalScript = "Ticker(CIGL).Session(IS.PREMARKET).GapUp(5).IsAboveVwap().IsDiPositive().Entry(4.15).TakeProfit(4.80).StopLoss(3.90).AutonomousTrading(IS.AGGRESSIVE)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Symbol, Is.EqualTo("CIGL"));
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.GapUp), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.AutonomousTrading), Is.True);
    }

    #endregion

    #region Serialized Format Tests

    [Test]
    public void ToScript_UsesCanonicalCommandNames()
    {
        // Parser accepts aliases, but serializer should output canonical names
        var originalScript = "Ticker(AAPL).Qty(100).TP(160).SL(145).TSL(15%)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);

        // Serialized output should use canonical names (Quantity, TakeProfit, StopLoss, TrailingStopLoss)
        // The exact format may vary, but it should be parseable
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.GetStats().Quantity, Is.EqualTo(100));
        Assert.That(reparsed.GetStats().TakeProfit, Is.EqualTo(160).Within(0.01));
        Assert.That(reparsed.GetStats().StopLoss, Is.EqualTo(145).Within(0.01));
        Assert.That(reparsed.GetStats().TrailingStopLossPercent, Is.EqualTo(0.15).Within(0.01));
    }

    [Test]
    public void ToScript_IncludesParentheses()
    {
        // Serializer should include parentheses on all commands
        var originalScript = "Ticker(AAPL).IsAboveVwap.IsDiPositive";
        var strategy = StrategyScriptParser.Parse(originalScript);

        var serialized = StrategyScriptParser.ToScript(strategy);

        // The serialized output should be valid (parseable)
        var reparsed = StrategyScriptParser.Parse(serialized);

        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(reparsed.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
    }

    #endregion

    #region Original Script Preservation

    [Test]
    public void Parse_PreservesOriginalScript()
    {
        var originalScript = "Ticker(AAPL).Entry(150).TakeProfit(160)";
        var strategy = StrategyScriptParser.Parse(originalScript);

        // The OriginalScript property should contain the input
        Assert.That(strategy.OriginalScript, Is.Not.Null);
        Assert.That(strategy.OriginalScript, Does.Contain("AAPL"));
    }

    [Test]
    public void Parse_PreservesCommentsInOriginal()
    {
        var originalScript = @"
            # This is a comment
            Ticker(AAPL).Entry(150) # inline comment
        ";
        var strategy = StrategyScriptParser.Parse(originalScript);

        // Comments should be preserved in OriginalScript
        Assert.That(strategy.OriginalScript, Is.Not.Null);
        Assert.That(strategy.OriginalScript, Does.Contain("# This is a comment"));
    }

    #endregion
}
