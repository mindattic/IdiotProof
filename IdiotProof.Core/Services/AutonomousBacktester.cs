// ============================================================================
// Autonomous Backtester - Full-Day Trading Simulation with Capital Tracking
// ============================================================================
//
// Simulates AutonomousTrading against a full trading day (4:00 AM - 8:00 PM EST)
// with realistic capital management, position sizing, and detailed trade analysis.
//
// USAGE:
//   var backtester = new AutonomousBacktester(historicalDataService);
//   var result = await backtester.RunAsync("NVDA", new DateOnly(2025, 12, 15), 1000.00m);
//   Console.WriteLine(result);
//
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Models;
using IdiotProof.BackTesting.Models;
using System.Text;

// Use the AutonomousMode from BackTesting.Services
using AutonomousMode = IdiotProof.BackTesting.Services.AutonomousMode;

namespace IdiotProof.Backend.Services;

/// <summary>
/// Configuration for autonomous backtesting with capital tracking.
/// </summary>
public sealed record AutonomousBacktestConfig
{
    /// <summary>Starting capital in dollars.</summary>
    public decimal StartingCapital { get; init; } = 1000.00m;

    /// <summary>Trading mode (Conservative, Balanced, Aggressive, Optimized).</summary>
    public AutonomousMode Mode { get; init; } = AutonomousMode.Balanced;

    /// <summary>Allow short positions.</summary>
    public bool AllowShort { get; init; } = true;

    /// <summary>Allow flipping from long to short (and vice versa).</summary>
    public bool AllowDirectionFlip { get; init; } = true;

    /// <summary>Minimum seconds between trades.</summary>
    public int MinSecondsBetweenTrades { get; init; } = 60; // Increased to avoid overtrading

    /// <summary>ATR multiplier for take profit calculation.</summary>
    public double TakeProfitAtrMultiplier { get; init; } = 1.5; // Tighter TP to lock in gains

    /// <summary>ATR multiplier for stop loss calculation.</summary>
    public double StopLossAtrMultiplier { get; init; } = 2.5; // Wider SL to avoid whipsaws

    /// <summary>Require trend alignment for entries (price above/below EMA).</summary>
    public bool RequireTrendAlignment { get; init; } = true;

    /// <summary>Require volume confirmation (above average).</summary>
    public bool RequireVolumeConfirmation { get; init; } = true;

    /// <summary>Minimum volume ratio for entry (1.0 = average volume).</summary>
    public double MinVolumeRatio { get; init; } = 1.2;

    /// <summary>Don't trade in the first N minutes of RTH (avoid opening volatility).</summary>
    public int AvoidFirstMinutesRTH { get; init; } = 5;

    /// <summary>Don't trade in the last N minutes of RTH (avoid closing volatility).</summary>
    public int AvoidLastMinutesRTH { get; init; } = 10;

    /// <summary>Include premarket session (4:00 AM - 9:30 AM).</summary>
    public bool IncludePremarket { get; init; } = true;

    /// <summary>Include after-hours session (4:00 PM - 8:00 PM).</summary>
    public bool IncludeAfterHours { get; init; } = true;

    /// <summary>Commission per trade in dollars.</summary>
    public decimal CommissionPerTrade { get; init; } = 0.00m;

    /// <summary>Slippage as percentage of price (0.001 = 0.1%).</summary>
    public decimal SlippagePercent { get; init; } = 0.0m;

    /// <summary>Use 100% of capital per trade (true) or calculate position size.</summary>
    public bool UseFullCapital { get; init; } = true;

    // ========================================================================
    // OPTIMIZED MODE SETTINGS (for maximum profit)
    // ========================================================================

    /// <summary>
    /// Minimum number of indicators that must agree for entry (Optimized mode).
    /// Higher values = fewer but higher quality trades.
    /// </summary>
    public int MinIndicatorConfirmation { get; init; } = 6; // Increased for better quality

    /// <summary>
    /// Enable trailing take profit that moves up as price moves favorably.
    /// </summary>
    public bool UseTrailingTakeProfit { get; init; } = true;

    /// <summary>
    /// Trail the take profit by this ATR multiplier behind highest price reached.
    /// </summary>
    public double TrailingTakeProfitAtr { get; init; } = 1.0;

    /// <summary>
    /// Move stop loss to breakeven after price moves this many ATRs in favor.
    /// </summary>
    public double MoveToBreakevenAtAtr { get; init; } = 1.5;

    /// <summary>
    /// Scale position size based on signal strength (Optimized mode).
    /// Score 90+ = 100% position, Score 70 = 70% position, etc.
    /// </summary>
    public bool UseDynamicPositionSizing { get; init; } = true;

    /// <summary>
    /// Minimum percentage of capital to allocate even on weaker signals.
    /// </summary>
    public decimal MinPositionPercent { get; init; } = 0.50m; // Minimum 50% position

    /// <summary>
    /// Add extra weight when multiple indicator categories agree.
    /// </summary>
    public double ConfirmationBonusMultiplier { get; init; } = 1.25;

    /// <summary>Maximum percentage of capital to use per trade (if not using full capital).</summary>
    public decimal MaxCapitalPerTradePercent { get; init; } = 0.25m; // 25%

    // ========================================================================
    // Mode-Specific Thresholds (from AutonomousConfig)
    // ========================================================================

    public int LongEntryThreshold => Mode switch
    {
        AutonomousMode.Conservative => 85, // Higher for safety
        AutonomousMode.Balanced => 75,     // Increased from 70
        AutonomousMode.Aggressive => 65,
        AutonomousMode.Optimized => 55,    // Requires indicator confirmation
        _ => 75
    };

    public int ShortEntryThreshold => Mode switch
    {
        AutonomousMode.Conservative => -85, // Higher for safety
        AutonomousMode.Balanced => -75,     // Increased from -70
        AutonomousMode.Aggressive => -65,
        AutonomousMode.Optimized => -55,    // Requires indicator confirmation
        _ => -75
    };

    public int LongExitThreshold => Mode switch
    {
        AutonomousMode.Conservative => 60,
        AutonomousMode.Balanced => 40,
        AutonomousMode.Aggressive => 20,
        AutonomousMode.Optimized => 10, // Hold longer with trailing stop
        _ => 40
    };

    public int ShortExitThreshold => Mode switch
    {
        AutonomousMode.Conservative => -60,
        AutonomousMode.Balanced => -40,
        AutonomousMode.Aggressive => -20,
        AutonomousMode.Optimized => -10, // Hold longer with trailing stop
        _ => -40
    };
}

/// <summary>
/// A single trade executed during backtesting.
/// </summary>
public sealed record BacktestTrade
{
    public required int TradeNumber { get; init; }
    public required DateTime EntryTime { get; init; }
    public required DateTime ExitTime { get; init; }
    public required double EntryPrice { get; init; }
    public required double ExitPrice { get; init; }
    public required int Shares { get; init; }
    public required bool IsLong { get; init; }
    public required string EntryReason { get; init; }
    public required string ExitReason { get; init; }
    public required double EntryScore { get; init; }
    public required double ExitScore { get; init; }
    public required decimal CapitalBefore { get; init; }
    public required decimal CapitalAfter { get; init; }
    public required decimal Commission { get; init; }

    // Calculated properties
    public decimal GrossPnL => IsLong
        ? (decimal)(ExitPrice - EntryPrice) * Shares
        : (decimal)(EntryPrice - ExitPrice) * Shares;

    public decimal NetPnL => GrossPnL - Commission;

    public decimal ReturnPercent => CapitalBefore > 0
        ? NetPnL / CapitalBefore * 100
        : 0;

    public TimeSpan Duration => ExitTime - EntryTime;

    public bool IsWin => NetPnL > 0;

    public override string ToString()
    {
        var direction = IsLong ? "LONG " : "SHORT";
        var pnlSign = NetPnL >= 0 ? "+" : "";
        return $"#{TradeNumber} {EntryTime:HH:mm} -> {ExitTime:HH:mm} | {direction} | " +
               $"${EntryPrice:F2} -> ${ExitPrice:F2} | {Shares} shares | " +
               $"PnL: {pnlSign}{NetPnL:F2} ({ReturnPercent:+0.00;-0.00}%)";
    }
}

/// <summary>
/// Complete result of an autonomous backtesting session.
/// </summary>
public sealed class AutonomousBacktestResult
{
    public required string Symbol { get; init; }
    public required DateOnly Date { get; init; }
    public required AutonomousBacktestConfig Config { get; init; }
    public required decimal StartingCapital { get; init; }
    public required decimal EndingCapital { get; init; }
    public required int TotalCandles { get; init; }

    public List<BacktestTrade> Trades { get; init; } = [];
    public List<(DateTime Time, double Score)> ScoreHistory { get; init; } = [];
    public List<(DateTime Time, decimal Capital)> EquityCurve { get; init; } = [];

    // Session stats
    public double DayOpen { get; init; }
    public double DayHigh { get; init; }
    public double DayLow { get; init; }
    public double DayClose { get; init; }
    public double DayChangePercent => DayOpen > 0 ? (DayClose - DayOpen) / DayOpen * 100 : 0;
    
    // Sentiment data
    public int SentimentScore { get; init; }
    public int SentimentConfidence { get; init; }
    public bool HasSentiment => SentimentConfidence > 0;

    // ========================================================================
    // Performance Metrics
    // ========================================================================

    public decimal TotalPnL => EndingCapital - StartingCapital;
    public decimal TotalReturnPercent => StartingCapital > 0 ? TotalPnL / StartingCapital * 100 : 0;

