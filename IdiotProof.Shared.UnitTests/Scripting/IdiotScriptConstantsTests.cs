// ============================================================================
// IdiotScriptConstantsTests - Tests for IdiotScript constants and boolean handling
// ============================================================================
//
// NOMENCLATURE:
// - Boolean Constant: IS.TRUE or IS.FALSE (IdiotScript canonical form)
// - Truthy Value: Y, YES, yes, true, TRUE, 1, IS.TRUE
// - Falsy Value: N, NO, no, false, FALSE, 0, IS.FALSE
//
// These tests validate:
// 1. All valid truthy values resolve to true
// 2. All valid falsy values resolve to false
// 3. Invalid values return null
// 4. Constant resolution works correctly
//
// ============================================================================

using IdiotProof.Shared.Scripting;

namespace IdiotProof.Shared.UnitTests.Scripting;

/// <summary>
/// Tests for IdiotScriptConstants - predefined constants and value resolution.
/// </summary>
[TestFixture]
public class IdiotScriptConstantsTests
{
    #region Constant Values

    [Test]
    public void PREFIX_HasCorrectValue()
    {
        Assert.That(IdiotScriptConstants.Prefix, Is.EqualTo("IS."));
    }

    #endregion

    #region Session Constants

    [Test]
    public void SessionConstants_HaveCorrectValues()
    {
        Assert.That(IdiotScriptConstants.PREMARKET, Is.EqualTo("IS.PREMARKET"));
        Assert.That(IdiotScriptConstants.RTH, Is.EqualTo("IS.RTH"));
        Assert.That(IdiotScriptConstants.AFTERHOURS, Is.EqualTo("IS.AFTERHOURS"));
        Assert.That(IdiotScriptConstants.EXTENDED, Is.EqualTo("IS.EXTENDED"));
        Assert.That(IdiotScriptConstants.ACTIVE, Is.EqualTo("IS.ACTIVE"));
        Assert.That(IdiotScriptConstants.PREMARKET_END_EARLY, Is.EqualTo("IS.PREMARKET_END_EARLY"));
        Assert.That(IdiotScriptConstants.PREMARKET_START_LATE, Is.EqualTo("IS.PREMARKET_START_LATE"));
    }

    #endregion

    #region Time Constants

    [Test]
    public void TimeConstants_HaveCorrectValues()
    {
        Assert.That(IdiotScriptConstants.BELL, Is.EqualTo("IS.BELL"));
        Assert.That(IdiotScriptConstants.PREMARKET_BELL, Is.EqualTo("IS.PREMARKET.BELL"));
        Assert.That(IdiotScriptConstants.RTH_BELL, Is.EqualTo("IS.RTH.BELL"));
        Assert.That(IdiotScriptConstants.AFTERHOURS_BELL, Is.EqualTo("IS.AFTERHOURS.BELL"));
        Assert.That(IdiotScriptConstants.OPEN, Is.EqualTo("IS.OPEN"));
        Assert.That(IdiotScriptConstants.CLOSE_TIME, Is.EqualTo("IS.CLOSE"));
        Assert.That(IdiotScriptConstants.EOD, Is.EqualTo("IS.EOD"));
        Assert.That(IdiotScriptConstants.PREMARKET_START, Is.EqualTo("IS.PM_START"));
        Assert.That(IdiotScriptConstants.AFTERHOURS_END, Is.EqualTo("IS.AH_END"));
    }

    #endregion

    #region TSL Constants

    [Test]
    public void TslConstants_HaveCorrectValues()
    {
        Assert.That(IdiotScriptConstants.TSL_TIGHT, Is.EqualTo("IS.TIGHT"));
        Assert.That(IdiotScriptConstants.TSL_MODERATE, Is.EqualTo("IS.MODERATE"));
        Assert.That(IdiotScriptConstants.TSL_STANDARD, Is.EqualTo("IS.STANDARD"));
        Assert.That(IdiotScriptConstants.TSL_LOOSE, Is.EqualTo("IS.LOOSE"));
        Assert.That(IdiotScriptConstants.TSL_WIDE, Is.EqualTo("IS.WIDE"));
    }

    #endregion

