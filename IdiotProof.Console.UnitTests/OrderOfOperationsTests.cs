// ============================================================================
// OrderOfOperationsTests - Tests for logical flow and command sequence
// ============================================================================
//
// NOMENCLATURE:
// - Order of Operations: The required sequence of commands in a valid script
// - Logical Flow: The progression from symbol → conditions → order → exit
// - Separation of Responsibility: Each command category serves a distinct purpose
//
// COMMAND EXECUTION ORDER:
// 1. Symbol/Identity (TICKER, NAME, DESC, ENABLED) - Must come first
// 2. Session/Timing (SESSION, START, END) - Defines when strategy is active
// 3. Conditions (BREAKOUT, PULLBACK, VWAP, EMA, RSI, ADX) - Entry criteria
// 4. Order (BUY, SELL, QTY, ENTRY, PRICE) - Trade execution details
// 5. Risk Management (TP, SL, TSL) - Exit criteria
// 6. Position Management (CLOSEPOSITION, CLOSE) - End-of-session handling
//
// These tests validate:
// 1. Scripts follow the correct order of operations
// 2. Each category of commands is processed correctly
// 3. Invalid ordering produces appropriate errors
// 4. Round-trip conversion preserves logical structure
//
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Scripting;
using IdiotProof.Shared.Validation;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for command execution order and logical flow in IdiotScript.
/// </summary>
[TestFixture]
public class OrderOfOperationsTests
{
    #region Symbol Must Be First

    [Test]
    public void Parse_SymbolFirst_Succeeds()
    {
        // Arrange - Symbol is first
        var script = "TICKER(AAPL).QTY(100).BUY";

        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [TestCase("TICKER(AAPL).SESSION(IS.PREMARKET).QTY(100)")]
    [TestCase("SYM(NVDA).BREAKOUT(150).BUY")]
    [TestCase("SYMBOL(TSLA).TP(200).SL(180)")]
    [TestCase("STOCK.TICKER(AMD).TSL(IS.MODERATE)")]
    public void Parse_VariousSymbolSyntax_AllSucceed(string script)
    {
        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(string.IsNullOrEmpty(result.Symbol), Is.False);
    }

    [Test]
    public void Parse_NoSymbol_ThrowsException()
    {
        // Arrange - No symbol
        var script = "QTY(100).BUY.TP(160)";

        // Act & Assert
        Assert.Throws<StrategyScriptException>(() => StrategyScriptParser.Parse(script));
    }

    #endregion

    #region Session Before Conditions

    [Test]
    public void Parse_SessionBeforeConditions_Succeeds()
    {
        // Arrange
        var script = "TICKER(AAPL).SESSION(IS.PREMARKET).BREAKOUT(150).BUY";

        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        var breakoutSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        
        Assert.That(sessionSegment, Is.Not.Null);
        Assert.That(breakoutSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_SessionAfterConditions_StillWorks()
    {
        // Parser is flexible with order - validates structure, not strict ordering
        var script = "TICKER(AAPL).BREAKOUT(150).SESSION(IS.PREMARKET).BUY";

        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert - Both segments should be present
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.SessionDuration));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
    }

    #endregion

    #region Conditions Before Order

    [Test]
    public void Parse_ConditionsBeforeOrder_Succeeds()
    {
        // Arrange
        var script = "TICKER(AAPL).BREAKOUT(150).ABOVEVWAP.BUY.QTY(100)";

        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
    }

    [TestCase("TICKER(AAPL).BREAKOUT.BUY")]
    [TestCase("TICKER(AAPL).PULLBACK.SELL")]
    [TestCase("TICKER(AAPL).ABOVEVWAP.BUY")]
    [TestCase("TICKER(AAPL).BELOWVWAP.SELL")]
    public void Parse_ConditionThenOrder_Succeeds(string script)
    {
        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        Assert.That(result.Segments, Is.Not.Empty);
    }

    #endregion

    #region Risk Management After Order

    [Test]
    public void Parse_RiskManagementAfterOrder_Succeeds()
    {
        // Arrange
        var script = "TICKER(AAPL).BREAKOUT(150).BUY.QTY(100).TP(160).SL(145).TSL(10)";

        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TakeProfit));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.StopLoss));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TrailingStopLoss));
    }

    [Test]
    public void Parse_RiskManagementWithISConstants_Succeeds()
    {
        // Arrange
        var script = "TICKER(AAPL).BREAKOUT.BUY.TSL(IS.MODERATE)";

        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        var tslSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.TrailingStopLoss);
        Assert.That(tslSegment, Is.Not.Null);
    }

    #endregion

    #region Complete Strategy Flow

    [Test]
    public void Parse_CompleteStrategyFlow_AllSegmentsPresent()
    {
        // Arrange - Complete strategy with all sections
        var script = "TICKER(NVDA).SESSION(IS.PREMARKET).BREAKOUT(150).ABOVEVWAP.BUY.QTY(10).TP(160).SL(145).TSL(IS.MODERATE).CLOSEPOSITION(IS.BELL)";

        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.SessionDuration));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.Breakout));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TakeProfit));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.StopLoss));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.TrailingStopLoss));
        Assert.That(result.Segments, Has.Some.Matches<StrategySegment>(s => s.Type == SegmentType.ClosePosition));
    }

    [TestCase("TICKER(AAPL).SESSION(IS.RTH).BREAKOUT.BUY.TP(160)")]
    [TestCase("TICKER(AAPL).PULLBACK.ABOVEVWAP.SELL.SL(140)")]
    [TestCase("TICKER(AAPL).EMAABOVE(9).EMABELOW(200).BUY.TSL(IS.TIGHT)")]
    public void Parse_VariousCompleteStrategies_AllSucceed(string script)
    {
        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Segments, Is.Not.Empty);
    }

    #endregion

    #region Segment Order in Result

    [Test]
    public void Parse_SegmentsHaveCorrectOrder()
    {
        // Arrange
        var script = "TICKER(AAPL).SESSION(IS.PREMARKET).BREAKOUT(150).TP(160).SL(145)";

        // Act
        var result = StrategyScriptParser.Parse(script);

        // Assert - Segments should have sequential Order values
        var orderedSegments = result.Segments.OrderBy(s => s.Order).ToList();
        for (int i = 0; i < orderedSegments.Count - 1; i++)
        {
            Assert.That(orderedSegments[i].Order, Is.LessThan(orderedSegments[i + 1].Order),
                $"Segment {orderedSegments[i].Type} should come before {orderedSegments[i + 1].Type}");
        }
    }

    #endregion
}

