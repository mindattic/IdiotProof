// ============================================================================
// Technical Indicator Calculator - Computes indicators for backtesting
// ============================================================================

using IdiotProof.Models;

namespace IdiotProof.Analysis;

/// <summary>
/// Calculates technical indicators on historical candle data.
/// </summary>
public sealed class IndicatorCalculator
{
    private readonly List<BackTestCandle> _candles;

    public IndicatorCalculator(List<BackTestCandle> candles)
    {
        _candles = candles;
    }

    // ========================================================================
    // EMA (Exponential Moving Average)
    // ========================================================================

    /// <summary>
    /// Calculates EMA for all candles.
    /// </summary>
    public double[] CalculateEma(int period)
    {
        if (_candles.Count == 0) return [];

        var ema = new double[_candles.Count];
        double multiplier = 2.0 / (period + 1);

        // First EMA is SMA
        double sum = 0;
        for (int i = 0; i < Math.Min(period, _candles.Count); i++)
        {
            sum += _candles[i].Close;
            ema[i] = sum / (i + 1);  // Running SMA during warmup
        }

        if (_candles.Count <= period) return ema;

        // EMA calculation
        for (int i = period; i < _candles.Count; i++)
        {
            ema[i] = (_candles[i].Close - ema[i - 1]) * multiplier + ema[i - 1];
        }

        return ema;
    }

    /// <summary>
    /// Gets the EMA value at a specific candle index.
    /// </summary>
    public double GetEma(int period, int index)
    {
        var ema = CalculateEma(period);
        return index < ema.Length ? ema[index] : 0;
    }

    // ========================================================================
    // RSI (Relative Strength Index)
    // ========================================================================

    /// <summary>
    /// Calculates RSI for all candles.
    /// </summary>
    public double[] CalculateRsi(int period = 14)
    {
        if (_candles.Count <= period) return new double[_candles.Count];

        var rsi = new double[_candles.Count];
        var gains = new double[_candles.Count];
        var losses = new double[_candles.Count];

        // Calculate price changes
        for (int i = 1; i < _candles.Count; i++)
        {
            double change = _candles[i].Close - _candles[i - 1].Close;
            gains[i] = change > 0 ? change : 0;
            losses[i] = change < 0 ? -change : 0;
        }

        // First average
        double avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            avgGain += gains[i];
            avgLoss += losses[i];
        }
        avgGain /= period;
        avgLoss /= period;

        rsi[period] = avgLoss == 0 ? 100 : 100 - (100 / (1 + avgGain / avgLoss));

        // Smoothed averages
        for (int i = period + 1; i < _candles.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;

            rsi[i] = avgLoss == 0 ? 100 : 100 - (100 / (1 + avgGain / avgLoss));
        }

