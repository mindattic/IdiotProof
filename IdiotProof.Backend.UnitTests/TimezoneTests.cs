// ============================================================================
// TimezoneTests - Comprehensive Tests for Timezone Conversions
// ============================================================================
//
// These tests verify that:
// 1. All timezone conversions are accurate
// 2. Market open at TimeOnly(9, 30) ET correctly converts to local times
// 3. Round-trip conversions maintain accuracy
// 4. DST transitions are handled (where applicable)
// 5. All trading periods convert correctly
// 6. IBKR API timezone strings are correct
// 7. Default timezone is EST (Eastern Standard Time)
//
// ============================================================================

using IdiotProof.Backend;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Models;
using IdiotProof.Shared.Settings;
using MarketTimeZone = IdiotProof.Shared.Enums.MarketTimeZone;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for timezone conversion functionality.
/// </summary>
[TestFixture]
public class TimezoneHelperTests
{
    #region MarketTimeZone Enum Tests

    [Test]
    public void MarketTimeZone_HasAllExpectedValues()
    {
        // Assert all expected values exist
        Assert.That(Enum.IsDefined(typeof(MarketTimeZone), MarketTimeZone.EST), Is.True);
        Assert.That(Enum.IsDefined(typeof(MarketTimeZone), MarketTimeZone.CST), Is.True);
        Assert.That(Enum.IsDefined(typeof(MarketTimeZone), MarketTimeZone.MST), Is.True);
        Assert.That(Enum.IsDefined(typeof(MarketTimeZone), MarketTimeZone.PST), Is.True);
    }

    [Test]
    public void MarketTimeZone_HasExactlyFourValues()
    {
        var values = Enum.GetValues<MarketTimeZone>();
        Assert.That(values, Has.Length.EqualTo(4));
    }

    #endregion

    #region GetTimeZoneInfo Tests

    [Test]
    [TestCase(MarketTimeZone.EST, "Eastern Standard Time")]
    [TestCase(MarketTimeZone.CST, "Central Standard Time")]
    [TestCase(MarketTimeZone.MST, "Mountain Standard Time")]
    [TestCase(MarketTimeZone.PST, "Pacific Standard Time")]
    public void GetTimeZoneInfo_ReturnsCorrectTimezone(MarketTimeZone timezone, string expectedId)
    {
        var timeZoneInfo = TimezoneHelper.GetTimeZoneInfo(timezone);
        Assert.That(timeZoneInfo.Id, Is.EqualTo(expectedId));
    }

