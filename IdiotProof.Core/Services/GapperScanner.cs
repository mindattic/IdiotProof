// ============================================================================
// GapperScanner - Simple Premarket Gap Detection
// ============================================================================
//
// PURPOSE:
// Monitor watchlist for stocks gapping up/down in premarket.
// Alerts the user with confidence scores - USER makes the final call.
//
// WHAT IT TRACKS:
//   - Gap % from previous close
//   - Premarket volume vs average
//   - Price momentum (is it fading or building?)
//   - Time until RTH open
//
// OUTPUT FORMAT:
// ╔════════════════════════════════════════════════════════════════════════╗
// ║  GAPPER ALERT: NVDA  +8.2%  $142.50  Vol: 3.2x  Confidence: 85%       ║
// ║  Building momentum | 45 min to open | Action: YOUR CALL               ║
// ╚════════════════════════════════════════════════════════════════════════╝
//
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IdiotProof.Core.Models;

namespace IdiotProof.Services;

/// <summary>
/// Represents a stock being scanned for gap opportunities.
/// </summary>
public sealed class GapperInfo
{
    public string Symbol { get; init; } = "";
    public double PreviousClose { get; set; }
    public double CurrentPrice { get; set; }
    public double PremarketHigh { get; set; }
    public double PremarketLow { get; set; } = double.MaxValue;
    public long PremarketVolume { get; set; }
    public double AverageVolume { get; set; } = 1_000_000; // Default 1M
    public DateTime LastUpdate { get; set; }
    public List<double> RecentPrices { get; } = new(20); // Last 20 price updates
    
    // Calculated properties
    public double GapPercent => PreviousClose > 0 
        ? ((CurrentPrice - PreviousClose) / PreviousClose) * 100 
        : 0;
    
    public double VolumeRatio => AverageVolume > 0 
        ? PremarketVolume / AverageVolume 
        : 0;
    
    public bool IsGapUp => GapPercent > 0;
    public bool IsGapDown => GapPercent < 0;
    
    /// <summary>
    /// Momentum: Are prices building or fading?
    /// +1 = strong building, 0 = flat, -1 = fading
    /// </summary>
    public double Momentum
    {
        get
        {
            if (RecentPrices.Count < 5) return 0;
            
            // Compare last 5 vs first 5 of recent prices
            var recent5 = RecentPrices.TakeLast(5).Average();
            var earlier5 = RecentPrices.Take(5).Average();
            
            if (earlier5 == 0) return 0;
            var change = (recent5 - earlier5) / earlier5 * 100;
            
            // Normalize to -1 to +1 range (2% = full momentum)
            return Math.Clamp(change / 2.0, -1.0, 1.0);
        }
    }
    
    /// <summary>
    /// How far current price is from premarket high (0 = at high, 1 = at low).
    /// Lower is better for gap-up plays.
    /// </summary>
    public double DistanceFromHigh
    {
        get
        {
            if (PremarketHigh == PremarketLow || PremarketLow == double.MaxValue) return 0;
            return (PremarketHigh - CurrentPrice) / (PremarketHigh - PremarketLow);
        }
    }
    
    public void AddPrice(double price)
    {
        CurrentPrice = price;
        LastUpdate = DateTime.Now;
        
        if (price > PremarketHigh) PremarketHigh = price;
        if (price < PremarketLow) PremarketLow = price;
        
        RecentPrices.Add(price);
        if (RecentPrices.Count > 20)
            RecentPrices.RemoveAt(0);
    }
}

/// <summary>
/// Confidence breakdown for a gapper.
/// </summary>
public sealed class GapperConfidence
{
    public int Total { get; init; }
    public int GapScore { get; init; }        // 0-30: Size of gap
    public int VolumeScore { get; init; }     // 0-30: Volume vs average  
    public int MomentumScore { get; init; }   // 0-25: Building or fading
    public int HoldingScore { get; init; }    // 0-15: Near high vs fading
    
    public string Grade => Total switch
    {
        >= 80 => "A",
        >= 65 => "B", 
        >= 50 => "C",
        >= 35 => "D",
        _ => "F"
    };
    
    public ConsoleColor Color => Total switch
    {
        >= 80 => ConsoleColor.Green,
        >= 65 => ConsoleColor.Cyan,
        >= 50 => ConsoleColor.Yellow,
        >= 35 => ConsoleColor.DarkYellow,
        _ => ConsoleColor.Gray
    };
}

