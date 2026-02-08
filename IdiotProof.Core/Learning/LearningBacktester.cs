// ============================================================================
// LearningBacktester - 100x Iterative Learning System
// ============================================================================
//
// Runs backtesting 100 times on a ticker, refining parameters each iteration.
// Each ticker has a unique "fingerprint" - patterns that work for AAPL
// may not work for NVDA. We discover these through repetition.
//
// PHILOSOPHY:
// - Every indicator has sliders that can be tuned per-ticker
// - What worked yesterday helps predict what works tomorrow
// - BUT we NEVER peek at future candles during simulation
// - Each moment uses only information available at that time
// - Results are saved and used to improve the next iteration
//
// DATA FILES:
// - TICKER.backtesting.json: Raw backtest results for learning
// - TICKER.profile.json: Refined configuration for live trading
//
// CORE RULE: NEVER TAKE LOSSES
// - A stock will eventually recover
// - Taking a loss is permanently lost money
// - Hold until profitable
// - Average down when conditions favor it
//
// ============================================================================

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Services;
using IdiotProof.BackTesting.Learning;
using IdiotProof.BackTesting.Models;
using IdiotProof.Core.Settings;
using AutonomousMode = IdiotProof.BackTesting.Services.AutonomousMode;

namespace IdiotProof.Core.Learning;

// ============================================================================
// Tunable Parameters - The "sliders" we adjust per ticker
// ============================================================================

/// <summary>
/// Tunable parameters for a ticker - these are the "sliders" we adjust.
/// </summary>
public sealed class TunableParameters
{
    // Entry thresholds
    public double LongEntryThreshold { get; set; } = 65;
    public double LodBounceThreshold { get; set; } = 30;
    public double NearLodPercent { get; set; } = 1.5;
    
    // Exit thresholds  
    public double NearHodPercent { get; set; } = 0.5;
    public double MomentumExitRsi { get; set; } = 70;
    public double MinProfitToTake { get; set; } = 0.5;
    
    // Indicator weights (how much each indicator matters for this ticker)
    public double RsiWeight { get; set; } = 1.0;
    public double MacdWeight { get; set; } = 1.0;
    public double VwapWeight { get; set; } = 1.0;
    public double EmaWeight { get; set; } = 1.0;
    public double VolumeWeight { get; set; } = 1.0;
    public double AdxWeight { get; set; } = 1.0;
    
    // Time preferences
    public List<int> BestHours { get; set; } = [9, 10, 11, 14, 15];
    public List<int> AvoidHours { get; set; } = [12, 13];
    public int SkipOpeningMinutes { get; set; } = 5;
    
    // Averaging down
    public bool AllowAverageDown { get; set; } = true;
    public int MaxAverageDowns { get; set; } = 3;
    public double MinDropForAverageDown { get; set; } = 2.0;
    
    /// <summary>
    /// Creates a copy with slight random variation for exploration.
    /// </summary>
    public TunableParameters Mutate(Random rng, double rate = 0.1)
    {
        return new TunableParameters
        {
            LongEntryThreshold = Clamp(LongEntryThreshold + Vary(rng, 5, rate), 30, 90),
            LodBounceThreshold = Clamp(LodBounceThreshold + Vary(rng, 5, rate), 15, 50),
            NearLodPercent = Clamp(NearLodPercent + Vary(rng, 0.5, rate), 0.5, 3.0),
            NearHodPercent = Clamp(NearHodPercent + Vary(rng, 0.3, rate), 0.2, 2.0),
            MomentumExitRsi = Clamp(MomentumExitRsi + Vary(rng, 5, rate), 60, 85),
            MinProfitToTake = Clamp(MinProfitToTake + Vary(rng, 0.2, rate), 0.2, 1.5),
            RsiWeight = Clamp(RsiWeight + Vary(rng, 0.2, rate), 0.2, 2.0),
            MacdWeight = Clamp(MacdWeight + Vary(rng, 0.2, rate), 0.2, 2.0),
            VwapWeight = Clamp(VwapWeight + Vary(rng, 0.2, rate), 0.2, 2.0),
            EmaWeight = Clamp(EmaWeight + Vary(rng, 0.2, rate), 0.2, 2.0),
            VolumeWeight = Clamp(VolumeWeight + Vary(rng, 0.2, rate), 0.2, 2.0),
            AdxWeight = Clamp(AdxWeight + Vary(rng, 0.2, rate), 0.2, 2.0),
            BestHours = BestHours,
            AvoidHours = AvoidHours,
            SkipOpeningMinutes = SkipOpeningMinutes,
            AllowAverageDown = AllowAverageDown,
            MaxAverageDowns = MaxAverageDowns,
            MinDropForAverageDown = Clamp(MinDropForAverageDown + Vary(rng, 0.5, rate), 1.0, 5.0)
        };
    }
    
