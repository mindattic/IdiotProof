// ============================================================================
// Strategy Comparison - Compare multiple strategies side-by-side
// ============================================================================

using IdiotProof.BackTesting.Models;
using IdiotProof.BackTesting.Optimization;

namespace IdiotProof.BackTesting.Analysis;

/// <summary>
/// Compares multiple strategies and provides comparative analysis.
/// </summary>
public sealed class StrategyComparison
{
    private readonly List<OptimizationResult> _strategies;
    private readonly BackTestSession _session;

    public StrategyComparison(BackTestSession session, List<OptimizationResult> strategies)
    {
        _session = session;
        _strategies = strategies;
    }

    /// <summary>
    /// Generates a comparison table.
    /// </summary>
    public string GenerateComparisonTable()
    {
        if (_strategies.Count == 0)
            return "No strategies to compare.";

        var sb = new System.Text.StringBuilder();

        // Header
        sb.AppendLine("+==================================================================+");
        sb.AppendLine("| STRATEGY COMPARISON                                              |");
        sb.AppendLine("+==================================================================+");
        sb.AppendLine();

        // Comparison table
        sb.AppendLine($"| {"Rank",-4} | {"Entry",-8} | {"TP%",-6} | {"SL%",-6} | {"Win%",-6} | {"PnL",-10} | {"PF",-6} | {"R:R",-5} |");
        sb.AppendLine($"|{new string('-', 6)}|{new string('-', 10)}|{new string('-', 8)}|{new string('-', 8)}|{new string('-', 8)}|{new string('-', 12)}|{new string('-', 8)}|{new string('-', 7)}|");

        foreach (var s in _strategies.Take(10))
        {
            var p = s.Parameters;
            var r = s.SimulationResult;
            sb.AppendLine($"| #{s.Rank,-3} | ${p.EntryPrice,-6:F2} | {p.TakeProfitPercent,5:F1}% | {p.StopLossPercent,5:F1}% | {r.WinRate,5:F1}% | ${r.TotalPnL,8:F2} | {r.ProfitFactor,5:F2} | {p.RiskRewardRatio,4:F1} |");
        }

        sb.AppendLine($"|{new string('-', 6)}|{new string('-', 10)}|{new string('-', 8)}|{new string('-', 8)}|{new string('-', 8)}|{new string('-', 12)}|{new string('-', 8)}|{new string('-', 7)}|");

        // Summary statistics
        sb.AppendLine();
        sb.AppendLine("| SUMMARY STATISTICS                                               |");
        sb.AppendLine("+------------------------------------------------------------------+");

        var avgPnL = _strategies.Average(s => s.SimulationResult.TotalPnL);
        var avgWinRate = _strategies.Average(s => s.SimulationResult.WinRate);
        var avgPF = _strategies.Where(s => s.SimulationResult.ProfitFactor < double.MaxValue)
                              .Average(s => s.SimulationResult.ProfitFactor);
        var avgRR = _strategies.Average(s => s.Parameters.RiskRewardRatio);

        sb.AppendLine($"| Strategies Compared: {_strategies.Count,6}");
        sb.AppendLine($"| Average PnL:        ${avgPnL,10:F2}");
        sb.AppendLine($"| Average Win Rate:   {avgWinRate,10:F1}%");
        sb.AppendLine($"| Average PF:         {avgPF,10:F2}");
        sb.AppendLine($"| Average R:R:        {avgRR,10:F2}");
        sb.AppendLine("+------------------------------------------------------------------+");

        return sb.ToString();
    }

