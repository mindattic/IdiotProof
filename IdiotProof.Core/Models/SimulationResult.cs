// ============================================================================
// Simulation Result - Outcome of a single backtest simulation run
// ============================================================================

namespace IdiotProof.Models;

/// <summary>
/// Represents the result of a single backtest simulation.
/// </summary>
public sealed class SimulationResult
{
    /// <summary>The strategy parameters used in this simulation.</summary>
    public required StrategyParameters Parameters { get; init; }

    /// <summary>List of all trades executed during the simulation.</summary>
    public List<Trade> Trades { get; init; } = [];

    // ========================================================================
    // Performance Metrics
    // ========================================================================

    /// <summary>Total profit/loss from all trades.</summary>
    public double TotalPnL => Trades.Sum(t => t.PnL);

    /// <summary>Total profit/loss percentage.</summary>
    public double TotalPnLPercent => Trades.Count > 0 
        ? Trades.Sum(t => t.PnLPercent) 
        : 0;

    /// <summary>Number of winning trades.</summary>
    public int WinCount => Trades.Count(t => t.PnL > 0);

    /// <summary>Number of losing trades.</summary>
    public int LossCount => Trades.Count(t => t.PnL < 0);

    /// <summary>Win rate as a percentage.</summary>
    public double WinRate => Trades.Count > 0 
        ? (double)WinCount / Trades.Count * 100 
        : 0;

    /// <summary>Average profit on winning trades.</summary>
    public double AvgWin => WinCount > 0 
        ? Trades.Where(t => t.PnL > 0).Average(t => t.PnL) 
        : 0;

    /// <summary>Average loss on losing trades.</summary>
    public double AvgLoss => LossCount > 0 
        ? Trades.Where(t => t.PnL < 0).Average(t => t.PnL) 
        : 0;

    /// <summary>Profit factor (gross profit / gross loss).</summary>
    public double ProfitFactor
    {
        get
        {
            var grossProfit = Trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
            var grossLoss = Math.Abs(Trades.Where(t => t.PnL < 0).Sum(t => t.PnL));
            return grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? double.MaxValue : 0;
        }
    }

    /// <summary>Maximum drawdown during the simulation.</summary>
    public double MaxDrawdown { get; set; }

    /// <summary>Maximum drawdown percentage.</summary>
    public double MaxDrawdownPercent { get; set; }

    /// <summary>Sharpe ratio (risk-adjusted return).</summary>
    public double SharpeRatio { get; set; }

    // ========================================================================
    // Display
    // ========================================================================

    public override string ToString()
    {
        return $"""
            +--------------------------------------------------+
            | SIMULATION RESULT                                |
            +--------------------------------------------------+
            | Trades:       {Trades.Count,5}                           |
            | Win Rate:     {WinRate,5:F1}%                          |
            | Total PnL:   ${TotalPnL,8:F2}                       |
            | Profit Factor: {ProfitFactor,5:F2}                        |
            | Max Drawdown: ${MaxDrawdown,8:F2}                      |
            +--------------------------------------------------+
            | Entry:  ${Parameters.EntryPrice,8:F2}                       |
            | TP:     ${Parameters.TakeProfitPrice,8:F2}                       |
            | SL:     ${Parameters.StopLossPrice,8:F2}                       |
            +--------------------------------------------------+
            """;
    }
}

/// <summary>
/// Represents a single trade in the simulation.
/// </summary>
public sealed class Trade
{
    /// <summary>Entry timestamp.</summary>
    public required DateTime EntryTime { get; init; }

    /// <summary>Exit timestamp.</summary>
    public required DateTime ExitTime { get; init; }

    /// <summary>Entry price.</summary>
    public required double EntryPrice { get; init; }

    /// <summary>Exit price.</summary>
    public required double ExitPrice { get; init; }

    /// <summary>Position size (shares).</summary>
    public required int Quantity { get; init; }

    /// <summary>Whether this was a long or short trade.</summary>
    public required bool IsLong { get; init; }

    /// <summary>Exit reason (TP hit, SL hit, EOD, etc.).</summary>
    public required ExitReason ExitReason { get; init; }

    /// <summary>Profit/Loss in dollars.</summary>
    public double PnL => IsLong 
        ? (ExitPrice - EntryPrice) * Quantity 
        : (EntryPrice - ExitPrice) * Quantity;

    /// <summary>Profit/Loss as percentage.</summary>
    public double PnLPercent => IsLong 
        ? (ExitPrice - EntryPrice) / EntryPrice * 100 
        : (EntryPrice - ExitPrice) / EntryPrice * 100;

    /// <summary>Duration of the trade.</summary>
    public TimeSpan Duration => ExitTime - EntryTime;

    public override string ToString() =>
        $"{EntryTime:HH:mm}->{ExitTime:HH:mm} " +
        $"{(IsLong ? "LONG" : "SHORT")} " +
        $"Entry:{EntryPrice:F2} Exit:{ExitPrice:F2} " +
        $"PnL:{(PnL >= 0 ? "+" : "")}{PnL:F2} ({ExitReason})";
}

/// <summary>
/// Reason for exiting a trade.
/// </summary>
public enum ExitReason
{
    TakeProfitHit,
    StopLossHit,
    TrailingStopHit,
    EndOfDay,
    ManualExit,
    ConditionMet
}
