// ============================================================================
// PremarketSetupScanner - Automatically identifies "No Break No Trade" setups
// ============================================================================
//
// Scans for breakout-pullback candidates based on the pro trader methodology:
// 1. Find stocks gapping or with momentum
// 2. Identify key resistance levels (trigger points)
// 3. Calculate support levels for confirmation
// 4. Generate trade setups with targets and invalidation
//
// This replicates the manual process of scanning charts and writing up setups
// like: "ERNA - Break over $0.52, confirmation: pullback + VWAP hold + $0.48"
// ============================================================================

using IdiotProof.Shared;

namespace IdiotProof.Scripting;

/// <summary>
/// Input data for the scanner (decoupled from Web's GapperCandidate).
/// </summary>
public sealed class ScannerInput
{
    public string Symbol { get; set; } = "";
    public string? CompanyName { get; set; }
    public double PremarketPrice { get; set; }
    public double PreviousClose { get; set; }
    public double GapPercent => PreviousClose > 0 
        ? (PremarketPrice - PreviousClose) / PreviousClose * 100 
        : 0;
    public long PremarketVolume { get; set; }
    public double AverageVolume { get; set; }
    public double VolumeRatio => AverageVolume > 0 
        ? PremarketVolume / AverageVolume 
        : 0;
    public string? Catalyst { get; set; }
    public int SourceCount { get; set; } = 1;
}

/// <summary>
/// Result of scanning for breakout-pullback setups.
/// </summary>
public sealed class SetupScanResult
{
    public List<BreakoutSetup> Setups { get; set; } = [];
    public DateTime ScanTime { get; set; } = DateTime.UtcNow;
    public int TotalScanned { get; set; }
    public int QualifiedCount => Setups.Count;
    public string SessionType { get; set; } = "Premarket";
}

/// <summary>
/// A complete breakout-pullback setup ready for monitoring.
/// </summary>
public sealed class BreakoutSetup
{
    public string Symbol { get; set; } = "";
    public string? CompanyName { get; set; }
    
    // Classification
    public string Bias { get; set; } = "";          // "Bullish continuation", "AH momentum", etc.
    public string Pattern { get; set; } = "";        // "Breakout pullback", "Wedge breakout", etc.
    public int ConfidenceScore { get; set; }         // 0-100
    
    // Key levels
    public double TriggerPrice { get; set; }         // Break above this = triggered
    public double SupportPrice { get; set; }         // Must hold this after breakout
    public double VwapPrice { get; set; }            // Current VWAP (dynamic support)
    public double InvalidationPrice { get; set; }    // Below this = pattern failed
    
    // Targets
    public List<TargetLevel> Targets { get; set; } = [];
    
    // Current state
    public SetupState State { get; set; } = SetupState.Watching;
    public double CurrentPrice { get; set; }
    public double GapPercent { get; set; }
    public double VolumeRatio { get; set; }
    
    // Timing
    public DateTime DiscoveredUtc { get; set; } = DateTime.UtcNow;
    public DateTime? TriggeredUtc { get; set; }
    public DateTime? ConfirmedUtc { get; set; }
    public DateTime? EnteredUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    // Trade tracking (populated when entered/completed)
    public double ActualEntryPrice { get; set; }
    public double ActualExitPrice { get; set; }
    public string? CompletionReason { get; set; }
    
    // Risk calculations
    public double RiskPercent => TriggerPrice > 0 && InvalidationPrice > 0
        ? (TriggerPrice - InvalidationPrice) / TriggerPrice * 100
        : 0;
    
    public double RewardPercent => Targets.Count > 0 && TriggerPrice > 0
        ? (Targets[0].Price - TriggerPrice) / TriggerPrice * 100
        : 0;
    
    public double RiskRewardRatio => RiskPercent > 0 ? RewardPercent / RiskPercent : 0;
    