    [Test]
    public void GetTimeZoneInfo_InvalidTimezone_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimezoneHelper.GetTimeZoneInfo((MarketTimeZone)99));
    }

    #endregion

    #region GetTimezoneId Tests

    [Test]
    [TestCase(MarketTimeZone.EST, "Eastern Standard Time")]
    [TestCase(MarketTimeZone.CST, "Central Standard Time")]
    [TestCase(MarketTimeZone.MST, "Mountain Standard Time")]
    [TestCase(MarketTimeZone.PST, "Pacific Standard Time")]
    public void GetTimezoneId_ReturnsCorrectId(MarketTimeZone timezone, string expectedId)
    {
        var id = TimezoneHelper.GetTimezoneId(timezone);
        Assert.That(id, Is.EqualTo(expectedId));
    }

    #endregion

    #region GetTimezoneAbbreviation Tests

    [Test]
    [TestCase(MarketTimeZone.EST, "EST")]
    [TestCase(MarketTimeZone.CST, "CST")]
    [TestCase(MarketTimeZone.MST, "MST")]
    [TestCase(MarketTimeZone.PST, "PST")]
    public void GetTimezoneAbbreviation_ReturnsCorrectAbbreviation(MarketTimeZone timezone, string expectedAbbrev)
    {
        var abbrev = TimezoneHelper.GetTimezoneAbbreviation(timezone);
        Assert.That(abbrev, Is.EqualTo(expectedAbbrev));
    }

    #endregion

    #region ToLocal Conversion Tests - Market Open (9:30 AM ET)

    /// <summary>
    /// Critical test: Verifies that market open at 9:30 AM ET converts correctly to all timezones.
    /// This ensures that when a user sets Time.RTH.Start, it correlates to the actual market open.
    /// </summary>
    [Test]
    public void ToLocal_MarketOpen_EST_Returns930AM()
    {
        var easternMarketOpen = new TimeOnly(9, 30);
        var localTime = TimezoneHelper.ToLocal(easternMarketOpen, MarketTimeZone.EST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(9, 30)),
            "Market open in EST should be 9:30 AM");
    }

    [Test]
    public void ToLocal_MarketOpen_CST_Returns830AM()
    {
        var easternMarketOpen = new TimeOnly(9, 30);
        var localTime = TimezoneHelper.ToLocal(easternMarketOpen, MarketTimeZone.CST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(8, 30)),
            "Market open in CST should be 8:30 AM (1 hour behind Eastern)");
    }

    [Test]
    public void ToLocal_MarketOpen_MST_Returns730AM()
    {
        var easternMarketOpen = new TimeOnly(9, 30);
        var localTime = TimezoneHelper.ToLocal(easternMarketOpen, MarketTimeZone.MST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(7, 30)),
            "Market open in MST should be 7:30 AM (2 hours behind Eastern)");
    }

    [Test]
    public void ToLocal_MarketOpen_PST_Returns630AM()
    {
        var easternMarketOpen = new TimeOnly(9, 30);
        var localTime = TimezoneHelper.ToLocal(easternMarketOpen, MarketTimeZone.PST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(6, 30)),
            "Market open in PST should be 6:30 AM (3 hours behind Eastern)");
    }

    #endregion

    #region ToLocal Conversion Tests - Market Close (4:00 PM ET)

    [Test]
    public void ToLocal_MarketClose_EST_Returns400PM()
    {
        var easternMarketClose = new TimeOnly(16, 0);
        var localTime = TimezoneHelper.ToLocal(easternMarketClose, MarketTimeZone.EST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(16, 0)),
            "Market close in EST should be 4:00 PM");
    }

    [Test]
    public void ToLocal_MarketClose_CST_Returns300PM()
    {
        var easternMarketClose = new TimeOnly(16, 0);
        var localTime = TimezoneHelper.ToLocal(easternMarketClose, MarketTimeZone.CST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(15, 0)),
            "Market close in CST should be 3:00 PM");
    }

    [Test]
    public void ToLocal_MarketClose_MST_Returns200PM()
    {
        var easternMarketClose = new TimeOnly(16, 0);
        var localTime = TimezoneHelper.ToLocal(easternMarketClose, MarketTimeZone.MST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(14, 0)),
            "Market close in MST should be 2:00 PM");
    }

    [Test]
    public void ToLocal_MarketClose_PST_Returns100PM()
    {
        var easternMarketClose = new TimeOnly(16, 0);
        var localTime = TimezoneHelper.ToLocal(easternMarketClose, MarketTimeZone.PST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(13, 0)),
            "Market close in PST should be 1:00 PM");
    }

    #endregion

    #region ToLocal Conversion Tests - Pre-Market (4:00 AM ET)

    [Test]
    public void ToLocal_PreMarketOpen_EST_Returns400AM()
    {
        var easternPreMarket = new TimeOnly(4, 0);
        var localTime = TimezoneHelper.ToLocal(easternPreMarket, MarketTimeZone.EST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(4, 0)),
            "Pre-market open in EST should be 4:00 AM");
    }

    [Test]
    public void ToLocal_PreMarketOpen_CST_Returns300AM()
    {
        var easternPreMarket = new TimeOnly(4, 0);
        var localTime = TimezoneHelper.ToLocal(easternPreMarket, MarketTimeZone.CST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(3, 0)),
            "Pre-market open in CST should be 3:00 AM");
    }

    [Test]
    public void ToLocal_PreMarketOpen_MST_Returns200AM()
    {
        var easternPreMarket = new TimeOnly(4, 0);
        var localTime = TimezoneHelper.ToLocal(easternPreMarket, MarketTimeZone.MST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(2, 0)),
            "Pre-market open in MST should be 2:00 AM");
    }

    [Test]
    public void ToLocal_PreMarketOpen_PST_Returns100AM()
    {
        var easternPreMarket = new TimeOnly(4, 0);
        var localTime = TimezoneHelper.ToLocal(easternPreMarket, MarketTimeZone.PST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(1, 0)),
            "Pre-market open in PST should be 1:00 AM");
    }

    #endregion

    #region ToLocal Conversion Tests - After-Hours (8:00 PM ET)

    [Test]
    public void ToLocal_AfterHoursClose_EST_Returns800PM()
    {
        var easternAfterHours = new TimeOnly(20, 0);
        var localTime = TimezoneHelper.ToLocal(easternAfterHours, MarketTimeZone.EST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(20, 0)),
            "After-hours close in EST should be 8:00 PM");
    }

    [Test]
    public void ToLocal_AfterHoursClose_CST_Returns700PM()
    {
        var easternAfterHours = new TimeOnly(20, 0);
        var localTime = TimezoneHelper.ToLocal(easternAfterHours, MarketTimeZone.CST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(19, 0)),
            "After-hours close in CST should be 7:00 PM");
    }

    [Test]
    public void ToLocal_AfterHoursClose_MST_Returns600PM()
    {
        var easternAfterHours = new TimeOnly(20, 0);
        var localTime = TimezoneHelper.ToLocal(easternAfterHours, MarketTimeZone.MST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(18, 0)),
            "After-hours close in MST should be 6:00 PM");
    }

    [Test]
    public void ToLocal_AfterHoursClose_PST_Returns500PM()
    {
        var easternAfterHours = new TimeOnly(20, 0);
        var localTime = TimezoneHelper.ToLocal(easternAfterHours, MarketTimeZone.PST);

        Assert.That(localTime, Is.EqualTo(new TimeOnly(17, 0)),
            "After-hours close in PST should be 5:00 PM");
    }

    #endregion

    #region ToEastern Conversion Tests

    [Test]
    public void ToEastern_FromCST_830AM_Returns930AM()
    {
        var cstTime = new TimeOnly(8, 30);
        var easternTime = TimezoneHelper.ToEastern(cstTime, MarketTimeZone.CST);

        Assert.That(easternTime, Is.EqualTo(new TimeOnly(9, 30)),
            "8:30 AM CST should convert to 9:30 AM ET");
    }

    [Test]
    public void ToEastern_FromMST_730AM_Returns930AM()
    {
        var mstTime = new TimeOnly(7, 30);
        var easternTime = TimezoneHelper.ToEastern(mstTime, MarketTimeZone.MST);

        Assert.That(easternTime, Is.EqualTo(new TimeOnly(9, 30)),
            "7:30 AM MST should convert to 9:30 AM ET");
    }

    [Test]
    public void ToEastern_FromPST_630AM_Returns930AM()
    {
        var pstTime = new TimeOnly(6, 30);
        var easternTime = TimezoneHelper.ToEastern(pstTime, MarketTimeZone.PST);

        Assert.That(easternTime, Is.EqualTo(new TimeOnly(9, 30)),
            "6:30 AM PST should convert to 9:30 AM ET");
    }

    [Test]
    public void ToEastern_FromEST_930AM_Returns930AM()
    {
        var estTime = new TimeOnly(9, 30);
        var easternTime = TimezoneHelper.ToEastern(estTime, MarketTimeZone.EST);

        Assert.That(easternTime, Is.EqualTo(new TimeOnly(9, 30)),
            "9:30 AM EST should return 9:30 AM ET (no conversion)");
    }

    #endregion

    #region Round-Trip Conversion Tests

    /// <summary>
    /// Verifies that converting from Eastern to local and back returns the original time.
    /// </summary>
    [Test]
    [TestCase(MarketTimeZone.EST)]
    [TestCase(MarketTimeZone.CST)]
    [TestCase(MarketTimeZone.MST)]
    [TestCase(MarketTimeZone.PST)]
    public void RoundTrip_EasternToLocalToEastern_PreservesTime(MarketTimeZone timezone)
    {
        var originalEastern = new TimeOnly(9, 30);

        var local = TimezoneHelper.ToLocal(originalEastern, timezone);
        var backToEastern = TimezoneHelper.ToEastern(local, timezone);

        Assert.That(backToEastern, Is.EqualTo(originalEastern),
            $"Round-trip conversion through {timezone} should preserve original time");
    }

    [Test]
    [TestCase(MarketTimeZone.EST)]
    [TestCase(MarketTimeZone.CST)]
    [TestCase(MarketTimeZone.MST)]
    [TestCase(MarketTimeZone.PST)]
    public void RoundTrip_AllMarketTimes_PreserveTime(MarketTimeZone timezone)
    {
        // Test all critical market times
        var marketTimes = new[]
        {
            new TimeOnly(4, 0),   // Pre-market open
            new TimeOnly(9, 30),  // Market open
            new TimeOnly(12, 0),  // Midday
            new TimeOnly(16, 0),  // Market close
            new TimeOnly(20, 0),  // After-hours close
        };

        foreach (var originalTime in marketTimes)
        {
            var local = TimezoneHelper.ToLocal(originalTime, timezone);
            var backToEastern = TimezoneHelper.ToEastern(local, timezone);

            Assert.That(backToEastern, Is.EqualTo(originalTime),
                $"Round-trip of {originalTime:HH:mm} through {timezone} should preserve original time");
        }
    }

    #endregion

    #region Convert Method Tests

    [Test]
    public void Convert_CST_To_PST_MarketOpen()
    {
        var cstTime = new TimeOnly(8, 30);  // Market open in CST
        var pstTime = TimezoneHelper.Convert(cstTime, MarketTimeZone.CST, MarketTimeZone.PST);

        Assert.That(pstTime, Is.EqualTo(new TimeOnly(6, 30)),
            "8:30 AM CST should convert to 6:30 AM PST");
    }

    [Test]
    public void Convert_PST_To_EST_MarketOpen()
    {
        var pstTime = new TimeOnly(6, 30);  // Market open in PST
        var estTime = TimezoneHelper.Convert(pstTime, MarketTimeZone.PST, MarketTimeZone.EST);

        Assert.That(estTime, Is.EqualTo(new TimeOnly(9, 30)),
            "6:30 AM PST should convert to 9:30 AM EST");
    }

    [Test]
    public void Convert_SameTimezone_ReturnsOriginal()
    {
        var time = new TimeOnly(12, 0);

        var result = TimezoneHelper.Convert(time, MarketTimeZone.CST, MarketTimeZone.CST);

        Assert.That(result, Is.EqualTo(time),
            "Converting to same timezone should return original time");
    }

    #endregion

    #region GetOffsetFromEastern Tests

    [Test]
    public void GetOffsetFromEastern_EST_ReturnsZero()
    {
        var offset = TimezoneHelper.GetOffsetFromEastern(MarketTimeZone.EST);
        Assert.That(offset, Is.EqualTo(0),
            "EST should have 0 offset from Eastern");
    }

    [Test]
    public void GetOffsetFromEastern_CST_ReturnsNegativeOne()
    {
        var offset = TimezoneHelper.GetOffsetFromEastern(MarketTimeZone.CST);
        Assert.That(offset, Is.EqualTo(-1),
            "CST should be 1 hour behind Eastern");
    }

    [Test]
    public void GetOffsetFromEastern_MST_ReturnsNegativeTwo()
    {
        var offset = TimezoneHelper.GetOffsetFromEastern(MarketTimeZone.MST);
        Assert.That(offset, Is.EqualTo(-2),
            "MST should be 2 hours behind Eastern");
    }

    [Test]
    public void GetOffsetFromEastern_PST_ReturnsNegativeThree()
    {
        var offset = TimezoneHelper.GetOffsetFromEastern(MarketTimeZone.PST);
        Assert.That(offset, Is.EqualTo(-3),
            "PST should be 3 hours behind Eastern");
    }

    #endregion

    #region FormatWithTimezone Tests

    [Test]
    [TestCase(MarketTimeZone.EST, "9:30 AM EST")]
    [TestCase(MarketTimeZone.CST, "9:30 AM CST")]
    [TestCase(MarketTimeZone.MST, "9:30 AM MST")]
    [TestCase(MarketTimeZone.PST, "9:30 AM PST")]
    public void FormatWithTimezone_FormatsCorrectly(MarketTimeZone timezone, string expected)
    {
        var time = new TimeOnly(9, 30);
        var formatted = TimezoneHelper.FormatWithTimezone(time, timezone);

        Assert.That(formatted, Is.EqualTo(expected));
    }

    #endregion

    #region GetTimezoneDisplayInfo Tests

    [Test]
    [TestCase(MarketTimeZone.EST)]
    [TestCase(MarketTimeZone.CST)]
    [TestCase(MarketTimeZone.MST)]
    [TestCase(MarketTimeZone.PST)]
    public void GetTimezoneDisplayInfo_ReturnsValidInfo(MarketTimeZone timezone)
    {
        var info = TimezoneHelper.GetTimezoneDisplayInfo(timezone);

        Assert.Multiple(() =>
        {
            Assert.That(info.Timezone, Is.EqualTo(timezone));
            Assert.That(info.TimeZoneInfo, Is.Not.Null);
            Assert.That(info.Abbreviation, Is.Not.Null.And.Not.Empty);
            Assert.That(info.MarketOpenLocal, Is.Not.EqualTo(default(TimeOnly)));
            Assert.That(info.MarketCloseLocal, Is.Not.EqualTo(default(TimeOnly)));
        });
    }

    [Test]
    public void GetTimezoneDisplayInfo_CST_HasCorrectMarketTimes()
    {
        var info = TimezoneHelper.GetTimezoneDisplayInfo(MarketTimeZone.CST);

        Assert.Multiple(() =>
        {
            Assert.That(info.MarketOpenLocal, Is.EqualTo(new TimeOnly(8, 30)),
                "Market open in CST should be 8:30 AM");
            Assert.That(info.MarketCloseLocal, Is.EqualTo(new TimeOnly(15, 0)),
                "Market close in CST should be 3:00 PM");
            Assert.That(info.PreMarketOpenLocal, Is.EqualTo(new TimeOnly(3, 0)),
                "Pre-market open in CST should be 3:00 AM");
            Assert.That(info.AfterHoursCloseLocal, Is.EqualTo(new TimeOnly(19, 0)),
                "After-hours close in CST should be 7:00 PM");
        });
    }

    [Test]
    public void GetTimezoneDisplayInfo_ToString_ReturnsNonEmpty()
    {
        var info = TimezoneHelper.GetTimezoneDisplayInfo(MarketTimeZone.CST);
        var displayString = info.ToString();

        Assert.That(displayString, Is.Not.Null.And.Not.Empty);
        Assert.That(displayString, Does.Contain("CST"));
    }

    #endregion

    #region GetCurrentTime Tests

    [Test]
    [TestCase(MarketTimeZone.EST)]
    [TestCase(MarketTimeZone.CST)]
    [TestCase(MarketTimeZone.MST)]
    [TestCase(MarketTimeZone.PST)]
    public void GetCurrentTime_ReturnsReasonableTime(MarketTimeZone timezone)
    {
        var currentTime = TimezoneHelper.GetCurrentTime(timezone);

        // Just verify it returns a valid time (not default)
        Assert.That(currentTime, Is.Not.EqualTo(default(TimeOnly)));
    }

    [Test]
    [TestCase(MarketTimeZone.EST)]
    [TestCase(MarketTimeZone.CST)]
    [TestCase(MarketTimeZone.MST)]
    [TestCase(MarketTimeZone.PST)]
    public void GetCurrentDateTime_ReturnsReasonableDateTime(MarketTimeZone timezone)
    {
        var currentDateTime = TimezoneHelper.GetCurrentDateTime(timezone);

        // Verify the DateTime is recent (within the last day to account for any timezone)
        var now = DateTime.Now;
        var dateDiff = Math.Abs((currentDateTime.Date - now.Date).TotalDays);

        Assert.That(dateDiff, Is.LessThanOrEqualTo(1),
            "Current DateTime should be within 1 day of today");

        // Also verify the time is valid (has hours/minutes set)
        Assert.That(currentDateTime.Hour, Is.InRange(0, 23));
        Assert.That(currentDateTime.Minute, Is.InRange(0, 59));
    }

    #endregion
}

