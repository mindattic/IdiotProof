// ============================================================================
// StrategyScriptParser Comment and Multi-line Script Tests
// ============================================================================

using IdiotProof.Core.Scripting;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.UnitTests;

/// <summary>
/// Tests for comment handling and multi-line script parsing.
/// Verifies # comments, // comments, inline comments, and line continuations.
/// </summary>
[TestFixture]
public class ScriptParserCommentMultilineTests
{
    #region Hash (#) Comment Tests

    [Test]
    public void Parse_HashComment_FullLineIgnored()
    {
        var script = @"
            # This is a comment
            Ticker(AAPL).Entry(150)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_HashComment_MultipleLines()
    {
        var script = @"
            # Strategy configuration
            # Author: Test User
            # Date: 2024-01-01
            Ticker(AAPL)
            .Entry(150)
            .TakeProfit(160)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_HashComment_InlineStripped()
    {
        var script = "Ticker(AAPL).Entry(150) # entry at 150";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(150).Within(0.01));
    }

    [Test]
    public void Parse_HashComment_AfterEachCommand()
    {
        var script = @"
            Ticker(AAPL)       # Stock symbol
            .Entry(150)        # Entry price
            .TakeProfit(160)   # Target profit
            .StopLoss(145)     # Risk management
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(150).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(160).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(145).Within(0.01));
    }

    #endregion

    #region Double-Slash (//) Comment Tests

    [Test]
    public void Parse_DoubleSlashComment_FullLineIgnored()
    {
        var script = @"
            // This is a C-style comment
            Ticker(NVDA).Entry(200)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
    }

    [Test]
    public void Parse_DoubleSlashComment_MultipleLines()
    {
        var script = @"
            // Strategy configuration
            // Author: Test User
            // Date: 2024-01-01
            Ticker(NVDA)
            .Entry(200)
            .TakeProfit(220)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
    }