    #region Order Direction Constants

    [Test]
    public void OrderDirectionConstants_HaveCorrectValues()
    {
        Assert.That(IdiotScriptConstants.BUY, Is.EqualTo("IS.BUY"));
        Assert.That(IdiotScriptConstants.SELL, Is.EqualTo("IS.SELL"));
        Assert.That(IdiotScriptConstants.CLOSE_LONG, Is.EqualTo("IS.CLOSE_LONG"));
        Assert.That(IdiotScriptConstants.CLOSE_SHORT, Is.EqualTo("IS.CLOSE_SHORT"));
    }

    #endregion

    #region Indicator Constants

    [Test]
    public void IndicatorConstants_HaveCorrectValues()
    {
        Assert.That(IdiotScriptConstants.RSI_OVERSOLD, Is.EqualTo("IS.RSI_OVERSOLD"));
        Assert.That(IdiotScriptConstants.RSI_OVERBOUGHT, Is.EqualTo("IS.RSI_OVERBOUGHT"));
        Assert.That(IdiotScriptConstants.ADX_STRONG, Is.EqualTo("IS.ADX_STRONG"));
        Assert.That(IdiotScriptConstants.ADX_WEAK, Is.EqualTo("IS.ADX_WEAK"));
    }

    #endregion

    #region Boolean Constants

    [Test]
    public void TRUE_Constant_HasCorrectValue()
    {
        Assert.That(IdiotScriptConstants.TRUE, Is.EqualTo("IS.TRUE"));
    }

    [Test]
    public void FALSE_Constant_HasCorrectValue()
    {
        Assert.That(IdiotScriptConstants.FALSE, Is.EqualTo("IS.FALSE"));
    }

    [Test]
    public void PROFITABLE_Constant_HasCorrectValue()
    {
        Assert.That(IdiotScriptConstants.PROFITABLE, Is.EqualTo("IS.PROFITABLE"));
    }

    [Test]
    public void PROFITABLE_Constant_ResolvesToTrue()
    {
        var result = IdiotScriptConstants.ResolveBoolean("IS.PROFITABLE");

        Assert.That(result.HasValue, Is.True);
        Assert.That(result!.Value, Is.True);
    }

    #endregion

    #region ResolveConstant - Sessions

