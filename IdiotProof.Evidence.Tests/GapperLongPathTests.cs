using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T483 alarm ride: the long exit plans (bracket, lottery trail, half at a target) and the passive limit entry.
/// 2026-07-17 is EDT: 04:00 ET = 08:00 UTC.
/// </summary>
public class GapperLongPathTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static Candle Bar(DateTime startUtc, decimal open, decimal close, decimal? high = null, decimal? low = null) => new()
    {
        Symbol = "T", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1), Open = open, Close = close,
        High = high ?? Math.Max(open, close), Low = low ?? Math.Min(open, close), Volume = 1000,
    };

    [Test]
    public void Stop_IsCheckedBeforeTheTarget_AndFillsAtTheWorseOfStopAndOpen()
    {
        var real = new List<Candle> { Bar(Et(8, 0), 10m, 10m), Bar(Et(8, 1), 10m, 10m, high: 12m, low: 9m), Bar(Et(9, 31), 10m, 10m) };
        var gap = new List<Candle> { Bar(Et(8, 0), 10m, 10m), Bar(Et(8, 1), 9m, 9m), Bar(Et(9, 31), 10m, 10m) };
        var plan = new GapperLongPlan("b", new TimeSpan(9, 31, 0), TakeProfitPct: 10, StopPct: 5);
        Assert.Multiple(() =>
        {
            var r = GapperLongPath.Simulate(plan, real, Day, Et(8, 0), 10m)!;
            Assert.That(r.ExitReason, Is.EqualTo("stop"));
            Assert.That(r.GrossReturn, Is.EqualTo(-0.05).Within(1e-9));
            Assert.That(GapperLongPath.Simulate(plan, gap, Day, Et(8, 0), 10m)!.GrossReturn, Is.EqualTo(-0.10).Within(1e-9), "a gap under the stop fills at the open");
        });
    }

    [Test]
    public void Target_FillsOnlyWhenTheHighTradesThrough()
    {
        var touch = new List<Candle> { Bar(Et(8, 0), 10m, 10m, high: 11m), Bar(Et(9, 31), 10.5m, 10.5m) };
        var through = new List<Candle> { Bar(Et(8, 0), 10m, 10m, high: 11.01m), Bar(Et(9, 31), 10.5m, 10.5m) };
        var plan = new GapperLongPlan("b", new TimeSpan(9, 31, 0), TakeProfitPct: 10);
        Assert.Multiple(() =>
        {
            Assert.That(GapperLongPath.Simulate(plan, touch, Day, Et(8, 0), 10m)!.ExitReason, Is.EqualTo("sell-by"));
            var r = GapperLongPath.Simulate(plan, through, Day, Et(8, 0), 10m)!;
            Assert.That(r.TargetHit);
            Assert.That(r.GrossReturn, Is.EqualTo(0.10).Within(1e-9));
            Assert.That(GapperLongPath.Simulate(plan, through, Day, Et(8, 0), 10m, targetsOnEntryBar: false)!.TargetHit, Is.False, "no target inside a passive entry's own bar");
        });
    }

    [Test]
    public void Signal_SellsAtTheNextOpen()
    {
        var real = new List<Candle> { Bar(Et(8, 0), 10m, 10m), Bar(Et(8, 15), 10.4m, 10.4m), Bar(Et(9, 31), 12m, 12m) };
        var r = GapperLongPath.Simulate(new GapperLongPlan("b", new TimeSpan(9, 31, 0), TakeProfitPct: 30, StopPct: 8), real, Day, Et(8, 0), 10m, Et(8, 15))!;
        Assert.That(r.ExitReason, Is.EqualTo("signal"));
        Assert.That(r.GrossReturn, Is.EqualTo(0.04).Within(1e-9));
    }

    [Test]
    public void Lottery_TrailReplacesTheStop_AndHalfSellsAtTheTarget()
    {
        // Up to 13 (+30%), half sold at +20%, trail 15% under 13 = 11.05 hit next bar.
        var real = new List<Candle> { Bar(Et(8, 0), 10m, 10m), Bar(Et(8, 1), 10m, 13m, high: 13m), Bar(Et(8, 2), 12m, 11m, low: 11m), Bar(Et(15, 55), 20m, 20m) };
        var plan = new GapperLongPlan("l", new TimeSpan(15, 55, 0), StopPct: 4, TrailActivatePct: 10, TrailPct: 15, HalfTargetPct: 20);
        var r = GapperLongPath.Simulate(plan, real, Day, Et(8, 0), 10m)!;
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitReason, Is.EqualTo("trail"));
            Assert.That(r.GrossReturn, Is.EqualTo(0.5 * 0.20 + 0.5 * 0.105).Within(1e-9));
            Assert.That(r.TargetHit, "the half target filled");
        });
    }

    [Test]
    public void PassiveLimit_FillsOnlyInsideTheWindow()
    {
        var real = new List<Candle> { Bar(Et(8, 0), 10m, 10m), Bar(Et(8, 2), 10m, 9.95m, low: 9.9m), Bar(Et(8, 10), 9m, 9m) };
        Assert.Multiple(() =>
        {
            var e = GapperLongPath.PassiveLimit(real, Et(8, 0), 9.9m)!;
            Assert.That(e.BarStartUtc, Is.EqualTo(Et(8, 2)));
            Assert.That(e.Fill, Is.EqualTo(9.9m));
            Assert.That(GapperLongPath.PassiveLimit(real, Et(8, 0), 9.5m), Is.Null, "the 08:10 bar is past the 5-minute window");
        });
    }
}