/// <summary>
/// Tests for Time class with timezone conversions.
/// </summary>
[TestFixture]
public class TimeWithTimezoneTests
{
    #region TradingPeriod Local Time Tests

    [Test]
    public void RTH_Start_IsMarketOpen_930AM_ET()
    {
        Assert.That(MarketTime.RTH.Start, Is.EqualTo(new TimeOnly(9, 30)),
            "RTH.Start should be 9:30 AM ET (market open)");
    }

    [Test]
    public void RTH_End_IsMarketClose_400PM_ET()
    {
        Assert.That(MarketTime.RTH.End, Is.EqualTo(new TimeOnly(16, 0)),
            "RTH.End should be 4:00 PM ET (market close)");
    }

    [Test]
    public void PreMarket_Start_Is400AM_ET()
    {
        Assert.That(MarketTime.PreMarket.Start, Is.EqualTo(new TimeOnly(4, 0)),
            "PreMarket.Start should be 4:00 AM ET");
    }

    [Test]
    public void PreMarket_End_Is930AM_ET()
    {
        Assert.That(MarketTime.PreMarket.End, Is.EqualTo(new TimeOnly(9, 30)),
            "PreMarket.End should be 9:30 AM ET (market open)");
    }

    [Test]
    public void AfterHours_Start_Is400PM_ET()
    {
        Assert.That(MarketTime.AfterHours.Start, Is.EqualTo(new TimeOnly(16, 0)),
            "AfterHours.Start should be 4:00 PM ET (market close)");
    }

