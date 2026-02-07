// ============================================================================
// Autonomous Backtest Runner - Command-line tool for quick backtesting
// ============================================================================
//
// USAGE:
//   Called from the console to run autonomous trading backtests.
//   Can work with synthetic data (offline) or real IBKR data (when connected).
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
/// Provides methods to run autonomous backtests from different data sources.
/// </summary>
public static class AutonomousBacktestRunner
{
    /// <summary>
    /// Runs a backtest using real historical data from IBKR.
    /// Requires active connection to IB Gateway.
    /// </summary>
    /// <param name="histService">The historical data service (connected to IBKR).</param>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="date">The date to backtest (must be a past trading day).</param>
    /// <param name="startingCapital">Starting capital in dollars.</param>
    /// <param name="mode">Trading mode (Conservative, Balanced, Aggressive).</param>
    /// <param name="allowShort">Allow short positions.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete backtest result with real market data.</returns>
    public static async Task<AutonomousBacktestResult> RunFromIbkrAsync(
        HistoricalDataService histService,
        string symbol,
        DateOnly date,
        decimal startingCapital = 1000.00m,
        AutonomousMode mode = AutonomousMode.Balanced,
        bool allowShort = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (histService == null)
            throw new ArgumentNullException(nameof(histService), "Historical data service required for IBKR data");

        progress?.Report($"Fetching historical data for {symbol} on {date:yyyy-MM-dd}...");

        // Fetch full day of 1-minute bars (extended hours = ~960 bars max)
        // Request 5 days to ensure we get data for the specific date
        int barsNeeded = 960;

        try
        {
            var barCount = await histService.FetchHistoricalDataAsync(
                symbol,
                barsNeeded,
                BarSize.Minutes1,
                HistoricalDataType.Trades,
                useRTH: false, // Include extended hours
                endDate: null,
                cancellationToken: cancellationToken);

            progress?.Report($"Fetched {barCount} bars from IBKR");

            var allBars = histService.Store.GetBars(symbol);

            if (allBars.Count == 0)
            {
                throw new InvalidOperationException($"No historical data received for {symbol}");
            }

            // Filter to the specific date
            var dateBars = allBars
                .Where(b => DateOnly.FromDateTime(b.Time) == date)
                .OrderBy(b => b.Time)
                .ToList();

            if (dateBars.Count == 0)
            {
                // Show available dates
                var availableDates = allBars
                    .Select(b => DateOnly.FromDateTime(b.Time))
                    .Distinct()
                    .OrderByDescending(d => d)
                    .Take(10);

                throw new InvalidOperationException(
                    $"No bars found for {symbol} on {date:yyyy-MM-dd}. " +
                    $"Available dates: {string.Join(", ", availableDates)}");
            }

            progress?.Report($"Found {dateBars.Count} bars for {date:yyyy-MM-dd}. Running simulation...");

            // Run backtest with real data
            return RunFromBars(dateBars, symbol, date, startingCapital, mode, allowShort);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                $"Timeout fetching historical data for {symbol}. " +
                "Ensure IB Gateway is connected and Historical Data Farm is active.");
        }
    }

    /// <summary>
    /// Runs a backtest using historical bars from the HistoricalDataStore.
    /// </summary>
    /// <param name="bars">List of historical bars (1-minute OHLCV).</param>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="date">The date to backtest.</param>
    /// <param name="startingCapital">Starting capital in dollars.</param>
    /// <param name="mode">Trading mode (Conservative, Balanced, Aggressive).</param>
    /// <param name="allowShort">Allow short positions.</param>
    /// <returns>Complete backtest result.</returns>
    public static AutonomousBacktestResult RunFromBars(
        IReadOnlyList<HistoricalBar> bars,
        string symbol,
        DateOnly date,
        decimal startingCapital = 1000.00m,
        AutonomousMode mode = AutonomousMode.Balanced,
        bool allowShort = true)
    {
        // Convert HistoricalBar to BackTestCandle
        var candles = bars.Select(b => new BackTestCandle
        {
            Timestamp = b.Time,
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume,
            Vwap = b.Vwap ?? 0
        }).ToList();

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = startingCapital,
            Mode = mode,
            AllowShort = allowShort
        };

        var backtester = new AutonomousBacktester(null!);
        return backtester.RunWithCandles(symbol, date, candles, config);
    }

    /// <summary>
    /// Generates synthetic data and runs a backtest - for quick testing without IBKR.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="date">The date to simulate.</param>
    /// <param name="startingCapital">Starting capital in dollars.</param>
    /// <param name="scenario">Price scenario to generate.</param>
    /// <param name="mode">Trading mode.</param>
    /// <param name="allowShort">Allow short positions.</param>
    /// <returns>Complete backtest result.</returns>
    public static AutonomousBacktestResult RunSynthetic(
        string symbol,
        DateOnly date,
        decimal startingCapital = 1000.00m,
        PriceScenario scenario = PriceScenario.Volatile,
        AutonomousMode mode = AutonomousMode.Balanced,
        bool allowShort = true)
    {
        var candles = GenerateSyntheticCandles(symbol, date, scenario);

        var config = new AutonomousBacktestConfig
        {
            StartingCapital = startingCapital,
            Mode = mode,
            AllowShort = allowShort
        };

        var backtester = new AutonomousBacktester(null!);
        return backtester.RunWithCandles(symbol, date, candles, config);
    }

    /// <summary>
    /// Runs a multi-day backtest using IBKR historical data.
    /// </summary>
    /// <param name="histService">The historical data service.</param>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="startDate">Start date (inclusive).</param>
    /// <param name="endDate">End date (inclusive).</param>
    /// <param name="startingCapital">Starting capital in dollars.</param>
    /// <param name="mode">Trading mode.</param>
    /// <param name="allowShort">Allow short positions.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of results across all days.</returns>
    public static async Task<MultiDayBacktestResult> RunMultiDayFromIbkrAsync(
        HistoricalDataService histService,
        string symbol,
        DateOnly startDate,
        DateOnly endDate,
        decimal startingCapital = 1000.00m,
        AutonomousMode mode = AutonomousMode.Balanced,
        bool allowShort = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AutonomousBacktestResult>();
        var currentDate = startDate;
        decimal runningCapital = startingCapital;

        while (currentDate <= endDate)
        {
            // Skip weekends
            if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday)
            {
                currentDate = currentDate.AddDays(1);
                continue;
            }

            try
            {
                progress?.Report($"Processing {currentDate:yyyy-MM-dd}...");

                var result = await RunFromIbkrAsync(
                    histService, symbol, currentDate, runningCapital, mode, allowShort, 
                    null, cancellationToken);

                results.Add(result);
                runningCapital = result.EndingCapital;

                progress?.Report($"  {currentDate:yyyy-MM-dd}: {result.TotalTrades} trades, " +
                               $"P&L: ${result.TotalPnL:+0.00;-0.00}, Capital: ${runningCapital:N2}");
            }
            catch (Exception ex)
            {
                progress?.Report($"  {currentDate:yyyy-MM-dd}: SKIPPED - {ex.Message}");
            }

            // IBKR pacing - wait between requests
            await Task.Delay(1000, cancellationToken);
            currentDate = currentDate.AddDays(1);
        }

        return new MultiDayBacktestResult
        {
            Symbol = symbol,
            StartDate = startDate,
            EndDate = endDate,
            StartingCapital = startingCapital,
            EndingCapital = runningCapital,
            Mode = mode,
            DailyResults = results
        };
    }

    /// <summary>
    /// Runs multiple scenarios and compares results.
    /// </summary>
    public static string RunScenarioComparison(
        string symbol,
        DateOnly date,
        decimal startingCapital = 1000.00m)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"╔══════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine($"║  AUTONOMOUS TRADING SCENARIO COMPARISON                                   ║");
        sb.AppendLine($"║  Symbol: {symbol,-10} | Date: {date:yyyy-MM-dd} | Capital: ${startingCapital:N2,-10}     ║");
        sb.AppendLine($"╠══════════════════════════════════════════════════════════════════════════╣");
        sb.AppendLine($"║  {"Scenario",-12} {"Mode",-12} {"Trades",7} {"Win %",7} {"PnL",12} {"Return",9} ║");
        sb.AppendLine($"╠══════════════════════════════════════════════════════════════════════════╣");

        var scenarios = new[] { PriceScenario.Uptrend, PriceScenario.Downtrend, PriceScenario.Volatile, PriceScenario.Range };
        var modes = new[] { AutonomousMode.Conservative, AutonomousMode.Balanced, AutonomousMode.Aggressive };

        foreach (var scenario in scenarios)
        {
            var candles = GenerateSyntheticCandles(symbol, date, scenario);

            foreach (var mode in modes)
            {
                var config = new AutonomousBacktestConfig
                {
                    StartingCapital = startingCapital,
                    Mode = mode,
                    AllowShort = true
                };

                var backtester = new AutonomousBacktester(null!);
                var result = backtester.RunWithCandles(symbol, date, candles, config);

                string pnlStr = result.TotalPnL >= 0 ? $"+${result.TotalPnL:N2}" : $"-${Math.Abs(result.TotalPnL):N2}";
                string retStr = result.TotalReturnPercent >= 0 ? $"+{result.TotalReturnPercent:N2}%" : $"{result.TotalReturnPercent:N2}%";

                sb.AppendLine($"║  {scenario,-12} {mode,-12} {result.TotalTrades,7} {result.WinRate,6:N1}% {pnlStr,12} {retStr,9} ║");
            }
        }

        sb.AppendLine($"╚══════════════════════════════════════════════════════════════════════════╝");

        return sb.ToString();
    }

    /// <summary>
    /// Runs a quick mode comparison on a single scenario.
    /// </summary>
    public static string RunModeComparison(
        string symbol,
        DateOnly date,
        decimal startingCapital = 1000.00m,
        PriceScenario scenario = PriceScenario.Volatile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n=== MODE COMPARISON: {symbol} on {date:yyyy-MM-dd} ({scenario}) ===\n");
        sb.AppendLine($"{"Mode",-15} {"Trades",8} {"Win %",8} {"PnL",12} {"Return %",10} {"MaxDD",10}");
        sb.AppendLine(new string('-', 65));

        var candles = GenerateSyntheticCandles(symbol, date, scenario);
        var modes = new[] { AutonomousMode.Conservative, AutonomousMode.Balanced, AutonomousMode.Aggressive };

        foreach (var mode in modes)
        {
            var config = new AutonomousBacktestConfig
            {
                StartingCapital = startingCapital,
                Mode = mode,
                AllowShort = true
            };

            var backtester = new AutonomousBacktester(null!);
            var result = backtester.RunWithCandles(symbol, date, candles, config);

            sb.AppendLine($"{mode,-15} {result.TotalTrades,8} {result.WinRate,7:N1}% ${result.TotalPnL,10:N2} {result.TotalReturnPercent,9:N2}% ${result.MaxDrawdown,8:N2}");
        }

        return sb.ToString();
    }

    private static List<BackTestCandle> GenerateSyntheticCandles(string symbol, DateOnly date, PriceScenario scenario)
    {
        var random = new Random(symbol.GetHashCode() + date.GetHashCode());
        var candles = new List<BackTestCandle>();

        // Get a reasonable starting price based on symbol
        double basePrice = symbol.ToUpperInvariant() switch
        {
            "AAPL" => 180.0,
            "TSLA" => 250.0,
            "NVDA" => 130.0,
            "MSFT" => 400.0,
            "GOOG" or "GOOGL" => 175.0,
            "META" => 500.0,
            "AMZN" => 185.0,
            "AMD" => 150.0,
            "SPY" => 500.0,
            "QQQ" => 450.0,
            _ => 50.0 + random.NextDouble() * 100
        };

        int candleCount = 390; // RTH = 6.5 hours
        var startTime = date.ToDateTime(new TimeOnly(9, 30));

        double price = basePrice;
        double volatility = basePrice * 0.002; // 0.2% per bar base volatility

        for (int i = 0; i < candleCount; i++)
        {
            var timestamp = startTime.AddMinutes(i);

            // Apply scenario-specific price movement
            double targetChange = scenario switch
            {
                PriceScenario.Uptrend => basePrice * 0.05 / candleCount, // +5% over day
                PriceScenario.Downtrend => -basePrice * 0.05 / candleCount, // -5% over day
                PriceScenario.Volatile => Math.Sin(i * Math.PI / 30) * basePrice * 0.02, // Oscillate ±2%
                PriceScenario.Range => Math.Sin(i * Math.PI / 60) * basePrice * 0.01, // Slower oscillation ±1%
                _ => 0
            };

            // Higher volatility at open and close
            double periodVolatility = volatility;
            if (i < 30) periodVolatility *= 2.5;
            else if (i > 360) periodVolatility *= 1.8;

            double noise = (random.NextDouble() * 2 - 1) * periodVolatility;
            double open = price;
            price += targetChange + noise;
            double close = price;

            double high = Math.Max(open, close) + random.NextDouble() * periodVolatility;
            double low = Math.Min(open, close) - random.NextDouble() * periodVolatility;

            // Ensure OHLC consistency
            high = Math.Max(high, Math.Max(open, close));
            low = Math.Min(low, Math.Min(open, close));

            // Volume - higher at open/close
            long volume = 10000 + random.Next(50000);
            if (i < 30 || i > 360) volume = (long)(volume * 2.5);

            candles.Add(new BackTestCandle
            {
                Timestamp = timestamp,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = volume
            });
        }

        return candles;
    }
}

