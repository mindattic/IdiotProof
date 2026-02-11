// ============================================================================
// Watchlist Manager - Loads Ticker List for Autonomous Trading
// ============================================================================
//
// PURPOSE:
// Simple JSON file to configure which tickers to trade and how much.
// Each ticker must be enabled AND have allocation > $0 to trade.
// Edit the file outside the app, restart, and it trades automatically.
//
// FILE LOCATION:
//   {SolutionRoot}\IdiotProof.Core\Data\watchlist.json
//
// FILE FORMAT:
// {
//   "tickers": [
//     { "symbol": "NVDA", "allocation": 1000, "enabled": true },
//     { "symbol": "AAPL", "allocation": 0, "enabled": false },
//     { "symbol": "TSLA", "allocation": 500, "enabled": true }
//   ],
//   "session": "RTH",
//   "enabled": true
// }
//
// USAGE:
//   var watchlist = WatchlistManager.Load();
//   foreach (var ticker in watchlist.Tickers)
//   {
//       // Create autonomous trading strategy for each ticker
//   }
//
// ============================================================================

using IdiotProof.Constants;
using IdiotProof.Enums;
using IdiotProof.Logging;
using IdiotProof.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace IdiotProof.Services;

/// <summary>
/// A single ticker entry in the watchlist.
/// </summary>
public sealed class WatchlistEntry
{
    /// <summary>Ticker symbol (e.g., "NVDA", "AAPL").</summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    /// <summary>Optional friendly name for this ticker.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Dollar allocation for this ticker (how much you're willing to risk). $0 = not configured (will not trade).</summary>
    [JsonPropertyName("allocation")]
    public double Allocation { get; set; } = 0;

    /// <summary>Whether this ticker is enabled for trading (default: false - must be manually turned on).</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    /// <summary>Whether this ticker is ready to trade (enabled AND has allocation).</summary>
    [JsonIgnore]
    public bool IsReadyToTrade => Enabled && Allocation > 0;

    /// <summary>
    /// Calculate number of shares to buy based on current price.
    /// Returns 0 if allocation is not configured.
    /// </summary>
    public int GetQuantityForPrice(double price)
    {
        if (price <= 0 || Allocation <= 0) return 0;
        return (int)Math.Floor(Allocation / price);
    }

    public override string ToString() => $"{Symbol} ${Allocation:F0}" + (Enabled ? "" : " [DISABLED]");
}

/// <summary>
/// The watchlist configuration loaded from watchlist.json.
/// </summary>
public sealed class Watchlist
{
    /// <summary>List of tickers to trade.</summary>
    [JsonPropertyName("tickers")]
    public List<WatchlistEntry> Tickers { get; set; } = [];

    /// <summary>Default trading session for all tickers (RTH, Premarket, AfterHours, Extended).</summary>
    [JsonPropertyName("session")]
    public string Session { get; set; } = "RTH";

    /// <summary>Whether autonomous trading is enabled globally.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Optional: Description/notes about this watchlist.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets only tickers that are enabled AND have allocation configured.</summary>
    [JsonIgnore]
    public IEnumerable<WatchlistEntry> EnabledTickers => Tickers.Where(t => t.IsReadyToTrade && !string.IsNullOrEmpty(t.Symbol));

    /// <summary>Gets the total number of tickers.</summary>
    [JsonIgnore]
    public int TotalCount => Tickers.Count;

    /// <summary>Gets the number of enabled tickers.</summary>
    [JsonIgnore]
    public int EnabledCount => EnabledTickers.Count();

    /// <summary>Parses the default session.</summary>
    [JsonIgnore]
    public TradingSession DefaultSession => Session?.ToUpperInvariant() switch
    {
        "PREMARKET" or "PRE" or "PM" => TradingSession.PreMarket,
        "AFTERHOURS" or "AFTER" or "AH" => TradingSession.AfterHours,
        "EXTENDED" or "EXT" or "ALL" => TradingSession.Extended,
        _ => TradingSession.RTH
    };
}

