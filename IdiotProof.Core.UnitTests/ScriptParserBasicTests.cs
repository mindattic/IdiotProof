// ============================================================================
// StrategyScriptParser Basic Tests - Symbol, Quantity, Name, Direction
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for basic script commands: SYM, TICKER, QTY, NAME, BUY, SELL
/// Uses new IdiotScript syntax with period delimiters
/// </summary>
[TestFixture]
public class ScriptParserBasicTests
{
    #region Symbol Tests

    [TestCase("SYM(AAPL)")]
    [TestCase("sym(aapl)")]
    [TestCase("Sym(Aapl)")]
    public void Parse_Symbol_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [TestCase("TICKER(NVDA)")]
    [TestCase("ticker(nvda)")]
    [TestCase("Ticker(Nvda)")]
    public void Parse_Ticker_Alias_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
    }

    [TestCase("SYM(PLTR)", "PLTR")]
    [TestCase("SYM(TSLA)", "TSLA")]
    [TestCase("SYM(SPY)", "SPY")]
    [TestCase("SYM(QQQ)", "QQQ")]
    [TestCase("SYM(AMD)", "AMD")]
    [TestCase("SYM(MSFT)", "MSFT")]
    [TestCase("SYM(GOOG)", "GOOG")]
    [TestCase("SYM(META)", "META")]
    public void Parse_Symbol_VariousSymbols(string script, string expected)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_MissingSymbol_ThrowsException()
    {
        Assert.Throws<StrategyScriptException>(() => 
            StrategyScriptParser.Parse("QTY(10).TP($158)"));
    }

    #endregion

    #region Quantity Tests

    [TestCase("SYM(AAPL).QTY(1)", 1)]
    [TestCase("SYM(AAPL).QTY(10)", 10)]
    [TestCase("SYM(AAPL).QTY(100)", 100)]
    [TestCase("SYM(AAPL).QTY(1000)", 1000)]
    [TestCase("SYM(AAPL).qty(50)", 50)]
    [TestCase("SYM(AAPL).Qty(25)", 25)]
    public void Parse_Quantity_Values(string script, int expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_DefaultQuantity_IsOne()
    {
        var result = StrategyScriptParser.Parse("SYM(AAPL)");
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(1));
    }

    #endregion

    #region Name Tests

    [TestCase("SYM(AAPL).NAME(\"My Strategy\")", "My Strategy")]
    [TestCase("SYM(AAPL).NAME('Test Strategy')", "Test Strategy")]
    [TestCase("SYM(AAPL).name(\"Lowercase Name\")", "Lowercase Name")]
    [TestCase("SYM(AAPL).NAME(\"Strategy With Numbers 123\")", "Strategy With Numbers 123")]
    public void Parse_Name_Values(string script, string expected)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Name, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_DefaultName_UsesSymbol()
    {
        var result = StrategyScriptParser.Parse("SYM(PLTR)");
        Assert.That(result.Name, Does.Contain("PLTR"));
    }

    #endregion

    #region Direction Tests

    [TestCase("SYM(AAPL).ORDER()")]
    [TestCase("SYM(AAPL).ORDER(IS.LONG)")]
    [TestCase("SYM(AAPL).ORDER(LONG)")]
    [TestCase("SYM(AAPL).order()")]
    [TestCase("SYM(AAPL).Order(is.long)")]
    public void Parse_OrderLongDirection_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).ORDER(IS.SHORT)")]
    [TestCase("SYM(AAPL).ORDER(SHORT)")]
    [TestCase("SYM(AAPL).order(is.short)")]
    [TestCase("SYM(AAPL).Order(Short)")]
    public void Parse_OrderShortDirection_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).LONG")]
    [TestCase("SYM(AAPL).long")]
    [TestCase("SYM(AAPL).Long")]
    public void Parse_LongDirection_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).SHORT")]
    [TestCase("SYM(AAPL).short")]
    [TestCase("SYM(AAPL).Short")]
    public void Parse_ShortDirection_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region Description Tests

    [TestCase("SYM(AAPL).DESC(\"A description\")")]
    [TestCase("SYM(AAPL).desc('Another description')")]
    public void Parse_Description_Values(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result, Is.Not.Null);
    }

    #endregion
}


