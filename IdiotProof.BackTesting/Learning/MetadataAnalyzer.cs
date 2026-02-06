// ============================================================================
// Metadata Analyzer - Extracts behavioral patterns from historical data
// ============================================================================
//
// Analyzes historical candle data to understand how a stock typically behaves:
// - When HOD/LOD typically occurs
// - Support/resistance levels
// - Optimal entry/exit points with hindsight analysis
// - Gap behavior patterns
// - VWAP interaction patterns
//
// This data helps autonomous trading make better decisions by "knowing"
// how the stock typically behaves before entering a trade.
//
// ============================================================================

using IdiotProof.BackTesting.Analysis;
using IdiotProof.BackTesting.Models;
using IdiotProof.BackTesting.Services;

namespace IdiotProof.BackTesting.Learning;

/// <summary>
/// Analyzes historical data to extract behavioral patterns for a ticker.
/// </summary>
public sealed class MetadataAnalyzer
{
    private readonly HistoricalMetadataManager _metadataManager;

    public MetadataAnalyzer(HistoricalMetadataManager? metadataManager = null)
    {
        _metadataManager = metadataManager ?? new HistoricalMetadataManager();
    }

    /// <summary>
    /// Analyzes a single day's session and extracts patterns.
    /// </summary>
    public DayAnalysis AnalyzeDay(BackTestSession session, double? previousClose = null)
    {
        var candles = session.Candles;
        if (candles.Count == 0)
        {
            throw new InvalidOperationException("Session has no candles to analyze");
        }

        // Calculate VWAP if not already done
        if (candles[0].Vwap == 0)
        {
            session.CalculateVwap();
        }

        // Find HOD/LOD
        var (hodCandle, lodCandle) = FindDailyExtremes(candles);
        var rthStart = new TimeOnly(9, 30);

        int hodMinutesFromOpen = (int)(hodCandle.Timestamp.TimeOfDay - rthStart.ToTimeSpan()).TotalMinutes;
        int lodMinutesFromOpen = (int)(lodCandle.Timestamp.TimeOfDay - rthStart.ToTimeSpan()).TotalMinutes;

        // Handle premarket HOD/LOD
        if (hodMinutesFromOpen < 0) hodMinutesFromOpen = 0;
        if (lodMinutesFromOpen < 0) lodMinutesFromOpen = 0;

        // Gap analysis
        double gapPercent = 0;
        bool gapFilled = false;
        int gapFillTimeMinutes = 0;

        if (previousClose.HasValue && previousClose.Value > 0)
        {
            gapPercent = (session.Open - previousClose.Value) / previousClose.Value * 100;

            // Check if gap filled
            if (gapPercent > 0.5)  // Gap up
            {
                gapFilled = session.Low <= previousClose.Value;
                if (gapFilled)
                {
                    var fillCandle = candles.FirstOrDefault(c => c.Low <= previousClose.Value);
                    if (fillCandle != null)
                    {
                        gapFillTimeMinutes = (int)(fillCandle.Timestamp - session.StartTime).TotalMinutes;
                    }
                }
            }
            else if (gapPercent < -0.5)  // Gap down
            {
                gapFilled = session.High >= previousClose.Value;
                if (gapFilled)
                {
                    var fillCandle = candles.FirstOrDefault(c => c.High >= previousClose.Value);
                    if (fillCandle != null)
                    {
                        gapFillTimeMinutes = (int)(fillCandle.Timestamp - session.StartTime).TotalMinutes;
                    }
                }
            }
        }

        // VWAP analysis
        int aboveVwapCount = candles.Count(c => c.Close > c.Vwap);
        double percentAboveVwap = (double)aboveVwapCount / candles.Count * 100;
        int vwapCrosses = CountVwapCrosses(candles);

        // Find optimal trade points with hindsight
        var (bestLong, bestShort, bestLongExit, bestShortExit) = FindOptimalTradePoints(session);

        return new DayAnalysis
        {
            Date = session.Date,
            Open = session.Open,
            High = session.High,
            Low = session.Low,
            Close = session.Close,
            HodPercentFromOpen = (session.High - session.Open) / session.Open * 100,
            LodPercentFromOpen = (session.Low - session.Open) / session.Open * 100,
            HodTime = TimeOnly.FromDateTime(hodCandle.Timestamp),
            LodTime = TimeOnly.FromDateTime(lodCandle.Timestamp),
            HodMinutesFromOpen = hodMinutesFromOpen,
            LodMinutesFromOpen = lodMinutesFromOpen,
            GapPercent = gapPercent,
            GapFilled = gapFilled,
            GapFillTimeMinutes = gapFillTimeMinutes,
            VwapCrosses = vwapCrosses,
            PercentTimeAboveVwap = percentAboveVwap,
            DailyRangePercent = session.Range / session.Open * 100,
            BestLongEntry = bestLong,
            BestShortEntry = bestShort,
            BestLongExit = bestLongExit,
            BestShortExit = bestShortExit
        };
    }