    public int TotalTrades => Trades.Count;
    public int WinCount => Trades.Count(t => t.IsWin);
    public int LossCount => Trades.Count(t => !t.IsWin);
    public decimal WinRate => TotalTrades > 0 ? (decimal)WinCount / TotalTrades * 100 : 0;

    public decimal GrossProfit => Trades.Where(t => t.NetPnL > 0).Sum(t => t.NetPnL);
    public decimal GrossLoss => Math.Abs(Trades.Where(t => t.NetPnL < 0).Sum(t => t.NetPnL));
    public decimal ProfitFactor => GrossLoss > 0 ? GrossProfit / GrossLoss : GrossProfit > 0 ? decimal.MaxValue : 0;

    public decimal AvgWin => WinCount > 0 ? Trades.Where(t => t.IsWin).Average(t => t.NetPnL) : 0;
    public decimal AvgLoss => LossCount > 0 ? Trades.Where(t => !t.IsWin).Average(t => t.NetPnL) : 0;
    public decimal AvgTrade => TotalTrades > 0 ? Trades.Average(t => t.NetPnL) : 0;

    public decimal LargestWin => WinCount > 0 ? Trades.Where(t => t.IsWin).Max(t => t.NetPnL) : 0;
    public decimal LargestLoss => LossCount > 0 ? Trades.Where(t => !t.IsWin).Min(t => t.NetPnL) : 0;

    public TimeSpan AvgTradeDuration => TotalTrades > 0
        ? TimeSpan.FromTicks((long)Trades.Average(t => t.Duration.Ticks))
        : TimeSpan.Zero;

    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public DateTime MaxDrawdownTime { get; set; }

    public int LongTrades => Trades.Count(t => t.IsLong);
    public int ShortTrades => Trades.Count(t => !t.IsLong);
    public decimal LongPnL => Trades.Where(t => t.IsLong).Sum(t => t.NetPnL);
    public decimal ShortPnL => Trades.Where(t => !t.IsLong).Sum(t => t.NetPnL);

    // ========================================================================
    // Optimization Insights
    // ========================================================================

    /// <summary>
    /// Analyzes trading patterns to suggest optimizations.
    /// </summary>
    public List<string> GetOptimizationInsights()
    {
        var insights = new List<string>();

        // Win rate analysis
        if (WinRate < 40)
            insights.Add($"Low win rate ({WinRate:F1}%): Consider raising entry thresholds or adding confirmation signals");
        else if (WinRate > 70)
            insights.Add($"High win rate ({WinRate:F1}%): Could potentially use wider take profit targets");

        // Profit factor
        if (ProfitFactor < 1.0m)
            insights.Add($"Profit factor < 1 ({ProfitFactor:F2}): Losses exceed gains - tighten stop losses or widen take profits");
        else if (ProfitFactor > 2.5m)
            insights.Add($"Excellent profit factor ({ProfitFactor:F2}): Consider increasing position size");

        // Long vs Short performance
        if (LongTrades > 0 && ShortTrades > 0)
        {
            var longAvg = LongTrades > 0 ? LongPnL / LongTrades : 0;
            var shortAvg = ShortTrades > 0 ? ShortPnL / ShortTrades : 0;

            if (longAvg > shortAvg * 2)
                insights.Add($"Long trades outperform shorts ({longAvg:F2} vs {shortAvg:F2}): Consider disabling AllowShort");
            else if (shortAvg > longAvg * 2)
                insights.Add($"Short trades outperform longs ({shortAvg:F2} vs {longAvg:F2}): Stock may trend down - prioritize shorts");
        }

        // Time-based analysis
        var morningTrades = Trades.Where(t => t.EntryTime.Hour < 12).ToList();
        var afternoonTrades = Trades.Where(t => t.EntryTime.Hour >= 12).ToList();

        if (morningTrades.Count > 0 && afternoonTrades.Count > 0)
        {
            var morningPnL = morningTrades.Sum(t => t.NetPnL);
            var afternoonPnL = afternoonTrades.Sum(t => t.NetPnL);

            if (morningPnL > afternoonPnL * 2 && afternoonPnL < 0)
                insights.Add("Morning trades profitable, afternoon trades losing: Consider EOD exit at noon");
            else if (afternoonPnL > morningPnL * 2 && morningPnL < 0)
                insights.Add("Afternoon trades profitable, morning trades losing: Consider delayed entry after 12:00");
        }

        // Drawdown analysis
        if (MaxDrawdownPercent > 20)
            insights.Add($"High max drawdown ({MaxDrawdownPercent:F1}%): Consider smaller position sizes or tighter stops");

        // Score threshold analysis
        var highScoreEntries = Trades.Where(t => Math.Abs(t.EntryScore) >= 80).ToList();
        var lowScoreEntries = Trades.Where(t => Math.Abs(t.EntryScore) < 80).ToList();

        if (highScoreEntries.Count > 0 && lowScoreEntries.Count > 0)
        {
            var highScoreWinRate = highScoreEntries.Count(t => t.IsWin) / (decimal)highScoreEntries.Count * 100;
            var lowScoreWinRate = lowScoreEntries.Count(t => t.IsWin) / (decimal)lowScoreEntries.Count * 100;

            if (highScoreWinRate > lowScoreWinRate + 15)
                insights.Add($"High-score entries ({highScoreWinRate:F0}% win) outperform low-score ({lowScoreWinRate:F0}%): Raise entry threshold to 80");
        }

        if (insights.Count == 0)
            insights.Add("No significant optimization opportunities detected");

        return insights;
    }

    // ========================================================================
    // Display
    // ========================================================================

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"""
            +======================================================================+
            | AUTONOMOUS TRADING BACKTEST RESULT                                   |
            +======================================================================+
            | Symbol:       {Symbol,-10} | Date: {Date:yyyy-MM-dd}
            | Mode:         {Config.Mode,-10} | Allow Short: {Config.AllowShort}
            +----------------------------------------------------------------------+
            | MARKET DATA                                                          |
            +----------------------------------------------------------------------+
            | Open:  ${DayOpen,8:F2}   | High:  ${DayHigh,8:F2}
            | Low:   ${DayLow,8:F2}   | Close: ${DayClose,8:F2}
            | Day Change: {DayChangePercent,+6:F2}%
            | Candles: {TotalCandles,5}
            +----------------------------------------------------------------------+
            | CAPITAL                                                              |
            +----------------------------------------------------------------------+
            | Starting: ${StartingCapital,10:F2}
            | Ending:   ${EndingCapital,10:F2}
            | Net P&L:  ${TotalPnL,10:F2} ({TotalReturnPercent:+0.00;-0.00}%)
            +----------------------------------------------------------------------+
            | PERFORMANCE                                                          |
            +----------------------------------------------------------------------+
            | Total Trades:   {TotalTrades,6}  | Win Rate:    {WinRate,6:F1}%
            | Winning:        {WinCount,6}  | Losing:      {LossCount,6}
            | Avg Win:      ${AvgWin,8:F2}  | Avg Loss:  ${AvgLoss,8:F2}
            | Largest Win:  ${LargestWin,8:F2}  | Largest Loss: ${LargestLoss,8:F2}
            | Profit Factor:  {ProfitFactor,6:F2}
            | Avg Trade:    ${AvgTrade,8:F2}  | Avg Duration: {AvgTradeDuration:mm\:ss}
            +----------------------------------------------------------------------+
            | DIRECTION BREAKDOWN                                                  |
            +----------------------------------------------------------------------+
            | Long Trades:  {LongTrades,4} (${LongPnL,+8:F2})
            | Short Trades: {ShortTrades,4} (${ShortPnL,+8:F2})
            +----------------------------------------------------------------------+
            | RISK                                                                 |
            +----------------------------------------------------------------------+
            | Max Drawdown:  ${MaxDrawdown,8:F2} ({MaxDrawdownPercent:F1}%)
            | Drawdown Time: {MaxDrawdownTime:HH:mm}
            +======================================================================+
            """);

        if (Trades.Count > 0)
        {
            sb.AppendLine("| TRADE LOG                                                            |");
            sb.AppendLine("+----------------------------------------------------------------------+");

            foreach (var trade in Trades)
            {
                var arrow = trade.IsWin ? "[+]" : "[-]";
                sb.AppendLine($"| {arrow} {trade}");
                sb.AppendLine($"|     Entry: {trade.EntryReason} (score: {trade.EntryScore:+0;-0})");
                sb.AppendLine($"|     Exit:  {trade.ExitReason} (score: {trade.ExitScore:+0;-0})");
            }
        }

        sb.AppendLine("+----------------------------------------------------------------------+");
        sb.AppendLine("| OPTIMIZATION INSIGHTS                                                |");
        sb.AppendLine("+----------------------------------------------------------------------+");
        foreach (var insight in GetOptimizationInsights())
        {
            sb.AppendLine($"| * {insight}");
        }
        sb.AppendLine("+======================================================================+");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a CSV-compatible trade log.
    /// </summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("TradeNum,EntryTime,ExitTime,Direction,EntryPrice,ExitPrice,Shares,GrossPnL,NetPnL,ReturnPct,EntryScore,ExitScore,EntryReason,ExitReason");

        foreach (var t in Trades)
        {
            sb.AppendLine($"{t.TradeNumber},{t.EntryTime:yyyy-MM-dd HH:mm:ss},{t.ExitTime:yyyy-MM-dd HH:mm:ss}," +
                         $"{(t.IsLong ? "LONG" : "SHORT")},{t.EntryPrice:F2},{t.ExitPrice:F2},{t.Shares}," +
                         $"{t.GrossPnL:F2},{t.NetPnL:F2},{t.ReturnPercent:F2},{t.EntryScore:F0},{t.ExitScore:F0}," +
                         $"\"{t.EntryReason}\",\"{t.ExitReason}\"");
        }

        return sb.ToString();
    }
}

