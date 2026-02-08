// ============================================================================
// Strategy Evaluation Engine - THE SINGLE SOURCE OF TRUTH FOR STRATEGY LOGIC
// ============================================================================
//
// CRITICAL: This engine contains ALL strategy evaluation logic.
// Both live trading (StrategyRunner) and backtesting (Backtester) 
// MUST use this same code. Any change here affects BOTH.
//
// This ensures backtesting results accurately predict live trading behavior.
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Strategy;
using IdiotProof.Core.Helpers;

namespace IdiotProof.Backend.Models;

// ============================================================================
// INDICATOR PROVIDER INTERFACE
// ============================================================================

/// <summary>
/// Provides indicator values for strategy evaluation.
/// Implemented by both live trading (from calculators) and backtesting (from arrays).
/// </summary>
public interface IIndicatorProvider
{
    // EMA values
    double GetEma(int period);
    double GetPreviousEma(int period);

    // ADX/DI values
    double GetAdx();
    double GetPlusDi();
    double GetMinusDi();

    // RSI
    double GetRsi();

    // MACD
    double GetMacd();
    double GetMacdSignal();
    double GetMacdHistogram();
    double GetMacdPreviousHistogram();

    // Momentum
    double GetMomentum();
    double GetRoc();

    // Volume
    long GetCurrentVolume();
    double GetAverageVolume();
    double GetVolumeRatio();

    // Candle data for pattern detection
    double GetLastCandleClose();
    double GetLastCandleHigh();
    double GetLastCandleLow();
    double[] GetRecentLows(int count);
    double[] GetRecentHighs(int count);

    // Bollinger Bands
    double GetBollingerUpper();
    double GetBollingerLower();
    double GetBollingerMiddle();

    // ATR
    double GetAtr();

    // Gap conditions
    double GetPreviousClose();

    // Warm-up status
    bool IsEmaReady(int period);
    bool IsAdxReady();
    bool IsRsiReady();
    bool IsMacdReady();
    bool IsMomentumReady();
    bool IsRocReady();
    bool IsVolumeReady();
    bool IsBollingerReady();
    bool IsAtrReady();
}

// ============================================================================
// EXIT CHECK RESULT
// ============================================================================

/// <summary>
/// Result of checking exit conditions.
/// </summary>
public readonly struct ExitCheckResult
{
    public bool ShouldExit { get; init; }
    public double ExitPrice { get; init; }
    public string ExitReason { get; init; }

    public static ExitCheckResult NoExit => new() { ShouldExit = false };

    public static ExitCheckResult Exit(double price, string reason) => new()
    {
        ShouldExit = true,
        ExitPrice = price,
        ExitReason = reason
    };
}

/// <summary>
/// Result of checking entry conditions.
/// </summary>
public readonly struct EntryCheckResult
{
    public bool AllConditionsMet { get; init; }
    public bool MissedTheBoat { get; init; }
    public int CurrentConditionIndex { get; init; }
    public string? BlockReason { get; init; }

    public static EntryCheckResult InProgress(int index) => new()
    {
        AllConditionsMet = false,
        MissedTheBoat = false,
        CurrentConditionIndex = index
    };

    public static EntryCheckResult Ready(int index) => new()
    {
        AllConditionsMet = true,
        MissedTheBoat = false,
        CurrentConditionIndex = index
    };

    public static EntryCheckResult Blocked(string reason) => new()
    {
        AllConditionsMet = false,
        MissedTheBoat = true,
        BlockReason = reason
    };
}

// ============================================================================
// STRATEGY EVALUATION ENGINE
// ============================================================================

/// <summary>
/// THE SINGLE SOURCE OF TRUTH for strategy evaluation logic.
/// Used by BOTH live trading (StrategyRunner) and backtesting.
/// </summary>
public static class StrategyEvaluationEngine
{
    // ========================================================================
    // CONDITION EVALUATION
    // ========================================================================

    /// <summary>
    /// Evaluates conditions for entry using the shared logic.
    /// This is THE ONLY method that should evaluate strategy conditions.
    /// </summary>
    /// <param name="conditions">The list of conditions to evaluate.</param>
    /// <param name="currentConditionIndex">Current index in the condition chain.</param>
    /// <param name="price">Current price.</param>
    /// <param name="vwap">Current VWAP.</param>
    /// <param name="indicators">Indicator provider for value lookups.</param>
    /// <returns>Updated condition index (advances when condition triggers).</returns>
    public static int EvaluateCondition(
        IReadOnlyList<IStrategyCondition> conditions,
        int currentConditionIndex,
        double price,
        double vwap,
        IIndicatorProvider? indicators = null)
    {
        if (currentConditionIndex >= conditions.Count)
            return currentConditionIndex;

        var condition = conditions[currentConditionIndex];

        // Wire up indicator callbacks if provider is available
        if (indicators != null)
        {
            WireUpIndicatorCallbacks(condition, indicators);
        }

        if (condition.Evaluate(price, vwap))
        {
            return currentConditionIndex + 1;
        }

        return currentConditionIndex;
    }

