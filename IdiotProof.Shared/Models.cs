// ============================================================================
// IdiotProof.Shared - Common Models
// ============================================================================
// Shared between Core, Web, and Scripting projects. Trade-related enums and
// the canonical TradeSetup/RiskLimits live in IdiotProof.Models — this file
// re-exports them by `using` so Shared callers don't have to switch namespaces.
// ============================================================================

using IdiotProof.Models;

namespace IdiotProof.Shared;

/// <summary>
/// Market indicator values at a point in time.
/// </summary>
public sealed class IndicatorSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Symbol { get; set; } = "";
    public double Price { get; set; }

    // VWAP
    public double? Vwap { get; set; }
    public double? VwapDistance => Vwap.HasValue && Vwap.Value > 0
        ? ((Price - Vwap.Value) / Vwap.Value) * 100 : null;

    // EMAs — fixed-period helpers + arbitrary-period dictionary for DSL verbs
    // like IsBetweenEma(7, 65) or RequireEmaStack(13, 89).
    public double? Ema9 { get; set; }
    public double? Ema21 { get; set; }
    public double? Ema50 { get; set; }
    public double? Ema200 { get; set; }
    public Dictionary<int, double> Emas { get; set; } = new();
    public Dictionary<int, double> PriorEmas { get; set; } = new();

    /// <summary>
    /// Resolve an EMA for any period. Falls back through the dedicated 9/21/50/200
    /// fields when not present in <see cref="Emas"/>.
    /// </summary>
    public double? GetEma(int period) =>
        Emas.TryGetValue(period, out var v) ? v
        : period switch
        {
            9   => Ema9,
            21  => Ema21,
            50  => Ema50,
            200 => Ema200,
            _   => null
        };

    public double? GetPriorEma(int period) =>
        PriorEmas.TryGetValue(period, out var v) ? v : null;

    /// <summary>Prior bar's close (used by reclaim / cross-up / cross-down conditions).</summary>
    public double? PriorPrice { get; set; }

    /// <summary>
    /// Previous trading day's official close. Required by gap conditions
    /// (IsGapUp / IsGapDown); null when the caller didn't supply daily data,
    /// in which case gap conditions fail closed rather than pass.
    /// </summary>
    public double? PreviousClose { get; set; }

    /// <summary>Gap vs the previous day's close, in percent. Null without PreviousClose.</summary>
    public double? GapPercent => PreviousClose is > 0
        ? (Price - PreviousClose.Value) / PreviousClose.Value * 100 : null;

    /// <summary>Prior bar's VWAP (used by VWAP reclaim / loss conditions).</summary>
    public double? PriorVwap { get; set; }

    // ATR (used by stop / target / trailing verbs that quote multiples of ATR)
    public double? Atr { get; set; }

    // Bollinger Bands
    public double? BollingerUpper { get; set; }
    public double? BollingerMiddle { get; set; }
    public double? BollingerLower { get; set; }

    // Stochastic
    public double? StochasticK { get; set; }
    public double? StochasticD { get; set; }

    // Candlestick pattern flags (computed by CandlestickPatterns from the latest bar).
    public bool IsBullishEngulfing { get; set; }
    public bool IsBearishEngulfing { get; set; }
    public bool IsHammer { get; set; }
    public bool IsShootingStar { get; set; }
    public bool IsDoji { get; set; }

    // Swing structure (used by IsAtSupport / IsAtResistance).
    public double? RecentSwingHigh { get; set; }
    public double? RecentSwingLow { get; set; }

    // Bar fields exposed to conditions that need OHLC, not just Price (close).
    public double? BarOpen { get; set; }
    public double? BarHigh { get; set; }
    public double? BarLow { get; set; }
    public double BarClose => Price;

    /// <summary>
    /// Highest high / lowest low across the ENTIRE candle window this snapshot
    /// was built from (the session so far in the backtesters; the trailing
    /// 4-hour window in the Monitor). These give per-tick evaluators —
    /// which re-materialize conditions every tick and therefore hold no
    /// instance state — a window-scoped memory: Breakout(level) can see that
    /// the level traded earlier, and HoldsAbove(level) can see an earlier
    /// violation, without cross-tick latches.
    /// </summary>
    public double? WindowHigh { get; set; }
    public double? WindowLow { get; set; }

    /// <summary>
    /// Swing structure from pivot detection over the window: the newest pivot
    /// low sits ABOVE the prior pivot low (a HIGHER LOW — "the bottom is likely
    /// in", the double-bottom buy) / the newest pivot high sits BELOW the prior
    /// (a LOWER HIGH — weakening, the short tell). Null until two pivots exist.
    /// </summary>
    public bool? HasHigherLow { get; set; }
    public bool? HasLowerHigh { get; set; }

    // RSI
    public double? Rsi { get; set; }
    public bool? HasBullishDivergence { get; set; }
    public bool? HasBearishDivergence { get; set; }

    // MACD
    public double? MacdLine { get; set; }
    public double? SignalLine { get; set; }
    public double? Histogram { get; set; }
    public bool IsMacdBullish => MacdLine > SignalLine;

    // ADX
    public double? Adx { get; set; }
    public double? PlusDI { get; set; }
    public double? MinusDI { get; set; }
    public bool IsTrending => Adx >= 25;
    public bool IsBullishTrend => PlusDI > MinusDI;

    // Volume
    public long Volume { get; set; }
    public double AverageVolume { get; set; }
    // Spike ratio = current bar volume vs the recent baseline. When the baseline
    // is zero (a thin premarket small-cap whose prior bars had no trades at all),
    // the arrival of ANY real volume IS the spike — returning 0 there would make
    // IsVolumeAbove/WithVolumeConfirm fail on exactly the gap-and-go breakout the
    // volume screen exists to catch, silently blocking the fire. Treat a live bar
    // over a dead baseline as a confirmed spike (large sentinel); only a bar that
    // itself has zero volume yields 0.
    public double VolumeRatio => AverageVolume > 0
        ? Volume / AverageVolume
        : (Volume > 0 ? 999.0 : 0);

    /// <summary>
    /// Calculates an overall market score from -100 to +100.
    /// </summary>
    public int CalculateMarketScore()
    {
        double score = 0;
        int factors = 0;

        // VWAP (15%)
        if (VwapDistance.HasValue)
        {
            var vwapScore = Math.Max(-100, Math.Min(100, VwapDistance.Value * 20));
            score += vwapScore * 0.15;
            factors++;
        }

        // EMA stack (20%)
        if (Ema9.HasValue && Ema21.HasValue && Ema50.HasValue)
        {
            int bullishCount = 0;
            if (Price > Ema9) bullishCount++;
            if (Price > Ema21) bullishCount++;
            if (Price > Ema50) bullishCount++;
            if (Ema9 > Ema21) bullishCount++;
            if (Ema21 > Ema50) bullishCount++;

            var emaScore = ((bullishCount / 5.0) - 0.5) * 200;
            score += emaScore * 0.20;
            factors++;
        }

        // RSI (15%)
        if (Rsi.HasValue)
        {
            double rsiScore;
            if (Rsi <= 30) rsiScore = 100;  // Oversold = bullish
            else if (Rsi >= 70) rsiScore = -100;  // Overbought = bearish
            else rsiScore = ((50 - Rsi.Value) / 20) * 100;  // Linear between

            // Divergence bonus
            if (HasBullishDivergence == true) rsiScore += 30;
            if (HasBearishDivergence == true) rsiScore -= 30;

            score += Math.Max(-100, Math.Min(100, rsiScore)) * 0.15;
            factors++;
        }

        // MACD (20%)
        if (MacdLine.HasValue && SignalLine.HasValue)
        {
            double macdScore = IsMacdBullish ? 50 : -50;
            if (Histogram.HasValue)
            {
                macdScore += Math.Max(-50, Math.Min(50, Histogram.Value * 10));
            }
            score += macdScore * 0.20;
            factors++;
        }

        // ADX (20%)
        if (Adx.HasValue && PlusDI.HasValue && MinusDI.HasValue)
        {
            var direction = IsBullishTrend ? 1 : -1;
            var strength = Math.Min(100, Adx.Value * 2);
            var adxScore = direction * strength;
            score += adxScore * 0.20;
            factors++;
        }

        // Volume (10%)
        if (VolumeRatio > 0)
        {
            // Volume confirms current direction
            var volumeBonus = Math.Min(50, (VolumeRatio - 1) * 25);
            var currentDirection = score > 0 ? 1 : -1;
            score += (volumeBonus * currentDirection) * 0.10;
        }

        return (int)Math.Max(-100, Math.Min(100, score));
    }
}

/// <summary>
/// AI analysis of a chart/setup.
/// </summary>
public sealed class AiAnalysis
{
    public string Symbol { get; set; } = "";
    public DateTime AnalyzedUtc { get; set; } = DateTime.UtcNow;

    // Overall assessment
    public string Summary { get; set; } = "";
    public int ConfidenceScore { get; set; }
    public TradeDirection? RecommendedDirection { get; set; }

    // Detailed analysis
    public List<string> BullishSignals { get; set; } = [];
    public List<string> BearishSignals { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    // Historical context
    public string? SimilarSetupReference { get; set; }
    public double? HistoricalWinRate { get; set; }

    // Risk assessment
    public string RiskLevel { get; set; } = "Medium";  // Low, Medium, High, Extreme
    public double SuggestedStopPercent { get; set; }
    public double SuggestedTargetPercent { get; set; }
}
