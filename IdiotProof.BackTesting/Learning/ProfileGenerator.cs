// ============================================================================
// Profile Generator - Extracts patterns from backtest results
// ============================================================================
//
// Analyzes backtest simulations and generates/updates TickerProfiles
// that can be used by AutonomousTrading to improve decisions.
//
// ============================================================================

using IdiotProof.BackTesting.Analysis;
using IdiotProof.BackTesting.Models;
using IdiotProof.BackTesting.Services;

namespace IdiotProof.BackTesting.Learning;

/// <summary>
/// Generates ticker profiles from backtest results.
/// </summary>
public sealed class ProfileGenerator
{
    private readonly TickerProfileManager _profileManager;

    public ProfileGenerator(TickerProfileManager? profileManager = null)
    {
        _profileManager = profileManager ?? new TickerProfileManager();
    }

    /// <summary>
    /// Generates or updates a profile from autonomous simulation results.
    /// </summary>
    public TickerProfile GenerateFromAutonomousSimulation(
        AutonomousSimulationResult simulation,
        BackTestSession session)
    {
        var profile = _profileManager.GetOrCreate(simulation.Symbol);
        var indicators = new IndicatorCalculator(session.Candles);

        // Pre-calculate indicators for the session
        var rsi = indicators.CalculateRsi(14);
        var adxData = indicators.CalculateAdx(14);
        var macdData = indicators.CalculateMacd(12, 26, 9);
        var volumeRatio = indicators.CalculateVolumeRatio(20);
        var ema9 = indicators.CalculateEma(9);
        var ema21 = indicators.CalculateEma(21);

        // Convert each autonomous trade to a learning record
        foreach (var trade in simulation.Trades)
        {
            // Find the candle index for entry
            int entryIndex = FindCandleIndex(session.Candles, trade.EntryTime);
            if (entryIndex < 0) continue;

            var entryCandle = session.Candles[entryIndex];

            var record = new TradeRecord
            {
                EntryTime = trade.EntryTime,
                ExitTime = trade.ExitTime,
                EntryPrice = trade.EntryPrice,
                ExitPrice = trade.ExitPrice,
                EntryScore = trade.EntryScore.TotalScore,
                ExitScore = trade.ExitScore.TotalScore,
                IsLong = trade.IsLong,
                IsWin = trade.PnL > 0,
                PnL = trade.PnL,
                PnLPercent = trade.PnLPercent,

                // Indicator values at entry
                RsiAtEntry = entryIndex < rsi.Length ? rsi[entryIndex] : 50,
                AdxAtEntry = entryIndex < adxData.adx.Length ? adxData.adx[entryIndex] : 0,
                MacdHistogramAtEntry = entryIndex < macdData.histogram.Length ? macdData.histogram[entryIndex] : 0,
                VolumeRatioAtEntry = entryIndex < volumeRatio.Length ? volumeRatio[entryIndex] : 1,
                AboveVwapAtEntry = entryCandle.Close >= entryCandle.Vwap,
                AboveEma9AtEntry = entryIndex < ema9.Length && entryCandle.Close > ema9[entryIndex],
                AboveEma21AtEntry = entryIndex < ema21.Length && entryCandle.Close > ema21[entryIndex]
            };

            profile.AddTrade(record);
        }

        // Recalculate all statistics
        profile.RecalculateStatistics();

        // Save the updated profile
        _profileManager.Save(profile);

        return profile;
    }

