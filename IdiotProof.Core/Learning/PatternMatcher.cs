// ============================================================================
// Pattern Matcher - Historical Pattern Recognition for Trading
// ============================================================================
//
// Stores historical market snapshots with their LSH signatures and outcomes.
// Finds analog periods in history that match current market conditions.
// Provides probabilistic forecasts based on what happened after similar patterns.
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  N-CUBE GEOMETRY INSIGHT                                                  ║
// ║                                                                           ║
// ║  In high-dimensional spaces (like our 256-bit signature space):          ║
// ║  - Random points cluster around N/2 Hamming distance (128 bits)          ║
// ║  - The spread around the mean is relatively small (~sqrt(N))             ║
// ║  - Genuine similarity shows as distance << N/2                            ║
// ║                                                                           ║
// ║  Thresholds for 256-bit signatures:                                       ║
// ║  - Distance > 140: Random/unrelated                                       ║
// ║  - Distance 100-140: Weak similarity                                      ║
// ║  - Distance 70-100: Moderate similarity (consider)                        ║
// ║  - Distance 40-70: Strong similarity (high confidence)                    ║
// ║  - Distance < 40: Very strong match (rare, high confidence)               ║
// ║                                                                           ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Helpers;

namespace IdiotProof.Learning;

/// <summary>
/// Represents a historical market snapshot with its signature and outcome.
/// </summary>
public sealed class HistoricalPattern
{
    /// <summary>When this pattern occurred.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>The ticker symbol.</summary>
    public string Symbol { get; set; } = "";

    /// <summary>Price at the time of the snapshot.</summary>
    public double Price { get; set; }

    /// <summary>Market score at snapshot time.</summary>
    public int MarketScore { get; set; }

    /// <summary>The LSH signature as hex string.</summary>
    public string SignatureHex { get; set; } = "";

    /// <summary>The raw feature vector (for reference/debugging).</summary>
    public float[]? Features { get; set; }

    /// <summary>Return in the next period (e.g., next 5 min, next hour).</summary>
    public double NextPeriodReturn { get; set; }

    /// <summary>Maximum drawdown in the next period.</summary>
    public double NextPeriodMaxDrawdown { get; set; }

    /// <summary>Maximum gain in the next period.</summary>
    public double NextPeriodMaxGain { get; set; }

    /// <summary>Whether price went higher in the next period.</summary>
    public bool WentHigher { get; set; }

    /// <summary>Whether a long trade would have been profitable.</summary>
    public bool LongProfitable { get; set; }

    /// <summary>Whether a short trade would have been profitable.</summary>
    public bool ShortProfitable { get; set; }

    /// <summary>Gets the binary signature.</summary>
    [JsonIgnore]
    public byte[] Signature => string.IsNullOrEmpty(SignatureHex) 
        ? [] 
        : MarketSignatureGenerator.HexToSignature(SignatureHex);
}

/// <summary>
/// Result of finding similar patterns in history.
/// </summary>
public sealed class PatternMatchResult
{
    /// <summary>The matching historical pattern.</summary>
    public HistoricalPattern Pattern { get; init; } = null!;

    /// <summary>Hamming distance (lower = more similar).</summary>
    public int HammingDistance { get; init; }

    /// <summary>Similarity percentage (0-100, higher = more similar).</summary>
    public double Similarity { get; init; }
}

/// <summary>
/// Aggregated forecast based on multiple analog patterns.
/// </summary>
public sealed class PatternForecast
{
    /// <summary>Number of analog patterns found.</summary>
    public int AnalogCount { get; set; }

    /// <summary>Average Hamming distance of analogs.</summary>
    public double AverageDistance { get; set; }

    /// <summary>Probability of price going higher (0-1).</summary>
    public double ProbabilityHigher { get; set; }

    /// <summary>Probability of long being profitable (0-1).</summary>
    public double ProbabilityLongProfit { get; set; }

    /// <summary>Probability of short being profitable (0-1).</summary>
    public double ProbabilityShortProfit { get; set; }

    /// <summary>Average return of analog periods.</summary>
    public double AverageReturn { get; set; }

    /// <summary>Average max gain seen in analog periods.</summary>
    public double AverageMaxGain { get; set; }

    /// <summary>Average max drawdown seen in analog periods.</summary>
    public double AverageMaxDrawdown { get; set; }

