using IdiotProof.Indicators;
using IdiotProof.Models;

namespace IdiotProof.Indicators.Tests;

public class IndicatorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static List<Candle> MakeCandles(params decimal[] closes)
    {
        var start = new DateTime(2025, 1, 2, 9, 30, 0, DateTimeKind.Utc);
        return closes.Select((c, i) => new Candle
        {
            Symbol = "TEST",
            StartUtc = start.AddMinutes(i),
            EndUtc = start.AddMinutes(i + 1),
            Open = c,
            High = c + 0.10m,
            Low = c - 0.10m,
            Close = c,
            Volume = 10_000
        }).ToList();
    }

    // ── RSI ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void RSI_AllGains_Returns100()
    {
        var candles = MakeCandles(10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24);
        var rsi = RSI.Calculate(candles, 14);
        Assert.Equal(100m, rsi[^1]);
    }

    [Fact]
    public void RSI_AllLosses_Returns0()
    {
        var candles = MakeCandles(24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10);
        var rsi = RSI.Calculate(candles, 14);
        Assert.Equal(0m, rsi[^1]);
    }

    [Fact]
    public void RSI_MixedData_InRange()
    {
        var closes = new decimal[] { 100, 102, 101, 103, 102, 104, 103, 105, 104, 103, 102, 101, 103, 104, 105 };
        var candles = MakeCandles(closes);
        var rsi = RSI.Calculate(candles, 14);
        Assert.All(rsi, v => Assert.InRange(v, 0m, 100m));
    }

    [Fact]
    public void RSI_ReturnsSameLengthAsInput()
    {
        var candles = MakeCandles(100, 101, 102, 103, 104);
        var rsi = RSI.Calculate(candles, 14);
        Assert.Equal(candles.Count, rsi.Length);
    }

    [Fact]
    public void RSI_EmptyInput_ReturnsEmpty()
    {
        var rsi = RSI.Calculate([], 14);
        Assert.Empty(rsi);
    }

    // ── EMA ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void EMA_Period1_EqualsClose()
    {
        var candles = MakeCandles(100, 105, 110, 95, 103);
        var ema = EMA.Calculate(candles, 1);
        for (int i = 0; i < candles.Count; i++)
            Assert.Equal(candles[i].Close, ema[i]);
    }

    [Fact]
    public void EMA_IsSmoothedVsRawPrice()
    {
        // With EMA(5), a single spike should be dampened
        var candles = MakeCandles(100, 100, 100, 100, 100, 200, 100, 100, 100, 100);
        var ema = EMA.Calculate(candles, 5);
        // EMA at spike index should be between 100 and 200
        Assert.True(ema[5] > 100m && ema[5] < 200m, $"Expected dampened spike but got {ema[5]}");
    }

    [Fact]
    public void EMA_ReturnsSameLengthAsInput()
    {
        var candles = MakeCandles(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var ema = EMA.Calculate(candles, 3);
        Assert.Equal(candles.Count, ema.Length);
    }

    // ── ATR ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void ATR_FlatMarket_IsSmall()
    {
        var candles = MakeCandles(100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100);
        var atr = ATR.Calculate(candles, 14);
        // In a perfectly flat market candles have High=Close+0.10, Low=Close-0.10, so ATR ≈ 0.20
        Assert.All(atr, v => Assert.InRange(v, 0m, 0.50m));
    }

    [Fact]
    public void ATR_HighVolatility_IsLarger()
    {
        // Wild swings: alternating 100 and 200
        var candles = Enumerable.Range(0, 20).Select(i => new Candle
        {
            Symbol = "TEST",
            StartUtc = DateTime.UtcNow.AddMinutes(i),
            Open = 150,
            High = i % 2 == 0 ? 200 : 110,
            Low  = i % 2 == 0 ? 90  : 100,
            Close = i % 2 == 0 ? 200 : 100,
            Volume = 1000
        }).ToList();

        var atr = ATR.Calculate(candles, 14);
        Assert.True(atr[^1] > 50m, $"Expected high ATR but got {atr[^1]}");
    }

    // ── MACD ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void MACD_ReturnsSameLengthAsInput()
    {
        var candles = MakeCandles(
            100, 101, 102, 103, 104, 105, 106, 107, 108, 109,
            108, 107, 106, 105, 104, 103, 102, 101, 100, 99,
            100, 101, 102, 103, 104, 105, 106, 107, 108, 109);
        var result = MACD.Calculate(candles);
        Assert.Equal(candles.Count, result.Length);
    }

    [Fact]
    public void MACD_Histogram_IsMacdMinusSignal()
    {
        var candles = MakeCandles(
            100, 101, 102, 103, 104, 105, 106, 107, 108, 109,
            108, 107, 106, 105, 104, 103, 102, 101, 100, 99,
            100, 101, 102, 103, 104, 105, 106, 107, 108, 109);
        var result = MACD.Calculate(candles);
        foreach (var r in result)
            Assert.Equal(r.Macd - r.Signal, r.Histogram);
    }

    // ── VWAP ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void VWAP_EqualVolume_IsAverageTypicalPrice()
    {
        var candles = new List<Candle>
        {
            new() { Open = 100, High = 110, Low = 90,  Close = 100, Volume = 100, StartUtc = DateTime.UtcNow },
            new() { Open = 110, High = 120, Low = 100, Close = 110, Volume = 100, StartUtc = DateTime.UtcNow.AddMinutes(1) },
            new() { Open = 120, High = 130, Low = 110, Close = 120, Volume = 100, StartUtc = DateTime.UtcNow.AddMinutes(2) },
        };
        var vwap = VWAP.Calculate(candles);
        // Typical prices: (100+110+90)/3=100, (110+120+100)/3≈110, (120+130+110)/3≈120
        // Equal volume → VWAP = (100+110+120)/3 ≈ 110
        Assert.InRange(vwap[^1], 109m, 111m);
    }

    [Fact]
    public void VWAP_ReturnsSameLengthAsInput()
    {
        var candles = MakeCandles(100, 101, 102, 103, 104);
        var vwap = VWAP.Calculate(candles);
        Assert.Equal(candles.Count, vwap.Length);
    }

    [Fact]
    public void VWAP_ResetsAtDayBoundary()
    {
        // Day 1: high-priced candles; Day 2: low-priced candles.
        // After the reset on day 2, VWAP should reflect only day-2 prices.
        var day1 = new DateTime(2025, 4, 14, 14, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2025, 4, 15, 14, 0, 0, DateTimeKind.Utc);

        var candles = new List<Candle>
        {
            new() { High = 210, Low = 190, Close = 200, Volume = 100, StartUtc = day1 },
            new() { High = 210, Low = 190, Close = 200, Volume = 100, StartUtc = day1.AddMinutes(1) },
            // Day 2 resets the accumulator
            new() { High = 11, Low = 9, Close = 10, Volume = 100, StartUtc = day2 },
            new() { High = 11, Low = 9, Close = 10, Volume = 100, StartUtc = day2.AddMinutes(1) },
        };

        var vwap = VWAP.Calculate(candles);
        // After reset, day-2 VWAP should be ~10, not influenced by the 200-level day-1 candles
        Assert.InRange(vwap[^1], 9m, 11m);
    }
}
