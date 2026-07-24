using IdiotProof.Models;
using IdiotProof.Monitor;

namespace IdiotProof.Monitor.Tests;

/// <summary>
/// Pins the pure surge/giveback math behind the premarket blow-off/fade
/// detector — no network/feed dependency, so these run against a synthetic
/// bar series. Thresholds: surge >=20% off the premarket low always flags;
/// an additional giveback >=10% off the peak (as a percent of the low->peak
/// run) escalates to "ESPECIALLY".
/// </summary>
public sealed class PremarketFadeScannerTests
{
    private static Candle Bar(DateTime t, decimal low, decimal high, decimal close) => new()
    {
        Symbol = "TEST", StartUtc = t, EndUtc = t.AddMinutes(1),
        Open = low, High = high, Low = low, Close = close, Volume = 1000,
    };

    [Test]
    public void Ohmh_ShapedMove_Surges_AndEscalates()
    {
        // low $0.40 -> peak $2.20 -> faded to $1.20: surge = (2.20-0.40)/0.40*100 = 450%;
        // giveback = (2.20-1.20)/(2.20-0.40)*100 = 55.6% -- both well past threshold.
        var start = new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        var bars = new List<Candle>
        {
            Bar(start,                 0.40m, 0.45m, 0.40m),
            Bar(start.AddMinutes(60),  1.50m, 2.20m, 2.00m),
            Bar(start.AddMinutes(120), 1.15m, 1.25m, 1.20m),
        };

        var result = PremarketFadeScanner.ComputeFade("OHMH", bars);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.SurgePercent, Is.EqualTo(450.0).Within(0.5));
            Assert.That(result.GivebackPercent, Is.EqualTo(55.6).Within(0.5));
            Assert.That(result.Escalated, Is.True);
        });
    }

    [Test]
    public void SurgeOnly_NoMeaningfulGiveback_FlagsButDoesNotEscalate()
    {
        // low $1.00 -> peak $1.30 (30% surge) -> still at $1.29 (barely off the peak).
        var start = new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        var bars = new List<Candle>
        {
            Bar(start,                1.00m, 1.05m, 1.00m),
            Bar(start.AddMinutes(30), 1.25m, 1.30m, 1.29m),
        };

        var result = PremarketFadeScanner.ComputeFade("XYZ", bars);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.SurgePercent, Is.GreaterThanOrEqualTo(20.0));
            Assert.That(result.Escalated, Is.False);
        });
    }

    [Test]
    public void BelowSurgeThreshold_DoesNotFlag()
    {
        // Only a 10% move off the low — under the 20% surge threshold.
        var start = new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        var bars = new List<Candle>
        {
            Bar(start,                1.00m, 1.02m, 1.00m),
            Bar(start.AddMinutes(30), 1.05m, 1.10m, 1.08m),
        };

        Assert.That(PremarketFadeScanner.ComputeFade("FLAT", bars), Is.Null);
    }

    [Test]
    public void TooFewBars_ReturnsNull()
    {
        var start = new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        var bars = new List<Candle> { Bar(start, 1.00m, 2.00m, 2.00m) };

        Assert.That(PremarketFadeScanner.ComputeFade("ONE", bars), Is.Null);
    }
}