    /// <summary>
    /// Analyzes multiple days and builds/updates the historical metadata.
    /// </summary>
    public async Task<HistoricalMetadata> AnalyzeMultipleDays(
        string symbol,
        IHistoricalDataProvider dataProvider,
        List<DateOnly> dates,
        IProgress<string>? progress = null)
    {
        var metadata = _metadataManager.GetOrCreate(symbol);

        double? previousClose = null;
        int processed = 0;

        foreach (var date in dates.OrderBy(d => d))
        {
            try
            {
                progress?.Report($"Analyzing {symbol} on {date:yyyy-MM-dd}...");

                var session = await dataProvider.LoadSessionAsync(symbol, date);
                var dayAnalysis = AnalyzeDay(session, previousClose);

                metadata.AddDayAnalysis(dayAnalysis);

                // Store close for next day's gap analysis
                previousClose = session.Close;

                processed++;
                progress?.Report($"  -> HOD: {dayAnalysis.HodTime:HH:mm} ({dayAnalysis.HodPercentFromOpen:+0.0;-0.0}%), " +
                               $"LOD: {dayAnalysis.LodTime:HH:mm} ({dayAnalysis.LodPercentFromOpen:+0.0;-0.0}%)");
            }
            catch (Exception ex)
            {
                progress?.Report($"  -> Error: {ex.Message}");
            }
        }

        // Recalculate aggregate statistics
        metadata.RecalculateStatistics();

        // Find support/resistance levels across all days
        var allCandles = await CollectAllCandles(symbol, dataProvider, dates);
        var (supports, resistances) = FindSupportResistanceLevels(allCandles);
        metadata.SupportLevels = supports;
        metadata.ResistanceLevels = resistances;

        // Save the updated metadata
        _metadataManager.Save(metadata);

        progress?.Report($"Metadata complete. {processed} days analyzed.");

        return metadata;
    }

    /// <summary>
    /// Generates a detailed report of the historical metadata.
    /// </summary>
    public string GenerateMetadataReport(HistoricalMetadata metadata)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(metadata.ToString());

        // Optimal trade points
        if (metadata.OptimalLongEntries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| OPTIMAL LONG ENTRY PATTERNS                                      |");
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| Time     | Rel. Open | Rel. VWAP | Win Rate | Avg Profit        |");
            sb.AppendLine("|----------|-----------|-----------|----------|-------------------|");

            foreach (var entry in metadata.OptimalLongEntries)
            {
                sb.AppendLine($"| {entry.TimeOfDay:HH:mm}    | {entry.PriceRelativeToOpen,+8:F2}% | {entry.PriceRelativeToVwap,+8:F2}% | {entry.WinRate,7:F1}% | {entry.PotentialProfitPercent,+6:F2}%         |");
            }
            sb.AppendLine("+------------------------------------------------------------------+");
        }

