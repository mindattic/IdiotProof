using IdiotProof.Core.Monitoring.Formation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Tests;

/// <summary>IP-T628: the live move start uses amendment 09's m0 rule (5x the profile's minute, close above the prior close, no earlier +20%).</summary>
[TestFixture]
public sealed class MoveStartWatchTests
{
    private static readonly DateOnly Day = new(2026, 11, 23);
    private const double Prev = 1.00;
    private const double Adv = 96_000; // a flat profile expects 100 shares a minute

    private static readonly TimeOfDayProfile Flat = new(Enumerable.Range(0, TimeOfDayProfile.DecileCount)
        .Select(_ => (IReadOnlyList<double>)Enumerable.Range(0, TimeOfDayProfile.MinutesPerDay + 1).Select(i => i / (double)TimeOfDayProfile.MinutesPerDay).ToList())
        .ToList());

    private static Candle Bar(TimeSpan startEt, double close, double volume, double? high = null)
    {
        var start = MinuteBarMath.ToUtc(Day, startEt);
        return new Candle { Symbol = "ABCD", StartUtc = start, EndUtc = start.AddMinutes(1), Open = (decimal)close, High = (decimal)(high ?? close), Low = (decimal)close, Close = (decimal)close, Volume = (decimal)volume };
    }

    private static DateTime At(TimeSpan et) => MinuteBarMath.ToUtc(Day, et);

    [Test]
    public void FindsTheFirstAbnormalUpMinute_AfterTheOpen()
    {
        var bars = new List<Candle>
        {
            Bar(new(9, 0, 0), 1.05, 2_000),   // premarket: never m0
            Bar(new(9, 30, 0), 1.02, 300),    // 3x: not abnormal
            Bar(new(9, 31, 0), 0.98, 900),    // abnormal but below the prior close
            Bar(new(9, 32, 0), 1.06, 600),    // m0
            Bar(new(9, 33, 0), 1.10, 5_000),
        };

        var hit = MoveStartDetector.Evaluate(bars, Day, At(new(9, 40, 0)), Prev, Adv, 3, Flat, out var excluded);

        Assert.That(excluded, Is.False);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.EndEt, Is.EqualTo(new TimeSpan(9, 33, 0)));
        Assert.That(hit.Gain, Is.EqualTo(0.06).Within(1e-9));
        Assert.That(hit.ExpectedVolume, Is.EqualTo(100).Within(1e-6));
    }

    [Test]
    public void APremarketPlus20_ExcludesTheDay()
    {
        var bars = new List<Candle> { Bar(new(8, 0, 0), 1.10, 100, high: 1.25), Bar(new(9, 32, 0), 1.06, 600) };

        var hit = MoveStartDetector.Evaluate(bars, Day, At(new(9, 40, 0)), Prev, Adv, 3, Flat, out var excluded);

        Assert.That(hit, Is.Null);
        Assert.That(excluded, Is.True);
    }

    [Test]
    public void AnEarlierRegularHoursPlus20_MeansNoMoveStart()
    {
        var bars = new List<Candle> { Bar(new(9, 30, 0), 1.15, 100, high: 1.21), Bar(new(9, 32, 0), 1.18, 600) };

        Assert.That(MoveStartDetector.Evaluate(bars, Day, At(new(9, 40, 0)), Prev, Adv, 3, Flat, out var excluded), Is.Null);
        Assert.That(excluded, Is.False);
    }

    [Test]
    public void ABarStillOpenAtNow_IsNotUsed()
    {
        var bars = new List<Candle> { Bar(new(9, 32, 0), 1.06, 600) };

        Assert.That(MoveStartDetector.Evaluate(bars, Day, At(new(9, 32, 30)), Prev, Adv, 3, Flat, out _), Is.Null);
        Assert.That(MoveStartDetector.Evaluate(bars, Day, At(new(9, 33, 0)), Prev, Adv, 3, Flat, out _), Is.Not.Null);
    }

    [Test]
    public void TheSharedRule_MatchesTheStudyArraysIndexing()
    {
        var v = new double[MoveStartRule.RthEndIndex];
        var c = Enumerable.Repeat(double.NaN, MoveStartRule.RthEndIndex).ToArray();
        var h = (double[])c.Clone();
        v[400] = 500; c[400] = 1.01; h[400] = 1.01;

        Assert.That(MoveStartRule.Find(v, c, h, Prev, Adv, 0, Flat, 5), Is.EqualTo(400));
        Assert.That(MoveStartRule.Find(v, c, h, Prev, Adv, 0, Flat, 5.01), Is.EqualTo(-1));
    }
}
