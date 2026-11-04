using IdiotProof.Evidence.Adaptation;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class ExhaustionEvaluatorTests
{
    private static readonly DateTime Open = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static readonly ExhaustionOptions Options = new()
    {
        MinSamples = 10, KnifeMinSamples = 10, MinReversalProbability = 0.55, KnifeMaxProbability = 0.5,
    };

    /// <summary>
    /// 40 quiet bars around $3, then one ~8% capitulation bar; <paramref name="after"/> decides what follows:
    /// a sharp bounce (capitulation) or a continued bleed (falling knife).
    /// </summary>
    internal static List<Candle> Session(int seed, string after, int dayOffset = 0, bool truncateAtLow = false)
    {
        var rng = new Random(seed);
        var start = Open.AddDays(dayOffset);
        var bars = new List<Candle>();
        double price = 3.0;
        void Add(double close, double wick)
        {
            double open = bars.Count == 0 ? close : (double)bars[^1].Close;
            double hi = Math.Max(open, close) * (1 + wick), lo = Math.Min(open, close) * (1 - wick);
            int i = bars.Count;
            bars.Add(new Candle
            {
                Symbol = "PNY", StartUtc = start.AddMinutes(i), EndUtc = start.AddMinutes(i + 1),
                Open = (decimal)open, Close = (decimal)close, High = (decimal)hi, Low = (decimal)lo, Volume = 10_000 + rng.Next(5_000),
            });
        }

        for (int i = 0; i < 40; i++) { price *= 1 + 0.002 * (rng.NextDouble() - 0.5); Add(price, 0.001); }
        price *= 1 - 0.08 - 0.01 * rng.NextDouble();
        Add(price, 0.002);
        if (truncateAtLow) return bars;
        if (after == "bounce")
            for (int i = 0; i < 20; i++) { price *= 1 + 0.012 + 0.003 * rng.NextDouble(); Add(price, 0.002); }
        else
            for (int i = 0; i < 20; i++) { price *= 1 - 0.012 - 0.003 * rng.NextDouble(); Add(price, 0.002); }
        return bars;
    }

    private static List<IReadOnlyList<Candle>> History(string after, int sessions = 80) =>
        Enumerable.Range(1, sessions).Select(d => (IReadOnlyList<Candle>)Session(100 + d, after, dayOffset: -d)).ToList();

    private static (List<Candle> Today, DateTime Now) TodayAtTheLow(int seed = 7)
    {
        var today = Session(seed, "bounce", truncateAtLow: true);
        return (today, today[^1].EndUtc);
    }

    [Test]
    public void Arms_ALong_AtACapitulationLow_WhenHistoryShowsTheseLowsBounce()
    {
        var (today, now) = TodayAtTheLow();
        var d = ExhaustionEvaluator.Evaluate(History("bounce"), today, Open, now, Options);
        Assert.That(d.Side, Is.EqualTo(ExhaustionSide.Long), d.Reason);
        Assert.That(d.Arm, Is.True, d.Reason);
        Assert.That(d.Reversal!.Value.Samples, Is.GreaterThanOrEqualTo(Options.MinSamples));
        Assert.That(d.Reversal!.Value.Probability, Is.GreaterThanOrEqualTo(Options.MinReversalProbability));
        Assert.That(d.Parameters, Is.Not.Null);
        Assert.That(d.TriggerPrice, Is.EqualTo((double)today[^1].High));
    }

    [Test]
    public void Skips_TheSameLow_WhenHistoryShowsTheseLowsKeepFalling()
    {
        var (today, now) = TodayAtTheLow();
        var d = ExhaustionEvaluator.Evaluate(History("knife"), today, Open, now, Options);
        Assert.That(d.Side, Is.EqualTo(ExhaustionSide.Long), d.Reason);
        Assert.That(d.Arm, Is.False, d.Reason);
    }

    [Test]
    public void NeverArms_BeforeTheInformationGate()
    {
        var (today, _) = TodayAtTheLow();
        var d = ExhaustionEvaluator.Evaluate(History("bounce"), today, Open, Open.AddMinutes(10), Options);
        Assert.That(d.Arm, Is.False);
        Assert.That(d.Side, Is.EqualTo(ExhaustionSide.None));
        Assert.That(d.Reason, Does.StartWith("waiting for information"));
    }

    [Test]
    public void ThinHistory_FailsClosed()
    {
        var (today, now) = TodayAtTheLow();
        var d = ExhaustionEvaluator.Evaluate(History("bounce", sessions: 1), today, Open, now, Options);
        Assert.That(d.Arm, Is.False);
        Assert.That(d.Reason, Does.Contain("thin"));
    }

    [Test]
    public void ReadsOnlyClosedBars_AFutureBounceInTodaysFeedChangesNothing()
    {
        var (today, now) = TodayAtTheLow();
        var withFuture = Session(7, "knife");
        var a = ExhaustionEvaluator.Evaluate(History("bounce"), today, Open, now, Options);
        var b = ExhaustionEvaluator.Evaluate(History("bounce"), withFuture, Open, now, Options);
        Assert.That(b.Arm, Is.EqualTo(a.Arm));
        Assert.That(b.Reason, Is.EqualTo(a.Reason));
        Assert.That(b.ClosedBars, Is.EqualTo(a.ClosedBars));
    }

    [Test]
    public void QuietTape_IsNotAnExtreme()
    {
        var today = Session(9, "bounce").Take(30).ToList();
        var d = ExhaustionEvaluator.Evaluate(History("bounce"), today, Open, today[^1].EndUtc, Options);
        Assert.That(d.Side, Is.EqualTo(ExhaustionSide.None));
        Assert.That(d.Arm, Is.False);
    }
    /// <summary>40 quiet bars, a ~8% spike to a new high-of-day, a bar that gives it all back, then a fade or a squeeze.</summary>
    internal static List<Candle> SpikeSession(int seed, string after, int dayOffset = 0, bool truncateAtFailure = false)
    {
        var rng = new Random(seed);
        var start = Open.AddDays(dayOffset);
        var bars = new List<Candle>();
        double price = 3.0;
        void Add(double close)
        {
            double open = bars.Count == 0 ? close : (double)bars[^1].Close;
            int i = bars.Count;
            bars.Add(new Candle
            {
                Symbol = "PNY", StartUtc = start.AddMinutes(i), EndUtc = start.AddMinutes(i + 1), Open = (decimal)open, Close = (decimal)close,
                High = (decimal)(Math.Max(open, close) * 1.002), Low = (decimal)(Math.Min(open, close) * 0.998), Volume = 10_000 + rng.Next(5_000),
            });
        }
        for (int i = 0; i < 40; i++) { price *= 1.0004 + 0.002 * (rng.NextDouble() - 0.5); Add(price); }
        double before = price;
        price *= 1.08 + 0.01 * rng.NextDouble(); Add(price);
        price = before * (0.996 - 0.002 * rng.NextDouble()); Add(price);
        if (truncateAtFailure) return bars;
        for (int i = 0; i < 20; i++) { price *= after == "fade" ? 0.988 - 0.003 * rng.NextDouble() : 1.012 + 0.003 * rng.NextDouble(); Add(price); }
        return bars;
    }

    [Test]
    public void FadeShort_AtAFailedNewHigh_IsJudgedByTheLearnedBullTrapAndFadeRates()
    {
        var today = SpikeSession(7, "fade", truncateAtFailure: true);
        List<IReadOnlyList<Candle>> Hist(string after) =>
            Enumerable.Range(1, 80).Select(d => (IReadOnlyList<Candle>)SpikeSession(200 + d, after, -d)).ToList();

        var fade = ExhaustionEvaluator.Evaluate(Hist("fade"), today, Open, today[^1].EndUtc, Options);
        Assert.That(fade.Side, Is.EqualTo(ExhaustionSide.Short), fade.Reason);
        Assert.That(fade.Arm, Is.True, fade.Reason);

        var squeeze = ExhaustionEvaluator.Evaluate(Hist("squeeze"), today, Open, today[^1].EndUtc, Options);
        Assert.That(squeeze.Side, Is.EqualTo(ExhaustionSide.Short), squeeze.Reason);
        Assert.That(squeeze.Arm, Is.False, squeeze.Reason);
    }
}
