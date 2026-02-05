// ============================================================================
// IdiotScriptValidatorTests - Tests for IdiotScript security and syntax validation
// ============================================================================
//
// These tests validate:
// 1. XSS attacks are detected and blocked
// 2. SQL injection patterns are detected
// 3. Template injection is prevented
// 4. Whitelist enforcement prevents arbitrary commands
// 5. Syntax validation (parentheses balance, etc.)
//
// ============================================================================

using IdiotProof.Shared.Scripting;
using IdiotProof.Shared.Validation;

namespace IdiotProof.Shared.UnitTests.Validation;

/// <summary>
/// Tests for IdiotScriptValidator - security and syntax validation.
/// </summary>
[TestFixture]
public class IdiotScriptValidatorTests
{
    #region Security Validation - XSS Detection

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
        var script = $"TICKER(AAPL).NAME(\"{maliciousContent}\")";

        var result = IdiotScriptValidator.ValidateSecurity(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    [TestCase("onclick=alert('xss')")]
    [TestCase("onload=evil()")]
    [TestCase("onerror=hack()")]
    [TestCase("onmouseover=steal()")]
    public void ValidateSecurity_EventHandlerPatterns_DetectsInjection(string eventHandler)
    {
        var script = $"TICKER(AAPL).DESC(\"{eventHandler}\")";

        var result = IdiotScriptValidator.ValidateSecurity(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region Security Validation - SQL Injection Detection

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
        var script = $"TICKER(AAPL).NAME(\"{sqlInjection}\")";

        var result = IdiotScriptValidator.ValidateSecurity(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    [TestCase("$(whoami)")]
    [TestCase("`cat /etc/passwd`")]
    [TestCase("&& rm -rf /")]
    [TestCase("|| shutdown -h now")]
    public void ValidateSecurity_CommandInjectionPatterns_DetectsInjection(string cmdInjection)
    {
        var script = $"TICKER(AAPL).DESC(\"{cmdInjection}\")";

        var result = IdiotScriptValidator.ValidateSecurity(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region Security Validation - Template Injection Detection

    [TestCase("{{constructor.constructor('return this')()}}")]
    [TestCase("${7*7}")]
    [TestCase("#{7*7}")]
    [TestCase("{{config}}")]
    [TestCase("${T(java.lang.Runtime).getRuntime().exec('id')}")]
    public void ValidateSecurity_TemplateInjectionPatterns_DetectsInjection(string templateInjection)
    {
        var script = $"TICKER(AAPL).NAME(\"{templateInjection}\")";

        var result = IdiotScriptValidator.ValidateSecurity(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InjectionDetected));
    }

    #endregion

    #region Security Validation - Clean Scripts

    [Test]
    public void ValidateSecurity_CleanScript_ReturnsValid()
    {
        var script = "TICKER(AAPL).QTY(10).TP(160).SL(145)";

        var result = IdiotScriptValidator.ValidateSecurity(script);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateSecurity_ScriptWithConstants_ReturnsValid()
    {
        var script = "TICKER(AAPL).SESSION(IS.PREMARKET).TSL(IS.MODERATE).CLOSEPOSITION(IS.BELL)";

        var result = IdiotScriptValidator.ValidateSecurity(script);

        Assert.That(result.IsValid, Is.True);
    }

    #endregion

    #region Syntax Validation - Parentheses Balance

    [Test]
    public void ValidateSyntax_BalancedParentheses_ReturnsValid()
    {
        var script = "TICKER(AAPL).QTY(10).TP(160)";

        var result = IdiotScriptValidator.ValidateSyntax(script);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateSyntax_MissingCloseParen_ReturnsError()
    {
        var script = "TICKER(AAPL.QTY(10)";

        var result = IdiotScriptValidator.ValidateSyntax(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidSyntax));
    }

    [Test]
    public void ValidateSyntax_ExtraCloseParen_ReturnsError()
    {
        var script = "TICKER(AAPL)).QTY(10)";

        var result = IdiotScriptValidator.ValidateSyntax(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidSyntax));
    }

    [Test]
    public void ValidateSyntax_ConsecutivePeriods_ReturnsError()
    {
        var script = "TICKER(AAPL)..QTY(10)";

        var result = IdiotScriptValidator.ValidateSyntax(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidSyntax));
    }

    [Test]
    public void ValidateSyntax_ConsecutiveCommas_ReturnsError()
    {
        var script = "TICKER(AAPL).EMABETWEEN(9,,21)";

        var result = IdiotScriptValidator.ValidateSyntax(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidSyntax));
    }

    #endregion

    #region Command Whitelist Validation

    [Test]
    public void ValidCommands_ContainsAllExpectedCommands()
    {
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("TICKER"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("SYM"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("SYMBOL"));
        // Note: BUY and SELL are NOT valid commands - use Order(IS.LONG), Order(IS.SHORT), Long(), or Short()
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
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("ABOVEVWAP"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("BELOWVWAP"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("EMAABOVE"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("EMABELOW"));
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Contain("EMABETWEEN"));
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
        Assert.That(IdiotScriptValidator.ValidCommands, Does.Not.Contain(dangerousCommand));
    }

    [TestCase("UNKNOWN_COMMAND")]
    [TestCase("MALICIOUS")]
    [TestCase("HACK")]
    [TestCase("INJECT")]
    public void ValidateCommands_UnknownCommand_ReturnsError(string unknownCommand)
    {
        var script = $"TICKER(AAPL).{unknownCommand}(test)";

        var result = IdiotScriptValidator.ValidateCommands(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidCommand));
    }

    #endregion

    #region IS. Constant Whitelist Validation

    [Test]
    public void ValidConstantPrefixes_ContainsSessionConstants()
    {
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.PREMARKET"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.RTH"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.AFTERHOURS"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.EXTENDED"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.ACTIVE"));
    }

    [Test]
    public void ValidConstantPrefixes_ContainsTimeConstants()
    {
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.BELL"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.PREMARKET.BELL"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.OPEN"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.CLOSE"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.EOD"));
    }

    [Test]
    public void ValidConstantPrefixes_ContainsTslConstants()
    {
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.TIGHT"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.MODERATE"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.STANDARD"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.LOOSE"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.WIDE"));
    }

    [Test]
    public void ValidConstantPrefixes_ContainsBooleanConstants()
    {
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.TRUE"));
        Assert.That(IdiotScriptValidator.ValidConstantPrefixes, Does.Contain("IS.FALSE"));
    }

    [TestCase("IS.UNKNOWN")]
    [TestCase("IS.EVIL")]
    [TestCase("IS.HACK")]
    public void ValidateCommands_UnknownConstant_ReturnsError(string unknownConstant)
    {
        var script = $"TICKER(AAPL).SESSION({unknownConstant})";

        var result = IdiotScriptValidator.ValidateCommands(script);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<ValidationError>(e => e.Code == ValidationCodes.InvalidCommand));
    }

    #endregion

    #region Comprehensive Validation

    [Test]
    public void Validate_NullScript_ReturnsRequired()
    {
        var result = IdiotScriptValidator.Validate(null);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.Required));
    }

    [Test]
    public void Validate_EmptyScript_ReturnsRequired()
    {
        var result = IdiotScriptValidator.Validate("");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.Required));
    }

    [Test]
    public void Validate_WhitespaceScript_ReturnsRequired()
    {
        var result = IdiotScriptValidator.Validate("   ");

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(ValidationCodes.Required));
    }

    [Test]
    public void Validate_ValidScript_ReturnsValid()
    {
        var script = "TICKER(AAPL).QTY(10).TP(160).SL(145).BREAKOUT()";

        var result = IdiotScriptValidator.Validate(script);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_ScriptWithAllConditions_ReturnsValid()
    {
        var script = "TICKER(NVDA).SESSION(IS.PREMARKET).QTY(1).ENTRY(200).TP(210).SL(190).TSL(IS.MODERATE).BREAKOUT().PULLBACK().ABOVEVWAP().EMAABOVE(9).CLOSEPOSITION(IS.BELL)";

        var result = IdiotScriptValidator.Validate(script);

        Assert.That(result.IsValid, Is.True);
    }

    #endregion
}


