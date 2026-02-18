// ============================================================================
// BreakoutBacktester - Backtests breakout-pullback strategies
// ============================================================================
//
// Tests breakout-pullback setups against historical data to validate:
// - Entry timing based on breakout + pullback confirmation
// - Target hit rates (T1, T2, T3)
// - Stop loss effectiveness
// - Overall profitability
// ============================================================================

using IdiotProof.Scripting;
using IdiotProof.Web.Services.TradingView;

namespace IdiotProof.Web.Services.MarketScanner;

/// <summary>
/// Backtests breakout-pullback strategies against historical data.
/// </summary>
public sealed class BreakoutBacktester
{
    private readonly ILogger<BreakoutBacktester> _logger;
    private readonly HistoricalDataProvider _dataProvider;
    
    public BreakoutBacktester(
        ILogger<BreakoutBacktester> logger,
        HistoricalDataProvider dataProvider)
    {
        _logger = logger;
        _dataProvider = dataProvider;
    }
    
    /// <summary>
    /// Backtests a breakout setup against historical data.
    /// </summary>
    public async Task<BacktestResult> BacktestSetupAsync(
        BreakoutSetup setup,
        DateTime startDate,
        DateTime endDate,
        BacktestConfig? config = null)
    {
        config ??= new BacktestConfig();
        
        var result = new BacktestResult
        {
            Symbol = setup.Symbol,
            SetupBias = setup.Bias,
            StartDate = startDate,
            EndDate = endDate,
            Config = config
        };
        
        try
        {
            // Get historical 1-minute bars
            var bars = await _dataProvider.GetHistoricalBarsAsync(
                setup.Symbol, startDate, endDate, "1min");
            
            if (bars == null || bars.Count == 0)
            {
                result.Error = "No historical data available";
                return result;
            }
            
            result.TotalBars = bars.Count;
            
            // Simulate the strategy
            var simulation = SimulateStrategy(setup, bars, config);
            
            result.Trades = simulation.Trades;
            result.TotalTrades = simulation.Trades.Count;
            result.WinningTrades = simulation.Trades.Count(t => t.PnL > 0);
            result.LosingTrades = simulation.Trades.Count(t => t.PnL <= 0);
            result.WinRate = result.TotalTrades > 0 
                ? result.WinningTrades * 100.0 / result.TotalTrades 
                : 0;
            result.TotalPnL = simulation.Trades.Sum(t => t.PnL);
            result.AveragePnL = result.TotalTrades > 0 
                ? result.TotalPnL / result.TotalTrades 
                : 0;
            result.MaxDrawdown = CalculateMaxDrawdown(simulation.Trades);
            result.ProfitFactor = CalculateProfitFactor(simulation.Trades);
            result.AverageHoldTime = simulation.Trades.Count > 0
                ? TimeSpan.FromMinutes(simulation.Trades.Average(t => t.HoldTimeMinutes))
                : TimeSpan.Zero;
            
            // Target analysis
            result.T1HitRate = CalculateTargetHitRate(simulation.Trades, "T1");
            result.T2HitRate = CalculateTargetHitRate(simulation.Trades, "T2");
            result.T3HitRate = CalculateTargetHitRate(simulation.Trades, "T3");
            
            _logger.LogInformation(
                "Backtest {Symbol}: {Trades} trades, {WinRate:F1}% win rate, ${PnL:F2} total P&L",
                setup.Symbol, result.TotalTrades, result.WinRate, result.TotalPnL);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backtest failed for {Symbol}", setup.Symbol);
            result.Error = ex.Message;
        }
        
        return result;
    }
    
