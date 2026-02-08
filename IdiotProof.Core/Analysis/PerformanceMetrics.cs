// ============================================================================
// Performance Metrics - Advanced performance calculations
// ============================================================================

using IdiotProof.Models;

namespace IdiotProof.Analysis;

/// <summary>
/// Calculates advanced performance metrics for trading results.
/// </summary>
public sealed class PerformanceMetrics
{
    private readonly SimulationResult _result;
    private readonly double _initialCapital;

    public PerformanceMetrics(SimulationResult result, double initialCapital = 10000)
    {
        _result = result;
        _initialCapital = initialCapital;
    }

    // ========================================================================
    // Return Metrics
    // ========================================================================

    /// <summary>Total return in dollars.</summary>
    public double TotalReturn => _result.TotalPnL;

    /// <summary>Total return as percentage of initial capital.</summary>
    public double TotalReturnPercent => TotalReturn / _initialCapital * 100;

    /// <summary>Average return per trade.</summary>
    public double AvgReturnPerTrade => _result.Trades.Count > 0
        ? TotalReturn / _result.Trades.Count
        : 0;

    /// <summary>Best single trade.</summary>
    public double BestTrade => _result.Trades.Count > 0
        ? _result.Trades.Max(t => t.PnL)
        : 0;

    /// <summary>Worst single trade.</summary>
    public double WorstTrade => _result.Trades.Count > 0
        ? _result.Trades.Min(t => t.PnL)
        : 0;

    // ========================================================================
    // Risk Metrics
    // ========================================================================

    /// <summary>Maximum drawdown in dollars.</summary>
    public double MaxDrawdown => _result.MaxDrawdown;

    /// <summary>Maximum drawdown as percentage.</summary>
    public double MaxDrawdownPercent => _result.MaxDrawdownPercent;

    /// <summary>Calmar Ratio (annualized return / max drawdown).</summary>
    public double CalmarRatio => MaxDrawdown > 0
        ? TotalReturn / MaxDrawdown
        : TotalReturn > 0 ? double.MaxValue : 0;

    /// <summary>Standard deviation of returns.</summary>
    public double ReturnStdDev
    {
        get
        {
            if (_result.Trades.Count < 2) return 0;
            var pnls = _result.Trades.Select(t => t.PnL).ToList();
            double mean = pnls.Average();
            double variance = pnls.Sum(p => Math.Pow(p - mean, 2)) / (pnls.Count - 1);
            return Math.Sqrt(variance);
        }
    }

    /// <summary>Sharpe Ratio (assuming risk-free rate = 0 for intraday).</summary>
    public double SharpeRatio => ReturnStdDev > 0
        ? AvgReturnPerTrade / ReturnStdDev
        : 0;

    /// <summary>Sortino Ratio (downside deviation only).</summary>
    public double SortinoRatio
    {
        get
        {
            var losses = _result.Trades.Where(t => t.PnL < 0).Select(t => t.PnL).ToList();
            if (losses.Count == 0) return TotalReturn > 0 ? double.MaxValue : 0;

            double downsideDeviation = Math.Sqrt(losses.Sum(l => l * l) / losses.Count);
            return downsideDeviation > 0 ? AvgReturnPerTrade / downsideDeviation : 0;
        }
    }

    // ========================================================================
    // Trade Analysis
    // ========================================================================

    /// <summary>Win rate as percentage.</summary>
    public double WinRate => _result.WinRate;

    /// <summary>Profit factor.</summary>
    public double ProfitFactor => _result.ProfitFactor;

    /// <summary>Average winning trade.</summary>
    public double AvgWin => _result.AvgWin;

    /// <summary>Average losing trade.</summary>
    public double AvgLoss => _result.AvgLoss;

    /// <summary>Ratio of avg win to avg loss.</summary>
    public double PayoffRatio => AvgLoss != 0
        ? Math.Abs(AvgWin / AvgLoss)
        : AvgWin > 0 ? double.MaxValue : 0;

    /// <summary>Expectancy per trade.</summary>
    public double Expectancy
    {
        get
        {
            double winProb = WinRate / 100;
            double lossProb = 1 - winProb;
            return winProb * AvgWin + lossProb * AvgLoss;
        }
    }