/// <summary>
/// Runs autonomous trading backtests against historical IBKR data.
/// Integrates technical indicators with market sentiment for comprehensive analysis.
/// </summary>
public sealed class AutonomousBacktester
{
    private readonly HistoricalDataService? _histService;
    private readonly SentimentService? _sentimentService;

    /// <summary>
    /// Creates a backtester with IBKR historical data service.
    /// </summary>
    /// <param name="historicalDataService">The historical data service (can be null for offline/synthetic testing).</param>
    /// <param name="sentimentService">Optional sentiment service for news/earnings integration.</param>
    public AutonomousBacktester(HistoricalDataService? historicalDataService, SentimentService? sentimentService = null)
    {
        _histService = historicalDataService;
        _sentimentService = sentimentService;
    }

    /// <summary>
    /// Runs an autonomous trading backtest for a symbol on a specific date.
    /// Requires a valid HistoricalDataService to be provided in the constructor.
    /// </summary>
    /// <param name="symbol">The ticker symbol (e.g., "NVDA").</param>
    /// <param name="date">The date to backtest.</param>
    /// <param name="startingCapital">Starting capital in dollars.</param>
    /// <param name="config">Optional configuration overrides.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete backtest result with trade analysis.</returns>
    public async Task<AutonomousBacktestResult> RunAsync(
        string symbol,
        DateOnly date,
        decimal startingCapital = 1000.00m,
        AutonomousBacktestConfig? config = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_histService == null)
            throw new InvalidOperationException("HistoricalDataService is required for RunAsync. Use RunWithCandles for offline testing.");

        config ??= new AutonomousBacktestConfig { StartingCapital = startingCapital };

        progress?.Report($"Fetching historical data for {symbol} on {date:yyyy-MM-dd}...");
        
        // Fetch sentiment data if service available
        SentimentResult? sentiment = null;
        if (_sentimentService != null)
        {
            progress?.Report($"Fetching sentiment data for {symbol}...");
            sentiment = await _sentimentService.GetSentimentAsync(symbol);
            progress?.Report($"Sentiment: {sentiment}");
        }

        // Fetch full day of data (need extended hours for 4AM-8PM)
        // Request extra bars to ensure we get the full day
        int barsNeeded = 960; // 16 hours * 60 minutes = 960 1-minute bars

        await _histService.FetchHistoricalDataAsync(
            symbol,
            barsNeeded,
            BarSize.Minutes1,
            HistoricalDataType.Trades,
            useRTH: false, // Include extended hours
            endDate: null,
            cancellationToken: cancellationToken);

        var bars = _histService.Store.GetBars(symbol);

        if (bars.Count == 0)
        {
            throw new InvalidOperationException($"No historical data available for {symbol} on {date:yyyy-MM-dd}");
        }

        progress?.Report($"Loaded {bars.Count} bars. Running simulation...");

        // Filter to the specific date
        var dateBars = bars
            .Where(b => DateOnly.FromDateTime(b.Time) == date)
            .OrderBy(b => b.Time)
            .ToList();

        if (dateBars.Count == 0)
        {
            throw new InvalidOperationException($"No bars found for {symbol} on {date:yyyy-MM-dd}. Available dates: " +
                string.Join(", ", bars.Select(b => DateOnly.FromDateTime(b.Time)).Distinct().Take(5)));
        }

        // Convert to BackTestCandles for the simulator
        var candles = ConvertToBackTestCandles(dateBars);