/// <summary>
/// Simple scanner for premarket gap opportunities.
/// </summary>
public sealed class GapperScanner
{
    private readonly ConcurrentDictionary<string, GapperInfo> gappers = new();
    private readonly HashSet<string> alertedSymbols = new();
    private readonly object alertLock = new();
    
    // Configuration
    public double MinGapPercent { get; set; } = 3.0;       // Minimum gap % to alert
    public double MinVolumeRatio { get; set; } = 1.5;      // Minimum volume vs average
    public int MinConfidence { get; set; } = 50;           // Minimum confidence to alert
    public bool AlertOnFade { get; set; } = true;          // Alert when gap is fading
    
    /// <summary>
    /// Event fired when a gapper meets alert criteria.
    /// </summary>
    public event Action<GapperInfo, GapperConfidence>? OnGapperAlert;
    
    /// <summary>
    /// Registers a symbol to scan with its previous close.
    /// </summary>
    public void RegisterSymbol(string symbol, double previousClose, double avgVolume = 1_000_000)
    {
        gappers[symbol] = new GapperInfo
        {
            Symbol = symbol,
            PreviousClose = previousClose,
            AverageVolume = avgVolume
        };
    }
    
    /// <summary>
    /// Updates price for a symbol and checks for alerts.
    /// </summary>
    public void OnPriceUpdate(string symbol, double price, long volume = 0)
    {
        if (!gappers.TryGetValue(symbol, out var gapper))
            return;
        
        gapper.AddPrice(price);
        if (volume > 0)
            gapper.PremarketVolume = volume;
        
        // Check if we should alert
        var confidence = CalculateConfidence(gapper);
        
        if (ShouldAlert(gapper, confidence))
        {
            lock (alertLock)
            {
                // Don't spam alerts for the same symbol
                var alertKey = $"{symbol}_{confidence.Grade}";
                if (!alertedSymbols.Contains(alertKey))
                {
                    alertedSymbols.Add(alertKey);
                    OnGapperAlert?.Invoke(gapper, confidence);
                }
            }
        }
    }
    
    /// <summary>
    /// Calculates confidence score for a gapper.
    /// </summary>
    public GapperConfidence CalculateConfidence(GapperInfo gapper)
    {
        // Gap Score (0-30): Based on gap size
        // 3% = 10, 5% = 15, 8% = 22, 10%+ = 30
        int gapScore = Math.Abs(gapper.GapPercent) switch
        {
            >= 10 => 30,
            >= 8 => 25,
            >= 5 => 20,
            >= 3 => 15,
            >= 2 => 10,
            _ => 5
        };
        
        // Volume Score (0-30): Based on volume ratio
        // 1.5x = 10, 2x = 15, 3x = 22, 5x+ = 30
        int volumeScore = gapper.VolumeRatio switch
        {
            >= 5 => 30,
            >= 3 => 25,
            >= 2 => 20,
            >= 1.5 => 15,
            >= 1 => 10,
            _ => 5
        };
        
        // Momentum Score (0-25): Is it building or fading?
        // For gap-up: positive momentum is good
        // For gap-down: negative momentum is good (for short plays)
        double momentumFactor = gapper.IsGapUp ? gapper.Momentum : -gapper.Momentum;
        int momentumScore = momentumFactor switch
        {
            >= 0.5 => 25,  // Strong building
            >= 0.2 => 20,  // Building
            >= 0 => 15,    // Flat/stable
            >= -0.3 => 10, // Slight fade
            _ => 5         // Fading
        };
        
        // Holding Score (0-15): How well is it holding near high?
        // 0 = at premarket high, 1 = at premarket low
        int holdingScore = gapper.DistanceFromHigh switch
        {
            <= 0.1 => 15,  // At or near high
            <= 0.25 => 12, // Close to high
            <= 0.5 => 8,   // Middle of range
            <= 0.75 => 5,  // Closer to low
            _ => 2         // At low
        };
        
        int total = gapScore + volumeScore + momentumScore + holdingScore;
        
        return new GapperConfidence
        {
            Total = Math.Min(100, total),
            GapScore = gapScore,
            VolumeScore = volumeScore,
            MomentumScore = momentumScore,
            HoldingScore = holdingScore
        };
    }
    
