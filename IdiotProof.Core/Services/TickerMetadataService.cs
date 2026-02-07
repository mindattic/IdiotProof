// ============================================================================
// Ticker Metadata Service - Builds behavioral metadata during warmup
// ============================================================================
//
// PURPOSE:
// Analyzes historical data during backend warmup to build TickerMetadata
// that helps AutonomousTrading and AdaptiveOrder make informed decisions.
//
// WHEN IT RUNS:
// - During backend startup after historical data is fetched
// - Uses the same data that warms up indicators
// - Saves metadata to disk for persistence across sessions
//
// WHAT IT PROVIDES:
// - HOD/LOD timing patterns (when highs/lows typically occur)
// - Support/resistance levels
// - Gap behavior patterns
// - VWAP interaction patterns
// - Time-of-day performance data
//
// ============================================================================

using IdiotProof.Backend.Models;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Settings;
using System.Text.Json;

namespace IdiotProof.Backend.Services;

/// <summary>
/// Service for building and managing ticker metadata during backend warmup.
/// </summary>
public sealed class TickerMetadataService
{
    private readonly string _metadataDirectory;
    private readonly Dictionary<string, TickerMetadata> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the metadata storage directory.
    /// </summary>
    public string MetadataDirectory => _metadataDirectory;

    public TickerMetadataService(string? metadataDirectory = null)
    {
        _metadataDirectory = metadataDirectory ?? GetDefaultMetadataDirectory();

        if (!Directory.Exists(_metadataDirectory))
        {
            Directory.CreateDirectory(_metadataDirectory);
        }
    }

    private static string GetDefaultMetadataDirectory()
    {
        // Use IdiotProof.Core\Scripts\Metadata directory for persistence
        var metadataPath = SettingsManager.GetMetadataFolder();
        if (!Directory.Exists(metadataPath))
            Directory.CreateDirectory(metadataPath);
        return metadataPath;
    }

