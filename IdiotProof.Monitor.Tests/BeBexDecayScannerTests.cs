using IdiotProof.Models;
using IdiotProof.Monitor;

namespace IdiotProof.Monitor.Tests;

/// <summary>
/// Pins the pure BE-vs-BEX decay math — no feed dependency, so these run
/// against synthetic daily bars. BEX is modeled as a 2x daily-reset leveraged
/// long ETF on BE: decayPercent = (2 * BE's N-day return) - BEX's actual N-day
/// return. Positive decayPercent means BEX underperformed its 2x target.
/// </summary>
public sealed class BeBexDecayScannerTests
{
    private static Candle Bar(DateTime t, decimal close) => new()
    {
        Symbol = "TEST", StartUtc = t, EndUtc = t.AddDays(1),
        Open = close, High = close, Low = close, Close = close, Volume = 1000,
    };

    /// <summary>Only the FIRST and LAST bar's Close matter to the formula; the
    /// bars in between just need to exist so the lookback window has enough length.</summary>
    private static List<Candle> Series(DateTime start, int lookbackDays, decimal startClose, decimal endClose)
    {
        var bars = new List<Candle> { Bar(start, startClose) };
        for (var i = 1; i < lookbackDays; i++)
            bars.Add(Bar(start.AddDays(i), startClose)); // filler — unused by ComputeDecay
        bars.Add(Bar(start.AddDays(lookbackDays), endClose));
        return bars;
    }

    [Test]
    public void CleanTrend_MatchesHandComputedDecay()
    {
        // BE +10% over 5 days; BEX actual +15% vs. its 2x-target of +20% -> 5% decay.
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var be  = Series(start, 5, 100m, 110m);
        var bex = Series(start, 5, 50m, 57.5m);

        var result = BeBexDecayScanner.ComputeDecay(be, bex, 5);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.BeReturnPercent, Is.EqualTo(10.0).Within(0.001));
            Assert.That(result.ExpectedBexReturnPercent, Is.EqualTo(20.0).Within(0.001));
            Assert.That(result.ActualBexReturnPercent, Is.EqualTo(15.0).Within(0.001));
            Assert.That(result.DecayPercent, Is.EqualTo(5.0).Within(0.001));
        });
    }

    [Test]
    public void FlatUnderlying_StillShowsPureDecayDrag()
    {
        // BE flat (0% return) but BEX still drifts down -4% -- pure rebalancing/expense drag,
        // not "no signal" just because the underlying didn't move.
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var be  = Series(start, 5, 100m, 100m);
        var bex = Series(start, 5, 50m, 48m);

        var result = BeBexDecayScanner.ComputeDecay(be, bex, 5);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.BeReturnPercent, Is.EqualTo(0.0).Within(0.001));
            Assert.That(result.ExpectedBexReturnPercent, Is.EqualTo(0.0).Within(0.001));
            Assert.That(result.ActualBexReturnPercent, Is.EqualTo(-4.0).Within(0.001));
            Assert.That(result.DecayPercent, Is.EqualTo(4.0).Within(0.001));
        });
    }

    [Test]
    public void NegativeUnderlyingReturn_ExpectedBexReturnIsCorrectlyMoreNegative()
    {
        // BE -20% -> the 2x target is -40%, NOT "no signal" just because the sign flipped.
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var be  = Series(start, 5, 100m, 80m);
        var bex = Series(start, 5, 50m, 32m); // actual -36%

        var result = BeBexDecayScanner.ComputeDecay(be, bex, 5);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.BeReturnPercent, Is.EqualTo(-20.0).Within(0.001));
            Assert.That(result.ExpectedBexReturnPercent, Is.EqualTo(-40.0).Within(0.001));
            Assert.That(result.ActualBexReturnPercent, Is.EqualTo(-36.0).Within(0.001));
            Assert.That(result.DecayPercent, Is.EqualTo(-4.0).Within(0.001)); // BEX outperformed its (negative) target
        });
    }

    [Test]
    public void TooFewBeBars_ReturnsNull()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var be  = Series(start, 3, 100m, 110m); // only 4 bars, need 6 for lookbackDays=5
        var bex = Series(start, 5, 50m, 57.5m);

        Assert.That(BeBexDecayScanner.ComputeDecay(be, bex, 5), Is.Null);
    }

    [Test]
    public void TooFewBexBars_ReturnsNull()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var be  = Series(start, 5, 100m, 110m);
        var bex = Series(start, 3, 50m, 57.5m);

        Assert.That(BeBexDecayScanner.ComputeDecay(be, bex, 5), Is.Null);
    }

    [Test]
    public void NonPositiveLookback_ReturnsNull()
    {
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var be  = Series(start, 5, 100m, 110m);
        var bex = Series(start, 5, 50m, 57.5m);

        Assert.That(BeBexDecayScanner.ComputeDecay(be, bex, 0), Is.Null);
    }
}