        // Support levels
        if (metadata.SupportLevels.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| SUPPORT LEVELS                                                   |");
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| Price     | Touches | Bounces | Strength                        |");
            sb.AppendLine("|-----------|---------|---------|----------------------------------|");

            foreach (var level in metadata.SupportLevels.Take(5))
            {
                string strength = level.Strength >= 0.8 ? "STRONG" :
                                 level.Strength >= 0.6 ? "MEDIUM" : "WEAK";
                sb.AppendLine($"| ${level.Price,8:F2} | {level.Touches,7} | {level.Bounces,7} | {level.Strength * 100,5:F0}% {strength,-8}        |");
            }
            sb.AppendLine("+------------------------------------------------------------------+");
        }

        // Resistance levels
        if (metadata.ResistanceLevels.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| RESISTANCE LEVELS                                                |");
            sb.AppendLine("+------------------------------------------------------------------+");
            sb.AppendLine("| Price     | Touches | Bounces | Strength                        |");
            sb.AppendLine("|-----------|---------|---------|----------------------------------|");

            foreach (var level in metadata.ResistanceLevels.Take(5))
            {
                string strength = level.Strength >= 0.8 ? "STRONG" :
                                 level.Strength >= 0.6 ? "MEDIUM" : "WEAK";
                sb.AppendLine($"| ${level.Price,8:F2} | {level.Touches,7} | {level.Bounces,7} | {level.Strength * 100,5:F0}% {strength,-8}        |");
            }
            sb.AppendLine("+------------------------------------------------------------------+");
        }

        // HOD/LOD visualization
        sb.AppendLine();
        sb.AppendLine("+------------------------------------------------------------------+");
        sb.AppendLine("| HOD/LOD TIMING DISTRIBUTION                                      |");
        sb.AppendLine("+------------------------------------------------------------------+");
        sb.AppendLine(GenerateHodLodVisualization(metadata));

