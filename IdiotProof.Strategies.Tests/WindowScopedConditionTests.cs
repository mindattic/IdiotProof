using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-A20 regressions: window-scoped memory for per-tick evaluators.
/// The Monitor re-materializes conditions every tick, so latch-style verbs
/// (Breakout/Pullback, HoldsAbove/HoldsBelow) need the snapshot's
/// WindowHigh/WindowLow to remember what price did earlier in the window —
/// without it Breakout could never pass live (IP-A18 made it fail closed)
/// while the Learning Center's example strategies are all built on it.
/// </summary>
public class WindowScopedConditionTests
{
    private static IndicatorSnapshot Snap(double price, double? windowHigh = null, double? windowLow = null, double? barLow = null) => new()
    {
        Symbol = "TEST",
        Timestamp = new DateTime(2026, 7, 17, 14, 0, 0, DateTimeKind.Utc),
        Price = price,
        WindowHigh = windowHigh,
        WindowLow = windowLow,
        BarLow = barLow,
    };

    [Test]
    public void Breakout_WindowSawTheLevel_Passes_OtherwiseFailsClosed()
    {
        var breakout = new PatternCondition(PatternType.Breakout, 3.68);
        Assert.Multiple(() =>
        {
            Assert.That(breakout.Evaluate(Snap(3.60, windowHigh: 3.75)), Is.True,
                "the level traded earlier in the window — breakout latched");
            Assert.That(breakout.Evaluate(Snap(3.60, windowHigh: 3.65)), Is.False,
                "the window never reached the level");
            Assert.That(breakout.Evaluate(Snap(3.80)), Is.False,
                "no window data → fail closed, even with price above the level");
            Assert.That(new PatternCondition(PatternType.Breakout).Evaluate(Snap(3.80, windowHigh: 99)), Is.False,
                "Breakout() with no level never latches — same as the backtester's tracker");
        });
    }

    [Test]
    public void Pullback_RetestsSupport_OrAnyRetracement()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new PatternCondition(PatternType.Pullback, 3.55).Evaluate(
                Snap(3.60, windowHigh: 3.80, barLow: 3.54)), Is.True,
                "bar low retested the given support");
            Assert.That(new PatternCondition(PatternType.Pullback, 3.55).Evaluate(
                Snap(3.70, windowHigh: 3.80, barLow: 3.68)), Is.False,
                "never came back to the support");
            Assert.That(new PatternCondition(PatternType.Pullback).Evaluate(
                Snap(3.70, windowHigh: 3.80)), Is.True,
                "no support given: any retracement below the window high");
            Assert.That(new PatternCondition(PatternType.Pullback).Evaluate(Snap(3.70)), Is.False,
                "no window data → fail closed");
        });
    }

    [Test]
    public void HoldsAbove_SeesEarlierViolations_ThroughWindowLow()
    {
        // Fresh instance per evaluation (the Monitor's reality): without the
        // window low this degraded to "currently above" and an earlier dip
        // through the level was invisible.
        Assert.Multiple(() =>
        {
            Assert.That(new PriceLevelCondition(PriceLevelType.HoldsAbove, 0.48).Evaluate(
                Snap(0.50, windowLow: 0.49)), Is.True, "held all window");
            Assert.That(new PriceLevelCondition(PriceLevelType.HoldsAbove, 0.48).Evaluate(
                Snap(0.50, windowLow: 0.40)), Is.False, "dipped through the level earlier — violated");
        });
    }

    [Test]
    public void SnapshotBuilder_PopulatesWindowExtremes()
    {
        var start = new DateTime(2026, 7, 17, 13, 30, 0, DateTimeKind.Utc);
        List<Candle> bars =
        [
            new() { Symbol = "T", StartUtc = start, EndUtc = start.AddMinutes(1), Open = 10, High = 12, Low = 9.5m, Close = 10, Volume = 1 },
            new() { Symbol = "T", StartUtc = start.AddMinutes(1), EndUtc = start.AddMinutes(2), Open = 10, High = 10.5m, Low = 10, Close = 10.2m, Volume = 1 },
        ];
        var snap = IndicatorSnapshotBuilder.BuildWithEmas("T", bars, []);
        Assert.Multiple(() =>
        {
            Assert.That(snap.WindowHigh, Is.EqualTo(12.0));
            Assert.That(snap.WindowLow, Is.EqualTo(9.5));
        });
    }

    [Test]
    public void GapperExit_PositionHeldPastItsDay_FlattensImmediately()
    {
        // A SellBy("09:28") position that survived past midnight ET (exits
        // kept failing / market closed) used to WAIT until 09:28 the next
        // morning because the plain time-of-day check saw 04:05 < 09:28 —
        // hours of unwanted overnight exposure with only the stop active.
        var def = Stock.Ticker("T").Long().Quantity(10).SellBy("09:28").Build();

        var entryUtc = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc); // Fri 08:00 ET
        var nextDay4amEt = new DateTime(2026, 7, 18, 8, 5, 0, DateTimeKind.Utc); // Sat 04:05 ET
        List<Candle> bars =
        [
            new() { Symbol = "T", StartUtc = entryUtc.AddMinutes(1), EndUtc = entryUtc.AddMinutes(2),
                    Open = 10, High = 10.1m, Low = 9.9m, Close = 10, Volume = 1 },
        ];

        var decision = GapperExitEvaluator.Evaluate(def, 10.0, entryUtc, bars, nextDay4amEt);

        Assert.That(decision, Is.Not.Null, "held past its day — must flatten at the first evaluated instant");
        Assert.That(decision!.Reason, Is.EqualTo(GapperExitReason.SellByTime));
    }

    [Test]
    public async Task MockFeed_EmitsNoWeekendMinuteBars()
    {
        // 2026-07-18 is a Saturday. The daily branch already skipped
        // weekends; the minute branch synthesized phantom bars.
        var feed = new MockDataFeed();
        var start = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var bars = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync("GAPT", start, start.AddHours(2), TimeSpan.FromMinutes(1)))
            bars.Add(c);
        Assert.That(bars, Is.Empty);
    }
}