        // Run simulation with sentiment
        return RunSimulation(symbol, date, candles, config, sentiment, progress);
    }

    /// <summary>
    /// Runs simulation with pre-loaded candle data (for unit testing without IBKR connection).
    /// </summary>
    public AutonomousBacktestResult RunWithCandles(
        string symbol,
        DateOnly date,
        List<BackTestCandle> candles,
        AutonomousBacktestConfig? config = null,
        IProgress<string>? progress = null,
        SentimentResult? sentiment = null)
    {
        config ??= new AutonomousBacktestConfig();
        return RunSimulation(symbol, date, candles, config, sentiment, progress);
    }

    private AutonomousBacktestResult RunSimulation(
        string symbol,
        DateOnly date,
        List<BackTestCandle> candles,
        AutonomousBacktestConfig config,
        SentimentResult? sentiment,
        IProgress<string>? progress)
    {
        var result = new AutonomousBacktestResult
        {
            Symbol = symbol,
            Date = date,
            Config = config,
            StartingCapital = config.StartingCapital,
            EndingCapital = config.StartingCapital,
            TotalCandles = candles.Count,
            DayOpen = candles.FirstOrDefault()?.Open ?? 0,
            DayHigh = candles.Count > 0 ? candles.Max(c => c.High) : 0,
            DayLow = candles.Count > 0 ? candles.Min(c => c.Low) : 0,
            DayClose = candles.LastOrDefault()?.Close ?? 0,
            SentimentScore = sentiment?.Score ?? 0,
            SentimentConfidence = sentiment?.Confidence ?? 0
        };

        if (candles.Count == 0)
            return result;

        // Calculate VWAP for all candles
        CalculateVwap(candles);

        // Create indicator calculator and inject sentiment
        var indicators = new BackTestIndicatorCalculator(candles);
        if (sentiment != null)
        {
            indicators.SetSentiment(sentiment.Score, sentiment.Confidence);
        }

        // Minimum warmup period for indicators (need enough for ADX/MACD)
        int warmupPeriod = Math.Min(50, candles.Count - 1);

        // State tracking
        decimal currentCapital = config.StartingCapital;
        decimal peakCapital = config.StartingCapital;
        bool inPosition = false;
        bool isLong = false;
        double entryPrice = 0;
        DateTime entryTime = default;
        double entryScore = 0;
        string entryReason = "";
        DateTime lastTradeTime = DateTime.MinValue;
        double takeProfitPrice = 0;
        double stopLossPrice = 0;
        double originalStopLoss = 0;  // For breakeven tracking
        int tradeNumber = 0;
        int shares = 0;
        int entryIndex = 0;           // For tracking high/low since entry
        double highestSinceEntry = 0; // For trailing TP (long)
        double lowestSinceEntry = double.MaxValue; // For trailing TP (short)
        double atrAtEntry = 0;        // For ATR-based adjustments

        progress?.Report("Simulating trades...");

        for (int i = warmupPeriod; i < candles.Count; i++)
        {
            var candle = candles[i];
            var score = indicators.CalculateMarketScore(i);

            // Track score history
            result.ScoreHistory.Add((candle.Timestamp, score));

            // Track equity curve
            if (inPosition)
            {
                decimal unrealizedPnL = isLong
                    ? (decimal)(candle.Close - entryPrice) * shares
                    : (decimal)(entryPrice - candle.Close) * shares;
                result.EquityCurve.Add((candle.Timestamp, currentCapital + unrealizedPnL));
            }
            else
            {
                result.EquityCurve.Add((candle.Timestamp, currentCapital));
            }

            // Track drawdown
            decimal currentEquity = result.EquityCurve.Last().Capital;
            if (currentEquity > peakCapital)
                peakCapital = currentEquity;

            decimal drawdown = peakCapital - currentEquity;
            if (drawdown > result.MaxDrawdown)
            {
                result.MaxDrawdown = drawdown;
                result.MaxDrawdownPercent = peakCapital > 0 ? drawdown / peakCapital * 100 : 0;
                result.MaxDrawdownTime = candle.Timestamp;
            }

            if (!inPosition)
            {
                // Check for entry signals
                bool canTrade = (candle.Timestamp - lastTradeTime).TotalSeconds >= config.MinSecondsBetweenTrades;
                bool hasCapital = currentCapital > 10; // Minimum $10 to trade
                
                // Time-based filters - avoid volatile periods
                var time = candle.Timestamp.TimeOfDay;
                var rthOpen = new TimeSpan(9, 30, 0);
                var rthClose = new TimeSpan(16, 0, 0);
                bool isRTH = time >= rthOpen && time < rthClose;
                bool avoidOpenVolatility = isRTH && time < rthOpen.Add(TimeSpan.FromMinutes(config.AvoidFirstMinutesRTH));
                bool avoidCloseVolatility = isRTH && time > rthClose.Subtract(TimeSpan.FromMinutes(config.AvoidLastMinutesRTH));
                bool timeOk = !avoidOpenVolatility && !avoidCloseVolatility;
                
                // Volume confirmation
                double volumeRatio = indicators.GetVolumeRatio(i);
                bool volumeOk = !config.RequireVolumeConfirmation || volumeRatio >= config.MinVolumeRatio;
                
                // Trend alignment check (price vs EMA21)
                double ema21 = indicators.GetEma21(i);
                bool longTrendOk = !config.RequireTrendAlignment || candle.Close > ema21;
                bool shortTrendOk = !config.RequireTrendAlignment || candle.Close < ema21;

                if (canTrade && hasCapital && timeOk && volumeOk)
                {
                    // Get indicator confirmation for Optimized mode
                    var (bullishCount, bearishCount, totalCategories) = indicators.GetIndicatorConfirmation(i);
                    bool isOptimizedMode = config.Mode == AutonomousMode.Optimized;
                    
                    // In Optimized mode, require minimum indicator confirmation
                    bool longConfirmed = !isOptimizedMode || bullishCount >= config.MinIndicatorConfirmation;
                    bool shortConfirmed = !isOptimizedMode || bearishCount >= config.MinIndicatorConfirmation;
                    
                    // Calculate position size - dynamic in Optimized mode
                    decimal capitalToUse;
                    if (isOptimizedMode && config.UseDynamicPositionSizing)
                    {
                        // Scale position size based on signal strength
                        double scoreStrength = Math.Abs(score) / 100.0; // 0.5 to 1.0
                        decimal positionPercent = config.MinPositionPercent + 
                            (1.0m - config.MinPositionPercent) * (decimal)scoreStrength;
                        capitalToUse = currentCapital * positionPercent;
                        
                        // Boost position if high indicator confirmation
                        int confirmCount = Math.Max(bullishCount, bearishCount);
                        if (confirmCount >= 8)
                            capitalToUse *= (decimal)config.ConfirmationBonusMultiplier;
                    }
                    else if (config.UseFullCapital)
                    {
                        capitalToUse = currentCapital;
                    }
                    else
                    {
                        capitalToUse = currentCapital * config.MaxCapitalPerTradePercent;
                    }

                    // Long entry - require trend alignment and bullish confirmation
                    if (score >= config.LongEntryThreshold && longConfirmed && longTrendOk)
                    {
                        double priceWithSlippage = candle.Close * (1 + (double)config.SlippagePercent);
                        shares = (int)(capitalToUse / (decimal)priceWithSlippage);

                        if (shares > 0)
                        {
                            inPosition = true;
                            isLong = true;
                            entryPrice = priceWithSlippage;
                            entryTime = candle.Timestamp;
                            entryScore = score;
                            entryIndex = i;
                            highestSinceEntry = candle.High;
                            lowestSinceEntry = candle.Low;
                            
                            string confirmStr = isOptimizedMode ? $" [{bullishCount}/{totalCategories} confirm]" : "";
                            entryReason = $"Score {score:+0} >= {config.LongEntryThreshold}{confirmStr}";
                            lastTradeTime = candle.Timestamp;

                            // Calculate TP/SL based on ATR
                            atrAtEntry = indicators.GetAtr(i);
                            takeProfitPrice = entryPrice + (atrAtEntry * config.TakeProfitAtrMultiplier);
                            stopLossPrice = entryPrice - (atrAtEntry * config.StopLossAtrMultiplier);
                            originalStopLoss = stopLossPrice;
                        }
                    }
                    // Short entry - require trend alignment and bearish confirmation
                    else if (config.AllowShort && score <= config.ShortEntryThreshold && shortConfirmed && shortTrendOk)
                    {
                        double priceWithSlippage = candle.Close * (1 - (double)config.SlippagePercent);
                        shares = (int)(capitalToUse / (decimal)priceWithSlippage);

                        if (shares > 0)
                        {
                            inPosition = true;
                            isLong = false;
                            entryPrice = priceWithSlippage;
                            entryTime = candle.Timestamp;
                            entryScore = score;
                            entryIndex = i;
                            highestSinceEntry = candle.High;
                            lowestSinceEntry = candle.Low;
                            
                            string confirmStr = isOptimizedMode ? $" [{bearishCount}/{totalCategories} confirm]" : "";
                            entryReason = $"Score {score:+0} <= {config.ShortEntryThreshold}{confirmStr}";
                            lastTradeTime = candle.Timestamp;

                            atrAtEntry = indicators.GetAtr(i);
                            takeProfitPrice = entryPrice - (atrAtEntry * config.TakeProfitAtrMultiplier);
                            stopLossPrice = entryPrice + (atrAtEntry * config.StopLossAtrMultiplier);
                            originalStopLoss = stopLossPrice;
                        }
                    }
                }
            }
            else
            {
                // Track high/low since entry for trailing
                if (candle.High > highestSinceEntry) highestSinceEntry = candle.High;
                if (candle.Low < lowestSinceEntry) lowestSinceEntry = candle.Low;
                
                // Optimized mode: Trailing take profit and breakeven stop
                bool isOptimizedMode = config.Mode == AutonomousMode.Optimized;
                if (isOptimizedMode && config.UseTrailingTakeProfit)
                {
                    if (isLong)
                    {
                        // Trail TP below the highest price reached
                        double newTrailingTp = highestSinceEntry - (atrAtEntry * config.TrailingTakeProfitAtr);
                        if (newTrailingTp > takeProfitPrice && highestSinceEntry > entryPrice + atrAtEntry)
                        {
                            takeProfitPrice = newTrailingTp;
                        }
                        
                        // Move stop to breakeven after price moves favorably
                        double favorableMove = highestSinceEntry - entryPrice;
                        if (favorableMove >= atrAtEntry * config.MoveToBreakevenAtAtr && stopLossPrice < entryPrice)
                        {
                            stopLossPrice = entryPrice; // Breakeven
                        }
                    }
                    else // Short position
                    {
                        // Trail TP above the lowest price reached
                        double newTrailingTp = lowestSinceEntry + (atrAtEntry * config.TrailingTakeProfitAtr);
                        if (newTrailingTp < takeProfitPrice && lowestSinceEntry < entryPrice - atrAtEntry)
                        {
                            takeProfitPrice = newTrailingTp;
                        }
                        
                        // Move stop to breakeven
                        double favorableMove = entryPrice - lowestSinceEntry;
                        if (favorableMove >= atrAtEntry * config.MoveToBreakevenAtAtr && stopLossPrice > entryPrice)
                        {
                            stopLossPrice = entryPrice; // Breakeven
                        }
                    }
                }
                
                // Check exit conditions
                string? exitReason = null;
                double exitPrice = candle.Close;

                if (isLong)
                {
                    if (candle.High >= takeProfitPrice)
                    {
                        exitReason = $"Take profit at ${takeProfitPrice:F2}";
                        exitPrice = takeProfitPrice;
                    }
                    else if (candle.Low <= stopLossPrice)
                    {
                        exitReason = $"Stop loss at ${stopLossPrice:F2}";
                        exitPrice = stopLossPrice;
                    }
                    else if (score < config.LongExitThreshold)
                    {
                        exitReason = $"Score {score:+0} < {config.LongExitThreshold} (momentum fading)";
                    }
                    else if (score <= -70)
                    {
                        exitReason = $"Emergency exit - bearish signal ({score:+0})";
                    }
                }
                else // Short position
                {
                    if (candle.Low <= takeProfitPrice)
                    {
                        exitReason = $"Take profit at ${takeProfitPrice:F2}";
                        exitPrice = takeProfitPrice;
                    }
                    else if (candle.High >= stopLossPrice)
                    {
                        exitReason = $"Stop loss at ${stopLossPrice:F2}";
                        exitPrice = stopLossPrice;
                    }
                    else if (score > config.ShortExitThreshold)
                    {
                        exitReason = $"Score {score:+0} > {config.ShortExitThreshold} (momentum fading)";
                    }
                    else if (score >= 70)
                    {
                        exitReason = $"Emergency exit - bullish signal ({score:+0})";
                    }
                }

                if (exitReason != null)
                {
                    // Apply slippage to exit
                    if (isLong)
                        exitPrice *= (1 - (double)config.SlippagePercent);
                    else
                        exitPrice *= (1 + (double)config.SlippagePercent);

                    // Calculate P&L
                    decimal grossPnL = isLong
                        ? (decimal)(exitPrice - entryPrice) * shares
                        : (decimal)(entryPrice - exitPrice) * shares;

                    decimal commission = config.CommissionPerTrade * 2; // Entry + exit
                    decimal netPnL = grossPnL - commission;

                    decimal capitalBefore = currentCapital;
                    currentCapital += netPnL;

                    tradeNumber++;
                    result.Trades.Add(new BacktestTrade
                    {
                        TradeNumber = tradeNumber,
                        EntryTime = entryTime,
                        ExitTime = candle.Timestamp,
                        EntryPrice = entryPrice,
                        ExitPrice = exitPrice,
                        Shares = shares,
                        IsLong = isLong,
                        EntryReason = entryReason,
                        ExitReason = exitReason,
                        EntryScore = entryScore,
                        ExitScore = score,
                        CapitalBefore = capitalBefore,
                        CapitalAfter = currentCapital,
                        Commission = commission
                    });

                    inPosition = false;

                    // Check for direction flip
                    if (config.AllowDirectionFlip && currentCapital > 10)
                    {
                        decimal capitalToUse = config.UseFullCapital
                            ? currentCapital
                            : currentCapital * config.MaxCapitalPerTradePercent;

                        if (isLong && score <= config.ShortEntryThreshold && config.AllowShort)
                        {
                            double flipPrice = candle.Close * (1 - (double)config.SlippagePercent);
                            shares = (int)(capitalToUse / (decimal)flipPrice);

                            if (shares > 0)
                            {
                                inPosition = true;
                                isLong = false;
                                entryPrice = flipPrice;
                                entryTime = candle.Timestamp;
                                entryScore = score;
                                entryReason = $"Direction flip: Score {score:+0} - going SHORT";
                                lastTradeTime = candle.Timestamp;

                                double atr = indicators.GetAtr(i);
                                takeProfitPrice = entryPrice - (atr * config.TakeProfitAtrMultiplier);
                                stopLossPrice = entryPrice + (atr * config.StopLossAtrMultiplier);
                            }
                        }
                        else if (!isLong && score >= config.LongEntryThreshold)
                        {
                            double flipPrice = candle.Close * (1 + (double)config.SlippagePercent);
                            shares = (int)(capitalToUse / (decimal)flipPrice);

                            if (shares > 0)
                            {
                                inPosition = true;
                                isLong = true;
                                entryPrice = flipPrice;
                                entryTime = candle.Timestamp;
                                entryScore = score;
                                entryReason = $"Direction flip: Score {score:+0} - going LONG";
                                lastTradeTime = candle.Timestamp;

                                double atr = indicators.GetAtr(i);
                                takeProfitPrice = entryPrice + (atr * config.TakeProfitAtrMultiplier);
                                stopLossPrice = entryPrice - (atr * config.StopLossAtrMultiplier);
                            }
                        }
                    }
                }
            }
        }

        // Close any open position at end of day
        if (inPosition && candles.Count > 0)
        {
            var lastCandle = candles[^1];
            double exitPrice = isLong
                ? lastCandle.Close * (1 - (double)config.SlippagePercent)
                : lastCandle.Close * (1 + (double)config.SlippagePercent);

            decimal grossPnL = isLong
                ? (decimal)(exitPrice - entryPrice) * shares
                : (decimal)(entryPrice - exitPrice) * shares;

            decimal commission = config.CommissionPerTrade * 2;
            decimal netPnL = grossPnL - commission;

            decimal capitalBefore = currentCapital;
            currentCapital += netPnL;

            tradeNumber++;
            result.Trades.Add(new BacktestTrade
            {
                TradeNumber = tradeNumber,
                EntryTime = entryTime,
                ExitTime = lastCandle.Timestamp,
                EntryPrice = entryPrice,
                ExitPrice = exitPrice,
                Shares = shares,
                IsLong = isLong,
                EntryReason = entryReason,
                ExitReason = "End of day exit",
                EntryScore = entryScore,
                ExitScore = indicators.CalculateMarketScore(candles.Count - 1),
                CapitalBefore = capitalBefore,
                CapitalAfter = currentCapital,
                Commission = commission
            });
        }

        // Update ending capital - create new result with updated value
        return new AutonomousBacktestResult
        {
            Symbol = result.Symbol,
            Date = result.Date,
            Config = result.Config,
            StartingCapital = result.StartingCapital,
            EndingCapital = currentCapital,
            TotalCandles = result.TotalCandles,
            Trades = result.Trades,
            ScoreHistory = result.ScoreHistory,
            EquityCurve = result.EquityCurve,
            DayOpen = result.DayOpen,
            DayHigh = result.DayHigh,
            DayLow = result.DayLow,
            DayClose = result.DayClose,
            MaxDrawdown = result.MaxDrawdown,
            MaxDrawdownPercent = result.MaxDrawdownPercent,
            MaxDrawdownTime = result.MaxDrawdownTime
        };
    }

    private static List<BackTestCandle> ConvertToBackTestCandles(IReadOnlyList<HistoricalBar> bars)
    {
        return bars.Select(b => new BackTestCandle
        {
            Timestamp = b.Time,
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume,
            Vwap = b.Vwap ?? 0
        }).ToList();
    }

    private static void CalculateVwap(List<BackTestCandle> candles)
    {
        double cumulativeTypicalPriceVolume = 0;
        long cumulativeVolume = 0;

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            double typicalPrice = (candle.High + candle.Low + candle.Close) / 3;
            cumulativeTypicalPriceVolume += typicalPrice * candle.Volume;
            cumulativeVolume += candle.Volume;

            double vwap = cumulativeVolume > 0
                ? cumulativeTypicalPriceVolume / cumulativeVolume
                : candle.Close;

            candles[i] = candle with { Vwap = vwap };
        }
    }
}

