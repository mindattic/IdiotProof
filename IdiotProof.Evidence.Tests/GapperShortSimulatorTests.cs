using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T476 fade short: the stop fills at the worse of the stop and the bar's open (a gap through it fills at the open), the
/// Rule 201 uptick entry waits for an open above the prior close, the borrow charge is APR x days / 360 with an intraday
/// short charged one day, and the signal reads only bars ending by the decision. 2026-07-17 is EDT: 04:00 ET = 08:00 UTC.
/// </summary>
public class GapperShortSimulatorTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static Candle Bar(DateTime startUtc, decimal open, decimal close, decimal? high = null, decimal? low = null) => new()
    {
        Symbol = "T", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1), Open = open, Close = close,
        High = high ?? Math.Max(open, close), Low = low ?? Math.Min(open, close), Volume = 1000,
    };

    [Test]
    public void Stop_FillsAtTheStop_WhenTradedThrough()
    {
        var bars = new List<Candle> { Bar(Et(9, 45), 10m, 10.2m), Bar(Et(9, 46), 10.2m, 10.5m, high: 11.5m), Bar(Et(9, 47), 10.5m, 10.4m) };
        var trade = GapperShortSimulator.Simulate(new GapperShortExit(new TimeSpan(15, 55, 0), 10, false), bars, Day, Et(9, 45), 10m)!;
        Assert.Multiple(() =>
        {
            Assert.That(trade.ExitReason, Is.EqualTo("stop"));
            Assert.That(trade.Exit, Is.EqualTo(11m));
            Assert.That(trade.GrossReturn, Is.EqualTo(-0.10).Within(1e-9));
            Assert.That(trade.GapFill, Is.False);
        });
    }

    [Test]
    public void Stop_FillsAtTheOpen_WhenAHaltGapsThroughIt()
    {
        // Halted 09:47-09:51; reopens at 13, over the 11 stop.
        var bars = new List<Candle> { Bar(Et(9, 45), 10m, 10.2m), Bar(Et(9, 46), 10.2m, 10.6m), Bar(Et(9, 52), 13m, 12.5m) };
        var trade = GapperShortSimulator.Simulate(new GapperShortExit(new TimeSpan(15, 55, 0), 10, false), bars, Day, Et(9, 45), 10m)!;
        Assert.Multiple(() =>
        {
            Assert.That(trade.Exit, Is.EqualTo(13m));
            Assert.That(trade.GrossReturn, Is.EqualTo(-0.30).Within(1e-9));
            Assert.That(trade.GapFill, Is.True);
        });
    }

    [Test]
    public void Clock_CoversAtTheFirstOpenAtOrAfterTheCoverTime()
    {
        var bars = new List<Candle> { Bar(Et(9, 45), 10m, 9.5m), Bar(Et(10, 59), 9m, 8.8m), Bar(Et(11, 0), 8.5m, 8.4m) };
        var trade = GapperShortSimulator.Simulate(new GapperShortExit(new TimeSpan(11, 0, 0), null, false), bars, Day, Et(9, 45), 10m)!;
        Assert.Multiple(() =>
        {
            Assert.That(trade.ExitReason, Is.EqualTo("cover-by 11:00"));
            Assert.That(trade.Exit, Is.EqualTo(8.5m));
            Assert.That(trade.GrossReturn, Is.EqualTo(0.15).Within(1e-9));
        });
    }

    [Test]
    public void SsrUptick_WaitsForAnOpenAboveThePriorClose()
    {
        var bars = new List<Candle>
        {
            Bar(Et(9, 44), 10m, 10m),
            Bar(Et(9, 45), 9.9m, 9.8m),   // downtick open: not fillable under SSR
            Bar(Et(9, 46), 9.8m, 9.7m),   // open equal to the prior close: not an uptick
            Bar(Et(9, 47), 9.75m, 9.9m),  // open above 9.70: the first uptick
        };
        var free = GapperShortSimulator.Entry(bars, Et(9, 45), ssrActive: false, ShortSsrHandling.Uptick)!;
        var ssr = GapperShortSimulator.Entry(bars, Et(9, 45), ssrActive: true, ShortSsrHandling.Uptick)!;
        Assert.Multiple(() =>
        {
            Assert.That(free.Fill, Is.EqualTo(9.9m));
            Assert.That(ssr.BarStartUtc, Is.EqualTo(Et(9, 47)));
            Assert.That(ssr.Fill, Is.EqualTo(9.75m));
            Assert.That(GapperShortSimulator.Entry(bars, Et(9, 45), ssrActive: true, ShortSsrHandling.Skip), Is.Null);
            Assert.That(GapperShortSimulator.Entry(bars, Et(9, 45), ssrActive: false, ShortSsrHandling.Skip)!.Fill, Is.EqualTo(9.9m));
        });
    }

    [Test]
    public void SsrUptick_GivesUpAfterTheWaitWindow()
    {
        var bars = new List<Candle> { Bar(Et(9, 44), 10m, 10m) };
        decimal price = 10m;
        for (int i = 0; i < 40; i++) { bars.Add(Bar(Et(9, 45).AddMinutes(i), price - 0.01m, price - 0.02m)); price -= 0.02m; }
        Assert.That(GapperShortSimulator.Entry(bars, Et(9, 45), ssrActive: true, ShortSsrHandling.Uptick), Is.Null);
    }

    [Test]
    public void Borrow_IsAprTimesDaysOver360_WithOneDayMinimum()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GapperShortSimulator.BorrowCost(0.5), Is.EqualTo(0.5 / 360).Within(1e-12));
            Assert.That(GapperShortSimulator.BorrowCost(2.0, 0), Is.EqualTo(2.0 / 360).Within(1e-12));
            Assert.That(GapperShortSimulator.BorrowCost(2.0, 3), Is.EqualTo(6.0 / 360).Within(1e-12));
            Assert.That(GapperShortSimulator.BorrowCost(0), Is.EqualTo(0));
        });
    }

    [Test]
    public void MaxAdverse_IsTheHighestPriceHeldThrough()
    {
        var bars = new List<Candle> { Bar(Et(9, 45), 10m, 10.2m, high: 10.8m), Bar(Et(9, 46), 10.2m, 9m), Bar(Et(11, 0), 8m, 8m, high: 12m) };
        var trade = GapperShortSimulator.Simulate(new GapperShortExit(new TimeSpan(11, 0, 0), null, false), bars, Day, Et(9, 45), 10m)!;
        // The 11:00 cover bar's high comes after the fill at its open, so it is not held through.
        Assert.That(trade.MaxAdverse, Is.EqualTo(0.08).Within(1e-9));
    }

    [Test]
    public void RoundLots_AndLocateDraw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GapperShortSimulator.RoundLotShares(1000, 3.0), Is.EqualTo(300));
            Assert.That(GapperShortSimulator.RoundLotShares(1000, 12.0), Is.EqualTo(0));
            Assert.That(GapperShortSimulator.LocateDraw("ABC", Day), Is.EqualTo(GapperShortSimulator.LocateDraw("ABC", Day)));
            var draws = Enumerable.Range(0, 2000).Select(i => GapperShortSimulator.LocateDraw("S" + i, Day)).ToList();
            Assert.That(draws.All(d => d is >= 0 and < 1), Is.True);
            Assert.That(draws.Count(d => d < 0.3) / 2000.0, Is.EqualTo(0.3).Within(0.05));
        });
    }

    [Test]
    public void Margin_SubFiveDollarShortsTieUpAtLeastTwoFiftyAShare()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GapperShortSimulator.MarginFraction(1), Is.EqualTo(2.5).Within(1e-12));
            Assert.That(GapperShortSimulator.MarginFraction(4), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(GapperShortSimulator.MarginFraction(20), Is.EqualTo(0.5).Within(1e-12));
        });
    }

    [Test]
    public void Signal_ReadsOnlyBarsEndingByTheDecision()
    {
        var bars = new List<Candle>
        {
            Bar(Et(8, 0), 1.2m, 1.5m, high: 1.6m),   // premarket high 1.6 = +60% over a 1.00 prior close
            Bar(Et(9, 30), 1.4m, 1.45m),
            Bar(Et(9, 44), 1.45m, 1.47m),
            Bar(Et(9, 45), 1.47m, 3m, high: 3m),     // ends after 09:45: must not be read
        };
        var s = GapperShortSimulator.Signal(bars, Day, 1m, Et(9, 45))!;
        Assert.Multiple(() =>
        {
            Assert.That(s.PmHighGapPct, Is.EqualTo(60).Within(1e-9));
            Assert.That(s.GapPct, Is.EqualTo(47).Within(1e-9));
            Assert.That(s.ExtensionPct, Is.EqualTo((1.47 / 1.4 - 1) * 100).Within(1e-9));
            Assert.That(s.RthVolume, Is.EqualTo(2000));
        });
    }
}
