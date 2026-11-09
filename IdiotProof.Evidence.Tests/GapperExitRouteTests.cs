using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T471 exit overlays and routes: the drift-change detector fires after a synthetic drift flip and not before,
/// the volatility-scaled giveback, the opg limit route falls back at 09:30:05 when the opening print misses the limit,
/// the participation cap, and the high-of-day hazard. 2026-07-17 is EDT: 04:00 ET = 08:00 UTC.
/// </summary>
public class GapperExitRouteTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static DetailedCandle Bar(DateTime startUtc, decimal open, decimal close, double volume = 1000, int trades = 10) => new()
    {
        Symbol = "T", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1),
        Open = open, Close = close, High = Math.Max(open, close), Low = Math.Min(open, close), Volume = (decimal)volume, TradeCount = trades,
    };

    /// <summary>Minute bars from <paramref name="fromUtc"/>, each closing at exp(return) times the last close.</summary>
    private static List<Candle> Path(DateTime fromUtc, double start, IEnumerable<double> logReturns)
    {
        var bars = new List<Candle>();
        double price = start;
        var t = fromUtc;
        foreach (var r in logReturns)
        {
            double next = price * Math.Exp(r);
            bars.Add(Bar(t, (decimal)Math.Round(price, 4), (decimal)Math.Round(next, 4)));
            price = next;
            t = t.AddMinutes(1);
        }
        return bars;
    }

    /// <summary>08:00-08:44 rising at about +0.5%/min (alternating +0.7/+0.3), from 08:45 falling at about -0.5%/min.</summary>
    private static List<Candle> DriftFlip() =>
        Path(Et(8, 0), 10, Enumerable.Range(0, 60).Select(i => (i < 45 ? 0.005 : -0.005) + (i % 2 == 0 ? 0.002 : -0.002)));

    [Test]
    public void DriftChange_FiresAfterTheDriftFlips_AndNotBefore()
    {
        var bars = DriftFlip();
        var rule = new DriftChangeExitRule(new DriftChangeParameters(PosteriorThreshold: 0.7, PostHighDrift: -0.005), HodHazard.Constant(0.01));

        var before = rule.Scan(bars, Et(8, 30), Et(8, 45));
        var after = rule.Scan(bars, Et(8, 30), Et(9, 0));

        Assert.Multiple(() =>
        {
            Assert.That(before.Exit, Is.False, "the run is still on through 08:45");
            Assert.That(after.Exit, Is.True);
            Assert.That(after.SignalUtc, Is.GreaterThan(Et(8, 45)).And.LessThanOrEqualTo(Et(8, 50)), "a few falling minutes are enough at this signal-to-noise");
            Assert.That(after.RunDrift, Is.EqualTo(0.005).Within(0.001));
        });
    }

    [Test]
    public void DriftChange_HoldsWhenTheRunIsNotAboveThePostHighDrift_AndTheCollapseCanOnlyRaiseTheHazard()
    {
        var flat = Path(Et(8, 0), 10, Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? 0.002 : -0.002));
        var rule = new DriftChangeExitRule(new DriftChangeParameters(0.7, PostHighDrift: 0.001), HodHazard.Constant(0.01));
        Assert.That(rule.Scan(flat, Et(8, 30), Et(9, 0)).Exit, Is.False, "no run drift at arming: fail closed");
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new DriftChangeExitRule(new DriftChangeParameters(0.7, -0.005, CollapseHazardMultiplier: 0.5), HodHazard.Constant(0.01)));
    }

    [Test]
    public void DriftChange_CollapseRaisesTheHazard_SoTheClockAloneFiresSooner()
    {
        // A noisy run that never flips: the returns argue against a change, so only the prior hazard moves the posterior.
        var run = Path(Et(8, 0), 10, Enumerable.Range(0, 90).Select(i => 0.002 + (i % 2 == 0 ? 0.004 : -0.004)));
        var plain = new DriftChangeExitRule(new DriftChangeParameters(0.999, -0.002, CollapseHazardMultiplier: 1), HodHazard.Constant(0.05));
        var raised = new DriftChangeExitRule(new DriftChangeParameters(0.999, -0.002, CollapseHazardMultiplier: 3), HodHazard.Constant(0.05));
        var a = plain.Scan(run, Et(8, 30), Et(9, 29), collapseUtc: Et(8, 35));
        var b = raised.Scan(run, Et(8, 30), Et(9, 29), collapseUtc: Et(8, 35));
        if (a.Exit)
            Assert.That(b.Exit && b.SignalUtc <= a.SignalUtc, Is.True, "a raised hazard fires no later");
        else if (!b.Exit)
            Assert.That(b.Posterior, Is.GreaterThan(a.Posterior ?? 0), "a raised hazard after the collapse lifts the posterior");
    }

    [Test]
    public void Giveback_SellsOnceTheDropFromThePeakExceedsKSigmas()
    {
        // Noisy rise (sigma about 0.2%/min) to 08:40, then a straight 1%/min fall.
        var bars = Path(Et(8, 0), 10, Enumerable.Range(0, 60).Select(i => i < 40 ? 0.003 + (i % 2 == 0 ? 0.002 : -0.002) : -0.01));
        var rule = new GivebackExitRule(new GivebackParameters(K: 3));
        var during = rule.Scan(bars, Et(8, 20), Et(8, 40));
        var verdict = rule.Scan(bars, Et(8, 20), Et(9, 0));
        Assert.Multiple(() =>
        {
            Assert.That(during.Exit, Is.False, "an alternating rise never gives back 3 sigma x sqrt 5");
            Assert.That(verdict.Exit, Is.True);
            Assert.That(verdict.SignalUtc, Is.GreaterThan(Et(8, 40)).And.LessThanOrEqualTo(Et(8, 50)), "sigma includes the falling minutes, so it takes about five of them");
            Assert.That(verdict.GivebackLog, Is.GreaterThan(verdict.Threshold ?? double.MaxValue));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new GivebackExitRule(new GivebackParameters(3, KAfterCollapse: 5)), "the collapse may only tighten k");
    }

    /// <summary>Flat $10 from 08:00 to 09:29, then the given 09:30 bar (or none) and flat bars after.</summary>
    private static List<Candle> OpenDay(decimal? open930, decimal close930, int firstRthMinute = 30)
    {
        var bars = new List<Candle>();
        for (var t = Et(8, 0); t < Et(9, 30); t = t.AddMinutes(1)) bars.Add(Bar(t, 10m, 10m));
        if (open930 is { } o) bars.Add(Bar(Et(9, 30), o, close930));
        for (int m = Math.Max(31, firstRthMinute); m < 50; m++) bars.Add(Bar(Et(9, m), 9.5m, 9.5m));
        return bars;
    }

    [Test]
    public void OpgRoute_FillsAtTheOpeningPrint_WhenItIsAboveTheLimit()
    {
        var trade = GapperExitRouteSimulator.Simulate(GapperExitRoute.Opg(0.03), GapperExitOverlays.ClockOnly, OpenDay(9.9m, 9.8m), Day, Et(8, 30), 10m);
        Assert.That(trade, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(trade!.ExitReason, Is.EqualTo("opg"));
            Assert.That(trade.OpgFilled, Is.True);
            Assert.That(trade.Exit, Is.EqualTo(9.9m), "limit 9.70, print 9.90");
        });
    }

    [Test]
    public void OpgRoute_FallsBackAt0930_WhenTheLimitMisses()
    {
        var trade = GapperExitRouteSimulator.Simulate(GapperExitRoute.Opg(0.03), GapperExitOverlays.ClockOnly, OpenDay(9.0m, 9.2m), Day, Et(8, 30), 10m);
        Assert.That(trade, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(trade!.ExitReason, Does.StartWith("opg-miss fallback"));
            Assert.That(trade.OpgFilled, Is.False);
            Assert.That(trade.Exit, Is.EqualTo(9.0m), "the fallback takes the worse of the 09:30 bar's open (9.00) and close (9.20)");
            Assert.That(trade.GrossReturn, Is.EqualTo(-0.10).Within(1e-12));
        });
    }

    [Test]
    public void OpgRoute_WithNoOpeningPrint_FallsBackToTheFirstPostGapOpen_AsAHaltFill()
    {
        var trade = GapperExitRouteSimulator.Simulate(GapperExitRoute.Opg(0.05), GapperExitOverlays.ClockOnly, OpenDay(null, 0m, firstRthMinute: 34), Day, Et(8, 30), 10m);
        Assert.That(trade, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(trade!.ExitReason, Is.EqualTo("opg no-cross fallback"));
            Assert.That(trade.HaltFill, Is.True);
            Assert.That(trade.ExitUtc, Is.EqualTo(Et(9, 34)));
            Assert.That(trade.Exit, Is.EqualTo(9.5m));
        });
    }

    [Test]
    public void OpgRoute_RejectsASubmitTimeAtOrAfterTheCutoff()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GapperExitRoute.Opg(0.03, new TimeSpan(9, 28, 0)));
        Assert.DoesNotThrow(() => GapperExitRoute.Opg(0.03, new TimeSpan(9, 27, 0)));
    }

    [Test]
    public void ContinuousRoute_SellsAtTheClockBarOpen_AndAStopSellsAtTheNextOpenAfterACloseBelowIt()
    {
        var flat = OpenDay(9.9m, 9.8m);
        var clock = GapperExitRouteSimulator.Simulate(GapperExitRoute.Continuous(new TimeSpan(9, 15, 0)), GapperExitOverlays.ClockOnly, flat, Day, Et(8, 30), 10m);
        Assert.That(clock!.ExitReason, Is.EqualTo("sell-by 09:15"));
        Assert.That(clock.ExitUtc, Is.EqualTo(Et(9, 15)));

        var dropping = flat.Select(b => b.StartUtc == Et(9, 0) ? Bar(b.StartUtc, 10m, 8.5m) : b.StartUtc > Et(9, 0) && b.StartUtc < Et(9, 30) ? Bar(b.StartUtc, 8.6m, 8.6m) : b).ToList();
        var stopped = GapperExitRouteSimulator.Simulate(GapperExitRoute.Continuous(new TimeSpan(9, 15, 0)), GapperExitOverlays.ClockOnly, dropping, Day, Et(8, 30), 10m);
        Assert.Multiple(() =>
        {
            Assert.That(stopped!.ExitReason, Is.EqualTo("stop"));
            Assert.That(stopped.ExitUtc, Is.EqualTo(Et(9, 1)));
            Assert.That(stopped.Exit, Is.EqualTo(8.6m), "close 8.50 under the 10% stop, sold at the next bar's open");
        });
    }

    [Test]
    public void LuldAwareRoute_ExitsAt0944_WhenTheLastMinutesMoveMoreThanTheBand()
    {
        var bars = OpenDay(10m, 10m);
        // 09:41-09:43 swing between 9 and 11 on a $10 name: range 2.00 > 0.8 x 10% x 10.
        bars = bars.Select(b => b.StartUtc >= Et(9, 41) && b.StartUtc < Et(9, 44) ? Bar(b.StartUtc, 9m, 11m) : b).ToList();
        var aware = GapperExitRouteSimulator.Simulate(GapperExitRoute.Regular(new TimeSpan(9, 50, 0), luldAware: true), GapperExitOverlays.ClockOnly, bars, Day, Et(8, 30), 10m);
        var naive = GapperExitRouteSimulator.Simulate(GapperExitRoute.Regular(new TimeSpan(9, 50, 0)), GapperExitOverlays.ClockOnly, bars, Day, Et(8, 30), 10m);
        Assert.Multiple(() =>
        {
            Assert.That(aware!.ExitReason, Is.EqualTo("luld-band"));
            Assert.That(aware.ExitUtc, Is.EqualTo(Et(9, 44)));
            Assert.That(naive!.ExitReason, Does.StartWith("sell-by"));
            Assert.That(GapperExitRouteSimulator.LuldBandFraction(0.5), Is.EqualTo(0.30).Within(1e-12), "below $0.75: the lesser of $0.15 or 75%");
        });
    }

    [Test]
    public void ParticipationCap_TakesTheSmallestOfNotionalRecentAndExitWindow()
    {
        var recentBinds = ParticipationCap.Size(price: 2, notionalUsd: 1000, recentShares: 4000, exitWindowShares: 20000);
        var exitBinds = ParticipationCap.Size(price: 2, notionalUsd: 1000, recentShares: 40000, exitWindowShares: 6000);
        var full = ParticipationCap.Size(price: 2, notionalUsd: 1000, recentShares: 40000, exitWindowShares: 40000);
        var skipped = ParticipationCap.Size(price: 2, notionalUsd: 1000, recentShares: 1000, exitWindowShares: 40000);
        Assert.Multiple(() =>
        {
            Assert.That(recentBinds.Shares, Is.EqualTo(200));
            Assert.That(recentBinds.Binding, Is.EqualTo("recent volume"));
            Assert.That(recentBinds.FullSize, Is.False);
            Assert.That(exitBinds.Shares, Is.EqualTo(300));
            Assert.That(exitBinds.Binding, Is.EqualTo("exit window"));
            Assert.That(full.Shares, Is.EqualTo(500));
            Assert.That(full.FullSize, Is.True);
            Assert.That(skipped.Skip, Is.True, "50 shares x $2 = $100 is under the $200 minimum");
            Assert.That(skipped.Shares, Is.EqualTo(0));
            Assert.That(ParticipationCap.ForecastWindowVolume(cumVolAtT: 100_000, fractionAtT: 0.05, fractionFrom: 0.06, fractionTo: 0.07), Is.EqualTo(20_000).Within(1e-6));
        });
    }

    [Test]
    public void HodHazard_IsTheShareOfMorningsStillAtRiskWhoseHighPrintsThatMinute()
    {
        var h = HodHazard.Fit([540, 540, 570, 600], smoothMinutes: 1);
        Assert.Multiple(() =>
        {
            Assert.That(h.Events, Is.EqualTo(4));
            Assert.That(h.At(540), Is.EqualTo(0.5).Within(1e-12), "2 of 4 at risk");
            Assert.That(h.At(570), Is.EqualTo(0.5).Within(1e-12), "1 of the 2 left");
            Assert.That(h.At(600), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(h.At(555), Is.EqualTo(h.Floor), "no high that minute");
        });
    }
}
