// ============================================================================
// StockDescriptionService - Fetches and Caches Stock Descriptions
// ============================================================================
//
// PURPOSE:
// Provides short, succinct descriptions of what a stock/company is.
// Fetches from ChatGPT in a SINGLE batch call and caches locally.
//
// DESIGN:
// - 60+ well-known tickers have built-in descriptions (no API needed)
// - Unknown tickers are batched into ONE ChatGPT API call
// - Each description is stored as {SYMBOL}.description.json (like history/weights)
// - Results are cached for 30 days
//
// FILE PATTERN:
//   {SolutionRoot}\IdiotProof.Core\Data\NVDA\NVDA.description.json
//   {SolutionRoot}\IdiotProof.Core\Data\AAPL\AAPL.description.json
//   {SolutionRoot}\IdiotProof.Core\Data\CCHH\CCHH.description.json
//
// USAGE (correct pattern - ONE API call for all unknown symbols):
//   // Step 1: Fetch all missing descriptions in ONE ChatGPT call
//   var symbols = new[] { "NVDA", "AAPL", "CCHH", "UNKNOWN1", "UNKNOWN2" };
//   await StockDescriptionService.FetchMissingDescriptionsAsync(symbols);
//
//   // Step 2: Get descriptions from cache (no API calls)
//   foreach (var symbol in symbols)
//   {
//       var desc = StockDescriptionService.GetDescription(symbol);
//       // NVDA -> "NVIDIA Corp. - GPU & AI chips" (well-known)
//       // CCHH -> "CCHH Inc. - Biotech company" (fetched + cached)
//   }
//
// ============================================================================

using System.Text.Json;
using IdiotProof.Logging;
using IdiotProof.Settings;

namespace IdiotProof.Services;

/// <summary>
/// A cached stock description entry.
/// </summary>
public sealed class StockDescriptionEntry
{
    public string Symbol { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime FetchedAt { get; set; }
}

/// <summary>
/// Service for fetching and caching stock descriptions from ChatGPT.
/// Each description is stored as {SYMBOL}.description.json in the Data folder.
/// </summary>
public static class StockDescriptionService
{
    private const string DescriptionFileSuffix = ".description.json";
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromDays(30); // Descriptions rarely change
    