        return sb.ToString();
    }

    // ========================================================================
    // Private Methods
    // ========================================================================

    private static (BackTestCandle hod, BackTestCandle lod) FindDailyExtremes(List<BackTestCandle> candles)
    {
        var hodCandle = candles.MaxBy(c => c.High) ?? candles[0];
        var lodCandle = candles.MinBy(c => c.Low) ?? candles[0];
        return (hodCandle, lodCandle);
    }

    private static int CountVwapCrosses(List<BackTestCandle> candles)
    {
        int crosses = 0;
        bool wasAbove = candles[0].Close > candles[0].Vwap;

        for (int i = 1; i < candles.Count; i++)
        {
            bool isAbove = candles[i].Close > candles[i].Vwap;
            if (isAbove != wasAbove)
            {
                crosses++;
                wasAbove = isAbove;
            }
        }

        return crosses;
    }

    /// <summary>
    /// With perfect hindsight, finds the optimal buy/sell points for the day.
    /// </summary>
    private (OptimalTradePoint? bestLong, OptimalTradePoint? bestShort,
             OptimalTradePoint? bestLongExit, OptimalTradePoint? bestShortExit)
        FindOptimalTradePoints(BackTestSession session)
    {
        var candles = session.Candles;
        if (candles.Count < 10) return (null, null, null, null);

        // Best long: Find the lowest low before a significant rally
        // Best short: Find the highest high before a significant drop
        // Best exits: Find the reversal points after entries

        OptimalTradePoint? bestLong = null;
        OptimalTradePoint? bestShort = null;
        OptimalTradePoint? bestLongExit = null;
        OptimalTradePoint? bestShortExit = null;

        double maxProfitLong = 0;
        double maxProfitShort = 0;

        // Scan for best long entry (buying at low, selling at subsequent high)
        for (int i = 0; i < candles.Count - 10; i++)
        {
            var entryCandle = candles[i];

            // Find the maximum high after this candle
            double maxHighAfter = candles.Skip(i + 1).Max(c => c.High);
            double profitPercent = (maxHighAfter - entryCandle.Low) / entryCandle.Low * 100;

            if (profitPercent > maxProfitLong)
            {
                maxProfitLong = profitPercent;
                bestLong = new OptimalTradePoint
                {
                    TimeOfDay = TimeOnly.FromDateTime(entryCandle.Timestamp),
                    PriceRelativeToOpen = (entryCandle.Low - session.Open) / session.Open * 100,
                    PriceRelativeToVwap = entryCandle.Vwap > 0
                        ? (entryCandle.Low - entryCandle.Vwap) / entryCandle.Vwap * 100
                        : 0,
                    Type = TradePointType.Long,
                    PotentialProfitPercent = profitPercent,
                    Occurrences = 1,
                    ProfitableCount = 1
                };

                // Find the exit point (the maximum high candle)
                var exitCandle = candles.Skip(i + 1).MaxBy(c => c.High);
                if (exitCandle != null)
                {
                    bestLongExit = new OptimalTradePoint
                    {
                        TimeOfDay = TimeOnly.FromDateTime(exitCandle.Timestamp),
                        PriceRelativeToOpen = (exitCandle.High - session.Open) / session.Open * 100,
                        PriceRelativeToVwap = exitCandle.Vwap > 0
                            ? (exitCandle.High - exitCandle.Vwap) / exitCandle.Vwap * 100
                            : 0,
                        Type = TradePointType.CloseLong,
                        PotentialProfitPercent = profitPercent,
                        Occurrences = 1,
                        ProfitableCount = 1
                    };
                }
            }
        }

        // Scan for best short entry (selling at high, buying at subsequent low)
        for (int i = 0; i < candles.Count - 10; i++)
        {
            var entryCandle = candles[i];

            // Find the minimum low after this candle
            double minLowAfter = candles.Skip(i + 1).Min(c => c.Low);
            double profitPercent = (entryCandle.High - minLowAfter) / entryCandle.High * 100;

            if (profitPercent > maxProfitShort)
            {
                maxProfitShort = profitPercent;
                bestShort = new OptimalTradePoint
                {
                    TimeOfDay = TimeOnly.FromDateTime(entryCandle.Timestamp),
                    PriceRelativeToOpen = (entryCandle.High - session.Open) / session.Open * 100,
                    PriceRelativeToVwap = entryCandle.Vwap > 0
                        ? (entryCandle.High - entryCandle.Vwap) / entryCandle.Vwap * 100
                        : 0,
                    Type = TradePointType.Short,
                    PotentialProfitPercent = profitPercent,
                    Occurrences = 1,
                    ProfitableCount = 1
                };

                // Find the exit point (the minimum low candle)
                var exitCandle = candles.Skip(i + 1).MinBy(c => c.Low);
                if (exitCandle != null)
                {
                    bestShortExit = new OptimalTradePoint
                    {
                        TimeOfDay = TimeOnly.FromDateTime(exitCandle.Timestamp),
                        PriceRelativeToOpen = (exitCandle.Low - session.Open) / session.Open * 100,
                        PriceRelativeToVwap = exitCandle.Vwap > 0
                            ? (exitCandle.Low - exitCandle.Vwap) / exitCandle.Vwap * 100
                            : 0,
                        Type = TradePointType.CloseShort,
                        PotentialProfitPercent = profitPercent,
                        Occurrences = 1,
                        ProfitableCount = 1
                    };
                }
            }
        }

        return (bestLong, bestShort, bestLongExit, bestShortExit);
    }

    private async Task<List<BackTestCandle>> CollectAllCandles(
        string symbol,
        IHistoricalDataProvider dataProvider,
        List<DateOnly> dates)
    {
        var allCandles = new List<BackTestCandle>();

        foreach (var date in dates)
        {
            try
            {
                var session = await dataProvider.LoadSessionAsync(symbol, date);
                allCandles.AddRange(session.Candles);
            }
            catch
            {
                // Skip dates with no data
            }
        }

        return allCandles;
    }

    /// <summary>
    /// Finds support and resistance levels using a simple clustering algorithm.
    /// </summary>
    private (List<PriceLevel> supports, List<PriceLevel> resistances)
        FindSupportResistanceLevels(List<BackTestCandle> candles)
    {
        if (candles.Count < 20) return ([], []);

        // Find local minima (supports) and maxima (resistances)
        var localMinima = new List<double>();
        var localMaxima = new List<double>();

        for (int i = 2; i < candles.Count - 2; i++)
        {
            // Local minimum: lower low than neighbors
            if (candles[i].Low < candles[i - 1].Low &&
                candles[i].Low < candles[i - 2].Low &&
                candles[i].Low < candles[i + 1].Low &&
                candles[i].Low < candles[i + 2].Low)
            {
                localMinima.Add(candles[i].Low);
            }

            // Local maximum: higher high than neighbors
            if (candles[i].High > candles[i - 1].High &&
                candles[i].High > candles[i - 2].High &&
                candles[i].High > candles[i + 1].High &&
                candles[i].High > candles[i + 2].High)
            {
                localMaxima.Add(candles[i].High);
            }
        }

        // Cluster nearby price levels
        var supports = ClusterPriceLevels(localMinima, true, candles);
        var resistances = ClusterPriceLevels(localMaxima, false, candles);

        return (supports, resistances);
    }

    private static List<PriceLevel> ClusterPriceLevels(
        List<double> prices,
        bool isSupport,
        List<BackTestCandle> allCandles)
    {
        if (prices.Count == 0) return [];

        // Sort prices
        prices = prices.OrderBy(p => p).ToList();

        // Cluster nearby prices (within 0.5% of each other)
        var clusters = new List<List<double>> { new() { prices[0] } };

        for (int i = 1; i < prices.Count; i++)
        {
            var lastCluster = clusters.Last();
            var lastPrice = lastCluster.Average();

            if (Math.Abs(prices[i] - lastPrice) / lastPrice < 0.005)
            {
                lastCluster.Add(prices[i]);
            }
            else
            {
                clusters.Add([prices[i]]);
            }
        }

        // Convert clusters to price levels
        var levels = clusters
            .Where(c => c.Count >= 2)  // At least 2 touches to be significant
            .Select(c =>
            {
                double avgPrice = c.Average();
                int touches = c.Count;

                // Count bounces (price reversed from this level)
                int bounces = CountBounces(avgPrice, allCandles, isSupport);

                return new PriceLevel
                {
                    Price = avgPrice,
                    Touches = touches,
                    Bounces = bounces,
                    Breaks = touches - bounces,
                    IsSupport = isSupport
                };
            })
            .Where(l => l.IsValid)
            .OrderByDescending(l => l.Strength)
            .Take(10)
            .ToList();

        return levels;
    }

    private static int CountBounces(double level, List<BackTestCandle> candles, bool isSupport)
    {
        int bounces = 0;
        double tolerance = level * 0.003;  // 0.3% tolerance

        for (int i = 1; i < candles.Count - 1; i++)
        {
            if (isSupport)
            {
                // Support bounce: low touches level, next candle closes higher
                if (Math.Abs(candles[i].Low - level) < tolerance &&
                    candles[i + 1].Close > candles[i].Close)
                {
                    bounces++;
                }
            }
            else
            {
                // Resistance bounce: high touches level, next candle closes lower
                if (Math.Abs(candles[i].High - level) < tolerance &&
                    candles[i + 1].Close < candles[i].Close)
                {
                    bounces++;
                }
            }
        }

        return bounces;
    }

    private static string GenerateHodLodVisualization(HistoricalMetadata metadata)
    {
        var sb = new System.Text.StringBuilder();

        // Create a simple histogram of HOD/LOD times
        // Time buckets: 9:30-10:00, 10:00-11:00, 11:00-12:00, 12:00-13:00, 13:00-14:00, 14:00-15:00, 15:00-16:00

        var timeBuckets = new[] { "9:30", "10:00", "11:00", "12:00", "13:00", "14:00", "15:00" };
        var bucketMinutes = new[] { 0, 30, 90, 150, 210, 270, 330 };

        var hodCounts = new int[7];
        var lodCounts = new int[7];

        foreach (var day in metadata.RecentDays)
        {
            for (int i = 0; i < bucketMinutes.Length; i++)
            {
                int nextBucket = i < bucketMinutes.Length - 1 ? bucketMinutes[i + 1] : 390;

                if (day.HodMinutesFromOpen >= bucketMinutes[i] && day.HodMinutesFromOpen < nextBucket)
                    hodCounts[i]++;

                if (day.LodMinutesFromOpen >= bucketMinutes[i] && day.LodMinutesFromOpen < nextBucket)
                    lodCounts[i]++;
            }
        }

        int maxCount = Math.Max(hodCounts.Max(), lodCounts.Max());
        if (maxCount == 0) maxCount = 1;

        sb.AppendLine("| Time    HOD Distribution                                         |");
        for (int i = 0; i < 7; i++)
        {
            int barLen = (int)((double)hodCounts[i] / maxCount * 30);
            string bar = new string('#', barLen);
            sb.AppendLine($"| {timeBuckets[i],-5}  |{bar,-30}| {hodCounts[i],2}                 |");
        }

        sb.AppendLine("|                                                                  |");
        sb.AppendLine("| Time    LOD Distribution                                         |");

        for (int i = 0; i < 7; i++)
        {
            int barLen = (int)((double)lodCounts[i] / maxCount * 30);
            string bar = new string('#', barLen);
            sb.AppendLine($"| {timeBuckets[i],-5}  |{bar,-30}| {lodCounts[i],2}                 |");
        }

        return sb.ToString();
    }
}