        return rsi;
    }

    // ========================================================================
    // ADX (Average Directional Index)
    // ========================================================================

    /// <summary>
    /// Calculates ADX, +DI, and -DI for all candles.
    /// </summary>
    public (double[] adx, double[] plusDi, double[] minusDi) CalculateAdx(int period = 14)
    {
        int count = _candles.Count;
        var adx = new double[count];
        var plusDi = new double[count];
        var minusDi = new double[count];

        if (count <= period * 2) return (adx, plusDi, minusDi);

        var tr = new double[count];
        var plusDm = new double[count];
        var minusDm = new double[count];

        // Calculate TR, +DM, -DM
        for (int i = 1; i < count; i++)
        {
            var curr = _candles[i];
            var prev = _candles[i - 1];

            double highLow = curr.High - curr.Low;
            double highPrevClose = Math.Abs(curr.High - prev.Close);
            double lowPrevClose = Math.Abs(curr.Low - prev.Close);
            tr[i] = Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));

            double upMove = curr.High - prev.High;
            double downMove = prev.Low - curr.Low;

            plusDm[i] = upMove > downMove && upMove > 0 ? upMove : 0;
            minusDm[i] = downMove > upMove && downMove > 0 ? downMove : 0;
        }

        // Smooth TR, +DM, -DM
        double smoothTr = 0, smoothPlusDm = 0, smoothMinusDm = 0;
        for (int i = 1; i <= period; i++)
        {
            smoothTr += tr[i];
            smoothPlusDm += plusDm[i];
            smoothMinusDm += minusDm[i];
        }

        // Calculate DI values
        for (int i = period; i < count; i++)
        {
            if (i > period)
            {
                smoothTr = smoothTr - smoothTr / period + tr[i];
                smoothPlusDm = smoothPlusDm - smoothPlusDm / period + plusDm[i];
                smoothMinusDm = smoothMinusDm - smoothMinusDm / period + minusDm[i];
            }

            plusDi[i] = smoothTr > 0 ? 100 * smoothPlusDm / smoothTr : 0;
            minusDi[i] = smoothTr > 0 ? 100 * smoothMinusDm / smoothTr : 0;
        }

        // Calculate DX and ADX
        var dx = new double[count];
        for (int i = period; i < count; i++)
        {
            double diSum = plusDi[i] + minusDi[i];
            dx[i] = diSum > 0 ? 100 * Math.Abs(plusDi[i] - minusDi[i]) / diSum : 0;
        }

        // Smooth DX to get ADX
        double smoothDx = 0;
        for (int i = period; i < period * 2 && i < count; i++)
        {
            smoothDx += dx[i];
        }

        if (period * 2 <= count)
        {
            adx[period * 2 - 1] = smoothDx / period;

            for (int i = period * 2; i < count; i++)
            {
                adx[i] = (adx[i - 1] * (period - 1) + dx[i]) / period;
            }
        }

        return (adx, plusDi, minusDi);
    }

    // ========================================================================
    // MACD (Moving Average Convergence Divergence)
    // ========================================================================

    /// <summary>
    /// Calculates MACD, Signal, and Histogram.
    /// </summary>
    public (double[] macd, double[] signal, double[] histogram) CalculateMacd(
        int fastPeriod = 12, 
        int slowPeriod = 26, 
        int signalPeriod = 9)
    {
        var fastEma = CalculateEma(fastPeriod);
        var slowEma = CalculateEma(slowPeriod);

        var macd = new double[_candles.Count];
        var signal = new double[_candles.Count];
        var histogram = new double[_candles.Count];

        // MACD = Fast EMA - Slow EMA
        for (int i = 0; i < _candles.Count; i++)
        {
            macd[i] = fastEma[i] - slowEma[i];
        }

        // Signal = EMA of MACD
        double multiplier = 2.0 / (signalPeriod + 1);
        for (int i = 0; i < slowPeriod && i < _candles.Count; i++)
        {
            signal[i] = macd[i];  // Warmup
        }

        for (int i = slowPeriod; i < _candles.Count; i++)
        {
            signal[i] = (macd[i] - signal[i - 1]) * multiplier + signal[i - 1];
            histogram[i] = macd[i] - signal[i];
        }

        return (macd, signal, histogram);
    }

    // ========================================================================
    // Momentum & ROC
    // ========================================================================

    /// <summary>
    /// Calculates momentum (price change over N periods).
    /// </summary>
    public double[] CalculateMomentum(int period = 10)
    {
        var momentum = new double[_candles.Count];

        for (int i = period; i < _candles.Count; i++)
        {
            momentum[i] = _candles[i].Close - _candles[i - period].Close;
        }

        return momentum;
    }

    /// <summary>
    /// Calculates Rate of Change (percentage momentum).
    /// </summary>
    public double[] CalculateRoc(int period = 10)
    {
        var roc = new double[_candles.Count];

        for (int i = period; i < _candles.Count; i++)
        {
            double prevClose = _candles[i - period].Close;
            roc[i] = prevClose > 0 ? ((_candles[i].Close - prevClose) / prevClose) * 100 : 0;
        }

        return roc;
    }

    // ========================================================================
    // ATR (Average True Range)
    // ========================================================================

    /// <summary>
    /// Calculates ATR for volatility measurement.
    /// </summary>
    public double[] CalculateAtr(int period = 14)
    {
        var atr = new double[_candles.Count];
        var tr = new double[_candles.Count];

        // Calculate True Range
        tr[0] = _candles[0].High - _candles[0].Low;

        for (int i = 1; i < _candles.Count; i++)
        {
            var curr = _candles[i];
            var prev = _candles[i - 1];

            double highLow = curr.High - curr.Low;
            double highPrevClose = Math.Abs(curr.High - prev.Close);
            double lowPrevClose = Math.Abs(curr.Low - prev.Close);
            tr[i] = Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));
        }

        // First ATR is SMA
        double sum = 0;
        for (int i = 0; i < Math.Min(period, _candles.Count); i++)
        {
            sum += tr[i];
            atr[i] = sum / (i + 1);
        }

        // Smoothed ATR
        for (int i = period; i < _candles.Count; i++)
        {
            atr[i] = (atr[i - 1] * (period - 1) + tr[i]) / period;
        }

        return atr;
    }

    // ========================================================================
    // Volume Analysis
    // ========================================================================

    /// <summary>
    /// Calculates volume moving average.
    /// </summary>
    public double[] CalculateVolumeSma(int period = 20)
    {
        var sma = new double[_candles.Count];

        for (int i = 0; i < _candles.Count; i++)
        {
            int start = Math.Max(0, i - period + 1);
            int count = i - start + 1;
            sma[i] = _candles.Skip(start).Take(count).Average(c => c.Volume);
        }

        return sma;
    }

    /// <summary>
    /// Gets volume ratio (current volume / average volume).
    /// </summary>
    public double[] CalculateVolumeRatio(int period = 20)
    {
        var avgVolume = CalculateVolumeSma(period);
        var ratio = new double[_candles.Count];

        for (int i = 0; i < _candles.Count; i++)
        {
            ratio[i] = avgVolume[i] > 0 ? _candles[i].Volume / avgVolume[i] : 1;
        }

        return ratio;
    }

    // ========================================================================
    // Pattern Detection
    // ========================================================================

    /// <summary>
    /// Detects higher lows pattern at each candle.
    /// </summary>
    public bool[] DetectHigherLows(int lookback = 3)
    {
        var result = new bool[_candles.Count];

        for (int i = lookback; i < _candles.Count; i++)
        {
            bool higherLows = true;
            for (int j = 1; j < lookback; j++)
            {
                if (_candles[i - j].Low <= _candles[i - j - 1].Low)
                {
                    higherLows = false;
                    break;
                }
            }
            result[i] = higherLows;
        }

        return result;
    }

    /// <summary>
    /// Detects higher highs pattern at each candle.
    /// </summary>
    public bool[] DetectHigherHighs(int lookback = 3)
    {
        var result = new bool[_candles.Count];

        for (int i = lookback; i < _candles.Count; i++)
        {
            bool higherHighs = true;
            for (int j = 1; j < lookback; j++)
            {
                if (_candles[i - j].High <= _candles[i - j - 1].High)
                {
                    higherHighs = false;
                    break;
                }
            }
            result[i] = higherHighs;
        }

        return result;
    }
}