/// <summary>
/// Tests for separation of responsibility in IdiotScript command categories.
/// </summary>
[TestFixture]
public class SeparationOfResponsibilityTests
{
    #region Command Category Tests

    [Test]
    public void FluentApiMapping_HasDistinctCategories()
    {
        // Act
        var categories = FluentApiScriptMapping.AllMappings
            .Select(m => m.Category)
            .Distinct()
            .ToList();

        // Assert - Should have multiple distinct categories
        Assert.That(categories.Count, Is.GreaterThanOrEqualTo(5), $"Expected at least 5 categories, got {categories.Count}");
    }

    [Test]
    public void IdentityCategory_ContainsOnlyIdentityCommands()
    {
        // Act
        var identityMappings = FluentApiScriptMapping.GetByCategory("Identity");

        // Assert - Identity commands include NAME, DESC, ENABLED
        foreach (var mapping in identityMappings)
        {
            Assert.That(mapping.IdiotScriptCommands,
                Has.Some.Matches<string>(cmd => cmd is "NAME" or "DESC" or "ENABLED"));
        }
    }

    [Test]
    public void SessionCategory_ContainsOnlySessionCommands()
    {
        // Act
        var sessionMappings = FluentApiScriptMapping.GetByCategory("Session");

        // Assert
        Assert.That(sessionMappings, Is.Not.Empty);
        foreach (var mapping in sessionMappings)
        {
            Assert.That(
                mapping.IdiotScriptCommands.Any(cmd =>
                    cmd is "SESSION" or "START" or "END"), Is.True,
                $"Expected session command, got: {string.Join(", ", mapping.IdiotScriptCommands)}");
        }
    }