    /// <summary>
    /// Generates the strategy card in pro trader format.
    /// </summary>
    public string ToStrategyCard()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"  {Symbol}");
        sb.AppendLine($"  Bias: {Bias}");
        sb.AppendLine($"  Trigger: Break over ${TriggerPrice:F2}");
        sb.AppendLine("  Confirmation:");
        sb.AppendLine("    - Pullback after breakout");
        sb.AppendLine("    - Holds / reclaims VWAP");
        if (SupportPrice > 0 && Math.Abs(SupportPrice - VwapPrice) > 0.01)
            sb.AppendLine($"    - Holds above ${SupportPrice:F2}");
        sb.AppendLine("  Entry:");
        sb.AppendLine("    On confirmed VWAP hold after breakout");
        sb.AppendLine("  Targets:");
        foreach (var t in Targets)
        {
            var pct = (t.Price - TriggerPrice) / TriggerPrice * 100;
            sb.AppendLine($"    {t.Label}: ${t.Price:F2} ({pct:+0.0}%)");
        }
        sb.AppendLine("  Invalidation:");
        sb.AppendLine($"    - No break over ${TriggerPrice:F2}");
        sb.AppendLine($"    - Failed VWAP hold after breakout");
        if (SupportPrice > 0)
            sb.AppendLine($"    - Loss of ${SupportPrice:F2}");
        sb.AppendLine("  Rule: NO BREAK, NO TRADE.");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Generates IdiotScript for this setup.
    /// </summary>
    public string ToIdiotScript()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"// {Symbol} - {Bias}");
        sb.AppendLine($"// Pattern: {Pattern}");
        sb.AppendLine($"// Trigger: Break over ${TriggerPrice:F2}");
        sb.AppendLine("// Rule: NO BREAK, NO TRADE");
        sb.AppendLine();
        sb.AppendLine($"Ticker({Symbol})");
        sb.AppendLine($"    .Name(\"{Symbol} {Pattern}\")");
        sb.AppendLine($"    .Session(IS.PREMARKET)");
        sb.AppendLine($"    .Breakout({TriggerPrice})");
        sb.AppendLine($"    .Pullback()");
        sb.AppendLine($"    .IsAboveVwap()");
        if (SupportPrice > 0 && Math.Abs(SupportPrice - VwapPrice) > 0.01)
            sb.AppendLine($"    .HoldsAbove({SupportPrice})");
        sb.AppendLine($"    .Long()");
        
        if (Targets.Count >= 3)
            sb.AppendLine($"    .TakeProfit({Targets[0].Price}, {Targets[1].Price}, {Targets[2].Price})");
        else if (Targets.Count >= 2)
            sb.AppendLine($"    .TakeProfit({Targets[0].Price}, {Targets[1].Price})");
        else if (Targets.Count >= 1)
            sb.AppendLine($"    .TakeProfit({Targets[0].Price})");
            
        sb.AppendLine($"    .StopLoss({InvalidationPrice})");
        sb.AppendLine($"    .Repeat()");
        
        return sb.ToString();
    }
}

/// <summary>
/// Target level for a setup.
/// </summary>
public sealed class TargetLevel
{
    public string Label { get; set; } = "T1";
    public double Price { get; set; }
    public int PercentToSell { get; set; } = 50;
    public bool IsHit { get; set; }
}

/// <summary>
/// State of a breakout setup.
/// </summary>
public enum SetupState
{
    Watching,       // Waiting for trigger
    Triggered,      // Breakout occurred, waiting for pullback
    PullingBack,    // In pullback phase
    Confirmed,      // Pullback held support - READY TO ENTER
    Entered,        // Position active
    Scaling,        // Hitting targets, scaling out
    Completed,      // All done
    Invalidated     // Pattern failed
}

/// <summary>
/// Scans for breakout-pullback setups from gapper candidates.
/// </summary>
public sealed class PremarketSetupScanner
{
    private readonly SetupScannerConfig config;
    
    public PremarketSetupScanner(SetupScannerConfig? config = null)
    {
        config = config ?? new SetupScannerConfig();
    }
    
    /// <summary>
    /// Scans gapper candidates and generates breakout-pullback setups.
    /// </summary>
    public SetupScanResult ScanGappers(IEnumerable<ScannerInput> gappers)
    {
        var result = new SetupScanResult();
        var candidates = gappers.ToList();
        result.TotalScanned = candidates.Count;
        
        foreach (var gapper in candidates)
        {
            var setup = AnalyzeGapper(gapper);
            if (setup != null && setup.ConfidenceScore >= config.MinConfidenceScore)
            {
                result.Setups.Add(setup);
            }
        }
        
        // Sort by confidence
        result.Setups = result.Setups
            .OrderByDescending(s => s.ConfidenceScore)
            .ThenByDescending(s => Math.Abs(s.GapPercent))
            .ToList();
        
        return result;
    }
    
