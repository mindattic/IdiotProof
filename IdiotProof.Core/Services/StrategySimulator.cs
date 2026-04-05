// ============================================================================
// Strategy Simulator - Runs backtest simulations
// ============================================================================
//
// Simulates trading a strategy against historical 1-minute candle data.
// Tracks entries, exits, P&L, and generates performance metrics.
//
// ============================================================================

using IdiotProof.Models;

namespace IdiotProof.Services;

/// <summary>
/// Simulates trading strategies against historical data.
/// </summary>
public sealed class StrategySimulator
{
    private readonly BackTestSession session;

    public StrategySimulator(BackTestSession session)
    {
        this.session = session;
    }

    /// <summary>
    /// Runs a simulation with the given parameters.
    /// </summary>
    public SimulationResult Simulate(StrategyParameters parameters)
    {
        var result = new SimulationResult { Parameters = parameters };
        var candles = session.Candles;

        if (candles.Count == 0)
            return result;

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
                if (ShouldEnter(candle, parameters, candles, i))
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
                        highSinceEntry = candle.Low;  // Actually low since entry for shorts
                        trailingStopPrice = highSinceEntry * (1 + parameters.TrailingStopPercent.Value / 100);
                    }
                }

                // Check exit conditions
                var (shouldExit, exitPrice, exitReason) = CheckExit(
                    candle, parameters, entryPrice, trailingStopPrice);

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

        // Close any open position at end of data
        if (inPosition && candles.Count > 0)
        {
            var lastCandle = candles[^1];
            result.Trades.Add(CreateTrade(
                entryTime, lastCandle.Timestamp,
                entryPrice, lastCandle.Close,
                parameters.Quantity, parameters.IsLong,
                ExitReason.EndOfDay));
        }

        // Calculate drawdown
        CalculateDrawdown(result);

        return result;
    }

    private bool ShouldEnter(
        BackTestCandle candle, 
        StrategyParameters parameters,
        List<BackTestCandle> candles,
        int index)
    {
        // Check if price hit entry level
        bool priceHitEntry = parameters.IsLong
            ? candle.High >= parameters.EntryPrice
            : candle.Low <= parameters.EntryPrice;

        if (!priceHitEntry) return false;

        // Check VWAP condition
        if (parameters.RequireAboveVwap)
        {
            bool aboveVwap = parameters.IsLong
                ? candle.Close >= candle.Vwap
                : candle.Close <= candle.Vwap;
            if (!aboveVwap) return false;
        }

        // Check higher lows condition
        if (parameters.RequireHigherLows && index >= 3)
        {
            var recent = candles.Skip(index - 3).Take(3).ToList();
            bool higherLows = recent[1].Low > recent[0].Low && recent[2].Low > recent[1].Low;
            if (!higherLows) return false;
        }

        // Check pullback condition
        if (parameters.PullbackPrice.HasValue && index > 0)
        {
            bool hadPullback = candles.Take(index).Any(c => 
                parameters.IsLong 
                    ? c.Low <= parameters.PullbackPrice.Value
                    : c.High >= parameters.PullbackPrice.Value);
            if (!hadPullback) return false;
        }

        return true;
    }

    private (bool shouldExit, double exitPrice, ExitReason reason) CheckExit(
        BackTestCandle candle,
        StrategyParameters parameters,
        double entryPrice,
        double trailingStopPrice)
    {
        if (parameters.IsLong)
        {
            // Check take profit
            if (candle.High >= parameters.TakeProfitPrice)
            {
                return (true, parameters.TakeProfitPrice, ExitReason.TakeProfitHit);
            }

            // Check stop loss
            if (candle.Low <= parameters.StopLossPrice)
            {
                return (true, parameters.StopLossPrice, ExitReason.StopLossHit);
            }

            // Check trailing stop
            if (parameters.TrailingStopPercent.HasValue && candle.Low <= trailingStopPrice)
            {
                return (true, trailingStopPrice, ExitReason.TrailingStopHit);
            }
        }
        else  // Short
        {
            // Check take profit
            if (candle.Low <= parameters.TakeProfitPrice)
            {
                return (true, parameters.TakeProfitPrice, ExitReason.TakeProfitHit);
            }

            // Check stop loss
            if (candle.High >= parameters.StopLossPrice)
            {
                return (true, parameters.StopLossPrice, ExitReason.StopLossHit);
            }

            // Check trailing stop
            if (parameters.TrailingStopPercent.HasValue && candle.High >= trailingStopPrice)
            {
                return (true, trailingStopPrice, ExitReason.TrailingStopHit);
            }
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
            {
                peak = runningPnL;
            }

            double drawdown = peak - runningPnL;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }

        result.MaxDrawdown = maxDrawdown;
        result.MaxDrawdownPercent = peak > 0 ? maxDrawdown / peak * 100 : 0;
    }
}