/// <summary>
/// Indicator calculator for backtesting (simplified version for BackTestCandle).
/// Includes comprehensive technical indicators for market scoring.
/// </summary>
internal sealed class BackTestIndicatorCalculator
{
    private readonly List<BackTestCandle> _candles;
    
    // Core indicators
    private readonly double[] _ema9;
    private readonly double[] _ema21;
    private readonly double[] _ema50;
    private readonly double[] _ema200;
    private readonly double[] _rsi;
    private readonly double[] _adx;
    private readonly double[] _plusDi;
    private readonly double[] _minusDi;
    private readonly double[] _macd;
    private readonly double[] _signal;
    private readonly double[] _histogram;
    private readonly double[] _volumeRatio;
    private readonly double[] _atr;
    
    // Additional indicators for enhanced scoring
    private readonly double[] _bollingerUpper;
    private readonly double[] _bollingerLower;
    private readonly double[] _bollingerMiddle;
    private readonly double[] _stochasticK;
    private readonly double[] _stochasticD;
    private readonly double[] _obv;
    private readonly double[] _cci;
    private readonly double[] _williamsR;
    private readonly double[] _mfi;       // Money Flow Index
    private readonly double[] _roc;       // Rate of Change
    private readonly double[] _momentum;
    
    // Sentiment score (injected from SentimentService)
    private int _sentimentScore;
    private int _sentimentConfidence;

    public BackTestIndicatorCalculator(List<BackTestCandle> candles)
    {
        _candles = candles;

        // Pre-calculate all indicators
        var closes = candles.Select(c => c.Close).ToArray();
        var highs = candles.Select(c => c.High).ToArray();
        var lows = candles.Select(c => c.Low).ToArray();
        var volumes = candles.Select(c => (double)c.Volume).ToArray();

        // Core indicators
        _ema9 = CalculateEma(closes, 9);
        _ema21 = CalculateEma(closes, 21);
        _ema50 = CalculateEma(closes, 50);
        _ema200 = CalculateEma(closes, 200);
        _rsi = CalculateRsi(closes, 14);
        (_adx, _plusDi, _minusDi) = CalculateAdx(highs, lows, closes, 14);
        (_macd, _signal, _histogram) = CalculateMacd(closes, 12, 26, 9);
        _volumeRatio = CalculateVolumeRatio(volumes, 20);
        _atr = CalculateAtr(highs, lows, closes, 14);
        
        // Additional indicators
        (_bollingerUpper, _bollingerMiddle, _bollingerLower) = CalculateBollingerBands(closes, 20, 2.0);
        (_stochasticK, _stochasticD) = CalculateStochastic(highs, lows, closes, 14, 3);
        _obv = CalculateObv(closes, volumes);
        _cci = CalculateCci(highs, lows, closes, 20);
        _williamsR = CalculateWilliamsR(highs, lows, closes, 14);
        _mfi = CalculateMfi(highs, lows, closes, volumes, 14);
        _roc = CalculateRoc(closes, 10);
        _momentum = CalculateMomentum(closes, 10);
    }
    
    /// <summary>
    /// Injects sentiment data from external source (news, earnings, etc.)
    /// </summary>
    public void SetSentiment(int score, int confidence)
    {
        _sentimentScore = score;
        _sentimentConfidence = confidence;
    }

    public double GetAtr(int index) => index < _atr.Length ? _atr[index] : 0;
    public double GetVolumeRatio(int index) => index < _volumeRatio.Length ? _volumeRatio[index] : 0;
    public double GetEma9(int index) => index < _ema9.Length ? _ema9[index] : 0;
    public double GetEma21(int index) => index < _ema21.Length ? _ema21[index] : 0;
    public double GetEma50(int index) => index < _ema50.Length ? _ema50[index] : 0;
    public double GetRsi(int index) => index < _rsi.Length ? _rsi[index] : 50;
    public double GetMacd(int index) => index < _macd.Length ? _macd[index] : 0;
    public double GetSignal(int index) => index < _signal.Length ? _signal[index] : 0;
    public double GetAdx(int index) => index < _adx.Length ? _adx[index] : 0;
    public double GetPlusDi(int index) => index < _plusDi.Length ? _plusDi[index] : 0;
    public double GetMinusDi(int index) => index < _minusDi.Length ? _minusDi[index] : 0;
    
    // Accessor methods for individual indicator scores
    public double GetBollingerScore(int index)
    {
        if (index < 0 || index >= _candles.Count) return 0;
        var close = _candles[index].Close;
        var upper = _bollingerUpper[index];
        var lower = _bollingerLower[index];
        var middle = _bollingerMiddle[index];
        
        if (upper == lower) return 0;
        
        // Position within bands: -100 (at lower) to +100 (at upper)
        double position = (close - middle) / (upper - middle) * 100;
        
        // Reversal signals are stronger at extremes
        if (close >= upper) return -50; // Overbought, expect pullback
        if (close <= lower) return 50;  // Oversold, expect bounce
        
        return Math.Clamp(-position * 0.5, -100, 100); // Contrarian scoring
    }
    
    public double GetStochasticScore(int index)
    {
        if (index < 0 || index >= _stochasticK.Length) return 0;
        double k = _stochasticK[index];
        double d = _stochasticD[index];
        
        // Overbought/oversold
        double score = 0;
        if (k <= 20) score = 100 - (k * 5); // Oversold = bullish
        else if (k >= 80) score = -(k - 80) * 5; // Overbought = bearish
        else score = (k - 50) * 2; // Momentum direction
        
        // K crossing D adds conviction
        if (k > d && k < 30) score += 25; // Bullish crossover in oversold
        if (k < d && k > 70) score -= 25; // Bearish crossover in overbought
        
        return Math.Clamp(score, -100, 100);
    }
    
    public double GetObvScore(int index)
    {
        if (index < 20 || index >= _obv.Length) return 0;
        
        // OBV trend over last 10 bars
        double obvNow = _obv[index];
        double obv10Ago = _obv[index - 10];
        double obvChange = obv10Ago != 0 ? (obvNow - obv10Ago) / Math.Abs(obv10Ago) * 100 : 0;
        
        return Math.Clamp(obvChange * 2, -100, 100);
    }
    
    public double GetCciScore(int index)
    {
        if (index < 0 || index >= _cci.Length) return 0;
        double cci = _cci[index];
        
        // CCI: +100/-100 are overbought/oversold levels
        if (cci >= 200) return -80;
        if (cci >= 100) return -((cci - 100) * 0.8);
        if (cci <= -200) return 80;
        if (cci <= -100) return (-cci - 100) * 0.8;
        
        return cci * 0.5; // Trend direction
    }
    