    /// <summary>
    /// Generates or updates a profile from optimization results.
    /// </summary>
    public TickerProfile GenerateFromOptimization(
        List<Optimization.OptimizationResult> results,
        BackTestSession session)
    {
        var profile = _profileManager.GetOrCreate(session.Symbol);
        var indicators = new IndicatorCalculator(session.Candles);

        // Pre-calculate indicators
        var rsi = indicators.CalculateRsi(14);
        var adxData = indicators.CalculateAdx(14);
        var macdData = indicators.CalculateMacd(12, 26, 9);
        var volumeRatio = indicators.CalculateVolumeRatio(20);
        var ema9 = indicators.CalculateEma(9);
        var ema21 = indicators.CalculateEma(21);

        // Process trades from top strategies
        foreach (var result in results.Take(5))  // Top 5 strategies
        {
            foreach (var trade in result.SimulationResult.Trades)
            {
                int entryIndex = FindCandleIndex(session.Candles, trade.EntryTime);
                if (entryIndex < 0) continue;

                var entryCandle = session.Candles[entryIndex];

                // Estimate score based on indicators (we don't have actual scores from optimization)
                double estimatedScore = EstimateScore(
                    entryCandle, rsi, adxData, macdData, volumeRatio, ema9, ema21, entryIndex);

                var record = new TradeRecord
                {
                    EntryTime = trade.EntryTime,
                    ExitTime = trade.ExitTime,
                    EntryPrice = trade.EntryPrice,
                    ExitPrice = trade.ExitPrice,
                    EntryScore = estimatedScore,
                    ExitScore = 0,  // Not available from optimization
                    IsLong = trade.IsLong,
                    IsWin = trade.PnL > 0,
                    PnL = trade.PnL,
                    PnLPercent = trade.PnLPercent,

                    RsiAtEntry = entryIndex < rsi.Length ? rsi[entryIndex] : 50,
                    AdxAtEntry = entryIndex < adxData.adx.Length ? adxData.adx[entryIndex] : 0,
                    MacdHistogramAtEntry = entryIndex < macdData.histogram.Length ? macdData.histogram[entryIndex] : 0,
                    VolumeRatioAtEntry = entryIndex < volumeRatio.Length ? volumeRatio[entryIndex] : 1,
                    AboveVwapAtEntry = entryCandle.Close >= entryCandle.Vwap,
                    AboveEma9AtEntry = entryIndex < ema9.Length && entryCandle.Close > ema9[entryIndex],
                    AboveEma21AtEntry = entryIndex < ema21.Length && entryCandle.Close > ema21[entryIndex]
                };

                profile.AddTrade(record);
            }
        }

        profile.RecalculateStatistics();
        _profileManager.Save(profile);

        return profile;
    }

    /// <summary>
    /// Analyzes multiple days of data and builds a comprehensive profile.
    /// </summary>
    public async Task<TickerProfile> GenerateFromMultipleDays(
        string symbol,
        IHistoricalDataProvider dataProvider,
        List<DateOnly> dates,
        AutonomousConfig? config = null,
        IProgress<string>? progress = null)
    {
        var profile = _profileManager.GetOrCreate(symbol);
        config ??= new AutonomousConfig { Mode = AutonomousMode.Balanced };

        int processed = 0;
        foreach (var date in dates)
        {
            try
            {
                progress?.Report($"Processing {symbol} on {date:yyyy-MM-dd}...");

                var session = await dataProvider.LoadSessionAsync(symbol, date);
                var simulator = new AutonomousTradeSimulator(session, config);
                var result = simulator.Simulate();

                // Add trades to profile
                var indicators = new IndicatorCalculator(session.Candles);
                var rsi = indicators.CalculateRsi(14);
                var adxData = indicators.CalculateAdx(14);
                var macdData = indicators.CalculateMacd(12, 26, 9);
                var volumeRatio = indicators.CalculateVolumeRatio(20);
                var ema9 = indicators.CalculateEma(9);
                var ema21 = indicators.CalculateEma(21);

                foreach (var trade in result.Trades)
                {
                    int entryIndex = FindCandleIndex(session.Candles, trade.EntryTime);
                    if (entryIndex < 0) continue;

                    var entryCandle = session.Candles[entryIndex];

                    var record = new TradeRecord
                    {
                        EntryTime = trade.EntryTime,
                        ExitTime = trade.ExitTime,
                        EntryPrice = trade.EntryPrice,
                        ExitPrice = trade.ExitPrice,
                        EntryScore = trade.EntryScore.TotalScore,
                        ExitScore = trade.ExitScore.TotalScore,
                        IsLong = trade.IsLong,
                        IsWin = trade.PnL > 0,
                        PnL = trade.PnL,
                        PnLPercent = trade.PnLPercent,

                        RsiAtEntry = entryIndex < rsi.Length ? rsi[entryIndex] : 50,
                        AdxAtEntry = entryIndex < adxData.adx.Length ? adxData.adx[entryIndex] : 0,
                        MacdHistogramAtEntry = entryIndex < macdData.histogram.Length ? macdData.histogram[entryIndex] : 0,
                        VolumeRatioAtEntry = entryIndex < volumeRatio.Length ? volumeRatio[entryIndex] : 1,
                        AboveVwapAtEntry = entryCandle.Close >= entryCandle.Vwap,
                        AboveEma9AtEntry = entryIndex < ema9.Length && entryCandle.Close > ema9[entryIndex],
                        AboveEma21AtEntry = entryIndex < ema21.Length && entryCandle.Close > ema21[entryIndex]
                    };

                    profile.AddTrade(record);
                }

                processed++;
                progress?.Report($"  -> {result.Trades.Count} trades, PnL: ${result.TotalPnL:F2}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  -> Error: {ex.Message}");
            }
        }

        profile.RecalculateStatistics();
        _profileManager.Save(profile);

        progress?.Report($"Profile complete. {processed} days processed, {profile.TotalTrades} total trades.");

        return profile;
    }

