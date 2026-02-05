// ============================================================================
// StrategyScriptParser Order Configuration Tests
// ============================================================================

using IdiotProof.Console.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for order configuration commands: TimeInForce, OutsideRTH, AllOrNone, OrderType
/// Uses IdiotScript syntax with period delimiters
/// </summary>
[TestFixture]
public class ScriptParserOrderConfigTests
{
    #region TimeInForce Tests

    [TestCase("SYM(AAPL).TimeInForce(DAY)", "DAY")]
    [TestCase("SYM(AAPL).TimeInForce(day)", "DAY")]
    [TestCase("SYM(AAPL).TimeInForce(Day)", "DAY")]
    [TestCase("SYM(AAPL).TIMEINFORCE(DAY)", "DAY")]
    [TestCase("SYM(AAPL).timeinforce(day)", "DAY")]
    public void Parse_TimeInForceDay_AllSyntax(string script, string expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var tifSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.TimeInForce);
        Assert.That(tifSegment, Is.Not.Null);

        var valueParam = tifSegment!.Parameters.FirstOrDefault(p => p.Name == "Value");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(valueParam!.Value?.ToString(), Is.EqualTo(expected).IgnoreCase);
    }

    [TestCase("SYM(AAPL).TimeInForce(GTC)", "GoodTillCancel")]
    [TestCase("SYM(AAPL).TimeInForce(gtc)", "GoodTillCancel")]
    public void Parse_TimeInForceGTC_AllSyntax(string script, string expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var tifSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.TimeInForce);
        Assert.That(tifSegment, Is.Not.Null);

        var valueParam = tifSegment!.Parameters.FirstOrDefault(p => p.Name == "Value");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(valueParam!.Value?.ToString(), Is.EqualTo(expected).IgnoreCase);
    }

    [TestCase("SYM(AAPL).TimeInForce(IOC)", "ImmediateOrCancel")]
    [TestCase("SYM(AAPL).TimeInForce(FOK)", "FillOrKill")]
    public void Parse_TimeInForce_OtherValues(string script, string expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var tifSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.TimeInForce);
        Assert.That(tifSegment, Is.Not.Null);

        var valueParam = tifSegment!.Parameters.FirstOrDefault(p => p.Name == "Value");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(valueParam!.Value?.ToString(), Is.EqualTo(expected).IgnoreCase);
    }

    #endregion

    #region OutsideRTH Tests

    [TestCase("SYM(AAPL).OutsideRTH(true)")]
    [TestCase("SYM(AAPL).OutsideRTH(TRUE)")]
    [TestCase("SYM(AAPL).OutsideRTH(Y)")]
    [TestCase("SYM(AAPL).OutsideRTH(YES)")]
    [TestCase("SYM(AAPL).OutsideRTH(1)")]
    [TestCase("SYM(AAPL).OUTSIDERTH(true)")]
    [TestCase("SYM(AAPL).outsiderth(true)")]
    public void Parse_OutsideRTHTrue_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var rthSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.OutsideRTH);
        Assert.That(rthSegment, Is.Not.Null);

        var valueParam = rthSegment!.Parameters.FirstOrDefault(p => p.Name == "Value" || p.Name == "Enabled");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(Convert.ToBoolean(valueParam!.Value), Is.True);
    }

    [TestCase("SYM(AAPL).OutsideRTH(false)")]
    [TestCase("SYM(AAPL).OutsideRTH(FALSE)")]
    [TestCase("SYM(AAPL).OutsideRTH(N)")]
    [TestCase("SYM(AAPL).OutsideRTH(NO)")]
    [TestCase("SYM(AAPL).OutsideRTH(0)")]
    public void Parse_OutsideRTHFalse_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var rthSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.OutsideRTH);
        Assert.That(rthSegment, Is.Not.Null);

        var valueParam = rthSegment!.Parameters.FirstOrDefault(p => p.Name == "Value" || p.Name == "Enabled");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(Convert.ToBoolean(valueParam!.Value), Is.False);
    }

    [TestCase("SYM(AAPL).OutsideRTH(IS.TRUE)")]
    [TestCase("SYM(AAPL).OutsideRTH(is.true)")]
    public void Parse_OutsideRTH_ISConstants(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var rthSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.OutsideRTH);
        Assert.That(rthSegment, Is.Not.Null);

        var valueParam = rthSegment!.Parameters.FirstOrDefault(p => p.Name == "Value" || p.Name == "Enabled");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(Convert.ToBoolean(valueParam!.Value), Is.True);
    }

    #endregion

    #region AllOrNone Tests

    [TestCase("SYM(AAPL).AllOrNone(true)")]
    [TestCase("SYM(AAPL).AllOrNone(TRUE)")]
    [TestCase("SYM(AAPL).AllOrNone(Y)")]
    [TestCase("SYM(AAPL).AllOrNone(YES)")]
    [TestCase("SYM(AAPL).ALLORNONE(true)")]
    [TestCase("SYM(AAPL).allornone(true)")]
    public void Parse_AllOrNoneTrue_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var aonSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AllOrNone);
        Assert.That(aonSegment, Is.Not.Null);

        var valueParam = aonSegment!.Parameters.FirstOrDefault(p => p.Name == "Value" || p.Name == "Enabled");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(Convert.ToBoolean(valueParam!.Value), Is.True);
    }

    [TestCase("SYM(AAPL).AllOrNone(false)")]
    [TestCase("SYM(AAPL).AllOrNone(FALSE)")]
    [TestCase("SYM(AAPL).AllOrNone(N)")]
    [TestCase("SYM(AAPL).AllOrNone(NO)")]
    public void Parse_AllOrNoneFalse_AllSyntax(string script)
    {
        var result = StrategyScriptParser.Parse(script);
        var aonSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.AllOrNone);
        Assert.That(aonSegment, Is.Not.Null);

        var valueParam = aonSegment!.Parameters.FirstOrDefault(p => p.Name == "Value" || p.Name == "Enabled");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(Convert.ToBoolean(valueParam!.Value), Is.False);
    }

    #endregion

    #region OrderType Tests

    [TestCase("SYM(AAPL).OrderType(MARKET)", "MARKET")]
    [TestCase("SYM(AAPL).OrderType(Market)", "MARKET")]
    [TestCase("SYM(AAPL).OrderType(market)", "MARKET")]
    [TestCase("SYM(AAPL).ORDERTYPE(MARKET)", "MARKET")]
    [TestCase("SYM(AAPL).ordertype(market)", "MARKET")]
    public void Parse_OrderTypeMarket_AllSyntax(string script, string expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var otSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.OrderType);
        Assert.That(otSegment, Is.Not.Null);

        var valueParam = otSegment!.Parameters.FirstOrDefault(p => p.Name == "Value");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(valueParam!.Value?.ToString(), Is.EqualTo(expected).IgnoreCase);
    }

    [TestCase("SYM(AAPL).OrderType(LIMIT)", "LIMIT")]
    [TestCase("SYM(AAPL).OrderType(Limit)", "LIMIT")]
    [TestCase("SYM(AAPL).OrderType(limit)", "LIMIT")]
    public void Parse_OrderTypeLimit_AllSyntax(string script, string expected)
    {
        var result = StrategyScriptParser.Parse(script);
        var otSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.OrderType);
        Assert.That(otSegment, Is.Not.Null);

        var valueParam = otSegment!.Parameters.FirstOrDefault(p => p.Name == "Value");
        Assert.That(valueParam, Is.Not.Null);
        Assert.That(valueParam!.Value?.ToString(), Is.EqualTo(expected).IgnoreCase);
    }

    #endregion

    #region Combined Order Config Tests

    [Test]
    public void Parse_AllOrderConfigCombined()
    {
        var script = "Ticker(AAPL).Entry(150).TakeProfit(160).TimeInForce(DAY).OutsideRTH(true).AllOrNone(false).OrderType(LIMIT)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.TimeInForce), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.OutsideRTH), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.AllOrNone), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.OrderType), Is.True);
    }

    [Test]
    public void Parse_PremarketStrategy_WithOrderConfig()
    {
        var script = @"
            Ticker(NVDA)
            .Session(IS.PREMARKET)
            .Entry(150)
            .TakeProfit(160)
            .TimeInForce(DAY)
            .OutsideRTH(IS.TRUE)
            .OrderType(LIMIT)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.TimeInForce), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.OutsideRTH), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.OrderType), Is.True);
    }

    [Test]
    public void Parse_ExtendedHoursStrategy()
    {
        var script = "Ticker(AAPL).Session(IS.AFTERHOURS).Entry(150).OutsideRTH(true).TimeInForce(GTC)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.SessionDuration), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.OutsideRTH), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.TimeInForce), Is.True);
    }

    #endregion

    #region Full Strategy with Order Config

    [Test]
    public void Parse_FullStrategyWithAllOrderConfig()
    {
        var script = @"
            Ticker(NVDA)
            .Session(IS.PREMARKET)
            .Entry(150)
            .TakeProfit(160)
            .StopLoss(145)
            .TrailingStopLoss(IS.MODERATE)
            .IsAboveVwap()
            .IsEmaAbove(9)
            .IsDiPositive()
            .Order(IS.LONG)
            .Quantity(100)
            .TimeInForce(DAY)
            .OutsideRTH(true)
            .AllOrNone(false)
            .OrderType(LIMIT)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));

        // Verify all order config segments exist
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.TimeInForce), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.OutsideRTH), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.AllOrNone), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.OrderType), Is.True);
    }

    #endregion
}