    private static double Vary(Random rng, double maxChange, double rate)
        => (rng.NextDouble() * 2 - 1) * maxChange * rate;
    
    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(max, value));
}

// ============================================================================
// Result Types
// ============================================================================

/// <summary>
/// Result of a single simulated day.
/// </summary>
public sealed class DayResult
{
    public DateOnly Date { get; set; }
    public int Trades { get; set; }
    public int Wins { get; set; }
    public double ProfitPercent { get; set; }
    public double DayHigh { get; set; }
    public double DayLow { get; set; }
    public double DayClose { get; set; }
    public double RangePercent { get; set; }
}

/// <summary>
/// Result of a single backtest iteration.
/// </summary>
public sealed class IterationResult
{
    public int Iteration { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TunableParameters Parameters { get; set; } = new();
    public TimeSpan Duration { get; set; }
    
    // Performance metrics
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;
    public double TotalProfitPercent { get; set; }
    public double AvgProfitPerTrade => TotalTrades > 0 ? TotalProfitPercent / TotalTrades : 0;
    public double MaxDrawdownPercent { get; set; }
    
    // Day-by-day breakdown
    public List<DayResult> DayResults { get; set; } = [];
    
    // Best/worst days
    public DateOnly? BestDay { get; set; }
    public double BestDayProfit { get; set; }
    public DateOnly? WorstDay { get; set; }
    public double WorstDayProfit { get; set; }
    
    /// <summary>
    /// Fitness score for ranking iterations (higher is better).
    /// </summary>
    public double FitnessScore => 
        (WinRate * 0.3) + 
        (TotalProfitPercent * 0.4) + 
        (TotalTrades * 0.1) + 
        ((100 - MaxDrawdownPercent) * 0.2);
}

/// <summary>
/// Complete backtesting results for a ticker (saved to TICKER.backtesting.json).
/// </summary>
public sealed class BacktestingData
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public int TotalIterationsRun { get; set; }
    
    // Best parameters discovered
    public TunableParameters BestParameters { get; set; } = new();
    public double BestFitnessScore { get; set; }
    public int BestIteration { get; set; }
    
    // Historical iterations (keep last 100)
    public List<IterationResult> Iterations { get; set; } = [];
    
    // Learned patterns
    public Dictionary<int, double> HourlyWinRates { get; set; } = new();
    public Dictionary<string, double> IndicatorCorrelations { get; set; } = new();
    
    private const int MaxIterations = 100;
    
    public void AddIteration(IterationResult result)
    {
        Iterations.Add(result);
        TotalIterationsRun++;
        LastUpdated = DateTime.UtcNow;
        
        // Keep only last 100
        while (Iterations.Count > MaxIterations)
            Iterations.RemoveAt(0);
        
        // Update best if this is better
        if (result.FitnessScore > BestFitnessScore)
        {
            BestFitnessScore = result.FitnessScore;
            BestParameters = result.Parameters;
            BestIteration = result.Iteration;
        }
    }
}