    /// <summary>
    /// Wires up indicator callbacks for conditions that need them.
    /// </summary>
    private static void WireUpIndicatorCallbacks(IStrategyCondition condition, IIndicatorProvider indicators)
    {
        switch (condition)
        {
            case EmaAboveCondition ema when ema.GetEmaValue == null:
                ema.GetEmaValue = () => indicators.GetEma(ema.Period);
                break;

            case EmaBelowCondition ema when ema.GetEmaValue == null:
                ema.GetEmaValue = () => indicators.GetEma(ema.Period);
                break;

            case EmaBetweenCondition ema when ema.GetLowerEmaValue == null:
                ema.GetLowerEmaValue = () => indicators.GetEma(ema.LowerPeriod);
                ema.GetUpperEmaValue = () => indicators.GetEma(ema.UpperPeriod);
                break;

            case EmaTurningUpCondition ema when ema.GetCurrentEmaValue == null:
                ema.GetCurrentEmaValue = () => indicators.GetEma(ema.Period);
                ema.GetPreviousEmaValue = () => indicators.GetPreviousEma(ema.Period);
                break;

            case AdxCondition adx when adx.GetAdxValue == null:
                adx.GetAdxValue = () => indicators.GetAdx();
                break;

            case DiCondition di when di.GetDiValues == null:
                di.GetDiValues = () => (indicators.GetPlusDi(), indicators.GetMinusDi());
                break;

            case RsiCondition rsi when rsi.GetRsiValue == null:
                rsi.GetRsiValue = () => indicators.GetRsi();
                break;

            case MacdCondition macd when macd.GetMacdValues == null:
                macd.GetMacdValues = () => (
                    indicators.GetMacd(),
                    indicators.GetMacdSignal(),
                    indicators.GetMacdHistogram(),
                    indicators.GetMacdPreviousHistogram()
                );
                break;

            case MomentumAboveCondition mom when mom.GetMomentumValue == null:
                mom.GetMomentumValue = () => indicators.GetMomentum();
                break;

            case MomentumBelowCondition mom when mom.GetMomentumValue == null:
                mom.GetMomentumValue = () => indicators.GetMomentum();
                break;

            case RocAboveCondition roc when roc.GetRocValue == null:
                roc.GetRocValue = () => indicators.GetRoc();
                break;

            case RocBelowCondition roc when roc.GetRocValue == null:
                roc.GetRocValue = () => indicators.GetRoc();
                break;

            case VolumeAboveCondition vol when vol.GetCurrentVolume == null:
                vol.GetCurrentVolume = () => indicators.GetCurrentVolume();
                vol.GetAverageVolume = () => indicators.GetAverageVolume();
                break;

            case HigherLowsCondition hl when hl.GetRecentLows == null:
                hl.GetRecentLows = () => indicators.GetRecentLows(hl.LookbackBars);
                break;

            case LowerHighsCondition lh when lh.GetRecentHighs == null:
                lh.GetRecentHighs = () => indicators.GetRecentHighs(lh.LookbackBars);
                break;

            case CloseAboveVwapCondition cv when cv.GetLastClose == null:
                cv.GetLastClose = () => indicators.GetLastCandleClose();
                break;

            case VwapRejectionCondition vr when vr.GetLastHigh == null:
                vr.GetLastHigh = () => indicators.GetLastCandleHigh();
                vr.GetLastClose = () => indicators.GetLastCandleClose();
                break;

            case GapUpCondition gu when !gu.IsPreviousCloseSet:
                double prevClose = indicators.GetPreviousClose();
                if (prevClose > 0)
                    gu.SetPreviousClose(prevClose);
                break;

            case GapDownCondition gd when !gd.IsPreviousCloseSet:
                double pc = indicators.GetPreviousClose();
                if (pc > 0)
                    gd.SetPreviousClose(pc);
                break;
        }
    }

    // ========================================================================
    // MISSED THE BOAT CHECK
    // ========================================================================

