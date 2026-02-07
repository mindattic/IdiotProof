// ============================================================================
// StrategyBacktestService - IdiotScript Strategy Backtester
// ============================================================================
//
// PURPOSE:
// Tests IdiotScript strategies (.idiot files) against historical bar data
// to validate condition logic before live trading.
//
// DIFFERENCE FROM BacktestService:
// - BacktestService: Autonomous AI scoring-based trading simulation
// - StrategyBacktestService: Tests actual IdiotScript entry/exit conditions
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Strategy;
using IdiotProof.Shared.Helpers;
using IdiotProof.Shared.Models;

namespace IdiotProof.Backend.Services;

/// <summary>
/// Represents a simulated trade from IdiotScript backtest.
/// </summary>
public sealed class StrategyBacktestTrade
{
    public DateTime EntryTime { get; init; }
    public double EntryPrice { get; init; }
    public DateTime? ExitTime { get; init; }
    public double? ExitPrice { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public double PnL { get; init; }
    public bool IsWin => PnL > 0;
    public int Quantity { get; init; }
    public List<string> ConditionsTriggered { get; init; } = new();
}

/// <summary>
/// Result of an IdiotScript strategy backtest.
/// </summary>
public sealed class StrategyBacktestResult
{
    public bool Success { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string StrategyName { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public int BarsProcessed { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public double WinRate => TotalTrades > 0 ? (WinningTrades * 100.0 / TotalTrades) : 0;
    public double TotalPnL { get; init; }
    public double AvgPnL => TotalTrades > 0 ? TotalPnL / TotalTrades : 0;
    public List<StrategyBacktestTrade> Trades { get; init; } = new();
    public DateTime? FirstBarTime { get; init; }
    public DateTime? LastBarTime { get; init; }
}

/// <summary>
/// Service for backtesting IdiotScript strategies against historical data.
/// </summary>
public sealed class StrategyBacktestService
{
    private readonly HistoricalDataService _historicalDataService;

    // Indicator calculators
    private readonly Dictionary<int, EmaCalculator> _emaCalculators = new();
    private AdxCalculator? _adxCalculator;
    private RsiCalculator? _rsiCalculator;
    private MacdCalculator? _macdCalculator;
    private MomentumCalculator? _momentumCalculator;
    private RocCalculator? _rocCalculator;
    private VolumeCalculator? _volumeCalculator;
    private CandlestickAggregator? _candlestickAggregator;

    // VWAP accumulators
    private double _pvSum;
    private double _vSum;

    public StrategyBacktestService(HistoricalDataService historicalDataService)
    {
        _historicalDataService = historicalDataService ?? throw new ArgumentNullException(nameof(historicalDataService));
    }

    /// <summary>
    /// Runs a backtest for a specific strategy against historical data.
    /// </summary>
    public async Task<StrategyBacktestResult> RunBacktestAsync(
        TradingStrategy strategy,
        int days = 10,
        bool verboseLogging = true)
    {
        try
        {
            Console.WriteLine($"[STRATEGY-BACKTEST] Starting backtest for {strategy.Symbol} - {strategy.Name}");
            Console.WriteLine($"[STRATEGY-BACKTEST] Conditions: {strategy.Conditions.Count}");

            // Fetch historical data
            int barsPerDay = 960; // Extended hours
            int totalBarsNeeded = days * barsPerDay;
            var allBars = new List<HistoricalBar>();

            int daysRemaining = days;
            DateTime endDate = DateTime.Now;

            while (daysRemaining > 0 && allBars.Count < totalBarsNeeded)
            {
                int fetchDays = Math.Min(daysRemaining, 5);

                if (verboseLogging)
                    Console.WriteLine($"[STRATEGY-BACKTEST] Fetching {fetchDays} days ending {endDate:yyyy-MM-dd HH:mm}");

                int barsFetched = await _historicalDataService.FetchHistoricalDataAsync(
                    strategy.Symbol,
                    barCount: fetchDays * barsPerDay,
                    barSize: BarSize.Minutes1,
                    dataType: HistoricalDataType.Trades,
                    useRTH: false,
                    endDate: endDate);

                if (barsFetched == 0 && allBars.Count == 0)
                {
                    return new StrategyBacktestResult
                    {
                        Success = false,
                        Symbol = strategy.Symbol,
                        StrategyName = strategy.Name,
                        ErrorMessage = $"Failed to fetch historical data for {strategy.Symbol}"
                    };
                }

                var fetchedBars = _historicalDataService.Store.GetBars(strategy.Symbol);
                if (fetchedBars != null && fetchedBars.Count > 0)
                {
                    foreach (var bar in fetchedBars)
                    {
                        if (!allBars.Any(b => b.Time == bar.Time))
                        {
                            allBars.Add(bar);
                        }
                    }
                }

                daysRemaining -= fetchDays;
                endDate = endDate.AddDays(-fetchDays);

                if (daysRemaining > 0)
                    await Task.Delay(1000);
            }

            // Sort bars by time
            allBars = allBars.OrderBy(b => b.Time).ToList();

            Console.WriteLine($"[STRATEGY-BACKTEST] Fetched {allBars.Count} total bars for {strategy.Symbol}");

            if (allBars.Count < 50)
            {
                return new StrategyBacktestResult
                {
                    Success = false,
                    Symbol = strategy.Symbol,
                    StrategyName = strategy.Name,
                    ErrorMessage = $"Insufficient data: only {allBars.Count} bars (need at least 50)"
                };
            }

            // Run the simulation
            var trades = SimulateStrategy(strategy, allBars, verboseLogging);

            // Calculate results
            int totalTrades = trades.Count;
            int winningTrades = trades.Count(t => t.IsWin);
            double totalPnL = trades.Sum(t => t.PnL);

            var result = new StrategyBacktestResult
            {
                Success = true,
                Symbol = strategy.Symbol,
                StrategyName = strategy.Name,
                BarsProcessed = allBars.Count,
                TotalTrades = totalTrades,
                WinningTrades = winningTrades,
                TotalPnL = totalPnL,
                Trades = trades,
                FirstBarTime = allBars.FirstOrDefault()?.Time,
                LastBarTime = allBars.LastOrDefault()?.Time
            };

            return result;
        }
        catch (Exception ex)
        {
            return new StrategyBacktestResult
            {
                Success = false,
                Symbol = strategy.Symbol,
                StrategyName = strategy.Name,
                ErrorMessage = $"Error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Simulates a strategy against historical bars.
    /// </summary>
    private List<StrategyBacktestTrade> SimulateStrategy(
        TradingStrategy strategy,
        List<HistoricalBar> bars,
        bool verboseLogging)
    {
        var trades = new List<StrategyBacktestTrade>();

        // Initialize calculators
        InitializeCalculators(strategy);

        // Reset VWAP
        _pvSum = 0;
        _vSum = 0;

        // State
        int currentConditionIndex = 0;
        bool inPosition = false;
        double entryPrice = 0;
        DateTime entryTime = DateTime.MinValue;
        var conditionsTriggered = new List<string>();
        double highWaterMark = 0;
        int quantity = strategy.Order.Quantity;
        DateOnly currentDate = DateOnly.MinValue;

        // Warm-up period (first 30 bars for indicators)
        int warmupBars = 30;

        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var barDate = DateOnly.FromDateTime(bar.Time);

            // Reset VWAP on new day
            if (barDate != currentDate)
            {
                currentDate = barDate;
                _pvSum = 0;
                _vSum = 0;

                // Reset conditions on new day if not in position
                if (!inPosition)
                {
                    currentConditionIndex = 0;
                    conditionsTriggered.Clear();
                }
            }

            // Update VWAP
            double typicalPrice = (bar.High + bar.Low + bar.Close) / 3.0;
            _pvSum += typicalPrice * bar.Volume;
            _vSum += bar.Volume;
            double vwap = _vSum > 0 ? _pvSum / _vSum : bar.Close;

            // Update candlestick aggregator (simulate a completed bar)
            _candlestickAggregator?.Update(bar.Close, bar.Volume);

            // Simulate a candle completion
            UpdateIndicators(bar);

            // Skip warm-up period
            if (i < warmupBars)
                continue;

            double price = bar.Close;

            // Check time window
            var timeET = TimeOnly.FromDateTime(bar.Time);
            if (strategy.StartTime.HasValue && timeET < strategy.StartTime.Value)
                continue;
            if (strategy.EndTime.HasValue && timeET > strategy.EndTime.Value)
                continue;

            // If in position, check exits
            if (inPosition)
            {
                highWaterMark = Math.Max(highWaterMark, price);

                bool shouldExit = false;
                string exitReason = "";
                double exitPrice = price;

                // Check take profit
                if (strategy.Order.TakeProfitPrice.HasValue)
                {
                    double tpTarget = strategy.Order.TakeProfitPrice.Value;
                    if (price >= tpTarget)
                    {
                        shouldExit = true;
                        exitReason = "TakeProfit";
                        exitPrice = tpTarget;
                    }
                }

                // Check stop loss
                if (!shouldExit && strategy.Order.StopLossPrice.HasValue)
                {
                    double slTarget = strategy.Order.StopLossPrice.Value;
                    if (price <= slTarget)
                    {
                        shouldExit = true;
                        exitReason = "StopLoss";
                        exitPrice = slTarget;
                    }
                }

                // Check trailing stop
                if (!shouldExit && strategy.Order.EnableTrailingStopLoss)
                {
                    double trailPercent = strategy.Order.TrailingStopLossPercent;
                    double trailStop = highWaterMark * (1 - trailPercent);
                    if (price <= trailStop)
                    {
                        shouldExit = true;
                        exitReason = $"TrailingStop ({trailPercent * 100:F1}%)";
                        exitPrice = price;
                    }
                }

                // Check close position time
                if (!shouldExit && strategy.ClosePositionTime.HasValue)
                {
                    if (timeET >= strategy.ClosePositionTime.Value)
                    {
                        shouldExit = true;
                        exitReason = "ClosePositionTime";
                        exitPrice = price;
                    }
                }

                if (shouldExit)
                {
                    double pnl = (exitPrice - entryPrice) * quantity;

                    trades.Add(new StrategyBacktestTrade
                    {
                        EntryTime = entryTime,
                        EntryPrice = entryPrice,
                        ExitTime = bar.Time,
                        ExitPrice = exitPrice,
                        ExitReason = exitReason,
                        PnL = pnl,
                        Quantity = quantity,
                        ConditionsTriggered = new List<string>(conditionsTriggered)
                    });

                    if (verboseLogging)
                    {
                        var pnlColor = pnl >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
                        Console.ForegroundColor = pnlColor;
                        Console.WriteLine($"  EXIT @ ${exitPrice:F2} ({exitReason}) | P&L: ${pnl:F2}");
                        Console.ResetColor();
                    }

                    inPosition = false;
                    entryPrice = 0;
                    highWaterMark = 0;

                    // Reset for next trade (if Repeat enabled)
                    if (strategy.RepeatEnabled)
                    {
                        currentConditionIndex = 0;
                        conditionsTriggered.Clear();
                    }
                    else
                    {
                        // Strategy complete for this session - wait for new day
                        continue;
                    }
                }
            }
            else
            {
                // Not in position - check entry conditions
                if (currentConditionIndex < strategy.Conditions.Count)
                {
                    var condition = strategy.Conditions[currentConditionIndex];

                    // Wire up indicator getters for evaluation
                    WireUpCondition(condition);

                    if (condition.Evaluate(price, vwap))
                    {
                        conditionsTriggered.Add(condition.Name);
                        currentConditionIndex++;

                        if (verboseLogging)
                            Console.WriteLine($"  [{barDate:MM/dd} {timeET:HH:mm}] Condition {currentConditionIndex}: {condition.Name} @ ${price:F2}");

                        // All conditions met?
                        if (currentConditionIndex >= strategy.Conditions.Count)
                        {
                            // Enter position
                            inPosition = true;
                            entryPrice = price;
                            entryTime = bar.Time;
                            highWaterMark = price;

                            if (verboseLogging)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"  ENTRY @ ${price:F2} | Qty: {quantity}");
                                Console.ResetColor();
                            }
                        }
                    }
                }
            }
        }

        // If still in position at end, close at last price
        if (inPosition && bars.Count > 0)
        {
            var lastBar = bars[^1];
            double pnl = (lastBar.Close - entryPrice) * quantity;

            trades.Add(new StrategyBacktestTrade
            {
                EntryTime = entryTime,
                EntryPrice = entryPrice,
                ExitTime = lastBar.Time,
                ExitPrice = lastBar.Close,
                ExitReason = "EndOfData",
                PnL = pnl,
                Quantity = quantity,
                ConditionsTriggered = new List<string>(conditionsTriggered)
            });

            if (verboseLogging)
            {
                var pnlColor = pnl >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Console.ForegroundColor = pnlColor;
                Console.WriteLine($"  EXIT (EOD) @ ${lastBar.Close:F2} | P&L: ${pnl:F2}");
                Console.ResetColor();
            }
        }

        return trades;
    }

    /// <summary>
    /// Initializes indicator calculators based on conditions in the strategy.
    /// </summary>
    private void InitializeCalculators(TradingStrategy strategy)
    {
        _emaCalculators.Clear();
        _adxCalculator = null;
        _rsiCalculator = null;
        _macdCalculator = null;
        _momentumCalculator = null;
        _rocCalculator = null;
        _volumeCalculator = null;
        _candlestickAggregator = new CandlestickAggregator(candleSizeMinutes: 1);

        foreach (var condition in strategy.Conditions)
        {
            switch (condition)
            {
                case EmaAboveCondition ema:
                    if (!_emaCalculators.ContainsKey(ema.Period))
                        _emaCalculators[ema.Period] = new EmaCalculator(ema.Period);
                    break;
                case EmaBelowCondition ema:
                    if (!_emaCalculators.ContainsKey(ema.Period))
                        _emaCalculators[ema.Period] = new EmaCalculator(ema.Period);
                    break;
                case EmaBetweenCondition between:
                    if (!_emaCalculators.ContainsKey(between.LowerPeriod))
                        _emaCalculators[between.LowerPeriod] = new EmaCalculator(between.LowerPeriod);
                    if (!_emaCalculators.ContainsKey(between.UpperPeriod))
                        _emaCalculators[between.UpperPeriod] = new EmaCalculator(between.UpperPeriod);
                    break;
                case EmaTurningUpCondition emaUp:
                    if (!_emaCalculators.ContainsKey(emaUp.Period))
                        _emaCalculators[emaUp.Period] = new EmaCalculator(emaUp.Period);
                    break;
                case AdxCondition:
                case DiCondition:
                    _adxCalculator ??= new AdxCalculator(14);
                    break;
                case RsiCondition:
                    _rsiCalculator ??= new RsiCalculator(14);
                    break;
                case MacdCondition:
                    _macdCalculator ??= new MacdCalculator();
                    break;
                case MomentumAboveCondition:
                case MomentumBelowCondition:
                    _momentumCalculator ??= new MomentumCalculator(10);
                    break;
                case RocAboveCondition:
                case RocBelowCondition:
                    _rocCalculator ??= new RocCalculator(10);
                    break;
                case VolumeAboveCondition:
                    _volumeCalculator ??= new VolumeCalculator(20);
                    break;
            }
        }
    }

    /// <summary>
    /// Updates indicator calculators with new bar data.
    /// </summary>
    private void UpdateIndicators(HistoricalBar bar)
    {
        // Update all EMA calculators
        foreach (var ema in _emaCalculators.Values)
        {
            ema.Update(bar.Close);
        }

        // Update ADX (needs OHLC data)
        _adxCalculator?.UpdateFromCandle(bar.High, bar.Low, bar.Close);

        // Update RSI
        _rsiCalculator?.Update(bar.Close);

        // Update MACD
        _macdCalculator?.Update(bar.Close);

        // Update Momentum
        _momentumCalculator?.Update(bar.Close);

        // Update ROC
        _rocCalculator?.Update(bar.Close);

        // Update Volume
        _volumeCalculator?.Update(bar.Volume);
    }

    /// <summary>
    /// Wires up the condition with current indicator values.
    /// </summary>
    private void WireUpCondition(IStrategyCondition condition)
    {
        switch (condition)
        {
            case EmaAboveCondition ema when _emaCalculators.TryGetValue(ema.Period, out var calc):
                ema.GetEmaValue = () => calc.CurrentValue;
                break;
            case EmaBelowCondition ema when _emaCalculators.TryGetValue(ema.Period, out var calc):
                ema.GetEmaValue = () => calc.CurrentValue;
                break;
            case EmaBetweenCondition between:
                if (_emaCalculators.TryGetValue(between.LowerPeriod, out var lowerCalc))
                    between.GetLowerEmaValue = () => lowerCalc.CurrentValue;
                if (_emaCalculators.TryGetValue(between.UpperPeriod, out var upperCalc))
                    between.GetUpperEmaValue = () => upperCalc.CurrentValue;
                break;
            case EmaTurningUpCondition emaUp when _emaCalculators.TryGetValue(emaUp.Period, out var calc):
                emaUp.GetCurrentEmaValue = () => calc.CurrentValue;
                emaUp.GetPreviousEmaValue = () => calc.PreviousValue;
                break;
            case AdxCondition adx when _adxCalculator != null:
                adx.GetAdxValue = () => _adxCalculator.CurrentAdx;
                break;
            case DiCondition di when _adxCalculator != null:
                di.GetDiValues = () => (_adxCalculator.PlusDI, _adxCalculator.MinusDI);
                break;
            case RsiCondition rsi when _rsiCalculator != null:
                rsi.GetRsiValue = () => _rsiCalculator.CurrentValue;
                break;
            case MacdCondition macd when _macdCalculator != null:
                macd.GetMacdValues = () => (_macdCalculator.MacdLine, _macdCalculator.SignalLine, _macdCalculator.Histogram, _macdCalculator.PreviousHistogram);
                break;
            case MomentumAboveCondition mom when _momentumCalculator != null:
                mom.GetMomentumValue = () => _momentumCalculator.CurrentValue;
                break;
            case MomentumBelowCondition mom when _momentumCalculator != null:
                mom.GetMomentumValue = () => _momentumCalculator.CurrentValue;
                break;
            case RocAboveCondition roc when _rocCalculator != null:
                roc.GetRocValue = () => _rocCalculator.CurrentValue;
                break;
            case RocBelowCondition roc when _rocCalculator != null:
                roc.GetRocValue = () => _rocCalculator.CurrentValue;
                break;
            case VolumeAboveCondition vol when _volumeCalculator != null:
                vol.GetCurrentVolume = () => _volumeCalculator.CurrentVolume;
                vol.GetAverageVolume = () => _volumeCalculator.AverageVolume;
                break;
            case HigherLowsCondition hl when _candlestickAggregator != null:
                hl.GetRecentLows = () => _candlestickAggregator.GetRecentLows(hl.LookbackBars);
                break;
            case LowerHighsCondition lh when _candlestickAggregator != null:
                lh.GetRecentHighs = () => _candlestickAggregator.GetRecentHighs(lh.LookbackBars);
                break;
            case CloseAboveVwapCondition closeVwap when _candlestickAggregator != null:
                closeVwap.GetLastClose = () => _candlestickAggregator.LastCompletedCandle?.Close ?? 0;
                break;
            case VwapRejectionCondition vwapRej when _candlestickAggregator != null:
                vwapRej.GetLastHigh = () => _candlestickAggregator.LastCompletedCandle?.High ?? 0;
                vwapRej.GetLastClose = () => _candlestickAggregator.LastCompletedCandle?.Close ?? 0;
                break;
        }
    }
}
