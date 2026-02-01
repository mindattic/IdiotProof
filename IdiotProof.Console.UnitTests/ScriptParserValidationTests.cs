// ============================================================================
// StrategyScriptParser Validation and Error Tests
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for validation, error handling, and TryParse
/// Uses new IdiotScript syntax with period delimiters
/// </summary>
[TestFixture]
public class ScriptParserValidationTests
{
    #region Required Field Validation

    [Test]
    public void Parse_EmptyScript_ThrowsException()
    {
        Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse(""));
    }

    [Test]
    public void Parse_WhitespaceOnly_ThrowsException()
    {
        Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse("   "));
    }

    [Test]
    public void Parse_NullScript_ThrowsException()
    {
        Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse(null!));
    }

    [Test]
    public void Parse_MissingSymbol_ThrowsException()
    {
        var ex = Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse("QTY(10).TP($158)"));

        Assert.That(ex!.Message, Does.Contain("Symbol").IgnoreCase);
    }

    #endregion

    #region Unknown Command Handling

    [Test]
    public void Parse_UnknownCommand_ThrowsException()
    {
        var ex = Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse("SYM(AAPL).UNKNOWN_COMMAND(123)"));

        Assert.That(ex!.Message, Does.Contain("Unknown").IgnoreCase);
    }

    [Test]
    public void Parse_MalformedCommand_ThrowsException()
    {
        Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse("SYM(AAPL).QTYY(10)"));
    }

    #endregion

    #region TryParse Tests

    [Test]
    public void TryParse_ValidScript_ReturnsTrue()
    {
        var success = StrategyScriptParser.TryParse(
            "SYM(AAPL).QTY(10).TP($160)",
            out var strategy,
            out var error);

        Assert.That(success, Is.True);
        Assert.That(strategy, Is.Not.Null);
        Assert.That(error, Is.Null);
        Assert.That(strategy!.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void TryParse_InvalidScript_ReturnsFalse()
    {
        var success = StrategyScriptParser.TryParse(
            "QTY(10).TP($160)", // Missing symbol
            out var strategy,
            out var error);

        Assert.That(success, Is.False);
        Assert.That(strategy, Is.Null);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("Symbol").IgnoreCase);
    }

    [Test]
    public void TryParse_EmptyScript_ReturnsFalse()
    {
        var success = StrategyScriptParser.TryParse(
            "",
            out var strategy,
            out var error);

        Assert.That(success, Is.False);
        Assert.That(strategy, Is.Null);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void TryParse_UnknownCommand_ReturnsFalse()
    {
        var success = StrategyScriptParser.TryParse(
            "SYM(AAPL).BADCMD(123)",
            out var strategy,
            out var error);

        Assert.That(success, Is.False);
        Assert.That(strategy, Is.Null);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("Unknown").IgnoreCase);
    }

    #endregion

    #region TryParse with Default Symbol

    [Test]
    public void TryParse_WithDefaultSymbol_UsesDefault()
    {
        var success = StrategyScriptParser.TryParse(
            "QTY(10).TP($160)",
            out var strategy,
            out var error,
            defaultSymbol: "AAPL");

        Assert.That(success, Is.True);
        Assert.That(strategy, Is.Not.Null);
        Assert.That(error, Is.Null);
        Assert.That(strategy!.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void TryParse_WithDefaultSymbol_ScriptOverrides()
    {
        var success = StrategyScriptParser.TryParse(
            "SYM(NVDA).QTY(10)",
            out var strategy,
            out var error,
            defaultSymbol: "AAPL");

        Assert.That(success, Is.True);
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.Symbol, Is.EqualTo("NVDA")); // Script overrides default
    }

    #endregion

    #region Invalid Value Handling

    [TestCase("SYM(AAPL).QTY(abc)")]
    [TestCase("SYM(AAPL).QTY()")]
    public void Parse_InvalidQuantity_ThrowsException(string script)
    {
        Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse(script));
    }

    [TestCase("SYM(AAPL).TP(abc)")]
    [TestCase("SYM(AAPL).TP()")]
    public void Parse_InvalidTakeProfit_ThrowsException(string script)
    {
        Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse(script));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Parse_OnlySymbol_ValidMinimalStrategy()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL)");
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Name, Is.Not.Null);
    }

    [Test]
    public void Parse_DuplicateCommands_LastWins()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL).QTY(5).QTY(10)");
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
    }

    #endregion
}
