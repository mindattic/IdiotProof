// ============================================================================
// BooleanConstantTests - Tests for IS.TRUE/IS.FALSE boolean handling
// ============================================================================
//
// NOMENCLATURE:
// - Boolean Constant: IS.TRUE or IS.FALSE (IdiotScript canonical form)
// - Truthy Value: Y, YES, yes, true, TRUE, 1, IS.TRUE
// - Falsy Value: N, NO, no, false, FALSE, 0, IS.FALSE
//
// These tests validate:
// 1. All valid truthy values resolve to true
// 2. All valid falsy values resolve to false
// 3. Invalid values return null
// 4. Boolean normalization works correctly
//
// ============================================================================

using IdiotProof.Shared.Scripting;
using IdiotProof.Shared.Validation;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for IdiotScript boolean constants (IS.TRUE, IS.FALSE) and value resolution.
/// </summary>
[TestFixture]
public class BooleanConstantTests
{
    #region IS.TRUE / IS.FALSE Constants

    [Test]
    public void TRUE_Constant_HasCorrectValue()
    {
        Assert.That(IdiotScriptConstants.TRUE, Is.EqualTo("IS.TRUE"));
    }

    [Test]
    public void FALSE_Constant_HasCorrectValue()
    {
        Assert.That(IdiotScriptConstants.FALSE, Is.EqualTo("IS.FALSE"));
    }

    #endregion

    #region ResolveBoolean - Truthy Values

    [TestCase("Y")]
    [TestCase("YES")]
    [TestCase("yes")]
    [TestCase("true")]
    [TestCase("TRUE")]
    [TestCase("True")]
    [TestCase("1")]
    [TestCase("IS.TRUE")]
    [TestCase("is.true")]
    [TestCase("Is.True")]
    public void ResolveBoolean_TruthyValues_ReturnsTrue(string input)
    {
        // Act
        var result = IdiotScriptConstants.ResolveBoolean(input);

        // Assert
        Assert.That(result.HasValue, Is.True);
        Assert.That(result!.Value, Is.True);
    }

    #endregion

    #region ResolveBoolean - Falsy Values

    [TestCase("N")]
    [TestCase("NO")]
    [TestCase("no")]
    [TestCase("No")]
    [TestCase("false")]
    [TestCase("FALSE")]
    [TestCase("False")]
    [TestCase("0")]
    [TestCase("IS.FALSE")]
    [TestCase("is.false")]
    [TestCase("Is.False")]
    public void ResolveBoolean_FalsyValues_ReturnsFalse(string input)
    {
        // Act
        var result = IdiotScriptConstants.ResolveBoolean(input);

        // Assert
        Assert.That(result.HasValue, Is.True);
        Assert.That(result!.Value, Is.False);
    }

    #endregion