    /// <summary>
    /// Prints a detailed analysis report for a profile.
    /// </summary>
    public string GenerateProfileReport(TickerProfile profile)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(profile.ToString());

        // Indicator correlations
        if (profile.IndicatorCorrelations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| INDICATOR CORRELATIONS                                           |");
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| Indicator           | With   | Without | Correlation            |");
            sb.AppendLine("|---------------------|--------|---------|------------------------|");

            foreach (var corr in profile.IndicatorCorrelations.OrderByDescending(c => Math.Abs(c.Correlation)))
            {
                string impact = corr.Correlation > 10 ? "HELPS ++" :
                               corr.Correlation > 5 ? "HELPS +" :
                               corr.Correlation < -10 ? "HURTS --" :
                               corr.Correlation < -5 ? "HURTS -" : "NEUTRAL";

                sb.AppendLine($"| {corr.IndicatorName,-19} | {corr.WinRateWith,5:F1}% | {corr.WinRateWithout,6:F1}% | {corr.Correlation,+6:F1}% {impact,-8} |");
            }

            sb.AppendLine("+------------------------------------------------------------------+");
        }

        // Score range analysis
        if (profile.ScoreRangeStats.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| SCORE RANGE ANALYSIS                                             |");
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| Score Range  | Trades | Win Rate | Avg PnL                      |");
            sb.AppendLine("|--------------|--------|----------|------------------------------|");

            foreach (var range in profile.ScoreRangeStats.OrderByDescending(r => r.MinScore))
            {
                string quality = range.WinRate >= 60 ? "[OK]" :
                                range.WinRate >= 45 ? "[* ]" : "[! ]";

                sb.AppendLine($"| {range.RangeLabel,-12} | {range.TradeCount,6} | {range.WinRate,7:F1}% | ${range.AvgPnL,8:F2} {quality}         |");
            }

            sb.AppendLine("+------------------------------------------------------------------+");
        }

        // Hourly analysis
        if (profile.HourlyStats.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| HOURLY PERFORMANCE                                               |");
            sb.AppendLine("+------------------------------------------------------------------+");

            foreach (var hourStat in profile.HourlyStats.Where(h => h.TradeCount >= 2))
            {
                string bar = new string('=', (int)(hourStat.WinRate / 5));
                string status = hourStat.WinRate >= 60 ? "[OK]" :
                               hourStat.WinRate < 40 ? "[! ]" : "[* ]";

                sb.AppendLine($"| {hourStat.Hour,2}:00 | {hourStat.TradeCount,3} trades | {hourStat.WinRate,5:F1}% | {bar,-20} {status}");
            }

            sb.AppendLine("+------------------------------------------------------------------+");
        }