    /// <summary>
    /// Backtests multiple setups and aggregates results.
    /// </summary>
    public async Task<AggregateBacktestResult> BacktestMultipleAsync(
        IEnumerable<BreakoutSetup> setups,
        DateTime startDate,
        DateTime endDate,
        BacktestConfig? config = null)
    {
        var results = new List<BacktestResult>();
        
        foreach (var setup in setups)
        {
            var result = await BacktestSetupAsync(setup, startDate, endDate, config);
            results.Add(result);
        }
        
        return new AggregateBacktestResult
        {
            StartDate = startDate,
            EndDate = endDate,
            SetupCount = results.Count,
            Results = results,
            TotalTrades = results.Sum(r => r.TotalTrades),
            OverallWinRate = results.Sum(r => r.TotalTrades) > 0
                ? results.Sum(r => r.WinningTrades) * 100.0 / results.Sum(r => r.TotalTrades)
                : 0,
            TotalPnL = results.Sum(r => r.TotalPnL),
            AverageT1HitRate = results.Count > 0 ? results.Average(r => r.T1HitRate) : 0,
            AverageT2HitRate = results.Count > 0 ? results.Average(r => r.T2HitRate) : 0,
            BestPerformer = results.MaxBy(r => r.TotalPnL),
            WorstPerformer = results.MinBy(r => r.TotalPnL)
        };
    }
    
