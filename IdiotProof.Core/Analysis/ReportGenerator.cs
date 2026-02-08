// ============================================================================
// Report Generator - Creates detailed backtest analysis reports
// ============================================================================

using IdiotProof.Models;
using IdiotProof.Optimization;

namespace IdiotProof.Analysis;

/// <summary>
/// Generates detailed analysis reports from backtest results.
/// </summary>
public sealed class ReportGenerator
{
    private readonly BackTestSession _session;
    private readonly List<OptimizationResult> _results;

    public ReportGenerator(BackTestSession session, List<OptimizationResult> results)
    {
        _session = session;
        _results = results;
    }

    /// <summary>
    /// Generates a complete text report.
    /// </summary>
    public string GenerateTextReport()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(GenerateHeader());
        sb.AppendLine(GenerateSessionSummary());
        sb.AppendLine(GeneratePriceAnalysis());
        sb.AppendLine(GenerateTopStrategies());
        sb.AppendLine(GenerateTradeAnalysis());
        sb.AppendLine(GenerateRecommendations());

        return sb.ToString();
    }

    private string GenerateHeader()
    {
        return $"""
            +==================================================================+
            |                                                                  |
            |     BACKTEST ANALYSIS REPORT                                     |
            |     {_session.Symbol} - {_session.Date:yyyy-MM-dd}
            |                                                                  |
            +==================================================================+
            | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            +==================================================================+
            """;
    }

    private string GenerateSessionSummary()
    {
        return $"""

            +------------------------------------------------------------------+
            | SESSION SUMMARY                                                  |
            +------------------------------------------------------------------+
            | Symbol:     {_session.Symbol,-20}
            | Date:       {_session.Date:yyyy-MM-dd}
            | Open:       ${_session.Open,10:F2}
            | High:       ${_session.High,10:F2}
            | Low:        ${_session.Low,10:F2}
            | Close:      ${_session.Close,10:F2}
            | Range:      ${_session.Range,10:F2} ({_session.Range / _session.Open * 100:F2}%)
            | Change:     {(_session.Change >= 0 ? "+" : "")}{_session.ChangePercent:F2}%
            | Volume:     {_session.TotalVolume,12:N0}
            | Candles:    {_session.CandleCount,6}
            +------------------------------------------------------------------+
            """;
    }

    private string GeneratePriceAnalysis()
    {
        var candles = _session.Candles;
        if (candles.Count == 0) return "";

        // Find key levels
        var lodCandle = candles.MinBy(c => c.Low)!;
        var hodCandle = candles.MaxBy(c => c.High)!;
        var vwapCrosses = CountVwapCrosses(candles);
        var avgRange = candles.Average(c => c.Range);

        return $"""

            +------------------------------------------------------------------+
            | PRICE ANALYSIS                                                   |
            +------------------------------------------------------------------+
            | Low of Day (LOD):
            |   Price:    ${lodCandle.Low,10:F2}
            |   Time:     {lodCandle.Timestamp:HH:mm}
            |
            | High of Day (HOD):
            |   Price:    ${hodCandle.High,10:F2}
            |   Time:     {hodCandle.Timestamp:HH:mm}
            |
            | VWAP Analysis:
            |   VWAP Crosses: {vwapCrosses,5}
            |   Final VWAP:   ${candles[^1].Vwap,10:F2}
            |   Price vs VWAP: {(candles[^1].Close >= candles[^1].Vwap ? "ABOVE" : "BELOW")}
            |
            | Volatility:
            |   Avg Bar Range: ${avgRange,8:F2}
            |   Avg % Range:   {avgRange / _session.Open * 100,6:F3}%
            +------------------------------------------------------------------+
            """;
    }

    private string GenerateTopStrategies()
    {
        if (_results.Count == 0)
        {
            return """

            +------------------------------------------------------------------+
            | TOP STRATEGIES                                                   |
            +------------------------------------------------------------------+
            | No profitable strategies found for this session.
            +------------------------------------------------------------------+
            """;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("+------------------------------------------------------------------+");
        sb.AppendLine("| TOP STRATEGIES                                                   |");
        sb.AppendLine("+------------------------------------------------------------------+");

        foreach (var result in _results.Take(5))
        {
            var p = result.Parameters;
            var s = result.SimulationResult;

            sb.AppendLine($"|");
            sb.AppendLine($"| #{result.Rank} - Score: {result.Score:F1}");
            sb.AppendLine($"|   Entry:     ${p.EntryPrice,8:F2}");
            sb.AppendLine($"|   TP:        ${p.TakeProfitPrice,8:F2} (+{p.TakeProfitPercent:F1}%)");
            sb.AppendLine($"|   SL:        ${p.StopLossPrice,8:F2} (-{p.StopLossPercent:F1}%)");
            sb.AppendLine($"|   R:R Ratio: {p.RiskRewardRatio,8:F2}");
            sb.AppendLine($"|   Trades:    {s.Trades.Count,8}");
            sb.AppendLine($"|   Win Rate:  {s.WinRate,7:F1}%");
            sb.AppendLine($"|   Total PnL: ${s.TotalPnL,8:F2}");
            sb.AppendLine($"|   PF:        {s.ProfitFactor,8:F2}");

            if (p.TrailingStopPercent.HasValue)
                sb.AppendLine($"|   TSL:       {p.TrailingStopPercent:F0}%");

            var flags = new List<string>();
            if (p.RequireAboveVwap) flags.Add("VWAP+");
            if (p.RequireHigherLows) flags.Add("HL+");
            if (flags.Count > 0)
                sb.AppendLine($"|   Filters:   {string.Join(", ", flags)}");
        }

        sb.AppendLine("+------------------------------------------------------------------+");
        return sb.ToString();
    }

    private string GenerateTradeAnalysis()
    {
        if (_results.Count == 0) return "";

        var best = _results.First();
        var trades = best.SimulationResult.Trades;

        if (trades.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("+------------------------------------------------------------------+");
        sb.AppendLine("| TRADE ANALYSIS (Best Strategy)                                   |");
        sb.AppendLine("+------------------------------------------------------------------+");

        foreach (var trade in trades)
        {
            sb.AppendLine($"| {trade.EntryTime:HH:mm} -> {trade.ExitTime:HH:mm} | " +
                $"${trade.EntryPrice:F2} -> ${trade.ExitPrice:F2} | " +
                $"{(trade.PnL >= 0 ? "+" : "")}{trade.PnL:F2} | {trade.ExitReason}");
        }

        sb.AppendLine("+------------------------------------------------------------------+");

        // Trade timing analysis
        var avgDuration = TimeSpan.FromMinutes(trades.Average(t => t.Duration.TotalMinutes));
        var winningTrades = trades.Where(t => t.PnL > 0).ToList();
        var losingTrades = trades.Where(t => t.PnL < 0).ToList();

        sb.AppendLine($"| Avg Duration:    {avgDuration.TotalMinutes:F0} minutes");
        if (winningTrades.Count > 0)
            sb.AppendLine($"| Avg Win:         ${winningTrades.Average(t => t.PnL):F2}");
        if (losingTrades.Count > 0)
            sb.AppendLine($"| Avg Loss:        ${losingTrades.Average(t => t.PnL):F2}");
        sb.AppendLine("+------------------------------------------------------------------+");

        return sb.ToString();
    }

    private string GenerateRecommendations()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("+------------------------------------------------------------------+");
        sb.AppendLine("| RECOMMENDATIONS                                                  |");
        sb.AppendLine("+------------------------------------------------------------------+");

        if (_results.Count == 0)
        {
            sb.AppendLine("| * Day may have been range-bound or choppy");
            sb.AppendLine("| * Consider widening entry/exit parameters");
            sb.AppendLine("| * Review for potential short strategies");
        }
        else
        {
            var best = _results.First();
            var p = best.Parameters;

            // Risk/Reward analysis
            if (p.RiskRewardRatio >= 2.0)
                sb.AppendLine("| [OK] Good R:R ratio (>= 2:1)");
            else
                sb.AppendLine("| [!] Consider improving R:R ratio");

            // Win rate analysis
            if (best.SimulationResult.WinRate >= 60)
                sb.AppendLine("| [OK] Strong win rate (>= 60%)");
            else if (best.SimulationResult.WinRate >= 40)
                sb.AppendLine("| [*] Moderate win rate - acceptable with good R:R");
            else
                sb.AppendLine("| [!] Low win rate - review entry conditions");

            // Profit factor
            if (best.SimulationResult.ProfitFactor >= 2.0)
                sb.AppendLine("| [OK] Excellent profit factor (>= 2.0)");
            else if (best.SimulationResult.ProfitFactor >= 1.5)
                sb.AppendLine("| [*] Acceptable profit factor");
            else
                sb.AppendLine("| [!] Low profit factor - review exits");

            // VWAP filter
            if (p.RequireAboveVwap)
                sb.AppendLine("| [*] Strategy uses VWAP filter");
            else
                sb.AppendLine("| [?] Consider adding VWAP filter for trend confirmation");

            // Trailing stop
            if (p.TrailingStopPercent.HasValue)
                sb.AppendLine($"| [*] Trailing stop at {p.TrailingStopPercent:F0}% helps protect gains");
            else
                sb.AppendLine("| [?] Consider adding trailing stop");
        }

        sb.AppendLine("+------------------------------------------------------------------+");
        return sb.ToString();
    }

    private static int CountVwapCrosses(List<BackTestCandle> candles)
    {
        int crosses = 0;
        bool wasAbove = candles[0].Close >= candles[0].Vwap;

        for (int i = 1; i < candles.Count; i++)
        {
            bool isAbove = candles[i].Close >= candles[i].Vwap;
            if (isAbove != wasAbove)
            {
                crosses++;
                wasAbove = isAbove;
            }
        }

        return crosses;
    }

    /// <summary>
    /// Saves the report to a file.
    /// </summary>
    public async Task SaveReportAsync(string filePath)
    {
        var report = GenerateTextReport();
        await File.WriteAllTextAsync(filePath, report);
    }
}