    /// <summary>
    /// Checks if we should fire an alert for this gapper.
    /// </summary>
    private bool ShouldAlert(GapperInfo gapper, GapperConfidence confidence)
    {
        // Must meet minimum gap threshold
        if (Math.Abs(gapper.GapPercent) < MinGapPercent)
            return false;
        
        // Must meet minimum volume
        if (gapper.VolumeRatio < MinVolumeRatio)
            return false;
        
        // Must meet minimum confidence
        if (confidence.Total < MinConfidence)
            return false;
        
        return true;
    }
    
    /// <summary>
    /// Gets all current gappers sorted by confidence.
    /// </summary>
    public IEnumerable<(GapperInfo Gapper, GapperConfidence Confidence)> GetAllGappers()
    {
        return gappers.Values
            .Select(g => (Gapper: g, Confidence: CalculateConfidence(g)))
            .OrderByDescending(x => x.Confidence.Total);
    }
    
    /// <summary>
    /// Resets alerts so symbols can alert again.
    /// Call this at RTH open or when refreshing scan.
    /// </summary>
    public void ResetAlerts()
    {
        lock (alertLock)
        {
            alertedSymbols.Clear();
        }
    }
    
    /// <summary>
    /// Clears all tracked gappers.
    /// </summary>
    public void Clear()
    {
        gappers.Clear();
        ResetAlerts();
    }
    
    /// <summary>
    /// Prints a formatted scan summary to console.
    /// </summary>
    public void PrintScanSummary()
    {
        var gappers = GetAllGappers().ToList();
        
        if (gappers.Count == 0)
        {
            Console.WriteLine("No gappers being tracked.");
            return;
        }
        
        // Calculate time until RTH (assumes Eastern Time - adjust as needed)
        var now = DateTime.Now;
        var rthOpen = now.Date.AddHours(9).AddMinutes(30);
        if (now > rthOpen) rthOpen = rthOpen.AddDays(1);
        var timeToOpen = rthOpen - now;
        
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  GAPPER SCAN  |  {gappers.Count} stocks  |  {(int)timeToOpen.TotalMinutes} min to RTH                          ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();
        
        foreach (var (gapper, confidence) in gappers)
        {
            var arrow = gapper.IsGapUp ? "↑" : "↓";
            var gapSign = gapper.IsGapUp ? "+" : "";
            var momentum = gapper.Momentum switch
            {
                >= 0.3 => "BUILDING",
                >= 0 => "Holding",
                >= -0.3 => "Slight fade",
                _ => "FADING"
            };
            
            Console.ForegroundColor = confidence.Color;
            Console.Write($"║  {gapper.Symbol,-6} ");
            
            Console.ForegroundColor = gapper.IsGapUp ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write($"{arrow}{gapSign}{gapper.GapPercent:F1}%  ");
            
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"${gapper.CurrentPrice:F2}  ");
            
            Console.ForegroundColor = gapper.VolumeRatio >= 2 ? ConsoleColor.Green : ConsoleColor.Gray;
            Console.Write($"Vol:{gapper.VolumeRatio:F1}x  ");
            
            Console.ForegroundColor = confidence.Color;
            Console.Write($"Conf:{confidence.Total}% ({confidence.Grade})  ");
            
            Console.ForegroundColor = gapper.Momentum >= 0 ? ConsoleColor.Cyan : ConsoleColor.Yellow;
            Console.Write($"{momentum,-12}");
            
            Console.ResetColor();
            Console.WriteLine("║");
        }
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }
    
    /// <summary>
    /// Prints a single gapper alert box.
    /// </summary>
    public static void PrintAlert(GapperInfo gapper, GapperConfidence confidence)
    {
        var arrow = gapper.IsGapUp ? "↑" : "↓";
        var gapSign = gapper.IsGapUp ? "+" : "";
        var momentum = gapper.Momentum switch
        {
            >= 0.3 => "Building momentum",
            >= 0 => "Holding steady",
            >= -0.3 => "Slight fade",
            _ => "FADING - caution"
        };
        
        var now = DateTime.Now;
        var rthOpen = now.Date.AddHours(9).AddMinutes(30);
        if (now > rthOpen) rthOpen = rthOpen.AddDays(1);
        var minToOpen = (int)(rthOpen - now).TotalMinutes;
        
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.Write($"║  GAPPER ALERT: {gapper.Symbol,-6} ");
        
        Console.ForegroundColor = gapper.IsGapUp ? ConsoleColor.Green : ConsoleColor.Red;
        Console.Write($"{arrow}{gapSign}{gapper.GapPercent:F1}%  ");
        
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"${gapper.CurrentPrice:F2}  ");
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"Vol:{gapper.VolumeRatio:F1}x  ");
        
        Console.ForegroundColor = confidence.Color;
        Console.WriteLine($"Conf:{confidence.Total}%     ║");
        
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"║  {momentum,-20} | {minToOpen} min to open | Action: YOUR CALL            ║");
        
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"║  Gap:{confidence.GapScore}/30  Vol:{confidence.VolumeScore}/30  Mom:{confidence.MomentumScore}/25  Hold:{confidence.HoldingScore}/15          ║");
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    /// <summary>
    /// Gets a gapper by symbol.
    /// </summary>
    public GapperInfo? GetGapper(string symbol)
    {
        return gappers.TryGetValue(symbol, out var gapper) ? gapper : null;
    }
}