/// <summary>
/// Manages the watchlist.json file for autonomous trading configuration.
/// </summary>
public static class WatchlistManager
{
    private const string WatchlistFileName = "watchlist.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Gets the path to the watchlist.json file.
    /// </summary>
    public static string GetWatchlistPath()
    {
        return Path.Combine(SettingsManager.GetDataFolder(), WatchlistFileName);
    }

    /// <summary>
    /// Checks if a watchlist file exists.
    /// </summary>
    public static bool Exists()
    {
        return File.Exists(GetWatchlistPath());
    }

    /// <summary>
    /// Loads the watchlist from the JSON file.
    /// Returns an empty watchlist if file doesn't exist.
    /// </summary>
    public static Watchlist Load()
    {
        var path = GetWatchlistPath();

        if (!File.Exists(path))
        {
            ConsoleLog.Write("Watchlist", $"No watchlist found at: {path}");
            ConsoleLog.Write("Watchlist", "Create one to enable autonomous trading.");
            return new Watchlist();
        }

        try
        {
            var json = File.ReadAllText(path);
            var watchlist = JsonSerializer.Deserialize<Watchlist>(json, JsonOptions);

            if (watchlist == null)
            {
                ConsoleLog.Warn("Watchlist", "Failed to parse watchlist, using empty list");
                return new Watchlist();
            }

            ConsoleLog.Write("Watchlist", $"Loaded {watchlist.EnabledCount}/{watchlist.TotalCount} tickers from {WatchlistFileName}");
            foreach (var ticker in watchlist.Tickers)
            {
                if (ticker.IsReadyToTrade)
                {
                    ConsoleLog.Write("Watchlist", $"  * {ticker.Symbol} (${ticker.Allocation:F0})");
                }
                else
                {
                    var reason = !ticker.Enabled ? "disabled" : "$0 allocation";
                    ConsoleLog.Write("Watchlist", $"  o {ticker.Symbol} ({reason})");
                }
            }

            return watchlist;
        }
        catch (Exception ex)
        {
            ConsoleLog.Error("Watchlist", $"Loading failed: {ex.Message}");
            return new Watchlist();
        }
    }

