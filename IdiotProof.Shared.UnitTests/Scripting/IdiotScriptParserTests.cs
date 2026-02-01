// ============================================================================
// IdiotScriptParserTests - Tests for IdiotScript parsing functionality
// ============================================================================
//
// Tests the IdiotScriptParser which converts IdiotScript text to StrategyDefinition.
// Uses period (.) as the universal delimiter.
//
// ============================================================================

using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Scripting;

namespace IdiotProof.Shared.UnitTests.Scripting;

/// <summary>
/// Tests for IdiotScriptParser - parsing IdiotScript to StrategyDefinition.
/// </summary>
[TestFixture]
public class IdiotScriptParserTests
{
    #region Empty/Null Input

    [Test]
    public void Parse_EmptyScript_ThrowsException()
    {
        Assert.Throws<IdiotScriptException>(() => IdiotScriptParser.Parse(""));
    }

    [Test]
    public void Parse_WhitespaceOnly_ThrowsException()
    {
        Assert.Throws<IdiotScriptException>(() => IdiotScriptParser.Parse("   "));
    }

    [Test]
    public void Parse_NullScript_ThrowsException()
    {
        Assert.Throws<IdiotScriptException>(() => IdiotScriptParser.Parse(null!));
    }

    #endregion

    #region Symbol Parsing