    /// <summary>
    /// Validates that the current price hasn't already exceeded the take profit target.
    /// This prevents buying at a price where there's no profit potential.
    /// </summary>
    public static bool CheckMissedTheBoat(
        double currentPrice,
        OrderAction order,
        bool isLong)
    {
        // Calculate the take profit threshold
        double? takeProfitThreshold = null;

        if (order.EnableTakeProfit)
        {
            if (order.TakeProfitOffset > 0)
            {
                // For long: entry not yet known, so use current price + offset as max sensible TP
                // This check is actually for AFTER conditions are met, so entry = currentPrice
                takeProfitThreshold = currentPrice + (isLong ? order.TakeProfitOffset : -order.TakeProfitOffset);
            }
            else if (order.TakeProfitPrice.HasValue)
            {
                takeProfitThreshold = order.TakeProfitPrice.Value;
            }
        }

        // If we have a threshold and current price already at or past it, we've missed the boat
        if (takeProfitThreshold.HasValue)
        {
            if (isLong && currentPrice >= takeProfitThreshold.Value)
            {
                return true; // Missed the boat - price already at/above TP
            }
            if (!isLong && currentPrice <= takeProfitThreshold.Value)
            {
                return true; // Missed the boat - price already at/below TP for short
            }
        }

        return false;
    }

    // ========================================================================
    // EXIT CONDITION CHECKING
    // ========================================================================

    /// <summary>
    /// Checks all exit conditions (Take Profit, Stop Loss, Trailing Stop).
    /// THE SAME LOGIC for both live trading and backtesting.
    /// </summary>
    /// <param name="currentPrice">Current price.</param>
    /// <param name="barHigh">High of the current bar (or same as currentPrice for tick data).</param>
    /// <param name="barLow">Low of the current bar (or same as currentPrice for tick data).</param>
    /// <param name="entryPrice">The entry price of the position.</param>
    /// <param name="order">The order configuration.</param>
    /// <param name="isLong">True if this is a long position.</param>
    /// <param name="highWaterMark">Highest price since entry (for trailing stop).</param>
    /// <param name="trailingStopPrice">Current trailing stop price.</param>
    /// <param name="atrValue">Current ATR value (for ATR-based stops).</param>
    /// <returns>Exit check result with details if exit is triggered.</returns>
    public static ExitCheckResult CheckExitConditions(
        double currentPrice,
        double barHigh,
        double barLow,
        double entryPrice,
        OrderAction order,
        bool isLong,
        ref double highWaterMark,
        ref double trailingStopPrice,
        double atrValue = 0)
    {
        // ====================================================================
        // TRAILING STOP LOSS
        // ====================================================================
        if (order.EnableTrailingStopLoss)
        {
            if (isLong)
            {
                // Update high water mark
                if (barHigh > highWaterMark)
                {
                    highWaterMark = barHigh;

                    // Calculate new trailing stop
                    double newStop;
                    if (order.UseAtrStopLoss && order.AtrStopLoss != null && atrValue > 0)
                    {
                        // ATR-based trailing stop
                        var atrConfig = order.AtrStopLoss;
                        double stopDistance = atrValue * atrConfig.Multiplier;

                        // Apply min/max bounds
                        double minDistance = highWaterMark * (atrConfig.MinStopPercent / 100.0);
                        double maxDistance = highWaterMark * (atrConfig.MaxStopPercent / 100.0);
                        stopDistance = Math.Clamp(stopDistance, minDistance, maxDistance);

                        newStop = highWaterMark - stopDistance;
                    }
                    else
                    {
                        // Percentage-based trailing stop
                        newStop = highWaterMark * (1 - order.TrailingStopLossPercent);
                    }

                    // Only update if new stop is higher (tighter)
                    if (newStop > trailingStopPrice)
                    {
                        trailingStopPrice = Math.Round(newStop, 2);
                    }
                }

                // Check if triggered
                if (trailingStopPrice > 0 && barLow <= trailingStopPrice)
                {
                    return ExitCheckResult.Exit(trailingStopPrice, "TrailingStop");
                }
            }
            else // Short position
            {
                // For shorts, track low water mark (lowest price = best for short)
                if (barLow < highWaterMark || highWaterMark == 0)
                {
                    highWaterMark = barLow;

                    double newStop;
                    if (order.UseAtrStopLoss && order.AtrStopLoss != null && atrValue > 0)
                    {
                        var atrConfig = order.AtrStopLoss;
                        double stopDistance = atrValue * atrConfig.Multiplier;
                        double minDistance = highWaterMark * (atrConfig.MinStopPercent / 100.0);
                        double maxDistance = highWaterMark * (atrConfig.MaxStopPercent / 100.0);
                        stopDistance = Math.Clamp(stopDistance, minDistance, maxDistance);
                        newStop = highWaterMark + stopDistance;
                    }
                    else
                    {
                        newStop = highWaterMark * (1 + order.TrailingStopLossPercent);
                    }

                    // For shorts, new stop should be lower (tighter)
                    if (trailingStopPrice == 0 || newStop < trailingStopPrice)
                    {
                        trailingStopPrice = Math.Round(newStop, 2);
                    }
                }

                // Check if triggered (price went above stop for shorts)
                if (trailingStopPrice > 0 && barHigh >= trailingStopPrice)
                {
                    return ExitCheckResult.Exit(trailingStopPrice, "TrailingStop");
                }
            }
        }

        // ====================================================================
        // TAKE PROFIT
        // ====================================================================
        if (order.EnableTakeProfit)
        {
            double tpPrice = order.TakeProfitPrice ?? 
                (isLong ? entryPrice + order.TakeProfitOffset : entryPrice - order.TakeProfitOffset);

            if (isLong && barHigh >= tpPrice)
            {
                return ExitCheckResult.Exit(tpPrice, "TakeProfit");
            }
            if (!isLong && barLow <= tpPrice)
            {
                return ExitCheckResult.Exit(tpPrice, "TakeProfit");
            }
        }

        // ====================================================================
        // STOP LOSS
        // ====================================================================
        if (order.EnableStopLoss)
        {
            double slPrice = order.StopLossPrice ?? 
                (isLong ? entryPrice - order.StopLossOffset : entryPrice + order.StopLossOffset);

            if (isLong && barLow <= slPrice)
            {
                return ExitCheckResult.Exit(slPrice, "StopLoss");
            }
            if (!isLong && barHigh >= slPrice)
            {
                return ExitCheckResult.Exit(slPrice, "StopLoss");
            }
        }

        return ExitCheckResult.NoExit;
    }

