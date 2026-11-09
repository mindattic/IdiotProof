using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T479 fade-short deep dive: the MAE-aware exit plans (half covers, cover limits, the trailing stop armed by a move in
/// favour) and the sizing bootstrap. 2026-07-17 is EDT: 04:00 ET = 08:00 UTC.
/// </summary>
public class GapperShortPathTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static readonly TimeSpan Close = new(15, 55, 0);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static Candle Bar(DateTime startUtc, decimal open, decimal close, decimal? high = null, decimal? low = null) => new()
    {
        Symbol = "T", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1), Open = open, Close = close,
        High = high ?? Math.Max(open, close), Low = low ?? Math.Min(open, close), Volume = 1000,
    };

    [Test]
    public void NoStop_CoversAtTheClockOpen_AndRecordsTheHigh()
    {
        var real = new List<Candle> { Bar(Et(9, 45), 10m, 10m, high: 12m), Bar(Et(12, 0), 9m, 8m), Bar(Et(15, 55), 8m, 8m) };
        var r = GapperShortPath.Simulate(new GapperShortPlan("base", Close), real, Day, Et(9, 45), 10m, null)!;
        Assert.Multiple(() =>
        {
            Assert.That(r.GrossReturn, Is.EqualTo(0.20).Within(1e-9));
            Assert.That(r.MaxAdverse, Is.EqualTo(0.20).Within(1e-9));
            Assert.That(r.ExitReason, Is.EqualTo("cover-by"));
        });
    }

    [Test]
    public void HalfAtTime_ThenRestAtTheClose()
    {
        var real = new List<Candle> { Bar(Et(9, 45), 10m, 10m), Bar(Et(11, 0), 9m, 9m), Bar(Et(15, 55), 7m, 7m) };
        var r = GapperShortPath.Simulate(new GapperShortPlan("half", Close, HalfAtEt: new TimeSpan(11, 0, 0)), real, Day, Et(9, 45), 10m, null)!;
        Assert.That(r.GrossReturn, Is.EqualTo(0.5 * 0.10 + 0.5 * 0.30).Within(1e-9));
    }

    [Test]
    public void Target_FillsAtTheLimit_OrABetterOpen()
    {
        var limit = new List<Candle> { Bar(Et(9, 45), 10m, 10m), Bar(Et(10, 0), 9.5m, 8.5m, low: 7.5m), Bar(Et(15, 55), 9m, 9m) };
        var gap = new List<Candle> { Bar(Et(9, 45), 10m, 10m), Bar(Et(10, 0), 7m, 7m), Bar(Et(15, 55), 9m, 9m) };
        var plan = new GapperShortPlan("target", Close, TargetPct: 20);
        Assert.Multiple(() =>
        {
            Assert.That(GapperShortPath.Simulate(plan, limit, Day, Et(9, 45), 10m, null)!.GrossReturn, Is.EqualTo(0.20).Within(1e-9));
            Assert.That(GapperShortPath.Simulate(plan, gap, Day, Et(9, 45), 10m, null)!.GrossReturn, Is.EqualTo(0.30).Within(1e-9));
        });
    }

    [Test]
    public void Stop_IsCheckedBeforeTheTarget_InTheSameBar()
    {
        var real = new List<Candle> { Bar(Et(9, 45), 10m, 10m), Bar(Et(10, 0), 10m, 9m, high: 13.5m, low: 7m), Bar(Et(15, 55), 9m, 9m) };
        var r = GapperShortPath.Simulate(new GapperShortPlan("both", Close, TargetPct: 20), real, Day, Et(9, 45), 10m, 30)!;
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitReason, Is.EqualTo("stop"));
            Assert.That(r.GrossReturn, Is.EqualTo(-0.30).Within(1e-9));
        });
    }

    [Test]
    public void Trail_ArmsAfterTheMove_AndTrailsThePriorBarsLow()
    {
        // Low 8 (-20%) arms it; the next bar's high 8.9 is over 8 x 1.10 = 8.8, so it covers at 8.8.
        var real = new List<Candle> { Bar(Et(9, 45), 10m, 10m), Bar(Et(10, 0), 9m, 8.2m, low: 8m), Bar(Et(10, 1), 8.3m, 8.5m, high: 8.9m), Bar(Et(15, 55), 6m, 6m) };
        var r = GapperShortPath.Simulate(new GapperShortPlan("trail", Close, TrailActivatePct: 20, TrailPct: 10), real, Day, Et(9, 45), 10m, null)!;
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitReason, Is.EqualTo("trail"));
            Assert.That(r.GrossReturn, Is.EqualTo(0.12).Within(1e-9));
        });
    }

    [Test]
    public void SizingPaths_RuinOnlyWhenEquityHalves()
    {
        var days = new List<double[]> { new[] { -1.0 } };
        var paths = GapperShortPath.SizingPaths(days, 10, 0.05, 5, 1);
        Assert.Multiple(() =>
        {
            Assert.That(paths, Has.Count.EqualTo(5));
            Assert.That(paths[0].Return, Is.EqualTo(Math.Pow(0.95, 10) - 1).Within(1e-9));
            Assert.That(paths[0].Ruined, Is.False);
            Assert.That(GapperShortPath.SizingPaths(days, 20, 0.05, 1, 1)[0].Ruined, Is.True);
        });
    }
}