    private static Dictionary<string, StockDescriptionEntry>? _cache;
    private static readonly object _lock = new();
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Well-known descriptions for common tickers (fallback / instant lookup)
    private static readonly Dictionary<string, string> WellKnownDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AAPL"] = "Apple Inc. - Consumer electronics & software",
        ["MSFT"] = "Microsoft Corp. - Enterprise software & cloud",
        ["GOOG"] = "Alphabet Inc. - Search & advertising",
        ["GOOGL"] = "Alphabet Inc. - Class A shares",
        ["AMZN"] = "Amazon.com - E-commerce & cloud services",
        ["NVDA"] = "NVIDIA Corp. - GPU & AI chips",
        ["TSLA"] = "Tesla Inc. - Electric vehicles & energy",
        ["META"] = "Meta Platforms - Social media & VR",
        ["AMD"] = "Advanced Micro Devices - CPUs & GPUs",
        ["INTC"] = "Intel Corp. - Semiconductors",
        ["SPY"] = "S&P 500 ETF - Index tracking fund",
        ["QQQ"] = "Nasdaq-100 ETF - Tech-heavy index fund",
        ["IWM"] = "Russell 2000 ETF - Small-cap index",
        ["DIA"] = "Dow Jones ETF - Blue chip index",
        ["NFLX"] = "Netflix Inc. - Streaming entertainment",
        ["CRM"] = "Salesforce Inc. - CRM & enterprise cloud",
        ["ORCL"] = "Oracle Corp. - Enterprise databases & cloud",
        ["IBM"] = "IBM Corp. - Enterprise IT & consulting",
        ["UBER"] = "Uber Technologies - Ride-hailing & delivery",
        ["LYFT"] = "Lyft Inc. - Ride-hailing services",
        ["COIN"] = "Coinbase Global - Cryptocurrency exchange",
        ["HOOD"] = "Robinhood Markets - Commission-free trading",
        ["PLTR"] = "Palantir Technologies - Data analytics",
        ["SNOW"] = "Snowflake Inc. - Cloud data platform",
        ["SQ"] = "Block Inc. (Square) - Digital payments",
        ["PYPL"] = "PayPal Holdings - Online payments",
        ["V"] = "Visa Inc. - Payment processing",
        ["MA"] = "Mastercard Inc. - Payment processing",
        ["JPM"] = "JPMorgan Chase - Investment banking",
        ["BAC"] = "Bank of America - Banking & finance",
        ["WFC"] = "Wells Fargo - Banking & finance",
        ["GS"] = "Goldman Sachs - Investment banking",
        ["MS"] = "Morgan Stanley - Investment banking",
        ["C"] = "Citigroup Inc. - Global banking",
        ["BA"] = "Boeing Co. - Aerospace & defense",
        ["LMT"] = "Lockheed Martin - Defense contractor",
        ["RTX"] = "RTX Corp. - Aerospace & defense",
        ["CAT"] = "Caterpillar Inc. - Heavy machinery",
        ["DE"] = "Deere & Co. - Agricultural equipment",
        ["XOM"] = "ExxonMobil - Oil & gas major",
        ["CVX"] = "Chevron Corp. - Oil & gas major",
        ["COP"] = "ConocoPhillips - Oil & gas E&P",
        ["JNJ"] = "Johnson & Johnson - Healthcare conglomerate",
        ["PFE"] = "Pfizer Inc. - Pharmaceuticals",
        ["MRNA"] = "Moderna Inc. - mRNA therapeutics",
        ["UNH"] = "UnitedHealth Group - Health insurance",
        ["WMT"] = "Walmart Inc. - Retail giant",
        ["TGT"] = "Target Corp. - Retail chain",
        ["COST"] = "Costco Wholesale - Warehouse retail",
        ["HD"] = "Home Depot - Home improvement retail",
        ["LOW"] = "Lowe's Companies - Home improvement retail",
        ["NKE"] = "Nike Inc. - Athletic footwear & apparel",
        ["DIS"] = "Walt Disney Co. - Entertainment & media",
        ["CMCSA"] = "Comcast Corp. - Cable & media",
        ["T"] = "AT&T Inc. - Telecommunications",
        ["VZ"] = "Verizon Communications - Telecom",
        ["TMUS"] = "T-Mobile US - Wireless carrier"
    };

    /// <summary>
    /// Gets the path to a ticker's description file.
    /// Stored in per-ticker subfolder: Data/{SYMBOL}/{SYMBOL}.description.json
    /// </summary>
    private static string GetDescriptionPath(string symbol)
    {
        // Store in Data/{SYMBOL}/{SYMBOL}.description.json
        var tickerFolder = SettingsManager.GetTickerDataFolder(symbol);
        return Path.Combine(tickerFolder, $"{symbol.ToUpperInvariant()}{DescriptionFileSuffix}");
    }

