using IdiotProof.Evidence.Adaptation;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class AdaptationTests
{
    private static readonly DateTime Open = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    [Test]
    public void Bandit_ConvergesOnTheBetterVariant_FromSyntheticPayoffs()
    {
        var rng = new Random(5);
        var bandit = new ThompsonBandit(3, seed: 42);
        double[] trueWinRate = { 0.45, 0.62, 0.50 };
        var pulls = new int[3];
        for (int t = 0; t < 2000; t++)
        {
            int arm = bandit.Choose();
            pulls[arm]++;
            bandit.Update(arm, rng.NextDouble() < trueWinRate[arm]);
        }
        Assert.That(pulls[1], Is.GreaterThan(pulls[0] + pulls[2]), string.Join(",", pulls));
        Assert.That(bandit.ProbabilityBest()[1], Is.GreaterThan(0.9));
        Assert.That(bandit.PosteriorMean(1), Is.EqualTo(0.62).Within(0.05));
    }

    [Test]
    public void Bandit_IsDeterministicForASeed()
    {
        int[] Run()
        {
            var b = new ThompsonBandit(4, seed: 7);
            return Enumerable.Range(0, 50).Select(i => { int a = b.Choose(); b.Update(a, i % 3 == 0); return a; }).ToArray();
        }
        Assert.That(Run(), Is.EqualTo(Run()));
    }

    [Test]
    public void InformationGate_BlocksTheFirstThinMinutes_ThenOpens()
    {
        var candles = Bars(Enumerable.Repeat(3.0, 30).ToArray());
        var early = InformationGate.Check(candles, Open, Open.AddMinutes(5));
        Assert.That(early.Ready, Is.False);
        Assert.That(early.ClosedBars, Is.EqualTo(5));

        var formingBarIgnored = InformationGate.Check(candles, Open, Open.AddMinutes(14).AddSeconds(30),
            new InformationGateOptions { MinBars = 15, MinElapsed = TimeSpan.Zero });
        Assert.That(formingBarIgnored.Ready, Is.False, "the 15th bar has not closed yet");

        var later = InformationGate.Check(candles, Open, Open.AddMinutes(20));
        Assert.That(later.Ready, Is.True, later.Reason);
    }

    [Test]
    public void InformationGate_RequiresVolume()
    {
        var candles = Bars(Enumerable.Repeat(3.0, 30).ToArray());
        var d = InformationGate.Check(candles, Open, Open.AddMinutes(30), new InformationGateOptions { MinVolume = 1_000_000 });
        Assert.That(d.Ready, Is.False);
        Assert.That(d.Reason, Does.Contain("shares"));
    }

    [Test]
    public void ShadowEvaluator_HandsTheCallbackOnlyClosedBars()
    {
        var candles = Bars(Enumerable.Range(0, 60).Select(i => 3 + i * 0.01).ToArray());
        var now = Open.AddMinutes(20).AddSeconds(30);
        DateTime latestSeen = DateTime.MinValue;
        int countSeen = 0;
        ShadowEvaluator.Evaluate(candles, now, new[] { new ParameterVariant("a", 1, null, 1) }, (v, closed) =>
        {
            countSeen = closed.Count;
            latestSeen = closed.Max(c => c.EndUtc);
            return Array.Empty<ShadowTrade>();
        }, seed: 1);
        Assert.That(countSeen, Is.EqualTo(20));
        Assert.That(latestSeen, Is.LessThanOrEqualTo(now));
    }

    [Test]
    public void ShadowEvaluator_RejectsASimulatorThatReportsAFutureExit()
    {
        var candles = Bars(Enumerable.Repeat(3.0, 40).ToArray());
        var now = Open.AddMinutes(10);
        Assert.Throws<InvalidOperationException>(() => ShadowEvaluator.Evaluate(candles, now,
            new[] { new ParameterVariant("cheat", 1, null, 1) },
            (v, closed) => new[] { new ShadowTrade(Open, Open.AddMinutes(30), 5) }, seed: 1));
    }

    [Test]
    public void SimulateLongBracket_DropsTheTradeStillOpenAtTheEndOfTheSlice()
    {
        // Rises steadily: a 50% target is never hit, a 50% stop never trips, so the trade stays open.
        var closed = Bars(Enumerable.Range(0, 20).Select(i => 3 + i * 0.01).ToArray());
        var trades = ShadowEvaluator.SimulateLongBracket(new ParameterVariant("x", 50, null, 50), closed, (c, i) => i == 0).ToList();
        Assert.That(trades, Is.Empty);
    }

    [Test]
    public void ShadowEvaluator_PrefersTheVariantThatWinsOnTodaysTape()
    {
        // Sawtooth: +2% over 4 bars, then -3.5% in one bar. Entering after each drop, a 1% target banks
        // the climb every time, while waiting for +10% with a 1% stop gets stopped by the next drop.
        var closes = new List<double>();
        double p = 3.0;
        for (int k = 0; k < 30; k++)
        {
            for (int i = 0; i < 4; i++) { p *= 1.005; closes.Add(p); }
            p *= 0.965; closes.Add(p);
        }
        var candles = Bars(closes.ToArray());
        var variants = new[]
        {
            new ParameterVariant("bank-1pct", 3, null, 1.0),
            new ParameterVariant("hold-for-10pct", 1.0, null, 10),
        };
        var eval = ShadowEvaluator.Evaluate(candles, Open.AddMinutes(150), variants,
            (v, closed) => ShadowEvaluator.SimulateLongBracket(v, closed, (c, i) => i > 0 && c[i].Close < c[i - 1].Close), seed: 3);
        Assert.That(eval.Scores[0].Wins, Is.GreaterThan(eval.Scores[0].Losses));
        Assert.That(eval.Scores[1].Losses, Is.GreaterThan(eval.Scores[1].Wins));
        Assert.That(eval.Scores[0].ProbabilityBest, Is.GreaterThan(0.95));
        Assert.That(eval.Chosen.Name, Is.EqualTo("bank-1pct"));
    }

    [Test]
    public void Deriver_ScalesStopsToEachTickersOwnNoise()
    {
        var calm = Bars(Wiggle(3.0, 0.002, 200, seed: 1));
        var wild = Bars(Wiggle(3.0, 0.02, 200, seed: 1));
        var pc = AdaptiveParameterDeriver.Derive(calm);
        var pw = AdaptiveParameterDeriver.Derive(wild);
        Assert.That(pw.StopLossPercent, Is.GreaterThan(pc.StopLossPercent * 3));
        Assert.That(pw.AtrPercent, Is.GreaterThan(pc.AtrPercent * 3));
        Assert.That(pc.Samples, Is.EqualTo(190));
        Assert.That(AdaptiveParameterDeriver.VariantsAround(pw), Has.Count.EqualTo(6));
    }

    [Test]
    public void Quantile_Interpolates()
    {
        Assert.That(AdaptiveParameterDeriver.Quantile(new double[] { 1, 2, 3, 4, 5 }, 0.5), Is.EqualTo(3));
        Assert.That(AdaptiveParameterDeriver.Quantile(new double[] { 0, 10 }, 0.8), Is.EqualTo(8).Within(1e-12));
    }

    private static double[] Wiggle(double start, double sigma, int n, int seed)
    {
        var rng = new Random(seed);
        var r = new double[n];
        double p = start;
        for (int i = 0; i < n; i++) { p *= 1 + sigma * Synthetic.Gaussian(rng); r[i] = p; }
        return r;
    }

    private static List<Candle> Bars(double[] closes)
    {
        var list = new List<Candle>();
        for (int i = 0; i < closes.Length; i++)
        {
            decimal c = (decimal)closes[i];
            decimal o = i == 0 ? c : (decimal)closes[i - 1];
            list.Add(new Candle
            {
                Symbol = "PNY",
                StartUtc = Open.AddMinutes(i),
                EndUtc = Open.AddMinutes(i + 1),
                Open = o,
                Close = c,
                High = Math.Max(o, c),
                Low = Math.Min(o, c),
                Volume = 10_000,
            });
        }
        return list;
    }
}
