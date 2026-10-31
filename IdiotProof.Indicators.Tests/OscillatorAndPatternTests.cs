using IdiotProof.Models;

namespace IdiotProof.Indicators.Tests;

/// <summary>
/// IP-T4: hand-computed values for the indicators and candlestick detectors that had no direct tests
/// (SMA, Bollinger Bands, Stochastic, Williams %R, CCI, OBV, Momentum/ROC, CandlestickPatterns),
/// including empty and shorter-than-period input, warm-up placeholders and flat-range fallbacks.
/// These feed strategy conditions, so a wrong value silently changes entries.
/// </summary>
[TestFixture]
public sealed class OscillatorAndPatternTests
{
    private static readonly DateTime T0 = new(2025, 1, 2, 14, 30, 0, DateTimeKind.Utc);

    private static Candle Bar(decimal open, decimal high, decimal low, decimal close, decimal volume = 1000, int i = 0) => new()
    {
        Symbol = "TEST", StartUtc = T0.AddMinutes(i), EndUtc = T0.AddMinutes(i + 1),
        Open = open, High = high, Low = low, Close = close, Volume = volume,
    };

    /// <summary>Bars whose open/high/low all equal the close, so typical price = close.</summary>
    private static List<Candle> Closes(params decimal[] closes) =>
        closes.Select((c, i) => Bar(c, c, c, c, 1000, i)).ToList();

    /// <summary>(high, low, close) bars for the range-based oscillators.</summary>
    private static List<Candle> Hlc(params (decimal H, decimal L, decimal C)[] bars) =>
        bars.Select((b, i) => Bar(b.C, b.H, b.L, b.C, 1000, i)).ToList();

    private static readonly List<Candle> Swing = Hlc((10, 8, 9), (11, 9, 10), (12, 10, 11), (13, 11, 11), (12, 10, 10));

    private static List<Candle> Flat(int n) => Closes(Enumerable.Repeat(5m, n).ToArray());

    // ── SMA ─────────────────────────────────────────────────────────────────────

    [Test]
    public void Sma_HandComputed_WithPartialWarmUp() =>
        Assert.That(SMA.Calculate(Closes(1, 2, 3, 4, 5), 3), Is.EqualTo(new[] { 1m, 1.5m, 2m, 3m, 4m }));

