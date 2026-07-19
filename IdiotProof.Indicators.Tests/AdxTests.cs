using IdiotProof.Models;

namespace IdiotProof.Indicators.Tests;

/// <summary>
/// ADX / Wilder-smoothing coverage (none existed before). The reference-math
/// test pins the WilderSmooth seed fix: the seed average must span only REAL
/// values (TR/DM start at bar 1 — bar 0 has no prior bar), not the phantom
/// zero at index 0 that used to dilute it.
/// </summary>
public class AdxTests
{
    private static Candle Bar(int i, decimal close, decimal range) => new()
    {
        Symbol = "TEST",
        StartUtc = new DateTime(2026, 7, 17, 4, 0, 0, DateTimeKind.Utc).AddMinutes(i),
        EndUtc = new DateTime(2026, 7, 17, 4, 1, 0, DateTimeKind.Utc).AddMinutes(i),
        Open = close,
        High = close + range / 2,
        Low = close - range / 2,
        Close = close,
        Volume = 1000,
    };

    /// <summary>Steadily rising bars — a clean uptrend.</summary>
    private static List<Candle> Uptrend(int count, decimal step = 1m, decimal range = 1m)
    {
        var list = new List<Candle>();
        for (int i = 0; i < count; i++) list.Add(Bar(i, 100m + i * step, range));
        return list;
    }

    [Test]
    public void Adx_Uptrend_PlusDiDominates_AndAdxIsHigh()
    {
        var results = ADX.Calculate(Uptrend(40));
        var last = results[^1];
        Assert.Multiple(() =>
        {
            Assert.That(last.PlusDI, Is.GreaterThan(last.MinusDI), "uptrend → +DI above -DI");
            Assert.That(last.ADX, Is.GreaterThan(50m), "clean one-way trend → strong ADX");
            Assert.That(last.ADX, Is.LessThanOrEqualTo(100m));
        });
    }

    [Test]
    public void Adx_TooFewBars_ReturnsZeroedResultsWithoutThrowing()
    {
        Assert.That(ADX.Calculate(Uptrend(1)), Has.Length.EqualTo(1));
        Assert.That(ADX.Calculate(new List<Candle>()), Is.Empty);
    }

    [Test]
    public void WilderSeed_AveragesOnlyRealValues_NotThePhantomFirstBar()
    {
        // Non-uniform series so seed dilution does NOT cancel in the DI ratio:
        // 5 wide-range thrust bars then mild drift. Expected +DI is computed
        // with reference Wilder math seeded over indexes 1..period (real
        // values only); the old phantom-zero seed produces a different number.
        const int period = 14;
        var candles = new List<Candle> { Bar(0, 100m, 1m) };
        for (int i = 1; i <= 5; i++) candles.Add(Bar(i, candles[^1].Close + 2m, 4m));   // thrust
        for (int i = 6; i < 40; i++) candles.Add(Bar(i, candles[^1].Close + 0.2m, 1m)); // drift

        var n = candles.Count;
        var tr = new decimal[n];
        var dmPlus = new decimal[n];
        for (int i = 1; i < n; i++)
        {
            var c = candles[i];
            var p = candles[i - 1];
            tr[i] = Math.Max(Math.Max(c.High - c.Low, Math.Abs(c.High - p.Close)), Math.Abs(c.Low - p.Close));
            var up = c.High - p.High;
            var down = p.Low - c.Low;
            dmPlus[i] = up > down && up > 0 ? up : 0m;
        }

        // Reference Wilder smoothing: seed = average of values[1..period].
        static decimal SmoothLast(decimal[] values, int period)
        {
            decimal sum = 0m;
            for (int i = 1; i <= period; i++) sum += values[i];
            var s = sum / period;
            for (int i = period + 1; i < values.Length; i++)
                s = (s * (period - 1) + values[i]) / period;
            return s;
        }

        var expectedPlusDi = 100m * SmoothLast(dmPlus, period) / SmoothLast(tr, period);
        var actual = ADX.Calculate(candles, period)[^1];

        Assert.That(actual.PlusDI, Is.EqualTo(expectedPlusDi).Within(0.0001m),
            "seed must average the first 14 REAL values (indexes 1..14), not include the phantom zero at index 0");
    }
}
