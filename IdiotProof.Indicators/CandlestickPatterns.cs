using IdiotProof.Models;

namespace IdiotProof.Indicators;

/// <summary>
/// Pure candlestick-pattern detectors. Each method takes a <see cref="Candle"/>
/// (and the prior candle for two-bar patterns) and returns true if the latest bar
/// matches the pattern. No state, no allocations — safe to call once per bar.
///
/// References:
///   - Engulfing / Hammer / Shooting Star / Doji defs follow Bulkowski's
///     "Encyclopedia of Candlestick Charts" with pragmatic body/shadow ratios.
///   - Detection is on the most recent CLOSED bar; intra-bar fires are caller's
///     responsibility (use bar timeframe boundaries).
/// </summary>
public static class CandlestickPatterns
{
    /// <summary>
    /// Bullish engulfing: prior bar bearish (close &lt; open) AND current bar
    /// bullish AND current body fully contains prior body.
    /// </summary>
    public static bool IsBullishEngulfing(Candle prior, Candle current) =>
        prior.Close < prior.Open
        && current.Close > current.Open
        && current.Open <= prior.Close
        && current.Close >= prior.Open;

    /// <summary>
    /// Bearish engulfing: prior bar bullish AND current bar bearish AND
    /// current body fully contains prior body.
    /// </summary>
    public static bool IsBearishEngulfing(Candle prior, Candle current) =>
        prior.Close > prior.Open
        && current.Close < current.Open
        && current.Open >= prior.Close
        && current.Close <= prior.Open;

    /// <summary>
    /// Hammer: small body in top third of range, lower shadow ≥ 2× body, tiny
    /// upper shadow. Single-bar reversal candidate at the bottom of a downtrend.
    /// </summary>
    public static bool IsHammer(Candle c)
    {
        var range = c.High - c.Low;
        if (range <= 0m) return false;
        var body  = Math.Abs(c.Close - c.Open);
        var lower = Math.Min(c.Open, c.Close) - c.Low;
        var upper = c.High - Math.Max(c.Open, c.Close);
        return body > 0m
            && lower >= 2m * body
            && upper <= body
            && body / range <= 0.35m;
    }

    /// <summary>
    /// Shooting star: small body in bottom third of range, upper shadow ≥ 2×
    /// body, tiny lower shadow. Single-bar reversal candidate at the top of an
    /// uptrend.
    /// </summary>
    public static bool IsShootingStar(Candle c)
    {
        var range = c.High - c.Low;
        if (range <= 0m) return false;
        var body  = Math.Abs(c.Close - c.Open);
        var upper = c.High - Math.Max(c.Open, c.Close);
        var lower = Math.Min(c.Open, c.Close) - c.Low;
        return body > 0m
            && upper >= 2m * body
            && lower <= body
            && body / range <= 0.35m;
    }

    /// <summary>
    /// Doji: open ≈ close (body ≤ 10% of range). Indecision; usually waits for
    /// the next bar to confirm direction.
    /// </summary>
    public static bool IsDoji(Candle c)
    {
        var range = c.High - c.Low;
        if (range <= 0m) return false;
        var body = Math.Abs(c.Close - c.Open);
        return body / range <= 0.10m;
    }
}
