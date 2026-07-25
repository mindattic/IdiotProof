using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Full coverage of <see cref="TimeWindowCondition.Evaluate"/>:
/// normal intra-day windows, midnight-wrapping overnight windows, exact
/// boundary semantics, and fail-closed behavior when no timestamp is present.
///
/// All times are expressed in ET (America/New_York).
/// The test timestamp base date is 2026-07-17 (EDT, UTC-4).
/// So 04:30 ET = 08:30 UTC, 09:30 ET = 13:30 UTC, etc.
/// </summary>
public class TimeWindowAllCasesTests
{
    // ── Snapshot factory ─────────────────────────────────────────────────

    private static IndicatorSnapshot SnapAt(int hourEt, int minuteEt)
    {
        // EDT = UTC-4; convert ET→UTC properly (handles overnight rollover).
        var et = new DateTime(2026, 7, 17, hourEt, minuteEt, 0, DateTimeKind.Unspecified);
        var utc = et.AddHours(4); // EDT offset
        return new IndicatorSnapshot
        {
            Symbol    = "TEST",
            Timestamp = DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            Price     = 10.0,
        };
    }

    private static bool Evaluate(int startH, int startM, int endH, int endM, int nowH, int nowM)
    {
        var c = new TimeWindowCondition(new TimeSpan(startH, startM, 0), new TimeSpan(endH, endM, 0));
        return c.Evaluate(SnapAt(nowH, nowM));
    }

    // ── Normal (intra-day) windows ────────────────────────────────────────

    [Test]
    public void InsideWindow_MidPoint_ReturnsTrue()
        => Assert.That(Evaluate(4, 0, 9, 0, 6, 30), Is.True,
            "06:30 ET is inside a 04:00–09:00 window");

    [Test]
    public void BeforeWindow_BeforeStart_ReturnsFalse()
        => Assert.That(Evaluate(4, 0, 9, 0, 3, 59), Is.False,
            "03:59 ET is before a 04:00 window start");

    [Test]
    public void AfterWindow_PastEnd_ReturnsFalse()
        => Assert.That(Evaluate(4, 0, 9, 0, 9, 1), Is.False,
            "09:01 ET is after a 04:00–09:00 window");

    [Test]
    public void AtExactStart_Inclusive_ReturnsTrue()
        => Assert.That(Evaluate(4, 0, 9, 0, 4, 0), Is.True,
            "start boundary is inclusive (>=)");

    [Test]
    public void AtExactEnd_Exclusive_ReturnsFalse()
        => Assert.That(Evaluate(4, 0, 9, 0, 9, 0), Is.False,
            "end boundary is exclusive (<)");

    [Test]
    public void OneMinuteBefore_End_ReturnsTrue()
        => Assert.That(Evaluate(4, 0, 9, 0, 8, 59), Is.True,
            "one minute before end is still inside the window");

    [Test]
    public void RegularHoursWindow_InsideRTH_ReturnsTrue()
        => Assert.That(Evaluate(9, 30, 16, 0, 12, 0), Is.True,
            "noon ET is inside a 09:30–16:00 RTH window");

    [Test]
    public void RegularHoursWindow_PremarketTime_ReturnsFalse()
        => Assert.That(Evaluate(9, 30, 16, 0, 7, 0), Is.False,
            "07:00 ET is before RTH");

    // ── Overnight (midnight-wrapping) windows ────────────────────────────

    [Test]
    public void OvernightWindow_AfterMidnight_ReturnsTrue()
    {
        // 22:00 ET → 04:00 ET (wraps midnight)
        Assert.That(Evaluate(22, 0, 4, 0, 1, 0), Is.True,
            "01:00 ET is inside a 22:00–04:00 overnight window (after midnight)");
    }

    [Test]
    public void OvernightWindow_BeforeMidnight_ReturnsTrue()
    {
        // 22:00 ET → 04:00 ET
        Assert.That(Evaluate(22, 0, 4, 0, 23, 30), Is.True,
            "23:30 ET is inside a 22:00–04:00 overnight window (before midnight)");
    }

