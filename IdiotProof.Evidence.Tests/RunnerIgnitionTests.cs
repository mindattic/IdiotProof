using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T484 runner watchlist: ignition detection, the long ride and the catalyst classifier. 2026-07-17 is EDT: 04:00 ET = 08:00 UTC.</summary>
public class RunnerIgnitionTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static Candle Bar(DateTime startUtc, decimal open, decimal close, decimal volume, decimal? high = null, decimal? low = null) => new()
    {
        Symbol = "T", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1), Open = open, Close = close,
        High = high ?? Math.Max(open, close), Low = low ?? Math.Min(open, close), Volume = volume,
    };

    [Test]
    public void Ignition_NeedsTheVolumeBurstAndTheBreakOfTheSessionHigh()
    {
        var bars = new List<Candle>();
        for (int m = 0; m < 40; m++) bars.Add(Bar(Et(7, m), 3m, 3m, 1000));
        bars.Add(Bar(Et(7, 40), 3m, 3.03m, 20_000));          // burst, but only +1% over the high: no ignition
        bars.Add(Bar(Et(7, 41), 3.03m, 3.20m, 30_000));       // burst and +5.6% over the high: ignition
        var g = RunnerGrid.Build(bars)!;
        var ev = RunnerIgnition.Ignitions(g, 3.0, 10, 100, 5_000);
        Assert.That(ev, Is.Not.Empty);
        Assert.That(ev[0].Minute, Is.EqualTo(RunnerGrid.MinuteOf(Et(7, 41))));
        Assert.That(ev[0].GapBefore, Is.EqualTo(0.01).Within(1e-4));
    }

    [Test]
    public void Ride_StopsBeforeArming_ThenTrailsTheHighestEarlierHigh()
    {
        var stopped = new List<Candle> { Bar(Et(9, 30), 10m, 10m, 1000), Bar(Et(9, 31), 10m, 9m, 1000, low: 9m), Bar(Et(15, 55), 9m, 9m, 1000) };
        var trailed = new List<Candle> { Bar(Et(9, 30), 10m, 10m, 1000), Bar(Et(9, 31), 10m, 20m, 1000, high: 20m), Bar(Et(9, 32), 20m, 14m, 1000, low: 14m), Bar(Et(15, 55), 14m, 14m, 1000) };
        var plan = new RunnerExitPlan("t", 8, 10, 25, null, RunnerCooling.None);
        var s = RunnerIgnition.Simulate(RunnerGrid.Build(stopped)!, RunnerGrid.MinuteOf(Et(9, 30)), 10, plan, false)!.Value;
        var t = RunnerIgnition.Simulate(RunnerGrid.Build(trailed)!, RunnerGrid.MinuteOf(Et(9, 30)), 10, plan, false)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(s.Reason, Is.EqualTo(0));
            Assert.That(s.Gross, Is.EqualTo(-0.08).Within(1e-6));
            Assert.That(t.Reason, Is.EqualTo(1));
            Assert.That(t.Gross, Is.EqualTo(0.5).Within(1e-6), "trail 25% under the 20.00 high fills at 15.00");
        });
    }

    [Test]
    public void Classifier_SeparatesTargetsFromAcquirersAndLoisFromContracts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CatalystTypes.FromHeadline("XYZ agrees to be acquired by BigCo for $5 per share", null) & CatalystTypes.Bit("acqTarget"), Is.Not.Zero);
            Assert.That(CatalystTypes.FromHeadline("XYZ to acquire SmallCo", null) & CatalystTypes.Bit("acqAcquirer"), Is.Not.Zero);
            Assert.That(CatalystTypes.FromHeadline("XYZ signs non-binding letter of intent with partner", null) & CatalystTypes.Bit("contract"), Is.Zero);
            Assert.That(CatalystTypes.FromHeadline("XYZ announces 1-for-50 reverse stock split", null) & CatalystTypes.Bit("rsplit"), Is.Not.Zero);
            Assert.That(CatalystTypes.FromFiling("8-K", ["3.02"]), Is.EqualTo(CatalystTypes.Bit("pipe")));
        });
    }
}
