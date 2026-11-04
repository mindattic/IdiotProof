using IdiotProof.Evidence.EventStudy;

namespace IdiotProof.Evidence.Tests;

public class EventStudyTests
{
    private static List<DateOnly> Calendar(int count)
    {
        var days = new List<DateOnly>();
        for (var d = new DateOnly(2025, 1, 6); days.Count < count; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days.Add(d);
        return days;
    }

    /// <summary>Market noise plus stocks following r = alpha + beta·m + noise, with optional planted jumps.</summary>
    private static (List<DateOnly> Cal, ReturnSeries Market, Dictionary<string, ReturnSeries> Stocks) World(
        int days, int symbols, int seed, Dictionary<(string, DateOnly), double>? jumps = null)
    {
        var cal = Calendar(days);
        var rng = new Random(seed);
        var m = new Dictionary<DateOnly, double>();
        foreach (var d in cal) m[d] = 0.01 * Synthetic.Gaussian(rng);
        var stocks = new Dictionary<string, ReturnSeries>();
        for (int s = 0; s < symbols; s++)
        {
            var sym = $"S{s}";
            var r = new Dictionary<DateOnly, double>();
            foreach (var d in cal)
            {
                double v = 0.0005 + 1.3 * m[d] + 0.02 * Synthetic.Gaussian(rng);
                if (jumps != null && jumps.TryGetValue((sym, d), out var j)) v += j;
                r[d] = v;
            }
            stocks[sym] = new ReturnSeries(r);
        }
        return (cal, new ReturnSeries(m), stocks);
    }

    [Test]
    public void MarketModel_RecoversPlantedAlphaAndBeta()
    {
        var rng = new Random(1);
        var m = Enumerable.Range(0, 2000).Select(_ => 0.01 * Synthetic.Gaussian(rng)).ToList();
        var s = m.Select(x => 0.001 + 1.5 * x + 0.005 * Synthetic.Gaussian(rng)).ToList();
        var model = MarketModel.Fit(s, m)!;
        Assert.That(model.Beta, Is.EqualTo(1.5).Within(0.05));
        Assert.That(model.Alpha, Is.EqualTo(0.001).Within(0.0005));
        Assert.That(Math.Sqrt(model.ResidualVariance), Is.EqualTo(0.005).Within(0.0005));
        Assert.That(model.PredictionVariance(0.05), Is.GreaterThan(model.PredictionVariance(model.MarketMean)));
    }

    [Test]
    public void PlantedEvent_ShowsUpAsItsAbnormalReturn()
    {
        var (cal, market, stocks) = World(300, 1, seed: 2, new() { [("S0", Calendar(300)[250])] = 0.25 });
        var engine = new EventStudyEngine(cal, market, stocks);
        var res = engine.Compute(new EventSpec("S0", cal[250], "test"), new EventWindow(0, 0));
        Assert.That(res.Valid, Is.True, res.Skipped);
        Assert.That(res.Car, Is.EqualTo(0.25).Within(0.06));
        Assert.That(res.Scar, Is.GreaterThan(5), "a 25% jump on 2% daily noise is many standard errors");
    }

    [Test]
    public void EventDateOnAWeekend_MapsToTheNextTradingDay()
    {
        var (cal, market, stocks) = World(300, 1, seed: 3);
        var engine = new EventStudyEngine(cal, market, stocks);
        var saturday = cal.First(d => d.DayOfWeek == DayOfWeek.Friday && cal.IndexOf(d) > 200).AddDays(1);
        var res = engine.Compute(new EventSpec("S0", saturday, "t"), new EventWindow(0, 1));
        Assert.That(res.Day0, Is.EqualTo(saturday.AddDays(2)));
    }

    [TestCase(-1, 1)]
    [TestCase(0, 1)]
    [TestCase(0, 5)]
    [TestCase(-5, 10)]
    public void EstimationWindow_NeverOverlapsTheEventWindow(int start, int end)
    {
        var (cal, market, stocks) = World(400, 1, seed: 4);
        var opts = new EventStudyOptions { EstimationLength = 120, Gap = 10 };
        var engine = new EventStudyEngine(cal, market, stocks, opts);
        var window = new EventWindow(start, end);
        for (int day0 = 150; day0 < 380; day0 += 17)
        {
            var (estStart, estEnd) = engine.EstimationRange(day0, window);
            Assert.That(estEnd - estStart, Is.EqualTo(120));
            Assert.That(estEnd - 1, Is.LessThan(day0 + window.Start), "last estimation day must precede the event window");
            Assert.That(day0 + window.Start - estEnd, Is.EqualTo(10), "gap is honored");
        }
    }

    [Test]
    public void ReturnInsideTheEventWindow_NeverMovesTheFittedModel()
    {
        var cal = Calendar(300);
        var (_, market, stocksA) = World(300, 1, seed: 5);
        var (_, _, stocksB) = World(300, 1, seed: 5, new() { [("S0", cal[249])] = 0.9 });
        var window = new EventWindow(-1, 1);
        var a = new EventStudyEngine(cal, market, stocksA).Compute(new EventSpec("S0", cal[250], "t"), window);
        var b = new EventStudyEngine(cal, market, stocksB).Compute(new EventSpec("S0", cal[250], "t"), window);
        // The day -1 jump lands in the CAR but not in α/β: CARs differ by exactly the jump.
        Assert.That(b.Car - a.Car, Is.EqualTo(0.9).Within(1e-9));
        Assert.That(b.EstimationObservations, Is.EqualTo(a.EstimationObservations));
    }

    [Test]
    public void PatellAndBmp_MatchHandComputedValues()
    {
        var (cal, market, stocks) = World(10, 1, seed: 6);
        var engine = new EventStudyEngine(cal, market, stocks);
        var ev = new EventSpec("S0", cal[0], "g", 1);
        var results = new[] { 1.0, 2.0, 3.0 }
            .Select(scar => new EventResult(ev, cal[0], scar / 10, scar, 104, null)).ToList();
        var g = engine.Aggregate("g", results, new EventWindow(0, 1), bootstrapDraws: 0);

        // Patell: ΣSCAR / sqrt(Σ (L-2)/(L-4)) = 6 / sqrt(3 × 102/100).
        Assert.That(g.PatellZ, Is.EqualTo(6 / Math.Sqrt(3 * 1.02)).Within(1e-12));
        // BMP: mean(SCAR) / (sd(SCAR)/sqrt(n)) = 2 / (1/sqrt(3)).
        Assert.That(g.BmpT, Is.EqualTo(2 * Math.Sqrt(3)).Within(1e-12));
        Assert.That(g.MeanCar, Is.EqualTo(0.2).Within(1e-12));
        Assert.That(g.MedianCar, Is.EqualTo(0.2).Within(1e-12));
        Assert.That(g.FractionPositive, Is.EqualTo(1));
        Assert.That(g.SignedEvents, Is.EqualTo(3));
        Assert.That(g.BmpP, Is.LessThan(0.2).And.GreaterThan(0.01), "t=3.46 with 2 df is p≈0.074");
    }

    [Test]
    public void RandomDates_OnNoise_AreNotSignificant()
    {
        var (cal, market, stocks) = World(500, 20, seed: 7);
        var engine = new EventStudyEngine(cal, market, stocks);
        var rng = new Random(70);
        var window = new EventWindow(0, 1);
        var results = Enumerable.Range(0, 20)
            .Select(s => engine.Compute(new EventSpec($"S{s}", cal[200 + rng.Next(250)], "noise"), window)).ToList();
        var g = engine.Aggregate("noise", results, window, bootstrapDraws: 300, seed: 1);
        Assert.That(g.Events, Is.EqualTo(20));
        Assert.That(g.BootstrapP, Is.GreaterThan(0.05), $"placebo p {g.BootstrapP}");
        Assert.That(g.BmpP, Is.GreaterThan(0.05), $"BMP p {g.BmpP}");
    }

    [Test]
    public void PlantedEvents_AreSignificant_OnEveryTest_AndSignedByTheirSentiment()
    {
        var cal = Calendar(500);
        var rng = new Random(80);
        var events = new List<EventSpec>();
        var jumps = new Dictionary<(string, DateOnly), double>();
        for (int s = 0; s < 20; s++)
        {
            var day = cal[200 + rng.Next(250)];
            int sign = s % 2 == 0 ? 1 : -1;
            jumps[($"S{s}", day)] = 0.06 * sign;
            events.Add(new EventSpec($"S{s}", day, "planted", sign));
        }
        var (_, market, stocks) = World(500, 20, seed: 8, jumps);
        var engine = new EventStudyEngine(cal, market, stocks);
        var window = new EventWindow(0, 1);
        var results = events.Select(e => engine.Compute(e, window)).ToList();
        var g = engine.Aggregate("planted", results, window, bootstrapDraws: 300, seed: 1);

        // Bullish and bearish jumps cancel in the raw mean, but the sentiment-signed CAR is strongly positive.
        Assert.That(g.MeanSignedCar, Is.EqualTo(0.06).Within(0.02));
        Assert.That(g.SignedBmpP, Is.LessThan(0.001));
    }

    [Test]
    public void OneSidedPlantedEvents_BeatThePlaceboBootstrap()
    {
        var cal = Calendar(500);
        var rng = new Random(90);
        var events = new List<EventSpec>();
        var jumps = new Dictionary<(string, DateOnly), double>();
        for (int s = 0; s < 20; s++)
        {
            var day = cal[200 + rng.Next(250)];
            jumps[($"S{s}", day)] = 0.06;
            events.Add(new EventSpec($"S{s}", day, "up", 1));
        }
        var (_, market, stocks) = World(500, 20, seed: 9, jumps);
        var engine = new EventStudyEngine(cal, market, stocks);
        var window = new EventWindow(0, 1);
        var g = engine.Aggregate("up", events.Select(e => engine.Compute(e, window)).ToList(), window, bootstrapDraws: 300, seed: 1);
        Assert.That(g.BootstrapP, Is.LessThan(0.01));
        Assert.That(g.PatellP, Is.LessThan(0.001));
        Assert.That(g.CrossSectionalP, Is.LessThan(0.001));
    }

    [Test]
    public void SplitSizedJump_InTheEstimationOrEventWindow_DropsTheEvent()
    {
        var cal = Calendar(300);
        var (_, market, reverseSplit) = World(300, 1, seed: 11, new() { [("S0", cal[180])] = 9.0 });
        var (_, _, forwardSplit) = World(300, 1, seed: 11, new() { [("S0", cal[251])] = -0.8 });
        var window = new EventWindow(0, 1);
        var a = new EventStudyEngine(cal, market, reverseSplit).Compute(new EventSpec("S0", cal[250], "t"), window);
        var b = new EventStudyEngine(cal, market, forwardSplit).Compute(new EventSpec("S0", cal[250], "t"), window);
        Assert.That(a.Skipped, Is.EqualTo("possible unadjusted split"));
        Assert.That(b.Skipped, Is.EqualTo("possible unadjusted split"));

        var lenient = new EventStudyEngine(cal, market, reverseSplit, new EventStudyOptions { MaxDailyMove = 20 })
            .Compute(new EventSpec("S0", cal[250], "t"), window);
        Assert.That(lenient.Valid, Is.True, "the threshold is a setting");
    }

    [Test]
    public void ThinHistory_IsSkippedWithAReason()
    {
        var (cal, market, stocks) = World(100, 1, seed: 10);
        var engine = new EventStudyEngine(cal, market, stocks);
        var res = engine.Compute(new EventSpec("S0", cal[50], "t"), new EventWindow(0, 1));
        Assert.That(res.Valid, Is.False);
        Assert.That(res.Skipped, Does.Contain("estimation"));
        var none = engine.Compute(new EventSpec("ZZZ", cal[90], "t"), new EventWindow(0, 1));
        Assert.That(none.Skipped, Is.EqualTo("no bars"));
    }

    [Test]
    public void ReturnSeries_FromCloses_OnlyBridgesConsecutiveTradingDays()
    {
        var cal = Calendar(4);
        var closes = new Dictionary<DateOnly, double> { [cal[0]] = 1.0, [cal[1]] = 1.1, [cal[3]] = 1.21 };
        var r = ReturnSeries.FromCloses(closes, cal);
        Assert.That(r.TryGet(cal[1], out var r1), Is.True);
        Assert.That(r1, Is.EqualTo(0.1).Within(1e-12));
        Assert.That(r.TryGet(cal[3], out _), Is.False, "day 2 is missing, so day 3's return spans two days and is dropped");
    }
}
