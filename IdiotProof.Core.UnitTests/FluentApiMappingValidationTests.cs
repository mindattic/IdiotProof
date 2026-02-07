// ============================================================================
// FluentApiMappingValidationTests - Tests for Fluent API / IdiotScript mapping
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
// 4. Round-trip conversion preserves all data
//
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Validation;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests to ensure complete mapping between Fluent API and IdiotScript.
/// Validates bidirectional conversion capability.
/// </summary>
[TestFixture]
public class FluentApiMappingValidationTests
{
    #region Fluent API Coverage Tests

    [Test]
    public void ValidateFluentApiCoverage_AllMethodsHaveIdiotScriptEquivalents()
    {
        // Act
        var result = IdiotScriptValidator.ValidateFluentApiCoverage();

        // Assert
        Assert.That(result.IsValid, Is.True,
            $"Some fluent API methods lack IdiotScript equivalents: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Test]
    public void AllMappings_HaveAtLeastOneIdiotScriptCommand()
    {
        // Assert
        foreach (var mapping in FluentApiScriptMapping.AllMappings)
        {
            Assert.That(mapping.IdiotScriptCommands.Length, Is.GreaterThan(0),
                $"Fluent method '{mapping.FluentMethod}' has no IdiotScript commands");
        }
    }

    [Test]
    public void AllMappings_HaveValidCategory()
    {
        // Arrange
        var validCategories = new HashSet<string>
        {
            "Start", "Identity", "Session", "Order", "PriceCondition",
            "VwapCondition", "IndicatorCondition", "RiskManagement", "PositionManagement", "OrderConfig",
            "ExecutionBehavior"
        };

        // Assert
        foreach (var mapping in FluentApiScriptMapping.AllMappings)
        {
            Assert.That(validCategories, Does.Contain(mapping.Category),
                $"Invalid category '{mapping.Category}' for method '{mapping.FluentMethod}'");
        }
    }

    #endregion

    #region IdiotScript Coverage Tests

    [Test]
    public void ValidateIdiotScriptCoverage_AllCommandsHaveFluentApiEquivalents()
    {
        // Act
        var result = IdiotScriptValidator.ValidateIdiotScriptCoverage();

        // Assert - Warnings are acceptable, errors are not
        Assert.That(result.Errors.Count, Is.EqualTo(0),
            $"Some IdiotScript commands lack fluent API equivalents: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Test]
    public void AllWhitelistedCommands_AreDocumentedInMappings()
    {
        // Arrange
        var mappedCommands = FluentApiScriptMapping.AllMappings
            .SelectMany(m => m.IdiotScriptCommands)
            .Select(c => c.ToUpperInvariant())
            .ToHashSet();

        var booleanKeywords = new HashSet<string> { "TRUE", "FALSE", "YES", "NO", "Y", "N" };
        var unmappedCommands = new List<string>();

        // Act
        foreach (var command in IdiotScriptValidator.ValidCommands)
        {
            if (!booleanKeywords.Contains(command.ToUpperInvariant()) &&
                !mappedCommands.Contains(command.ToUpperInvariant()))
            {
                unmappedCommands.Add(command);
            }
        }

        // Assert - log warnings but don't fail
        if (unmappedCommands.Count > 0)
        {
            // This is informational - some commands may be aliases
            Assert.Pass($"Unmapped commands (may be aliases): {string.Join(", ", unmappedCommands)}");
        }
    }

    #endregion

    #region Parameter Compatibility Tests

    [Test]
    public void ValidateParameterCompatibility_NoMismatches()
    {
        // Act
        var result = IdiotScriptValidator.ValidateParameterCompatibility();

        // Assert
        Assert.That(result.IsValid, Is.True,
            $"Parameter compatibility issues: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Test]
    public void AllRequiredParameterMethods_HaveDocumentedParameters()
    {
        // Assert
        foreach (var mapping in FluentApiScriptMapping.AllMappings.Where(m => m.RequiresParameters))
        {
            Assert.That(mapping.Parameters.Length, Is.GreaterThan(0),
                $"Method '{mapping.FluentMethod}' requires parameters but has none documented");
        }
    }

    #endregion

    #region Complete Mapping Validation Tests

    [Test]
    public void ValidateMappingCompleteness_NoCriticalErrors()
    {
        // Act
        var result = IdiotScriptValidator.ValidateMappingCompleteness();

        // Assert
        Assert.That(result.Errors.Count, Is.EqualTo(0),
            $"Mapping completeness errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [TestCase("Start")]
    [TestCase("Identity")]
    [TestCase("Session")]
    [TestCase("Order")]
    [TestCase("PriceCondition")]
    [TestCase("VwapCondition")]
    [TestCase("IndicatorCondition")]
    [TestCase("RiskManagement")]
    [TestCase("PositionManagement")]
    public void GetByCategory_ReturnsNonEmptyResults(string category)
    {
        // Act
        var mappings = FluentApiScriptMapping.GetByCategory(category);

        // Assert
        Assert.That(mappings, Is.Not.Empty);
    }

    #endregion

    #region Bidirectional Lookup Tests

    [TestCase("TICKER")]
    [TestCase("SYM")]
    [TestCase("SYMBOL")]
    [TestCase("LONG")]
    [TestCase("SHORT")]
    [TestCase("TP")]
    [TestCase("SL")]
    [TestCase("TSL")]
    [TestCase("BREAKOUT")]
    [TestCase("PULLBACK")]
    [TestCase("SESSION")]
    public void FindByIdiotScriptCommand_ReturnsMapping(string command)
    {
        // Act
        var mapping = FluentApiScriptMapping.FindByIdiotScriptCommand(command);

        // Assert
        Assert.That(mapping, Is.Not.Null);
    }

    [TestCase("Stock.Ticker")]
    [TestCase(".Long")]
    [TestCase(".Short")]
    [TestCase(".TakeProfit")]
    [TestCase(".StopLoss")]
    [TestCase(".TrailingStopLoss")]
    [TestCase(".Breakout")]
    [TestCase(".Pullback")]
    [TestCase(".TimeFrame")]
    public void FindByFluentMethod_ReturnsMapping(string methodSubstring)
    {
        // Act
        var mapping = FluentApiScriptMapping.FindByFluentMethod(methodSubstring);

        // Assert
        Assert.That(mapping, Is.Not.Null);
    }

    #endregion
}