    private SimulationResult SimulateStrategy(
        BreakoutSetup setup, 
        List<HistoricalBar> bars,
        BacktestConfig config)
    {
        var result = new SimulationResult();
        var state = SetupState.Watching;
        BacktestTrade? currentTrade = null;
        double? triggerBreakTime = null;
        
        for (int i = 1; i < bars.Count; i++)
        {
            var bar = bars[i];
            var prevBar = bars[i - 1];
            
            switch (state)
            {
                case SetupState.Watching:
                    // Check for breakout
                    if (bar.High > setup.TriggerPrice && prevBar.High <= setup.TriggerPrice)
                    {
                        state = SetupState.Triggered;
                        triggerBreakTime = i;
                        _logger.LogDebug("[{Time}] Triggered at ${Price}", bar.Time, bar.High);
                    }
                    break;
                    
                case SetupState.Triggered:
                    // Check for pullback (price comes back down)
                    if (bar.Low < setup.TriggerPrice * 1.01) // Within 1% of trigger
                    {
                        state = SetupState.PullingBack;
                    }
                    // Check for immediate failure
                    else if (bar.Low < setup.InvalidationPrice)
                    {
                        state = SetupState.Watching; // Reset
                        triggerBreakTime = null;
                    }
                    break;
                    
                case SetupState.PullingBack:
                    // Check for confirmation (bounce back above trigger)
                    bool holdsSupport = bar.Low >= setup.SupportPrice * 0.995;
                    bool bouncing = bar.Close > setup.TriggerPrice;
                    
                    if (holdsSupport && bouncing)
                    {
                        // ENTER TRADE
                        currentTrade = new BacktestTrade
                        {
                            EntryTime = bar.Time,
                            EntryPrice = bar.Close,
                            EntryBarIndex = i,
                            StopLoss = setup.InvalidationPrice,
                            Targets = setup.Targets.Select(t => new BacktestTarget
                            {
                                Label = t.Label,
                                Price = t.Price,
                                PercentToSell = t.PercentToSell
                            }).ToList()
                        };
                        state = SetupState.Entered;
                        _logger.LogDebug("[{Time}] Entered at ${Price}", bar.Time, bar.Close);
                    }
                    // Failed support
                    else if (bar.Low < setup.InvalidationPrice)
                    {
                        state = SetupState.Watching;
                        triggerBreakTime = null;
                    }
                    break;
                    
                case SetupState.Entered:
                    if (currentTrade == null) break;
                    
                    // Check stop loss
                    if (bar.Low <= currentTrade.StopLoss)
                    {
                        // Stop hit - close entire position
                        currentTrade.ExitTime = bar.Time;
                        currentTrade.ExitPrice = currentTrade.StopLoss;
                        currentTrade.ExitReason = "Stop Loss";
                        currentTrade.HoldTimeMinutes = (int)(bar.Time - currentTrade.EntryTime).TotalMinutes;
                        currentTrade.PnL = CalculatePnL(currentTrade, config.PositionSize);
                        
                        result.Trades.Add(currentTrade);
                        currentTrade = null;
                        state = SetupState.Watching;
                        triggerBreakTime = null;
                        continue;
                    }
                    
                    // Check targets
                    foreach (var target in currentTrade.Targets.Where(t => !t.IsHit))
                    {
                        if (bar.High >= target.Price)
                        {
                            target.IsHit = true;
                            target.HitTime = bar.Time;
                            target.HitBarIndex = i;
                            
                            // Move stop to breakeven after T1
                            if (target.Label == "T1" && config.MoveStopToBreakevenAfterT1)
                            {
                                currentTrade.StopLoss = currentTrade.EntryPrice;
                            }
                            // Move stop to T1 after T2
                            else if (target.Label == "T2" && config.MoveStopToT1AfterT2)
                            {
                                var t1 = currentTrade.Targets.FirstOrDefault(t => t.Label == "T1");
                                if (t1 != null)
                                    currentTrade.StopLoss = t1.Price;
                            }
                        }
                    }
                    
                    // All targets hit - trade complete
                    if (currentTrade.Targets.All(t => t.IsHit))
                    {
                        currentTrade.ExitTime = bar.Time;
                        currentTrade.ExitPrice = currentTrade.Targets.Last().Price;
                        currentTrade.ExitReason = "All Targets Hit";
                        currentTrade.HoldTimeMinutes = (int)(bar.Time - currentTrade.EntryTime).TotalMinutes;
                        currentTrade.PnL = CalculatePnL(currentTrade, config.PositionSize);
                        
                        result.Trades.Add(currentTrade);
                        currentTrade = null;
                        state = SetupState.Watching;
                        triggerBreakTime = null;
                    }
                    // Time limit
                    else if (config.MaxHoldMinutes > 0 && 
                             (bar.Time - currentTrade.EntryTime).TotalMinutes >= config.MaxHoldMinutes)
                    {
                        currentTrade.ExitTime = bar.Time;
                        currentTrade.ExitPrice = bar.Close;
                        currentTrade.ExitReason = "Time Limit";
                        currentTrade.HoldTimeMinutes = (int)(bar.Time - currentTrade.EntryTime).TotalMinutes;
                        currentTrade.PnL = CalculatePnL(currentTrade, config.PositionSize);
                        
                        result.Trades.Add(currentTrade);
                        currentTrade = null;
                        state = SetupState.Watching;
                        triggerBreakTime = null;
                    }
                    break;
            }
        }
        
        // Close any open trade at end
        if (currentTrade != null)
        {
            var lastBar = bars[^1];
            currentTrade.ExitTime = lastBar.Time;
            currentTrade.ExitPrice = lastBar.Close;
            currentTrade.ExitReason = "End of Data";
            currentTrade.HoldTimeMinutes = (int)(lastBar.Time - currentTrade.EntryTime).TotalMinutes;
            currentTrade.PnL = CalculatePnL(currentTrade, config.PositionSize);
            result.Trades.Add(currentTrade);
        }
        
        return result;
    }
    
    private double CalculatePnL(BacktestTrade trade, int positionSize)
    {
        // Calculate P&L based on partial exits at targets
        double totalPnL = 0;
        int remainingShares = positionSize;
        
        foreach (var target in trade.Targets.Where(t => t.IsHit).OrderBy(t => t.Price))
        {
            int sharesToSell = (int)(positionSize * target.PercentToSell / 100.0);
            sharesToSell = Math.Min(sharesToSell, remainingShares);
            
            totalPnL += sharesToSell * (target.Price - trade.EntryPrice);
            remainingShares -= sharesToSell;
        }
        
        // Remaining shares exit at final exit price
        if (remainingShares > 0)
        {
            totalPnL += remainingShares * (trade.ExitPrice - trade.EntryPrice);
        }
        
        return totalPnL;
    }
    