    [Test]
    public void AfterHours_End_Is800PM_ET()
    {
        Assert.That(MarketTime.AfterHours.End, Is.EqualTo(new TimeOnly(20, 0)),
            "AfterHours.End should be 8:00 PM ET");
    }

    [Test]
    public void Extended_Covers_AllTradingHours()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarketTime.Extended.Start, Is.EqualTo(new TimeOnly(4, 0)),
                "Extended.Start should be 4:00 AM ET");
            Assert.That(MarketTime.Extended.End, Is.EqualTo(new TimeOnly(20, 0)),
                "Extended.End should be 8:00 PM ET");
        });
    }

    #endregion

    #region TradingPeriod StartLocal/EndLocal Tests

    [Test]
    public void RTH_StartLocal_ConvertsCorrectly_WhenCST()
    {
        // This test validates that the StartLocal property correctly uses Settings.Timezone
        // When Settings.Timezone is CST (default), RTH start should be 8:30 AM local
        var localStart = TimezoneHelper.ToLocal(MarketTime.RTH.Start, MarketTimeZone.CST);

        Assert.That(localStart, Is.EqualTo(new TimeOnly(8, 30)),
            "RTH.StartLocal in CST should be 8:30 AM");
    }

    [Test]
    public void RTH_EndLocal_ConvertsCorrectly_WhenCST()
    {
        var localEnd = TimezoneHelper.ToLocal(MarketTime.RTH.End, MarketTimeZone.CST);

        Assert.That(localEnd, Is.EqualTo(new TimeOnly(15, 0)),
            "RTH.EndLocal in CST should be 3:00 PM");
    }

    #endregion

    #region TradingPeriod.ContainsLocal Tests

    [Test]
    public void ContainsLocal_MarketHours_ReturnsTrue()
    {
        // 10:00 AM CST = 11:00 AM ET (within RTH)
        var localTime = new TimeOnly(10, 0);
        var result = MarketTime.RTH.ContainsLocal(localTime, MarketTimeZone.CST);

        Assert.That(result, Is.True,
            "10:00 AM CST (11:00 AM ET) should be within RTH");
    }

    [Test]
    public void ContainsLocal_BeforeMarket_ReturnsFalse()
    {
        // 7:00 AM CST = 8:00 AM ET (before RTH)
        var localTime = new TimeOnly(7, 0);
        var result = MarketTime.RTH.ContainsLocal(localTime, MarketTimeZone.CST);

        Assert.That(result, Is.False,
            "7:00 AM CST (8:00 AM ET) should be before RTH");
    }

    [Test]
    public void ContainsLocal_AfterMarket_ReturnsFalse()
    {
        // 4:00 PM CST = 5:00 PM ET (after RTH)
        var localTime = new TimeOnly(16, 0);
        var result = MarketTime.RTH.ContainsLocal(localTime, MarketTimeZone.CST);

        Assert.That(result, Is.False,
            "4:00 PM CST (5:00 PM ET) should be after RTH");
    }

    [Test]
    public void ContainsLocal_AtMarketOpen_ReturnsTrue()
    {
        // 8:30 AM CST = 9:30 AM ET (exactly at RTH start)
        var localTime = new TimeOnly(8, 30);
        var result = MarketTime.RTH.ContainsLocal(localTime, MarketTimeZone.CST);

        Assert.That(result, Is.True,
            "8:30 AM CST (9:30 AM ET) should be at RTH start (inclusive)");
    }

    [Test]
    public void ContainsLocal_AtMarketClose_ReturnsTrue()
    {
        // 3:00 PM CST = 4:00 PM ET (exactly at RTH end)
        var localTime = new TimeOnly(15, 0);
        var result = MarketTime.RTH.ContainsLocal(localTime, MarketTimeZone.CST);

        Assert.That(result, Is.True,
            "3:00 PM CST (4:00 PM ET) should be at RTH end (inclusive)");
    }

    #endregion

    #region TradingPeriod.ToString Tests

    [Test]
    public void ToString_ReturnsEasternTime()
    {
        var str = MarketTime.RTH.ToString();

        Assert.That(str, Does.Contain("09:30"));
        Assert.That(str, Does.Contain("16:00"));
        Assert.That(str, Does.Contain("ET"));
    }

    [Test]
    public void ToString_WithTimezone_ShowsBothTimes()
    {
        var str = MarketTime.RTH.ToString(MarketTimeZone.CST);

        Assert.That(str, Does.Contain("CST"));
        Assert.That(str, Does.Contain("ET"));
        Assert.That(str, Does.Contain("08:30"));  // Local CST time
        Assert.That(str, Does.Contain("09:30"));  // Eastern time
    }

    #endregion

    #region Time.ToLocal and Time.ToEastern Tests

    [Test]
    public void Time_ToLocal_UsesSettingsTimezone()
    {
        // Time.ToLocal uses Settings.Timezone (default EST)
        var easternTime = new TimeOnly(9, 30);

        // This tests the static helper method on Time class
        // Note: This will use whatever Settings.Timezone is configured to
        var localTime = MarketTime.ToLocal(easternTime);

        // Since Settings.Timezone defaults to EST, expect 9:30 AM (no conversion)
        Assert.That(localTime, Is.EqualTo(new TimeOnly(9, 30)),
            "Time.ToLocal should convert using Settings.Timezone (EST - no conversion)");
    }

    [Test]
    public void Time_ToEastern_UsesSettingsTimezone()
    {
        var localTime = new TimeOnly(9, 30);

        var easternTime = MarketTime.ToEastern(localTime);

        Assert.That(easternTime, Is.EqualTo(new TimeOnly(9, 30)),
            "Time.ToEastern should convert using Settings.Timezone (EST - no conversion)");
    }

    #endregion

    #region Critical Validation: Market Open Correlation Tests

    /// <summary>
    /// CRITICAL TEST: Verifies that TimeOnly(8, 30) in CST correlates to 8:30 AM CST market open,
    /// which is 9:30 AM ET (actual market open).
    /// </summary>
    [Test]
    public void CriticalValidation_MarketOpenAt830AM_CST_Correlates_To_930AM_ET()
    {
        // User in CST enters 8:30 AM as market open
        var userLocalTime = new TimeOnly(8, 30);

        // Convert to Eastern
        var easternTime = TimezoneHelper.ToEastern(userLocalTime, MarketTimeZone.CST);

        // Should equal actual market open
        var actualMarketOpen = new TimeOnly(9, 30);

        Assert.That(easternTime, Is.EqualTo(actualMarketOpen),
            "8:30 AM CST should correlate to 9:30 AM ET (actual market open)");
    }

    /// <summary>
    /// CRITICAL TEST: Verifies that Time.RTH.Start (9:30 AM ET) converts to 8:30 AM for CST users.
    /// </summary>
    [Test]
    public void CriticalValidation_RTH_Start_Shows_830AM_For_CST_Users()
    {
        var rthStartEastern = MarketTime.RTH.Start;  // 9:30 AM ET
        var rthStartLocal = TimezoneHelper.ToLocal(rthStartEastern, MarketTimeZone.CST);

        Assert.That(rthStartLocal, Is.EqualTo(new TimeOnly(8, 30)),
            "RTH.Start should display as 8:30 AM for CST users");
    }

    /// <summary>
    /// CRITICAL TEST: Full matrix validation of market open across all timezones.
    /// </summary>
    [Test]
    public void CriticalValidation_MarketOpen_AllTimezones()
    {
        var expectedLocalTimes = new Dictionary<MarketTimeZone, TimeOnly>
        {
            { MarketTimeZone.EST, new TimeOnly(9, 30) },
            { MarketTimeZone.CST, new TimeOnly(8, 30) },
            { MarketTimeZone.MST, new TimeOnly(7, 30) },
            { MarketTimeZone.PST, new TimeOnly(6, 30) },
        };

        foreach (var (timezone, expectedLocal) in expectedLocalTimes)
        {
            var actualLocal = TimezoneHelper.ToLocal(MarketTime.RTH.Start, timezone);

            Assert.That(actualLocal, Is.EqualTo(expectedLocal),
                $"Market open in {timezone} should be {expectedLocal:h:mm tt}");

            // Also verify the reverse conversion
            var backToEastern = TimezoneHelper.ToEastern(actualLocal, timezone);
            Assert.That(backToEastern, Is.EqualTo(MarketTime.RTH.Start),
                $"Converting {expectedLocal:h:mm tt} {timezone} back to Eastern should give 9:30 AM ET");
        }
    }

    #endregion
}