    /// <summary>Confidence level based on distance spread and count.</summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Suggested direction: 1 = long, -1 = short, 0 = neutral.
    /// </summary>
    public int SuggestedDirection { get; set; }

    /// <summary>The individual analog matches.</summary>
    public List<PatternMatchResult> Analogs { get; set; } = [];

    /// <summary>Whether this forecast is usable (enough analogs, reasonable confidence).</summary>
    public bool IsUsable => AnalogCount >= 3 && Confidence >= 0.5;

    public override string ToString()
    {
        var direction = SuggestedDirection switch
        {
            1 => "LONG",
            -1 => "SHORT",
            _ => "NEUTRAL"
        };
        return $"[{AnalogCount} analogs] {direction} | P(up)={ProbabilityHigher:P0} | " +
               $"Avg={AverageReturn:+0.00%;-0.00%} | Conf={Confidence:P0}";
    }
}

/// <summary>
/// Pattern matcher for a specific ticker.
/// Stores historical patterns and finds analogs for current market conditions.
/// </summary>
public sealed class PatternMatcher
{
    private const string PatternFileName = "patterns.json";
    private const int MaxPatterns = 10000;  // Rolling window of patterns
    private const int DefaultMaxAnalogs = 20;
    private const int DefaultMaxDistance = 85;  // ~33% different in 256 bits

    private readonly string _symbol;
    private readonly string _dataDirectory;
    private readonly MarketSignatureGenerator _signatureGenerator;
    private readonly object _lock = new();

    private List<HistoricalPattern> _patterns = [];
    private bool _isDirty;

    /// <summary>
    /// Creates a pattern matcher for a specific ticker.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="dataDirectory">Directory for pattern storage.</param>
    public PatternMatcher(string symbol, string dataDirectory)
    {
        _symbol = symbol.ToUpperInvariant();
        _dataDirectory = Path.Combine(dataDirectory, "Patterns");
        _signatureGenerator = new MarketSignatureGenerator(_dataDirectory);

        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }

        LoadPatterns();
    }

    /// <summary>
    /// Gets the number of stored patterns.
    /// </summary>
    public int PatternCount => _patterns.Count;

    /// <summary>
    /// Gets the underlying signature generator.
    /// </summary>
    public MarketSignatureGenerator SignatureGenerator => _signatureGenerator;

    /// <summary>
    /// Records a historical pattern with its outcome.
    /// Call this after you know what happened (e.g., end of bar/period).
    /// </summary>
    /// <param name="snapshot">The indicator snapshot at the time.</param>
    /// <param name="timestamp">When this occurred.</param>
    /// <param name="price">Price at the time.</param>
    /// <param name="marketScore">Market score at the time.</param>
    /// <param name="nextPeriodReturn">Return in the next period (e.g., % change).</param>
    /// <param name="nextPeriodMaxGain">Maximum gain in next period.</param>
    /// <param name="nextPeriodMaxDrawdown">Maximum drawdown in next period.</param>
    public void RecordPattern(
        IndicatorSnapshot snapshot,
        DateTime timestamp,
        double price,
        int marketScore,
        double nextPeriodReturn,
        double nextPeriodMaxGain,
        double nextPeriodMaxDrawdown)
    {
        var features = _signatureGenerator.ToFeatureVector(snapshot);
        var signature = _signatureGenerator.GetSignature(features);

        var pattern = new HistoricalPattern
        {
            Timestamp = timestamp,
            Symbol = _symbol,
            Price = price,
            MarketScore = marketScore,
            SignatureHex = MarketSignatureGenerator.SignatureToHex(signature),
            Features = features,
            NextPeriodReturn = nextPeriodReturn,
            NextPeriodMaxGain = nextPeriodMaxGain,
            NextPeriodMaxDrawdown = nextPeriodMaxDrawdown,
            WentHigher = nextPeriodReturn > 0,
            LongProfitable = nextPeriodReturn > 0.001,  // 0.1% threshold
            ShortProfitable = nextPeriodReturn < -0.001
        };

        lock (_lock)
        {
            _patterns.Add(pattern);

            // Trim to max size (remove oldest)
            while (_patterns.Count > MaxPatterns)
            {
                _patterns.RemoveAt(0);
            }

            _isDirty = true;
        }
    }

    /// <summary>
    /// Finds analog patterns in history that are similar to the current snapshot.
    /// </summary>
    /// <param name="currentSnapshot">Current indicator snapshot.</param>
    /// <param name="maxResults">Maximum number of analogs to return.</param>
    /// <param name="maxDistance">Maximum Hamming distance to consider.</param>
    /// <returns>List of matching patterns sorted by similarity.</returns>
    public List<PatternMatchResult> FindAnalogs(
        IndicatorSnapshot currentSnapshot,
        int maxResults = DefaultMaxAnalogs,
        int maxDistance = DefaultMaxDistance)
    {
        var currentSignature = _signatureGenerator.GetSignature(currentSnapshot);
        return FindAnalogsBySignature(currentSignature, maxResults, maxDistance);
    }

    /// <summary>
    /// Finds analog patterns by pre-computed signature.
    /// </summary>
    public List<PatternMatchResult> FindAnalogsBySignature(
        byte[] currentSignature,
        int maxResults = DefaultMaxAnalogs,
        int maxDistance = DefaultMaxDistance)
    {
        var results = new List<PatternMatchResult>();

        lock (_lock)
        {
            foreach (var pattern in _patterns)
            {
                var signature = pattern.Signature;
                if (signature.Length == 0) continue;

                var distance = LSHService.HammingDistance(currentSignature, signature);

                if (distance <= maxDistance)
                {
                    results.Add(new PatternMatchResult
                    {
                        Pattern = pattern,
                        HammingDistance = distance,
                        Similarity = _signatureGenerator.LSH.ComputeSimilarity(currentSignature, signature)
                    });
                }
            }
        }

        // Sort by distance (closest first), take top N
        return results
            .OrderBy(r => r.HammingDistance)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Generates a probabilistic forecast based on analog patterns.
    /// </summary>
    /// <param name="currentSnapshot">Current indicator snapshot.</param>
    /// <param name="maxAnalogs">Maximum number of analogs to consider.</param>
    /// <param name="maxDistance">Maximum Hamming distance to consider.</param>
    /// <returns>Aggregated forecast based on what happened after similar patterns.</returns>
    public PatternForecast GetForecast(
        IndicatorSnapshot currentSnapshot,
        int maxAnalogs = DefaultMaxAnalogs,
        int maxDistance = DefaultMaxDistance)
    {
        var analogs = FindAnalogs(currentSnapshot, maxAnalogs, maxDistance);
        return ComputeForecast(analogs);
    }

    /// <summary>
    /// Computes a forecast from a list of analog matches.
    /// </summary>
    private PatternForecast ComputeForecast(List<PatternMatchResult> analogs)
    {
        var forecast = new PatternForecast { Analogs = analogs };

        if (analogs.Count == 0)
        {
            return forecast;
        }

        forecast.AnalogCount = analogs.Count;
        forecast.AverageDistance = analogs.Average(a => a.HammingDistance);

        // Weight by similarity (closer = higher weight)
        double totalWeight = 0;
        double weightedHigher = 0;
        double weightedLongProfit = 0;
        double weightedShortProfit = 0;
        double weightedReturn = 0;
        double weightedMaxGain = 0;
        double weightedMaxDrawdown = 0;

        foreach (var analog in analogs)
        {
            // Weight: inverse of distance (but ensure minimum)
            var weight = Math.Max(0.1, 1.0 - analog.HammingDistance / 256.0);
            totalWeight += weight;

            weightedHigher += analog.Pattern.WentHigher ? weight : 0;
            weightedLongProfit += analog.Pattern.LongProfitable ? weight : 0;
            weightedShortProfit += analog.Pattern.ShortProfitable ? weight : 0;
            weightedReturn += analog.Pattern.NextPeriodReturn * weight;
            weightedMaxGain += analog.Pattern.NextPeriodMaxGain * weight;
            weightedMaxDrawdown += analog.Pattern.NextPeriodMaxDrawdown * weight;
        }

        forecast.ProbabilityHigher = weightedHigher / totalWeight;
        forecast.ProbabilityLongProfit = weightedLongProfit / totalWeight;
        forecast.ProbabilityShortProfit = weightedShortProfit / totalWeight;
        forecast.AverageReturn = weightedReturn / totalWeight;
        forecast.AverageMaxGain = weightedMaxGain / totalWeight;
        forecast.AverageMaxDrawdown = weightedMaxDrawdown / totalWeight;

        // Confidence based on:
        // 1. Number of analogs (more = better)
        // 2. Average distance (closer = better)
        // 3. Consistency of outcomes (all agree = better)
        var countFactor = Math.Min(1.0, analogs.Count / 10.0);  // Max at 10 analogs
        var distanceFactor = Math.Max(0, 1.0 - forecast.AverageDistance / 128.0);  // Better if < 128
        var consistencyFactor = Math.Abs(forecast.ProbabilityHigher - 0.5) * 2;  // 0-1 based on consistency

        forecast.Confidence = (countFactor * 0.3 + distanceFactor * 0.4 + consistencyFactor * 0.3);

        // Suggested direction
        if (forecast.ProbabilityHigher > 0.6)
            forecast.SuggestedDirection = 1;  // Long
        else if (forecast.ProbabilityHigher < 0.4)
            forecast.SuggestedDirection = -1; // Short
        else
            forecast.SuggestedDirection = 0;  // Neutral

        return forecast;
    }

    /// <summary>
    /// Saves patterns to disk.
    /// </summary>
    public void Save()
    {
        if (!_isDirty) return;

        lock (_lock)
        {
            try
            {
                var path = Path.Combine(_dataDirectory, $"{_symbol}_{PatternFileName}");
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = false,  // Compact for large files
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var json = JsonSerializer.Serialize(_patterns, options);
                File.WriteAllText(path, json);
                _isDirty = false;
            }
            catch
            {
                // Log error but don't throw - patterns are still in memory
            }
        }
    }

    /// <summary>
    /// Loads patterns from disk.
    /// </summary>
    private void LoadPatterns()
    {
        try
        {
            var path = Path.Combine(_dataDirectory, $"{_symbol}_{PatternFileName}");
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var patterns = JsonSerializer.Deserialize<List<HistoricalPattern>>(json);

            if (patterns != null)
            {
                lock (_lock)
                {
                    _patterns = patterns;
                }
            }
        }
        catch
        {
            // Start fresh if can't load
            _patterns = new List<HistoricalPattern>();
        }
    }

    /// <summary>
    /// Gets statistics about stored patterns.
    /// </summary>
    public string GetStatistics()
    {
        lock (_lock)
        {
            if (_patterns.Count == 0)
                return $"{_symbol}: No patterns stored";

            var oldest = _patterns.Min(p => p.Timestamp);
            var newest = _patterns.Max(p => p.Timestamp);
            var bullish = _patterns.Count(p => p.WentHigher);
            var bearish = _patterns.Count - bullish;

            return $"{_symbol}: {_patterns.Count} patterns | " +
                   $"{oldest:yyyy-MM-dd} to {newest:yyyy-MM-dd} | " +
                   $"Bullish:{bullish} Bearish:{bearish}";
        }
    }

    /// <summary>
    /// Clears all stored patterns.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _patterns.Clear();
            _isDirty = true;
        }
    }
}