        // Recommendations
        sb.AppendLine();
        sb.AppendLine("+------------------------------------------------------------------+");
        sb.AppendLine("| RECOMMENDATIONS                                                  |");
        sb.AppendLine("+------------------------------------------------------------------+");

        if (profile.Confidence < 0.3)
        {
            sb.AppendLine("| [!] Low confidence - need more trades for reliable patterns.    |");
        }
        else
        {
            // Entry threshold recommendation
            if (profile.OptimalLongEntryThreshold > 75)
                sb.AppendLine($"| [*] Use higher entry threshold ({profile.OptimalLongEntryThreshold:F0}+) for this ticker.   |");
            else if (profile.OptimalLongEntryThreshold < 65)
                sb.AppendLine($"| [*] Lower entry threshold ({profile.OptimalLongEntryThreshold:F0}+) works for this ticker.  |");

            // Best hours
            if (profile.BestHours.Count > 0)
                sb.AppendLine($"| [*] Best trading hours: {string.Join(", ", profile.BestHours.Select(h => $"{h}:00"))}");

            // Hours to avoid
            if (profile.AvoidHours.Count > 0)
                sb.AppendLine($"| [!] Avoid trading at: {string.Join(", ", profile.AvoidHours.Select(h => $"{h}:00"))}");

            // Top correlations
            var bestIndicator = profile.IndicatorCorrelations
                .OrderByDescending(c => c.Correlation)
                .FirstOrDefault();

            if (bestIndicator != null && bestIndicator.Correlation > 10)
                sb.AppendLine($"| [*] '{bestIndicator.IndicatorName}' strongly predicts wins (+{bestIndicator.Correlation:F0}%)");
        }

        sb.AppendLine("+------------------------------------------------------------------+");

        return sb.ToString();
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static int FindCandleIndex(List<BackTestCandle> candles, DateTime time)
    {
        for (int i = 0; i < candles.Count; i++)
        {
            if (candles[i].Timestamp >= time)
                return i;
        }
        return -1;
    }

    private static double EstimateScore(
        BackTestCandle candle,
        double[] rsi,
        (double[] adx, double[] plusDi, double[] minusDi) adxData,
        (double[] macd, double[] signal, double[] histogram) macdData,
        double[] volumeRatio,
        double[] ema9,
        double[] ema21,
        int index)
    {
        double score = 0;

        // VWAP
        if (candle.Vwap > 0)
        {
            double vwapDist = (candle.Close - candle.Vwap) / candle.Vwap * 100;
            score += Math.Clamp(vwapDist * 20, -15, 15);  // 15% weight
        }

        // EMA
        int aboveCount = 0;
        if (index < ema9.Length && candle.Close > ema9[index]) aboveCount++;
        if (index < ema21.Length && candle.Close > ema21[index]) aboveCount++;
        score += (aboveCount - 1) * 20;  // 20% weight

        // RSI
        if (index < rsi.Length)
        {
            if (rsi[index] <= 30) score += 15;
            else if (rsi[index] >= 70) score -= 15;
        }

        // MACD
        if (index < macdData.macd.Length)
        {
            if (macdData.macd[index] > macdData.signal[index]) score += 20;
            else score -= 20;
        }

        // ADX
        if (index < adxData.adx.Length)
        {
            if (adxData.plusDi[index] > adxData.minusDi[index])
                score += Math.Min(adxData.adx[index] / 50, 1) * 20;
            else
                score -= Math.Min(adxData.adx[index] / 50, 1) * 20;
        }

        // Volume
        if (index < volumeRatio.Length && volumeRatio[index] > 1.5)
        {
            if (candle.Close > candle.Vwap) score += 10;
            else score -= 10;
        }

        return Math.Clamp(score, -100, 100);
    }
}