/// <summary>
/// Complete result of learning session.
/// </summary>
public sealed class LearningResult
{
    public string Symbol { get; init; } = string.Empty;
    public int TotalIterations { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public TickerProfile FinalProfile { get; init; } = null!;
    public BacktestingData BacktestData { get; init; } = null!;
    public List<IterationResult> Iterations { get; init; } = [];
    
    // Summary stats
    public double InitialWinRate { get; init; }
    public double FinalWinRate { get; init; }
    public double WinRateImprovement => FinalWinRate - InitialWinRate;
    
    public double InitialThreshold { get; init; }
    public double FinalThreshold { get; init; }
    
    public override string ToString()
    {
        return $@"
+============================================================================+
|  LEARNING COMPLETE: {Symbol,-10}                                           |
+============================================================================+
|  Iterations:     {TotalIterations}                                         |
|  Duration:       {TotalDuration:hh\:mm\:ss}                                |
+----------------------------------------------------------------------------+
|  Win Rate:       {InitialWinRate:F1}% -> {FinalWinRate:F1}% ({WinRateImprovement:+0.0;-0.0}%)
|  Threshold:      {InitialThreshold:F0} -> {FinalThreshold:F0}              |
|  Best Fitness:   {BacktestData?.BestFitnessScore:F2}                       |
+----------------------------------------------------------------------------+
|  Profile Confidence: {FinalProfile?.Confidence:P0}                         |
|  Total Trades:       {FinalProfile?.TotalTrades}                           |
|  Total P&L:          {FinalProfile?.TotalPnL:F2}%                          |
+============================================================================+";
    }
}

// ============================================================================
// Main Learning Backtester
// ============================================================================

/// <summary>
/// The main learning backtester that runs 100 iterations using REAL historical data.
/// Uses AutonomousBacktester for actual simulation - no random numbers!
/// 
/// WORKFLOW:
/// 1. Tickers - User adds symbols to watchlist
/// 2. Learn   - Downloads 30 days of data + runs iterations → saves profile
/// 3. Live    - Uses learned profiles to trade for real money
/// </summary>
public sealed class LearningBacktester
{
    private readonly string _dataFolder;
    private readonly Random _rng = new();
    private readonly HistoricalDataCache _dataCache;
    private readonly HistoricalDataService? _histService;
    private List<HistoricalBar>? _cachedBars;
    private string? _cachedSymbol;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    /// <summary>
    /// Creates a LearningBacktester for offline learning (data must already be cached).
    /// </summary>
    public LearningBacktester() : this(null) { }
    
    /// <summary>
    /// Creates a LearningBacktester with optional data fetching capability.
    /// </summary>
    /// <param name="histService">Historical data service for fetching data. 
    /// If null, data must already be cached.</param>
    public LearningBacktester(HistoricalDataService? histService)
    {
        _dataFolder = SettingsManager.GetHistoryFolder();
        Directory.CreateDirectory(_dataFolder);
        _dataCache = new HistoricalDataCache();
        _histService = histService;
    }
    
    /// <summary>
    /// Runs the full learning process for a ticker using REAL historical data.
    /// Automatically fetches 30 days of data if not already cached.
    /// </summary>
    /// <param name="symbol">Ticker symbol to learn.</param>
    /// <param name="iterations">Number of iterations (default 100).</param>
    /// <param name="daysPerIteration">Days to simulate per iteration.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<LearningResult> LearnAsync(
        string symbol,
        int iterations = 100,
        int daysPerIteration = 30,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        
        // ====================================================================
        // STEP 1: Get historical data (fetch if needed)
        // ====================================================================
        progress?.Report($"[LEARN] Starting learning process for {symbol}...");
        
        if (!_dataCache.HasCachedData(symbol))
        {
            if (_histService != null)
            {
                // Auto-fetch 30 days of data
                progress?.Report($"[FETCH] Downloading 30 days of historical data for {symbol}...");
                _cachedBars = await _dataCache.GetOrFetchAsync(symbol, _histService, cancellationToken: ct);
                progress?.Report($"[FETCH] Downloaded {_cachedBars.Count} bars for {symbol}");
            }
            else
            {
                progress?.Report($"ERROR: No historical data found for {symbol} and no data service available.");
                progress?.Report($"Either provide a HistoricalDataService or run 'backtest {symbol}' first.");
                throw new InvalidOperationException($"No historical data for {symbol}. Run 'backtest {symbol}' first.");
            }
        }
        else
        {
            _cachedBars = _dataCache.LoadFromCache(symbol);
            progress?.Report($"[CACHE] Loaded cached data for {symbol}");
        }
        
        _cachedSymbol = symbol;
        
        if (_cachedBars == null || _cachedBars.Count == 0)
        {
            throw new InvalidOperationException($"Historical data for {symbol} is empty.");
        }
        