    /// <summary>
    /// Analyzes a single gapper and generates a setup if qualified.
    /// </summary>
    public BreakoutSetup? AnalyzeGapper(ScannerInput gapper)
    {
        // Basic filters
        if (gapper.PremarketPrice < config.MinPrice || gapper.PremarketPrice > config.MaxPrice)
            return null;
            
        if (Math.Abs(gapper.GapPercent) < config.MinGapPercent)
            return null;
            
        if (gapper.VolumeRatio < config.MinVolumeRatio)
            return null;
        
        // Determine bias and pattern
        var (bias, pattern) = DetermineBiasAndPattern(gapper);
        
        // Calculate key levels
        var trigger = CalculateTriggerLevel(gapper);
        var support = CalculateSupportLevel(gapper, trigger);
        var invalidation = CalculateInvalidation(gapper, support);
        var targets = CalculateTargets(trigger, invalidation, gapper.GapPercent);
        
        // Calculate confidence score
        var confidence = CalculateConfidence(gapper, trigger, support);
        
        return new BreakoutSetup
        {
            Symbol = gapper.Symbol,
            CompanyName = gapper.CompanyName,
            Bias = bias,
            Pattern = pattern,
            ConfidenceScore = confidence,
            TriggerPrice = Math.Round(trigger, 2),
            SupportPrice = Math.Round(support, 2),
            VwapPrice = 0, // Will be set dynamically
            InvalidationPrice = Math.Round(invalidation, 2),
            Targets = targets,
            CurrentPrice = gapper.PremarketPrice,
            GapPercent = gapper.GapPercent,
            VolumeRatio = gapper.VolumeRatio,
            State = SetupState.Watching
        };
    }
    
    private (string bias, string pattern) DetermineBiasAndPattern(ScannerInput gapper)
    {
        string bias;
        string pattern;
        
        // Determine bias based on gap direction and magnitude
        if (gapper.GapPercent >= 10)
            bias = "Strong gap momentum";
        else if (gapper.GapPercent >= 5)
            bias = "Bullish continuation";
        else if (gapper.GapPercent > 0)
            bias = "Modest gap - watch for confirmation";
        else if (gapper.GapPercent <= -5)
            bias = "Bearish gap (short or fade candidate)";
        else
            bias = "Minor gap";
        
        // Determine pattern based on available info
        if (gapper.VolumeRatio >= 3.0)
            pattern = "High volume breakout";
        else if (!string.IsNullOrEmpty(gapper.Catalyst))
            pattern = "Catalyst-driven momentum";
        else if (gapper.VolumeRatio >= 1.5)
            pattern = "Breakout pullback";
        else
            pattern = "Standard gap play";
        
        return (bias, pattern);
    }
    
    private double CalculateTriggerLevel(ScannerInput gapper)
    {
        var price = gapper.PremarketPrice;
        
        // Trigger is typically slightly above current price for a breakout play
        // Round to clean levels based on price tier
        if (price < 1)
        {
            // Penny stocks: round to nearest $0.02-$0.05
            var increment = price < 0.50 ? 0.02 : 0.05;
            return Math.Ceiling(price / increment) * increment;
        }
        else if (price < 5)
        {
            // Low-priced: round to nearest $0.05-$0.10
            var increment = price < 2 ? 0.05 : 0.10;
            return Math.Ceiling(price / increment) * increment;
        }
        else if (price < 20)
        {
            // Mid-priced: round to nearest $0.25
            return Math.Ceiling(price * 4) / 4;
        }
        else
        {
            // Higher-priced: round to nearest $0.50 or $1
            var increment = price < 50 ? 0.50 : 1.00;
            return Math.Ceiling(price / increment) * increment;
        }
    }
    