    private double CalculateMaxDrawdown(List<BacktestTrade> trades)
    {
        if (trades.Count == 0) return 0;
        
        double peak = 0;
        double maxDrawdown = 0;
        double equity = 0;
        
        foreach (var trade in trades)
        {
            equity += trade.PnL;
            if (equity > peak)
                peak = equity;
            
            var drawdown = peak - equity;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }
        
        return maxDrawdown;
    }
    
    private double CalculateProfitFactor(List<BacktestTrade> trades)
    {
        var grossProfit = trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
        var grossLoss = Math.Abs(trades.Where(t => t.PnL < 0).Sum(t => t.PnL));
        
        return grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? double.MaxValue : 0;
    }
    
    private double CalculateTargetHitRate(List<BacktestTrade> trades, string targetLabel)
    {
        var tradesWithTarget = trades.Where(t => t.Targets.Any(tg => tg.Label == targetLabel)).ToList();
        if (tradesWithTarget.Count == 0) return 0;
        
        var hits = tradesWithTarget.Count(t => t.Targets.Any(tg => tg.Label == targetLabel && tg.IsHit));
        return hits * 100.0 / tradesWithTarget.Count;
    }
}

/// <summary>
/// Configuration for backtesting.
/// </summary>
public sealed class BacktestConfig
{
    public int PositionSize { get; set; } = 100;
    public bool MoveStopToBreakevenAfterT1 { get; set; } = true;
    public bool MoveStopToT1AfterT2 { get; set; } = true;
    public int MaxHoldMinutes { get; set; } = 0; // 0 = no limit
    public double Commission { get; set; } = 0;
}

/// <summary>
/// Result of backtesting a single setup.
/// </summary>
public sealed class BacktestResult
{
    public string Symbol { get; set; } = "";
    public string SetupBias { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public BacktestConfig Config { get; set; } = new();
    
    public int TotalBars { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public double WinRate { get; set; }
    public double TotalPnL { get; set; }
    public double AveragePnL { get; set; }
    public double MaxDrawdown { get; set; }
    public double ProfitFactor { get; set; }
    public TimeSpan AverageHoldTime { get; set; }
    
    public double T1HitRate { get; set; }
    public double T2HitRate { get; set; }
    public double T3HitRate { get; set; }
    
    public List<BacktestTrade> Trades { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>
/// Aggregate results from backtesting multiple setups.
/// </summary>
public sealed class AggregateBacktestResult
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int SetupCount { get; set; }
    public int TotalTrades { get; set; }
    public double OverallWinRate { get; set; }
    public double TotalPnL { get; set; }
    public double AverageT1HitRate { get; set; }
    public double AverageT2HitRate { get; set; }
    public List<BacktestResult> Results { get; set; } = [];
    public BacktestResult? BestPerformer { get; set; }
    public BacktestResult? WorstPerformer { get; set; }
}

/// <summary>
/// A simulated trade from backtesting.
/// </summary>
public sealed class BacktestTrade
{
    public DateTime EntryTime { get; set; }
    public double EntryPrice { get; set; }
    public int EntryBarIndex { get; set; }
    public double StopLoss { get; set; }
    public List<BacktestTarget> Targets { get; set; } = [];
    
    public DateTime ExitTime { get; set; }
    public double ExitPrice { get; set; }
    public string ExitReason { get; set; } = "";
    public int HoldTimeMinutes { get; set; }
    public double PnL { get; set; }
}

/// <summary>
/// A target level in a backtest trade.
/// </summary>
public sealed class BacktestTarget
{
    public string Label { get; set; } = "";
    public double Price { get; set; }
    public int PercentToSell { get; set; }
    public bool IsHit { get; set; }
    public DateTime? HitTime { get; set; }
    public int? HitBarIndex { get; set; }
}

/// <summary>
/// Internal simulation state.
/// </summary>
internal sealed class SimulationResult
{
    public List<BacktestTrade> Trades { get; set; } = [];
}