    [TestCase("IS.PREMARKET", "PreMarket")]
    [TestCase("IS.RTH", "RTH")]
    [TestCase("IS.AFTERHOURS", "AfterHours")]
    [TestCase("IS.EXTENDED", "Extended")]
    [TestCase("IS.ACTIVE", "Active")]
    [TestCase("IS.PREMARKET_END_EARLY", "PreMarketEndEarly")]
    [TestCase("IS.PREMARKET_START_LATE", "PreMarketStartLate")]
    public void ResolveConstant_SessionConstants_ReturnsCorrectValue(string input, string expected)
    {
        var result = IdiotScriptConstants.ResolveConstant(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ResolveConstant - Times

    [TestCase("IS.BELL", "15:59")]           // Default to RTH bell
    [TestCase("IS.PREMARKET.BELL", "9:29")]  // 1 min before 9:30 open
    [TestCase("IS.RTH.BELL", "15:59")]       // 1 min before 4:00 close
    [TestCase("IS.AFTERHOURS.BELL", "19:59")] // 1 min before 8:00 AH end
    [TestCase("IS.OPEN", "9:30")]
    [TestCase("IS.CLOSE", "16:00")]
    [TestCase("IS.EOD", "16:00")]
    [TestCase("IS.PM_START", "4:00")]
    [TestCase("IS.AH_END", "20:00")]
    public void ResolveConstant_TimeConstants_ReturnsCorrectValue(string input, string expected)
    {
        var result = IdiotScriptConstants.ResolveConstant(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ResolveBellTime - Session Aware

    [TestCase("IS.BELL", null, 15, 59)]           // Default to RTH
    [TestCase("IS.BELL", "PreMarket", 9, 29)]     // Premarket: 1 min before 9:30
    [TestCase("IS.BELL", "RTH", 15, 59)]          // RTH: 1 min before 16:00
    [TestCase("IS.BELL", "AfterHours", 19, 59)]   // AH: 1 min before 20:00
    [TestCase("IS.BELL", "Extended", 19, 59)]     // Extended: 1 min before 20:00
    [TestCase("IS.BELL", "Active", 15, 59)]       // Active: default to RTH
    [TestCase("IS.PREMARKET.BELL", null, 9, 29)]  // Explicit premarket bell
    [TestCase("IS.RTH.BELL", null, 15, 59)]       // Explicit RTH bell
    [TestCase("IS.AFTERHOURS.BELL", null, 19, 59)] // Explicit AH bell
    public void ResolveBellTime_ReturnsCorrectTimeForSession(string input, string? session, int expectedHour, int expectedMinute)
    {
        var result = IdiotScriptConstants.ResolveBellTime(input, session);

        Assert.That(result.Hour, Is.EqualTo(expectedHour));
        Assert.That(result.Minute, Is.EqualTo(expectedMinute));
    }

    [Test]
    public void IsBellConstant_ReturnsTrueForBellConstants()
    {
        Assert.That(IdiotScriptConstants.IsBellConstant("IS.BELL"), Is.True);
        Assert.That(IdiotScriptConstants.IsBellConstant("IS.PREMARKET.BELL"), Is.True);
        Assert.That(IdiotScriptConstants.IsBellConstant("IS.RTH.BELL"), Is.True);
        Assert.That(IdiotScriptConstants.IsBellConstant("IS.AFTERHOURS.BELL"), Is.True);
        Assert.That(IdiotScriptConstants.IsBellConstant("is.bell"), Is.True);  // Case insensitive
    }

    [Test]
    public void IsBellConstant_ReturnsFalseForNonBellConstants()
    {
        Assert.That(IdiotScriptConstants.IsBellConstant("IS.OPEN"), Is.False);
        Assert.That(IdiotScriptConstants.IsBellConstant("IS.CLOSE"), Is.False);
        Assert.That(IdiotScriptConstants.IsBellConstant("IS.PREMARKET"), Is.False);
        Assert.That(IdiotScriptConstants.IsBellConstant(null), Is.False);
        Assert.That(IdiotScriptConstants.IsBellConstant(""), Is.False);
    }

    #endregion

    #region ResolveConstant - TSL Percentages

    [TestCase("IS.TIGHT", "0.05")]
    [TestCase("IS.MODERATE", "0.10")]
    [TestCase("IS.STANDARD", "0.15")]
    [TestCase("IS.LOOSE", "0.20")]
    [TestCase("IS.WIDE", "0.25")]
    public void ResolveConstant_TslConstants_ReturnsCorrectValue(string input, string expected)
    {
        var result = IdiotScriptConstants.ResolveConstant(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ResolveConstant - Indicators

    [TestCase("IS.RSI_OVERSOLD", "30")]
    [TestCase("IS.RSI_OVERBOUGHT", "70")]
    [TestCase("IS.ADX_STRONG", "25")]
    [TestCase("IS.ADX_WEAK", "20")]
    public void ResolveConstant_IndicatorConstants_ReturnsCorrectValue(string input, string expected)
    {
        var result = IdiotScriptConstants.ResolveConstant(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ResolveConstant - Booleans

    [TestCase("IS.TRUE", "true")]
    [TestCase("IS.FALSE", "false")]
    public void ResolveConstant_BooleanConstants_ReturnsCorrectValue(string input, string expected)
    {
        var result = IdiotScriptConstants.ResolveConstant(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ResolveConstant - Case Insensitivity

    [TestCase("is.premarket", "PreMarket")]
    [TestCase("Is.PreMarket", "PreMarket")]
    [TestCase("IS.PREMARKET", "PreMarket")]
    [TestCase("is.bell", "15:59")]
    [TestCase("Is.Bell", "15:59")]
    [TestCase("is.premarket.bell", "9:29")]
    [TestCase("is.true", "true")]
    [TestCase("IS.TRUE", "true")]
    public void ResolveConstant_IsCaseInsensitive(string input, string expected)
    {
        var result = IdiotScriptConstants.ResolveConstant(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ResolveConstant - Invalid Input

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("PREMARKET")]  // Missing IS. prefix
    [TestCase("IS.UNKNOWN")]
    [TestCase("IS.INVALID")]
    [TestCase("NOT_A_CONSTANT")]
    public void ResolveConstant_InvalidInput_ReturnsNull(string? input)
    {
        var result = IdiotScriptConstants.ResolveConstant(input!);

        Assert.That(result, Is.Null);
    }

    #endregion

    #region ResolveBoolean - Truthy Values

    [TestCase("Y")]
    [TestCase("YES")]
    [TestCase("yes")]
    [TestCase("Yes")]
    [TestCase("true")]
    [TestCase("TRUE")]
    [TestCase("True")]
    [TestCase("1")]
    [TestCase("IS.TRUE")]
    [TestCase("is.true")]
    [TestCase("Is.True")]
    public void ResolveBoolean_TruthyValues_ReturnsTrue(string input)
    {
        var result = IdiotScriptConstants.ResolveBoolean(input);

        Assert.That(result.HasValue, Is.True);
        Assert.That(result!.Value, Is.True);
    }

    #endregion

    #region ResolveBoolean - Falsy Values

    [TestCase("N")]
    [TestCase("NO")]
    [TestCase("no")]
    [TestCase("No")]
    [TestCase("false")]
    [TestCase("FALSE")]
    [TestCase("False")]
    [TestCase("0")]
    [TestCase("IS.FALSE")]
    [TestCase("is.false")]
    [TestCase("Is.False")]
    public void ResolveBoolean_FalsyValues_ReturnsFalse(string input)
    {
        var result = IdiotScriptConstants.ResolveBoolean(input);

        Assert.That(result.HasValue, Is.True);
        Assert.That(result!.Value, Is.False);
    }

    #endregion

    #region ResolveBoolean - Invalid Values

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("maybe")]
    [TestCase("yep")]
    [TestCase("nope")]
    [TestCase("2")]
    [TestCase("IS.MAYBE")]
    public void ResolveBoolean_InvalidValues_ReturnsNull(string? input)
    {
        var result = IdiotScriptConstants.ResolveBoolean(input!);

        Assert.That(result.HasValue, Is.False);
    }

    #endregion

    #region NormalizeBoolean

    [TestCase("Y", "IS.TRUE")]
    [TestCase("YES", "IS.TRUE")]
    [TestCase("true", "IS.TRUE")]
    [TestCase("1", "IS.TRUE")]
    [TestCase("N", "IS.FALSE")]
    [TestCase("NO", "IS.FALSE")]
    [TestCase("false", "IS.FALSE")]
    [TestCase("0", "IS.FALSE")]
    public void NormalizeBoolean_ValidValues_ReturnsNormalizedConstant(string input, string expected)
    {
        var result = IdiotScriptConstants.NormalizeBoolean(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("invalid")]
    [TestCase("maybe")]
    [TestCase("")]
    public void NormalizeBoolean_InvalidValues_ReturnsNull(string input)
    {
        var result = IdiotScriptConstants.NormalizeBoolean(input);

        Assert.That(result, Is.Null);
    }

    #endregion

    #region IsValidBoolean

    [TestCase("Y", true)]
    [TestCase("YES", true)]
    [TestCase("TRUE", true)]
    [TestCase("true", true)]
    [TestCase("1", true)]
    [TestCase("IS.TRUE", true)]
    [TestCase("N", true)]
    [TestCase("NO", true)]
    [TestCase("FALSE", true)]
    [TestCase("false", true)]
    [TestCase("0", true)]
    [TestCase("IS.FALSE", true)]
    [TestCase("maybe", false)]
    [TestCase("invalid", false)]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    public void IsValidBoolean_ReturnsExpectedResult(string? input, bool expected)
    {
        var result = IdiotScriptConstants.IsValidBoolean(input!);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ToIdiotScriptBoolean

    [TestCase(true, "IS.TRUE")]
    [TestCase(false, "IS.FALSE")]
    public void ToIdiotScriptBoolean_ReturnsCanonicalForm(bool input, string expected)
    {
        var result = IdiotScriptConstants.ToIdiotScriptBoolean(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion
}