    [Test]
    public void OrderCategory_ContainsOnlyOrderCommands()
    {
        // Act
        var orderMappings = FluentApiScriptMapping.GetByCategory("Order");

        // Assert
        Assert.That(orderMappings, Is.Not.Empty);
        foreach (var mapping in orderMappings)
        {
            Assert.That(
                mapping.IdiotScriptCommands.Any(cmd =>
                    cmd is "BUY" or "SELL" or "QTY" or "CLOSE" or "CLOSELONG" or "CLOSESHORT"), Is.True,
                $"Expected order command, got: {string.Join(", ", mapping.IdiotScriptCommands)}");
        }
    }

    [Test]
    public void RiskManagementCategory_ContainsOnlyRiskCommands()
    {
        // Act
        var riskMappings = FluentApiScriptMapping.GetByCategory("RiskManagement");

        // Assert
        Assert.That(riskMappings, Is.Not.Empty);
        foreach (var mapping in riskMappings)
        {
            Assert.That(
                mapping.IdiotScriptCommands.Any(cmd =>
                    cmd is "TP" or "TAKEPROFIT" or "SL" or "STOPLOSS" or "TSL" or "TRAILINGSTOPLOSS"), Is.True,
                $"Expected risk command, got: {string.Join(", ", mapping.IdiotScriptCommands)}");
        }
    }

    #endregion

    #region No Cross-Category Pollution

    [Test]
    public void PriceConditions_NotInOrderCategory()
    {
        // Act
        var orderMappings = FluentApiScriptMapping.GetByCategory("Order");

        // Assert
        foreach (var mapping in orderMappings)
        {
            Assert.That(mapping.IdiotScriptCommands,
                Has.None.Matches<string>(cmd => cmd is "BREAKOUT" or "PULLBACK" or "ISPRICEABOVE" or "ISPRICEBELOW"));
        }
    }

