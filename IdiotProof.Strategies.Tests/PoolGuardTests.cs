using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;
using IdiotProof.Strategies;

namespace IdiotProof.Strategies.Tests;

/// <summary>NotIntoRaidedPool / NearRaidedPool: stop-pool guards on measured prior-session raid rates, fail-closed (IP-T404).</summary>
public class PoolGuardTests
{
    private static bool Eval(IndicatorType type, Action<IndicatorSnapshot> set, double p, int n)
    {
        var snap = new IndicatorSnapshot { Symbol = "X", Price = 3.0 };
        set(snap);
        return new IndicatorCondition(type, p, n).Evaluate(snap);
    }

    [Test]
    public void NotIntoRaidedPool_FailsClosed_PassesOnlyOnMeasuredSafety()
    {
        Action<IndicatorSnapshot> Into(bool? present, double? rate = null, int? n = null) =>
            s => { s.IntoPoolPresent = present; s.IntoPoolFailRate = rate; s.IntoPoolSamples = n; };
        Assert.Multiple(() =>
        {
            Assert.That(Eval(IndicatorType.NotIntoRaidedPool, Into(false), 0.4, 30), Is.True, "no pool overhead passes");
            Assert.That(Eval(IndicatorType.NotIntoRaidedPool, Into(null), 0.4, 30), Is.False, "unknown window fails closed");
            Assert.That(Eval(IndicatorType.NotIntoRaidedPool, Into(true, 0.1, 5), 0.4, 30), Is.False, "thin history fails closed");
            Assert.That(Eval(IndicatorType.NotIntoRaidedPool, Into(true, 0.3, 50), 0.4, 30), Is.True, "moves into pools like it usually carried through");
            Assert.That(Eval(IndicatorType.NotIntoRaidedPool, Into(true, 0.7, 50), 0.4, 30), Is.False, "moves into pools like it usually failed");
        });
    }

    [Test]
    public void NearRaidedPool_PassesOnlyOnAMeasuredReversingRaid()
    {
        Action<IndicatorSnapshot> Raid(bool? present, double? rate = null, int? n = null) =>
            s => { s.RaidedPoolPresent = present; s.RaidedPoolReverseRate = rate; s.RaidedPoolSamples = n; };
        Assert.Multiple(() =>
        {
            Assert.That(Eval(IndicatorType.NearRaidedPool, Raid(false), 0.6, 30), Is.False, "no raid, no trigger");
            Assert.That(Eval(IndicatorType.NearRaidedPool, Raid(null), 0.6, 30), Is.False, "unknown window fails closed");
            Assert.That(Eval(IndicatorType.NearRaidedPool, Raid(true, 0.9, 5), 0.6, 30), Is.False, "thin history fails closed");
            Assert.That(Eval(IndicatorType.NearRaidedPool, Raid(true, 0.7, 40), 0.6, 30), Is.True, "raids like it usually reversed");
            Assert.That(Eval(IndicatorType.NearRaidedPool, Raid(true, 0.4, 40), 0.6, 30), Is.False, "raids like it usually broke through");
        });
    }

    [TestCase("NotIntoRaidedPool", IndicatorType.NotIntoRaidedPool, 0.5)]
    [TestCase("NearRaidedPool", IndicatorType.NearRaidedPool, 0.6)]
    public void RoundTrips_AndDefaults(string verb, IndicatorType type, double defaultP)
    {
        var parsed = ScriptParser.ParseScript($"Stock.Ticker(\"X\").{verb}(0.35, 40).Long()")!;
        var cond = parsed.EntryConditions.OfType<IndicatorCondition>().Single(c => c.Type == type);
        Assert.That((cond.Parameter, cond.Parameter2), Is.EqualTo(((double?)0.35, (double?)40)));
        Assert.That(parsed.ToScript(), Does.Contain($"{verb}(0.35, 40)"));

        var defaults = ScriptParser.ParseScript($"Stock.Ticker(\"X\").{verb}().Long()")!
            .EntryConditions.OfType<IndicatorCondition>().Single(c => c.Type == type);
        Assert.That((defaults.Parameter, defaults.Parameter2), Is.EqualTo(((double?)defaultP, (double?)30)));
    }

    [Test]
    public void Builder_EmitsBothGuards()
    {
        var text = Stock.Ticker("X").NotIntoRaidedPool(0.3, 25).NearRaidedPool(0.65, 35).Long().ToScript();
        Assert.That(text, Does.Contain("NotIntoRaidedPool(0.3, 25)").And.Contain("NearRaidedPool(0.65, 35)"));
    }

    [Test]
    public void SnapshotBuilder_ShortWindow_LeavesPoolsUnknown()
    {
        var snap = IndicatorSnapshotBuilder.Build("X", Day(new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc), Flat(5, 3.05)));
        Assert.That(new[] { snap.IntoPoolPresent, snap.RaidedPoolPresent }, Is.All.Null);
    }

    private static readonly DateTime Mon = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static IEnumerable<(double H, double L, double C)> Flat(int n, double c) =>
        Enumerable.Repeat((c + 0.005, c - 0.005, c), n);

    /// <summary>Flat at 3.05, a dip that pierces 3.00 and closes back above, then 12 bars back at 3.04.</summary>
    private static IEnumerable<(double H, double L, double C)> RaidBelow() =>
        Flat(12, 3.05).Append((3.05, 2.97, 3.02)).Concat(Flat(12, 3.04));

    private static List<Candle> Day(DateTime openUtc, IEnumerable<(double H, double L, double C)> bars) =>
        bars.Select((b, i) =>
        {
            decimal c = (decimal)b.C;
            return new Candle
            {
                Symbol = "X", StartUtc = openUtc.AddMinutes(i), EndUtc = openUtc.AddMinutes(i + 1),
                Open = c, Close = c, High = (decimal)Math.Max(b.H, b.C), Low = (decimal)Math.Min(b.L, b.C), Volume = 1000,
            };
        }).ToList();

    private static IEnumerable<(double H, double L, double C)> TodayRaid() =>
        Flat(12, 3.05).Append((3.05, 2.97, 3.02)).Concat(Flat(2, 3.04));

    [Test]
    public void SnapshotBuilder_LearnsRaidsFromPriorSessionsOnly()
    {
        var history = Enumerable.Range(0, 10).SelectMany(_ => RaidBelow()).ToList();
        var priorDay = Day(Mon, history);
        var today = Day(Mon.AddDays(1), TodayRaid());
        var snap = IndicatorSnapshotBuilder.Build("X", priorDay.Concat(today).ToList());
        Assert.That(snap.RaidedPoolPresent, Is.True, "3.00 was pierced and reclaimed two bars ago");
        Assert.That(snap.RaidedPoolSamples, Is.GreaterThanOrEqualTo(10), "yesterday's ten raids are evidence");
        Assert.That(snap.RaidedPoolReverseRate, Is.GreaterThan(0.6));
        Assert.That(new IndicatorCondition(IndicatorType.NearRaidedPool, 0.6, 10).Evaluate(snap), Is.True);

        var sameDay = Day(Mon, history.Concat(TodayRaid()).ToList());
        var snapSameDay = IndicatorSnapshotBuilder.Build("X", sameDay);
        Assert.That(snapSameDay.RaidedPoolPresent, Is.True);
        Assert.That(snapSameDay.RaidedPoolSamples, Is.EqualTo(0), "today's own raids never grade today");
        Assert.That(new IndicatorCondition(IndicatorType.NearRaidedPool, 0.6, 10).Evaluate(snapSameDay), Is.False, "no prior-session evidence blocks");
    }
}