    /// <summary>
    /// Loads all cached descriptions from individual files.
    /// </summary>
    private static void EnsureCacheLoaded()
    {
        if (_cache != null) return;

        lock (_lock)
        {
            if (_cache != null) return;

            _cache = new Dictionary<string, StockDescriptionEntry>(StringComparer.OrdinalIgnoreCase);
            var dataFolder = SettingsManager.GetDataFolder();

            if (!Directory.Exists(dataFolder))
                return;

            // Scan for all *.description.json files in per-ticker subfolders
            var descFiles = Directory.GetFiles(dataFolder, $"*{DescriptionFileSuffix}", SearchOption.AllDirectories);
            foreach (var file in descFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var entry = JsonSerializer.Deserialize<StockDescriptionEntry>(json, JsonOptions);
                    if (entry != null && !string.IsNullOrEmpty(entry.Symbol))
                    {
                        _cache[entry.Symbol] = entry;
                    }
                }
                catch
                {
                    // Skip invalid files
                }
            }
        }
    }

    /// <summary>
    /// Saves a single ticker's description to its own file.
    /// </summary>
    private static void SaveDescription(StockDescriptionEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Symbol)) return;

        try
        {
            var path = GetDescriptionPath(entry.Symbol);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(entry, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            ConsoleLog.Warn("StockDesc", $"Failed to save {entry.Symbol}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a single ticker's description from its file.
    /// </summary>
    private static StockDescriptionEntry? LoadDescription(string symbol)
    {
        var path = GetDescriptionPath(symbol);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StockDescriptionEntry>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the description for a stock symbol (from cache/well-known only, NO API call).
    /// Use FetchMissingDescriptionsAsync() first to batch-fetch all unknown symbols.
    /// </summary>
    /// <param name="symbol">Stock ticker symbol.</param>
    /// <returns>Short description or placeholder if not cached.</returns>
    public static string GetDescription(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return "";

        symbol = symbol.ToUpperInvariant().Trim();
        EnsureCacheLoaded();

        // Check well-known descriptions first (instant)
        if (WellKnownDescriptions.TryGetValue(symbol, out var wellKnown))
        {
            return wellKnown;
        }

        // Check cache
        lock (_lock)
        {
            if (_cache!.TryGetValue(symbol, out var cached))
            {
                if (DateTime.UtcNow - cached.FetchedAt < CacheExpiry)
                {
                    return cached.Description;
                }
            }
        }

        // Not in cache - return placeholder (use FetchMissingDescriptionsAsync to populate)
        return $"{symbol} - (not fetched)";
    }

    /// <summary>
    /// Fetches descriptions for ALL unknown symbols in a single ChatGPT API call.
    /// Call this ONCE at startup with all your ticker symbols, then use GetDescription() or GetDescriptionSync().
    /// </summary>
    /// <param name="symbols">All symbols you need descriptions for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary of all descriptions (cached + freshly fetched).</returns>
    public static async Task<Dictionary<string, string>> FetchMissingDescriptionsAsync(
        IEnumerable<string> symbols, 
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var toFetch = new List<string>();

        EnsureCacheLoaded();

        foreach (var symbol in symbols)
        {
            var s = symbol.ToUpperInvariant().Trim();
            
            // Check well-known first
            if (WellKnownDescriptions.TryGetValue(s, out var wellKnown))
            {
                result[s] = wellKnown;
                continue;
            }

            // Check cache
            lock (_lock)
            {
                if (_cache!.TryGetValue(s, out var cached) && 
                    DateTime.UtcNow - cached.FetchedAt < CacheExpiry)
                {
                    result[s] = cached.Description;
                    continue;
                }
            }

            toFetch.Add(s);
        }

        // Fetch remaining symbols from ChatGPT (batch if multiple)
        if (toFetch.Count > 0)
        {
            try
            {
                var fetched = await FetchBatchFromChatGptAsync(toFetch, ct);
                foreach (var kvp in fetched)
                {
                    result[kvp.Key] = kvp.Value;
                    
                    var entry = new StockDescriptionEntry
                    {
                        Symbol = kvp.Key,
                        Description = kvp.Value,
                        FetchedAt = DateTime.UtcNow
                    };
                    
                    lock (_lock)
                    {
                        _cache![kvp.Key] = entry;
                    }
                    
                    // Save each ticker to its own file
                    SaveDescription(entry);
                }
            }
            catch (Exception ex)
            {
                ConsoleLog.Warn("StockDesc", $"Failed to batch fetch: {ex.Message}");
                
                // Fill in with unknown for failed fetches
                foreach (var s in toFetch)
                {
                    if (!result.ContainsKey(s))
                    {
                        result[s] = $"{s} - Unknown";
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Fetches descriptions from ChatGPT for multiple symbols in ONE API call.
    /// This is the ONLY method that calls ChatGPT - all symbols are batched together.
    /// </summary>
    private static async Task<Dictionary<string, string>> FetchBatchFromChatGptAsync(
        List<string> symbols, 
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var openai = new OpenAIService();
        
        if (!openai.IsConfigured)
        {
            foreach (var s in symbols)
            {
                result[s] = $"{s} - (AI not configured)";
            }
            return result;
        }

        var symbolList = string.Join(", ", symbols);
        var prompt = $"""
            For each stock ticker, provide a very short description (max 6 words each).
            Format each line as: TICKER: Company Name - Business
            
            Tickers: {symbolList}
            
            Example output:
            NVDA: NVIDIA Corp. - GPU & AI chips
            AAPL: Apple Inc. - Consumer electronics
            
            List only the tickers requested, one per line.
            """;

        var reply = await openai.AskWithInstructionsAsync(
            prompt,
            "You are a financial data assistant. Give extremely concise stock descriptions, one per line.",
            ct);

        // Parse the response
        var lines = reply.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var ticker = line[..colonIndex].Trim().ToUpperInvariant();
                var description = line[(colonIndex + 1)..].Trim();
                
                if (symbols.Contains(ticker, StringComparer.OrdinalIgnoreCase))
                {
                    result[ticker] = CleanDescription(description);
                }
            }
        }

        // Fill in any missing with unknown
        foreach (var s in symbols)
        {
            if (!result.ContainsKey(s))
            {
                result[s] = $"{s} - Unknown";
            }
        }

        return result;
    }

    /// <summary>
    /// Cleans up a description from ChatGPT response.
    /// </summary>
    private static string CleanDescription(string description)
    {
        // Remove quotes, extra whitespace, etc.
        var cleaned = description
            .Trim()
            .Trim('"', '\'')
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim();

        // Limit length to 1024 chars (allows long descriptions)
        if (cleaned.Length > 1024)
        {
            cleaned = cleaned[..1021] + "...";
        }

        return cleaned;
    }

    /// <summary>
    /// Adds or updates a description in the cache (for manual entries).
    /// </summary>
    public static void SetDescription(string symbol, string description)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        
        symbol = symbol.ToUpperInvariant().Trim();
        EnsureCacheLoaded();

        var entry = new StockDescriptionEntry
        {
            Symbol = symbol,
            Description = description,
            FetchedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _cache![symbol] = entry;
        }
        
        // Save to individual file
        SaveDescription(entry);
    }

    /// <summary>
    /// Gets the synchronous description (from cache/well-known only, no API call).
    /// Returns null if not cached - use this to check if fetching is needed.
    /// </summary>
    public static string? GetDescriptionSync(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        symbol = symbol.ToUpperInvariant().Trim();

        // Check well-known first
        if (WellKnownDescriptions.TryGetValue(symbol, out var wellKnown))
        {
            return wellKnown;
        }

        // Check cache
        EnsureCacheLoaded();
        lock (_lock)
        {
            if (_cache!.TryGetValue(symbol, out var cached) && 
                DateTime.UtcNow - cached.FetchedAt < CacheExpiry)
            {
                return cached.Description;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the list of symbols that don't have cached descriptions.
    /// Call FetchMissingDescriptionsAsync() with these symbols to populate them.
    /// </summary>
    public static List<string> GetSymbolsNeedingFetch(IEnumerable<string> symbols)
    {
        var toFetch = new List<string>();
        EnsureCacheLoaded();

        foreach (var symbol in symbols)
        {
            var s = symbol.ToUpperInvariant().Trim();
            
            // Skip well-known
            if (WellKnownDescriptions.ContainsKey(s))
                continue;

            // Skip cached
            lock (_lock)
            {
                if (_cache!.TryGetValue(s, out var cached) && 
                    DateTime.UtcNow - cached.FetchedAt < CacheExpiry)
                    continue;
            }

            toFetch.Add(s);
        }

        return toFetch;
    }
}