/// <summary>
/// Manages historical metadata - loading, saving, and caching.
/// </summary>
public sealed class HistoricalMetadataManager
{
    private readonly string _metadataDirectory;
    private readonly Dictionary<string, HistoricalMetadata> _cache = [];

    public string MetadataDirectory => _metadataDirectory;

    private static string GetDefaultMetadataDirectory()
    {
        string currentDir = AppDomain.CurrentDomain.BaseDirectory;

        DirectoryInfo? dir = new DirectoryInfo(currentDir);
        while (dir != null)
        {
            string scriptsPath = Path.Combine(dir.FullName, "IdiotProof.Scripts", "Metadata");
            if (Directory.Exists(Path.Combine(dir.FullName, "IdiotProof.Scripts")))
            {
                return scriptsPath;
            }

            string siblingPath = Path.Combine(dir.FullName, "..", "IdiotProof.Scripts", "Metadata");
            if (Directory.Exists(Path.Combine(dir.FullName, "..", "IdiotProof.Scripts")))
            {
                return Path.GetFullPath(siblingPath);
            }

            dir = dir.Parent;
        }

        return Path.Combine(currentDir, "Metadata");
    }

    public HistoricalMetadataManager(string? metadataDirectory = null)
    {
        _metadataDirectory = metadataDirectory ?? GetDefaultMetadataDirectory();

        if (!Directory.Exists(_metadataDirectory))
        {
            Directory.CreateDirectory(_metadataDirectory);
        }
    }

    public HistoricalMetadata? Load(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        if (_cache.TryGetValue(symbol, out var cached))
            return cached;

        var filePath = Path.Combine(_metadataDirectory, $"{symbol}.metadata.json");
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var metadata = System.Text.Json.JsonSerializer.Deserialize<HistoricalMetadata>(json);
            if (metadata != null)
            {
                _cache[symbol] = metadata;
            }
            return metadata;
        }
        catch
        {
            return null;
        }
    }

    public HistoricalMetadata GetOrCreate(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        var existing = Load(symbol);
        if (existing != null)
            return existing;

        var metadata = new HistoricalMetadata { Symbol = symbol };
        _cache[symbol] = metadata;
        return metadata;
    }

    public void Save(HistoricalMetadata metadata)
    {
        metadata.UpdatedAt = DateTime.UtcNow;
        _cache[metadata.Symbol] = metadata;

        var filePath = Path.Combine(_metadataDirectory, $"{metadata.Symbol}.metadata.json");
        var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(filePath, json);
    }
}
