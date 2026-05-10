using IdiotProof.Indicators;
using IdiotProof.Models;

namespace IdiotProof.NUnitTests;

/// <summary>
/// Validates each candlestick pattern detector against canonical example bars.
/// Bars are built with deterministic OHLC values that satisfy the body/shadow
/// ratios the detectors check; non-pattern controls assert false-positives are
/// rejected.
/// </summary>
[TestFixture]
public class CandlestickPatternsTests
{
    private static Candle Bar(decimal open, decimal high, decimal low, decimal close) => new()
    {
        Symbol   = "TEST",
        StartUtc = DateTime.UtcNow,
        EndUtc   = DateTime.UtcNow.AddMinutes(5),
        Open     = open,
        High     = high,
        Low      = low,
        Close    = close,
        Volume   = 1000m,
    };

    // ── Engulfing ────────────────────────────────────────────────────────

    [Test]
    public void BullishEngulfing_PriorRedCurrentGreenContainingBody_ReturnsTrue()
    {
        var prior   = Bar(open: 100, high: 101, low: 95, close: 96);   // bearish body 96-100
        var current = Bar(open: 95,  high: 105, low: 94, close: 101);  // bullish body 95-101 contains 96-100
        Assert.That(CandlestickPatterns.IsBullishEngulfing(prior, current), Is.True);
    }

    [Test]
    public void BullishEngulfing_PriorWasBullish_ReturnsFalse()
    {
        var prior   = Bar(open: 95, high: 101, low: 94, close: 100);   // bullish — pre-condition fails
        var current = Bar(open: 95, high: 105, low: 94, close: 101);
        Assert.That(CandlestickPatterns.IsBullishEngulfing(prior, current), Is.False);
    }

    [Test]
    public void BearishEngulfing_PriorGreenCurrentRedContainingBody_ReturnsTrue()
    {
        var prior   = Bar(open: 95,  high: 101, low: 94, close: 100);  // bullish body 95-100
        var current = Bar(open: 101, high: 102, low: 90, close: 94);   // bearish body 94-101 contains 95-100
        Assert.That(CandlestickPatterns.IsBearishEngulfing(prior, current), Is.True);
    }

    // ── Hammer ───────────────────────────────────────────────────────────

    [Test]
    public void Hammer_LongLowerShadowSmallBodyTopThird_ReturnsTrue()
    {
        // Range = 100 - 80 = 20, body = 100 - 98 = 2, lower shadow = 98 - 80 = 18
        // Body/range = 0.10 (≤ 0.35), lower ≥ 2x body, upper minimal.
        var c = Bar(open: 98, high: 100, low: 80, close: 100);
        Assert.That(CandlestickPatterns.IsHammer(c), Is.True);
    }

    [Test]
    public void Hammer_LargeBody_ReturnsFalse()
    {
        // Body too large — body/range = 0.50.
        var c = Bar(open: 92, high: 102, low: 90, close: 98);
        Assert.That(CandlestickPatterns.IsHammer(c), Is.False);
    }

    // ── Shooting Star ─────────────────────────────────────────────────────

    [Test]
    public void ShootingStar_LongUpperShadowSmallBodyBottomThird_ReturnsTrue()
    {
        // Range = 120 - 100 = 20, body = 102 - 100 = 2, upper shadow = 120 - 102 = 18
        var c = Bar(open: 100, high: 120, low: 100, close: 102);
        Assert.That(CandlestickPatterns.IsShootingStar(c), Is.True);
    }

    // ── Doji ──────────────────────────────────────────────────────────────

    [Test]
    public void Doji_OpenEqualsClose_ReturnsTrue()
    {
        var c = Bar(open: 100, high: 105, low: 95, close: 100);
        Assert.That(CandlestickPatterns.IsDoji(c), Is.True);
    }

    [Test]
    public void Doji_LargeBody_ReturnsFalse()
    {
        // Body 5, range 10 → 0.50, fails ≤ 0.10 cutoff.
        var c = Bar(open: 100, high: 105, low: 95, close: 105);
        Assert.That(CandlestickPatterns.IsDoji(c), Is.False);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Test]
    public void AllPatterns_ZeroRangeBar_ReturnFalse()
    {
        // Identical OHLC — no range. All detectors should reject (no division by zero).
        var c = Bar(open: 100, high: 100, low: 100, close: 100);
        Assert.That(CandlestickPatterns.IsHammer(c), Is.False);
        Assert.That(CandlestickPatterns.IsShootingStar(c), Is.False);
        Assert.That(CandlestickPatterns.IsDoji(c), Is.False);
    }
}