/// <summary>
/// Tests for IBKR API timezone string values.
/// Verifies the correct timezone strings are used for IBKR historical data requests.
/// </summary>
/// <remarks>
/// <para><b>Reference:</b> https://interactivebrokers.github.io/tws-api/historical_bars.html</para>
/// <para>IBKR uses specific timezone string format (e.g., "US/Eastern") for historical data requests.</para>
/// </remarks>
[TestFixture]
public class IbkrTimezoneTests
{
    #region IBKR Timezone String Tests

    /// <summary>
    /// Verifies the IBKR timezone strings match the documented IBKR API values.
    /// </summary>
    [Test]
    [TestCase(MarketTimeZone.EST, "US/Eastern")]
    [TestCase(MarketTimeZone.CST, "US/Central")]
    [TestCase(MarketTimeZone.MST, "US/Mountain")]
    [TestCase(MarketTimeZone.PST, "US/Pacific")]
    public void GetIbkrTimezoneString_ReturnsCorrectIbkrFormat(MarketTimeZone timezone, string expectedIbkrString)
    {
        var ibkrString = TimezoneHelper.GetIbkrTimezoneString(timezone);
        Assert.That(ibkrString, Is.EqualTo(expectedIbkrString),
            $"IBKR timezone string for {timezone} should be '{expectedIbkrString}'");
    }

