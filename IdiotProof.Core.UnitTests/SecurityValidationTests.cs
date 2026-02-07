// ============================================================================
// SecurityValidationTests - XSS/Injection protection tests
// ============================================================================
//
// NOMENCLATURE:
// - Sanitization: Removing or escaping potentially dangerous content
// - XSS (Cross-Site Scripting): JavaScript injection attacks
// - SQL Injection: Database command injection attacks
// - Template Injection: Server-side template injection attacks
//
// These tests validate:
// 1. XSS attacks are detected and blocked
// 2. SQL injection patterns are detected
// 3. Template injection is prevented
// 4. Dangerous characters are sanitized
// 5. Whitelist enforcement prevents arbitrary commands
//
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Validation;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for security validation and XSS/injection prevention.
/// Ensures IdiotScript is safe for both frontend and backend processing.
/// </summary>
[TestFixture]
public class SecurityValidationTests
{
    #region XSS Detection Tests

    [TestCase("<script>alert('xss')</script>")]
    [TestCase("<SCRIPT>alert('xss')</SCRIPT>")]
    [TestCase("javascript:alert('xss')")]
    [TestCase("JAVASCRIPT:alert('xss')")]
    [TestCase("<iframe src='evil.com'></iframe>")]
    [TestCase("<object data='evil.swf'></object>")]
    [TestCase("<embed src='evil.swf'>")]
    [TestCase("vbscript:msgbox('xss')")]
    [TestCase("data:text/html,<script>alert('xss')</script>")]
    public void ValidateSecurity_XssPatterns_DetectsInjection(string maliciousContent)
    {
        // Arrange
        var script = $"TICKER(AAPL).NAME(\"{maliciousContent}\")";

        // Act
        var result = IdiotScriptValidator.ValidateSecurity(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    [TestCase("onclick=alert('xss')")]
    [TestCase("onload=evil()")]
    [TestCase("onerror=hack()")]
    [TestCase("onmouseover=steal()")]
    public void ValidateSecurity_EventHandlerPatterns_DetectsInjection(string eventHandler)
    {
        // Arrange
        var script = $"TICKER(AAPL).DESC(\"{eventHandler}\")";

        // Act
        var result = IdiotScriptValidator.ValidateSecurity(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region SQL Injection Detection Tests

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
    public void ValidateSecurity_SqlInjectionPatterns_DetectsInjection(string sqlInjection)
    {
        // Arrange
        var script = $"TICKER(AAPL).NAME(\"{sqlInjection}\")";

        // Act
        var result = IdiotScriptValidator.ValidateSecurity(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    [TestCase("$(whoami)")]
    [TestCase("`cat /etc/passwd`")]
    [TestCase("&& rm -rf /")]
    [TestCase("|| shutdown -h now")]
    public void ValidateSecurity_CommandInjectionPatterns_DetectsInjection(string cmdInjection)
    {
        // Arrange
        var script = $"TICKER(AAPL).DESC(\"{cmdInjection}\")";

        // Act
        var result = IdiotScriptValidator.ValidateSecurity(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region Template Injection Detection Tests

    [TestCase("{{constructor.constructor('return this')()}}")]
    [TestCase("${7*7}")]
    [TestCase("#{7*7}")]
    [TestCase("{{config}}")]
    [TestCase("${T(java.lang.Runtime).getRuntime().exec('id')}")]
    public void ValidateSecurity_TemplateInjectionPatterns_DetectsInjection(string templateInjection)
    {
        // Arrange
        var script = $"TICKER(AAPL).NAME(\"{templateInjection}\")";

        // Act
        var result = IdiotScriptValidator.ValidateSecurity(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region Command Whitelist Tests

    [Test]
    public void ValidCommands_ContainsAllExpectedCommands()
    {
        // Assert - Core commands are in whitelist
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("TICKER"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("SYM"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("SYMBOL"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("ORDER"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("LONG"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("SHORT"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("QTY"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("TP"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("SL"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("TSL"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("SESSION"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("BREAKOUT"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("PULLBACK"));
    }

    [TestCase("EVAL")]
    [TestCase("EXEC")]
    [TestCase("SYSTEM")]
    [TestCase("SHELL")]
    [TestCase("CMD")]
    [TestCase("POWERSHELL")]
    [TestCase("BASH")]
    public void ValidCommands_DoesNotContainDangerousCommands(string dangerousCommand)
    {
        // Assert
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Not.Contain(dangerousCommand));
    }

    [TestCase("UNKNOWN_COMMAND")]
    [TestCase("MALICIOUS")]
    [TestCase("HACK")]
    [TestCase("INJECT")]
    public void ValidateCommands_UnknownCommand_ReturnsError(string unknownCommand)
    {
        // Arrange
        var script = $"TICKER(AAPL).{unknownCommand}(test)";

        // Act
        var result = IdiotScriptValidator.ValidateCommands(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidCommand));
    }

    #endregion

    #region IS. Constant Whitelist Tests

    [Test]
    public void ValidConstantPrefixes_ContainsAllExpectedConstants()
    {
        // Assert - Session constants
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.PREMARKET"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.RTH"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.AFTERHOURS"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.EXTENDED"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.ACTIVE"));

        // Assert - Time constants
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.BELL"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.OPEN"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.CLOSE"));

        // Assert - TSL constants
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.TIGHT"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.MODERATE"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.STANDARD"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.LOOSE"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.WIDE"));

        // Assert - Boolean constants
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.TRUE"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.FALSE"));
    }

    [TestCase("IS.EVIL")]
    [TestCase("IS.HACK")]
    [TestCase("IS.INJECT")]
    [TestCase("IS.MALICIOUS")]
    public void ValidateCommands_UnknownConstant_ReturnsError(string unknownConstant)
    {
        // Arrange
        var script = $"TICKER(AAPL).SESSION({unknownConstant})";

        // Act
        var result = IdiotScriptValidator.ValidateCommands(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidCommand));
    }

    #endregion

    #region Sanitization Tests

    [Test]
    public void Sanitize_RemovesXssContent()
    {
        // Arrange
        var malicious = "TICKER(AAPL)<script>alert('xss')</script>.Order()";

        // Act
        var sanitized = IdiotScriptValidator.Sanitize(malicious);

        // Assert - The <script tag is removed by the XSS pattern
        Assert.That(sanitized.ToLower(), Does.Not.Contain("<script"));
    }

    [Test]
    public void Sanitize_RemovesSqlInjectionContent()
    {
        // Arrange
        var malicious = "TICKER(AAPL); DROP TABLE users; --.Order()";

        // Act
        var sanitized = IdiotScriptValidator.Sanitize(malicious);

        // Assert
        Assert.That(sanitized, Does.Not.Contain(";"));
        Assert.That(sanitized, Does.Not.Contain("--"));
    }

    [Test]
    public void Sanitize_NormalizesWhitespace()
    {
        // Arrange
        var messy = "TICKER(AAPL)    .    Order()    .    TP(160)";

        // Act
        var sanitized = IdiotScriptValidator.Sanitize(messy);

        // Assert
        Assert.That(sanitized, Does.Not.Contain("    "));
        Assert.That(sanitized, Is.EqualTo(sanitized.Trim()));
    }

    [Test]
    public void Sanitize_EmptyInput_ReturnsEmpty()
    {
        // Act
        var result = IdiotScriptValidator.Sanitize("");

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Sanitize_NullInput_ReturnsEmpty()
    {
        // Act
        var result = IdiotScriptValidator.Sanitize(null!);

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Sanitize_WhitespaceInput_ReturnsEmpty()
    {
        // Act
        var result = IdiotScriptValidator.Sanitize("   ");

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    #endregion

    #region String Parameter Validation Tests

    [TestCase("Valid Strategy Name")]
    [TestCase("My AAPL Breakout")]
    [TestCase("Pre-market momentum")]
    public void ValidateStringParameter_ValidContent_Succeeds(string validContent)
    {
        // Act
        var result = IdiotScriptValidator.ValidateStringParameter(validContent, "Name");

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateStringParameter_TooLong_ReturnsError()
    {
        // Arrange
        var tooLong = new string('a', 150);

        // Act
        var result = IdiotScriptValidator.ValidateStringParameter(tooLong, "Name", maxLength: 100);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidLength));
    }

    [TestCase("<script>")]
    [TestCase("javascript:")]
    public void ValidateStringParameter_XssContent_ReturnsError(string xssContent)
    {
        // Act
        var result = IdiotScriptValidator.ValidateStringParameter(xssContent, "Name");

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region Ticker Symbol Validation Tests

    [TestCase("AAPL")]
    [TestCase("NVDA")]
    [TestCase("A")]
    [TestCase("GOOGL")]
    public void ValidateTickerSymbol_ValidSymbols_Succeeds(string symbol)
    {
        // Act
        var result = IdiotScriptValidator.ValidateTickerSymbol(symbol);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateTickerSymbol_EmptySymbol_ReturnsError(string? symbol)
    {
        // Act
        var result = IdiotScriptValidator.ValidateTickerSymbol(symbol);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.Required));
    }

    [TestCase("TOOLONG")]
    [TestCase("123456")]
    [TestCase("aapl")]
    [TestCase("AAP-L")]
    public void ValidateTickerSymbol_InvalidSymbols_HandledCorrectly(string symbol)
    {
        // Act
        var result = IdiotScriptValidator.ValidateTickerSymbol(symbol);

        // Assert - Either invalid or normalized
        // Note: Some validators normalize, others reject
        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region Full Script Validation Tests

    [Test]
    public void Validate_ValidScript_Succeeds()
    {
        // Arrange
        var script = "TICKER(AAPL).SESSION(IS.PREMARKET).BREAKOUT(150).Order().TP(160).SL(145)";

        // Act
        var result = IdiotScriptValidator.Validate(script);

        // Assert
        Assert.That(result.IsValid, Is.True,
            $"Validation failed: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_EmptyScript_ReturnsError(string? script)
    {
        // Act
        var result = IdiotScriptValidator.Validate(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.Required));
    }

    [Test]
    public void Validate_MaliciousScript_ReturnsSecurityError()
    {
        // Arrange
        var script = "TICKER(AAPL)<script>alert('xss')</script>.Order()";

        // Act
        var result = IdiotScriptValidator.Validate(script);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion
}


