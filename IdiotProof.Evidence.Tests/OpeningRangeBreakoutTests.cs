using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T492 opening-range breakout: the 09:30-09:35 candle's side, the 09:35 entry, the ATR stop and the 15:55 exit. 2026-07-17 is EDT.</summary>
public class OpeningRangeBreakoutTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static Candle Bar(DateTime startUtc, decimal open, decimal close, decimal volume = 1000, decimal? high = null, decimal? low = null) => new()
    {
        Symbol = "T", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1), Open = open, Close = close,
        High = high ?? Math.Max(open, close), Low = low ?? Math.Min(open, close), Volume = volume,
    };

    private static List<Candle> Range(decimal open, decimal close)
    {
        var bars = new List<Candle> { Bar(Et(9, 30), open, open) };
        for (int m = 31; m < 35; m++) bars.Add(Bar(Et(9, m), open, m == 34 ? close : open));
        return bars;
    }

    [Test]
    public void Range_TakesTheFirstOpenAndTheLastCloseOfTheFirstFiveMinutes()
    {
        var up = OpeningRangeBreakout.Range(RunnerGrid.Build(Range(10m, 10.2m))!)!.Value;
        var doji = OpeningRangeBreakout.Range(RunnerGrid.Build(Range(10m, 10m))!)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(up.Direction, Is.EqualTo(1));
            Assert.That(up.Open, Is.EqualTo(10f));
            Assert.That(up.Volume, Is.EqualTo(5000));
            Assert.That(doji.Direction, Is.EqualTo(0));
        });
    }

    [Test]
    public void Long_IsStoppedAtEntryMinusXAtr_ElseSoldAt1555()
    {
        var stopped = Range(10m, 10.2m);
        stopped.Add(Bar(Et(9, 35), 10.2m, 10.2m));
        stopped.Add(Bar(Et(9, 40), 10.2m, 10.0m, low: 10.0m));
        stopped.Add(Bar(Et(15, 55), 11m, 11m));
        var held = Range(10m, 10.2m);
        held.Add(Bar(Et(9, 35), 10.2m, 10.3m));
        held.Add(Bar(Et(15, 55), 11.22m, 11.3m));
        var s = OpeningRangeBreakout.Simulate(RunnerGrid.Build(stopped)!, 1, 1.0, 0.10)!.Value;
        var h = OpeningRangeBreakout.Simulate(RunnerGrid.Build(held)!, 1, 1.0, 0.10)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(s.Stopped, Is.True);
            Assert.That(s.Exit, Is.EqualTo(10.1).Within(1e-5), "stop 0.10 x ATR 1.00 under the 10.20 entry");
            Assert.That(h.Stopped, Is.False);
            Assert.That(h.Gross, Is.EqualTo(0.10).Within(1e-5), "out at the 15:55 open, 11.22 / 10.20 - 1");
        });
    }

    [Test]
    public void Short_GainsWhenThePriceFalls_AndAGapThroughTheStopFillsAtTheOpen()
    {
        var down = Range(10m, 9.8m);
        down.Add(Bar(Et(9, 35), 9.8m, 9.8m));
        down.Add(Bar(Et(15, 55), 8.82m, 8.8m));
        var gap = Range(10m, 9.8m);
        gap.Add(Bar(Et(9, 35), 9.8m, 9.8m));
        gap.Add(Bar(Et(10, 0), 10.5m, 10.5m));
        var d = OpeningRangeBreakout.Simulate(RunnerGrid.Build(down)!, -1, 1.0, 0.10)!.Value;
        var g = OpeningRangeBreakout.Simulate(RunnerGrid.Build(gap)!, -1, 1.0, 0.10)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(d.Gross, Is.EqualTo(0.10).Within(1e-5));
            Assert.That(g.Exit, Is.EqualTo(10.5).Within(1e-5), "the worse of the 9.90 stop and the 10.50 open");
        });
    }

    [Test]
    public void Atr_IsTheMeanTrueRangeOfThePriorSessions()
    {
        double[] high = [10, 11, 12, 13], low = [9, 10, 10, 12], close = [9.5, 10.5, 11, 12.5];
        Assert.That(OpeningRangeBreakout.Atr(high, low, close, 3, 2), Is.EqualTo((1.5 + 2.0) / 2).Within(1e-9), "TR = max(H - L, |H - prior close|, |L - prior close|)");
        Assert.That(OpeningRangeBreakout.Atr(high, low, close, 1, 2), Is.Null);
    }
}