    /// <summary>Maximum consecutive wins.</summary>
    public int MaxConsecutiveWins
    {
        get
        {
            int max = 0, current = 0;
            foreach (var trade in _result.Trades)
            {
                if (trade.PnL > 0) { current++; max = Math.Max(max, current); }
                else { current = 0; }
            }
            return max;
        }
    }

    /// <summary>Maximum consecutive losses.</summary>
    public int MaxConsecutiveLosses
    {
        get
        {
            int max = 0, current = 0;
            foreach (var trade in _result.Trades)
            {
                if (trade.PnL < 0) { current++; max = Math.Max(max, current); }
                else { current = 0; }
            }
            return max;
        }
    }

    // ========================================================================
    // Time Analysis
    // ========================================================================

    /// <summary>Average trade duration.</summary>
    public TimeSpan AvgTradeDuration => _result.Trades.Count > 0
        ? TimeSpan.FromMinutes(_result.Trades.Average(t => t.Duration.TotalMinutes))
        : TimeSpan.Zero;

    /// <summary>Longest trade duration.</summary>
    public TimeSpan LongestTrade => _result.Trades.Count > 0
        ? _result.Trades.Max(t => t.Duration)
        : TimeSpan.Zero;

    /// <summary>Shortest trade duration.</summary>
    public TimeSpan ShortestTrade => _result.Trades.Count > 0
        ? _result.Trades.Min(t => t.Duration)
        : TimeSpan.Zero;

    // ========================================================================
    // Exit Analysis
    // ========================================================================

    /// <summary>Count of trades by exit reason.</summary>
    public Dictionary<ExitReason, int> ExitReasonCounts =>
        _result.Trades
            .GroupBy(t => t.ExitReason)
            .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Most common exit reason.</summary>
    public ExitReason? MostCommonExitReason =>
        ExitReasonCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;

    // ========================================================================
    // Display
    // ========================================================================

    public override string ToString()
    {
        return $"""
            +==================================================================+
            | PERFORMANCE METRICS                                              |
            +==================================================================+
            | RETURNS                                                          |
            +------------------------------------------------------------------+
            | Total Return:      ${TotalReturn,10:F2} ({TotalReturnPercent:F2}%)
            | Avg Per Trade:     ${AvgReturnPerTrade,10:F2}
            | Best Trade:        ${BestTrade,10:F2}
            | Worst Trade:       ${WorstTrade,10:F2}
            +------------------------------------------------------------------+
            | RISK                                                             |
            +------------------------------------------------------------------+
            | Max Drawdown:      ${MaxDrawdown,10:F2} ({MaxDrawdownPercent:F1}%)
            | Return Std Dev:    ${ReturnStdDev,10:F2}
            | Sharpe Ratio:      {SharpeRatio,10:F2}
            | Sortino Ratio:     {SortinoRatio,10:F2}
            | Calmar Ratio:      {CalmarRatio,10:F2}
            +------------------------------------------------------------------+
            | TRADE ANALYSIS                                                   |
            +------------------------------------------------------------------+
            | Trades:            {_result.Trades.Count,10}
            | Win Rate:          {WinRate,10:F1}%
            | Profit Factor:     {ProfitFactor,10:F2}
            | Payoff Ratio:      {PayoffRatio,10:F2}
            | Expectancy:        ${Expectancy,10:F2}
            | Max Consec Wins:   {MaxConsecutiveWins,10}
            | Max Consec Losses: {MaxConsecutiveLosses,10}
            +------------------------------------------------------------------+
            | TIME                                                             |
            +------------------------------------------------------------------+
            | Avg Duration:      {AvgTradeDuration.TotalMinutes,10:F0} min
            | Longest Trade:     {LongestTrade.TotalMinutes,10:F0} min
            | Shortest Trade:    {ShortestTrade.TotalMinutes,10:F0} min
            +==================================================================+
            """;
    }

    /// <summary>
    /// Generates a compact summary line.
    /// </summary>
    public string ToCompactString() =>
        $"PnL: ${TotalReturn:F2} | WR: {WinRate:F0}% | PF: {ProfitFactor:F2} | " +
        $"Sharpe: {SharpeRatio:F2} | DD: {MaxDrawdownPercent:F1}%";
}
