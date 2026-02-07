// ============================================================================
// StrategyScriptParser Session and Close Tests
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Enums;
using IdiotProof.Core.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for SESSION and CLOSE commands
/// Uses new IdiotScript syntax with period delimiters and IS. constants
/// </summary>
[TestFixture]
public class ScriptParserSessionTests
{
    #region Session Tests

    [TestCase("SYM(AAPL).SESSION(PreMarket)")]
    [TestCase("SYM(AAPL).SESSION(premarket)")]
    [TestCase("SYM(AAPL).session(PreMarket)")]
    [TestCase("SYM(AAPL).SESSION(IS.PREMARKET)")]
    public void Parse_SessionPreMarket_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).SESSION(RTH)")]
    [TestCase("SYM(AAPL).SESSION(rth)")]
    [TestCase("SYM(AAPL).session(RTH)")]
    [TestCase("SYM(AAPL).SESSION(IS.RTH)")]
    public void Parse_SessionRTH_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).SESSION(AfterHours)")]
    [TestCase("SYM(AAPL).SESSION(afterhours)")]
    [TestCase("SYM(AAPL).session(AfterHours)")]
    [TestCase("SYM(AAPL).SESSION(IS.AFTERHOURS)")]
    public void Parse_SessionAfterHours_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).SESSION(PreMarketEndEarly)")]
    public void Parse_SessionVariants(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
    }

    #endregion

    #region Close Tests

    [TestCase("SYM(AAPL).CLOSE(Ending)")]
    [TestCase("SYM(AAPL).CLOSE(ending)")]
    [TestCase("SYM(AAPL).close(Ending)")]
    [TestCase("SYM(AAPL).CLOSE(IS.BELL)")]
    public void Parse_CloseEnding_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).CLOSE(9:20)")]
    [TestCase("SYM(AAPL).CLOSE(9:30)")]
    [TestCase("SYM(AAPL).CLOSE(15:55)")]
    [TestCase("SYM(AAPL).CLOSE(16:00)")]
    public void Parse_CloseTime_Values(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).ExitStrategy(IS.BELL).IsProfitable()")]
    [TestCase("SYM(AAPL).EXITSTRATEGY(IS.BELL).ISPROFITABLE()")]
    [TestCase("SYM(AAPL).exitstrategy(is.bell).isprofitable()")]
    [TestCase("SYM(AAPL).ExitStrategy(IS.BELL).Profitable()")]
    public void Parse_CloseOnlyIfProfitable_CaseInsensitive(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);

        // Check for IsProfitable segment (single-responsibility pattern)
        var profitableSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsProfitable);
        Assert.That(profitableSegment, Is.Not.Null);
    }

    [TestCase("SYM(AAPL).ExitStrategy(9:29).IsProfitable()")]
    public void Parse_CloseTimeWithProfitable(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);
    }

    #endregion

    #region Session and Close Combined

    [Test]
    public void Parse_SessionAndClose_Combined()
    {
        var result = StrategyScriptParser.Parse(
            "SYM(PLTR).SESSION(PreMarketEndEarly).ExitStrategy(9:29).IsProfitable()");

        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);

        Assert.That(sessionSegment, Is.Not.Null);
        Assert.That(closeSegment, Is.Not.Null);
    }

    #endregion
}