        // Get available date range
        var availableDates = _cachedBars
            .Select(b => DateOnly.FromDateTime(b.Time))
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        
        progress?.Report($"[DATA] {_cachedBars.Count} bars covering {availableDates.Count} trading days");
        progress?.Report($"[DATA] Date range: {availableDates.First()} to {availableDates.Last()}");
        
        // ====================================================================
        // STEP 2: Load existing learning data or create new
        // ====================================================================
        var data = LoadBacktestingData(symbol);
        var profile = LoadOrCreateProfile(symbol);
        
        // Start with best known parameters or defaults
        var currentParams = data.TotalIterationsRun > 0 ? data.BestParameters : new TunableParameters();
        double initialThreshold = currentParams.LongEntryThreshold;
        double initialWinRate = 0;
        double lastWinRate = 0;
        
        progress?.Report($"[LEARN] Starting {iterations}-iteration learning...");
        if (data.TotalIterationsRun > 0)
        {
            progress?.Report($"[LEARN] Resuming from iteration {data.TotalIterationsRun} (best fitness: {data.BestFitnessScore:F2})");
        }
        
        for (int i = 1; i <= iterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            
            var iterSw = Stopwatch.StartNew();
            
            // Run one iteration
            var result = await RunIterationAsync(symbol, i, daysPerIteration, currentParams, progress, ct);
            result.Duration = iterSw.Elapsed;
            
            // Track initial win rate
            if (i == 1) initialWinRate = result.WinRate;
            lastWinRate = result.WinRate;
            
            // Save result
            data.AddIteration(result);
            
            // Print results
            PrintIterationResult(result, i, iterations, progress);
            
            // Evolve parameters for next iteration
            // If this iteration was good, mutate slightly from it
            // If bad, mutate more aggressively or revert to best
            if (result.FitnessScore >= data.BestFitnessScore * 0.9)
            {
                // Good iteration - small mutations
                currentParams = result.Parameters.Mutate(_rng, 0.1);
            }
            else if (result.FitnessScore >= data.BestFitnessScore * 0.7)
            {
                // OK iteration - medium mutations
                currentParams = result.Parameters.Mutate(_rng, 0.2);
            }
            else
            {
                // Bad iteration - revert to best with large mutations to explore
                currentParams = data.BestParameters.Mutate(_rng, 0.3);
            }
            
            // Save every 10 iterations
            if (i % 10 == 0)
            {
                SaveBacktestingData(data);
                UpdateProfile(profile, data);
                SaveProfile(profile);
                progress?.Report($"[Checkpoint] Saved data after iteration {i}");
            }
        }
        
        // Final save
        SaveBacktestingData(data);
        UpdateProfile(profile, data);
        SaveProfile(profile);
        
        sw.Stop();
        progress?.Report($"\n[COMPLETE] {iterations} iterations in {sw.Elapsed:hh\\:mm\\:ss}");
        progress?.Report($"Best fitness: {data.BestFitnessScore:F2} from iteration {data.BestIteration}");
        
        return new LearningResult
        {
            Symbol = symbol,
            TotalIterations = iterations,
            TotalDuration = sw.Elapsed,
            FinalProfile = profile,
            BacktestData = data,
            Iterations = data.Iterations,
            InitialWinRate = initialWinRate,
            FinalWinRate = lastWinRate,
            InitialThreshold = initialThreshold,
            FinalThreshold = data.BestParameters.LongEntryThreshold
        };
    }
    
