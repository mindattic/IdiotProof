using IdiotProof.Evidence.Adaptation;
using IdiotProof.Evidence.Liquidity;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class StopPoolTests
{
    // 2026-11-02 is EST: 9:30 ET = 14:30 UTC, 4:00 ET = 09:00 UTC.
    private static readonly DateTime Day1Open = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static Candle Bar(DateTime startUtc, double high, double low, double close, double volume = 1000) => new()
    {
        Symbol = "TEST", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1),
        Open = (decimal)close, High = (decimal)Math.Max(high, close), Low = (decimal)Math.Min(low, close),
        Close = (decimal)close, Volume = (decimal)volume,
    };

    private static List<Candle> Rth(DateTime openUtc, int minutes, Func<int, (double H, double L, double C)> at) =>
        Enumerable.Range(0, minutes).Select(i => { var (h, l, c) = at(i); return Bar(openUtc.AddMinutes(i), h, l, c); }).ToList();

    [Test]
    public void OpeningRange_AppearsOnlyOnceItsLastMinuteHasClosed()
    {
        var bars = Rth(Day1Open, 30, i => (2.00 + i * 0.01 + 0.005, 2.00 + i * 0.01 - 0.005, 2.00 + i * 0.01));
        var s = new StopPoolSeries(bars, new StopPoolOptions { OpeningRangeMinutes = 15 });
        Assert.That(s.PoolsAt(13).Any(p => p.Kind == PoolKind.OpeningRangeHigh), Is.False, "9:43 bar closes 9:44: range not formed");
        var orHigh = s.PoolsAt(14).Single(p => p.Kind == PoolKind.OpeningRangeHigh);
        Assert.That(orHigh.Price, Is.EqualTo(2.145).Within(1e-9), "the range is bars 9:30..9:44 only");

        var altered = bars.Select((c, i) => i == 20 ? Bar(c.StartUtc, 9, 0.5, 5) : c).ToList();
        var s2 = new StopPoolSeries(altered, new StopPoolOptions { OpeningRangeMinutes = 15 });
        Assert.That(s2.PoolsAt(14), Is.EqualTo(s.PoolsAt(14)), "a later bar never changes an earlier map");
    }

    [Test]
    public void Premarket_And_PriorDay_Levels_ExistOnlyOnceFormed()
    {
        var pm = Enumerable.Range(0, 30).Select(i => Bar(new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc).AddMinutes(i), 2.50, 2.40, 2.45)).ToList();
        var day1 = Rth(Day1Open, 60, i => (2.30, 2.20, 2.25));
        var day2 = Rth(Day1Open.AddDays(1), 10, i => (2.28, 2.22, 2.26));
        var s = new StopPoolSeries(pm.Concat(day1).Concat(day2).ToList());

        Assert.That(s.PoolsAt(29).Any(p => p.Kind == PoolKind.PremarketHigh), Is.False, "premarket is still forming before 9:30");
        Assert.That(s.PoolsAt(30).Single(p => p.Kind == PoolKind.PremarketHigh).Price, Is.EqualTo(2.50));
        Assert.That(s.PoolsAt(89).Any(p => p.Kind == PoolKind.PriorDayHigh), Is.False, "no prior day on day one");
        var prior = s.PoolsAt(90).Single(p => p.Kind == PoolKind.PriorDayHigh);
        Assert.That(prior.Price, Is.EqualTo(2.30), "prior-day level comes from the regular session, not the premarket");
    }

    [Test]
    public void RoundNumbers_FollowThePriceTier()
    {
        Assert.That(StopPoolSeries.RoundIncrement(0.80), Is.EqualTo(0.05));
        Assert.That(StopPoolSeries.RoundIncrement(2.95), Is.EqualTo(0.10));
        Assert.That(StopPoolSeries.RoundIncrement(12.0), Is.EqualTo(0.25));
        var s = new StopPoolSeries(Rth(Day1Open, 1, _ => (2.96, 2.94, 2.95)));
        var rounds = s.PoolsAt(0).Where(p => p.Kind == PoolKind.RoundNumber).Select(p => p.Price).OrderBy(x => x).ToList();
        Assert.That(rounds, Is.EqualTo(new[] { 2.90, 3.00 }).Within(1e-9));
    }

    /// <summary>Flat at 2.90, then one bar to <paramref name="spikeHigh"/>, then 12 bars at <paramref name="after"/>.</summary>
    private static List<Candle> Episode(DateTime start, double spikeHigh, double spikeClose, double after)
    {
        var bars = new List<Candle>();
        for (int i = 0; i < 12; i++) bars.Add(Bar(start.AddMinutes(bars.Count), 2.905, 2.895, 2.90));
        bars.Add(Bar(start.AddMinutes(bars.Count), spikeHigh, 2.90, spikeClose));
        for (int i = 0; i < 12; i++) bars.Add(Bar(start.AddMinutes(bars.Count), after + 0.005, after - 0.005, after));
        return bars;
    }

    private static PoolTouch RoundTouch(IReadOnlyList<Candle> bars) =>
        PoolRaidDetector.Touches(new StopPoolSeries(bars))
            .Single(t => t.Pool.Kind == PoolKind.RoundNumber && Math.Abs(t.Pool.Price - 3.00) < 1e-9 && t.Key.Side == PoolSide.Above);

    [Test]
    public void Classifies_RaidAndReverse_BreakThrough_And_Reject()
    {
        var raid = RoundTouch(Episode(Day1Open, 3.03, 2.97, 2.92));
        Assert.That(raid.Outcome, Is.EqualTo(PoolOutcome.RaidAndReverse));
        Assert.That(raid.PierceDepth, Is.EqualTo(0.01).Within(1e-9));
        Assert.That(raid.Index, Is.EqualTo(12));
        Assert.That(raid.ResolvedAt, Is.EqualTo(22));

        Assert.That(RoundTouch(Episode(Day1Open, 3.08, 3.06, 3.06)).Outcome, Is.EqualTo(PoolOutcome.BreakThrough));
        Assert.That(RoundTouch(Episode(Day1Open, 2.999, 2.98, 2.95)).Outcome, Is.EqualTo(PoolOutcome.Reject));
    }

    [Test]
    public void LearnedRates_RecoverPlantedBaseRates()
    {
        var rng = new Random(404);
        var bars = new List<Candle>();
        int raids = 0, total = 300;
        for (int e = 0; e < total; e++)
        {
            bool reverse = rng.NextDouble() < 0.7;
            if (reverse) raids++;
            var start = Day1Open.AddMinutes(bars.Count);
            bars.AddRange(reverse ? Episode(start, 3.03, 2.97, 2.92) : Episode(start, 3.08, 3.06, 3.10));
        }
        var touches = PoolRaidDetector.Touches(new StopPoolSeries(bars))
            .Where(t => t.Pool.Kind == PoolKind.RoundNumber && Math.Abs(t.Pool.Price - 3.00) < 1e-9);
        var table = PoolRaidTable.Learn(touches);
        var key = new PoolKey(PoolKind.RoundNumber, PoolSide.Above, 1);
        var rate = table.ReverseRate(key);

        Assert.That(rate.Samples, Is.EqualTo(total), "every planted pierce is counted once");
        Assert.That(rate.Successes, Is.EqualTo(raids));
        Assert.That(rate.Probability, Is.EqualTo(0.7).Within(0.07));
        Assert.That(table.MedianRaidDepth(key), Is.EqualTo(0.01).Within(1e-9));
    }

    [Test]
    public void Learn_HonorsTheResolutionCutoff()
    {
        var bars = Episode(Day1Open, 3.03, 2.97, 2.92);
        var touches = PoolRaidDetector.Touches(new StopPoolSeries(bars));
        var none = PoolRaidTable.Learn(touches, t => t.ResolvedAt < 12);
        Assert.That(none.Approaches, Is.EqualTo(0), "an approach not yet resolved is never learned");
    }

    [Test]
    public void Assessor_FindsAPoolOverheadAndARecentRaidBelow()
    {
        var table = new PoolRaidTable();
        var overhead = Rth(Day1Open, 20, i => (2.985, 2.975, 2.98));
        var reading = PoolAssessor.IntoPool(new StopPoolSeries(overhead), 19, table, new PoolRaidOptions { ProximityAtr = 3 });
        Assert.That(reading.Present, Is.True);
        Assert.That(reading.Pool!.Value.Price, Is.GreaterThan(2.98).And.LessThanOrEqualTo(3.00), "the nearest pool overhead");

        var far = PoolAssessor.IntoPool(new StopPoolSeries(Rth(Day1Open, 10, i => (2.955, 2.945, 2.95))), 9, table, new PoolRaidOptions { ProximityAtr = 1 });
        Assert.That(far.Present, Is.False, $"no pool within reach overhead before the opening range forms, got {far.Pool}");

        var raidBars = Rth(Day1Open, 20, i => i < 15 ? (2.935, 2.925, 2.93) : i == 15 ? (2.93, 2.88, 2.91) : (2.935, 2.925, 2.93));
        var raided = PoolAssessor.RecentRaid(new StopPoolSeries(raidBars), 19, table);
        Assert.That(raided.Present, Is.True);
        Assert.That(raided.Pool!.Value.Price, Is.EqualTo(2.90).Within(1e-9));
    }

    private static PoolRaidTable PlantedBelowTable(int reversed, int broke, double depth)
    {
        var table = new PoolRaidTable();
        var key = new PoolKey(PoolKind.RoundNumber, PoolSide.Below, 1);
        var pool = new StopPool(PoolKind.RoundNumber, 3.00);
        for (int i = 0; i < reversed; i++) table.Add(new PoolTouch(i, i + 10, pool, key, PoolOutcome.RaidAndReverse, depth));
        for (int i = 0; i < broke; i++) table.Add(new PoolTouch(i, i + 10, pool, key, PoolOutcome.BreakThrough, 0.02));
        return table;
    }

    [Test]
    public void Deriver_WidensAStopRestingInsideARaidedPool_OnlyWhenAsked()
    {
        var p = new DerivedParameters(1, StopLossPercent: 2.0, TrailingStopPercent: 1, TakeProfitPercent: 2, 100, 10, 0.8,
            RegimeStats.Read(new double[] { 1, 2, 3 }));
        double entry = 3.05; // stop at 2.989, inside the 1% raid zone below 3.00
        var nudge = new StopPoolNudge([new StopPool(PoolKind.RoundNumber, 3.00)], PlantedBelowTable(40, 10, 0.01), Tier: 1);

        var widened = AdaptiveParameterDeriver.NudgePastRaidedPools(p, entry, nudge);
        double expected = (entry - 3.00 * (1 - 0.01 - 0.001)) / entry * 100;
        Assert.That(widened.StopLossPercent, Is.EqualTo(Math.Round(expected, 2)));
        Assert.That(widened.StopNudge, Does.StartWith("widened past RoundNumber"));

        var rarelyRaided = nudge with { Table = PlantedBelowTable(10, 40, 0.01) };
        Assert.That(AdaptiveParameterDeriver.NudgePastRaidedPools(p, entry, rarelyRaided), Is.EqualTo(p), "raids that rarely reverse don't move the stop");

        var thin = nudge with { Table = PlantedBelowTable(5, 1, 0.01) };
        Assert.That(AdaptiveParameterDeriver.NudgePastRaidedPools(p, entry, thin), Is.EqualTo(p), "too few pierces to trust");

        var closed = Rth(Day1Open, 40, i => (3.06, 3.04, 3.05));
        Assert.That(AdaptiveParameterDeriver.Derive(closed).StopNudge, Is.Null, "no nudge unless one is supplied");
    }

    [Test]
    public void Deriver_NeverWidensBeyondTheCap()
    {
        var p = new DerivedParameters(1, StopLossPercent: 0.3, TrailingStopPercent: 1, TakeProfitPercent: 2, 100, 10, 0.8,
            RegimeStats.Read(new double[] { 1, 2, 3 }));
        var nudge = new StopPoolNudge([new StopPool(PoolKind.RoundNumber, 3.00)], PlantedBelowTable(40, 10, 0.05), Tier: 1);
        var r = AdaptiveParameterDeriver.NudgePastRaidedPools(p, 3.005, nudge);
        Assert.That(r.StopLossPercent, Is.EqualTo(0.3));
        Assert.That(r.StopNudge, Does.StartWith("not widened"));
    }
}