    /// <summary>
    /// Loads existing metadata for a symbol from disk.
    /// </summary>
    public TickerMetadata? Load(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        lock (_lock)
        {
            if (_cache.TryGetValue(symbol, out var cached))
                return cached;
        }

        var filePath = Path.Combine(_metadataDirectory, $"{symbol}.metadata.json");
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var metadata = JsonSerializer.Deserialize<TickerMetadata>(json);
            if (metadata != null)
            {
                lock (_lock)
                {
                    _cache[symbol] = metadata;
                }
            }
            return metadata;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[METADATA] Failed to load metadata for {symbol}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets or creates metadata for a symbol.
    /// </summary>
    public TickerMetadata GetOrCreate(string symbol)
    {
        symbol = symbol.ToUpperInvariant();

        var existing = Load(symbol);
        if (existing != null)
            return existing;

        var metadata = new TickerMetadata { Symbol = symbol };
        lock (_lock)
        {
            _cache[symbol] = metadata;
        }
        return metadata;
    }

    /// <summary>
    /// Saves metadata to disk.
    /// </summary>
    public void Save(TickerMetadata metadata)
    {
        metadata.UpdatedAt = DateTime.UtcNow;

        lock (_lock)
        {
            _cache[metadata.Symbol.ToUpperInvariant()] = metadata;
        }

        var filePath = Path.Combine(_metadataDirectory, $"{metadata.Symbol.ToUpperInvariant()}.metadata.json");
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Builds or updates metadata from historical bars.
    /// Called during backend warmup with the same data used for indicator warmup.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="bars">Historical bars in chronological order.</param>
    /// <param name="previousClose">Previous day's close for gap analysis (optional).</param>
    public TickerMetadata BuildFromHistoricalBars(
        string symbol,
        IReadOnlyList<HistoricalBar> bars,
        double? previousClose = null)
    {
        if (bars.Count == 0)
            return GetOrCreate(symbol);

        Console.WriteLine($"[METADATA] Building metadata for {symbol} from {bars.Count} historical bars...");

        var metadata = GetOrCreate(symbol);
        metadata.BarsAnalyzed = bars.Count;

        // Group bars by day for daily pattern analysis
        var dayGroups = bars
            .GroupBy(b => DateOnly.FromDateTime(b.Time))
            .OrderBy(g => g.Key)
            .ToList();

        metadata.DaysAnalyzed = dayGroups.Count;

        // Analyze each day
        var dayAnalyses = new List<DayAnalysisResult>();
        double? lastClose = previousClose;

        foreach (var dayGroup in dayGroups)
        {
            var dayBars = dayGroup.OrderBy(b => b.Time).ToList();
            if (dayBars.Count < 10) continue; // Skip incomplete days

            var analysis = AnalyzeDay(dayBars, lastClose);
            dayAnalyses.Add(analysis);

            lastClose = dayBars.Last().Close;
        }

        // Aggregate daily analyses into metadata
        if (dayAnalyses.Count > 0)
        {
            AggregateDailyExtremes(metadata, dayAnalyses);
            AggregateGapBehavior(metadata, dayAnalyses);
            AggregateVwapBehavior(metadata, dayAnalyses);
            CalculateVolatilityMetrics(metadata, dayAnalyses, bars);
        }

        // Find support/resistance levels from all bars
        FindSupportResistanceLevels(metadata, bars.ToList());

        // Save the updated metadata
        Save(metadata);

        Console.WriteLine($"[METADATA] {symbol}: {metadata.DaysAnalyzed} days analyzed, " +
            $"{metadata.SupportLevels.Count} supports, {metadata.ResistanceLevels.Count} resistances");

        // Log key insights
        LogInsights(metadata);

        return metadata;
    }

    private sealed class DayAnalysisResult
    {
        public DateOnly Date { get; init; }
        public double Open { get; init; }
        public double High { get; init; }
        public double Low { get; init; }
        public double Close { get; init; }
        public int HodMinutesFromOpen { get; init; }
        public int LodMinutesFromOpen { get; init; }
        public double HodPercentFromOpen { get; init; }
        public double LodPercentFromOpen { get; init; }
        public double GapPercent { get; init; }
        public bool GapFilled { get; init; }
        public int GapFillTimeMinutes { get; init; }
        public int VwapCrosses { get; init; }
        public double PercentTimeAboveVwap { get; init; }
        public double DailyRangePercent { get; init; }
    }

    private static DayAnalysisResult AnalyzeDay(List<HistoricalBar> bars, double? previousClose)
    {
        var firstBar = bars.First();
        var hodBar = bars.MaxBy(b => b.High) ?? firstBar;
        var lodBar = bars.MinBy(b => b.Low) ?? firstBar;

        double open = firstBar.Open;
        double high = bars.Max(b => b.High);
        double low = bars.Min(b => b.Low);
        double close = bars.Last().Close;

        // Calculate minutes from market open (9:30)
        var marketOpen = new TimeSpan(9, 30, 0);
        int hodMinutes = (int)(hodBar.Time.TimeOfDay - marketOpen).TotalMinutes;
        int lodMinutes = (int)(lodBar.Time.TimeOfDay - marketOpen).TotalMinutes;
        if (hodMinutes < 0) hodMinutes = 0;
        if (lodMinutes < 0) lodMinutes = 0;

        // Gap analysis
        double gapPercent = 0;
        bool gapFilled = false;
        int gapFillTime = 0;

        if (previousClose.HasValue && previousClose.Value > 0)
        {
            gapPercent = (open - previousClose.Value) / previousClose.Value * 100;

            if (gapPercent > 0.5) // Gap up
            {
                gapFilled = low <= previousClose.Value;
                if (gapFilled)
                {
                    var fillBar = bars.FirstOrDefault(b => b.Low <= previousClose.Value);
                    if (fillBar != null)
                        gapFillTime = (int)(fillBar.Time - firstBar.Time).TotalMinutes;
                }
            }
            else if (gapPercent < -0.5) // Gap down
            {
                gapFilled = high >= previousClose.Value;
                if (gapFilled)
                {
                    var fillBar = bars.FirstOrDefault(b => b.High >= previousClose.Value);
                    if (fillBar != null)
                        gapFillTime = (int)(fillBar.Time - firstBar.Time).TotalMinutes;
                }
            }
        }

        // VWAP analysis (simplified - uses close vs running average as proxy)
        double runningPv = 0;
        double runningV = 0;
        int aboveVwapCount = 0;
        int vwapCrosses = 0;
        bool wasAbove = false;

        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            double typicalPrice = (bar.High + bar.Low + bar.Close) / 3;
            runningPv += typicalPrice * bar.Volume;
            runningV += bar.Volume;

            double vwap = runningV > 0 ? runningPv / runningV : bar.Close;
            bool isAbove = bar.Close > vwap;

            if (isAbove) aboveVwapCount++;

            if (i > 0 && isAbove != wasAbove)
                vwapCrosses++;

            wasAbove = isAbove;
        }

        return new DayAnalysisResult
        {
            Date = DateOnly.FromDateTime(firstBar.Time),
            Open = open,
            High = high,
            Low = low,
            Close = close,
            HodMinutesFromOpen = hodMinutes,
            LodMinutesFromOpen = lodMinutes,
            HodPercentFromOpen = (high - open) / open * 100,
            LodPercentFromOpen = (low - open) / open * 100,
            GapPercent = gapPercent,
            GapFilled = gapFilled,
            GapFillTimeMinutes = gapFillTime,
            VwapCrosses = vwapCrosses,
            PercentTimeAboveVwap = bars.Count > 0 ? (double)aboveVwapCount / bars.Count * 100 : 50,
            DailyRangePercent = open > 0 ? (high - low) / open * 100 : 0
        };
    }

    private static void AggregateDailyExtremes(TickerMetadata metadata, List<DayAnalysisResult> days)
    {
        metadata.DailyExtremes = new DailyExtremesPattern
        {
            AvgHodMinutesFromOpen = days.Average(d => d.HodMinutesFromOpen),
            AvgLodMinutesFromOpen = days.Average(d => d.LodMinutesFromOpen),
            HodInFirst30MinPercent = days.Count(d => d.HodMinutesFromOpen <= 30) * 100.0 / days.Count,
            LodInFirst30MinPercent = days.Count(d => d.LodMinutesFromOpen <= 30) * 100.0 / days.Count,
            HodInLast30MinPercent = days.Count(d => d.HodMinutesFromOpen >= 360) * 100.0 / days.Count,
            LodInLast30MinPercent = days.Count(d => d.LodMinutesFromOpen >= 360) * 100.0 / days.Count,
            AvgHodPercentFromOpen = days.Average(d => d.HodPercentFromOpen),
            AvgLodPercentFromOpen = days.Average(d => d.LodPercentFromOpen),
            AvgDailyRangePercent = days.Average(d => d.DailyRangePercent)
        };
    }

    private static void AggregateGapBehavior(TickerMetadata metadata, List<DayAnalysisResult> days)
    {
        var gapUpDays = days.Where(d => d.GapPercent > 0.5).ToList();
        var gapDownDays = days.Where(d => d.GapPercent < -0.5).ToList();

        metadata.GapBehavior = new GapBehaviorPattern
        {
            AvgGapUpPercent = gapUpDays.Count > 0 ? gapUpDays.Average(d => d.GapPercent) : 0,
            AvgGapDownPercent = gapDownDays.Count > 0 ? gapDownDays.Average(d => d.GapPercent) : 0,
            GapUpFillRate = gapUpDays.Count > 0 ? gapUpDays.Count(d => d.GapFilled) * 100.0 / gapUpDays.Count : 0,
            GapDownFillRate = gapDownDays.Count > 0 ? gapDownDays.Count(d => d.GapFilled) * 100.0 / gapDownDays.Count : 0,
            GapUpContinuationRate = gapUpDays.Count > 0 ? gapUpDays.Count(d => d.Close > d.Open) * 100.0 / gapUpDays.Count : 0,
            GapDownContinuationRate = gapDownDays.Count > 0 ? gapDownDays.Count(d => d.Close < d.Open) * 100.0 / gapDownDays.Count : 0,
            AvgGapFillTimeMinutes = days.Where(d => d.GapFilled && d.GapFillTimeMinutes > 0)
                .Select(d => (double)d.GapFillTimeMinutes)
                .DefaultIfEmpty(0)
                .Average()
        };
    }

    private static void AggregateVwapBehavior(TickerMetadata metadata, List<DayAnalysisResult> days)
    {
        metadata.VwapBehavior = new VwapBehaviorPattern
        {
            AvgPercentAboveVwap = days.Average(d => d.PercentTimeAboveVwap),
            AvgVwapCrossesPerDay = days.Average(d => d.VwapCrosses)
        };
    }

    private static void FindSupportResistanceLevels(TickerMetadata metadata, List<HistoricalBar> bars)
    {
        if (bars.Count < 20) return;

        // Find local minima (supports) and maxima (resistances)
        var localMinima = new List<double>();
        var localMaxima = new List<double>();

        for (int i = 2; i < bars.Count - 2; i++)
        {
            // Local minimum
            if (bars[i].Low < bars[i - 1].Low &&
                bars[i].Low < bars[i - 2].Low &&
                bars[i].Low < bars[i + 1].Low &&
                bars[i].Low < bars[i + 2].Low)
            {
                localMinima.Add(bars[i].Low);
            }

            // Local maximum
            if (bars[i].High > bars[i - 1].High &&
                bars[i].High > bars[i - 2].High &&
                bars[i].High > bars[i + 1].High &&
                bars[i].High > bars[i + 2].High)
            {
                localMaxima.Add(bars[i].High);
            }
        }

        // Cluster and create price levels
        metadata.SupportLevels = ClusterPriceLevels(localMinima, true, bars);
        metadata.ResistanceLevels = ClusterPriceLevels(localMaxima, false, bars);
    }

    private static List<PriceLevel> ClusterPriceLevels(List<double> prices, bool isSupport, List<HistoricalBar> bars)
    {
        if (prices.Count == 0) return new List<PriceLevel>();

        prices = prices.OrderBy(p => p).ToList();

        // Cluster nearby prices (within 0.5%)
        var clusters = new List<List<double>> { new List<double> { prices[0] } };

        for (int i = 1; i < prices.Count; i++)
        {
            var lastCluster = clusters.Last();
            var lastPrice = lastCluster.Average();

            if (Math.Abs(prices[i] - lastPrice) / lastPrice < 0.005)
                lastCluster.Add(prices[i]);
            else
                clusters.Add(new List<double> { prices[i] });
        }

        // Convert to price levels
        return clusters
            .Where(c => c.Count >= 2)
            .Select(c =>
            {
                double avgPrice = c.Average();
                int touches = c.Count;
                int bounces = CountBounces(avgPrice, bars, isSupport);

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
    }

    private static int CountBounces(double level, List<HistoricalBar> bars, bool isSupport)
    {
        int bounces = 0;
        double tolerance = level * 0.003;

        for (int i = 1; i < bars.Count - 1; i++)
        {
            if (isSupport)
            {
                if (Math.Abs(bars[i].Low - level) < tolerance && bars[i + 1].Close > bars[i].Close)
                    bounces++;
            }
            else
            {
                if (Math.Abs(bars[i].High - level) < tolerance && bars[i + 1].Close < bars[i].Close)
                    bounces++;
            }
        }

        return bounces;
    }

    private static void CalculateVolatilityMetrics(
        TickerMetadata metadata, 
        List<DayAnalysisResult> days,
        IReadOnlyList<HistoricalBar> bars)
    {
        // Calculate 52-week high/low from available data
        if (bars.Count > 0)
        {
            metadata.High52Week = bars.Max(b => b.High);
            metadata.Low52Week = bars.Min(b => b.Low);
        }

        // Calculate average daily volume
        var dailyVolumes = bars
            .GroupBy(b => DateOnly.FromDateTime(b.Time))
            .Select(g => g.Sum(b => b.Volume))
            .ToList();

        if (dailyVolumes.Count > 0)
        {
            metadata.AvgVolume = (long)dailyVolumes.Average();
        }

        // Calculate 14-day ATR (Average True Range)
        // ATR uses daily OHLC, so we need daily data
        if (days.Count >= 14)
        {
            var atrValues = new List<double>();
            
            for (int i = 1; i < days.Count; i++)
            {
                var today = days[i];
                var yesterday = days[i - 1];
                
                // True Range = max of:
                // 1) High - Low
                // 2) |High - Previous Close|
                // 3) |Low - Previous Close|
                double tr1 = today.High - today.Low;
                double tr2 = Math.Abs(today.High - yesterday.Close);
                double tr3 = Math.Abs(today.Low - yesterday.Close);
                double trueRange = Math.Max(tr1, Math.Max(tr2, tr3));
                
                atrValues.Add(trueRange);
            }

            // Take last 14 values for ATR(14)
            var last14 = atrValues.TakeLast(14).ToList();
            if (last14.Count == 14)
            {
                metadata.Atr14Day = last14.Average();
            }
        }

        // Log volatility metrics
        if (metadata.Atr14Day.HasValue)
        {
            double avgPrice = bars.Average(b => b.Close);
            double atrPercent = metadata.Atr14Day.Value / avgPrice * 100;
            Console.WriteLine($"[METADATA] {metadata.Symbol}: ATR(14) = ${metadata.Atr14Day.Value:F2} ({atrPercent:F1}% of price)");
        }

        if (metadata.AvgVolume.HasValue)
        {
            string volumeStr = metadata.AvgVolume.Value >= 1_000_000 
                ? $"{metadata.AvgVolume.Value / 1_000_000.0:F1}M" 
                : $"{metadata.AvgVolume.Value / 1_000.0:F0}K";
            Console.WriteLine($"[METADATA] {metadata.Symbol}: Avg Daily Volume = {volumeStr}");
        }
    }

    private static void LogInsights(TickerMetadata metadata)
    {
        var de = metadata.DailyExtremes;
        var gb = metadata.GapBehavior;
        var vb = metadata.VwapBehavior;

        // HOD/LOD insights
        if (de.HodInFirst30MinPercent > 50)
            Console.WriteLine($"[METADATA] {metadata.Symbol}: HOD typically in first 30 min ({de.HodInFirst30MinPercent:F0}%) - take profits early on longs");

        if (de.LodInFirst30MinPercent > 50)
            Console.WriteLine($"[METADATA] {metadata.Symbol}: LOD typically in first 30 min ({de.LodInFirst30MinPercent:F0}%) - wait for morning dip before buying");

        // Gap insights
        if (gb.GapUpFillRate > 60)
            Console.WriteLine($"[METADATA] {metadata.Symbol}: Gap ups fill {gb.GapUpFillRate:F0}% of time - consider fading gaps");
        else if (gb.GapUpContinuationRate > 60)
            Console.WriteLine($"[METADATA] {metadata.Symbol}: Gap ups continue {gb.GapUpContinuationRate:F0}% of time - consider buying gaps");

        // VWAP insights
        if (vb.AvgPercentAboveVwap > 60)
            Console.WriteLine($"[METADATA] {metadata.Symbol}: Spends {vb.AvgPercentAboveVwap:F0}% of time above VWAP - bullish bias");
        else if (vb.AvgPercentAboveVwap < 40)
            Console.WriteLine($"[METADATA] {metadata.Symbol}: Spends {100 - vb.AvgPercentAboveVwap:F0}% of time below VWAP - bearish bias");

        // Support/Resistance
        if (metadata.SupportLevels.Count > 0)
        {
            var strongestSupport = metadata.SupportLevels.First();
            Console.WriteLine($"[METADATA] {metadata.Symbol}: Key support at ${strongestSupport.Price:F2} ({strongestSupport.Strength * 100:F0}% strength)");
        }

        if (metadata.ResistanceLevels.Count > 0)
        {
            var strongestResistance = metadata.ResistanceLevels.First();
            Console.WriteLine($"[METADATA] {metadata.Symbol}: Key resistance at ${strongestResistance.Price:F2} ({strongestResistance.Strength * 100:F0}% strength)");
        }
    }
}
