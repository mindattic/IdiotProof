using IdiotProof.Evidence.Microstructure;
using IdiotProof.Evidence.PointProcess;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class HawkesTests
{
    [Test]
    public void Fit_RecoversSimulatedParameters()
    {
        var truth = new HawkesParams(Mu: 0.5, Alpha: 0.8, Beta: 1.6);
        var events = HawkesProcess.Simulate(truth, horizon: 8000, seed: 42);
        var fit = HawkesProcess.Fit(events, 8000);

        Assert.That(fit.Params.Mu, Is.EqualTo(truth.Mu).Within(0.1));
        Assert.That(fit.Params.BranchingRatio, Is.EqualTo(0.5).Within(0.06));
        Assert.That(fit.Params.Beta, Is.EqualTo(truth.Beta).Within(0.4));
        Assert.That(fit.LikelihoodRatio, Is.GreaterThan(100), "a self-exciting series must beat Poisson decisively");
    }

    [Test]
    public void Fit_OnPoissonArrivals_FindsNoSelfExcitation()
    {
        var rng = new Random(5);
        var events = new List<double>();
        double t = 0;
        while ((t += -Math.Log(1 - rng.NextDouble()) / 2.0) < 4000) events.Add(t);

        var fit = HawkesProcess.Fit(events, 4000);
        Assert.That(fit.Params.BranchingRatio, Is.LessThan(0.08));
        Assert.That(fit.Params.UnconditionalRate, Is.EqualTo(2.0).Within(0.15));
        Assert.That(fit.LikelihoodRatio, Is.LessThan(10), "no meaningful gain over a constant rate");
    }

    [Test]
    public void LogLikelihood_MatchesBruteForceSum()
    {
        var times = new[] { 0.3, 0.9, 1.0, 2.5, 4.1 };
        var p = new HawkesParams(0.7, 0.4, 1.3);
        double brute = 0;
        foreach (var ti in times) brute += Math.Log(HawkesProcess.Intensity(times, ti, p));
        brute -= p.Mu * 5;
        foreach (var ti in times) brute -= p.Alpha / p.Beta * (1 - Math.Exp(-p.Beta * (5 - ti)));
        Assert.That(HawkesProcess.LogLikelihood(times, 5, p), Is.EqualTo(brute).Within(1e-10));
    }

    [Test]
    public void Stationarity_Guard()
    {
        Assert.That(new HawkesParams(1, 0.5, 1).IsStationary, Is.True);
        var explosive = new HawkesParams(1, 1.2, 1);
        Assert.That(explosive.IsStationary, Is.False);
        Assert.That(explosive.UnconditionalRate, Is.EqualTo(double.PositiveInfinity));

        var fit = HawkesProcess.Fit(HawkesProcess.Simulate(new HawkesParams(0.3, 0.9, 1.0), 3000, 9), 3000);
        Assert.That(fit.Params.IsStationary, Is.True, "the fit's parameterization can never leave the stationary region");
        Assert.That(fit.Params.BranchingRatio, Is.LessThan(1));
    }

    [Test]
    public void Intensity_IgnoresEventsAtOrAfterTheQueryTime()
    {
        var p = new HawkesParams(1, 0.5, 2);
        var past = new[] { 1.0, 2.0 };
        var withFuture = new[] { 1.0, 2.0, 3.0, 3.5 };
        Assert.That(HawkesProcess.Intensity(withFuture, 3.0, p), Is.EqualTo(HawkesProcess.Intensity(past, 3.0, p)));
    }

    [Test]
    public void Detector_FiresOnAPlantedBurst_AndNotOnSteadyFlow()
    {
        var open = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        var bars = Bars(open, 60);
        var trades = new List<TradePrint>();
        // Steady 1 trade every 2 s for the hour...
        for (int s = 0; s < 3600; s += 2) trades.Add(new TradePrint(open.AddSeconds(s), 2.0, 100));
        // ...plus a self-exciting burst from minute 40 to 45: clusters of 25 prints every 15 s.
        var rng = new Random(1);
        for (int s = 40 * 60; s < 45 * 60; s += 15)
            for (int k = 0; k < 25; k++) trades.Add(new TradePrint(open.AddSeconds(s + rng.NextDouble() * 2), 2.1, 100));

        var readings = new IgnitionDetector { WindowMinutes = 5 }.Read(bars, trades, baselineRate: 0.5);
        Assert.That(readings.Take(35).Any(r => r.Ignition), Is.False, "steady flow before the burst");
        Assert.That(readings.Skip(41).Take(5).Any(r => r.Ignition), Is.True, "the burst must register");
        Assert.That(readings[20].BranchingRatio, Is.LessThan(0.3));
    }

    [Test]
    public void Detector_HasNoLookahead_FutureTradesNeverChangeAPastReading()
    {
        var open = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        var bars = Bars(open, 30);
        var early = Enumerable.Range(0, 900).Select(s => new TradePrint(open.AddSeconds(s * 2), 2.0, 100)).ToList();
        var withLateBurst = early.Concat(Enumerable.Range(0, 500).Select(k => new TradePrint(open.AddMinutes(25).AddMilliseconds(k * 10), 2.2, 100))).ToList();

        var detector = new IgnitionDetector { WindowMinutes = 5 };
        var a = detector.Read(bars, early, 0.5);
        var b = detector.Read(bars, withLateBurst, 0.5);
        for (int i = 0; i < 25; i++) Assert.That(b[i], Is.EqualTo(a[i]), $"bar {i} closes before the burst");
        Assert.That(b[25].IntensityRatio, Is.GreaterThan(a[25].IntensityRatio));
    }

    [Test]
    public void Detector_WithoutABaseline_ReportsNoIntensityRatioAndNeverFires()
    {
        var open = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        var trades = Enumerable.Range(0, 2000).Select(k => new TradePrint(open.AddMilliseconds(k * 300), 2.0, 100)).ToList();
        var readings = new IgnitionDetector().Read(Bars(open, 10), trades, double.NaN);
        Assert.That(readings.All(r => double.IsNaN(r.IntensityRatio) && !r.Ignition), Is.True);
    }

    private static List<Candle> Bars(DateTime open, int minutes) =>
        Enumerable.Range(0, minutes).Select(m => new Candle
        {
            Symbol = "TEST", StartUtc = open.AddMinutes(m), EndUtc = open.AddMinutes(m + 1),
            Open = 2, High = 2, Low = 2, Close = 2, Volume = 1000,
        }).ToList();
}