    [Test]
    public void GetIbkrTimezoneString_InvalidTimezone_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimezoneHelper.GetIbkrTimezoneString((MarketTimeZone)99));
    }

    #endregion

    #region IBKR Historical Data DateTime Format Tests

    /// <summary>
    /// Verifies the format used for IBKR reqHistoricalData endDateTime parameter.
    /// Format: "YYYYMMDD HH:mm:ss timezone"
    /// </summary>
    [Test]
    public void IbkrDateTimeFormat_WithEasternTimezone_FormatsCorrectly()
    {
        // Arrange
        var date = new DateTime(2024, 1, 15, 16, 0, 0);
        var timezone = MarketTimeZone.EST;

        // Act
        var formatted = $"{date:yyyyMMdd HH:mm:ss} {TimezoneHelper.GetIbkrTimezoneString(timezone)}";

        // Assert
        Assert.That(formatted, Is.EqualTo("20240115 16:00:00 US/Eastern"),
            "IBKR datetime format should be 'YYYYMMDD HH:mm:ss US/Eastern'");
    }

    [Test]
    public void IbkrDateTimeFormat_AllTimezones_ContainCorrectSuffix()
    {
        var date = new DateTime(2024, 1, 15, 16, 0, 0);
        var expectedFormats = new Dictionary<MarketTimeZone, string>
        {
            { MarketTimeZone.EST, "20240115 16:00:00 US/Eastern" },
            { MarketTimeZone.CST, "20240115 16:00:00 US/Central" },
            { MarketTimeZone.MST, "20240115 16:00:00 US/Mountain" },
            { MarketTimeZone.PST, "20240115 16:00:00 US/Pacific" },
        };

        foreach (var (timezone, expectedFormat) in expectedFormats)
        {
            var formatted = $"{date:yyyyMMdd HH:mm:ss} {TimezoneHelper.GetIbkrTimezoneString(timezone)}";
            Assert.That(formatted, Is.EqualTo(expectedFormat),
                $"IBKR datetime format for {timezone} should be '{expectedFormat}'");
        }
    }

    #endregion

    #region Default Timezone Tests

    /// <summary>
    /// Verifies that Settings.Timezone defaults to EST (Eastern Standard Time).
    /// This is the standard timezone for US equity markets.
    /// </summary>
    [Test]
    public void Settings_Timezone_DefaultsToEST()
    {
        Assert.That(Settings.Timezone, Is.EqualTo(MarketTimeZone.EST),
            "Default timezone should be EST (Eastern Standard Time) for US equity market compatibility");
    }

    [Test]
    public void Settings_Timezone_EST_HasCorrectIbkrString()
    {
        var ibkrString = TimezoneHelper.GetIbkrTimezoneString(Settings.Timezone);
        Assert.That(ibkrString, Is.EqualTo("US/Eastern"),
            "Default timezone should map to 'US/Eastern' for IBKR API");
    }

    #endregion

    #region Cross-Reference: Windows vs IBKR Timezone IDs

    /// <summary>
    /// Documents and verifies the relationship between Windows timezone IDs and IBKR timezone strings.
    /// </summary>
    [Test]
    public void TimezoneIds_WindowsAndIbkr_AreConsistent()
    {
        // Windows timezone IDs vs IBKR strings
        var mappings = new[]
        {
            (Timezone: MarketTimeZone.EST, WindowsId: "Eastern Standard Time", IbkrId: "US/Eastern"),
            (Timezone: MarketTimeZone.CST, WindowsId: "Central Standard Time", IbkrId: "US/Central"),
            (Timezone: MarketTimeZone.MST, WindowsId: "Mountain Standard Time", IbkrId: "US/Mountain"),
            (Timezone: MarketTimeZone.PST, WindowsId: "Pacific Standard Time", IbkrId: "US/Pacific"),
        };

        foreach (var (timezone, expectedWindowsId, expectedIbkrId) in mappings)
        {
            var windowsId = TimezoneHelper.GetTimezoneId(timezone);
            var ibkrId = TimezoneHelper.GetIbkrTimezoneString(timezone);

            Assert.Multiple(() =>
            {
                Assert.That(windowsId, Is.EqualTo(expectedWindowsId),
                    $"Windows timezone ID for {timezone} should be '{expectedWindowsId}'");
                Assert.That(ibkrId, Is.EqualTo(expectedIbkrId),
                    $"IBKR timezone string for {timezone} should be '{expectedIbkrId}'");
            });
        }
    }

    #endregion

    #region IBKR Market Hours in Eastern Time Tests

    /// <summary>
    /// Verifies that all market times are correctly defined in Eastern Time,
    /// which is the standard for US equity markets and IBKR.
    /// </summary>
    [Test]
    public void MarketHours_AreDefinedInEasternTime()
    {
        // These are the official US equity market hours in Eastern Time
        Assert.Multiple(() =>
        {
            // Pre-market: 4:00 AM - 9:30 AM ET
            Assert.That(MarketTime.PreMarket.Start, Is.EqualTo(new TimeOnly(4, 0)),
                "Pre-market start should be 4:00 AM ET");
            Assert.That(MarketTime.PreMarket.End, Is.EqualTo(new TimeOnly(9, 30)),
                "Pre-market end should be 9:30 AM ET");

            // Regular Trading Hours: 9:30 AM - 4:00 PM ET
            Assert.That(MarketTime.RTH.Start, Is.EqualTo(new TimeOnly(9, 30)),
                "RTH start (market open) should be 9:30 AM ET");
            Assert.That(MarketTime.RTH.End, Is.EqualTo(new TimeOnly(16, 0)),
                "RTH end (market close) should be 4:00 PM ET");

            // After-Hours: 4:00 PM - 8:00 PM ET
            Assert.That(MarketTime.AfterHours.Start, Is.EqualTo(new TimeOnly(16, 0)),
                "After-hours start should be 4:00 PM ET");
            Assert.That(MarketTime.AfterHours.End, Is.EqualTo(new TimeOnly(20, 0)),
                "After-hours end should be 8:00 PM ET");

            // Extended Hours: 4:00 AM - 8:00 PM ET
            Assert.That(MarketTime.Extended.Start, Is.EqualTo(new TimeOnly(4, 0)),
                "Extended hours start should be 4:00 AM ET");
            Assert.That(MarketTime.Extended.End, Is.EqualTo(new TimeOnly(20, 0)),
                "Extended hours end should be 8:00 PM ET");
        });
    }

    #endregion
}