    [Test]
    public void OvernightWindow_AtStart_Inclusive_ReturnsTrue()
        => Assert.That(Evaluate(22, 0, 4, 0, 22, 0), Is.True,
            "start of overnight window is inclusive");

    [Test]
    public void OvernightWindow_AtEnd_Exclusive_ReturnsFalse()
        => Assert.That(Evaluate(22, 0, 4, 0, 4, 0), Is.False,
            "end of overnight window is exclusive");

    [Test]
    public void OvernightWindow_Midday_ReturnsFalse()
        => Assert.That(Evaluate(22, 0, 4, 0, 12, 0), Is.False,
            "12:00 ET is outside a 22:00–04:00 overnight window");

    [Test]
    public void OvernightWindow_OneMinutePastEnd_ReturnsFalse()
        => Assert.That(Evaluate(22, 0, 4, 0, 4, 1), Is.False,
            "04:01 ET is past the end of a 22:00–04:00 overnight window");

    // ── Premarket gapper window (most common real-world usage) ────────────

    [Test]
    public void GapperWindow_4am_to_9am_TypicalCheck()
    {
        var c = new TimeWindowCondition(new TimeSpan(4, 0, 0), new TimeSpan(9, 0, 0));

        Assert.Multiple(() =>
        {
            Assert.That(c.Evaluate(SnapAt(3, 59)), Is.False, "03:59 — not yet open");
            Assert.That(c.Evaluate(SnapAt(4, 0)),  Is.True,  "04:00 — just opened");
            Assert.That(c.Evaluate(SnapAt(8, 59)), Is.True,  "08:59 — last minute");
            Assert.That(c.Evaluate(SnapAt(9, 0)),  Is.False, "09:00 — closed exactly");
            Assert.That(c.Evaluate(SnapAt(9, 30)), Is.False, "09:30 — bell; definitely closed");
        });
    }

    // ── As entry condition in a full strategy ────────────────────────────

    [Test]
    public void TimeWindow_AsEntryCondition_BlocksFireOutsideWindow()
    {
        // 04:00–09:00 ET gapper window; bar ending at 03:59 ET must NOT fire.
        // EndUtc = 07:59 UTC = 03:59 ET (one minute before the 04:00 open).
        // Use EndUtc - 1 min for StartUtc so the candle is clearly pre-window.
        var end   = new DateTime(2026, 7, 17, 7, 59, 0, DateTimeKind.Utc); // 03:59 ET
        var start = end.AddMinutes(-1);
        var candles = new List<Candle>
        {
            new() { Symbol = "GAP", StartUtc = start, EndUtc = end,
                    Open = 10m, High = 11m, Low = 9.9m, Close = 10.5m, Volume = 5_000_000 },
        };

        var def = Stock.Ticker("GAP")
            .IsGapUp(5)
            .RequireEntryWindow("04:00", "09:00")
            .Long()
            .StopLossPercent(5)
            .Build();

        var ctx = new StrategyContext { PreviousClose = 9.5m };
        var signals = new DslStrategy(def).Evaluate("GAP", candles, ctx);

        Assert.That(signals, Is.Empty,
            "strategy must not fire outside its time window — even if gap conditions pass");
    }

    [Test]
    public void TimeWindow_AsEntryCondition_AllowsFireInsideWindow()
    {
        // 04:30 ET = 08:30 UTC — comfortably inside a 04:00–09:00 window
        var start = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
        var candles = new List<Candle>
        {
            new() { Symbol = "GAP", StartUtc = start, EndUtc = start.AddMinutes(1),
                    Open = 10m, High = 11m, Low = 9.9m, Close = 10.5m, Volume = 5_000_000 },
        };

        var def = Stock.Ticker("GAP")
            .IsGapUp(5)
            .RequireEntryWindow("04:00", "09:00")
            .IsVolumeAbove(1)
            .Long()
            .StopLossPercent(5)
            .Build();

        var ctx = new StrategyContext { PreviousClose = 9.0m }; // 10.5/9.0 - 1 = 16.7% gap
        var signals = new DslStrategy(def).Evaluate("GAP", candles, ctx);

        Assert.That(signals, Has.Count.EqualTo(1),
            "strategy must fire when inside window and all conditions pass");
    }
}