    #region ResolveBoolean - Invalid Values

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("maybe")]
    [TestCase("yep")]
    [TestCase("nope")]
    [TestCase("2")]
    [TestCase("-1")]
    [TestCase("IS.MAYBE")]
    [TestCase("IS.")]
    [TestCase("IS")]
    public void ResolveBoolean_InvalidValues_ReturnsNull(string? input)
    {
        // Act
        var result = IdiotScriptConstants.ResolveBoolean(input);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region IsValidBoolean Tests

    [TestCase("Y", true)]
    [TestCase("YES", true)]
    [TestCase("TRUE", true)]
    [TestCase("1", true)]
    [TestCase("IS.TRUE", true)]
    [TestCase("N", true)]
    [TestCase("NO", true)]
    [TestCase("FALSE", true)]
    [TestCase("0", true)]
    [TestCase("IS.FALSE", true)]
    [TestCase("maybe", false)]
    [TestCase("invalid", false)]
    [TestCase(null, false)]
    [TestCase("", false)]
    public void IsValidBoolean_ReturnsExpectedResult(string? input, bool expected)
    {
        // Act
        var result = IdiotScriptConstants.IsValidBoolean(input);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ToIdiotScriptBoolean Tests

    [TestCase(true, "IS.TRUE")]
    [TestCase(false, "IS.FALSE")]
    public void ToIdiotScriptBoolean_ReturnsCanonicalForm(bool input, string expected)
    {
        // Act
        var result = IdiotScriptConstants.ToIdiotScriptBoolean(input);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region NormalizeBoolean Tests

    [TestCase("Y", "IS.TRUE")]
    [TestCase("YES", "IS.TRUE")]
    [TestCase("true", "IS.TRUE")]
    [TestCase("1", "IS.TRUE")]
    [TestCase("N", "IS.FALSE")]
    [TestCase("NO", "IS.FALSE")]
    [TestCase("false", "IS.FALSE")]
    [TestCase("0", "IS.FALSE")]
    [TestCase("IS.TRUE", "IS.TRUE")]
    [TestCase("IS.FALSE", "IS.FALSE")]
    public void NormalizeBoolean_ValidInput_ReturnsCanonicalForm(string input, string expected)
    {
        // Act
        var result = IdiotScriptConstants.NormalizeBoolean(input);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("invalid")]
    [TestCase("maybe")]
    public void NormalizeBoolean_InvalidInput_ReturnsNull(string? input)
    {
        // Act
        var result = IdiotScriptConstants.NormalizeBoolean(input);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region TruthyValues / FalsyValues Arrays

    [Test]
    public void TruthyValues_ContainsExpectedValues()
    {
        // Assert - TruthyValues contains uppercase constants
        Assert.That(IdiotScriptConstants.TruthyValues, Does.Contain("Y"));
        Assert.That(IdiotScriptConstants.TruthyValues, Does.Contain("YES"));
        Assert.That(IdiotScriptConstants.TruthyValues, Does.Contain("TRUE"));
        Assert.That(IdiotScriptConstants.TruthyValues, Does.Contain("1"));
        Assert.That(IdiotScriptConstants.TruthyValues, Does.Contain("IS.TRUE"));
    }

    [Test]
    public void FalsyValues_ContainsExpectedValues()
    {
        // Assert - FalsyValues contains uppercase constants
        Assert.That(IdiotScriptConstants.FalsyValues, Does.Contain("N"));
        Assert.That(IdiotScriptConstants.FalsyValues, Does.Contain("NO"));
        Assert.That(IdiotScriptConstants.FalsyValues, Does.Contain("FALSE"));
        Assert.That(IdiotScriptConstants.FalsyValues, Does.Contain("0"));
        Assert.That(IdiotScriptConstants.FalsyValues, Does.Contain("IS.FALSE"));
    }

    #endregion

    #region AllBooleanValues HashSet

    [Test]
    public void AllBooleanValues_ContainsAllValidValues()
    {
        // Assert - case-insensitive
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("Y"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("YES"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("TRUE"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("1"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("IS.TRUE"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("N"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("NO"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("FALSE"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("0"));
        Assert.That(IdiotScriptConstants.AllBooleanValues, Does.Contain("IS.FALSE"));
    }

    [Test]
    public void AllBooleanValues_IsCaseInsensitive()
    {
        // Assert
        Assert.That(IdiotScriptConstants.AllBooleanValues.Contains("yes"), Is.True);
        Assert.That(IdiotScriptConstants.AllBooleanValues.Contains("YES"), Is.True);
        Assert.That(IdiotScriptConstants.AllBooleanValues.Contains("Yes"), Is.True);
        Assert.That(IdiotScriptConstants.AllBooleanValues.Contains("no"), Is.True);
        Assert.That(IdiotScriptConstants.AllBooleanValues.Contains("NO"), Is.True);
        Assert.That(IdiotScriptConstants.AllBooleanValues.Contains("No"), Is.True);
    }

    #endregion

    #region Validator Boolean Validation

    [TestCase("Y")]
    [TestCase("YES")]
    [TestCase("TRUE")]
    [TestCase("IS.TRUE")]
    [TestCase("N")]
    [TestCase("NO")]
    [TestCase("FALSE")]
    [TestCase("IS.FALSE")]
    public void ValidateBooleanParameter_ValidValues_ReturnsSuccess(string value)
    {
        // Act
        var result = IdiotScriptValidator.ValidateBooleanParameter(value, "TestField");

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [TestCase("invalid")]
    [TestCase("maybe")]
    [TestCase("2")]
    public void ValidateBooleanParameter_InvalidValues_ReturnsError(string value)
    {
        // Act
        var result = IdiotScriptValidator.ValidateBooleanParameter(value, "TestField");

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidValue));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateBooleanParameter_EmptyValues_ReturnsSuccess(string? value)
    {
        // Act
        var result = IdiotScriptValidator.ValidateBooleanParameter(value, "TestField");

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    #endregion
}


