// ============================================================================
// Enhanced Strategy Simulator - Runs simulations with technical indicators
// ============================================================================
//
// Extends the basic simulator with full indicator support for:
// - EMA crossovers and trends
// - RSI oversold/overbought
// - ADX trend strength
// - MACD signals
// - Volume confirmation
//
// ============================================================================

using IdiotProof.BackTesting.Analysis;
using IdiotProof.BackTesting.Models;

namespace IdiotProof.BackTesting.Services;

/// <summary>
/// Extended strategy parameters with indicator conditions.
/// </summary>
public sealed record EnhancedStrategyParameters : StrategyParameters
{
    // ========================================================================
    // EMA Conditions
    // ========================================================================

    /// <summary>Require price above this EMA period.</summary>
    public int? RequireEmaAbove { get; init; }

    /// <summary>Require price below this EMA period.</summary>
    public int? RequireEmaBelow { get; init; }

    /// <summary>Require EMA turning up.</summary>
    public int? RequireEmaTurningUp { get; init; }

    // ========================================================================
    // RSI Conditions
    // ========================================================================

    /// <summary>Maximum RSI for entry (oversold condition).</summary>
    public double? MaxRsiForEntry { get; init; }

    /// <summary>Minimum RSI for entry (momentum condition).</summary>
    public double? MinRsiForEntry { get; init; }

    /// <summary>Exit when RSI exceeds this level (overbought).</summary>
    public double? ExitOnRsiAbove { get; init; }

    // ========================================================================
    // MACD Conditions
    // ========================================================================

    /// <summary>Require MACD > Signal for entry.</summary>
    public bool RequireMacdBullish { get; init; }

    /// <summary>Exit on MACD bearish crossover.</summary>
    public bool ExitOnMacdBearish { get; init; }

    // ========================================================================
    // Volume Conditions
    // ========================================================================

    /// <summary>Minimum volume ratio for entry (e.g., 1.5 = 150% of average).</summary>
    public double? MinVolumeRatio { get; init; }
}

/// <summary>
/// Simulates strategies with full technical indicator support.
/// </summary>
public sealed class EnhancedStrategySimulator
{
    private readonly BackTestSession _session;
    private readonly IndicatorCalculator _indicators;

    // Cached indicator values
    private readonly Dictionary<int, double[]> _emaCache = [];
    private double[]? _rsi;
    private (double[] adx, double[] plusDi, double[] minusDi)? _adxData;
    private (double[] macd, double[] signal, double[] histogram)? _macdData;
    private double[]? _volumeRatio;

    public EnhancedStrategySimulator(BackTestSession session)
    {
        _session = session;
        _indicators = new IndicatorCalculator(session.Candles);
    }

    /// <summary>
    /// Runs a simulation with enhanced indicator conditions.
    /// </summary>
    public SimulationResult Simulate(EnhancedStrategyParameters parameters)
    {
        var result = new SimulationResult { Parameters = parameters };
        var candles = _session.Candles;

        if (candles.Count == 0)
            return result;

        // Pre-calculate indicators
        EnsureIndicatorsCalculated(parameters);

        bool inPosition = false;
        double entryPrice = 0;
        DateTime entryTime = default;
        double highSinceEntry = 0;
        double trailingStopPrice = 0;

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            var time = TimeOnly.FromDateTime(candle.Timestamp);

            // Check for EOD exit
            if (inPosition && parameters.ExitAtEod && time >= parameters.EodExitTime)
            {
                result.Trades.Add(CreateTrade(
                    entryTime, candle.Timestamp,
                    entryPrice, candle.Close,
                    parameters.Quantity, parameters.IsLong,
                    ExitReason.EndOfDay));
                inPosition = false;
                continue;
            }

            if (!inPosition)
            {
                // Check entry conditions
                if (ShouldEnter(candle, parameters, i))
                {
                    inPosition = true;
                    entryPrice = parameters.EntryPrice > 0 ? parameters.EntryPrice : candle.Close;
                    entryTime = candle.Timestamp;
                    highSinceEntry = candle.High;

                    // Initialize trailing stop
                    if (parameters.TrailingStopPercent.HasValue)
                    {
                        trailingStopPrice = parameters.IsLong
                            ? entryPrice * (1 - parameters.TrailingStopPercent.Value / 100)
                            : entryPrice * (1 + parameters.TrailingStopPercent.Value / 100);
                    }
                }
            }
            else
            {
                // Update trailing stop
                if (parameters.TrailingStopPercent.HasValue)
                {
                    if (parameters.IsLong && candle.High > highSinceEntry)
                    {
                        highSinceEntry = candle.High;
                        trailingStopPrice = highSinceEntry * (1 - parameters.TrailingStopPercent.Value / 100);
                    }
                    else if (!parameters.IsLong && candle.Low < highSinceEntry)
                    {
                        highSinceEntry = candle.Low;
                        trailingStopPrice = highSinceEntry * (1 + parameters.TrailingStopPercent.Value / 100);
                    }
                }

                // Check exit conditions
                var (shouldExit, exitPrice, exitReason) = CheckExit(
                    candle, parameters, entryPrice, trailingStopPrice, i);

                if (shouldExit)
                {
                    result.Trades.Add(CreateTrade(
                        entryTime, candle.Timestamp,
                        entryPrice, exitPrice,
                        parameters.Quantity, parameters.IsLong,
                        exitReason));
                    inPosition = false;
                }
            }
        }