    public double GetWilliamsRScore(int index)
    {
        if (index < 0 || index >= _williamsR.Length) return 0;
        double wr = _williamsR[index];
        
        // Williams %R: -80 to -100 is oversold (bullish), 0 to -20 is overbought (bearish)
        if (wr <= -80) return 100 - ((wr + 100) * 5); // Oversold = bullish
        if (wr >= -20) return -(20 + wr) * 5; // Overbought = bearish
        
        return (wr + 50) * 2; // Momentum direction
    }
    
    public double GetMfiScore(int index)
    {
        if (index < 0 || index >= _mfi.Length) return 0;
        double mfi = _mfi[index];
        
        // Similar to RSI but includes volume
        if (mfi <= 20) return 100; // Oversold
        if (mfi >= 80) return -100; // Overbought
        
        return (mfi - 50) * 2;
    }
    
    public double GetMomentumScore(int index)
    {
        if (index < 0 || index >= _momentum.Length) return 0;
        double mom = _momentum[index];
        double atr = _atr[index];
        
        if (atr == 0) return 0;
        
        // Normalize momentum by ATR
        double normalizedMom = mom / atr * 25;
        return Math.Clamp(normalizedMom, -100, 100);
    }
    
    public double GetRocScore(int index)
    {
        if (index < 0 || index >= _roc.Length) return 0;
        double roc = _roc[index];
        
        // ROC as percentage, scale to -100 to 100
        return Math.Clamp(roc * 10, -100, 100);
    }

    /// <summary>
    /// Calculates enhanced market score including all indicators and sentiment.
    /// </summary>

    public double CalculateMarketScore(int index)
    {
        if (index < 0 || index >= _candles.Count)
            return 0;

        var candle = _candles[index];

        // VWAP Position (15% weight)
        double vwapScore = 0;
        if (candle.Vwap > 0)
        {
            double vwapDistance = (candle.Close - candle.Vwap) / candle.Vwap * 100;
            vwapScore = Math.Clamp(vwapDistance * 20, -100, 100);
        }

        // EMA Stack (20% weight)
        double emaScore = 0;
        int aboveCount = 0;
        if (candle.Close > _ema9[index]) aboveCount++;
        if (candle.Close > _ema21[index]) aboveCount++;
        if (candle.Close > _ema50[index]) aboveCount++;

        bool bullishStack = _ema9[index] > _ema21[index] && _ema21[index] > _ema50[index];
        bool bearishStack = _ema9[index] < _ema21[index] && _ema21[index] < _ema50[index];

        emaScore = (aboveCount - 1.5) * 66.67;
        if (bullishStack) emaScore = Math.Min(100, emaScore + 25);
        if (bearishStack) emaScore = Math.Max(-100, emaScore - 25);

        // RSI (15% weight)
        double rsiScore = 0;
        double rsiValue = _rsi[index];

        if (rsiValue <= 30)
            rsiScore = 100 - (rsiValue - 30) * 3.33;
        else if (rsiValue >= 70)
            rsiScore = -(rsiValue - 70) * 3.33;
        else if (rsiValue < 50)
            rsiScore = (rsiValue - 50) * 2;
        else
            rsiScore = (rsiValue - 50) * 2;

        rsiScore = Math.Clamp(rsiScore, -100, 100);

        // MACD (20% weight)
        double macdScore = 0;
        double macdValue = _macd[index];
        double signalValue = _signal[index];
        double histogram = _histogram[index];

        if (macdValue > signalValue)
            macdScore = 50;
        else
            macdScore = -50;

        double histogramStrength = Math.Abs(histogram) / (Math.Abs(macdValue) + 0.001) * 100;
        histogramStrength = Math.Min(histogramStrength, 50);

        if (histogram > 0)
            macdScore += histogramStrength;
        else
            macdScore -= histogramStrength;

        macdScore = Math.Clamp(macdScore, -100, 100);

        // ADX (20% weight)
        double adxScore = 0;
        double adxValue = _adx[index];
        double plusDi = _plusDi[index];
        double minusDi = _minusDi[index];

        double trendStrength = Math.Min(adxValue / 50, 1) * 100;

        if (plusDi > minusDi)
            adxScore = trendStrength;
        else
            adxScore = -trendStrength;

        // Volume (10% weight)
        double volumeScore = 0;
        double volRatio = _volumeRatio[index];

        if (volRatio > 1.5)
        {
            if (candle.Close > candle.Vwap)
                volumeScore = Math.Min((volRatio - 1) * 100, 100);
            else
                volumeScore = -Math.Min((volRatio - 1) * 100, 100);
        }
        else if (volRatio >= 0.5)
        {
            if (candle.Close > candle.Vwap)
                volumeScore = 25;
            else
                volumeScore = -25;
        }

        // ========================================================================
        // Enhanced Indicators (additional weight distributed)
        // ========================================================================
        
        // Bollinger Bands (5% weight) - Mean reversion signals
        double bollingerScore = GetBollingerScore(index);
        
        // Stochastic (5% weight) - Momentum oscillator
        double stochasticScore = GetStochasticScore(index);
        
        // CCI (3% weight) - Trend strength and reversals
        double cciScore = GetCciScore(index);
        
        // Williams %R (2% weight) - Overbought/oversold confirmation
        double williamsRScore = GetWilliamsRScore(index);
        
        // MFI (3% weight) - Volume-weighted RSI
        double mfiScore = GetMfiScore(index);
        
        // OBV (2% weight) - Volume trend confirmation
        double obvScore = GetObvScore(index);
        
        // ========================================================================
        // Sentiment Score (from news, earnings, analyst ratings)
        // Weight depends on confidence level
        // ========================================================================
        double sentimentWeight = _sentimentConfidence > 50 ? 0.10 : (_sentimentConfidence > 25 ? 0.05 : 0);
        double sentimentScore = _sentimentScore;
        
        // Adjust core weights when sentiment is factored in
        double coreWeightAdjustment = 1.0 - sentimentWeight - 0.20; // 20% for enhanced indicators

        // Calculate weighted total with enhanced indicators
        // Core indicators: 80% base (adjusted for sentiment), Enhanced: 20%, Sentiment: 0-10%
        double coreScore = 
            vwapScore * (0.15 * coreWeightAdjustment / 0.80) +
            emaScore * (0.20 * coreWeightAdjustment / 0.80) +
            rsiScore * (0.15 * coreWeightAdjustment / 0.80) +
            macdScore * (0.20 * coreWeightAdjustment / 0.80) +
            adxScore * (0.20 * coreWeightAdjustment / 0.80) +
            volumeScore * (0.10 * coreWeightAdjustment / 0.80);
        
        double enhancedScore =
            bollingerScore * 0.05 +
            stochasticScore * 0.05 +
            cciScore * 0.03 +
            williamsRScore * 0.02 +
            mfiScore * 0.03 +
            obvScore * 0.02;
        
        double finalScore = coreScore + enhancedScore + (sentimentScore * sentimentWeight);
        
        return Math.Clamp(finalScore, -100, 100);
    }
    
    /// <summary>
    /// Gets a detailed breakdown of all indicator scores for analysis.
    /// </summary>
    public Dictionary<string, double> GetIndicatorBreakdown(int index)
    {
        if (index < 0 || index >= _candles.Count)
            return new Dictionary<string, double>();
            
        var candle = _candles[index];
        
        return new Dictionary<string, double>
        {
            ["VWAP"] = candle.Vwap > 0 ? Math.Clamp((candle.Close - candle.Vwap) / candle.Vwap * 100 * 20, -100, 100) : 0,
            ["EMA_Stack"] = GetEmaStackScore(index),
            ["RSI"] = _rsi[index],
            ["RSI_Score"] = GetRsiScore(index),
            ["MACD"] = _macd[index],
            ["MACD_Signal"] = _signal[index],
            ["MACD_Score"] = GetMacdScore(index),
            ["ADX"] = _adx[index],
            ["+DI"] = _plusDi[index],
            ["-DI"] = _minusDi[index],
            ["ADX_Score"] = GetAdxScore(index),
            ["Volume_Ratio"] = _volumeRatio[index],
            ["Bollinger"] = GetBollingerScore(index),
            ["Stochastic_K"] = _stochasticK[index],
            ["Stochastic_D"] = _stochasticD[index],
            ["Stochastic_Score"] = GetStochasticScore(index),
            ["CCI"] = _cci[index],
            ["CCI_Score"] = GetCciScore(index),
            ["Williams_R"] = _williamsR[index],
            ["Williams_R_Score"] = GetWilliamsRScore(index),
            ["MFI"] = _mfi[index],
            ["MFI_Score"] = GetMfiScore(index),
            ["OBV_Score"] = GetObvScore(index),
            ["Momentum"] = _momentum[index],
            ["ROC"] = _roc[index],
            ["ATR"] = _atr[index],
            ["Sentiment"] = _sentimentScore,
            ["Sentiment_Confidence"] = _sentimentConfidence,
            ["Final_Score"] = CalculateMarketScore(index)
        };
    }
    
    private double GetEmaStackScore(int index)
    {
        if (index < 0 || index >= _candles.Count) return 0;
        var close = _candles[index].Close;
        
        int aboveCount = 0;
        if (close > _ema9[index]) aboveCount++;
        if (close > _ema21[index]) aboveCount++;
        if (close > _ema50[index]) aboveCount++;
        if (close > _ema200[index]) aboveCount++;
        
        bool bullishStack = _ema9[index] > _ema21[index] && _ema21[index] > _ema50[index];
        bool bearishStack = _ema9[index] < _ema21[index] && _ema21[index] < _ema50[index];
        
        double score = (aboveCount - 2) * 50;
        if (bullishStack) score += 25;
        if (bearishStack) score -= 25;
        
        return Math.Clamp(score, -100, 100);
    }
    