    private double CalculateSupportLevel(ScannerInput gapper, double trigger)
    {
        // Support is typically:
        // 1. VWAP (we'll use 0 as placeholder - set dynamically)
        // 2. Or a specific level ~2-5% below trigger
        
        // Default: resistance becomes support concept
        // The trigger level itself becomes support after breakout
        // But we also want a "hard" support level
        
        var price = gapper.PremarketPrice;
        
        // For penny stocks, tighter support
        if (price < 1)
            return trigger * 0.96; // 4% below trigger
        else if (price < 5)
            return trigger * 0.95; // 5% below trigger
        else
            return trigger * 0.97; // 3% below trigger
    }
    
    private double CalculateInvalidation(ScannerInput gapper, double support)
    {
        // Invalidation is below support
        // Typically 1-3% below support level
        var price = gapper.PremarketPrice;
        
        if (price < 1)
            return support * 0.95; // 5% below support for volatile pennies
        else if (price < 5)
            return support * 0.97; // 3% below support
        else
            return support * 0.98; // 2% below support
    }
    
    private List<TargetLevel> CalculateTargets(double trigger, double invalidation, double gapPercent)
    {
        var targets = new List<TargetLevel>();
        var risk = trigger - invalidation;
        
        // Larger gaps get more aggressive targets
        var t1Multiplier = 1.5;
        var t2Multiplier = 2.5;
        var t3Multiplier = 4.0;
        
        if (gapPercent >= 10)
        {
            // Big gap = potential runner
            t1Multiplier = 2.0;
            t2Multiplier = 3.5;
            t3Multiplier = 5.0;
        }
        
        targets.Add(new TargetLevel
        {
            Label = "T1",
            Price = Math.Round(trigger + risk * t1Multiplier, 2),
            PercentToSell = 40
        });
        
        targets.Add(new TargetLevel
        {
            Label = "T2",
            Price = Math.Round(trigger + risk * t2Multiplier, 2),
            PercentToSell = 40
        });
        
        // T3 for strong setups
        if (gapPercent >= 5)
        {
            targets.Add(new TargetLevel
            {
                Label = "T3",
                Price = Math.Round(trigger + risk * t3Multiplier, 2),
                PercentToSell = 20
            });
        }
        
        return targets;
    }
    
    private int CalculateConfidence(ScannerInput gapper, double trigger, double support)
    {
        int score = 50; // Base score
        
        // Gap magnitude (up to +20)
        var gapAbs = Math.Abs(gapper.GapPercent);
        if (gapAbs >= 10) score += 20;
        else if (gapAbs >= 5) score += 15;
        else if (gapAbs >= 3) score += 10;
        
        // Volume ratio (up to +20)
        if (gapper.VolumeRatio >= 3.0) score += 20;
        else if (gapper.VolumeRatio >= 2.0) score += 15;
        else if (gapper.VolumeRatio >= 1.5) score += 10;
        
        // Catalyst (up to +10)
        if (!string.IsNullOrEmpty(gapper.Catalyst)) score += 10;
        
        // Multiple sources (up to +10)
        if (gapper.SourceCount >= 3) score += 10;
        else if (gapper.SourceCount >= 2) score += 5;
        
        // Penalize very low priced stocks
        if (gapper.PremarketPrice < 0.50) score -= 10;
        
        return Math.Clamp(score, 0, 100);
    }
}

/// <summary>
/// Configuration for the setup scanner.
/// </summary>
public sealed class SetupScannerConfig
{
    public double MinPrice { get; set; } = 0.30;
    public double MaxPrice { get; set; } = 25.00;
    public double MinGapPercent { get; set; } = 3.0;
    public double MinVolumeRatio { get; set; } = 1.5;
    public int MinConfidenceScore { get; set; } = 60;
}

/// <summary>
/// Statistics about current breakout setups.
/// </summary>
public sealed class SetupStatistics
{
    public int TotalSetups { get; set; }
    public int Watching { get; set; }
    public int Triggered { get; set; }
    public int PullingBack { get; set; }
    public int Confirmed { get; set; }
    public int Entered { get; set; }
    public int Completed { get; set; }
    public int Invalidated { get; set; }
    public double AverageConfidence { get; set; }
}
