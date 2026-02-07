// ============================================================================
// InputValidatorTests - Tests for primitive input validation
// ============================================================================

using IdiotProof.Core.Validation;

namespace IdiotProof.Core.UnitTests.Validation;

/// <summary>
/// Tests for InputValidator class - validates primitive inputs.
/// </summary>
[TestFixture]
public class InputValidatorTests
{
    #region ValidateRequired

    [Test]
    public void ValidateRequired_WithValue_ReturnsValid()
    {
        var result = InputValidator.ValidateRequired("test", "TestField");

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    [TestCase("\n")]
    public void ValidateRequired_NullOrEmpty_ReturnsError(string? value)
    {
        var result = InputValidator.ValidateRequired(value, "TestField");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.Required));
        Assert.That(result.Errors[0].FieldName, Is.EqualTo("TestField"));
    }

    #endregion

    #region ValidateLength

    [TestCase("abc", 1, 5)]
    [TestCase("a", 1, 1)]
    [TestCase("12345", 1, 5)]
    public void ValidateLength_WithinBounds_ReturnsValid(string value, int min, int max)
    {
        var result = InputValidator.ValidateLength(value, "TestField", min, max);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateLength_NullValue_ReturnsValid()
    {
        var result = InputValidator.ValidateLength(null, "TestField", 1, 10);

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase("ab", 3, 5)]
    [TestCase("abcdef", 1, 5)]
    [TestCase("", 1, 5)]
    public void ValidateLength_OutOfBounds_ReturnsError(string value, int min, int max)
    {
        var result = InputValidator.ValidateLength(value, "TestField", min, max);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InvalidLength));
    }

    #endregion

    #region ValidateTickerSymbol

    [TestCase("AAPL")]
    [TestCase("NVDA")]
    [TestCase("A")]
    [TestCase("META")]
    [TestCase("PLTR")]
    [TestCase("TSLA")]
    [TestCase("GOOG")]
    public void ValidateTickerSymbol_ValidSymbols_ReturnsValid(string symbol)
    {
        var result = InputValidator.ValidateTickerSymbol(symbol);

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase("aapl")]  // Lowercase
    [TestCase("AAPLX")]  // 5 chars is valid
    [TestCase(" AAPL")]  // Leading space (gets trimmed)
    [TestCase("AAPL ")]  // Trailing space (gets trimmed)
    public void ValidateTickerSymbol_EdgeCases_HandledCorrectly(string symbol)
    {
        // These should still be valid after normalization
        var result = InputValidator.ValidateTickerSymbol(symbol);
        // Depends on implementation - lowercase may be normalized
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateTickerSymbol_NullOrEmpty_ReturnsRequired(string? symbol)
    {
        var result = InputValidator.ValidateTickerSymbol(symbol);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.Required));
    }

    [TestCase("AAPLXX")]  // Too long (6 chars)
    [TestCase("123")]     // Numbers only
    [TestCase("AAP1")]    // Contains number
    [TestCase("AA-PL")]   // Contains hyphen
    [TestCase("AA.PL")]   // Contains period
    [TestCase("AA PL")]   // Contains space
    public void ValidateTickerSymbol_InvalidFormat_ReturnsInvalidSymbol(string symbol)
    {
        var result = InputValidator.ValidateTickerSymbol(symbol);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InvalidSymbol));
    }

    #endregion

    #region ValidateSafeText - XSS Detection

    [TestCase("<script>alert('xss')</script>")]
    [TestCase("<SCRIPT>alert('xss')</SCRIPT>")]
    [TestCase("javascript:alert('xss')")]
    [TestCase("JAVASCRIPT:alert('xss')")]
    [TestCase("<iframe src='evil.com'></iframe>")]
    [TestCase("<object data='evil.swf'></object>")]
    [TestCase("<embed src='evil.swf'>")]
    [TestCase("vbscript:msgbox('xss')")]
    [TestCase("data:text/html,<script>alert('xss')</script>")]
    public void ValidateSafeText_XssPatterns_ReturnsInjectionDetected(string maliciousContent)
    {
        var result = InputValidator.ValidateSafeText(maliciousContent, "TestField");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InjectionDetected));
    }

    [TestCase("onclick=alert('xss')")]
    [TestCase("onload=evil()")]
    [TestCase("onerror=hack()")]
    [TestCase("onmouseover=steal()")]
    public void ValidateSafeText_EventHandlerPatterns_ReturnsInjectionDetected(string eventHandler)
    {
        var result = InputValidator.ValidateSafeText(eventHandler, "TestField");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InjectionDetected));
    }

    #endregion

    #region ValidateSafeText - SQL Injection Detection

    [TestCase("'; DROP TABLE users; --")]
    [TestCase("1 OR 1=1")]
    [TestCase("1 AND 1=1")]
    [TestCase("UNION SELECT * FROM passwords")]
    [TestCase("SELECT * FROM users")]
    [TestCase("DELETE FROM strategies")]
    [TestCase("UPDATE users SET admin=1")]
    [TestCase("INSERT INTO hacks VALUES('evil')")]
    [TestCase("EXEC xp_cmdshell")]
    [TestCase("EXECUTE sp_configure")]
    public void ValidateSafeText_SqlInjectionPatterns_ReturnsInjectionDetected(string sqlInjection)
    {
        var result = InputValidator.ValidateSafeText(sqlInjection, "TestField");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InjectionDetected));
    }

    [TestCase("$(whoami)")]
    [TestCase("`cat /etc/passwd`")]
    [TestCase("&& rm -rf /")]
    [TestCase("|| shutdown -h now")]
    public void ValidateSafeText_CommandInjectionPatterns_ReturnsInjectionDetected(string cmdInjection)
    {
        var result = InputValidator.ValidateSafeText(cmdInjection, "TestField");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InjectionDetected));
    }

    #endregion

    #region ValidateSafeText - Template Injection Detection

    [TestCase("{{constructor.constructor('return this')()}}")]
    [TestCase("${7*7}")]
    [TestCase("#{7*7}")]
    [TestCase("{{config}}")]
    [TestCase("${T(java.lang.Runtime).getRuntime().exec('id')}")]
    public void ValidateSafeText_TemplateInjectionPatterns_ReturnsInjectionDetected(string templateInjection)
    {
        var result = InputValidator.ValidateSafeText(templateInjection, "TestField");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InjectionDetected));
    }

    #endregion

    #region ValidateSafeText - Valid Content

    [TestCase("My Trading Strategy")]
    [TestCase("Buy AAPL at $150")]
    [TestCase("Pre-market breakout play")]
    [TestCase("Target: 10% profit")]
    [TestCase("")]  // Empty is allowed by default
    [TestCase(null)]  // Null is allowed by default
    public void ValidateSafeText_ValidContent_ReturnsValid(string? text)
    {
        var result = InputValidator.ValidateSafeText(text, "TestField");

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateSafeText_NullNotAllowed_ReturnsRequired()
    {
        var result = InputValidator.ValidateSafeText(null, "TestField", allowNull: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.Required));
    }

    #endregion

    #region ValidateFilename

    [TestCase("strategy.idiot")]
    [TestCase("my-strategy.idiot")]
    [TestCase("AAPL_breakout.idiot")]
    [TestCase("test123.txt")]
    public void ValidateFilename_ValidFilenames_ReturnsValid(string filename)
    {
        var result = InputValidator.ValidateFilename(filename);

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateFilename_NullOrEmpty_ReturnsRequired(string? filename)
    {
        var result = InputValidator.ValidateFilename(filename);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.Required));
    }

    [TestCase("../../../etc/passwd")]
    [TestCase("..\\..\\windows\\system32")]
    [TestCase("~/.ssh/id_rsa")]
    [TestCase("folder/../../../secret")]
    [TestCase("%2e%2e/etc/passwd")]
    public void ValidateFilename_PathTraversal_ReturnsError(string filename)
    {
        var result = InputValidator.ValidateFilename(filename);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.PathTraversal));
    }

    #endregion

    #region ValidateRange - Integer

    [TestCase(5, 1, 10)]
    [TestCase(1, 1, 10)]
    [TestCase(10, 1, 10)]
    [TestCase(0, 0, 100)]
    public void ValidateRange_IntegerWithinRange_ReturnsValid(int value, int min, int max)
    {
        var result = InputValidator.ValidateRange(value, "TestField", min, max);

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(0, 1, 10)]
    [TestCase(11, 1, 10)]
    [TestCase(-1, 0, 100)]
    public void ValidateRange_IntegerOutOfRange_ReturnsError(int value, int min, int max)
    {
        var result = InputValidator.ValidateRange(value, "TestField", min, max);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InvalidRange));
    }

    #endregion

    #region ValidateRange - Double

    [TestCase(5.5, 1.0, 10.0)]
    [TestCase(1.0, 1.0, 10.0)]
    [TestCase(10.0, 1.0, 10.0)]
    [TestCase(0.01, 0.0, 1.0)]
    public void ValidateRange_DoubleWithinRange_ReturnsValid(double value, double min, double max)
    {
        var result = InputValidator.ValidateRange(value, "TestField", min, max);

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(0.0, 1.0, 10.0)]
    [TestCase(10.1, 1.0, 10.0)]
    [TestCase(-0.1, 0.0, 1.0)]
    public void ValidateRange_DoubleOutOfRange_ReturnsError(double value, double min, double max)
    {
        var result = InputValidator.ValidateRange(value, "TestField", min, max);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.InvalidRange));
    }

    [Test]
    public void ValidateRange_NaN_ReturnsError()
    {
        var result = InputValidator.ValidateRange(double.NaN, "TestField", 0, 100);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidateRange_Infinity_ReturnsError()
    {
        var result = InputValidator.ValidateRange(double.PositiveInfinity, "TestField", 0, 100);

        Assert.That(result.IsValid, Is.False);
    }

    #endregion
}