    // ========================================================================
    // MARKET SCORE CALCULATION (delegates to MarketScoreCalculator)
    // ========================================================================

    /// <summary>
    /// Calculates market score using the shared MarketScoreCalculator.
    /// This ensures backtesting uses THE EXACT SAME formula as live trading.
    /// </summary>
    public static MarketScoreResult CalculateMarketScore(
        double price,
        double vwap,
        IIndicatorProvider indicators)
    {
        var snapshot = new IndicatorSnapshot
        {
            Price = price,
            Vwap = vwap,
            Ema9 = indicators.GetEma(9),
            Ema21 = indicators.GetEma(21),
            Ema50 = indicators.GetEma(50),
            Rsi = indicators.GetRsi(),
            Macd = indicators.GetMacd(),
            MacdSignal = indicators.GetMacdSignal(),
            MacdHistogram = indicators.GetMacdHistogram(),
            Adx = indicators.GetAdx(),
            PlusDi = indicators.GetPlusDi(),
            MinusDi = indicators.GetMinusDi(),
            VolumeRatio = indicators.GetVolumeRatio(),
            BollingerUpper = indicators.GetBollingerUpper(),
            BollingerLower = indicators.GetBollingerLower(),
            BollingerMiddle = indicators.GetBollingerMiddle(),
            Atr = indicators.GetAtr()
        };

        return MarketScoreCalculator.Calculate(snapshot);
    }

    // ========================================================================
    // TRAILING STOP INITIALIZATION
    // ========================================================================

    /// <summary>
    /// Initializes trailing stop parameters when entering a position.
    /// </summary>
    public static (double highWaterMark, double trailingStopPrice) InitializeTrailingStop(
        double entryPrice,
        OrderAction order,
        bool isLong,
        double atrValue = 0)
    {
        double highWaterMark = entryPrice;
        double trailingStopPrice = 0;

        if (!order.EnableTrailingStopLoss)
            return (highWaterMark, trailingStopPrice);

        if (order.UseAtrStopLoss && order.AtrStopLoss != null && atrValue > 0)
        {
            var cfg = order.AtrStopLoss;
            double stopDistance = atrValue * cfg.Multiplier;
            double minDist = entryPrice * (cfg.MinStopPercent / 100.0);
            double maxDist = entryPrice * (cfg.MaxStopPercent / 100.0);
            stopDistance = Math.Clamp(stopDistance, minDist, maxDist);

            trailingStopPrice = isLong
                ? entryPrice - stopDistance
                : entryPrice + stopDistance;
        }
        else
        {
            trailingStopPrice = isLong
                ? entryPrice * (1 - order.TrailingStopLossPercent)
                : entryPrice * (1 + order.TrailingStopLossPercent);
        }

        return (highWaterMark, Math.Round(trailingStopPrice, 2));
    }
}
