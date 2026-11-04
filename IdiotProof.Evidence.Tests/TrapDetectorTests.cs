using IdiotProof.Evidence.Traps;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class TrapDetectorTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static Candle Bar(int i, double o, double h, double l, double c, double v = 1000) => new()
    {
        Symbol = "TRAP",
        StartUtc = T0.AddMinutes(i),
        EndUtc = T0.AddMinutes(i + 1),
        Open = (decimal)o, High = (decimal)h, Low = (decimal)l, Close = (decimal)c, Volume = (decimal)v,
    };

    private static List<Candle> FromCloses(IReadOnlyList<double> closes, Func<int, double>? volume = null)
    {
        var list = new List<Candle>();
        for (int i = 0; i < closes.Count; i++)
        {
            double o = i == 0 ? closes[0] : closes[i - 1], c = closes[i];
            list.Add(Bar(i, o, Math.Max(o, c) * 1.001, Math.Min(o, c) * 0.999, c, volume?.Invoke(i) ?? 1000));
        }
        return list;
    }

    private static List<Candle> RandomCandles(int n, int seed)
    {
        var rng = new Random(seed);
        var closes = Synthetic.PricesFromReturns(Synthetic.RandomWalkReturns(n - 1, seed, 0.01));
        return FromCloses(closes, _ => 500 + rng.Next(2000));
    }

    [Test]
    public void FallingKnife_SteadyDecline_ReadsLowerLowsBelowVwapAndNegativeReturn()
    {
        var closes = Enumerable.Range(0, 40).Select(i => 4.0 * Math.Pow(0.99, i)).ToArray();
        var s = new CausalSeries(FromCloses(closes, i => i > 30 ? 3000 : 1000));
        var f = TrapDetectors.FallingKnife().Features(s, 39);
        Assert.That(f, Is.Not.Null);
        Assert.That(f![0], Is.EqualTo(8), "capped consecutive lower lows");
        Assert.That(f[1], Is.GreaterThan(1.5), "down-bar volume is accelerating");
        Assert.That(f[2], Is.GreaterThan(0), "below VWAP");
        Assert.That(f[3], Is.LessThan(-0.08));
    }

    [Test]
    public void FallingKnife_IsAbsent_WhenPriceIsUp()
    {
        var closes = Enumerable.Range(0, 40).Select(i => 2.0 + i * 0.01).ToArray();
        Assert.That(TrapDetectors.FallingKnife().Features(new CausalSeries(FromCloses(closes)), 39), Is.Null);
    }

    [Test]
    public void FailedBreakout_BreakAboveThenCloseBackBelow_IsDetected()
    {
        var bars = new List<Candle>();
        for (int i = 0; i < 25; i++) bars.Add(Bar(i, 3.00, 3.05, 2.95, 3.00));
        bars.Add(Bar(25, 3.00, 3.20, 2.99, 3.10, 5000));
        bars.Add(Bar(26, 3.10, 3.11, 2.96, 2.98));
        var f = TrapDetectors.FailedBreakout().Features(new CausalSeries(bars), 26);
        Assert.That(f, Is.Not.Null);
        Assert.That(f![0], Is.EqualTo(1), "breakout one bar ago");
        Assert.That(f[1], Is.GreaterThan(0), "closed back below the broken high");
        Assert.That(f[2], Is.GreaterThan(3), "breakout-bar volume ratio");
        Assert.That(TrapDetectors.FailedBreakout().Features(new CausalSeries(bars), 25), Is.Null, "still above: no trap yet");
    }

    [Test]
    public void StopHuntWick_PierceThenReclaim_IsDetected_ButACloseBelowIsNot()
    {
        var bars = new List<Candle>();
        for (int i = 0; i < 25; i++) bars.Add(Bar(i, 3.00, 3.05, 2.95, 3.00));
        bars.Add(Bar(25, 3.00, 3.02, 2.85, 3.01, 4000));
        var f = TrapDetectors.StopHuntWick().Features(new CausalSeries(bars), 25);
        Assert.That(f, Is.Not.Null);
        Assert.That(f![0], Is.GreaterThan(0));
        Assert.That(f[1], Is.GreaterThan(0.8), "closed near the bar's high");

        bars[25] = Bar(25, 3.00, 3.02, 2.85, 2.90);
        Assert.That(TrapDetectors.StopHuntWick().Features(new CausalSeries(bars), 25), Is.Null);
    }

    [Test]
    public void VwapLoss_CrossBelowVwap_IsDetected()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 3.0 + i * 0.01).Concat(new[] { 3.0 }).ToArray();
        var s = new CausalSeries(FromCloses(closes));
        Assert.That(TrapDetectors.VwapLoss().Features(s, 30), Is.Not.Null);
        Assert.That(TrapDetectors.VwapLoss().Features(s, 29), Is.Null);
    }

    [Test]
    public void AllDetectors_HaveNoLookahead_ChangingLaterBarsNeverChangesAFeature()
    {
        var a = RandomCandles(300, 21);
        var b = a.Take(200).Concat(RandomCandles(100, 99).Select((c, k) => Bar(200 + k, (double)c.Open * 3, (double)c.High * 3, (double)c.Low * 3, (double)c.Close * 3, 99999))).ToList();
        var sa = new CausalSeries(a);
        var sb = new CausalSeries(b);
        foreach (var d in TrapDetectors.All())
            for (int i = 0; i < 200; i++)
                Assert.That(d.Features(sb, i), Is.EqualTo(d.Features(sa, i)), $"{d.Name} bar {i}");
    }

    [Test]
    public void Learn_RecoversPlantedBaseRates()
    {
        // Segments of 12 bars. Bar 0 is a setup bar: "loud" (volume 5000) or "quiet" (2000).
        // After a loud setup price collapses 80% of the time; after a quiet one, 20% of the time.
        var rng = new Random(5);
        var bars = new List<Candle>();
        double p = 3.0;
        int idx = 0;
        for (int seg = 0; seg < 600; seg++)
        {
            bool loud = seg % 2 == 0;
            bool crash = rng.NextDouble() < (loud ? 0.8 : 0.2);
            bars.Add(Bar(idx++, p, p * 1.001, p * 0.999, p, loud ? 5000 : 2000));
            for (int k = 0; k < 11; k++)
            {
                double next = crash ? p * 0.97 : p * 1.01;
                bars.Add(Bar(idx++, p, Math.Max(p, next), Math.Min(p, next), next));
                p = next;
            }
            if (p < 0.5 || p > 50) p = 3.0;
        }

        var detector = new TrapDetector("Planted", 1, (s, i) => s.Volume(i) > 1000 ? new[] { s.Volume(i) } : null, horizon: 10, adverseAtr: 1.0, bins: 2);
        detector.Learn(bars);
        var s = new CausalSeries(bars);
        var loudRate = detector.Assess(s, 12 * 10)!.Rate;
        var quietRate = detector.Assess(s, 12 * 11)!.Rate;
        Assert.That(loudRate.Samples, Is.GreaterThan(250));
        Assert.That(loudRate.Probability, Is.EqualTo(0.8).Within(0.07));
        Assert.That(quietRate.Probability, Is.EqualTo(0.2).Within(0.07));
        Assert.That(loudRate.Low, Is.GreaterThan(quietRate.High), "the intervals separate");
    }

    [Test]
    public void Assess_BeforeLearning_Throws_AndAThinBucketIsNeverClear()
    {
        var d = TrapDetectors.FallingKnife();
        var candles = RandomCandles(60, 3);
        Assert.Throws<InvalidOperationException>(() => d.Assess(candles, 59));

        var thin = new TrapAssessment("x", "0", Array.Empty<double>(), new OutcomeRate(0, 3, 0.2, 0, 0.5, 0.5));
        Assert.That(thin.IsClear(0.5, minSamples: 30), Is.False, "too few samples fails closed");
        var backed = new TrapAssessment("x", "0", Array.Empty<double>(), new OutcomeRate(10, 100, 0.108, 0.05, 0.17, 0.5));
        Assert.That(backed.IsClear(0.5, minSamples: 30), Is.True);
    }

    [Test]
    public void Learn_OnlyLabelsBarsWhoseOutcomeResolvedInsideTheHistory()
    {
        var candles = RandomCandles(400, 8);
        var d = new TrapDetector("All", 20, (s, i) => new[] { 1.0 }, horizon: 10);
        d.Learn(candles);
        Assert.That(d.Samples, Is.EqualTo(400 - 20 - 10));
    }
}