/// <summary>
/// Manages pattern matchers for multiple tickers.
/// </summary>
public sealed class PatternMatcherManager
{
    private readonly string _dataDirectory;
    private readonly Dictionary<string, PatternMatcher> _matchers = new();
    private readonly object _lock = new();

    /// <summary>
    /// Creates a pattern matcher manager.
    /// </summary>
    /// <param name="dataDirectory">Base directory for pattern storage.</param>
    public PatternMatcherManager(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    /// <summary>
    /// Gets or creates a pattern matcher for a ticker.
    /// </summary>
    public PatternMatcher GetMatcher(string symbol)
    {
        var key = symbol.ToUpperInvariant();

        lock (_lock)
        {
            if (!_matchers.TryGetValue(key, out var matcher))
            {
                matcher = new PatternMatcher(key, _dataDirectory);
                _matchers[key] = matcher;
            }
            return matcher;
        }
    }

    /// <summary>
    /// Saves all pattern matchers.
    /// </summary>
    public void SaveAll()
    {
        lock (_lock)
        {
            foreach (var matcher in _matchers.Values)
            {
                matcher.Save();
            }
        }
    }

    /// <summary>
    /// Gets statistics for all tracked tickers.
    /// </summary>
    public IEnumerable<string> GetAllStatistics()
    {
        lock (_lock)
        {
            return _matchers.Values.Select(m => m.GetStatistics()).ToList();
        }
    }
}