    private double GetRsiScore(int index)
    {
        if (index < 0 || index >= _rsi.Length) return 0;
        double rsi = _rsi[index];
        
        if (rsi <= 30) return 100 - (rsi - 30) * 3.33;
        if (rsi >= 70) return -(rsi - 70) * 3.33;
        return (rsi - 50) * 2;
    }
    
    private double GetMacdScore(int index)
    {
        if (index < 0 || index >= _macd.Length) return 0;
        
        double score = _macd[index] > _signal[index] ? 50 : -50;
        double histStrength = Math.Min(Math.Abs(_histogram[index]) / (Math.Abs(_macd[index]) + 0.001) * 100, 50);
        
        return Math.Clamp(score + (_histogram[index] > 0 ? histStrength : -histStrength), -100, 100);
    }
    
    private double GetAdxScore(int index)
    {
        if (index < 0 || index >= _adx.Length) return 0;
        
        double strength = Math.Min(_adx[index] / 50, 1) * 100;
        return _plusDi[index] > _minusDi[index] ? strength : -strength;
    }

    private static double[] CalculateEma(double[] prices, int period)
    {
        var ema = new double[prices.Length];
        if (prices.Length == 0) return ema;

        double multiplier = 2.0 / (period + 1);
        ema[0] = prices[0];

        for (int i = 1; i < prices.Length; i++)
        {
            ema[i] = (prices[i] - ema[i - 1]) * multiplier + ema[i - 1];
        }

        return ema;
    }

    private static double[] CalculateRsi(double[] prices, int period)
    {
        var rsi = new double[prices.Length];
        if (prices.Length < period + 1) return rsi;

        double avgGain = 0, avgLoss = 0;

        for (int i = 1; i <= period; i++)
        {
            double change = prices[i] - prices[i - 1];
            if (change > 0) avgGain += change;
            else avgLoss -= change;
        }

        avgGain /= period;
        avgLoss /= period;

        rsi[period] = avgLoss == 0 ? 100 : 100 - (100 / (1 + avgGain / avgLoss));

        for (int i = period + 1; i < prices.Length; i++)
        {
            double change = prices[i] - prices[i - 1];
            double gain = change > 0 ? change : 0;
            double loss = change < 0 ? -change : 0;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;

            rsi[i] = avgLoss == 0 ? 100 : 100 - (100 / (1 + avgGain / avgLoss));
        }

        return rsi;
    }

    private static (double[] adx, double[] plusDi, double[] minusDi) CalculateAdx(
        double[] highs, double[] lows, double[] closes, int period)
    {
        int len = closes.Length;
        var adx = new double[len];
        var plusDi = new double[len];
        var minusDi = new double[len];

        if (len < period * 2) return (adx, plusDi, minusDi);

        var tr = new double[len];
        var plusDm = new double[len];
        var minusDm = new double[len];

        for (int i = 1; i < len; i++)
        {
            double highDiff = highs[i] - highs[i - 1];
            double lowDiff = lows[i - 1] - lows[i];

            plusDm[i] = highDiff > lowDiff && highDiff > 0 ? highDiff : 0;
            minusDm[i] = lowDiff > highDiff && lowDiff > 0 ? lowDiff : 0;

            double hl = highs[i] - lows[i];
            double hc = Math.Abs(highs[i] - closes[i - 1]);
            double lc = Math.Abs(lows[i] - closes[i - 1]);
            tr[i] = Math.Max(hl, Math.Max(hc, lc));
        }

        var smoothTr = CalculateWilderSmoothing(tr, period);
        var smoothPlusDm = CalculateWilderSmoothing(plusDm, period);
        var smoothMinusDm = CalculateWilderSmoothing(minusDm, period);

        var dx = new double[len];

        for (int i = period; i < len; i++)
        {
            plusDi[i] = smoothTr[i] != 0 ? 100 * smoothPlusDm[i] / smoothTr[i] : 0;
            minusDi[i] = smoothTr[i] != 0 ? 100 * smoothMinusDm[i] / smoothTr[i] : 0;

            double diSum = plusDi[i] + minusDi[i];
            dx[i] = diSum != 0 ? 100 * Math.Abs(plusDi[i] - minusDi[i]) / diSum : 0;
        }

        var smoothDx = CalculateWilderSmoothing(dx, period);
        for (int i = 0; i < len; i++)
            adx[i] = smoothDx[i];

        return (adx, plusDi, minusDi);
    }

    private static double[] CalculateWilderSmoothing(double[] data, int period)
    {
        var result = new double[data.Length];
        if (data.Length < period) return result;

        double sum = 0;
        for (int i = 0; i < period; i++)
            sum += data[i];

        result[period - 1] = sum / period;

        for (int i = period; i < data.Length; i++)
        {
            result[i] = (result[i - 1] * (period - 1) + data[i]) / period;
        }

        return result;
    }

    private static (double[] macd, double[] signal, double[] histogram) CalculateMacd(
        double[] prices, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        var fastEma = CalculateEma(prices, fastPeriod);
        var slowEma = CalculateEma(prices, slowPeriod);

        var macd = new double[prices.Length];
        for (int i = 0; i < prices.Length; i++)
            macd[i] = fastEma[i] - slowEma[i];

        var signal = CalculateEma(macd, signalPeriod);

        var histogram = new double[prices.Length];
        for (int i = 0; i < prices.Length; i++)
            histogram[i] = macd[i] - signal[i];

        return (macd, signal, histogram);
    }

    private static double[] CalculateVolumeRatio(double[] volumes, int period)
    {
        var ratio = new double[volumes.Length];
        if (volumes.Length < period) return ratio;

        double sum = 0;
        for (int i = 0; i < period; i++)
            sum += volumes[i];

        for (int i = period; i < volumes.Length; i++)
        {
            double avg = sum / period;
            ratio[i] = avg > 0 ? volumes[i] / avg : 1;
            sum = sum - volumes[i - period] + volumes[i];
        }

        return ratio;
    }

    private static double[] CalculateAtr(double[] highs, double[] lows, double[] closes, int period)
    {
        var atr = new double[closes.Length];
        if (closes.Length < period + 1) return atr;

        var tr = new double[closes.Length];
        tr[0] = highs[0] - lows[0];

        for (int i = 1; i < closes.Length; i++)
        {
            double hl = highs[i] - lows[i];
            double hc = Math.Abs(highs[i] - closes[i - 1]);
            double lc = Math.Abs(lows[i] - closes[i - 1]);
            tr[i] = Math.Max(hl, Math.Max(hc, lc));
        }

        double sum = 0;
        for (int i = 0; i < period; i++)
            sum += tr[i];

        atr[period - 1] = sum / period;

        for (int i = period; i < closes.Length; i++)
        {
            atr[i] = (atr[i - 1] * (period - 1) + tr[i]) / period;
        }

        return atr;
    }
    
    // ========================================================================
    // Additional Indicator Calculations
    // ========================================================================
    
    /// <summary>
    /// Calculates Bollinger Bands (upper, middle, lower).
    /// </summary>
    private static (double[] upper, double[] middle, double[] lower) CalculateBollingerBands(
        double[] prices, int period, double stdDevMultiplier)
    {
        int len = prices.Length;
        var upper = new double[len];
        var middle = new double[len];
        var lower = new double[len];
        
        if (len < period) return (upper, middle, lower);
        
        for (int i = period - 1; i < len; i++)
        {
            // Calculate SMA (middle band)
            double sum = 0;
            for (int j = i - period + 1; j <= i; j++)
                sum += prices[j];
            
            double sma = sum / period;
            middle[i] = sma;
            
            // Calculate standard deviation
            double sumSquares = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                double diff = prices[j] - sma;
                sumSquares += diff * diff;
            }
            
            double stdDev = Math.Sqrt(sumSquares / period);
            
            upper[i] = sma + (stdDev * stdDevMultiplier);
            lower[i] = sma - (stdDev * stdDevMultiplier);
        }
        
