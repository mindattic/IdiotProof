// ============================================================================
// ValidationCodesTests - Tests for validation code constants
// ============================================================================

using IdiotProof.Core.Validation;

namespace IdiotProof.Core.UnitTests.Validation;

/// <summary>
/// Tests for ValidationCodes constants.
/// </summary>
[TestFixture]
public class ValidationCodesTests
{
    #region General Validation Codes

    [Test]
    public void Required_HasCorrectValue()
    {
        Assert.That(ValidationCodes.Required, Is.EqualTo("REQUIRED"));
    }

    [Test]
    public void InvalidFormat_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidFormat, Is.EqualTo("INVALID_FORMAT"));
    }

    [Test]
    public void InvalidLength_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidLength, Is.EqualTo("INVALID_LENGTH"));
    }

    [Test]
    public void InvalidRange_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidRange, Is.EqualTo("INVALID_RANGE"));
    }

    [Test]
    public void InvalidValue_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidValue, Is.EqualTo("INVALID_VALUE"));
    }

    [Test]
    public void Duplicate_HasCorrectValue()
    {
        Assert.That(ValidationCodes.Duplicate, Is.EqualTo("DUPLICATE"));
    }

    [Test]
    public void NotFound_HasCorrectValue()
    {
        Assert.That(ValidationCodes.NotFound, Is.EqualTo("NOT_FOUND"));
    }

    #endregion

    #region Security Validation Codes

    [Test]
    public void InjectionDetected_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InjectionDetected, Is.EqualTo("INJECTION_DETECTED"));
    }

    [Test]
    public void InvalidCharacters_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidCharacters, Is.EqualTo("INVALID_CHARACTERS"));
    }

    [Test]
    public void PathTraversal_HasCorrectValue()
    {
        Assert.That(ValidationCodes.PathTraversal, Is.EqualTo("PATH_TRAVERSAL"));
    }

    [Test]
    public void MaliciousContent_HasCorrectValue()
    {
        Assert.That(ValidationCodes.MaliciousContent, Is.EqualTo("MALICIOUS_CONTENT"));
    }

    #endregion

    #region Strategy Validation Codes

    [Test]
    public void InvalidSymbol_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidSymbol, Is.EqualTo("INVALID_SYMBOL"));
    }

    [Test]
    public void MissingTicker_HasCorrectValue()
    {
        Assert.That(ValidationCodes.MissingTicker, Is.EqualTo("MISSING_TICKER"));
    }

    [Test]
    public void MissingCondition_HasCorrectValue()
    {
        Assert.That(ValidationCodes.MissingCondition, Is.EqualTo("MISSING_CONDITION"));
    }

    [Test]
    public void MissingOrder_HasCorrectValue()
    {
        Assert.That(ValidationCodes.MissingOrder, Is.EqualTo("MISSING_ORDER"));
    }

    [Test]
    public void InvalidSession_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidSession, Is.EqualTo("INVALID_SESSION"));
    }

    #endregion

    #region Order Validation Codes

    [Test]
    public void InvalidQuantity_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidQuantity, Is.EqualTo("INVALID_QUANTITY"));
    }

    [Test]
    public void InvalidPrice_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidPrice, Is.EqualTo("INVALID_PRICE"));
    }

    [Test]
    public void InvalidStopLoss_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidStopLoss, Is.EqualTo("INVALID_STOP_LOSS"));
    }

    [Test]
    public void InvalidTakeProfit_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidTakeProfit, Is.EqualTo("INVALID_TAKE_PROFIT"));
    }

    #endregion

    #region IdiotScript Validation Codes

    [Test]
    public void InvalidSyntax_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidSyntax, Is.EqualTo("INVALID_SYNTAX"));
    }

    [Test]
    public void InvalidCommand_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidCommand, Is.EqualTo("INVALID_COMMAND"));
    }

    [Test]
    public void RoundTripMismatch_HasCorrectValue()
    {
        Assert.That(ValidationCodes.RoundTripMismatch, Is.EqualTo("ROUNDTRIP_MISMATCH"));
    }

    [Test]
    public void MissingParameter_HasCorrectValue()
    {
        Assert.That(ValidationCodes.MissingParameter, Is.EqualTo("MISSING_PARAMETER"));
    }

    [Test]
    public void ParameterOutOfRange_HasCorrectValue()
    {
        Assert.That(ValidationCodes.ParameterOutOfRange, Is.EqualTo("PARAMETER_OUT_OF_RANGE"));
    }

    [Test]
    public void NoScriptEquivalent_HasCorrectValue()
    {
        Assert.That(ValidationCodes.NoScriptEquivalent, Is.EqualTo("NO_SCRIPT_EQUIVALENT"));
    }

    [Test]
    public void NoFluentEquivalent_HasCorrectValue()
    {
        Assert.That(ValidationCodes.NoFluentEquivalent, Is.EqualTo("NO_FLUENT_EQUIVALENT"));
    }

    [Test]
    public void InvalidBoolean_HasCorrectValue()
    {
        Assert.That(ValidationCodes.InvalidBoolean, Is.EqualTo("INVALID_BOOLEAN"));
    }

    #endregion

    #region Connection Validation Codes

    [Test]
    public void ConnectionFailed_HasCorrectValue()
    {
        Assert.That(ValidationCodes.ConnectionFailed, Is.EqualTo("CONNECTION_FAILED"));
    }

    [Test]
    public void Timeout_HasCorrectValue()
    {
        Assert.That(ValidationCodes.Timeout, Is.EqualTo("TIMEOUT"));
    }

    [Test]
    public void NotConnected_HasCorrectValue()
    {
        Assert.That(ValidationCodes.NotConnected, Is.EqualTo("NOT_CONNECTED"));
    }

    [Test]
    public void ServiceUnavailable_HasCorrectValue()
    {
        Assert.That(ValidationCodes.ServiceUnavailable, Is.EqualTo("SERVICE_UNAVAILABLE"));
    }

    #endregion

    #region Uniqueness Tests

    [Test]
    public void AllCodes_AreUnique()
    {
        var codes = new[]
        {
            ValidationCodes.Required,
            ValidationCodes.InvalidFormat,
            ValidationCodes.InvalidLength,
            ValidationCodes.InvalidRange,
            ValidationCodes.InvalidValue,
            ValidationCodes.Duplicate,
            ValidationCodes.NotFound,
            ValidationCodes.InjectionDetected,
            ValidationCodes.InvalidCharacters,
            ValidationCodes.PathTraversal,
            ValidationCodes.MaliciousContent,
            ValidationCodes.InvalidSymbol,
            ValidationCodes.MissingTicker,
            ValidationCodes.MissingCondition,
            ValidationCodes.MissingOrder,
            ValidationCodes.InvalidSession,
            ValidationCodes.InvalidQuantity,
            ValidationCodes.InvalidPrice,
            ValidationCodes.InvalidStopLoss,
            ValidationCodes.InvalidTakeProfit,
            ValidationCodes.InvalidSyntax,
            ValidationCodes.InvalidCommand,
            ValidationCodes.RoundTripMismatch,
            ValidationCodes.MissingParameter,
            ValidationCodes.ParameterOutOfRange,
            ValidationCodes.NoScriptEquivalent,
            ValidationCodes.NoFluentEquivalent,
            ValidationCodes.InvalidBoolean,
            ValidationCodes.ConnectionFailed,
            ValidationCodes.Timeout,
            ValidationCodes.NotConnected,
            ValidationCodes.ServiceUnavailable
        };

        var uniqueCodes = codes.Distinct().ToList();

        Assert.That(uniqueCodes.Count, Is.EqualTo(codes.Length), "Duplicate validation codes found");
    }

    #endregion
}