    /// <summary>
    /// Saves a watchlist to the JSON file.
    /// </summary>
    public static void Save(Watchlist watchlist)
    {
        var path = GetWatchlistPath();

        try
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(watchlist, JsonOptions);
            File.WriteAllText(path, json);

            ConsoleLog.Write("Watchlist", $"Saved {watchlist.TotalCount} tickers to {WatchlistFileName}");
        }
        catch (Exception ex)
        {
            ConsoleLog.Error("Watchlist", $"Saving failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a sample watchlist file if none exists.
    /// All tickers start disabled with $0 allocation - user must manually configure.
    /// </summary>
    public static void CreateSampleIfNotExists()
    {
        if (Exists())
            return;

        var sample = new Watchlist
        {
            Description = "Autonomous Trading Watchlist - Edit this file to add/remove tickers. Set allocation and enabled=true to trade.",
            Session = "RTH",
            Enabled = true,
            Tickers =
            [
                new() { Symbol = "NVDA", Allocation = 0, Enabled = false },
                new() { Symbol = "AAPL", Allocation = 0, Enabled = false },
                new() { Symbol = "TSLA", Allocation = 0, Enabled = false },
                new() { Symbol = "SPY", Allocation = 0, Enabled = false }
            ]
        };

        Save(sample);
        ConsoleLog.Write("Watchlist", $"Created sample watchlist at: {GetWatchlistPath()}");
        ConsoleLog.Write("Watchlist", "Edit this file to enable tickers and set allocations.");
    }

    /// <summary>
    /// Adds a ticker to the watchlist (or updates allocation if exists).
    /// New tickers start disabled with $0 allocation by default.
    /// </summary>
    public static void AddOrUpdate(string symbol, double allocation)
    {
        var watchlist = Load();
        var existing = watchlist.Tickers.FirstOrDefault(t => 
            t.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Allocation = allocation;
            if (allocation > 0) existing.Enabled = true;
        }
        else
        {
            watchlist.Tickers.Add(new WatchlistEntry
            {
                Symbol = symbol.ToUpperInvariant(),
                Allocation = allocation,
                Enabled = allocation > 0  // Only enable if allocation is configured
            });
        }

        Save(watchlist);
    }

    /// <summary>
    /// Removes a ticker from the watchlist.
    /// </summary>
    public static bool Remove(string symbol)
    {
        var watchlist = Load();
        var removed = watchlist.Tickers.RemoveAll(t => 
            t.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)) > 0;

        if (removed)
        {
            Save(watchlist);
        }

        return removed;
    }

    /// <summary>
    /// Disables a ticker without removing it.
    /// </summary>
    public static bool Disable(string symbol)
    {
        var watchlist = Load();
        var ticker = watchlist.Tickers.FirstOrDefault(t => 
            t.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (ticker != null)
        {
            ticker.Enabled = false;
            Save(watchlist);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Generates IdiotScript strategies for all enabled tickers in the watchlist.
    /// </summary>
    public static IEnumerable<string> GenerateScripts()
    {
        var watchlist = Load();
        
        if (!watchlist.Enabled)
        {
            ConsoleLog.Warn("Watchlist", "Autonomous trading is disabled globally");
            yield break;
        }

        foreach (var ticker in watchlist.EnabledTickers)
        {
            var session = watchlist.Session;
            var name = $"{ticker.Symbol} Auto";
            var alloc = ticker.Allocation;

            // Skip tickers with no allocation configured
            if (alloc <= 0) continue;

            // Generate IdiotScript for this ticker (allocation-based, quantity calculated at order time)
            var script = $"Ticker({ticker.Symbol})" +
                        $".Name(\"{name}\")" +
                        $".Session(IS.{session.ToUpperInvariant()})" +
                        $".Allocation({alloc:F0})" +
                        $".AutonomousTrading()";

            yield return script;
        }
    }

    /// <summary>
    /// Prints a summary of the current watchlist.
    /// </summary>
    public static void PrintSummary()
    {
        PrintSummaryWithPrices(null);
    }

    /// <summary>
    /// Prints a summary of the current watchlist with price information.
    /// </summary>
    /// <param name="priceProvider">Optional function to get current price for a symbol.</param>
    public static void PrintSummaryWithPrices(Func<string, double>? priceProvider)
    {
        var watchlist = Load();

        Console.WriteLine();
        Console.WriteLine("=== Ticker Watchlist ===");
        Console.WriteLine();

        if (watchlist.Tickers.Count == 0)
        {
            Console.WriteLine("  (no tickers - press 1 to add)");
            Console.WriteLine();
            return;
        }

        // Check if any tickers need descriptions fetched from ChatGPT
        var symbols = watchlist.Tickers.Select(t => t.Symbol).ToList();
        var missingDesc = symbols.Where(s => 
            StockDescriptionService.GetDescription(s).Contains("(not fetched)")).ToList();
        
        if (missingDesc.Count > 0)
        {
            Console.WriteLine($"  Fetching {missingDesc.Count} description(s) from AI...");
            try
            {
                StockDescriptionService.FetchMissingDescriptionsAsync(missingDesc).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Could not fetch descriptions: {ex.Message}");
            }
        }

        double totalInvestment = 0;
        int rowNum = 0;
        
        foreach (var ticker in watchlist.Tickers.OrderBy(t => t.Symbol))
        {
            rowNum++;

            // Determine if this ticker is active (enabled + has allocation)
            bool isActive = ticker.IsReadyToTrade;
            string status;
            if (!ticker.Enabled)
                status = "[OFF]";
            else if (ticker.Allocation <= 0)
                status = "[$0]";
            else
                status = "[ACTIVE]";

            // Get price: try live provider first, then cache
            double price = 0;
            if (priceProvider != null)
            {
                price = priceProvider(ticker.Symbol);
                if (price > 0)
                    TickerDataCache.UpdatePrice(ticker.Symbol, price);
            }
            if (price <= 0)
            {
                var cached = TickerDataCache.Get(ticker.Symbol);
                price = cached?.Price ?? 0;
            }
            
            var priceStr = price > 0 ? $"${price:F2}" : "--";

            // Show allocation - $0 means not configured
            var allocStr = ticker.Allocation > 0 ? $"${ticker.Allocation:F0}" : "  $0";
            
            if (isActive)
                totalInvestment += ticker.Allocation;

            // Get description
            var desc = StockDescriptionService.GetDescription(ticker.Symbol);

            // Disabled or unconfigured tickers render in gray to make it obvious
            if (!isActive)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
            }

            // Format:  1  CCHH        $1000   $0.82  [ACTIVE]    China Ceramics Co. Ltd.
            Console.WriteLine($" {rowNum,2}  {ticker.Symbol,-10}{allocStr,7}  {priceStr,7}  {status,-10}  {desc}");

            if (!isActive)
            {
                Console.ResetColor();
            }
        }

        Console.WriteLine();
        if (totalInvestment > 0)
        {
            Console.WriteLine($"  Total Allocated: ${totalInvestment:F0}");
            Console.WriteLine();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  No tickers configured. Set allocation > $0 and enabled = true to trade.");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Prints a summary of the current watchlist with price information (async).
    /// </summary>
    /// <param name="priceProvider">Optional function to get current price for a symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task PrintSummaryAsync(Func<string, double>? priceProvider, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        PrintSummaryWithPrices(priceProvider);
    }

    /// <summary>
    /// Adds multiple tickers from a comma-separated string.
    /// Quantity is set to 0 (auto-calculated by price tier).
    /// </summary>
    /// <param name="commaSeparatedTickers">Comma-separated ticker symbols (e.g., "NVDA, AAPL, TSLA, CCHH")</param>
    /// <returns>Number of tickers added.</returns>
    /// <example>
    /// WatchlistManager.AddFromCsv("NVDA, AAPL, TSLA");
    /// // Adds all 3 with Quantity=0 (auto tier-based allocation)
    /// </example>
    public static int AddFromCsv(string commaSeparatedTickers)
    {
        if (string.IsNullOrWhiteSpace(commaSeparatedTickers))
            return 0;

        var watchlist = Load();
        int added = 0;

        // Split by comma, semicolon, or whitespace
        var symbols = commaSeparatedTickers
            .Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrEmpty(s) && s.All(c => char.IsLetterOrDigit(c)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            var existing = watchlist.Tickers.FirstOrDefault(t => 
                t.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // Already exists - just enable it if it has allocation
                if (existing.Allocation > 0) existing.Enabled = true;
            }
            else
            {
                // Add new ticker with Allocation=0 (disabled, must manually configure)
                watchlist.Tickers.Add(new WatchlistEntry
                {
                    Symbol = symbol,
                    Allocation = 0,  // Must be manually set
                    Enabled = false  // Must be manually enabled
                });
                added++;
            }
        }

        if (added > 0)
        {
            Save(watchlist);
            ConsoleLog.Write("Watchlist", $"Added {added} tickers (auto quantity by tier)");
        }

        return added;
    }

    /// <summary>
    /// Clears all tickers from the watchlist.
    /// </summary>
    public static void Clear()
    {
        var watchlist = Load();
        watchlist.Tickers.Clear();
        Save(watchlist);
        ConsoleLog.Write("Watchlist", "Cleared all tickers");
    }
}