    [TestCase("TICKER(AAPL)", "AAPL")]
    [TestCase("SYM(NVDA)", "NVDA")]
    [TestCase("SYMBOL(META)", "META")]
    [TestCase("STOCK.TICKER(PLTR)", "PLTR")]
    [TestCase("STOCK.SYMBOL(TSLA)", "TSLA")]
    public void Parse_SymbolCommands_ExtractsSymbol(string script, string expected)
    {
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo(expected));
    }

    [TestCase("ticker(aapl)", "AAPL")]
    [TestCase("sym(nvda)", "NVDA")]
    [TestCase("Ticker(Aapl)", "AAPL")]
    [TestCase("SYM(goog)", "GOOG")]
    public void Parse_SymbolCaseInsensitive_NormalizesToUppercase(string script, string expected)
    {
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_MissingSymbol_ThrowsException()
    {
        var ex = Assert.Throws<IdiotScriptException>(() => 
            IdiotScriptParser.Parse("QTY(10).TP($158)"));

        Assert.That(ex!.Message, Does.Contain("Symbol").IgnoreCase);
    }

    [Test]
    public void Parse_WithDefaultSymbol_UsesDefault()
    {
        var result = IdiotScriptParser.Parse("QTY(10).TP($160)", defaultSymbol: "AAPL");

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
    }

    [Test]
    public void Parse_SymbolOverridesDefault_ScriptWins()
    {
        var result = IdiotScriptParser.Parse("TICKER(NVDA).QTY(10)", defaultSymbol: "AAPL");

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
    }

    #endregion

    #region Quantity Parsing

    [TestCase("TICKER(AAPL).QTY(1)", 1)]
    [TestCase("TICKER(AAPL).QTY(10)", 10)]
    [TestCase("TICKER(AAPL).QTY(100)", 100)]
    [TestCase("TICKER(AAPL).QUANTITY(50)", 50)]
    public void Parse_QuantityCommands_ExtractsQuantity(string script, int expected)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();

        Assert.That(stats.Quantity, Is.EqualTo(expected));
    }

    #endregion

    #region Price Parsing

    [TestCase("TICKER(AAPL).TP(160)", 160.0)]
    [TestCase("TICKER(AAPL).TP($160)", 160.0)]
    [TestCase("TICKER(AAPL).TAKEPROFIT(175.50)", 175.50)]
    [TestCase("TICKER(AAPL).TakeProfit($200)", 200.0)]
    public void Parse_TakeProfitCommands_ExtractsPrice(string script, double expected)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();

        Assert.That(stats.TakeProfit, Is.EqualTo(expected).Within(0.01));
    }

    [TestCase("TICKER(AAPL).SL(145)", 145.0)]
    [TestCase("TICKER(AAPL).SL($145)", 145.0)]
    [TestCase("TICKER(AAPL).STOPLOSS(140.25)", 140.25)]
    [TestCase("TICKER(AAPL).StopLoss($135)", 135.0)]
    public void Parse_StopLossCommands_ExtractsPrice(string script, double expected)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();

        Assert.That(stats.StopLoss, Is.EqualTo(expected).Within(0.01));
    }

    [TestCase("TICKER(AAPL).ENTRY(150)", 150.0)]
    [TestCase("TICKER(AAPL).ENTRY($150)", 150.0)]
    [TestCase("TICKER(AAPL).PRICE(148.75)", 148.75)]
    public void Parse_EntryCommands_ExtractsPrice(string script, double expected)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();

        Assert.That(stats.Price, Is.EqualTo(expected).Within(0.01));
    }

    #endregion

    #region Trailing Stop Loss Parsing

    [TestCase("TICKER(AAPL).TSL(10)", 0.10)]
    [TestCase("TICKER(AAPL).TSL(15)", 0.15)]
    [TestCase("TICKER(AAPL).TRAILINGSTOPLOSS(20)", 0.20)]
    [TestCase("TICKER(AAPL).TSL(10%)", 0.10)]
    [TestCase("TICKER(AAPL).TSL(15%)", 0.15)]
    public void Parse_TslNumericValues_ExtractsPercentage(string script, double expected)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();

        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expected).Within(0.01));
    }

    [TestCase("TICKER(AAPL).TSL(IS.TIGHT)", 0.05)]
    [TestCase("TICKER(AAPL).TSL(IS.MODERATE)", 0.10)]
    [TestCase("TICKER(AAPL).TSL(IS.STANDARD)", 0.15)]
    [TestCase("TICKER(AAPL).TSL(IS.LOOSE)", 0.20)]
    [TestCase("TICKER(AAPL).TSL(IS.WIDE)", 0.25)]
    public void Parse_TslConstants_ExtractsPercentage(string script, double expected)
    {
        var result = IdiotScriptParser.Parse(script);
        var stats = result.GetStats();

        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(expected).Within(0.01));
    }

    #endregion

    #region Session Parsing

    [TestCase("TICKER(AAPL).SESSION(IS.PREMARKET)")]
    [TestCase("TICKER(AAPL).SESSION(IS.RTH)")]
    [TestCase("TICKER(AAPL).SESSION(IS.AFTERHOURS)")]
    [TestCase("TICKER(AAPL).SESSION(IS.EXTENDED)")]
    public void Parse_SessionCommands_CreatesSessionSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var sessionSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.SessionDuration);
        Assert.That(sessionSegment, Is.Not.Null);
    }

    #endregion

    #region Close Position Parsing

    [TestCase("TICKER(AAPL).CLOSEPOSITION(IS.BELL)")]
    [TestCase("TICKER(AAPL).CLOSEPOSITION(IS.PREMARKET.BELL)")]
    [TestCase("TICKER(AAPL).CLOSEPOSITION(IS.OPEN)")]
    [TestCase("TICKER(AAPL).CLOSEPOSITION(IS.CLOSE)")]
    public void Parse_ClosePositionCommands_CreatesCloseSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ClosePosition);
        Assert.That(closeSegment, Is.Not.Null);
    }

    #endregion

    #region Condition Parsing

    [Test]
    public void Parse_Breakout_CreatesBreakoutSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).BREAKOUT()");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        Assert.That(segment, Is.Not.Null);
    }

    [Test]
    public void Parse_BreakoutWithPrice_CreatesBreakoutSegmentWithPrice()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).BREAKOUT(150)");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Breakout);
        Assert.That(segment, Is.Not.Null);
    }

    [Test]
    public void Parse_Pullback_CreatesPullbackSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).PULLBACK()");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Pullback);
        Assert.That(segment, Is.Not.Null);
    }

    [Test]
    public void Parse_AboveVwap_CreatesVwapSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).ABOVEVWAP");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAboveVwap);
        Assert.That(segment, Is.Not.Null);
    }

    [Test]
    public void Parse_BelowVwap_CreatesVwapSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).BELOWVWAP");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsBelowVwap);
        Assert.That(segment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).EMAABOVE(9)")]
    [TestCase("TICKER(AAPL).ISEMAABOVE(9)")]
    public void Parse_EmaAbove_CreatesEmaSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaAbove);
        Assert.That(segment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).EMABELOW(21)")]
    [TestCase("TICKER(AAPL).ISEMABELOW(21)")]
    public void Parse_EmaBelow_CreatesEmaSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBelow);
        Assert.That(segment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).EMABETWEEN(9, 21)")]
    [TestCase("TICKER(AAPL).ISEMABETWEEN(9, 21)")]
    public void Parse_EmaBetween_CreatesEmaSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsEmaBetween);
        Assert.That(segment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).RSIOVERSOLD(30)")]
    [TestCase("TICKER(AAPL).ISRSIOVERSOLD(30)")]
    public void Parse_RsiOversold_CreatesRsiSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRsi);
        Assert.That(segment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).RSIOVERBOUGHT(70)")]
    [TestCase("TICKER(AAPL).ISRSIOVERBOUGHT(70)")]
    public void Parse_RsiOverbought_CreatesRsiSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsRsi);
        Assert.That(segment, Is.Not.Null);
    }

    [TestCase("TICKER(AAPL).ADXABOVE(25)")]
    [TestCase("TICKER(AAPL).ISADXABOVE(25)")]
    public void Parse_AdxAbove_CreatesAdxSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsAdx);
        Assert.That(segment, Is.Not.Null);
    }

    #endregion

    #region Order Direction

    [Test]
    public void Parse_Buy_CreatesBuySegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).QTY(10).BUY");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Buy);
        Assert.That(segment, Is.Not.Null);
    }

    [Test]
    public void Parse_Sell_CreatesSellSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).QTY(10).SELL");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Sell);
        Assert.That(segment, Is.Not.Null);
    }

    #endregion

    #region Enabled Flag

    [TestCase("TICKER(AAPL).ENABLED(true)", true)]
    [TestCase("TICKER(AAPL).ENABLED(TRUE)", true)]
    [TestCase("TICKER(AAPL).ENABLED(Y)", true)]
    [TestCase("TICKER(AAPL).ENABLED(IS.TRUE)", true)]
    [TestCase("TICKER(AAPL).ENABLED(false)", false)]
    [TestCase("TICKER(AAPL).ENABLED(FALSE)", false)]
    [TestCase("TICKER(AAPL).ENABLED(N)", false)]
    [TestCase("TICKER(AAPL).ENABLED(IS.FALSE)", false)]
    public void Parse_EnabledFlag_SetsEnabled(string script, bool expected)
    {
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Enabled, Is.EqualTo(expected));
    }

    #endregion

    #region Name and Description

    [Test]
    public void Parse_Name_SetsStrategyName()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).NAME(\"My Strategy\")");

        Assert.That(result.Name, Is.EqualTo("My Strategy"));
    }

    [Test]
    public void Parse_Description_SetsDescription()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).DESC(\"A trading strategy\")");

        Assert.That(result.Description, Is.EqualTo("A trading strategy"));
    }

    #endregion

    #region TryParse

    [Test]
    public void TryParse_ValidScript_ReturnsTrue()
    {
        var success = IdiotScriptParser.TryParse("TICKER(AAPL).QTY(10)", out var strategy, out var error);

        Assert.That(success, Is.True);
        Assert.That(strategy, Is.Not.Null);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void TryParse_InvalidScript_ReturnsFalse()
    {
        var success = IdiotScriptParser.TryParse("QTY(10)", out var strategy, out var error);

        Assert.That(success, Is.False);
        Assert.That(strategy, Is.Null);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void TryParse_EmptyScript_ReturnsFalse()
    {
        var success = IdiotScriptParser.TryParse("", out var strategy, out var error);

        Assert.That(success, Is.False);
        Assert.That(strategy, Is.Null);
        Assert.That(error, Is.Not.Null);
    }

    #endregion

    #region Validate

    [Test]
    public void Validate_ValidScript_ReturnsTrue()
    {
        var (isValid, errors) = IdiotScriptParser.Validate("TICKER(AAPL).QTY(10)");

        Assert.That(isValid, Is.True);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Validate_InvalidScript_ReturnsFalseWithErrors()
    {
        var (isValid, errors) = IdiotScriptParser.Validate("QTY(10)");

        Assert.That(isValid, Is.False);
        Assert.That(errors, Is.Not.Empty);
    }

    #endregion

    #region Case Insensitivity

    [TestCase("ticker(aapl).qty(10).tp(160)")]
    [TestCase("TICKER(AAPL).QTY(10).TP(160)")]
    [TestCase("Ticker(Aapl).Qty(10).Tp(160)")]
    [TestCase("TiCkEr(aApL).qTy(10).tP(160)")]
    public void Parse_CommandsCaseInsensitive_ParsesCorrectly(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
        Assert.That(stats.TakeProfit, Is.EqualTo(160.0).Within(0.01));
    }

    #endregion

    #region Complex Scripts

    [Test]
    public void Parse_FullStrategy_ParsesAllComponents()
    {
        var script = "TICKER(NVDA).SESSION(IS.PREMARKET).QTY(1).ENTRY(200).TP(210).SL(190).TSL(IS.MODERATE).BREAKOUT().PULLBACK().ABOVEVWAP.EMAABOVE(9).CLOSEPOSITION(IS.BELL)";

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(1));
        Assert.That(stats.Price, Is.EqualTo(200.0).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(210.0).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(190.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.10).Within(0.01));

        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Shared.Models.StrategySegment>(s => s.Type == SegmentType.SessionDuration));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Shared.Models.StrategySegment>(s => s.Type == SegmentType.Breakout));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Shared.Models.StrategySegment>(s => s.Type == SegmentType.Pullback));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Shared.Models.StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Shared.Models.StrategySegment>(s => s.Type == SegmentType.IsEmaAbove));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Shared.Models.StrategySegment>(s => s.Type == SegmentType.ClosePosition));
    }

    #endregion

    #region Unknown Commands

    [Test]
    public void Parse_UnknownCommand_ThrowsException()
    {
        var ex = Assert.Throws<IdiotScriptException>(() => 
            IdiotScriptParser.Parse("TICKER(AAPL).UNKNOWN_COMMAND(123)"));

        Assert.That(ex!.Message, Does.Contain("Unknown").IgnoreCase);
    }

    #endregion
}
