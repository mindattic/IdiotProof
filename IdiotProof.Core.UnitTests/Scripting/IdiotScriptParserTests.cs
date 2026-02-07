// ============================================================================
// IdiotScriptParserTests - Tests for IdiotScript parsing functionality
// ============================================================================
//
// Tests the IdiotScriptParser which converts IdiotScript text to StrategyDefinition.
// Uses period (.) as the universal delimiter.
//
// ============================================================================

using IdiotProof.Core.Enums;
using IdiotProof.Core.Scripting;

namespace IdiotProof.Core.UnitTests.Scripting;

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

    #region Exit Strategy Parsing

    [TestCase("TICKER(AAPL).EXITSTRATEGY(IS.BELL)")]
    [TestCase("TICKER(AAPL).EXITSTRATEGY(IS.PREMARKET.BELL)")]
    [TestCase("TICKER(AAPL).EXITSTRATEGY(IS.OPEN)")]
    [TestCase("TICKER(AAPL).EXITSTRATEGY(IS.CLOSE)")]
    [TestCase("TICKER(AAPL).CLOSEPOSITION(IS.BELL)")] // Legacy support
    public void Parse_ExitStrategyCommands_CreatesExitSegment(string script)
    {
        var result = IdiotScriptParser.Parse(script);

        var closeSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        Assert.That(closeSegment, Is.Not.Null);
    }

    [Test]
    public void Parse_ExitStrategyWithIsProfitable_SetsBothValues()
    {
        var script = "TICKER(AAPL).EXITSTRATEGY(IS.BELL).ISPROFITABLE()";
        var result = IdiotScriptParser.Parse(script);

        var exitSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.ExitStrategy);
        var profitableSegment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.IsProfitable);
        Assert.That(exitSegment, Is.Not.Null);
        Assert.That(profitableSegment, Is.Not.Null);
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
    public void Parse_Order_CreatesOrderSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).QTY(10).ORDER()");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order);
        Assert.That(segment, Is.Not.Null);
        // Default direction is LONG
        var directionParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam?.Value?.ToString(), Is.EqualTo("Long"));
    }

    [Test]
    public void Parse_OrderLong_CreatesLongOrderSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).QTY(10).ORDER(IS.LONG)");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order);
        Assert.That(segment, Is.Not.Null);
        var directionParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam?.Value?.ToString(), Is.EqualTo("Long"));
    }

    [Test]
    public void Parse_OrderShort_CreatesShortOrderSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).QTY(10).ORDER(IS.SHORT)");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order);
        Assert.That(segment, Is.Not.Null);
        var directionParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam?.Value?.ToString(), Is.EqualTo("Short"));
    }

    [TestCase("TICKER(AAPL).QTY(10).ORDER()", "Long")]
    [TestCase("TICKER(AAPL).QTY(10).ORDER(IS.LONG)", "Long")]
    [TestCase("TICKER(AAPL).QTY(10).ORDER(LONG)", "Long")]
    [TestCase("TICKER(AAPL).QTY(10).ORDER(IS.SHORT)", "Short")]
    [TestCase("TICKER(AAPL).QTY(10).ORDER(SHORT)", "Short")]
    public void Parse_OrderVariations_SetsCorrectDirection(string script, string expectedDirection)
    {
        var result = IdiotScriptParser.Parse(script);

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order);
        Assert.That(segment, Is.Not.Null);
        var directionParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam?.Value?.ToString(), Is.EqualTo(expectedDirection));
    }

    [Test]
    public void Parse_Long_CreatesLongOrderSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).QTY(10).LONG");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order);
        Assert.That(segment, Is.Not.Null);
        var directionParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam?.Value?.ToString(), Is.EqualTo("Long"));
    }

    [Test]
    public void Parse_Short_CreatesShortOrderSegment()
    {
        var result = IdiotScriptParser.Parse("TICKER(AAPL).QTY(10).SHORT");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order);
        Assert.That(segment, Is.Not.Null);
        var directionParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam?.Value?.ToString(), Is.EqualTo("Short"));
    }

    [Test]
    public void Parse_DefaultOrder_IsLong()
    {
        // When no order direction is specified, default is LONG
        var result = IdiotScriptParser.Parse("TICKER(AAPL).QTY(10)");

        var segment = result.Segments.FirstOrDefault(s => s.Type == SegmentType.Order);
        Assert.That(segment, Is.Not.Null);
        var directionParam = segment!.Parameters.FirstOrDefault(p => p.Name == "Direction");
        Assert.That(directionParam?.Value?.ToString(), Is.EqualTo("Long"));
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

    [TestCase("TICKER(AAPL).Enabled", true)]
    [TestCase("TICKER(AAPL).IsEnabled", true)]
    [TestCase("TICKER(AAPL).ENABLED()", true)]
    [TestCase("TICKER(AAPL).ISENABLED()", true)]
    [TestCase("TICKER(AAPL).IsEnabled(Y)", true)]
    [TestCase("TICKER(AAPL).IsEnabled(YES)", true)]
    [TestCase("TICKER(AAPL).IsEnabled(IS.True)", true)]
    [TestCase("TICKER(AAPL).IsEnabled(N)", false)]
    [TestCase("TICKER(AAPL).IsEnabled(NO)", false)]
    [TestCase("TICKER(AAPL).IsEnabled(IS.False)", false)]
    public void Parse_EnabledVariations_SetsEnabled(string script, bool expected)
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
        var script = "TICKER(NVDA).SESSION(IS.PREMARKET).QTY(1).ENTRY(200).TP(210).SL(190).TSL(IS.MODERATE).BREAKOUT().PULLBACK().ISABOVEVWAP().EMAABOVE(9).CLOSEPOSITION(IS.BELL)";

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(1));
        Assert.That(stats.Price, Is.EqualTo(200.0).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(210.0).Within(0.01));
        Assert.That(stats.StopLoss, Is.EqualTo(190.0).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.10).Within(0.01));

        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.SessionDuration));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.Breakout));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.Pullback));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.IsEmaAbove));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.ExitStrategy));
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

    #region Repeat Parsing

    [Test]
    public void Parse_Repeat_SetsRepeatEnabled()
    {
        var script = "TICKER(ABC).ENTRY(5.00).TP(6.00).ABOVEVWAP().Repeat()";

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.RepeatEnabled, Is.True);
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.Repeat));
    }

    [Test]
    public void Parse_RepeatWithoutParentheses_SetsRepeatEnabled()
    {
        var script = "TICKER(ABC).ENTRY(5.00).TP(6.00).ABOVEVWAP().Repeat";

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.RepeatEnabled, Is.True);
    }

    [Test]
    public void Parse_RepeatCaseInsensitive_SetsRepeatEnabled()
    {
        var script = "TICKER(ABC).ENTRY(5.00).TP(6.00).REPEAT()";

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.RepeatEnabled, Is.True);
    }

    [Test]
    public void Parse_NoRepeat_RepeatEnabledIsFalse()
    {
        var script = "TICKER(ABC).ENTRY(5.00).TP(6.00).ABOVEVWAP";

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.RepeatEnabled, Is.False);
        Assert.That(result.Segments, Has.None.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.Repeat));
    }

    [Test]
    public void Parse_RepeatInFullStrategy_ParsesCorrectly()
    {
        var script = "TICKER(ABC).SESSION(IS.PREMARKET).ENTRY(5.00).TP(6.00).IsAboveVwap.DiPositive.Repeat()";

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("ABC"));
        Assert.That(result.RepeatEnabled, Is.True);
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.IsDI));
        Assert.That(result.Segments, Has.Some.Matches<IdiotProof.Core.Models.StrategySegment>(s => s.Type == SegmentType.Repeat));
    }

    [TestCase("TICKER(ABC).Repeat", true)]
    [TestCase("TICKER(ABC).IsRepeat", true)]
    [TestCase("TICKER(ABC).REPEAT()", true)]
    [TestCase("TICKER(ABC).ISREPEAT()", true)]
    [TestCase("TICKER(ABC).IsRepeat(Y)", true)]
    [TestCase("TICKER(ABC).IsRepeat(YES)", true)]
    [TestCase("TICKER(ABC).IsRepeat(IS.True)", true)]
    [TestCase("TICKER(ABC).Repeat(true)", true)]
    [TestCase("TICKER(ABC).IsRepeat(N)", false)]
    [TestCase("TICKER(ABC).IsRepeat(NO)", false)]
    [TestCase("TICKER(ABC).IsRepeat(IS.False)", false)]
    [TestCase("TICKER(ABC).Repeat(false)", false)]
    public void Parse_RepeatVariations_SetsRepeatEnabled(string script, bool expected)
    {
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.RepeatEnabled, Is.EqualTo(expected));
    }

    #endregion

    #region Comments and Whitespace

    [Test]
    public void Parse_CommentLine_IgnoresComment()
    {
        var script = """
            # This is a comment
            TICKER(AAPL).QTY(10)
            """;

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
    }

    [Test]
    public void Parse_MultipleCommentLines_IgnoresAllComments()
    {
        var script = """
            # INLF - Day 2 Short Squeeze Setup
            # Strategy: Break above $0.82 with volume, pullback to VWAP
            # Targets: $1.25, $2.07

            Ticker(INLF).Qty(1000).Entry(0.82).TP(1.25)
            """;

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("INLF"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(1000));
        Assert.That(stats.Price, Is.EqualTo(0.82).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(1.25).Within(0.01));
    }

    [Test]
    public void Parse_InlineComment_StripsComment()
    {
        var script = "TICKER(AAPL).QTY(10) # buy 10 shares";

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
    }

    [Test]
    public void Parse_EmptyLines_IgnoresEmptyLines()
    {
        var script = """

            TICKER(AAPL)

            .QTY(10)

            """;

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(10));
    }

    [Test]
    public void Parse_MultilineScript_JoinsLines()
    {
        var script = """
            Ticker(NVDA)
            .Session(IS.PREMARKET)
            .Qty(100)
            .Entry(200)
            .TP(210)
            """;

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        var stats = result.GetStats();
        Assert.That(stats.Quantity, Is.EqualTo(100));
        Assert.That(stats.Price, Is.EqualTo(200.0).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(210.0).Within(0.01));
    }

    [Test]
    public void Parse_MixedCommentsAndCode_ParsesCorrectly()
    {
        var script = """
            # FUSE Strategy
            Ticker(FUSE)
            .Name("FUSE Day 2") # strategy name
            .Session(IS.PREMARKET)
            # Entry conditions
            .Entry(2.90)
            .Breakout(2.90)
            .AboveVwap()
            # Exit conditions
            .TP(3.60)
            .TSL(IS.MODERATE)
            """;

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("FUSE"));
        Assert.That(result.Name, Is.EqualTo("FUSE Day 2"));
        var stats = result.GetStats();
        Assert.That(stats.Price, Is.EqualTo(2.90).Within(0.01));
        Assert.That(stats.TakeProfit, Is.EqualTo(3.60).Within(0.01));
        Assert.That(stats.TrailingStopLossPercent, Is.EqualTo(0.10).Within(0.01));
    }

    [Test]
    public void Parse_HashInsideQuotes_PreservesHash()
    {
        var script = """
            Ticker(AAPL).Name("Strategy #1")
            """;

        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Name, Is.EqualTo("Strategy #1"));
    }

    [Test]
    public void Parse_OnlyComments_ThrowsException()
    {
        var script = """
            # This is a comment
            # Another comment
            """;

        Assert.Throws<IdiotScriptException>(() => IdiotScriptParser.Parse(script));
    }

    #endregion

    #region GapUp/GapDown Parsing

    [Test]
    public void Parse_GapUp_CreatesGapUpCondition()
    {
        var script = "Ticker(NVDA).GapUp(5)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.GapUp &&
            s.Parameters.Any(p => p.Name == "Percentage" && Convert.ToDouble(p.Value) == 5)));
    }

    [Test]
    public void Parse_GapUpWithPercent_CreatesGapUpCondition()
    {
        // Should support both "5" and "5%" syntax
        var script = "Ticker(NVDA).GapUp(5%)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.GapUp &&
            s.Parameters.Any(p => p.Name == "Percentage" && Convert.ToDouble(p.Value) == 5)));
    }

    [Test]
    public void Parse_IsGapUp_CreatesGapUpCondition()
    {
        // IsGapUp is the canonical form (GapUp is an alias)
        var script = "Ticker(NVDA).IsGapUp(5)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("NVDA"));
        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.GapUp));
    }

    [Test]
    public void Parse_GapDown_CreatesGapDownCondition()
    {
        var script = "Ticker(AAPL).GapDown(3)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.GapDown &&
            s.Parameters.Any(p => p.Name == "Percentage" && Convert.ToDouble(p.Value) == 3)));
    }

    [Test]
    public void Parse_GapDownWithPercent_CreatesGapDownCondition()
    {
        var script = "Ticker(AAPL).GapDown(3%)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.GapDown &&
            s.Parameters.Any(p => p.Name == "Percentage" && Convert.ToDouble(p.Value) == 3)));
    }

    [Test]
    public void Parse_IsGapDown_CreatesGapDownCondition()
    {
        // IsGapDown is the canonical form (GapDown is an alias)
        var script = "Ticker(AAPL).IsGapDown(3)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.GapDown));
    }

    [Test]
    public void Parse_GapAndGoStrategy_ParsesCorrectly()
    {
        // Complete "Gap and Go" strategy
        var script = "Ticker(NVDA).Session(IS.PREMARKET).GapUp(5).AboveVwap().DiPositive().AutonomousTrading(IS.AGGRESSIVE)";
        var result = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(result.Symbol, Is.EqualTo("NVDA"));
            Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s => s.Type == SegmentType.GapUp));
            Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s => s.Type == SegmentType.IsAboveVwap));
            Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s => s.Type == SegmentType.IsDI));
            Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s => s.Type == SegmentType.AutonomousTrading));
        });
    }

    [Test]
    public void Parse_GapUpDecimalPercent_ParsesCorrectly()
    {
        var script = "Ticker(NVDA).GapUp(2.5)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.GapUp &&
            s.Parameters.Any(p => p.Name == "Percentage" && Convert.ToDouble(p.Value) == 2.5)));
    }

    #endregion

    #region Autonomous Trading

    [Test]
    public void Parse_AutonomousTrading_DefaultMode_ParsesCorrectly()
    {
        var script = "Ticker(AAPL).AutonomousTrading()";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Symbol, Is.EqualTo("AAPL"));
        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.AutonomousTrading &&
            s.Parameters.Any(p => p.Name == "Mode" && p.Value?.ToString() == "Balanced")));
    }

    [Test]
    public void Parse_AutonomousTradingWithMode_ParsesCorrectly()
    {
        var script = "Ticker(NVDA).AutonomousTrading(IS.AGGRESSIVE)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.AutonomousTrading &&
            s.Parameters.Any(p => p.Name == "Mode" && p.Value?.ToString() == "Aggressive")));
    }

    [Test]
    public void Parse_IsAutonomousTrading_AliasParsesCorrectly()
    {
        var script = "Ticker(TSLA).IsAutonomousTrading(IS.CONSERVATIVE)";
        var result = IdiotScriptParser.Parse(script);

        Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
            s.Type == SegmentType.AutonomousTrading &&
            s.Parameters.Any(p => p.Name == "Mode" && p.Value?.ToString() == "Conservative")));
    }

    [Test]
    public void Parse_AutonomousTradingMinimalScript_OnlyTickerNeeded()
    {
        // This is the core use case: just a ticker with autonomous trading
        var script = "Ticker(AAPL).AutonomousTrading()";
        var result = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(result.Symbol, Is.EqualTo("AAPL"));
            Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
                s.Type == SegmentType.AutonomousTrading));
        });
    }

    [Test]
    public void Parse_AutonomousTradingWithSession_ParsesCorrectly()
    {
        var script = "Ticker(NVDA).Session(IS.PREMARKET).AutonomousTrading(IS.BALANCED)";
        var result = IdiotScriptParser.Parse(script);

        Assert.Multiple(() =>
        {
            Assert.That(result.Symbol, Is.EqualTo("NVDA"));
            Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
                s.Type == SegmentType.SessionDuration));
            Assert.That(result.Segments, Has.Some.Matches<Models.StrategySegment>(s =>
                s.Type == SegmentType.AutonomousTrading));
        });
    }

    #endregion
}


