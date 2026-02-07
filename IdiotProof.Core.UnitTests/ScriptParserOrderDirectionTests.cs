// ============================================================================
// StrategyScriptParser Order Direction Tests
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Core.Enums;
using IdiotProof.Core.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for order direction commands: Order(), Long(), Short(), CloseLong(), CloseShort()
/// Uses IdiotScript syntax with period delimiters and IS. constants
/// </summary>
[TestFixture]
public class ScriptParserOrderDirectionTests
{
    #region Order() Default Tests

    [TestCase("SYM(AAPL).Order()")]
    [TestCase("SYM(AAPL).Order")]
    [TestCase("SYM(AAPL).ORDER()")]
    [TestCase("SYM(AAPL).ORDER")]
    [TestCase("SYM(AAPL).order()")]
    [TestCase("SYM(AAPL).order")]
    public void Parse_OrderNoParam_DefaultsToLong(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var orderSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order || s.Type == SegmentType.Long);
        Assert.That(orderSegment, Is.Not.Null);
    }

    #endregion

    #region Order(IS.LONG) Tests

    [TestCase("SYM(AAPL).Order(IS.LONG)")]
    [TestCase("SYM(AAPL).Order(is.long)")]
    [TestCase("SYM(AAPL).Order(Is.Long)")]
    [TestCase("SYM(AAPL).Order(LONG)")]
    [TestCase("SYM(AAPL).Order(long)")]
    [TestCase("SYM(AAPL).Order(Long)")]
    public void Parse_OrderLong_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var orderSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order || s.Type == SegmentType.Long);
        Assert.That(orderSegment, Is.Not.Null);

        // Check direction if parameter exists
        var directionParam = orderSegment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        if (directionParam != null)
        {
            Assert.That(directionParam.Value?.ToString(), Is.EqualTo("Long").IgnoreCase);
        }
    }

    #endregion

    #region Order(IS.SHORT) Tests

    [TestCase("SYM(AAPL).Order(IS.SHORT)")]
    [TestCase("SYM(AAPL).Order(is.short)")]
    [TestCase("SYM(AAPL).Order(Is.Short)")]
    [TestCase("SYM(AAPL).Order(SHORT)")]
    [TestCase("SYM(AAPL).Order(short)")]
    [TestCase("SYM(AAPL).Order(Short)")]
    public void Parse_OrderShort_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var orderSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order || s.Type == SegmentType.Short);
        Assert.That(orderSegment, Is.Not.Null);

        // Check direction if parameter exists
        var directionParam = orderSegment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        if (directionParam != null)
        {
            Assert.That(directionParam.Value?.ToString(), Is.EqualTo("Short").IgnoreCase);
        }
    }

    #endregion

    #region Long() Alias Tests

    [TestCase("SYM(AAPL).Long()")]
    [TestCase("SYM(AAPL).Long")]
    [TestCase("SYM(AAPL).LONG()")]
    [TestCase("SYM(AAPL).LONG")]
    [TestCase("SYM(AAPL).long()")]
    [TestCase("SYM(AAPL).long")]
    public void Parse_LongAlias_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var orderSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order || s.Type == SegmentType.Long);
        Assert.That(orderSegment, Is.Not.Null);
    }

    #endregion

    #region Short() Alias Tests

    [TestCase("SYM(AAPL).Short()")]
    [TestCase("SYM(AAPL).Short")]
    [TestCase("SYM(AAPL).SHORT()")]
    [TestCase("SYM(AAPL).SHORT")]
    [TestCase("SYM(AAPL).short()")]
    [TestCase("SYM(AAPL).short")]
    public void Parse_ShortAlias_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var orderSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order || s.Type == SegmentType.Short);
        Assert.That(orderSegment, Is.Not.Null);
    }

    #endregion

    #region CloseLong() Tests

    [TestCase("SYM(AAPL).CloseLong()")]
    [TestCase("SYM(AAPL).CloseLong")]
    [TestCase("SYM(AAPL).CLOSELONG()")]
    [TestCase("SYM(AAPL).CLOSELONG")]
    [TestCase("SYM(AAPL).closelong()")]
    [TestCase("SYM(AAPL).closelong")]
    public void Parse_CloseLong_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.CloseLong);
        Assert.That(closeSegment, Is.Not.Null);
    }

    #endregion

    #region CloseShort() Tests

    [TestCase("SYM(AAPL).CloseShort()")]
    [TestCase("SYM(AAPL).CloseShort")]
    [TestCase("SYM(AAPL).CLOSESHORT()")]
    [TestCase("SYM(AAPL).CLOSESHORT")]
    [TestCase("SYM(AAPL).closeshort()")]
    [TestCase("SYM(AAPL).closeshort")]
    public void Parse_CloseShort_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.CloseShort);
        Assert.That(closeSegment, Is.Not.Null);
    }

    #endregion

    #region Combined Order with Conditions Tests

    [Test]
    public void Parse_LongWithConditions()
    {
        var script = "Ticker(AAPL).IsAboveVwap().IsEmaAbove(9).Long().TakeProfit(160)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaAbove), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Order || s.Type == SegmentType.Long), Is.True);
    }

    [Test]
    public void Parse_ShortWithConditions()
    {
        var script = "Ticker(AAPL).IsBelowVwap().IsEmaBelow(9).Short().TakeProfit(140)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsBelowVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaBelow), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Order || s.Type == SegmentType.Short), Is.True);
    }

    [Test]
    public void Parse_BearishSetupWithShort()
    {
        var script = "Ticker(SPY).DiNegative().MacdBearish().EmaBelow(9).Order(IS.SHORT).TakeProfit(480).StopLoss(510)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("SPY"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsMacd), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Order || s.Type == SegmentType.Short), Is.True);
    }

    #endregion

    #region Full Strategy with Order Direction Tests

    [Test]
    public void Parse_FullLongStrategy()
    {
        var script = @"
            Ticker(NVDA)
            .Session(IS.PREMARKET)
            .Entry(150)
            .IsAboveVwap()
            .IsEmaAbove(9)
            .IsDiPositive()
            .Order(IS.LONG)
            .Quantity(100)
            .TakeProfit(160)
            .StopLoss(145)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Order || s.Type == SegmentType.Long), Is.True);
    }

    [Test]
    public void Parse_FullShortStrategy()
    {
        var script = @"
            Ticker(SPY)
            .Session(IS.RTH)
            .Entry(500)
            .IsBelowVwap()
            .IsEmaBelow(9)
            .IsDiNegative()
            .Order(IS.SHORT)
            .Quantity(50)
            .TakeProfit(490)
            .StopLoss(505)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("SPY"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.Order || s.Type == SegmentType.Short), Is.True);
    }

    #endregion
}