        // Close any open position at end
        if (inPosition && candles.Count > 0)
        {
            var lastCandle = candles[^1];
            result.Trades.Add(CreateTrade(
                entryTime, lastCandle.Timestamp,
                entryPrice, lastCandle.Close,
                parameters.Quantity, parameters.IsLong,
                ExitReason.EndOfDay));
        }

        CalculateDrawdown(result);
        return result;
    }

    private void EnsureIndicatorsCalculated(EnhancedStrategyParameters p)
    {
        // Cache EMAs as needed
        if (p.RequireEmaAbove.HasValue && !_emaCache.ContainsKey(p.RequireEmaAbove.Value))
            _emaCache[p.RequireEmaAbove.Value] = _indicators.CalculateEma(p.RequireEmaAbove.Value);

        if (p.RequireEmaBelow.HasValue && !_emaCache.ContainsKey(p.RequireEmaBelow.Value))
            _emaCache[p.RequireEmaBelow.Value] = _indicators.CalculateEma(p.RequireEmaBelow.Value);

        if (p.RequireEmaTurningUp.HasValue && !_emaCache.ContainsKey(p.RequireEmaTurningUp.Value))
            _emaCache[p.RequireEmaTurningUp.Value] = _indicators.CalculateEma(p.RequireEmaTurningUp.Value);

        // RSI
        if (p.MaxRsiForEntry.HasValue || p.MinRsiForEntry.HasValue || p.ExitOnRsiAbove.HasValue)
            _rsi ??= _indicators.CalculateRsi();

        // ADX
        if (p.MinAdx.HasValue || p.RequireDiPositive)
            _adxData ??= _indicators.CalculateAdx();

        // MACD
        if (p.RequireMacdBullish || p.ExitOnMacdBearish)
            _macdData ??= _indicators.CalculateMacd();

        // Volume
        if (p.MinVolumeRatio.HasValue)
            _volumeRatio ??= _indicators.CalculateVolumeRatio();
    }

    private bool ShouldEnter(BackTestCandle candle, EnhancedStrategyParameters p, int index)
    {
        // Check price hit entry level
        bool priceHitEntry = p.IsLong
            ? candle.High >= p.EntryPrice
            : candle.Low <= p.EntryPrice;

        if (!priceHitEntry) return false;

        // Check VWAP condition
        if (p.RequireAboveVwap)
        {
            bool aboveVwap = p.IsLong
                ? candle.Close >= candle.Vwap
                : candle.Close <= candle.Vwap;
            if (!aboveVwap) return false;
        }

        // Check EMA above
        if (p.RequireEmaAbove.HasValue)
        {
            var ema = _emaCache[p.RequireEmaAbove.Value];
            if (candle.Close < ema[index]) return false;
        }

        // Check EMA below
        if (p.RequireEmaBelow.HasValue)
        {
            var ema = _emaCache[p.RequireEmaBelow.Value];
            if (candle.Close > ema[index]) return false;
        }

        // Check EMA turning up
        if (p.RequireEmaTurningUp.HasValue && index > 0)
        {
            var ema = _emaCache[p.RequireEmaTurningUp.Value];
            if (ema[index] <= ema[index - 1]) return false;
        }

        // Check RSI
        if (_rsi != null)
        {
            if (p.MaxRsiForEntry.HasValue && _rsi[index] > p.MaxRsiForEntry.Value)
                return false;
            if (p.MinRsiForEntry.HasValue && _rsi[index] < p.MinRsiForEntry.Value)
                return false;
        }

        // Check ADX
        if (_adxData.HasValue)
        {
            if (p.MinAdx.HasValue && _adxData.Value.adx[index] < p.MinAdx.Value)
                return false;
            if (p.RequireDiPositive && _adxData.Value.plusDi[index] <= _adxData.Value.minusDi[index])
                return false;
        }

        // Check MACD
        if (_macdData.HasValue && p.RequireMacdBullish)
        {
            if (_macdData.Value.macd[index] <= _macdData.Value.signal[index])
                return false;
        }

        // Check Volume
        if (_volumeRatio != null && p.MinVolumeRatio.HasValue)
        {
            if (_volumeRatio[index] < p.MinVolumeRatio.Value)
                return false;
        }

        // Check higher lows
        if (p.RequireHigherLows && index >= 3)
        {
            var candles = _session.Candles;
            var recent = candles.Skip(index - 3).Take(3).ToList();
            bool higherLows = recent[1].Low > recent[0].Low && recent[2].Low > recent[1].Low;
            if (!higherLows) return false;
        }

        return true;
    }

    private (bool shouldExit, double exitPrice, ExitReason reason) CheckExit(
        BackTestCandle candle,
        EnhancedStrategyParameters p,
        double entryPrice,
        double trailingStopPrice,
        int index)
    {
        if (p.IsLong)
        {
            // Check RSI overbought exit
            if (_rsi != null && p.ExitOnRsiAbove.HasValue && _rsi[index] >= p.ExitOnRsiAbove.Value)
            {
                return (true, candle.Close, ExitReason.ConditionMet);
            }

            // Check MACD bearish exit
            if (_macdData.HasValue && p.ExitOnMacdBearish)
            {
                if (_macdData.Value.macd[index] < _macdData.Value.signal[index])
                    return (true, candle.Close, ExitReason.ConditionMet);
            }

            // Check take profit
            if (candle.High >= p.TakeProfitPrice)
                return (true, p.TakeProfitPrice, ExitReason.TakeProfitHit);

            // Check stop loss
            if (candle.Low <= p.StopLossPrice)
                return (true, p.StopLossPrice, ExitReason.StopLossHit);

            // Check trailing stop
            if (p.TrailingStopPercent.HasValue && candle.Low <= trailingStopPrice)
                return (true, trailingStopPrice, ExitReason.TrailingStopHit);
        }
        else  // Short
        {
            // Check RSI oversold exit (for shorts)
            if (_rsi != null && p.ExitOnRsiAbove.HasValue && _rsi[index] <= (100 - p.ExitOnRsiAbove.Value))
            {
                return (true, candle.Close, ExitReason.ConditionMet);
            }

            // Check take profit
            if (candle.Low <= p.TakeProfitPrice)
                return (true, p.TakeProfitPrice, ExitReason.TakeProfitHit);

            // Check stop loss
            if (candle.High >= p.StopLossPrice)
                return (true, p.StopLossPrice, ExitReason.StopLossHit);

            // Check trailing stop
            if (p.TrailingStopPercent.HasValue && candle.High >= trailingStopPrice)
                return (true, trailingStopPrice, ExitReason.TrailingStopHit);
        }

        return (false, 0, ExitReason.ManualExit);
    }

    private static Trade CreateTrade(
        DateTime entryTime,
        DateTime exitTime,
        double entryPrice,
        double exitPrice,
        int quantity,
        bool isLong,
        ExitReason exitReason)
    {
        return new Trade
        {
            EntryTime = entryTime,
            ExitTime = exitTime,
            EntryPrice = entryPrice,
            ExitPrice = exitPrice,
            Quantity = quantity,
            IsLong = isLong,
            ExitReason = exitReason
        };
    }

    private static void CalculateDrawdown(SimulationResult result)
    {
        if (result.Trades.Count == 0) return;

        double peak = 0;
        double maxDrawdown = 0;
        double runningPnL = 0;

        foreach (var trade in result.Trades)
        {
            runningPnL += trade.PnL;

            if (runningPnL > peak)
                peak = runningPnL;

            double drawdown = peak - runningPnL;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        result.MaxDrawdown = maxDrawdown;
        result.MaxDrawdownPercent = peak > 0 ? maxDrawdown / peak * 100 : 0;
    }
}