    [Test]
    public void Parse_DoubleSlashComment_InlineStripped()
    {
        var script = "Ticker(NVDA).Entry(200) // entry at 200";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(200).Within(0.01));
    }

    [Test]
    public void Parse_DoubleSlashComment_AfterEachCommand()
    {
        var script = @"
            Ticker(NVDA)       // Stock symbol
            .Entry(200)        // Entry price
            .TakeProfit(220)   // Target profit
            .StopLoss(190)     // Risk management
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(200).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(220).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(190).Within(0.01));
    }

    #endregion

    #region Mixed Comment Tests

    [Test]
    public void Parse_MixedCommentStyles()
    {
        var script = @"
            # Header comment with hash
            // Alternative header with double-slash
            Ticker(AAPL)
            .Entry(150)     # inline hash comment
            .TakeProfit(160) // inline double-slash comment
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_CommentsOnlyLines_Ignored()
    {
        var script = @"
            #
            # Empty comment
            //
            // Another empty comment
            Ticker(AAPL)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region Comment Inside Quotes (Should NOT Strip)

    [Test]
    public void Parse_HashInsideQuotes_Preserved()
    {
        var script = @"Ticker(AAPL).Name(""Strategy #1"")";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Name, Is.EqualTo("Strategy #1"));
    }

    [Test]
    public void Parse_DoubleSlashInsideQuotes_Preserved()
    {
        var script = @"Ticker(AAPL).Name(""Strategy // Test"")";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Name, Is.EqualTo("Strategy // Test"));
    }

    #endregion

    #region Multi-line Script Tests

    [Test]
    public void Parse_MultiLine_BasicScript()
    {
        var script = @"
            Ticker(AAPL)
            .Entry(150)
            .TakeProfit(160)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(150).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(160).Within(0.01));
    }

    [Test]
    public void Parse_MultiLine_WithConditions()
    {
        var script = @"
            Ticker(PLTR)
            .Session(IS.PREMARKET)
            .Entry(25)
            .TakeProfit(27)
            .StopLoss(24)
            .IsAboveVwap()
            .IsEmaAbove(9)
            .IsDiPositive()
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("PLTR"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsEmaAbove), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
    }

    [Test]
    public void Parse_MultiLine_FullStrategy()
    {
        var script = @"
            # Premarket Momentum Strategy
            Ticker(NVDA)
            .Name(""NVDA Premarket Momentum"")
            .Session(IS.PREMARKET)
            .Quantity(10)
            .Entry(150)
            .TakeProfit(160)
            .StopLoss(145)
            .TrailingStopLoss(IS.MODERATE)
            .IsAboveVwap()
            .IsEmaAbove(9)
            .IsEmaBetween(9, 21)
            .IsEmaAbove(200)
            .IsDiPositive()
            .IsMacdBullish()
            .Order(IS.LONG)
            .AdaptiveOrder(IS.AGGRESSIVE)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Name, Is.EqualTo("NVDA Premarket Momentum"));
    }

    [Test]
    public void Parse_MultiLine_DifferentIndentation()
    {
        var script = @"
Ticker(AAPL)
    .Entry(150)
        .TakeProfit(160)
            .StopLoss(145)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_MultiLine_NoLeadingPeriod()
    {
        // Some users might forget the period at the start of a continued line
        // The parser should still handle this gracefully by joining lines
        var script = @"
            Ticker(AAPL).Entry(150)
            .TakeProfit(160)
            .StopLoss(145)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(150).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(160).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(145).Within(0.01));
    }

    #endregion

    #region Empty Line Handling

    [Test]
    public void Parse_EmptyLines_Ignored()
    {
        var script = @"
            Ticker(AAPL)

            .Entry(150)

            .TakeProfit(160)

        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_OnlyWhitespaceLines_Ignored()
    {
        var script = @"
            Ticker(AAPL)
            
            .Entry(150)
               
            .TakeProfit(160)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region Windows vs Unix Line Endings

    [Test]
    public void Parse_UnixLineEndings()
    {
        var script = "Ticker(AAPL)\n.Entry(150)\n.TakeProfit(160)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_WindowsLineEndings()
    {
        var script = "Ticker(AAPL)\r\n.Entry(150)\r\n.TakeProfit(160)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_MixedLineEndings()
    {
        var script = "Ticker(AAPL)\n.Entry(150)\r\n.TakeProfit(160)\r.StopLoss(145)";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    #endregion

    #region Complex Real-World Examples

    [Test]
    public void Parse_DocumentedExampleWithComments()
    {
        // Example from documentation with comments
        var script = @"
            # ============================================
            # Gap and Go Premarket Strategy
            # ============================================
            # This strategy looks for gap up stocks in premarket
            # and enters when momentum is confirmed
            
            Ticker(CIGL)                        # Small cap stock
            .Session(IS.PREMARKET)              # Trade premarket only
            .GapUp(5)                           # 5%+ gap up
            .IsAboveVwap()                      # Above VWAP
            .IsDiPositive()                     # Bullish momentum
            .Entry(4.15)                        # Entry level
            .TakeProfit(4.80)                   # Target
            .StopLoss(3.90)                     # Risk level
            .AutonomousTrading(IS.AGGRESSIVE)   # Smart order management
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("CIGL"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.GapUp), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAboveVwap), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.AutonomousTrading), Is.True);
    }

    [Test]
    public void Parse_TrendFollowingWithComments()
    {
        var script = @"
            // ============================================
            // RTH Trend Following Strategy
            // ============================================
            
            Ticker(SPY)
            .Session(IS.RTH)
            .IsAdxAbove(25)       // Strong trend
            .IsDiPositive()       // Bullish direction
            .IsMacdBullish()      // Momentum confirmation
            .IsEmaAbove(9)        // Short-term bullish
            .IsEmaAbove(21)       // Medium-term bullish
            .Order(IS.LONG)
            .TakeProfit(510)
            .StopLoss(495)
        ";
        var result = StrategyScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("SPY"));
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsAdx), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsDI), Is.True);
        Assert.That(result.Segments.Any(s => s.Type == SegmentType.IsMacd), Is.True);
    }

    #endregion
}