    /// <summary>
    /// Runs a single iteration of backtesting using REAL historical data.
    /// IMPORTANT: Never peek at future candles!
    /// </summary>
    private async Task<IterationResult> RunIterationAsync(
        string symbol,
        int iteration,
        int days,
        TunableParameters p,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        await Task.Yield(); // Allow cancellation
        
        var dayResults = new List<DayResult>();
        
        // Get available trading days from cached data
        if (_cachedBars == null || _cachedSymbol != symbol)
        {
            throw new InvalidOperationException("Historical data not loaded. Call LearnAsync first.");
        }
        
        var availableDates = _cachedBars
            .Select(b => DateOnly.FromDateTime(b.Time))
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        
        // Use last N days (or all if fewer available)
        var datesToUse = availableDates.TakeLast(Math.Min(days, availableDates.Count)).ToList();
        
        int totalTrades = 0;
        int wins = 0;
        double totalProfit = 0;
        double maxDrawdown = 0;
        double peak = 100; // Start with 100 units
        double current = 100;
        
        DateOnly? bestDay = null;
        double bestDayProfit = double.MinValue;
        DateOnly? worstDay = null;
        double worstDayProfit = double.MaxValue;
        
        // Create backtester with config from parameters
        var backtester = new AutonomousBacktester(null); // null = offline mode
        var config = CreateConfigFromParameters(p);
        
        // Simulate each day using REAL historical data
        foreach (var date in datesToUse)
        {
            ct.ThrowIfCancellationRequested();
            
            // Get bars for this day
            var dayBars = _cachedBars
                .Where(b => DateOnly.FromDateTime(b.Time) == date)
                .OrderBy(b => b.Time)
                .ToList();
            
            if (dayBars.Count < 10) continue; // Skip days with insufficient data
            
            // Convert to BackTestCandles
            var candles = dayBars.Select(b => new BackTestCandle
            {
                Timestamp = b.Time,
                Open = b.Open,
                High = b.High,
                Low = b.Low,
                Close = b.Close,
                Volume = b.Volume,
                Vwap = b.Vwap ?? 0
            }).ToList();
            
            // Run actual backtest simulation
            var result = backtester.RunWithCandles(symbol, date, candles, config);
            
            // Extract results
            var day = new DayResult
            {
                Date = date,
                Trades = result.TotalTrades,
                Wins = result.WinCount,
                ProfitPercent = (double)result.TotalReturnPercent,
                DayHigh = result.DayHigh,
                DayLow = result.DayLow,
                DayClose = result.DayClose,
                RangePercent = result.DayOpen > 0 ? (result.DayHigh - result.DayLow) / result.DayOpen * 100 : 0
            };
            
            dayResults.Add(day);
            totalTrades += day.Trades;
            wins += day.Wins;
            totalProfit += day.ProfitPercent;
            
            // Track best/worst days
            if (day.ProfitPercent > bestDayProfit)
            {
                bestDayProfit = day.ProfitPercent;
                bestDay = date;
            }
            if (day.ProfitPercent < worstDayProfit)
            {
                worstDayProfit = day.ProfitPercent;
                worstDay = date;
            }
            
            // Track drawdown
            current = current * (1 + day.ProfitPercent / 100);
            if (current > peak) peak = current;
            var dd = (peak - current) / peak * 100;
            if (dd > maxDrawdown) maxDrawdown = dd;
        }
        
        return new IterationResult
        {
            Iteration = iteration,
            Parameters = p,
            TotalTrades = totalTrades,
            WinningTrades = wins,
            LosingTrades = totalTrades - wins,
            TotalProfitPercent = totalProfit,
            MaxDrawdownPercent = maxDrawdown,
            DayResults = dayResults,
            BestDay = bestDay,
            BestDayProfit = bestDayProfit,
            WorstDay = worstDay,
            WorstDayProfit = worstDayProfit
        };
    }
    
    /// <summary>
    /// Creates AutonomousBacktestConfig from TunableParameters.
    /// This maps our learning sliders to the actual backtest configuration.
    /// </summary>
    private static AutonomousBacktestConfig CreateConfigFromParameters(TunableParameters p)
    {
        return new AutonomousBacktestConfig
        {
            StartingCapital = 1000m,
            Mode = AutonomousMode.Optimized,
            EnableSelfCalibration = true,
            InitialLongThreshold = (int)p.LongEntryThreshold,
            InitialShortThreshold = -(int)p.LongEntryThreshold,
            AllowShort = true,
            AllowDirectionFlip = true,
            UseTickerMetadata = true,
            AvoidFirstMinutesRTH = p.SkipOpeningMinutes,
            AvoidLastMinutesRTH = 10,
            MinVolumeRatio = 1.0 + (p.VolumeWeight - 1.0) * 0.2, // Scale volume requirement
            RequireTrendAlignment = true,
            RequireVolumeConfirmation = p.VolumeWeight > 0.8
        };
    }
    
