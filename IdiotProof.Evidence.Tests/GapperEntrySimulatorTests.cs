using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T453 track 4: each entry kind triggers on bars after the detection only, at the right price, and gives up after its window. 2026-07-17 is EDT.</summary>
public class GapperEntrySimulatorTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static Candle Bar(int h, int m, double open, double close, double? low = null, double? high = null, double volume = 100) => new()
    {
        Symbol = "T", StartUtc = Et(h, m), EndUtc = Et(h, m).AddMinutes(1),
        Open = (decimal)open, High = (decimal)(high ?? Math.Max(open, close)), Low = (decimal)(low ?? Math.Min(open, close)), Close = (decimal)close, Volume = (decimal)volume,
    };

    /// <summary>A climb from 10.00 to 10.29 over 08:00-08:29 (premarket high 10.29 by 08:30).</summary>
    private static List<Candle> Climb()
    {
        var bars = new List<Candle>();
        for (int m = 0; m < 30; m++) bars.Add(Bar(8, m, 10 + 0.01 * m, 10 + 0.01 * m));
        return bars;
    }

    [Test]
    public void Now_IsTheOpenOfTheFirstTradedBarAtOrAfterT()
    {
        var bars = Climb();
        bars.Add(Bar(8, 31, 10.40, 10.45));
        var e = GapperEntrySimulator.Find(GapperEntryKind.Now, bars, Day, Et(8, 30))!;
        Assert.Multiple(() =>
        {
            Assert.That(e.BarStartUtc, Is.EqualTo(Et(8, 31)), "08:30 had no trade");
            Assert.That(e.Fill, Is.EqualTo(10.40m));
        });
    }

    [Test]
    public void Breakout_FillsAtThePremarketHighAsOfT_OrTheOpenIfItGappedOver()
    {
        var bars = Climb();
        bars.Add(Bar(8, 30, 10.20, 10.25));
        bars.Add(Bar(8, 31, 10.25, 10.35, high: 10.36));
        var e = GapperEntrySimulator.Find(GapperEntryKind.PmHighBreakout, bars, Day, Et(8, 30))!;
        Assert.Multiple(() =>
        {
            Assert.That(e.BarStartUtc, Is.EqualTo(Et(8, 31)));
            Assert.That(e.Fill, Is.EqualTo(10.29m), "the level, not the bar's high");
        });

        var gapped = Climb();
        gapped.Add(Bar(8, 30, 10.50, 10.55));
        Assert.That(GapperEntrySimulator.Find(GapperEntryKind.PmHighBreakout, gapped, Day, Et(8, 30))!.Fill, Is.EqualTo(10.50m));
    }

    [Test]
    public void Pullback_TouchesTheVwapThroughThePreviousBar_AndGivesUpAfterTheWindow()
    {
        var bars = Climb();
        bars.Add(Bar(8, 30, 10.30, 10.30));
        bars.Add(Bar(8, 31, 10.25, 10.10, low: 10.05));
        var e = GapperEntrySimulator.Find(GapperEntryKind.VwapPullback, bars, Day, Et(8, 30))!;
        Assert.Multiple(() =>
        {
            Assert.That(e.BarStartUtc, Is.EqualTo(Et(8, 31)), "the 08:30 bar stays above VWAP");
            Assert.That(e.Fill, Is.LessThan(10.25m), "fills at the VWAP, below the open");
            Assert.That(e.Fill, Is.GreaterThan(10.05m));
        });

        var late = Climb();
        for (int m = 30; m < 60; m++) late.Add(Bar(8, m, 10.30, 10.30));
        late.Add(Bar(9, 5, 10.00, 9.90));
        Assert.That(GapperEntrySimulator.Find(GapperEntryKind.VwapPullback, late, Day, Et(8, 30), windowMinutes: 30), Is.Null, "the touch at 09:05 is after the 30-minute window");
    }

    [Test]
    public void NoBarsAfterT_IsNoEntry()
    {
        Assert.That(GapperEntrySimulator.Find(GapperEntryKind.Now, Climb(), Day, Et(8, 30)), Is.Null);
    }
}