        return (upper, middle, lower);
    }
    
    /// <summary>
    /// Calculates Stochastic Oscillator (%K and %D).
    /// </summary>
    private static (double[] k, double[] d) CalculateStochastic(
        double[] highs, double[] lows, double[] closes, int kPeriod, int dPeriod)
    {
        int len = closes.Length;
        var k = new double[len];
        var d = new double[len];
        
        if (len < kPeriod) return (k, d);
        
        for (int i = kPeriod - 1; i < len; i++)
        {
            double highestHigh = double.MinValue;
            double lowestLow = double.MaxValue;
            
            for (int j = i - kPeriod + 1; j <= i; j++)
            {
                if (highs[j] > highestHigh) highestHigh = highs[j];
                if (lows[j] < lowestLow) lowestLow = lows[j];
            }
            
            double range = highestHigh - lowestLow;
            k[i] = range > 0 ? ((closes[i] - lowestLow) / range) * 100 : 50;
        }
        
        // %D is SMA of %K
        d = CalculateSma(k, dPeriod);
        
        return (k, d);
    }
    
    /// <summary>
    /// Simple Moving Average for internal use.
    /// </summary>
    private static double[] CalculateSma(double[] data, int period)
    {
        var sma = new double[data.Length];
        if (data.Length < period) return sma;
        
        double sum = 0;
        for (int i = 0; i < period; i++)
            sum += data[i];
        
        sma[period - 1] = sum / period;
        
        for (int i = period; i < data.Length; i++)
        {
            sum = sum - data[i - period] + data[i];
            sma[i] = sum / period;
        }
        
        return sma;
    }
    
    /// <summary>
    /// Calculates On-Balance Volume (OBV).
    /// </summary>
    private static double[] CalculateObv(double[] closes, double[] volumes)
    {
        var obv = new double[closes.Length];
        if (closes.Length == 0) return obv;
        
        obv[0] = volumes[0];
        
        for (int i = 1; i < closes.Length; i++)
        {
            if (closes[i] > closes[i - 1])
                obv[i] = obv[i - 1] + volumes[i];
            else if (closes[i] < closes[i - 1])
                obv[i] = obv[i - 1] - volumes[i];
            else
                obv[i] = obv[i - 1];
        }
        
        return obv;
    }
    
    /// <summary>
    /// Calculates Commodity Channel Index (CCI).
    /// </summary>
    private static double[] CalculateCci(double[] highs, double[] lows, double[] closes, int period)
    {
        int len = closes.Length;
        var cci = new double[len];
        
        if (len < period) return cci;
        
        var typicalPrices = new double[len];
        for (int i = 0; i < len; i++)
            typicalPrices[i] = (highs[i] + lows[i] + closes[i]) / 3;
        
        for (int i = period - 1; i < len; i++)
        {
            // Calculate SMA of typical price
            double sum = 0;
            for (int j = i - period + 1; j <= i; j++)
                sum += typicalPrices[j];
            
            double sma = sum / period;
            
            // Calculate mean deviation
            double meanDev = 0;
            for (int j = i - period + 1; j <= i; j++)
                meanDev += Math.Abs(typicalPrices[j] - sma);
            
            meanDev /= period;
            
            // CCI = (Typical Price - SMA) / (0.015 * Mean Deviation)
            cci[i] = meanDev != 0 ? (typicalPrices[i] - sma) / (0.015 * meanDev) : 0;
        }
        
        return cci;
    }
    
    /// <summary>
    /// Calculates Williams %R.
    /// </summary>
    private static double[] CalculateWilliamsR(double[] highs, double[] lows, double[] closes, int period)
    {
        int len = closes.Length;
        var wr = new double[len];
        
        if (len < period) return wr;
        
        for (int i = period - 1; i < len; i++)
        {
            double highestHigh = double.MinValue;
            double lowestLow = double.MaxValue;
            
            for (int j = i - period + 1; j <= i; j++)
            {
                if (highs[j] > highestHigh) highestHigh = highs[j];
                if (lows[j] < lowestLow) lowestLow = lows[j];
            }
            
            double range = highestHigh - lowestLow;
            // Williams %R = (Highest High - Close) / (Highest High - Lowest Low) * -100
            wr[i] = range > 0 ? ((highestHigh - closes[i]) / range) * -100 : -50;
        }
        
        return wr;
    }
    
    /// <summary>
    /// Calculates Money Flow Index (MFI) - Volume-weighted RSI.
    /// </summary>
    private static double[] CalculateMfi(double[] highs, double[] lows, double[] closes, double[] volumes, int period)
    {
        int len = closes.Length;
        var mfi = new double[len];
        
        if (len < period + 1) return mfi;
        
        var typicalPrices = new double[len];
        var rawMoneyFlow = new double[len];
        
        for (int i = 0; i < len; i++)
        {
            typicalPrices[i] = (highs[i] + lows[i] + closes[i]) / 3;
            rawMoneyFlow[i] = typicalPrices[i] * volumes[i];
        }
        
        for (int i = period; i < len; i++)
        {
            double positiveFlow = 0;
            double negativeFlow = 0;
            
            for (int j = i - period + 1; j <= i; j++)
            {
                if (typicalPrices[j] > typicalPrices[j - 1])
                    positiveFlow += rawMoneyFlow[j];
                else if (typicalPrices[j] < typicalPrices[j - 1])
                    negativeFlow += rawMoneyFlow[j];
            }
            
            if (negativeFlow == 0)
                mfi[i] = 100;
            else
            {
                double moneyRatio = positiveFlow / negativeFlow;
                mfi[i] = 100 - (100 / (1 + moneyRatio));
            }
        }
        
        return mfi;
    }
    
    /// <summary>
    /// Calculates Rate of Change (ROC) - Percentage price change.
    /// </summary>
    private static double[] CalculateRoc(double[] prices, int period)
    {
        var roc = new double[prices.Length];
        
        for (int i = period; i < prices.Length; i++)
        {
            double prevPrice = prices[i - period];
            roc[i] = prevPrice != 0 ? ((prices[i] - prevPrice) / prevPrice) * 100 : 0;
        }
        
        return roc;
    }
    
    /// <summary>
    /// Calculates Momentum (price difference over N periods).
    /// </summary>
    private static double[] CalculateMomentum(double[] prices, int period)
    {
        var momentum = new double[prices.Length];
        
        for (int i = period; i < prices.Length; i++)
        {
            momentum[i] = prices[i] - prices[i - period];
        }
        
        return momentum;
    }
    
    // ========================================================================
    // INDICATOR CONFIRMATION (for Optimized mode)
    // ========================================================================
    
    /// <summary>
    /// Counts how many indicator categories agree on direction.
    /// Returns positive count for bullish agreement, negative for bearish.
    /// </summary>
    public (int BullishCount, int BearishCount, int TotalCategories) GetIndicatorConfirmation(int index)
    {
        if (index < 0 || index >= _candles.Count)
            return (0, 0, 0);
            
        int bullish = 0;
        int bearish = 0;
        const int totalCategories = 12;
        
        var candle = _candles[index];
        
        // 1. VWAP Position
        if (candle.Vwap > 0)
        {
            if (candle.Close > candle.Vwap) bullish++;
            else bearish++;
        }
        
        // 2. EMA Stack (short-term)
        if (candle.Close > _ema9[index] && candle.Close > _ema21[index]) bullish++;
        else if (candle.Close < _ema9[index] && candle.Close < _ema21[index]) bearish++;
        
        // 3. EMA Stack (long-term)
        if (_ema9[index] > _ema21[index] && _ema21[index] > _ema50[index]) bullish++;
        else if (_ema9[index] < _ema21[index] && _ema21[index] < _ema50[index]) bearish++;
        
        // 4. RSI
        if (_rsi[index] > 50 && _rsi[index] < 70) bullish++;
        else if (_rsi[index] < 50 && _rsi[index] > 30) bearish++;
        else if (_rsi[index] <= 30) bullish++; // Oversold = bullish reversal
        else if (_rsi[index] >= 70) bearish++; // Overbought = bearish reversal
        
        // 5. MACD Signal
        if (_macd[index] > _signal[index]) bullish++;
        else bearish++;
        
        // 6. MACD Histogram Momentum
        if (_histogram[index] > 0 && index > 0 && _histogram[index] > _histogram[index - 1]) bullish++;
        else if (_histogram[index] < 0 && index > 0 && _histogram[index] < _histogram[index - 1]) bearish++;
        
        // 7. ADX/DI Direction
        if (_plusDi[index] > _minusDi[index]) bullish++;
        else bearish++;
        
        // 8. Volume Confirmation
        if (_volumeRatio[index] > 1.2)
        {
            if (candle.Close > candle.Vwap) bullish++;
            else bearish++;
        }
        
        // 9. Bollinger Bands
        double bbPos = _bollingerUpper[index] - _bollingerLower[index];
        if (bbPos > 0)
        {
            double pricePos = (candle.Close - _bollingerLower[index]) / bbPos;
            if (pricePos > 0.6) bullish++;
            else if (pricePos < 0.4) bearish++;
        }
        
        // 10. Stochastic
        if (_stochasticK[index] > _stochasticD[index] && _stochasticK[index] < 80) bullish++;
        else if (_stochasticK[index] < _stochasticD[index] && _stochasticK[index] > 20) bearish++;
        
        // 11. CCI
        if (_cci[index] > 0 && _cci[index] < 200) bullish++;
        else if (_cci[index] < 0 && _cci[index] > -200) bearish++;
        
        // 12. MFI (Money Flow)
        if (_mfi[index] > 50 && _mfi[index] < 80) bullish++;
        else if (_mfi[index] < 50 && _mfi[index] > 20) bearish++;
        
        return (bullish, bearish, totalCategories);
    }
    
    /// <summary>
    /// Gets a recommendation string based on indicator confirmation.
    /// </summary>
    public string GetConfirmationSummary(int index)
    {
        var (bullish, bearish, total) = GetIndicatorConfirmation(index);
        double bullishPct = (double)bullish / total * 100;
        double bearishPct = (double)bearish / total * 100;
        
        if (bullish >= 8) return $"STRONG BUY ({bullish}/{total} bullish)";
        if (bullish >= 6) return $"BUY ({bullish}/{total} bullish)";
        if (bearish >= 8) return $"STRONG SELL ({bearish}/{total} bearish)";
        if (bearish >= 6) return $"SELL ({bearish}/{total} bearish)";
        return $"NEUTRAL (Bull:{bullish} Bear:{bearish} / {total})";
    }
    
    /// <summary>
    /// Gets highest price reached since the specified index.
    /// </summary>
    public double GetHighestSince(int fromIndex, int toIndex)
    {
        double highest = 0;
        for (int i = fromIndex; i <= Math.Min(toIndex, _candles.Count - 1); i++)
        {
            if (_candles[i].High > highest) highest = _candles[i].High;
        }
        return highest;
    }
    
    /// <summary>
    /// Gets lowest price reached since the specified index.
    /// </summary>
    public double GetLowestSince(int fromIndex, int toIndex)
    {
        double lowest = double.MaxValue;
        for (int i = fromIndex; i <= Math.Min(toIndex, _candles.Count - 1); i++)
        {
            if (_candles[i].Low < lowest) lowest = _candles[i].Low;
        }
        return lowest == double.MaxValue ? 0 : lowest;
    }
}
