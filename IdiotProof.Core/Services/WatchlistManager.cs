// ============================================================================
// Watchlist Manager - Loads Ticker List for Autonomous Trading
// ============================================================================
//
// PURPOSE:
// Simple JSON file to configure which tickers to trade and how much.
// Edit the file outside the app, restart, and it trades automatically.
//
// FILE LOCATION:
//   {SolutionRoot}\IdiotProof.Core\Scripts\watchlist.json
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

using IdiotProof.Core.Enums;
using IdiotProof.Core.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Backend.Services;

/// <summary>
/// A single ticker entry in the watchlist.
/// </summary>
public sealed class WatchlistEntry
{
    /// <summary>Ticker symbol (e.g., "NVDA", "AAPL").</summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    /// <summary>Number of shares to trade.</summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;

    /// <summary>Optional: Override the default session for this ticker.</summary>
    [JsonPropertyName("session")]
    public string? Session { get; set; }

    /// <summary>Whether this ticker is enabled (default: true).</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Optional: Custom name for this ticker's strategy.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

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
        return Path.Combine(SettingsManager.GetStrategiesFolder(), WatchlistFileName);
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
            Console.WriteLine($"[Watchlist] No watchlist found at: {path}");
            Console.WriteLine($"[Watchlist] Create one to enable autonomous trading.");
            return new Watchlist();
        }

        try
        {
            var json = File.ReadAllText(path);
            var watchlist = JsonSerializer.Deserialize<Watchlist>(json, JsonOptions);

            if (watchlist == null)
            {
                Console.WriteLine($"[Watchlist] Failed to parse watchlist, using empty list");
                return new Watchlist();
            }

            Console.WriteLine($"[Watchlist] Loaded {watchlist.EnabledCount}/{watchlist.TotalCount} tickers from {WatchlistFileName}");
            foreach (var ticker in watchlist.EnabledTickers)
            {
                Console.WriteLine($"  - {ticker.Symbol} x{ticker.Quantity}");
            }

            return watchlist;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchlist] Error loading watchlist: {ex.Message}");
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

            Console.WriteLine($"[Watchlist] Saved {watchlist.TotalCount} tickers to {WatchlistFileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchlist] Error saving watchlist: {ex.Message}");
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
                new() { Symbol = "NVDA", Quantity = 5, Name = "NVIDIA" },
                new() { Symbol = "AAPL", Quantity = 10, Name = "Apple" },
                new() { Symbol = "TSLA", Quantity = 3, Name = "Tesla", Enabled = false },
                new() { Symbol = "SPY", Quantity = 20, Name = "S&P 500 ETF" }
            ]
        };

        Save(sample);
        Console.WriteLine($"[Watchlist] Created sample watchlist at: {GetWatchlistPath()}");
        Console.WriteLine($"[Watchlist] Edit this file to configure your tickers and quantities.");
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
            Console.WriteLine("[Watchlist] Autonomous trading is disabled globally");
            yield break;
        }

        foreach (var ticker in watchlist.EnabledTickers)
        {
            var session = ticker.Session ?? watchlist.Session;
            var name = ticker.Name ?? $"{ticker.Symbol} Auto";

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
        var watchlist = Load();

        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              AUTONOMOUS TRADING WATCHLIST                  ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Status: {(watchlist.Enabled ? "ENABLED" : "DISABLED"),-48} ║");
        Console.WriteLine($"║  Session: {watchlist.Session,-47} ║");
        Console.WriteLine($"║  Tickers: {watchlist.EnabledCount} active, {watchlist.TotalCount - watchlist.EnabledCount} disabled              ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Symbol    Qty     Session       Status                   ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");

        foreach (var ticker in watchlist.Tickers)
        {
            var session = ticker.Session ?? watchlist.Session;
            var status = ticker.Enabled ? "ACTIVE" : "disabled";
            Console.WriteLine($"║  {ticker.Symbol,-8}  {ticker.Quantity,4}    {session,-12}  {status,-10}           ║");
        }

        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}
