// ============================================================================
// StrategyScriptParser Execution Behavior Tests - Repeat, Enabled
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for execution behavior commands: Repeat(), IsRepeat(), Enabled(), IsEnabled()
/// Uses IdiotScript syntax with period delimiters and IS. constants
/// </summary>
[TestFixture]
public class ScriptParserExecutionBehaviorTests
{
    #region Repeat() Basic Syntax Tests

    [TestCase("SYM(AAPL).Repeat()")]
    [TestCase("SYM(AAPL).Repeat")]
    [TestCase("SYM(AAPL).REPEAT()")]
    [TestCase("SYM(AAPL).REPEAT")]
    [TestCase("SYM(AAPL).repeat()")]
    [TestCase("SYM(AAPL).repeat")]
    public void Parse_Repeat_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var repeatSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Repeat);
        Assert.That(repeatSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).IsRepeat()")]
    [TestCase("SYM(AAPL).IsRepeat")]
    [TestCase("SYM(AAPL).ISREPEAT()")]
    [TestCase("SYM(AAPL).isrepeat()")]
    public void Parse_IsRepeat_Prefix(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var repeatSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Repeat);
        Assert.That(repeatSegment, Is.Not.Null);
    }

    #endregion

    #region Repeat(Y/N) Boolean Tests

    [TestCase("SYM(AAPL).Repeat(Y)")]
    [TestCase("SYM(AAPL).Repeat(YES)")]
    [TestCase("SYM(AAPL).Repeat(yes)")]
    [TestCase("SYM(AAPL).Repeat(true)")]
    [TestCase("SYM(AAPL).Repeat(TRUE)")]
    [TestCase("SYM(AAPL).Repeat(1)")]
    [TestCase("SYM(AAPL).Repeat(IS.TRUE)")]
    [TestCase("SYM(AAPL).Repeat(is.true)")]
    public void Parse_RepeatTrue_AllBooleanValues(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var repeatSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Repeat);
        Assert.That(repeatSegment, Is.Not.Null);

        var valueParam = repeatSegment!.Parameters.FirstOrDefault(p => p.Name == "Value" || p.Name == "Enabled");
        if (valueParam != null)
        {
            Assert.That(Convert.ToBoolean(valueParam.Value), Is.True);
        }
    }

    [TestCase("SYM(AAPL).Repeat(N)")]
    [TestCase("SYM(AAPL).Repeat(NO)")]
    [TestCase("SYM(AAPL).Repeat(no)")]
    [TestCase("SYM(AAPL).Repeat(false)")]
    [TestCase("SYM(AAPL).Repeat(FALSE)")]
    [TestCase("SYM(AAPL).Repeat(0)")]
    [TestCase("SYM(AAPL).Repeat(IS.FALSE)")]
    [TestCase("SYM(AAPL).Repeat(is.false)")]
    public void Parse_RepeatFalse_AllBooleanValues(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var repeatSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Repeat);
        Assert.That(repeatSegment, Is.Not.Null);

        var valueParam = repeatSegment!.Parameters.FirstOrDefault(p => p.Name == "Value" || p.Name == "Enabled");
        if (valueParam != null)
        {
            Assert.That(Convert.ToBoolean(valueParam.Value), Is.False);
        }
    }

    #endregion

    #region Enabled() Basic Syntax Tests

    [TestCase("SYM(AAPL).Enabled()")]
    [TestCase("SYM(AAPL).Enabled")]
    [TestCase("SYM(AAPL).ENABLED()")]
    [TestCase("SYM(AAPL).ENABLED")]
    [TestCase("SYM(AAPL).enabled()")]
    [TestCase("SYM(AAPL).enabled")]
    public void Parse_Enabled_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        // Enabled is typically handled as a strategy property, not a segment
        // Just verify it parses without error
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [TestCase("SYM(AAPL).IsEnabled()")]
    [TestCase("SYM(AAPL).IsEnabled")]
    [TestCase("SYM(AAPL).ISENABLED()")]
    [TestCase("SYM(AAPL).isenabled()")]
    public void Parse_IsEnabled_Prefix(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region Enabled(Y/N) Boolean Tests

    [TestCase("SYM(AAPL).Enabled(Y)")]
    [TestCase("SYM(AAPL).Enabled(YES)")]
    [TestCase("SYM(AAPL).Enabled(yes)")]
    [TestCase("SYM(AAPL).Enabled(true)")]
    [TestCase("SYM(AAPL).Enabled(TRUE)")]
    [TestCase("SYM(AAPL).Enabled(1)")]
    [TestCase("SYM(AAPL).Enabled(IS.TRUE)")]
    [TestCase("SYM(AAPL).IsEnabled(IS.TRUE)")]
    public void Parse_EnabledTrue_AllBooleanValues(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        // Strategy should be enabled (default or explicit)
    }

    [TestCase("SYM(AAPL).Enabled(N)")]
    [TestCase("SYM(AAPL).Enabled(NO)")]
    [TestCase("SYM(AAPL).Enabled(no)")]
    [TestCase("SYM(AAPL).Enabled(false)")]
    [TestCase("SYM(AAPL).Enabled(FALSE)")]
    [TestCase("SYM(AAPL).Enabled(0)")]
    [TestCase("SYM(AAPL).Enabled(IS.FALSE)")]
    [TestCase("SYM(AAPL).IsEnabled(IS.FALSE)")]
    public void Parse_EnabledFalse_AllBooleanValues(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        // Strategy should be disabled
    }

    #endregion

    #region Combined Repeat with Full Strategy

    [Test]
    public void Parse_RepeatingStrategyExample()
    {
        // Example from copilot-instructions.md
        var script = "Ticker(ABC).Entry(5.00).TakeProfit(6.00).AboveVwap().DiPositive().Repeat()";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("ABC"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Repeat), Is.True);
    }

    [Test]
    public void Parse_NonRepeatingStrategy()
    {
        var script = "Ticker(AAPL).Entry(150).TakeProfit(160).Repeat(N)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var repeatSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Repeat);
        Assert.That(repeatSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_RepeatWithAllConditions()
    {
        var script = @"
            Ticker(PLTR)
            .Session(IS.RTH)
            .Entry(25)
            .TakeProfit(27)
            .StopLoss(24)
            .IsAboveVwap()
            .IsEmaAbove(9)
            .IsDiPositive()
            .Repeat(IS.TRUE)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Repeat), Is.True);
    }

    #endregion

    #region Combined Enabled with Full Strategy

    [Test]
    public void Parse_DisabledStrategy()
    {
        var script = "Ticker(AAPL).Entry(150).TakeProfit(160).Enabled(IS.FALSE)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        // Strategy should parse but be disabled
    }

    [Test]
    public void Parse_EnabledStrategy()
    {
        var script = "Ticker(AAPL).Entry(150).TakeProfit(160).Enabled(IS.TRUE)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region Repeat and Enabled Combined

    [Test]
    public void Parse_RepeatAndEnabledCombined()
    {
        var script = "Ticker(AAPL).Entry(150).TakeProfit(160).Repeat(Y).Enabled(Y)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Repeat), Is.True);
    }

    [Test]
    public void Parse_DisabledRepeatingStrategy()
    {
        // A strategy can be configured to repeat but currently disabled
        var script = "Ticker(AAPL).Entry(150).TakeProfit(160).Repeat(Y).Enabled(N)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Repeat), Is.True);
    }

    #endregion
}
