// ============================================================================
// Backtester - Offline strategy testing against historical data
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  BACKTESTING SETUP GUIDE                                                  ║
// ║                                                                           ║
// ║  OPTION 1: OFFLINE BACKTESTING (No IB Connection Required)               ║
// ║  ─────────────────────────────────────────────────────────────────────── ║
// ║  Use pre-loaded CSV/JSON data files or mock data generators.              ║
// ║  This is what this file provides - fully offline simulation.              ║
// ║                                                                           ║
// ║  OPTION 2: LIVE DATA BACKTESTING (IB Gateway Connection Required)        ║
// ║  ─────────────────────────────────────────────────────────────────────── ║
// ║  To fetch historical data from IB Gateway:                                ║
// ║                                                                           ║
// ║  1. Start IB Gateway (not TWS)                                           ║
// ║     - Paper: Port 4002                                                   ║
// ║     - Live:  Port 4001                                                   ║
// ║                                                                           ║
// NOTE: Add using IdiotProof.Enums; at the top of this file for enum types
// ║  2. Enable API connections in Gateway:                                    ║
// ║     Configure → Settings → API → Settings                                ║
// ║     ☑ Enable ActiveX and Socket Clients                                 ║
// ║     ☑ Read-Only API (for safety during testing)                         ║
// ║                                                                           ║
// ║  3. Use HistoricalDataFetcher to request data:                           ║
// ║     var fetcher = new HistoricalDataFetcher(wrapper, client);            ║
// ║     var bars = await fetcher.FetchAsync("AAPL", ...);                    ║
// ║                                                                           ║
// ║  4. Run backtest with fetched data:                                      ║
// ║     var result = Backtester.Run(strategy, bars);                         ║
// ║                                                                           ║
// ║  LIMITATIONS:                                                             ║
// ║  ─────────────────────────────────────────────────────────────────────── ║
// ║  • No order book simulation (fills at bar close)                         ║
// ║  • No slippage modeling                                                  ║
// ║  • Market orders fill at bar close price                                 ║
// ║  • Limit orders fill if price reaches limit                              ║
// ║                                                                           ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IdiotProof.Backend.UnitTests
{
    /// <summary>
    /// Configuration for a backtest run.
    /// </summary>
    public sealed class BacktestConfig
    {
        /// <summary>Starting cash balance for the simulation.</summary>
        public double InitialCash { get; init; } = 100_000.0;

        /// <summary>Commission per trade (total, not per share).</summary>
        public double CommissionPerTrade { get; init; } = 1.00;

        /// <summary>Slippage per share (simulated market impact).</summary>
        public double SlippagePerShare { get; init; } = 0.00;

        /// <summary>Allow multiple simultaneous positions.</summary>
        public bool AllowMultiplePositions { get; init; } = false;

        /// <summary>Log detailed trade information.</summary>
        public bool VerboseLogging { get; init; } = true;
    }

    /// <summary>
    /// Result of a single simulated trade.
    /// </summary>
    public sealed record TradeResult
    {
        /// <summary>Entry timestamp.</summary>
        public required DateTime EntryTime { get; init; }

        /// <summary>Exit timestamp.</summary>
        public required DateTime ExitTime { get; init; }

        /// <summary>Entry price.</summary>
        public required double EntryPrice { get; init; }

        /// <summary>Exit price.</summary>
        public required double ExitPrice { get; init; }

        /// <summary>Number of shares.</summary>
        public required int Quantity { get; init; }

        /// <summary>Trade side (Buy = long, Sell = short).</summary>
        public required OrderSide Side { get; init; }

        /// <summary>Exit reason (TakeProfit, StopLoss, TrailingStop, EndOfData).</summary>
        public required string ExitReason { get; init; }

        /// <summary>Commission paid.</summary>
        public double Commission { get; init; }

        /// <summary>Gross P&L before commission.</summary>
        public double GrossPnL => Side == OrderSide.Buy
            ? (ExitPrice - EntryPrice) * Quantity
            : (EntryPrice - ExitPrice) * Quantity;

        /// <summary>Net P&L after commission.</summary>
        public double NetPnL => GrossPnL - Commission;

        /// <summary>Return percentage.</summary>
        public double ReturnPercent => EntryPrice > 0
            ? (Side == OrderSide.Buy
                ? (ExitPrice - EntryPrice) / EntryPrice * 100
                : (EntryPrice - ExitPrice) / EntryPrice * 100)
            : 0;

        /// <summary>Trade duration.</summary>
        public TimeSpan Duration => ExitTime - EntryTime;
    }

    /// <summary>
    /// Aggregated results from a backtest run.
    /// </summary>
    public sealed class BacktestResult
    {
        /// <summary>Symbol that was tested.</summary>
        public required string Symbol { get; init; }

        /// <summary>Strategy name/description.</summary>
        public string? StrategyName { get; init; }

        /// <summary>Total number of trades executed.</summary>
        public int TotalTrades { get; init; }

        /// <summary>Number of winning trades (NetPnL > 0).</summary>
        public int WinningTrades { get; init; }

        /// <summary>Number of losing trades (NetPnL <= 0).</summary>
        public int LosingTrades { get; init; }

        /// <summary>Win rate as percentage.</summary>
        public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;

        /// <summary>Total gross profit from winning trades.</summary>
        public double GrossProfit { get; init; }

        /// <summary>Total gross loss from losing trades (as positive number).</summary>
        public double GrossLoss { get; init; }

        /// <summary>Net profit/loss after all commissions.</summary>
        public double NetPnL { get; init; }

        /// <summary>Profit factor (gross profit / gross loss).</summary>
        public double ProfitFactor => GrossLoss > 0 ? GrossProfit / GrossLoss : GrossProfit > 0 ? double.PositiveInfinity : 0;

        /// <summary>Maximum peak-to-trough drawdown in dollars.</summary>
        public double MaxDrawdown { get; init; }

        /// <summary>Maximum drawdown as percentage of peak equity.</summary>
        public double MaxDrawdownPercent { get; init; }

        /// <summary>Final equity (initial cash + net P&L).</summary>
        public double FinalEquity { get; init; }

        /// <summary>Total return as percentage.</summary>
        public double ReturnPercent { get; init; }

        /// <summary>Average trade P&L.</summary>
        public double AvgTradePnL => TotalTrades > 0 ? NetPnL / TotalTrades : 0;

        /// <summary>Average winning trade P&L.</summary>
        public double AvgWinningTrade { get; init; }

        /// <summary>Average losing trade P&L.</summary>
        public double AvgLosingTrade { get; init; }

        /// <summary>Largest winning trade.</summary>
        public double LargestWin { get; init; }

        /// <summary>Largest losing trade.</summary>
        public double LargestLoss { get; init; }

        /// <summary>Individual trade results.</summary>
        public IReadOnlyList<TradeResult> Trades { get; init; } = [];

        /// <summary>Start time of backtest data.</summary>
        public DateTime StartTime { get; init; }

        /// <summary>End time of backtest data.</summary>
        public DateTime EndTime { get; init; }

        /// <summary>Number of bars processed.</summary>
        public int BarsProcessed { get; init; }

        /// <summary>
        /// Generates a formatted summary report.
        /// </summary>
        public override string ToString() =>
            $"""
            
            ╔═══════════════════════════════════════════════════════════════════╗
            ║  BACKTEST RESULTS: {Symbol,-15}                                   ║
            ╠═══════════════════════════════════════════════════════════════════╣
            ║  Period: {StartTime:yyyy-MM-dd HH:mm} to {EndTime:yyyy-MM-dd HH:mm,-11} ║
            ║  Bars:   {BarsProcessed,-10}                                      ║
            ╠═══════════════════════════════════════════════════════════════════╣
            ║  TRADES                                                           ║
            ║  ───────────────────────────────────────────────────────────────  ║
            ║  Total:          {TotalTrades,-8} ({WinningTrades}W / {LosingTrades}L)                         ║
            ║  Win Rate:       {WinRate,7:F1}%                                       ║
            ║  Profit Factor:  {ProfitFactor,7:F2}                                       ║
            ║  Avg Trade:      ${AvgTradePnL,10:N2}                                 ║
            ╠═══════════════════════════════════════════════════════════════════╣
            ║  PROFIT & LOSS                                                    ║
            ║  ───────────────────────────────────────────────────────────────  ║
            ║  Gross Profit:   ${GrossProfit,10:N2}                                 ║
            ║  Gross Loss:     ${GrossLoss,10:N2}                                 ║
            ║  Net P&L:        ${NetPnL,10:N2}                                 ║
            ╠═══════════════════════════════════════════════════════════════════╣
            ║  RISK METRICS                                                     ║
            ║  ───────────────────────────────────────────────────────────────  ║
            ║  Max Drawdown:   ${MaxDrawdown,10:N2} ({MaxDrawdownPercent:F1}%)                   ║
            ║  Largest Win:    ${LargestWin,10:N2}                                 ║
            ║  Largest Loss:   ${LargestLoss,10:N2}                                 ║
            ╠═══════════════════════════════════════════════════════════════════╣
            ║  RETURNS                                                          ║
            ║  ───────────────────────────────────────────────────────────────  ║
            ║  Final Equity:   ${FinalEquity,10:N2}                                 ║
            ║  Return:         {ReturnPercent,10:F2}%                                   ║
            ╚═══════════════════════════════════════════════════════════════════╝
            """;
    }

    /// <summary>
    /// Simulates strategy execution against historical bar data.
    /// Completely offline - no IB connection required.
    /// </summary>
    public static class Backtester
    {
        /// <summary>
        /// Runs a strategy against historical data.
        /// </summary>
        /// <param name="strategy">The strategy to test.</param>
        /// <param name="bars">Historical price bars.</param>
        /// <param name="config">Backtest configuration (optional).</param>
        /// <returns>Backtest results with statistics and trade history.</returns>
        public static BacktestResult Run(
            TradingStrategy strategy,
            IReadOnlyList<HistoricalBar> bars,
            BacktestConfig? config = null)
        {
            config ??= new BacktestConfig();

            if (bars.Count == 0)
            {
                return CreateEmptyResult(strategy.Symbol, config);
            }

            var simulator = new StrategySimulator(strategy, config);
            var trades = new List<TradeResult>();

            // Process each bar
            foreach (var bar in bars)
            {
                simulator.ProcessBar(bar);

                // Collect any completed trades
                while (simulator.TryGetCompletedTrade(out var trade))
                {
                    trades.Add(trade);

                    if (config.VerboseLogging)
                    {
                        var icon = trade.NetPnL >= 0 ? "✓" : "✗";
                        Console.WriteLine($"  [{trade.ExitTime:MM/dd HH:mm}] {icon} {trade.ExitReason}: ${trade.NetPnL:F2}");
                    }
                }
            }

            // Close any open position at end
            if (simulator.TryCloseOpenPosition(bars[^1], out var finalTrade))
            {
                trades.Add(finalTrade);

                if (config.VerboseLogging)
                {
                    Console.WriteLine($"  [{finalTrade.ExitTime:MM/dd HH:mm}] ○ End of data: ${finalTrade.NetPnL:F2}");
                }
            }

            return CalculateResults(strategy.Symbol, bars, trades, config);
        }

        /// <summary>
        /// Runs a strategy against bars generated from a simple price sequence.
        /// Useful for unit testing specific price patterns.
        /// </summary>
        public static BacktestResult RunWithPrices(
            TradingStrategy strategy,
            double[] prices,
            DateTime startTime,
            TimeSpan barInterval,
            int volumePerBar = 1000,
            BacktestConfig? config = null)
        {
            var bars = GenerateBarsFromPrices(prices, startTime, barInterval, volumePerBar);
            return Run(strategy, bars, config);
        }

        /// <summary>
        /// Generates simple bars from a price sequence (close prices only).
        /// </summary>
        public static IReadOnlyList<HistoricalBar> GenerateBarsFromPrices(
            double[] prices,
            DateTime startTime,
            TimeSpan barInterval,
            int volumePerBar = 1000)
        {
            var bars = new List<HistoricalBar>(prices.Length);

            for (int i = 0; i < prices.Length; i++)
            {
                double price = prices[i];
                double prevPrice = i > 0 ? prices[i - 1] : price;

                bars.Add(new HistoricalBar
                {
                    Time = startTime.Add(barInterval * i),
                    Open = prevPrice,
                    High = Math.Max(price, prevPrice),
                    Low = Math.Min(price, prevPrice),
                    Close = price,
                    Volume = volumePerBar
                });
            }

            return bars;
        }

        /// <summary>
        /// Generates realistic test data with trend, pullback, and VWAP dynamics.
        /// </summary>
        public static IReadOnlyList<HistoricalBar> GenerateTestScenario(
            double startPrice,
            double breakoutPrice,
            double pullbackPrice,
            double finalPrice,
            int barsToBreakout = 10,
            int barsToPullback = 5,
            int barsToFinal = 20,
            DateTime? startTime = null,
            TimeSpan? barInterval = null)
        {
            startTime ??= new DateTime(2024, 1, 15, 4, 0, 0); // Pre-market start
            barInterval ??= TimeSpan.FromMinutes(1);

            var prices = new List<double>();

            // Phase 1: Run up to breakout
            for (int i = 0; i <= barsToBreakout; i++)
            {
                double t = (double)i / barsToBreakout;
                prices.Add(startPrice + (breakoutPrice - startPrice) * t);
            }

            // Phase 2: Pullback
            for (int i = 1; i <= barsToPullback; i++)
            {
                double t = (double)i / barsToPullback;
                prices.Add(breakoutPrice + (pullbackPrice - breakoutPrice) * t);
            }

            // Phase 3: Recovery to final
            for (int i = 1; i <= barsToFinal; i++)
            {
                double t = (double)i / barsToFinal;
                prices.Add(pullbackPrice + (finalPrice - pullbackPrice) * t);
            }

            return GenerateBarsFromPrices(prices.ToArray(), startTime.Value, barInterval.Value);
        }

        private static BacktestResult CreateEmptyResult(string symbol, BacktestConfig config)
        {
            return new BacktestResult
            {
                Symbol = symbol,
                TotalTrades = 0,
                WinningTrades = 0,
                LosingTrades = 0,
                GrossProfit = 0,
                GrossLoss = 0,
                NetPnL = 0,
                MaxDrawdown = 0,
                MaxDrawdownPercent = 0,
                FinalEquity = config.InitialCash,
                ReturnPercent = 0,
                Trades = [],
                StartTime = DateTime.MinValue,
                EndTime = DateTime.MinValue,
                BarsProcessed = 0
            };
        }

        private static BacktestResult CalculateResults(
            string symbol,
            IReadOnlyList<HistoricalBar> bars,
            List<TradeResult> trades,
            BacktestConfig config)
        {
            var winners = trades.Where(t => t.NetPnL > 0).ToList();
            var losers = trades.Where(t => t.NetPnL <= 0).ToList();

            double grossProfit = winners.Sum(t => t.GrossPnL);
            double grossLoss = Math.Abs(losers.Sum(t => t.GrossPnL));
            double totalCommission = trades.Sum(t => t.Commission);
            double netPnL = grossProfit - grossLoss - totalCommission;

            // Calculate drawdown
            double equity = config.InitialCash;
            double peakEquity = equity;
            double maxDrawdown = 0;
            double maxDrawdownPercent = 0;

            foreach (var trade in trades)
            {
                equity += trade.NetPnL;
                if (equity > peakEquity)
                    peakEquity = equity;

                double drawdown = peakEquity - equity;
                if (drawdown > maxDrawdown)
                {
                    maxDrawdown = drawdown;
                    maxDrawdownPercent = peakEquity > 0 ? drawdown / peakEquity * 100 : 0;
                }
            }

            return new BacktestResult
            {
                Symbol = symbol,
                TotalTrades = trades.Count,
                WinningTrades = winners.Count,
                LosingTrades = losers.Count,
                GrossProfit = grossProfit,
                GrossLoss = grossLoss,
                NetPnL = netPnL,
                MaxDrawdown = maxDrawdown,
                MaxDrawdownPercent = maxDrawdownPercent,
                FinalEquity = config.InitialCash + netPnL,
                ReturnPercent = netPnL / config.InitialCash * 100,
                AvgWinningTrade = winners.Count > 0 ? winners.Average(t => t.NetPnL) : 0,
                AvgLosingTrade = losers.Count > 0 ? losers.Average(t => t.NetPnL) : 0,
                LargestWin = winners.Count > 0 ? winners.Max(t => t.NetPnL) : 0,
                LargestLoss = losers.Count > 0 ? losers.Min(t => t.NetPnL) : 0,
                Trades = trades,
                StartTime = bars[0].Time,
                EndTime = bars[^1].Time,
                BarsProcessed = bars.Count
            };
        }
    }

    /// <summary>
    /// Internal simulator that processes bars and tracks strategy state.
    /// </summary>
    internal sealed class StrategySimulator
    {
        private readonly TradingStrategy _strategy;
        private readonly BacktestConfig _config;
        private readonly Queue<TradeResult> _completedTrades = new();

        private int _conditionIndex;
        private bool _inPosition;
        private double _entryPrice;
        private DateTime _entryTime;

        // VWAP tracking
        private double _pvSum;
        private double _vSum;

        // Trailing stop tracking
        private double _highWaterMark;
        private double _trailingStopPrice;

        public StrategySimulator(TradingStrategy strategy, BacktestConfig config)
        {
            _strategy = strategy;
            _config = config;
        }

        public void ProcessBar(HistoricalBar bar)
        {
            // Update VWAP
            if (bar.Close > 0 && bar.Volume > 0)
            {
                _pvSum += bar.Close * bar.Volume;
                _vSum += bar.Volume;
            }

            double vwap = _vSum > 0 ? _pvSum / _vSum : bar.Close;

            if (!_inPosition)
            {
                EvaluateEntryConditions(bar.Close, vwap, bar.Time);
            }
            else
            {
                EvaluateExitConditions(bar, vwap);
            }
        }

        public bool TryGetCompletedTrade(out TradeResult trade)
        {
            if (_completedTrades.Count > 0)
            {
                trade = _completedTrades.Dequeue();
                return true;
            }
            trade = null!;
            return false;
        }

        public bool TryCloseOpenPosition(HistoricalBar bar, out TradeResult trade)
        {
            if (_inPosition)
            {
                trade = CreateTradeResult(bar.Close, bar.Time, "EndOfData");
                ResetForNextTrade();
                return true;
            }
            trade = null!;
            return false;
        }

        private void EvaluateEntryConditions(double price, double vwap, DateTime time)
        {
            if (_conditionIndex >= _strategy.Conditions.Count)
                return;

            var condition = _strategy.Conditions[_conditionIndex];
            if (condition.Evaluate(price, vwap))
            {
                _conditionIndex++;

                // All conditions met - enter position
                if (_conditionIndex >= _strategy.Conditions.Count)
                {
                    EnterPosition(price, time);
                }
            }
        }

        private void EnterPosition(double price, DateTime time)
        {
            // Apply slippage for entry
            double slippage = _config.SlippagePerShare * _strategy.Order.Quantity;
            double fillPrice = _strategy.Order.Side == OrderSide.Buy
                ? price + slippage / _strategy.Order.Quantity
                : price - slippage / _strategy.Order.Quantity;

            _inPosition = true;
            _entryPrice = Math.Round(fillPrice, 2);
            _entryTime = time;
            _highWaterMark = fillPrice;

            // Initialize trailing stop
            if (_strategy.Order.EnableTrailingStopLoss)
            {
                _trailingStopPrice = _entryPrice * (1 - _strategy.Order.TrailingStopLossPercent);
            }
        }

        private void EvaluateExitConditions(HistoricalBar bar, double vwap)
        {
            var order = _strategy.Order;
            bool isLong = order.Side == OrderSide.Buy;
            double price = bar.Close;

            // Update trailing stop (check high for long positions)
            if (order.EnableTrailingStopLoss && isLong)
            {
                if (bar.High > _highWaterMark)
                {
                    _highWaterMark = bar.High;
                    double newStop = _highWaterMark * (1 - order.TrailingStopLossPercent);
                    if (newStop > _trailingStopPrice)
                        _trailingStopPrice = newStop;
                }

                // Check if low touched trailing stop
                if (bar.Low <= _trailingStopPrice)
                {
                    ExitPosition(_trailingStopPrice, bar.Time, "TrailingStop");
                    return;
                }
            }

            // Check take profit
            if (order.EnableTakeProfit)
            {
                double tpPrice = order.TakeProfitPrice ?? (_entryPrice + order.TakeProfitOffset);
                if ((isLong && bar.High >= tpPrice) || (!isLong && bar.Low <= tpPrice))
                {
                    ExitPosition(tpPrice, bar.Time, "TakeProfit");
                    return;
                }
            }

            // Check stop loss
            if (order.EnableStopLoss)
            {
                double slPrice = order.StopLossPrice ?? (_entryPrice - order.StopLossOffset);
                if ((isLong && bar.Low <= slPrice) || (!isLong && bar.High >= slPrice))
                {
                    ExitPosition(slPrice, bar.Time, "StopLoss");
                    return;
                }
            }
        }

        private void ExitPosition(double price, DateTime time, string reason)
        {
            var trade = CreateTradeResult(price, time, reason);
            _completedTrades.Enqueue(trade);
            ResetForNextTrade();
        }

        private TradeResult CreateTradeResult(double exitPrice, DateTime exitTime, string reason)
        {
            return new TradeResult
            {
                EntryTime = _entryTime,
                ExitTime = exitTime,
                EntryPrice = _entryPrice,
                ExitPrice = Math.Round(exitPrice, 2),
                Quantity = _strategy.Order.Quantity,
                Side = _strategy.Order.Side,
                ExitReason = reason,
                Commission = _config.CommissionPerTrade * 2 // Entry + exit
            };
        }

        private void ResetForNextTrade()
        {
            _inPosition = false;
            _conditionIndex = 0;
            _pvSum = 0;
            _vSum = 0;
        }
    }
}