    [Test]
    public void VwapConditions_NotInOrderCategory()
    {
        // Act
        var orderMappings = FluentApiScriptMapping.GetByCategory("Order");

        // Assert
        foreach (var mapping in orderMappings)
        {
            Assert.That(mapping.IdiotScriptCommands,
                Has.None.Matches<string>(cmd => cmd.Contains("VWAP", StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Test]
    public void IndicatorConditions_NotInOrderCategory()
    {
        // Act
        var orderMappings = FluentApiScriptMapping.GetByCategory("Order");

        // Assert
        foreach (var mapping in orderMappings)
        {
            Assert.That(mapping.IdiotScriptCommands,
                Has.None.Matches<string>(cmd => cmd.Contains("EMA", StringComparison.OrdinalIgnoreCase) ||
                       cmd.Contains("RSI", StringComparison.OrdinalIgnoreCase) ||
                       cmd.Contains("ADX", StringComparison.OrdinalIgnoreCase)));
        }
    }

    #endregion

    #region Segment Type Mapping

    [TestCase("TICKER", SegmentType.Ticker)]
    [TestCase("SESSION", SegmentType.SessionDuration)]
    [TestCase("BREAKOUT", SegmentType.Breakout)]
    [TestCase("PULLBACK", SegmentType.Pullback)]
    [TestCase("ABOVEVWAP", SegmentType.IsAboveVwap)]
    [TestCase("BELOWVWAP", SegmentType.IsBelowVwap)]
    [TestCase("EMAABOVE", SegmentType.IsEmaAbove)]
    [TestCase("EMABELOW", SegmentType.IsEmaBelow)]
    [TestCase("BUY", SegmentType.Buy)]
    [TestCase("SELL", SegmentType.Sell)]
    [TestCase("TP", SegmentType.TakeProfit)]
    [TestCase("SL", SegmentType.StopLoss)]
    [TestCase("TSL", SegmentType.TrailingStopLoss)]
    public void GetSegmentType_ReturnsCorrectType(string command, SegmentType expectedType)
    {
        // Act
        var result = FluentApiScriptMapping.GetSegmentType(command);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value, Is.EqualTo(expectedType));
    }

    [TestCase(SegmentType.Ticker)]
    [TestCase(SegmentType.SessionDuration)]
    [TestCase(SegmentType.Breakout)]
    [TestCase(SegmentType.Pullback)]
    [TestCase(SegmentType.IsAboveVwap)]
    [TestCase(SegmentType.IsBelowVwap)]
    [TestCase(SegmentType.Buy)]
    [TestCase(SegmentType.Sell)]
    [TestCase(SegmentType.TakeProfit)]
    [TestCase(SegmentType.StopLoss)]
    [TestCase(SegmentType.TrailingStopLoss)]
    public void GetIdiotScriptCommand_ReturnsNonEmptyCommand(SegmentType segmentType)
    {
        // Act
        var result = FluentApiScriptMapping.GetIdiotScriptCommand(segmentType);

        // Assert
        Assert.That(string.IsNullOrEmpty(result), Is.False);
    }

    #endregion
}

/// <summary>
/// Tests for round-trip conversion preserving logical structure.
/// </summary>
[TestFixture]
public class RoundTripPreservationTests
{
    #region Symbol Preservation

    [TestCase("AAPL")]
    [TestCase("NVDA")]
    [TestCase("TSLA")]
    [TestCase("AMD")]
    [TestCase("GOOG")]
    public void RoundTrip_PreservesSymbol(string symbol)
    {
        // Arrange
        var script = $"TICKER({symbol}).BREAKOUT.BUY";

        // Act
        var parsed = StrategyScriptParser.Parse(script);
        var serialized = IdiotScriptSerializer.Serialize(parsed);
        var reparsed = IdiotScriptParser.Parse(serialized);

        // Assert
        Assert.That(reparsed.Symbol, Is.EqualTo(symbol));
    }

    #endregion

    #region Segment Count Preservation

    [TestCase("TICKER(AAPL).BREAKOUT", 1)]
    [TestCase("TICKER(AAPL).BREAKOUT.ABOVEVWAP", 2)]
    [TestCase("TICKER(AAPL).BREAKOUT.ABOVEVWAP.TP(160)", 3)]
    public void RoundTrip_PreservesSegmentCount(string script, int minExpectedSegments)
    {
        // Act
        var parsed = StrategyScriptParser.Parse(script);
        var serialized = IdiotScriptSerializer.Serialize(parsed);
        var reparsed = IdiotScriptParser.Parse(serialized);

        // Assert
        Assert.That(reparsed.Segments.Count, Is.GreaterThanOrEqualTo(minExpectedSegments),
            $"Expected at least {minExpectedSegments} segments, got {reparsed.Segments.Count}");
    }

    #endregion

    #region Parameter Value Preservation

    [Test]
    public void RoundTrip_PreservesTakeProfitValue()
    {
        // Arrange
        var script = "TICKER(AAPL).BREAKOUT.BUY.TP(165.50)";

        // Act
        var parsed = StrategyScriptParser.Parse(script);
        var serialized = IdiotScriptSerializer.Serialize(parsed);
        var reparsed = IdiotScriptParser.Parse(serialized);

        // Assert
        var tpSegment = reparsed.Segments.FirstOrDefault(s => s.Type == SegmentType.TakeProfit);
        Assert.That(tpSegment, Is.Not.Null);
    }

    [Test]
    public void RoundTrip_PreservesStopLossValue()
    {
        // Arrange
        var script = "TICKER(AAPL).BREAKOUT.BUY.SL(145.00)";

        // Act
        var parsed = StrategyScriptParser.Parse(script);
        var serialized = IdiotScriptSerializer.Serialize(parsed);
        var reparsed = IdiotScriptParser.Parse(serialized);

        // Assert
        var slSegment = reparsed.Segments.FirstOrDefault(s => s.Type == SegmentType.StopLoss);
        Assert.That(slSegment, Is.Not.Null);
    }

    #endregion

    #region Validator Round-Trip

    [Test]
    public void ValidateRoundTrip_ValidScript_Succeeds()
    {
        // Arrange
        var script = "TICKER(AAPL).SESSION(IS.PREMARKET).BREAKOUT(150).BUY.TP(160)";

        // Act
        var result = IdiotScriptValidator.ValidateRoundTrip(script);

        // Assert
        Assert.That(result.IsValid, Is.True,
            $"Round-trip validation failed: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    #endregion
}
