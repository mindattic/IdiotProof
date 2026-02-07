// ============================================================================
// ValidationResultTests - Tests for ValidationResult class
// ============================================================================

using IdiotProof.Core.Validation;

namespace IdiotProof.Core.UnitTests.Validation;

/// <summary>
/// Tests for ValidationResult class and related functionality.
/// </summary>
[TestFixture]
public class ValidationResultTests
{
    #region Success Factory

    [Test]
    public void Success_ReturnsValidResult()
    {
        var result = ValidationResult.Success();

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings, Is.Empty);
    }

    #endregion

    #region Failure Factory

    [Test]
    public void Failure_WithCodeAndMessage_ReturnsInvalidResult()
    {
        var result = ValidationResult.Failure("TEST_CODE", "Test message");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0].Code, Is.EqualTo("TEST_CODE"));
        Assert.That(result.Errors[0].Message, Is.EqualTo("Test message"));
    }

    [Test]
    public void Failure_WithFieldName_IncludesFieldName()
    {
        var result = ValidationResult.Failure("TEST_CODE", "Test message", "TestField");

        Assert.That(result.Errors[0].FieldName, Is.EqualTo("TestField"));
    }

    [Test]
    public void Failure_WithAttemptedValue_IncludesAttemptedValue()
    {
        var result = ValidationResult.Failure("TEST_CODE", "Test message", "TestField", "BadValue");

        Assert.That(result.Errors[0].AttemptedValue, Is.EqualTo("BadValue"));
    }

    [Test]
    public void Failure_WithMultipleErrors_ReturnsAllErrors()
    {
        var errors = new[]
        {
            new ValidationError("CODE1", "Message 1"),
            new ValidationError("CODE2", "Message 2"),
            new ValidationError("CODE3", "Message 3")
        };

        var result = ValidationResult.Failure(errors);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(3));
    }

    #endregion

    #region Combine

    [Test]
    public void Combine_MultipleSuccessResults_ReturnsSuccess()
    {
        var result = ValidationResult.Combine(
            ValidationResult.Success(),
            ValidationResult.Success(),
            ValidationResult.Success());

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Combine_MixedResults_ReturnsAllErrors()
    {
        var result = ValidationResult.Combine(
            ValidationResult.Success(),
            ValidationResult.Failure("CODE1", "Error 1"),
            ValidationResult.Failure("CODE2", "Error 2"));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(2));
    }

    [Test]
    public void Combine_PreservesWarnings()
    {
        var withWarning = new ValidationResult
        {
            Warnings = [new ValidationWarning("WARN1", "Warning 1")]
        };

        var result = ValidationResult.Combine(
            ValidationResult.Success(),
            withWarning);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
    }

    #endregion

    #region GetErrorSummary

    [Test]
    public void GetErrorSummary_MultipleErrors_JoinsWithSemicolon()
    {
        var result = ValidationResult.Combine(
            ValidationResult.Failure("CODE1", "Error 1"),
            ValidationResult.Failure("CODE2", "Error 2"));

        var summary = result.GetErrorSummary();

        Assert.That(summary, Does.Contain("Error 1"));
        Assert.That(summary, Does.Contain("Error 2"));
        Assert.That(summary, Does.Contain(";"));
    }

    [Test]
    public void GetErrorSummary_NoErrors_ReturnsEmpty()
    {
        var result = ValidationResult.Success();

        var summary = result.GetErrorSummary();

        Assert.That(summary, Is.Empty);
    }

    #endregion

    #region ThrowIfInvalid

    [Test]
    public void ThrowIfInvalid_Valid_DoesNotThrow()
    {
        var result = ValidationResult.Success();

        Assert.DoesNotThrow(() => result.ThrowIfInvalid());
    }

    [Test]
    public void ThrowIfInvalid_Invalid_ThrowsValidationException()
    {
        var result = ValidationResult.Failure("CODE", "Error message");

        Assert.Throws<ValidationException>(() => result.ThrowIfInvalid());
    }

    #endregion
}