    /// <summary>
    /// Prints results of an iteration.
    /// </summary>
    private static void PrintIterationResult(IterationResult r, int current, int total, IProgress<string>? progress)
    {
        var summary = $@"
+===========================================================================+
|  ITERATION {current,3}/{total}  |  Fitness: {r.FitnessScore,6:F2}  |  {DateTime.Now:HH:mm:ss}
+===========================================================================+
|  Trades: {r.TotalTrades,4}  |  Wins: {r.WinningTrades,4}  |  Win Rate: {r.WinRate,5:F1}%
|  Profit: {r.TotalProfitPercent,7:F2}%  |  Avg/Trade: {r.AvgProfitPerTrade,5:F2}%  |  MaxDD: {r.MaxDrawdownPercent,5:F1}%
+---------------------------------------------------------------------------+
|  PARAMETERS:
|  Entry Threshold: {r.Parameters.LongEntryThreshold,5:F0}  |  LOD Bounce: {r.Parameters.LodBounceThreshold,5:F0}
|  Near LOD: {r.Parameters.NearLodPercent,5:F2}%  |  Near HOD: {r.Parameters.NearHodPercent,5:F2}%
|  RSI Exit: {r.Parameters.MomentumExitRsi,5:F0}  |  Min Profit: {r.Parameters.MinProfitToTake,5:F2}%
+===========================================================================+";
        
        progress?.Report(summary);
    }
    
    // ========================================================================
    // Persistence
    // ========================================================================
    
    private string GetBacktestingPath(string symbol) => 
        Path.Combine(_dataFolder, $"{symbol}.backtesting.json");
    
    private string GetProfilePath(string symbol) => 
        Path.Combine(_dataFolder, $"{symbol}.profile.json");
    
    private BacktestingData LoadBacktestingData(string symbol)
    {
        var path = GetBacktestingPath(symbol);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<BacktestingData>(json, JsonOptions);
            if (data != null)
            {
                data.Symbol = symbol;
                return data;
            }
        }
        return new BacktestingData { Symbol = symbol };
    }
    
    private void SaveBacktestingData(BacktestingData data)
    {
        var path = GetBacktestingPath(data.Symbol);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(path, json);
    }
    
    private TickerProfile LoadOrCreateProfile(string symbol)
    {
        var path = GetProfilePath(symbol);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<TickerProfile>(json, JsonOptions);
            if (profile != null)
            {
                profile.Symbol = symbol;
                return profile;
            }
        }
        return new TickerProfile { Symbol = symbol };
    }
    
    private void SaveProfile(TickerProfile profile)
    {
        var path = GetProfilePath(profile.Symbol);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }
    
    /// <summary>
    /// Updates the ticker profile with learned parameters.
    /// </summary>
    private static void UpdateProfile(TickerProfile profile, BacktestingData data)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        
        // Update thresholds from best parameters
        profile.OptimalLongEntryThreshold = data.BestParameters.LongEntryThreshold;
        
        // Update timing patterns
        profile.BestHours = data.BestParameters.BestHours;
        profile.AvoidHours = data.BestParameters.AvoidHours;
        
        // Update total trade stats
        if (data.Iterations.Count > 0)
        {
            var lastIter = data.Iterations[^1];
            profile.TotalTrades += lastIter.TotalTrades;
            profile.TotalWins += lastIter.WinningTrades;
            profile.TotalPnL += lastIter.TotalProfitPercent;
        }
    }
}

// ============================================================================
// TickerProfile Extensions
// ============================================================================

/// <summary>
/// Extension methods for TickerProfile persistence.
/// </summary>
public static class TickerProfileExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public static void Save(this TickerProfile profile, string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        var path = Path.Combine(dataFolder, $"{profile.Symbol}.profile.json");
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }
    
    public static TickerProfile GetOrCreate(string dataFolder, string symbol)
    {
        var path = Path.Combine(dataFolder, $"{symbol}.profile.json");
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<TickerProfile>(json, JsonOptions);
            if (profile != null)
            {
                profile.Symbol = symbol;
                return profile;
            }
        }
        
        return new TickerProfile { Symbol = symbol };
    }
}