    /// <summary>
    /// Finds common characteristics among top strategies.
    /// </summary>
    public string AnalyzeCommonPatterns()
    {
        if (_strategies.Count < 3)
            return "Need at least 3 strategies for pattern analysis.";

        var sb = new System.Text.StringBuilder();

        sb.AppendLine();
        sb.AppendLine("+------------------------------------------------------------------+");
        sb.AppendLine("| COMMON PATTERNS IN TOP STRATEGIES                                |");
        sb.AppendLine("+------------------------------------------------------------------+");

        var top = _strategies.Take(5).ToList();

        // Entry price range
        var minEntry = top.Min(s => s.Parameters.EntryPrice);
        var maxEntry = top.Max(s => s.Parameters.EntryPrice);
        sb.AppendLine($"| Entry Price Range: ${minEntry:F2} - ${maxEntry:F2}");

        // TP% range
        var minTP = top.Min(s => s.Parameters.TakeProfitPercent);
        var maxTP = top.Max(s => s.Parameters.TakeProfitPercent);
        sb.AppendLine($"| Take Profit Range: {minTP:F1}% - {maxTP:F1}%");

        // SL% range
        var minSL = top.Min(s => s.Parameters.StopLossPercent);
        var maxSL = top.Max(s => s.Parameters.StopLossPercent);
        sb.AppendLine($"| Stop Loss Range:   {minSL:F1}% - {maxSL:F1}%");

        // Common filters
        var vwapUsage = top.Count(s => s.Parameters.RequireAboveVwap);
        var hlUsage = top.Count(s => s.Parameters.RequireHigherLows);
        var tslUsage = top.Count(s => s.Parameters.TrailingStopPercent.HasValue);

        sb.AppendLine();
        sb.AppendLine($"| VWAP Filter Used:  {vwapUsage}/{top.Count} strategies ({vwapUsage * 100.0 / top.Count:F0}%)");
        sb.AppendLine($"| Higher Lows Used:  {hlUsage}/{top.Count} strategies ({hlUsage * 100.0 / top.Count:F0}%)");
        sb.AppendLine($"| Trailing Stop:     {tslUsage}/{top.Count} strategies ({tslUsage * 100.0 / top.Count:F0}%)");

        // Recommendations
        sb.AppendLine();
        sb.AppendLine("| RECOMMENDATIONS                                                  |");
        sb.AppendLine("+------------------------------------------------------------------+");

        if (vwapUsage >= top.Count * 0.6)
            sb.AppendLine("| [*] VWAP filter is important - keep it enabled.");
        if (hlUsage >= top.Count * 0.6)
            sb.AppendLine("| [*] Higher lows pattern adds value to entry.");
        if (tslUsage >= top.Count * 0.6)
            sb.AppendLine("| [*] Trailing stop helps capture larger moves.");

        // Optimal ranges
        var bestRR = top.Max(s => s.Parameters.RiskRewardRatio);
        if (bestRR >= 2.0)
            sb.AppendLine($"| [*] Best R:R ratio is {bestRR:F1}:1 - maintain this target.");

        sb.AppendLine("+------------------------------------------------------------------+");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a risk-adjusted ranking.
    /// </summary>
    public List<(int OriginalRank, double RiskAdjustedScore, string Reason)> GetRiskAdjustedRanking()
    {
        var results = new List<(int OriginalRank, double RiskAdjustedScore, string Reason)>();

        foreach (var s in _strategies)
        {
            var sim = s.SimulationResult;
            var p = s.Parameters;

            // Risk-adjusted scoring
            // Penalize low win rates, high drawdowns, low profit factors
            double baseScore = s.Score;

            // Win rate adjustment
            double winRateMultiplier = sim.WinRate >= 50 ? 1.0 : 0.8;

            // Drawdown penalty
            double ddMultiplier = sim.MaxDrawdownPercent <= 20 ? 1.0 :
                                  sim.MaxDrawdownPercent <= 50 ? 0.85 : 0.7;

            // Profit factor bonus
            double pfMultiplier = sim.ProfitFactor >= 2.0 ? 1.1 :
                                  sim.ProfitFactor >= 1.5 ? 1.0 : 0.9;

            // R:R bonus
            double rrMultiplier = p.RiskRewardRatio >= 2.0 ? 1.1 :
                                  p.RiskRewardRatio >= 1.5 ? 1.0 : 0.9;

            double adjustedScore = baseScore * winRateMultiplier * ddMultiplier * pfMultiplier * rrMultiplier;

            var reasons = new List<string>();
            if (winRateMultiplier < 1) reasons.Add("low win rate");
            if (ddMultiplier < 1) reasons.Add("high drawdown");
            if (pfMultiplier < 1) reasons.Add("low PF");
            if (rrMultiplier < 1) reasons.Add("low R:R");
            if (pfMultiplier > 1) reasons.Add("high PF");
            if (rrMultiplier > 1) reasons.Add("good R:R");

            string reason = reasons.Count > 0 ? string.Join(", ", reasons) : "balanced";

            results.Add((s.Rank, adjustedScore, reason));
        }

        return results.OrderByDescending(r => r.RiskAdjustedScore).ToList();
    }
}