/// <summary>
/// Price scenarios for synthetic data generation.
/// </summary>
public enum PriceScenario
{
    /// <summary>Steady upward trend (+5% over day)</summary>
    Uptrend,

    /// <summary>Steady downward trend (-5% over day)</summary>
    Downtrend,

    /// <summary>High volatility with oscillations</summary>
    Volatile,

    /// <summary>Narrow range-bound movement</summary>
    Range
}

/// <summary>
/// Result of a multi-day backtest.
/// </summary>
public sealed class MultiDayBacktestResult
{
    public required string Symbol { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required decimal StartingCapital { get; init; }
    public required decimal EndingCapital { get; init; }
    public required AutonomousMode Mode { get; init; }
    public List<AutonomousBacktestResult> DailyResults { get; init; } = [];

    // Aggregate metrics
    public decimal TotalPnL => EndingCapital - StartingCapital;
    public decimal TotalReturnPercent => StartingCapital > 0 ? TotalPnL / StartingCapital * 100 : 0;
    public int TotalTrades => DailyResults.Sum(r => r.TotalTrades);
    public int TotalWins => DailyResults.Sum(r => r.WinCount);
    public int TotalLosses => DailyResults.Sum(r => r.LossCount);
    public decimal WinRate => TotalTrades > 0 ? (decimal)TotalWins / TotalTrades * 100 : 0;
    public int TradingDays => DailyResults.Count;
    public int ProfitableDays => DailyResults.Count(r => r.TotalPnL > 0);
    public int LosingDays => DailyResults.Count(r => r.TotalPnL < 0);

    public decimal AvgDailyPnL => TradingDays > 0 ? TotalPnL / TradingDays : 0;
    public decimal BestDay => DailyResults.Count > 0 ? DailyResults.Max(r => r.TotalPnL) : 0;
    public decimal WorstDay => DailyResults.Count > 0 ? DailyResults.Min(r => r.TotalPnL) : 0;

    public decimal MaxDrawdown
    {
        get
        {
            decimal peak = StartingCapital;
            decimal maxDD = 0;
            decimal running = StartingCapital;

            foreach (var day in DailyResults)
            {
                running = day.EndingCapital;
                if (running > peak) peak = running;
                decimal dd = peak - running;
                if (dd > maxDD) maxDD = dd;
            }

            return maxDD;
        }
    }

    public decimal MaxDrawdownPercent => StartingCapital > 0 ? MaxDrawdown / StartingCapital * 100 : 0;

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"""
            +======================================================================+
            | MULTI-DAY AUTONOMOUS TRADING BACKTEST                                |
            +======================================================================+
            | Symbol:     {Symbol,-10} | Mode: {Mode}
            | Period:     {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}
            +----------------------------------------------------------------------+
            | CAPITAL                                                              |
            +----------------------------------------------------------------------+
            | Starting:   ${StartingCapital,10:N2}
            | Ending:     ${EndingCapital,10:N2}
            | Net P&L:    ${TotalPnL,10:N2} ({TotalReturnPercent:+0.00;-0.00}%)
            +----------------------------------------------------------------------+
            | PERFORMANCE                                                          |
            +----------------------------------------------------------------------+
            | Trading Days:     {TradingDays,5}  | Profitable Days: {ProfitableDays,5}
            | Total Trades:     {TotalTrades,5}  | Win Rate:        {WinRate,5:N1}%
            | Avg Daily P&L:  ${AvgDailyPnL,8:N2}
            | Best Day:       ${BestDay,8:N2}  | Worst Day:     ${WorstDay,8:N2}
            | Max Drawdown:   ${MaxDrawdown,8:N2} ({MaxDrawdownPercent:N1}%)
            +----------------------------------------------------------------------+
            | DAILY BREAKDOWN                                                      |
            +----------------------------------------------------------------------+
            | Date         Trades    Win%       PnL    Capital                     |
            +----------------------------------------------------------------------+
            """);

        foreach (var day in DailyResults)
        {
            string pnlStr = day.TotalPnL >= 0 ? $"+${day.TotalPnL:N2}" : $"-${Math.Abs(day.TotalPnL):N2}";
            sb.AppendLine($"| {day.Date:yyyy-MM-dd}     {day.TotalTrades,4}   {day.WinRate,5:N1}%  {pnlStr,10}  ${day.EndingCapital,10:N2}");
        }

        sb.AppendLine("+======================================================================+");

        return sb.ToString();
    }
}
