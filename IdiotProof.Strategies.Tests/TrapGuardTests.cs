using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;
using IdiotProof.Strategies;

namespace IdiotProof.Strategies.Tests;

/// <summary>NotBullTrap / NotStopHunt / NotVwapLoss: learned, fail-closed trap guards (IP-T381).</summary>
public class TrapGuardTests
{
    public static IEnumerable<TestCaseData> Guards()
    {
        yield return new TestCaseData(IndicatorType.NotBullTrap, "NotBullTrap",
            (Action<IndicatorSnapshot, bool?, double?, int?>)((s, p, r, n) => { s.BullTrapPresent = p; s.BullTrapRisk = r; s.BullTrapSamples = n; }))
            .SetArgDisplayNames("NotBullTrap");
        yield return new TestCaseData(IndicatorType.NotStopHunt, "NotStopHunt",
            (Action<IndicatorSnapshot, bool?, double?, int?>)((s, p, r, n) => { s.StopHuntPresent = p; s.StopHuntRisk = r; s.StopHuntSamples = n; }))
            .SetArgDisplayNames("NotStopHunt");
        yield return new TestCaseData(IndicatorType.NotVwapLoss, "NotVwapLoss",
            (Action<IndicatorSnapshot, bool?, double?, int?>)((s, p, r, n) => { s.VwapLossPresent = p; s.VwapLossRisk = r; s.VwapLossSamples = n; }))
            .SetArgDisplayNames("NotVwapLoss");
    }

    private static bool Eval(IndicatorType type, Action<IndicatorSnapshot, bool?, double?, int?> set, bool? present, double? risk = null, int? samples = null)
    {
        var snap = new IndicatorSnapshot { Symbol = "X", Price = 2.0 };
        set(snap, present, risk, samples);
        return new IndicatorCondition(type, 0.4, 30).Evaluate(snap);
    }

    [TestCaseSource(nameof(Guards))]
    public void FailsClosed_PassesOnlyOnMeasuredSafety(IndicatorType type, string verb, Action<IndicatorSnapshot, bool?, double?, int?> set)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Eval(type, set, false), Is.True, "absent setup passes");
            Assert.That(Eval(type, set, null), Is.False, "unknown window fails closed");
            Assert.That(Eval(type, set, true, 0.1, 12), Is.False, "thin history fails closed");
            Assert.That(Eval(type, set, true, null, 100), Is.False, "missing risk fails closed");
            Assert.That(Eval(type, set, true, 0.3, 50), Is.True, "measured safe passes");
            Assert.That(Eval(type, set, true, 0.7, 50), Is.False, "measured dangerous blocks");
        });
    }

    [TestCaseSource(nameof(Guards))]
    public void RoundTrips_AndDefaults(IndicatorType type, string verb, Action<IndicatorSnapshot, bool?, double?, int?> _)
    {
        var parsed = ScriptParser.ParseScript($"Stock.Ticker(\"X\").{verb}(0.35, 40).Long()")!;
        var cond = parsed.EntryConditions.OfType<IndicatorCondition>().Single(c => c.Type == type);
        Assert.That((cond.Parameter, cond.Parameter2), Is.EqualTo(((double?)0.35, (double?)40)));
        Assert.That(parsed.ToScript(), Does.Contain($"{verb}(0.35, 40)"));

        var defaults = ScriptParser.ParseScript($"Stock.Ticker(\"X\").{verb}().Long()")!
            .EntryConditions.OfType<IndicatorCondition>().Single(c => c.Type == type);
        Assert.That((defaults.Parameter, defaults.Parameter2), Is.EqualTo(((double?)0.5, (double?)30)));
    }

    [Test]
    public void Builder_EmitsEachGuard()
    {
        var text = Stock.Ticker("X").NotBullTrap(0.3, 25).NotStopHunt(0.45, 35).NotVwapLoss(0.5, 30).Long().ToScript();
        Assert.That(text, Does.Contain("NotBullTrap(0.3, 25)").And.Contain("NotStopHunt(0.45, 35)").And.Contain("NotVwapLoss(0.5, 30)"));
    }

    [Test]
    public void SnapshotBuilder_ShortWindow_LeavesEveryTrapUnknown()
    {
        var snap = IndicatorSnapshotBuilder.Build("X", Candles(Enumerable.Range(0, 20).Select(i => 3.0 + i * 0.01).ToArray()));
        Assert.That(new[] { snap.BullTrapPresent, snap.StopHuntPresent, snap.VwapLossPresent }, Is.All.Null);
    }

    [Test]
    public void SnapshotBuilder_SteadyRise_HasNoTrapsPresent()
    {
        var snap = IndicatorSnapshotBuilder.Build("X", Candles(Enumerable.Range(0, 120).Select(i => 2.0 + i * 0.01).ToArray()));
        Assert.That(new[] { snap.BullTrapPresent, snap.StopHuntPresent, snap.VwapLossPresent }, Is.All.False);
    }

    [Test]
    public void SnapshotBuilder_BreakoutThenCloseBackBelow_IsABullTrap_WithALearnedRate()
    {
        var rng = new Random(5);
        var closes = new List<double> { 3.0 };
        for (int i = 1; i < 200; i++) closes.Add(3.0 + 0.03 * Math.Sin(i / 3.0) + 0.005 * (rng.NextDouble() - 0.5));
        closes.Add(3.10); // breaks the 20-bar high
        closes.Add(2.98); // closes back below it
        var snap = IndicatorSnapshotBuilder.Build("X", Candles(closes));
        Assert.That(snap.BullTrapPresent, Is.True);
        Assert.That(snap.BullTrapSamples, Is.Not.Null);
        Assert.That(snap.BullTrapRisk, Is.InRange(0.0, 1.0));
    }

    private static List<Candle> Candles(IReadOnlyList<double> closes)
    {
        var t0 = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        var list = new List<Candle>();
        for (int i = 0; i < closes.Count; i++)
        {
            decimal o = (decimal)(i == 0 ? closes[0] : closes[i - 1]), c = (decimal)closes[i];
            list.Add(new Candle
            {
                Symbol = "X", StartUtc = t0.AddMinutes(i), EndUtc = t0.AddMinutes(i + 1),
                Open = o, Close = c, High = Math.Max(o, c), Low = Math.Min(o, c), Volume = 1000,
            });
        }
        return list;
    }
}
