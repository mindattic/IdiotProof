using IdiotProof.Models;
using IdiotProof.Strategies;

namespace IdiotProof.NUnitTests;

/// <summary>
/// IndicatorSnapshotBuilder is the bridge from raw candles → IndicatorSnapshot.
/// These tests verify the snapshot is populated with the right values for the
/// most-used fields. We feed deterministic synthetic candles (linear price
/// ramp, constant volume) so expected values are computable by hand.
/// </summary>
[TestFixture]
public class IndicatorSnapshotBuilderTests
{
    private static IReadOnlyList<Candle> SyntheticRamp(int count, decimal startPrice = 100m, decimal step = 1m)
    {
        var list = new List<Candle>(count);
        var t = new DateTime(2026, 5, 1, 13, 30, 0, DateTimeKind.Utc);
        for (int i = 0; i < count; i++)
        {
            var p = startPrice + step * i;
            list.Add(new Candle
            {
                Symbol = "TEST",
                StartUtc = t.AddMinutes(5 * i),
                EndUtc   = t.AddMinutes(5 * i + 5),
                Open  = p,
                High  = p + 0.5m,
                Low   = p - 0.5m,
                Close = p,
                Volume = 1000m,
            });
        }
        return list;
    }

    [Test]
    public void Build_EmptyCandles_ReturnsBlankSnapshot()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", []);
        Assert.That(snap.Symbol, Is.EqualTo("TEST"));
        Assert.That(snap.Price, Is.EqualTo(0));
        Assert.That(snap.Emas, Is.Empty);
    }

    [Test]
    public void Build_RampCandles_PopulatesPriceAndEmas()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", SyntheticRamp(50));
        // Last bar's close on a ramp from 100 step 1 over 50 bars = 149
        Assert.That(snap.Price, Is.EqualTo(149).Within(0.01));
        Assert.That(snap.Emas.Keys, Does.Contain(9));
        Assert.That(snap.Emas.Keys, Does.Contain(21));
        Assert.That(snap.Emas.Keys, Does.Contain(31));
        Assert.That(snap.Emas.Keys, Does.Contain(50));
    }

    [Test]
    public void Build_PopulatesPriorPriceAndPriorEmas()
    {
        var candles = SyntheticRamp(30);
        var snap = IndicatorSnapshotBuilder.Build("TEST", candles);
        Assert.That(snap.PriorPrice, Is.EqualTo((double)candles[^2].Close).Within(0.01));
        Assert.That(snap.PriorEmas, Is.Not.Empty);
    }

    [Test]
    public void BuildWithEmas_AddsRequestedPeriods()
    {
        var snap = IndicatorSnapshotBuilder.BuildWithEmas("TEST", SyntheticRamp(80), [7, 65]);
        Assert.That(snap.Emas.Keys, Does.Contain(7));
        Assert.That(snap.Emas.Keys, Does.Contain(65));
        // Defaults still there
        Assert.That(snap.Emas.Keys, Does.Contain(9));
    }

    [Test]
    public void Build_WithEnoughCandles_ComputesAdxAndRsi()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", SyntheticRamp(60));
        Assert.That(snap.Adx, Is.Not.Null);
        Assert.That(snap.Rsi, Is.Not.Null);
        // Strict uptrend ramp → bullish DI / strong ADX / overbought-ish RSI
        Assert.That(snap.IsBullishTrend, Is.True);
    }

    [Test]
    public void Build_PopulatesSwingLevels()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", SyntheticRamp(30));
        Assert.That(snap.RecentSwingHigh, Is.Not.Null);
        Assert.That(snap.RecentSwingLow,  Is.Not.Null);
    }
}
