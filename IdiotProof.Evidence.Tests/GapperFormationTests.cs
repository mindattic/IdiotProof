using System.Text.Json;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// The shared gapper feature + exit brain: no lookahead, wall-clock velocity, the time-of-day
/// profile round trip and the velocity exit's fail-closed guards. 2026-07-17 is EDT (UTC-4), so
/// the 04:00 ET session opens at 08:00 UTC and 09:30 ET is 13:30 UTC.
/// </summary>
public class GapperFormationTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static readonly DateTime SessionOpen = new(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);

    private static Candle Bar(DateTime startUtc, double close, double volume, double? high = null, double? low = null) => new()
    {
        Symbol = "T", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1),
        Open = (decimal)close, High = (decimal)(high ?? close), Low = (decimal)(low ?? close), Close = (decimal)close,
        Volume = (decimal)volume,
    };

    /// <summary>Ten rising minute bars from 08:00 UTC: closes 10.0, 10.1, ... 10.9, volumes 100 each.</summary>
    private static List<Candle> Rising(int count = 10, double step = 0.1, double volume = 100)
    {
        var bars = new List<Candle>();
        for (int i = 0; i < count; i++) bars.Add(Bar(SessionOpen.AddMinutes(i), 10.0 + step * i, volume));
        return bars;
    }

    private static GapperFeatureInputs Inputs(List<Candle> bars, DateTime t, params CatalystObservation[] catalysts) => new()
    {
        Symbol = "T", DateEt = Day, DecisionUtc = t, MinuteBars = bars, PriorClose = 8m, Catalysts = catalysts,
    };

    // ── Velocity math ─────────────────────────────────────────────────────

    [Test]
    public void Velocity_IsPercentPerClockMinute()
    {
        var bars = Rising();
        var t = SessionOpen.AddMinutes(10);
        var expected = (10.9 / 10.6 - 1) * 100 / 3;
        Assert.That(MinuteBarMath.Velocity(bars, t, 3), Is.EqualTo(expected).Within(1e-9));
        Assert.That(MinuteBarMath.Velocity(bars, t, 1), Is.EqualTo((10.9 / 10.8 - 1) * 100).Within(1e-9));
    }

    [Test]
    public void Velocity_MissingMinute_CarriesLastCloseForward()
    {
        var bars = Rising();
        bars.RemoveAt(6); // the bar ending 08:07 is missing; the close as of 08:07 is the 08:06 close, 10.5
        var t = SessionOpen.AddMinutes(10);
        Assert.That(MinuteBarMath.Velocity(bars, t, 3), Is.EqualTo((10.9 / 10.5 - 1) * 100 / 3).Within(1e-9));
    }

    [Test]
    public void Velocity_ZeroVolumeGap_ReadsAsFlat()
    {
        var bars = Rising(5);
        for (int i = 5; i < 10; i++) bars.Add(Bar(SessionOpen.AddMinutes(i), 10.4, 0));
        Assert.That(MinuteBarMath.Velocity(bars, SessionOpen.AddMinutes(10), 3), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void Velocity_FailsClosed_WhenWindowStartHasNoBar()
    {
        var bars = Rising(2);
        Assert.That(MinuteBarMath.Velocity(bars, SessionOpen.AddMinutes(2), 3), Is.Null);
    }

    [Test]
    public void Acceleration_IsChangeInThreeMinuteVelocity()
    {
        var bars = Rising();
        var t = SessionOpen.AddMinutes(10);
        var v3Now = MinuteBarMath.Velocity(bars, t, 3)!.Value;
        var v3Before = MinuteBarMath.Velocity(bars, t.AddMinutes(-3), 3)!.Value;
        Assert.That(MinuteBarMath.Acceleration(bars, t), Is.EqualTo(v3Now - v3Before).Within(1e-12));
    }

    [Test]
    public void VolumeRoc_LastThreeMinutesOverThePriorThree()
    {
        var bars = Rising();
        // bars ending 08:05-08:07 print 100 each; bars ending 08:08-08:10 print 200 each.
        for (int i = 7; i < 10; i++) bars[i] = Bar(bars[i].StartUtc, (double)bars[i].Close, 200);
        Assert.That(MinuteBarMath.VolumeRoc(bars, SessionOpen.AddMinutes(10)), Is.EqualTo(2.0).Within(1e-12));
    }

    [Test]
    public void VolumeRoc_FailsClosed_WhenPriorWindowPrintedNothing()
    {
        var bars = new List<Candle>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(SessionOpen.AddMinutes(i), 10, i >= 7 ? 100 : 0));
        Assert.That(MinuteBarMath.VolumeRoc(bars, SessionOpen.AddMinutes(10)), Is.Null);
    }

    // ── Formation snapshot ────────────────────────────────────────────────

    [Test]
    public void Compute_DistToPmHigh_And_Gap()
    {
        var bars = Rising();
        bars[4] = Bar(bars[4].StartUtc, 10.4, 100, high: 12.0);
        var snap = GapperFormationFeatures.Compute(Inputs(bars, SessionOpen.AddMinutes(10)));
        Assert.Multiple(() =>
        {
            Assert.That(snap.DistToPmHighPct, Is.EqualTo((12.0 - 10.9) / 12.0 * 100).Within(1e-9));
            Assert.That(snap.GapPct, Is.EqualTo((10.9 / 8.0 - 1) * 100).Within(1e-9));
            Assert.That(snap.PmCumVol, Is.EqualTo(1000));
            Assert.That(snap.TradesProxy, Is.EqualTo(10));
            Assert.That(snap.VolRatioTod, Is.Null, "no profile, no time-of-day ratio");
            Assert.That(snap.Velocity3m, Is.EqualTo(MinuteBarMath.Velocity(bars, SessionOpen.AddMinutes(10), 3)));
        });
    }

    [Test]
    public void Compute_NeverReadsPastDecisionTime()
    {
        var t = SessionOpen.AddMinutes(10);
        var before = Rising();
        var early = new CatalystObservation(SessionOpen.AddMinutes(2), "Fda", 1, 1, 80);
        var baseline = GapperFormationFeatures.Compute(Inputs(before, t, early));

        var after = Rising();
        for (int i = 10; i < 40; i++) after.Add(Bar(SessionOpen.AddMinutes(i), 20 + i, 50_000, high: 99));
        var late = new CatalystObservation(SessionOpen.AddMinutes(15), "OfferingDilution", -1, 1, 95);
        var withFuture = GapperFormationFeatures.Compute(Inputs(after, t, early, late));

        Assert.That(JsonSerializer.Serialize(withFuture), Is.EqualTo(JsonSerializer.Serialize(baseline)));
        Assert.That(withFuture.CatalystPreClass, Is.EqualTo("Fda"));
    }

    [Test]
    public void Compute_FillsExtraFromRegistry_AndDailyFeatures()
    {
        var daily = new List<Candle>();
        for (int i = 0; i < 30; i++)
        {
            var d = new DateTime(2026, 6, 1, 13, 30, 0, DateTimeKind.Utc).AddDays(i);
            daily.Add(new Candle { Symbol = "T", StartUtc = d, EndUtc = d.AddHours(6.5), Open = 8m, High = 8.2m, Low = 7.8m, Close = 8m, Volume = 1000 });
        }
        var last = daily[^1];
        daily[^1] = new Candle { Symbol = "T", StartUtc = last.StartUtc, EndUtc = last.EndUtc, Open = 8.4m, High = 8.8m, Low = 8.3m, Close = 8.8m, Volume = 5000 };

        var inputs = Inputs(Rising(), SessionOpen.AddMinutes(10)) with { DailyBars = daily, PriorClose = null };
        var snap = GapperFormationFeatures.Compute(inputs);
        Assert.Multiple(() =>
        {
            Assert.That(snap.Extra["overnightReturnD1"], Is.EqualTo(5.0).Within(1e-9));
            Assert.That(snap.Extra["priorDayVolumeRatio"], Is.EqualTo(5.0).Within(1e-9));
            Assert.That(snap.VolumeRatioD1, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(snap.AbnormalReturnD1, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(snap.GapPct, Is.EqualTo((10.9 / 8.8 - 1) * 100).Within(1e-9), "prior close derived from the last daily bar");
        });
    }

    // ── Time-of-day profile ───────────────────────────────────────────────

    private static List<Candle> FullDay(double frontLoad)
    {
        var bars = new List<Candle>();
        for (int m = 0; m < TimeOfDayProfile.MinutesPerDay; m++)
            bars.Add(Bar(SessionOpen.AddMinutes(m), 10, m < 330 ? frontLoad : 1));
        return bars;
    }

    [Test]
    public void TimeOfDayProfile_RoundTrips_AndIsMonotonic()
    {
        var profile = TimeOfDayProfile.Build([(3, FullDay(2)), (3, FullDay(4)), (3, FullDay(3)), (7, FullDay(1))]);
        var restored = TimeOfDayProfile.FromJson(profile.ToJson());

        double previous = -1;
        for (int m = 0; m <= TimeOfDayProfile.MinutesPerDay; m++)
        {
            var f = restored.FractionAt(3, TimeSpan.FromHours(4) + TimeSpan.FromMinutes(m));
            Assert.That(f, Is.GreaterThanOrEqualTo(previous), $"fraction fell at minute {m}");
            previous = f;
        }
        Assert.Multiple(() =>
        {
            Assert.That(restored.Version, Is.EqualTo(TimeOfDayProfile.CurrentVersion));
            Assert.That(restored.FractionAt(3, new TimeSpan(4, 0, 0)), Is.EqualTo(0));
            Assert.That(restored.FractionAt(3, new TimeSpan(20, 0, 0)), Is.EqualTo(1).Within(1e-12));
            Assert.That(restored.FractionAt(3, new TimeSpan(9, 30, 0)), Is.EqualTo(profile.FractionAt(3, new TimeSpan(9, 30, 0))));
            Assert.That(restored.FractionAt(3, new TimeSpan(9, 30, 0)), Is.EqualTo(330.0 * 3 / (330 * 3 + 630)).Within(1e-9), "median of the three decile-3 days");
            Assert.That(restored.FractionAt(9, new TimeSpan(12, 0, 0)), Is.EqualTo(0.5).Within(1e-12), "an empty decile is uniform");
        });
    }

    [Test]
    public void TimeOfDayProfile_VolRatioTod_ScalesByExpectedFraction()
    {
        var profile = TimeOfDayProfile.Build([(0, FullDay(1))]);
        var at0930 = profile.FractionAt(0, new TimeSpan(9, 30, 0));
        Assert.Multiple(() =>
        {
            Assert.That(profile.VolRatioTod(1000, 10_000, 0, new TimeSpan(9, 30, 0)), Is.EqualTo(1000 / (10_000 * at0930)).Within(1e-9));
            Assert.That(profile.VolRatioTod(1000, 10_000, 0, new TimeSpan(4, 0, 0)), Is.Null, "nothing is expected at the open");
            Assert.That(GapperVolumeRatio.Compute(1000, 10_000), Is.EqualTo(0.1).Within(1e-12), "no profile: plain cumVol / ADV");
            Assert.That(GapperVolumeRatio.Compute(1000, 10_000, profile, 0, new TimeSpan(9, 30, 0)), Is.EqualTo(profile.VolRatioTod(1000, 10_000, 0, new TimeSpan(9, 30, 0))));
        });
    }

    [Test]
    public void TimeOfDayProfile_RejectsOtherVersions()
    {
        var json = TimeOfDayProfile.Build([(0, FullDay(1))]).ToJson().Replace("\"version\":1", "\"version\":2");
        Assert.That(() => TimeOfDayProfile.FromJson(json), Throws.TypeOf<InvalidDataException>());
    }

    // ── Velocity exit ─────────────────────────────────────────────────────

    private static readonly DateTime Entry = SessionOpen.AddMinutes(30);

    /// <summary>Runs from entry 10 up to <paramref name="peak"/> over five bars, then prints <paramref name="flatMinutes"/> flat bars.</summary>
    private static List<Candle> RunThenStall(double peak, int flatMinutes, double? high = null)
    {
        var bars = new List<Candle>();
        for (int i = 1; i <= 5; i++) bars.Add(Bar(Entry.AddMinutes(i - 1), 10 + (peak - 10) * i / 5, 100));
        for (int i = 0; i < flatMinutes; i++) bars.Add(Bar(Entry.AddMinutes(5 + i), peak, 100, high: high));
        return bars;
    }

    private static DateTime EndOf(List<Candle> bars) => bars[^1].EndUtc;

    [Test]
    public void VelocityExit_FiresOnlyAfterMinRun()
    {
        var bars = RunThenStall(10.3, 4);
        var strict = new VelocityExitRule(new VelocityExitParameters(0.5, 3, MinRunPercent: 5));
        var loose = new VelocityExitRule(new VelocityExitParameters(0.5, 3, MinRunPercent: 2));
        Assert.Multiple(() =>
        {
            Assert.That(strict.Evaluate(bars, 10m, EndOf(bars)).Exit, Is.False, "a 3% run has not reached the 5% minimum");
            Assert.That(loose.Evaluate(bars, 10m, EndOf(bars)).Exit, Is.True, "after the minimum run a dead velocity exits");
            Assert.That(loose.Evaluate(bars, 10m, EndOf(bars)).Velocity, Is.EqualTo(0).Within(1e-12));
        });
    }

    [Test]
    public void VelocityExit_DoesNotFireWhileVelocityHolds()
    {
        var bars = new List<Candle>();
        for (int i = 0; i < 8; i++) bars.Add(Bar(Entry.AddMinutes(i), 10 + 0.1 * i, 100));
        var rule = new VelocityExitRule(new VelocityExitParameters(0.5, 3));
        var verdict = rule.Evaluate(bars, 10m, EndOf(bars));
        Assert.That(verdict.Exit, Is.False);
        Assert.That(verdict.Velocity, Is.GreaterThan(0.5));
    }

    [Test]
    public void VelocityExit_DoesNotFireBeforeArmTime()
    {
        var bars = RunThenStall(10.5, 4);
        var rule = new VelocityExitRule(new VelocityExitParameters(0.5, 3, ArmAtEt: new TimeSpan(9, 0, 0)));
        Assert.Multiple(() =>
        {
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars)).Exit, Is.False, "04:39 ET is before the 09:00 arm");
            var armedBars = bars.Select(b => Bar(b.StartUtc.AddHours(5), (double)b.Close, 100)).ToList();
            Assert.That(rule.Evaluate(armedBars, 10m, EndOf(armedBars)).Exit, Is.True, "09:39 ET is armed");
        });
    }

    [Test]
    public void VelocityExit_HonorsRequireLowerHigh()
    {
        var flat = RunThenStall(10.5, 6);
        var rule = new VelocityExitRule(new VelocityExitParameters(0.5, 3, RequireLowerHigh: true));
        Assert.That(rule.Evaluate(flat, 10m, EndOf(flat)).Exit, Is.False, "no swing highs at all");

        // Two confirmed swing highs since entry, the second below the first, then a stall.
        double[] closes = [10.2, 10.4, 10.8, 10.4, 10.2, 10.3, 10.6, 10.3, 10.2, 10.2, 10.2, 10.2];
        var lowerHigh = closes.Select((c, i) => Bar(Entry.AddMinutes(i), c, 100)).ToList();
        Assert.That(rule.Evaluate(lowerHigh, 10m, EndOf(lowerHigh)).Exit, Is.True);
    }

    [Test]
    public void VelocityExit_HonorsRequireVwapLoss()
    {
        var bars = RunThenStall(10.5, 4);
        var rule = new VelocityExitRule(new VelocityExitParameters(0.5, 3, RequireVwapLoss: true));
        Assert.Multiple(() =>
        {
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars)).Exit, Is.False, "no VWAP given: hold");
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars), sessionVwap: 10.2m).Exit, Is.False, "still above VWAP");
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars), sessionVwap: 10.8m).Exit, Is.True, "under VWAP and stalled");
        });
    }

    [Test]
    public void VelocityExit_StandsDownWhenExtendedFromTheOpen()
    {
        var bars = RunThenStall(12, 4);
        var rule = new VelocityExitRule(new VelocityExitParameters(0.5, 3, ExtensionFromOpenSwitchPct: 10));
        Assert.Multiple(() =>
        {
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars), rthOpen: 10m).Exit, Is.False, "20% over the open: trail instead");
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars), rthOpen: 11.5m).Exit, Is.True, "4% over the open: the switch doesn't apply");
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars)).Exit, Is.True, "no open yet (premarket): the switch doesn't apply");
        });
    }

    [Test]
    public void VelocityExit_FailsClosedOnShortInput()
    {
        var rule = new VelocityExitRule(new VelocityExitParameters(0.5, 3));
        var one = new List<Candle> { Bar(Entry, 10.5, 100) };
        Assert.Multiple(() =>
        {
            Assert.That(rule.Evaluate([], 10m, Entry.AddMinutes(5)).Exit, Is.False);
            Assert.That(rule.Evaluate(one, 10m, EndOf(one)).Exit, Is.False, "one bar can't carry a 3-minute velocity");
            Assert.That(rule.Evaluate(one, 0m, EndOf(one)).Exit, Is.False);
            Assert.That(() => new VelocityExitRule(new VelocityExitParameters(0.5, 2)), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void VelocityExit_ShortSide_MeasuresTheDownMove()
    {
        var bars = new List<Candle>();
        for (int i = 1; i <= 5; i++) bars.Add(Bar(Entry.AddMinutes(i - 1), 10 - 0.1 * i, 100));
        for (int i = 0; i < 4; i++) bars.Add(Bar(Entry.AddMinutes(5 + i), 9.5, 100));
        var rule = new VelocityExitRule(new VelocityExitParameters(0.5, 3, MinRunPercent: 3));
        Assert.Multiple(() =>
        {
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars), shortSide: true).Exit, Is.True, "a 5% drop that stalled");
            Assert.That(rule.Evaluate(bars, 10m, EndOf(bars), shortSide: false).Exit, Is.False, "for a long the run never happened");
        });
    }
}
