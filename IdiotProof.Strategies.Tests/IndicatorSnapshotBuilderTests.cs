using IdiotProof.Models;
using IdiotProof.Strategies;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Pins the exact bar-count warmup thresholds IndicatorSnapshotBuilder gates
/// RSI/MACD/ADX on. Each was previously off — RSI/MACD/ADX all served a
/// fabricated (non-null) value one or more bars before their underlying math
/// actually had enough history to converge, which a strategy's RequireAdxAbove/
/// IsRsiOversold/IsMacdBearish condition would read as real. Below the
/// threshold must be null (fail closed, IP-LAW-1); at/above must be non-null.
/// </summary>
public sealed class IndicatorSnapshotBuilderTests
{
    private static List<Candle> Bars(int count)
    {
        var bars = new List<Candle>(count);
        var start = new DateTime(2026, 1, 2, 14, 30, 0, DateTimeKind.Utc); // 9:30 ET
        decimal price = 100m;
        for (var i = 0; i < count; i++)
        {
            // Small alternating moves so RSI/MACD have real up AND down bars to smooth over.
            price += i % 2 == 0 ? 0.10m : -0.05m;
            var t = start.AddMinutes(i);
            bars.Add(new Candle
            {
                Symbol = "TEST", StartUtc = t, EndUtc = t.AddMinutes(1),
                Open = price, High = price + 0.05m, Low = price - 0.05m, Close = price, Volume = 1000,
            });
        }
        return bars;
    }

    [Test]
    public void Rsi_BelowWarmup_IsNull()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", Bars(14));
        Assert.That(snap.Rsi, Is.Null);
    }

    [Test]
    public void Rsi_AtWarmup_IsPopulated()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", Bars(15));
        Assert.That(snap.Rsi, Is.Not.Null);
    }

    [Test]
    public void Macd_BelowWarmup_IsNull()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", Bars(33));
        Assert.That(snap.MacdLine, Is.Null);
        Assert.That(snap.SignalLine, Is.Null);
    }

    [Test]
    public void Macd_AtWarmup_SignalIsNotJustEqualToMacd()
    {
        // The bug: at the old (too-low) threshold, Signal was forced exactly
        // equal to Macd (a 1-point "EMA"), zeroing the histogram every time.
        var snap = IndicatorSnapshotBuilder.Build("TEST", Bars(34));
        Assert.That(snap.MacdLine, Is.Not.Null);
        Assert.That(snap.SignalLine, Is.Not.Null);
        Assert.That(snap.MacdLine, Is.Not.EqualTo(snap.SignalLine),
            "Signal must be a real multi-point EMA of the MACD line, not a copy of the single seed point");
    }

    [Test]
    public void Adx_BelowWarmup_IsNull()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", Bars(28));
        Assert.That(snap.Adx, Is.Null);
    }

    [Test]
    public void Adx_AtWarmup_IsPopulated()
    {
        var snap = IndicatorSnapshotBuilder.Build("TEST", Bars(29));
        Assert.That(snap.Adx, Is.Not.Null);
    }
}