// ============================================================================
// QuickTrade - One-Button Entry with Auto-Calculated Levels
// ============================================================================

/// <summary>
/// Calculated trade levels for quick execution.
/// </summary>
public sealed class QuickTradeLevels
{
    public string Symbol { get; init; } = "";
    public bool IsLong { get; init; }
    public double EntryPrice { get; init; }
    public double StopLoss { get; init; }
    public double TakeProfit { get; init; }
    public double TrailingStopPercent { get; init; }
    public int Quantity { get; init; }
    public double RiskAmount { get; init; }
    public double RewardAmount { get; init; }
    public double RiskRewardRatio { get; init; }

    /// <summary>
    /// Whether this trade should be taken (meets R:R requirements).
    /// </summary>
    public bool IsViable => RiskRewardRatio >= 1.5;

    /// <summary>
    /// Calculates distance from entry to stop as a percentage.
    /// </summary>
    public double StopDistancePercent => EntryPrice > 0 
        ? Math.Abs(EntryPrice - StopLoss) / EntryPrice * 100 
        : 0;

    public void Print()
    {
        var direction = IsLong ? "LONG" : "SHORT";
        var dirColor = IsLong ? ConsoleColor.Green : ConsoleColor.Red;

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.Write($"║  QUICK TRADE: {Symbol,-6} ");
        Console.ForegroundColor = dirColor;
        Console.Write($"{direction,-6} ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"                                              ║");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();

        Console.Write("║  ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"Entry:     ${EntryPrice:F2}");
        Console.ResetColor();
        Console.WriteLine($"                                                    ║");

        Console.Write("║  ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write($"Stop Loss: ${StopLoss:F2} (-{StopDistancePercent:F1}%)");
        Console.ResetColor();
        Console.WriteLine($"                                            ║");

        Console.Write("║  ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"Take Profit: ${TakeProfit:F2}");
        Console.ResetColor();
        Console.WriteLine($"                                                ║");

        Console.Write("║  ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"Trailing Stop: {TrailingStopPercent:F1}%");
        Console.ResetColor();
        Console.WriteLine($"                                                ║");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();

        Console.Write("║  ");
        Console.Write($"Quantity: {Quantity} shares");
        Console.WriteLine($"                                                    ║");

        Console.Write("║  ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write($"Risk: ${RiskAmount:F2}");
        Console.ResetColor();
        Console.Write($"  |  ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"Reward: ${RewardAmount:F2}");
        Console.ResetColor();
        Console.Write($"  |  ");
        Console.ForegroundColor = RiskRewardRatio >= 2 ? ConsoleColor.Green : 
                                  RiskRewardRatio >= 1.5 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.Write($"R:R = {RiskRewardRatio:F1}");
        Console.ResetColor();
        Console.WriteLine($"              ║");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");

        if (IsViable)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("║  [ENTER] Execute Trade  |  [ESC] Cancel                                  ║");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("║  R:R too low - NOT RECOMMENDED  |  [ESC] Cancel                          ║");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }
}

/// <summary>
/// Calculates quick trade levels based on gapper data.
/// </summary>
public static class QuickTradeCalculator
{
    /// <summary>
    /// Default risk per trade in dollars.
    /// </summary>
    public static double DefaultRiskDollars { get; set; } = 50.0;

    /// <summary>
    /// Default trailing stop percentage.
    /// </summary>
    public static double DefaultTrailingStopPercent { get; set; } = 1.5;

    /// <summary>
    /// Calculates optimal trade levels for a gap-up LONG play.
    /// </summary>
    public static QuickTradeLevels CalculateLong(GapperInfo gapper, double riskDollars = 0)
    {
        if (riskDollars <= 0) riskDollars = DefaultRiskDollars;

        var entry = gapper.CurrentPrice;

        // Stop Loss: Below premarket low OR 2% below entry (whichever is tighter)
        var stopFromLow = gapper.PremarketLow > 0 && gapper.PremarketLow < double.MaxValue
            ? gapper.PremarketLow * 0.995  // Just below premarket low
            : entry * 0.98;
        var stopFromPercent = entry * 0.98;  // 2% below entry
        var stopLoss = Math.Max(stopFromLow, stopFromPercent);  // Use tighter stop

        // Take Profit: 2x the risk distance OR premarket high + gap continuation
        var riskDistance = entry - stopLoss;
        var tpFromRisk = entry + (riskDistance * 2.5);  // 2.5:1 R:R target
        var tpFromHigh = gapper.PremarketHigh * 1.02;   // 2% above premarket high
        var takeProfit = Math.Min(tpFromRisk, tpFromHigh);  // Use more conservative

        // Trailing stop: Based on volatility (gap size)
        var trailingPercent = Math.Abs(gapper.GapPercent) switch
        {
            >= 10 => 2.0,   // High volatility = wider trail
            >= 5 => 1.5,    // Medium volatility
            _ => 1.0        // Lower volatility = tighter trail
        };

        // Calculate quantity based on risk
        var riskPerShare = entry - stopLoss;
        var quantity = riskPerShare > 0 ? (int)Math.Floor(riskDollars / riskPerShare) : 1;
        quantity = Math.Max(1, quantity);  // At least 1 share

        var actualRisk = quantity * riskPerShare;
        var actualReward = quantity * (takeProfit - entry);
        var rr = actualRisk > 0 ? actualReward / actualRisk : 0;

        return new QuickTradeLevels
        {
            Symbol = gapper.Symbol,
            IsLong = true,
            EntryPrice = Math.Round(entry, 2),
            StopLoss = Math.Round(stopLoss, 2),
            TakeProfit = Math.Round(takeProfit, 2),
            TrailingStopPercent = trailingPercent,
            Quantity = quantity,
            RiskAmount = Math.Round(actualRisk, 2),
            RewardAmount = Math.Round(actualReward, 2),
            RiskRewardRatio = Math.Round(rr, 2)
        };
    }

    /// <summary>
    /// Calculates optimal trade levels for a gap-down SHORT play.
    /// </summary>
    public static QuickTradeLevels CalculateShort(GapperInfo gapper, double riskDollars = 0)
    {
        if (riskDollars <= 0) riskDollars = DefaultRiskDollars;

        var entry = gapper.CurrentPrice;

        // Stop Loss: Above premarket high OR 2% above entry (whichever is tighter)
        var stopFromHigh = gapper.PremarketHigh > 0
            ? gapper.PremarketHigh * 1.005  // Just above premarket high
            : entry * 1.02;
        var stopFromPercent = entry * 1.02;  // 2% above entry
        var stopLoss = Math.Min(stopFromHigh, stopFromPercent);  // Use tighter stop

        // Take Profit: 2x the risk distance OR premarket low - continuation
        var riskDistance = stopLoss - entry;
        var tpFromRisk = entry - (riskDistance * 2.5);  // 2.5:1 R:R target
        var tpFromLow = gapper.PremarketLow < double.MaxValue
            ? gapper.PremarketLow * 0.98   // 2% below premarket low
            : entry * 0.95;
        var takeProfit = Math.Max(tpFromRisk, tpFromLow);  // Use more conservative

        // Trailing stop: Based on volatility
        var trailingPercent = Math.Abs(gapper.GapPercent) switch
        {
            >= 10 => 2.0,
            >= 5 => 1.5,
            _ => 1.0
        };

        // Calculate quantity based on risk
        var riskPerShare = stopLoss - entry;
        var quantity = riskPerShare > 0 ? (int)Math.Floor(riskDollars / riskPerShare) : 1;
        quantity = Math.Max(1, quantity);

        var actualRisk = quantity * riskPerShare;
        var actualReward = quantity * (entry - takeProfit);
        var rr = actualRisk > 0 ? actualReward / actualRisk : 0;

        return new QuickTradeLevels
        {
            Symbol = gapper.Symbol,
            IsLong = false,
            EntryPrice = Math.Round(entry, 2),
            StopLoss = Math.Round(stopLoss, 2),
            TakeProfit = Math.Round(takeProfit, 2),
            TrailingStopPercent = trailingPercent,
            Quantity = quantity,
            RiskAmount = Math.Round(actualRisk, 2),
            RewardAmount = Math.Round(actualReward, 2),
            RiskRewardRatio = Math.Round(rr, 2)
        };
    }

    /// <summary>
    /// Calculates BOTH long and short levels for dual-account hedging.
    /// </summary>
    public static (QuickTradeLevels Long, QuickTradeLevels Short) CalculateHedge(GapperInfo gapper, double riskDollars = 0)
    {
        return (CalculateLong(gapper, riskDollars), CalculateShort(gapper, riskDollars));
    }

    /// <summary>
    /// Determines if the gapper is too choppy for hedging.
    /// Choppy = price oscillating without clear direction.
    /// </summary>
    public static bool IsToChoppy(GapperInfo gapper)
    {
        if (gapper.RecentPrices.Count < 10) return true;  // Not enough data

        // Check for direction changes
        var directionChanges = 0;
        var prevDirection = 0;
        for (int i = 1; i < gapper.RecentPrices.Count; i++)
        {
            var diff = gapper.RecentPrices[i] - gapper.RecentPrices[i - 1];
            var direction = diff > 0 ? 1 : (diff < 0 ? -1 : 0);
            if (direction != 0 && direction != prevDirection)
            {
                directionChanges++;
                prevDirection = direction;
            }
        }

        // If price changed direction more than 60% of the time, it's choppy
        var choppinessRatio = (double)directionChanges / gapper.RecentPrices.Count;
        return choppinessRatio > 0.6;
    }

    /// <summary>
    /// Prints hedge analysis for dual-account trading.
    /// </summary>
    public static void PrintHedgeAnalysis(GapperInfo gapper, double riskDollars = 0)
    {
        var (longTrade, shortTrade) = CalculateHedge(gapper, riskDollars);
        var isChoppy = IsToChoppy(gapper);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  DUAL-ACCOUNT HEDGE ANALYSIS: {gapper.Symbol,-6}                                  ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();

        if (isChoppy)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("║  ⚠ TOO CHOPPY - Price oscillating without clear direction                ║");
            Console.WriteLine("║  RECOMMENDATION: SKIP this trade                                         ║");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            return;
        }

        // Primary account (LONG)
        Console.Write("║  ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("PRIMARY (LONG): ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"Entry ${longTrade.EntryPrice:F2} | SL ${longTrade.StopLoss:F2} | TP ${longTrade.TakeProfit:F2}");
        Console.ResetColor();
        Console.WriteLine("   ║");

        Console.Write("║                ");
        Console.Write($"Qty: {longTrade.Quantity} | Risk: ${longTrade.RiskAmount:F2} | R:R: {longTrade.RiskRewardRatio:F1}");
        Console.WriteLine("              ║");

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╠──────────────────────────────────────────────────────────────────────────╣");
        Console.ResetColor();

        // Secondary account (SHORT)
        Console.Write("║  ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("SECONDARY (SHORT): ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"Entry ${shortTrade.EntryPrice:F2} | SL ${shortTrade.StopLoss:F2} | TP ${shortTrade.TakeProfit:F2}");
        Console.ResetColor();
        Console.WriteLine(" ║");

        Console.Write("║                   ");
        Console.Write($"Qty: {shortTrade.Quantity} | Risk: ${shortTrade.RiskAmount:F2} | R:R: {shortTrade.RiskRewardRatio:F1}");
        Console.WriteLine("            ║");

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();

        // Summary
        var totalRisk = longTrade.RiskAmount + shortTrade.RiskAmount;
        var minReward = Math.Min(longTrade.RewardAmount, shortTrade.RewardAmount);

        Console.Write("║  ");
        Console.Write($"Total Risk: ${totalRisk:F2} | Min Reward (either direction): ${minReward:F2}");
        Console.WriteLine("        ║");

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");

        if (longTrade.IsViable && shortTrade.IsViable)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("║  [H] Execute HEDGE (both)  |  [L] LONG only  |  [S] SHORT only  |  [ESC]  ║");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("║  R:R marginal - consider single direction  |  [L] [S] [ESC]              ║");
        }

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }
}
