// ============================================================================
// IdiotScriptSerializerTests - Tests for IdiotScript serialization
// ============================================================================
//
// Tests the IdiotScriptSerializer which converts StrategyDefinition to IdiotScript text.
// Validates round-trip conversion capability.
//
// ============================================================================

using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Scripting;

namespace IdiotProof.Shared.UnitTests.Scripting;

/// <summary>
/// Tests for IdiotScriptSerializer - serializing StrategyDefinition to IdiotScript.
/// </summary>
[TestFixture]
public class IdiotScriptSerializerTests
{
    #region Basic Serialization

    [Test]
    public void Serialize_NullStrategy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => IdiotScriptSerializer.Serialize(null!));
    }

    [Test]
    public void Serialize_MinimalStrategy_IncludesTicker()
    {
        var strategy = CreateMinimalStrategy("AAPL");

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.StartWith("Ticker(AAPL)"));
    }

    [Test]
    public void Serialize_StrategyWithName_IncludesName()
    {
        var strategy = CreateMinimalStrategy("AAPL");
        strategy.Name = "My Trading Strategy";

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.Contain("Name(\"My Trading Strategy\")"));
    }

    [Test]
    public void Serialize_StrategyWithDescription_IncludesDescription()
    {
        var strategy = CreateMinimalStrategy("AAPL");
        strategy.Description = "A test strategy";

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.Contain("Desc(\"A test strategy\")"));
    }

    [Test]
    public void Serialize_DisabledStrategy_IncludesEnabled()
    {
        var strategy = CreateMinimalStrategy("AAPL");
        strategy.Enabled = false;

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.Contain("Enabled(false)"));
    }

    [Test]
    public void Serialize_EnabledStrategy_DoesNotIncludeEnabled()
    {
        var strategy = CreateMinimalStrategy("AAPL");
        strategy.Enabled = true;

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.Not.Contain("Enabled"));
    }

    #endregion

    #region Round-trip Tests

    [Test]
    public void RoundTrip_MinimalScript_PreservesSymbol()
    {
        var original = "Ticker(AAPL)";
        var strategy = IdiotScriptParser.Parse(original);
        var serialized = IdiotScriptSerializer.Serialize(strategy);
        var reparsed = IdiotScriptParser.Parse(serialized);

        Assert.That(reparsed.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void RoundTrip_WithQuantity_PreservesQuantity()
    {
        var original = "Ticker(NVDA).Qty(10)";
        var strategy = IdiotScriptParser.Parse(original);
        var serialized = IdiotScriptSerializer.Serialize(strategy);
        var reparsed = IdiotScriptParser.Parse(serialized);

        var originalStats = strategy.GetStats();
        var reparsedStats = reparsed.GetStats();
        Assert.That(reparsedStats.Quantity, Is.EqualTo(originalStats.Quantity));
    }

    [Test]
    public void RoundTrip_WithTakeProfit_PreservesPrice()
    {
        var original = "Ticker(AAPL).TP(160)";
        var strategy = IdiotScriptParser.Parse(original);
        var serialized = IdiotScriptSerializer.Serialize(strategy);
        var reparsed = IdiotScriptParser.Parse(serialized);

        var originalStats = strategy.GetStats();
        var reparsedStats = reparsed.GetStats();
        Assert.That(reparsedStats.TakeProfit, Is.EqualTo(originalStats.TakeProfit).Within(0.01));
    }

    [Test]
    public void RoundTrip_WithStopLoss_PreservesPrice()
    {
        var original = "Ticker(AAPL).SL(145)";
        var strategy = IdiotScriptParser.Parse(original);
        var serialized = IdiotScriptSerializer.Serialize(strategy);
        var reparsed = IdiotScriptParser.Parse(serialized);

        var originalStats = strategy.GetStats();
        var reparsedStats = reparsed.GetStats();
        Assert.That(reparsedStats.StopLoss, Is.EqualTo(originalStats.StopLoss).Within(0.01));
    }

    [Test]
    public void RoundTrip_WithTrailingStopLoss_PreservesPercentage()
    {
        var original = "Ticker(AAPL).TSL(IS.MODERATE)";
        var strategy = IdiotScriptParser.Parse(original);
        var serialized = IdiotScriptSerializer.Serialize(strategy);
        var reparsed = IdiotScriptParser.Parse(serialized);

        var originalStats = strategy.GetStats();
        var reparsedStats = reparsed.GetStats();
        Assert.That(reparsedStats.TrailingStopLossPercent, Is.EqualTo(originalStats.TrailingStopLossPercent).Within(0.01));
    }

    [Test]
    public void RoundTrip_FullStrategy_PreservesAllComponents()
    {
        var original = "Ticker(NVDA).Qty(1).Entry(200).TP(210).SL(190).TSL(IS.MODERATE).Breakout().Pullback().AboveVwap";
        var strategy = IdiotScriptParser.Parse(original);
        var serialized = IdiotScriptSerializer.Serialize(strategy);
        var reparsed = IdiotScriptParser.Parse(serialized);

        // Verify symbol
        Assert.That(reparsed.Symbol, Is.EqualTo("NVDA"));

        // Verify stats
        var originalStats = strategy.GetStats();
        var reparsedStats = reparsed.GetStats();
        Assert.That(reparsedStats.Quantity, Is.EqualTo(originalStats.Quantity));
        Assert.That(reparsedStats.Price, Is.EqualTo(originalStats.Price).Within(0.01));
        Assert.That(reparsedStats.TakeProfit, Is.EqualTo(originalStats.TakeProfit).Within(0.01));
        Assert.That(reparsedStats.StopLoss, Is.EqualTo(originalStats.StopLoss).Within(0.01));
        Assert.That(reparsedStats.TrailingStopLossPercent, Is.EqualTo(originalStats.TrailingStopLossPercent).Within(0.01));

        // Verify segments
        Assert.That(reparsed.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
        Assert.That(reparsed.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Pullback));
        Assert.That(reparsed.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
    }

    #endregion

    #region Output Format

    [Test]
    public void Serialize_UsesPascalCase()
    {
        var strategy = CreateMinimalStrategy("AAPL");
        strategy.Segments.Add(SegmentFactory.CreateBreakout());
        strategy.Segments.Add(SegmentFactory.CreatePullback());

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.Contain("Ticker"));
        Assert.That(result, Does.Contain("Breakout"));
        Assert.That(result, Does.Contain("Pullback"));
    }

    [Test]
    public void Serialize_UsesPeriodDelimiter()
    {
        var strategy = CreateMinimalStrategy("AAPL");
        strategy.Segments.Add(SegmentFactory.CreateBreakout());

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.Contain("."));
        Assert.That(result, Does.Not.Contain("  ")); // No double spaces
    }

    #endregion

    #region Repeat Serialization

    [Test]
    public void Serialize_RepeatEnabled_IncludesRepeat()
    {
        var strategy = CreateMinimalStrategy("ABC");
        strategy.RepeatEnabled = true;

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.Contain("Repeat()"));
    }

    [Test]
    public void Serialize_RepeatDisabled_DoesNotIncludeRepeat()
    {
        var strategy = CreateMinimalStrategy("ABC");
        strategy.RepeatEnabled = false;

        var result = IdiotScriptSerializer.Serialize(strategy);

        Assert.That(result, Does.Not.Contain("Repeat"));
    }

    [Test]
    public void RoundTrip_WithRepeat_PreservesRepeatEnabled()
    {
        var original = "Ticker(ABC).ENTRY(5.00).TP(6.00).ABOVEVWAP().Repeat()";
        var strategy = IdiotScriptParser.Parse(original);
        var serialized = IdiotScriptSerializer.Serialize(strategy);
        var reparsed = IdiotScriptParser.Parse(serialized);

        Assert.That(reparsed.RepeatEnabled, Is.True);
        Assert.That(reparsed.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Repeat));
    }

    [Test]
    public void RoundTrip_WithoutRepeat_RepeatEnabledIsFalse()
    {
        var original = "Ticker(ABC).ENTRY(5.00).TP(6.00).ABOVEVWAP";
        var strategy = IdiotScriptParser.Parse(original);
        var serialized = IdiotScriptSerializer.Serialize(strategy);
        var reparsed = IdiotScriptParser.Parse(serialized);

        Assert.That(reparsed.RepeatEnabled, Is.False);
    }

    [Test]
    public void Serialize_RepeatAtEnd_FollowsOtherCommands()
    {
        var strategy = CreateMinimalStrategy("ABC");
        strategy.RepeatEnabled = true;
        strategy.Segments.Add(SegmentFactory.CreateTakeProfit());

        var result = IdiotScriptSerializer.Serialize(strategy);

        // Repeat should come after TakeProfit
        var tpIndex = result.IndexOf("TakeProfit");
        var repeatIndex = result.IndexOf("Repeat");
        Assert.That(repeatIndex, Is.GreaterThan(tpIndex), "Repeat should appear after TakeProfit");
    }

    #endregion

    #region Helper Methods

    private static StrategyDefinition CreateMinimalStrategy(string symbol)
    {
        return new StrategyDefinition
        {
            Symbol = symbol,
            Name = $"{symbol} Strategy",
            Enabled = true,
            Segments = []
        };
    }

    #endregion
}


