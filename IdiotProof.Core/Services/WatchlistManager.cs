// ============================================================================
// Watchlist Manager - Loads Ticker List for Autonomous Trading
// ============================================================================
//
// PURPOSE:
// Simple JSON file to configure which tickers to trade and how much.
// Edit the file outside the app, restart, and it trades automatically.
//
// FILE LOCATION:
//   {SolutionRoot}\IdiotProof.Core\Data\watchlist.json
//
// FILE FORMAT:
// {
//   "tickers": [
//     { "symbol": "NVDA", "quantity": 5 },
//     { "symbol": "AAPL", "quantity": 10 },
//     { "symbol": "TSLA", "quantity": 3 }
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

    /// <summary>Number of shares to trade (0 = auto-calculate based on price).</summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 0;

    /// <summary>Whether this ticker is enabled (default: true).</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    public override string ToString() => $"{Symbol} x{Quantity}" + (Enabled ? "" : " [DISABLED]");
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

    /// <summary>Gets only enabled tickers.</summary>
    [JsonIgnore]
    public IEnumerable<WatchlistEntry> EnabledTickers => Tickers.Where(t => t.Enabled && !string.IsNullOrEmpty(t.Symbol));

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
            foreach (var ticker in watchlist.EnabledTickers)
            {
                ConsoleLog.Write("Watchlist", $"  - {ticker.Symbol} x{ticker.Quantity}");
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
    /// </summary>
    public static void CreateSampleIfNotExists()
    {
        if (Exists())
            return;

        var sample = new Watchlist
        {
            Description = "Autonomous Trading Watchlist - Edit this file to add/remove tickers",
            Session = "RTH",
            Enabled = true,
            Tickers =
            [
                new() { Symbol = "NVDA", Quantity = 5 },
                new() { Symbol = "AAPL", Quantity = 10 },
                new() { Symbol = "TSLA", Quantity = 3, Enabled = false },
                new() { Symbol = "SPY", Quantity = 20 }
            ]
        };

        Save(sample);
        ConsoleLog.Write("Watchlist", $"Created sample watchlist at: {GetWatchlistPath()}");
        ConsoleLog.Write("Watchlist", "Edit this file to configure your tickers and quantities.");
    }

    /// <summary>
    /// Adds a ticker to the watchlist (or updates quantity if exists).
    /// </summary>
    public static void AddOrUpdate(string symbol, int quantity)
    {
        var watchlist = Load();
        var existing = watchlist.Tickers.FirstOrDefault(t => 
            t.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Quantity = quantity;
            existing.Enabled = true;
        }
        else
        {
            watchlist.Tickers.Add(new WatchlistEntry
            {
                Symbol = symbol.ToUpperInvariant(),
                Quantity = quantity,
                Enabled = true
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

            // Generate IdiotScript for this ticker
            var script = $"Ticker({ticker.Symbol})" +
                        $".Name(\"{name}\")" +
                        $".Session(IS.{session.ToUpperInvariant()})" +
                        $".Quantity({ticker.Quantity})" +
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
        Console.WriteLine($"    {"#",2}  {"Symbol",-8}  {"Qty",5}  {"Price",9}  {"Status",-10}  {"Description"}");
        Console.WriteLine($"  {"---",3}  {"------",-8}  {"---",5}  {"-----",9}  {"------",-10}  {"-----------"}");

        int rowNum = 0;
        foreach (var ticker in watchlist.Tickers.OrderBy(t => t.Symbol))
        {
            rowNum++;
            var status = ticker.Enabled ? "[ACTIVE]" : "[OFF]";
            var qtyStr = ticker.Quantity > 0 ? $"{ticker.Quantity,5}" : " auto";

            // Get price from cache or provider
            var cached = TickerDataCache.Get(ticker.Symbol);
            var price = cached?.Price ?? priceProvider?.Invoke(ticker.Symbol) ?? 0;
            var priceStr = price > 0 ? $"${price,7:F2}" : $"{"--",9}";

            // Get description
            var desc = StockDescriptionService.GetDescription(ticker.Symbol);

            Console.WriteLine($"    {rowNum,2}  {ticker.Symbol,-8}  {qtyStr}  {priceStr}  {status,-10}  {desc}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Total: {watchlist.Tickers.Count} ticker(s)");
        Console.WriteLine();
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
                // Already exists - just enable it
                existing.Enabled = true;
            }
            else
            {
                // Add new ticker with Quantity=0 (auto tier-based allocation)
                watchlist.Tickers.Add(new WatchlistEntry
                {
                    Symbol = symbol,
                    Quantity = 0,  // Auto-calculate by price tier
                    Enabled = true
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