    [Test]
    public void Sma_EmptyAndShorterThanPeriod()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SMA.Calculate([], 5), Is.Empty);
            Assert.That(SMA.Calculate(Closes(2, 4), 5), Is.EqualTo(new[] { 2m, 3m }));
            Assert.That(() => SMA.Calculate(Closes(1), 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    // ── Bollinger Bands ─────────────────────────────────────────────────────────

    [Test]
    public void Bollinger_HandComputed()
    {
        // Mean 5, population variance 32/8 = 4, sigma 2: bands 9 / 1, last close 9 sits on the upper band.
        var r = BollingerBands.Calculate(Closes(2, 4, 4, 4, 5, 5, 7, 9), period: 8, multiplier: 2m);
        var last = r[^1];
        Assert.Multiple(() =>
        {
            Assert.That(last.Middle, Is.EqualTo(5m));
            Assert.That(last.Upper, Is.EqualTo(9m));
            Assert.That(last.Lower, Is.EqualTo(1m));
            Assert.That(last.PercentB, Is.EqualTo(1m));
            Assert.That(last.Bandwidth, Is.EqualTo(160m));
            Assert.That(r[6], Is.EqualTo(new BollingerResult(0, 0, 0, 0.5m, 0)), "warm-up placeholder");
        });
    }

    [Test]
    public void Bollinger_EmptyShortAndFlat()
    {
        var flat = BollingerBands.Calculate(Flat(5), period: 3)[^1];
        Assert.Multiple(() =>
        {
            Assert.That(BollingerBands.Calculate([], 20), Is.Empty);
            Assert.That(BollingerBands.Calculate(Closes(1, 2), 20).All(b => b.PercentB == 0.5m && b.Upper == 0m), Is.True);
            Assert.That(flat.Upper, Is.EqualTo(5m));
            Assert.That(flat.Lower, Is.EqualTo(5m));
            Assert.That(flat.PercentB, Is.EqualTo(0.5m), "zero band width falls back to mid");
            Assert.That(flat.Bandwidth, Is.EqualTo(0m));
        });
    }

    // ── Stochastic ──────────────────────────────────────────────────────────────

    [Test]
    public void Stochastic_HandComputed_WarmUpIs50_AndPercentDExcludesWarmUpBars()
    {
        var r = Stochastic.Calculate(Swing, kPeriod: 3, dPeriod: 3);
        Assert.Multiple(() =>
        {
            Assert.That(r[0], Is.EqualTo(new StochasticResult(50m, 50m)));
            Assert.That(r[1], Is.EqualTo(new StochasticResult(50m, 50m)));
            // Bar 2: HH 12, LL 8, close 11 -> 75; %D is only the one valid %K, not (50+50+75)/3.
            Assert.That(r[2].PercentK, Is.EqualTo(75m));
            Assert.That(r[2].PercentD, Is.EqualTo(75m));
            // Bar 3: HH 13, LL 9, close 11 -> 50; %D = (75+50)/2.
            Assert.That(r[3].PercentK, Is.EqualTo(50m));
            Assert.That(r[3].PercentD, Is.EqualTo(62.5m));
            // Bar 4: HH 13, LL 10, close 10 -> 0; %D = (75+50+0)/3.
            Assert.That(r[4].PercentK, Is.EqualTo(0m));
            Assert.That(r[4].PercentD, Is.EqualTo(125m / 3m));
        });
    }

    [Test]
    public void Stochastic_EmptyShortAndFlat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Stochastic.Calculate([]), Is.Empty);
            Assert.That(Stochastic.Calculate(Swing, kPeriod: 14).All(s => s.PercentK == 50m && s.PercentD == 50m), Is.True);
            Assert.That(Stochastic.Calculate(Flat(4), kPeriod: 3).All(s => s.PercentK == 50m && s.PercentD == 50m), Is.True,
                "zero high-low range falls back to 50");
        });
    }

    // ── Williams %R ─────────────────────────────────────────────────────────────

    [Test]
    public void WilliamsR_HandComputed_WarmUpIsMinus50() =>
        Assert.That(WilliamsR.Calculate(Swing, period: 3), Is.EqualTo(new[] { -50m, -50m, -25m, -50m, -100m }));

    [Test]
    public void WilliamsR_EmptyShortAndFlat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WilliamsR.Calculate([]), Is.Empty);
            Assert.That(WilliamsR.Calculate(Swing, period: 14), Is.All.EqualTo(-50m));
            Assert.That(WilliamsR.Calculate(Flat(4), period: 3), Is.All.EqualTo(-50m));
        });
    }

    // ── CCI ─────────────────────────────────────────────────────────────────────

    [Test]
    public void Cci_HandComputed()
    {
        // Typical prices 1,2,3: SMA 2, mean deviation 2/3, CCI = (3-2) / (0.015 * 2/3) = 100.
        var r = CCI.Calculate(Closes(1, 2, 3, 2), period: 3);
        Assert.Multiple(() =>
        {
            Assert.That(r[0], Is.Zero);
            Assert.That(r[1], Is.Zero);
            Assert.That(r[2], Is.EqualTo(100m).Within(1e-6m));
            // 2,3,2: SMA 7/3, mean dev 4/9, (2 - 7/3) / (0.015 * 4/9) = -50.
            Assert.That(r[3], Is.EqualTo(-50m).Within(1e-6m));
        });
    }

    [Test]
    public void Cci_EmptyShortAndFlat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CCI.Calculate([]), Is.Empty);
            Assert.That(CCI.Calculate(Closes(1, 2, 3), period: 20), Is.All.Zero);
            Assert.That(CCI.Calculate(Flat(5), period: 3), Is.All.Zero, "zero mean deviation falls back to 0");
        });
    }

    // ── OBV ─────────────────────────────────────────────────────────────────────

    [Test]
    public void Obv_AddsOnUp_SubtractsOnDown_HoldsOnUnchanged()
    {
        decimal[] closes = [10, 11, 11, 9, 12];
        decimal[] volumes = [100, 200, 300, 400, 500];
        var candles = closes.Select((c, i) => Bar(c, c, c, c, volumes[i], i)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(OBV.Calculate(candles), Is.EqualTo(new[] { 100m, 300m, 300m, -100m, 400m }));
            Assert.That(OBV.Calculate([]), Is.Empty);
        });
    }

    // ── Momentum / ROC ──────────────────────────────────────────────────────────

    [Test]
    public void Momentum_And_Roc_HandComputed()
    {
        var candles = Closes(10, 11, 13, 12);
        var roc = ROC.Calculate(candles, period: 2);
        Assert.Multiple(() =>
        {
            Assert.That(Momentum.Calculate(candles, period: 2), Is.EqualTo(new[] { 0m, 0m, 3m, 1m }));
            Assert.That(roc[2], Is.EqualTo(30m));
            Assert.That(roc[3], Is.EqualTo(100m / 11m).Within(1e-20m));
            Assert.That(roc.Take(2), Is.All.Zero);
        });
    }

    [Test]
    public void Momentum_EmptyShortAndZeroBase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Momentum.Calculate([]), Is.Empty);
            Assert.That(Momentum.Calculate(Closes(1, 2, 3), period: 10), Is.All.Zero);
            Assert.That(ROC.Calculate(Closes(0, 5), period: 1)[1], Is.Zero, "a zero base price falls back to 0");
        });
    }

    // ── Candlestick patterns ────────────────────────────────────────────────────

    private static readonly Candle RedPrior = Bar(open: 10m, high: 10.2m, low: 8.8m, close: 9m);
    private static readonly Candle GreenPrior = Bar(open: 9m, high: 10.2m, low: 8.8m, close: 10m);

    [Test]
    public void BullishEngulfing_TrueAndFalseCases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CandlestickPatterns.IsBullishEngulfing(RedPrior, Bar(8.9m, 10.6m, 8.8m, 10.5m)), Is.True);
            Assert.That(CandlestickPatterns.IsBullishEngulfing(RedPrior, Bar(9m, 10m, 9m, 10m)), Is.True, "exact body match counts");
            Assert.That(CandlestickPatterns.IsBullishEngulfing(GreenPrior, Bar(8.9m, 10.6m, 8.8m, 10.5m)), Is.False, "prior must be red");
            Assert.That(CandlestickPatterns.IsBullishEngulfing(RedPrior, Bar(10.5m, 10.6m, 8.8m, 8.9m)), Is.False, "current must be green");
            Assert.That(CandlestickPatterns.IsBullishEngulfing(RedPrior, Bar(9.5m, 10.6m, 9.4m, 10.5m)), Is.False, "opens above the prior close");
            Assert.That(CandlestickPatterns.IsBullishEngulfing(RedPrior, Bar(8.9m, 9.9m, 8.8m, 9.8m)), Is.False, "closes below the prior open");
            Assert.That(CandlestickPatterns.IsBullishEngulfing(Bar(9m, 9m, 9m, 9m), Bar(8m, 10m, 8m, 10m)), Is.False, "zero-body prior is not red");
        });
    }

    [Test]
    public void BearishEngulfing_TrueAndFalseCases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CandlestickPatterns.IsBearishEngulfing(GreenPrior, Bar(10.1m, 10.2m, 8.7m, 8.8m)), Is.True);
            Assert.That(CandlestickPatterns.IsBearishEngulfing(GreenPrior, Bar(10m, 10m, 9m, 9m)), Is.True, "exact body match counts");
            Assert.That(CandlestickPatterns.IsBearishEngulfing(RedPrior, Bar(10.1m, 10.2m, 8.7m, 8.8m)), Is.False, "prior must be green");
            Assert.That(CandlestickPatterns.IsBearishEngulfing(GreenPrior, Bar(8.8m, 10.2m, 8.7m, 10.1m)), Is.False, "current must be red");
            Assert.That(CandlestickPatterns.IsBearishEngulfing(GreenPrior, Bar(9.8m, 9.9m, 8.7m, 8.8m)), Is.False, "opens below the prior close");
            Assert.That(CandlestickPatterns.IsBearishEngulfing(GreenPrior, Bar(10.1m, 10.2m, 9.1m, 9.2m)), Is.False, "closes above the prior open");
        });
    }

    [Test]
    public void Hammer_TrueAndFalseCases()
    {
        Assert.Multiple(() =>
        {
            // Range 1.25, body 0.2, lower wick 1.0, upper wick 0.05.
            Assert.That(CandlestickPatterns.IsHammer(Bar(10m, 10.25m, 9m, 10.2m)), Is.True);
            Assert.That(CandlestickPatterns.IsHammer(Bar(10.2m, 10.25m, 9m, 10m)), Is.True, "red body works too");
            Assert.That(CandlestickPatterns.IsHammer(Bar(10m, 10m, 10m, 10m)), Is.False, "zero range");
            Assert.That(CandlestickPatterns.IsHammer(Bar(10m, 10.05m, 9m, 10m)), Is.False, "zero body");
            Assert.That(CandlestickPatterns.IsHammer(Bar(10m, 10.5m, 9m, 10.2m)), Is.False, "upper wick longer than body");
            Assert.That(CandlestickPatterns.IsHammer(Bar(10m, 10.25m, 9.8m, 10.2m)), Is.False, "lower wick under 2x body");
        });
    }

    [Test]
    public void ShootingStar_TrueAndFalseCases()
    {
        Assert.Multiple(() =>
        {
            // Range 1.25, body 0.2, upper wick 1.0, lower wick 0.05.
            Assert.That(CandlestickPatterns.IsShootingStar(Bar(10.2m, 11.2m, 9.95m, 10m)), Is.True);
            Assert.That(CandlestickPatterns.IsShootingStar(Bar(10m, 10m, 10m, 10m)), Is.False, "zero range");
            Assert.That(CandlestickPatterns.IsShootingStar(Bar(10m, 11m, 9.95m, 10m)), Is.False, "zero body");
            Assert.That(CandlestickPatterns.IsShootingStar(Bar(10.2m, 11.2m, 9.5m, 10m)), Is.False, "lower wick longer than body");
            Assert.That(CandlestickPatterns.IsShootingStar(Bar(10m, 10.25m, 9m, 10.2m)), Is.False, "a hammer is not a shooting star");
        });
    }

    [Test]
    public void Doji_TrueAndFalseCases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CandlestickPatterns.IsDoji(Bar(10m, 11m, 9m, 10m)), Is.True, "zero body");
            Assert.That(CandlestickPatterns.IsDoji(Bar(10m, 11m, 9m, 10.2m)), Is.True, "body exactly 10% of range");
            Assert.That(CandlestickPatterns.IsDoji(Bar(10m, 11m, 9m, 10.3m)), Is.False, "body 15% of range");
            Assert.That(CandlestickPatterns.IsDoji(Bar(10m, 10m, 10m, 10m)), Is.False, "zero range");
        });
    }
}
