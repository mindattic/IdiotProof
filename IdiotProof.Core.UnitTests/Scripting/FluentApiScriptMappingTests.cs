// ============================================================================
// FluentApiScriptMappingTests - Tests for Fluent API / IdiotScript mapping
// ============================================================================
//
// NOMENCLATURE:
// - Fluent API: The C# builder pattern API (Stock.Ticker().Breakout().Long())
// - IdiotScript: The text-based DSL (Ticker(AAPL).Breakout(150).Buy)
// - Mapping: The bidirectional relationship between Fluent API and IdiotScript
// - Round-trip: Converting from one format to the other and back
//
// These tests validate:
// 1. Every fluent API method has an IdiotScript equivalent
// 2. Every IdiotScript command has a fluent API equivalent
// 3. Parameters are compatible between both formats
//
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Validation;

namespace IdiotProof.Core.UnitTests.Scripting;

/// <summary>
/// Tests to ensure complete mapping between Fluent API and IdiotScript.
/// Validates bidirectional conversion capability.
/// </summary>
[TestFixture]
public class FluentApiScriptMappingTests
{
    #region Fluent API Coverage Tests

    [Test]
    public void ValidateFluentApiCoverage_AllMethodsHaveIdiotScriptEquivalents()
    {
        var result = IdiotScriptValidator.ValidateFluentApiCoverage();

        Assert.That(result.IsValid, Is.True,
            $"Some fluent API methods lack IdiotScript equivalents: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Test]
    public void AllMappings_HaveAtLeastOneIdiotScriptCommand()
    {
        foreach (var mapping in FluentApiScriptMapping.AllMappings)
        {
            Assert.That(mapping.IdiotScriptCommands.Length, Is.GreaterThan(0),
                $"Fluent method '{mapping.FluentMethod}' has no IdiotScript commands");
        }
    }

    [Test]
    public void AllMappings_HaveValidCategory()
    {
        var validCategories = new HashSet<string>
        {
            "Start", "Identity", "Session", "Order", "PriceCondition",
            "VwapCondition", "IndicatorCondition", "RiskManagement", "PositionManagement",
            "OrderConfig", "ExecutionBehavior"
        };

        foreach (var mapping in FluentApiScriptMapping.AllMappings)
        {
            Assert.That(validCategories, Does.Contain(mapping.Category),
                $"Invalid category '{mapping.Category}' for method '{mapping.FluentMethod}'");
        }
    }

    [Test]
    public void AllMappings_HaveDescription()
    {
        foreach (var mapping in FluentApiScriptMapping.AllMappings)
        {
            Assert.That(string.IsNullOrWhiteSpace(mapping.Description), Is.False,
                $"Fluent method '{mapping.FluentMethod}' has no description");
        }
    }

    #endregion

    #region IdiotScript Coverage Tests

    [Test]
    public void ValidateIdiotScriptCoverage_AllCommandsHaveFluentApiEquivalents()
    {
        var result = IdiotScriptValidator.ValidateIdiotScriptCoverage();

        // Warnings are acceptable, errors are not
        Assert.That(result.Errors.Count, Is.EqualTo(0),
            $"Some IdiotScript commands lack fluent API equivalents: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Test]
    public void AllWhitelistedCommands_AreDocumentedInMappings()
    {
        var mappedCommands = FluentApiScriptMapping.AllMappings
            .SelectMany(m => m.IdiotScriptCommands)
            .Select(c => c.ToUpperInvariant())
            .ToHashSet();

        var booleanKeywords = new HashSet<string> { "TRUE", "FALSE", "YES", "NO", "Y", "N" };
        var unmappedCommands = new List<string>();

        foreach (var command in IdiotScriptValidator.ValidCommands)
        {
            // Skip boolean keywords - they're parameters, not commands
            if (booleanKeywords.Contains(command.ToUpperInvariant()))
                continue;

            if (!mappedCommands.Contains(command.ToUpperInvariant()))
            {
                unmappedCommands.Add(command);
            }
        }

        Assert.That(unmappedCommands, Is.Empty,
            $"Commands not documented in mappings: {string.Join(", ", unmappedCommands)}");
    }

    #endregion

    #region Symbol/Identity Mappings

    [Test]
    public void SymbolMappings_ExistAndAreComplete()
    {
        var symbolMappings = FluentApiScriptMapping.AllMappings
            .Where(m => m.Category == "Start" || m.Category == "Identity")
            .ToList();

        Assert.That(symbolMappings, Is.Not.Empty);

        // Verify TICKER/SYM/SYMBOL are mapped
        var tickerMapping = symbolMappings.FirstOrDefault(m =>
            m.IdiotScriptCommands.Contains("TICKER") ||
            m.IdiotScriptCommands.Contains("SYM") ||
            m.IdiotScriptCommands.Contains("SYMBOL"));

        Assert.That(tickerMapping, Is.Not.Null, "No mapping found for ticker commands");
    }

    #endregion

    #region Session Mappings

    [Test]
    public void SessionMappings_ExistAndAreComplete()
    {
        var sessionMappings = FluentApiScriptMapping.AllMappings
            .Where(m => m.Category == "Session")
            .ToList();

        Assert.That(sessionMappings, Is.Not.Empty);

        // Verify SESSION command is mapped
        var sessionMapping = sessionMappings.FirstOrDefault(m =>
            m.IdiotScriptCommands.Contains("SESSION"));

        Assert.That(sessionMapping, Is.Not.Null, "No mapping found for SESSION command");
    }

    #endregion

    #region Order Mappings

    [Test]
    public void OrderMappings_ExistAndAreComplete()
    {
        var orderMappings = FluentApiScriptMapping.AllMappings
            .Where(m => m.Category == "Order")
            .ToList();

        Assert.That(orderMappings, Is.Not.Empty);

        // Verify BUY and QTY are mapped
        var hasQty = orderMappings.Any(m =>
            m.IdiotScriptCommands.Contains("QTY") ||
            m.IdiotScriptCommands.Contains("QUANTITY"));

        Assert.That(hasQty, Is.True, "No mapping found for quantity commands");
    }

    #endregion

    #region Risk Management Mappings

    [Test]
    public void RiskManagementMappings_ExistAndAreComplete()
    {
        var riskMappings = FluentApiScriptMapping.AllMappings
            .Where(m => m.Category == "RiskManagement")
            .ToList();

        Assert.That(riskMappings, Is.Not.Empty);

        // Verify TP, SL, TSL are mapped
        var hasTp = riskMappings.Any(m =>
            m.IdiotScriptCommands.Contains("TP") ||
            m.IdiotScriptCommands.Contains("TAKEPROFIT"));

        var hasSl = riskMappings.Any(m =>
            m.IdiotScriptCommands.Contains("SL") ||
            m.IdiotScriptCommands.Contains("STOPLOSS"));

        var hasTsl = riskMappings.Any(m =>
            m.IdiotScriptCommands.Contains("TSL") ||
            m.IdiotScriptCommands.Contains("TRAILINGSTOPLOSS"));

        Assert.That(hasTp, Is.True, "No mapping found for take profit commands");
        Assert.That(hasSl, Is.True, "No mapping found for stop loss commands");
        Assert.That(hasTsl, Is.True, "No mapping found for trailing stop loss commands");
    }

    #endregion

    #region Indicator Mappings

    [Test]
    public void IndicatorMappings_ExistAndAreComplete()
    {
        var indicatorMappings = FluentApiScriptMapping.AllMappings
            .Where(m => m.Category == "IndicatorCondition")
            .ToList();

        Assert.That(indicatorMappings, Is.Not.Empty);

        // Verify EMA commands are mapped
        var hasEma = indicatorMappings.Any(m =>
            m.IdiotScriptCommands.Any(c => c.Contains("EMA", StringComparison.OrdinalIgnoreCase)));

        Assert.That(hasEma, Is.True, "No mapping found for EMA commands");
    }

    #endregion

    #region VWAP Mappings

    [Test]
    public void VwapMappings_ExistAndAreComplete()
    {
        var vwapMappings = FluentApiScriptMapping.AllMappings
            .Where(m => m.Category == "VwapCondition")
            .ToList();

        Assert.That(vwapMappings, Is.Not.Empty);

        var hasAboveVwap = vwapMappings.Any(m =>
            m.IdiotScriptCommands.Any(c => c.Contains("ABOVEVWAP", StringComparison.OrdinalIgnoreCase)));

        var hasBelowVwap = vwapMappings.Any(m =>
            m.IdiotScriptCommands.Any(c => c.Contains("BELOWVWAP", StringComparison.OrdinalIgnoreCase)));

        Assert.That(hasAboveVwap, Is.True, "No mapping found for AboveVwap command");
        Assert.That(hasBelowVwap, Is.True, "No mapping found for BelowVwap command");
    }

    #endregion

    #region Price Condition Mappings

    [Test]
    public void PriceConditionMappings_ExistAndAreComplete()
    {
        var priceMappings = FluentApiScriptMapping.AllMappings
            .Where(m => m.Category == "PriceCondition")
            .ToList();

        Assert.That(priceMappings, Is.Not.Empty);

        var hasBreakout = priceMappings.Any(m =>
            m.IdiotScriptCommands.Contains("BREAKOUT"));

        var hasPullback = priceMappings.Any(m =>
            m.IdiotScriptCommands.Contains("PULLBACK"));

        Assert.That(hasBreakout, Is.True, "No mapping found for BREAKOUT command");
        Assert.That(hasPullback, Is.True, "No mapping found for PULLBACK command");
    }

    #endregion
}