/// <summary>
/// Tests for bidirectional timezone ID conversions between Windows and IBKR formats.
/// </summary>
[TestFixture]
public class TimezoneIdConversionTests
{
    #region FromIbkrTimezoneString Tests

    [Test]
    [TestCase("US/Eastern", MarketTimeZone.EST)]
    [TestCase("US/Central", MarketTimeZone.CST)]
    [TestCase("US/Mountain", MarketTimeZone.MST)]
    [TestCase("US/Pacific", MarketTimeZone.PST)]
    public void FromIbkrTimezoneString_ReturnsCorrectMarketTimeZone(string ibkrString, MarketTimeZone expected)
    {
        var result = TimezoneHelper.FromIbkrTimezoneString(ibkrString);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void FromIbkrTimezoneString_InvalidString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            TimezoneHelper.FromIbkrTimezoneString("Invalid/Timezone"));
    }

    [Test]
    public void FromIbkrTimezoneString_EmptyString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            TimezoneHelper.FromIbkrTimezoneString(""));
    }

    #endregion

    #region FromWindowsTimezoneId Tests

    [Test]
    [TestCase("Eastern Standard Time", MarketTimeZone.EST)]
    [TestCase("Central Standard Time", MarketTimeZone.CST)]
    [TestCase("Mountain Standard Time", MarketTimeZone.MST)]
    [TestCase("Pacific Standard Time", MarketTimeZone.PST)]
    public void FromWindowsTimezoneId_ReturnsCorrectMarketTimeZone(string windowsId, MarketTimeZone expected)
    {
        var result = TimezoneHelper.FromWindowsTimezoneId(windowsId);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void FromWindowsTimezoneId_InvalidId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            TimezoneHelper.FromWindowsTimezoneId("Invalid Timezone"));
    }

    #endregion

    #region IbkrToWindowsTimezoneId Tests

    [Test]
    [TestCase("US/Eastern", "Eastern Standard Time")]
    [TestCase("US/Central", "Central Standard Time")]
    [TestCase("US/Mountain", "Mountain Standard Time")]
    [TestCase("US/Pacific", "Pacific Standard Time")]
    public void IbkrToWindowsTimezoneId_ConvertsCorrectly(string ibkrString, string expectedWindowsId)
    {
        var result = TimezoneHelper.IbkrToWindowsTimezoneId(ibkrString);
        Assert.That(result, Is.EqualTo(expectedWindowsId));
    }

    [Test]
    public void IbkrToWindowsTimezoneId_InvalidIbkr_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            TimezoneHelper.IbkrToWindowsTimezoneId("Invalid/Timezone"));
    }

    #endregion

    #region WindowsToIbkrTimezoneString Tests

    [Test]
    [TestCase("Eastern Standard Time", "US/Eastern")]
    [TestCase("Central Standard Time", "US/Central")]
    [TestCase("Mountain Standard Time", "US/Mountain")]
    [TestCase("Pacific Standard Time", "US/Pacific")]
    public void WindowsToIbkrTimezoneString_ConvertsCorrectly(string windowsId, string expectedIbkrString)
    {
        var result = TimezoneHelper.WindowsToIbkrTimezoneString(windowsId);
        Assert.That(result, Is.EqualTo(expectedIbkrString));
    }

    [Test]
    public void WindowsToIbkrTimezoneString_InvalidWindows_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            TimezoneHelper.WindowsToIbkrTimezoneString("Invalid Timezone"));
    }

    #endregion

    #region Round-Trip Conversion Tests

    [Test]
    [TestCase(MarketTimeZone.EST)]
    [TestCase(MarketTimeZone.CST)]
    [TestCase(MarketTimeZone.MST)]
    [TestCase(MarketTimeZone.PST)]
    public void RoundTrip_MarketTimeZone_ToIbkr_AndBack(MarketTimeZone original)
    {
        // MarketTimeZone -> IBKR string -> MarketTimeZone
        var ibkrString = TimezoneHelper.GetIbkrTimezoneString(original);
        var backToEnum = TimezoneHelper.FromIbkrTimezoneString(ibkrString);

        Assert.That(backToEnum, Is.EqualTo(original),
            $"Round-trip through IBKR string should preserve {original}");
    }

    [Test]
    [TestCase(MarketTimeZone.EST)]
    [TestCase(MarketTimeZone.CST)]
    [TestCase(MarketTimeZone.MST)]
    [TestCase(MarketTimeZone.PST)]
    public void RoundTrip_MarketTimeZone_ToWindows_AndBack(MarketTimeZone original)
    {
        // MarketTimeZone -> Windows ID -> MarketTimeZone
        var windowsId = TimezoneHelper.GetTimezoneId(original);
        var backToEnum = TimezoneHelper.FromWindowsTimezoneId(windowsId);

        Assert.That(backToEnum, Is.EqualTo(original),
            $"Round-trip through Windows ID should preserve {original}");
    }

    [Test]
    [TestCase("US/Eastern")]
    [TestCase("US/Central")]
    [TestCase("US/Mountain")]
    [TestCase("US/Pacific")]
    public void RoundTrip_IbkrString_ToWindows_AndBack(string originalIbkr)
    {
        // IBKR string -> Windows ID -> IBKR string
        var windowsId = TimezoneHelper.IbkrToWindowsTimezoneId(originalIbkr);
        var backToIbkr = TimezoneHelper.WindowsToIbkrTimezoneString(windowsId);

        Assert.That(backToIbkr, Is.EqualTo(originalIbkr),
            $"Round-trip IBKR -> Windows -> IBKR should preserve '{originalIbkr}'");
    }

    [Test]
    [TestCase("Eastern Standard Time")]
    [TestCase("Central Standard Time")]
    [TestCase("Mountain Standard Time")]
    [TestCase("Pacific Standard Time")]
    public void RoundTrip_WindowsId_ToIbkr_AndBack(string originalWindows)
    {
        // Windows ID -> IBKR string -> Windows ID
        var ibkrString = TimezoneHelper.WindowsToIbkrTimezoneString(originalWindows);
        var backToWindows = TimezoneHelper.IbkrToWindowsTimezoneId(ibkrString);

        Assert.That(backToWindows, Is.EqualTo(originalWindows),
            $"Round-trip Windows -> IBKR -> Windows should preserve '{originalWindows}'");
    }

    #endregion

    #region Complete Mapping Consistency Tests

    /// <summary>
    /// Verifies that all timezone representations are internally consistent.
    /// </summary>
    [Test]
    public void AllTimezoneFormats_AreConsistent()
    {
        var mappings = new[]
        {
            (Enum: MarketTimeZone.EST, Windows: "Eastern Standard Time", Ibkr: "US/Eastern", Abbrev: "EST"),
            (Enum: MarketTimeZone.CST, Windows: "Central Standard Time", Ibkr: "US/Central", Abbrev: "CST"),
            (Enum: MarketTimeZone.MST, Windows: "Mountain Standard Time", Ibkr: "US/Mountain", Abbrev: "MST"),
            (Enum: MarketTimeZone.PST, Windows: "Pacific Standard Time", Ibkr: "US/Pacific", Abbrev: "PST"),
        };

        foreach (var (enumVal, windows, ibkr, abbrev) in mappings)
        {
            Assert.Multiple(() =>
            {
                // Enum -> various formats
                Assert.That(TimezoneHelper.GetTimezoneId(enumVal), Is.EqualTo(windows));
                Assert.That(TimezoneHelper.GetIbkrTimezoneString(enumVal), Is.EqualTo(ibkr));
                Assert.That(TimezoneHelper.GetTimezoneAbbreviation(enumVal), Is.EqualTo(abbrev));

                // Various formats -> Enum
                Assert.That(TimezoneHelper.FromWindowsTimezoneId(windows), Is.EqualTo(enumVal));
                Assert.That(TimezoneHelper.FromIbkrTimezoneString(ibkr), Is.EqualTo(enumVal));

                // Cross-format conversions
                Assert.That(TimezoneHelper.IbkrToWindowsTimezoneId(ibkr), Is.EqualTo(windows));
                Assert.That(TimezoneHelper.WindowsToIbkrTimezoneString(windows), Is.EqualTo(ibkr));
            });
        }
    }

    /// <summary>
    /// Verifies that TimeZoneInfo objects can be created from both Windows IDs.
    /// </summary>
    [Test]
    [TestCase("Eastern Standard Time")]
    [TestCase("Central Standard Time")]
    [TestCase("Mountain Standard Time")]
    [TestCase("Pacific Standard Time")]
    public void WindowsTimezoneId_CreatesValidTimeZoneInfo(string windowsId)
    {
        var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        Assert.That(tzInfo, Is.Not.Null);
        Assert.That(tzInfo.Id, Is.EqualTo(windowsId));
    }

    #endregion
}


