// ============================================================================
// MultiTargetExitManager - Handles scaling out at multiple take profit levels
// ============================================================================
//
// Implements the pro trader approach of scaling out:
// - T1: Take 40-50% profit (quick scalp, reduce risk)
// - T2: Take another 30-40% (main target)
// - T3: Let remaining 10-20% run (runner for home runs)
//
// After each target hit:
// - Adjusts remaining position size
// - Optionally moves stop loss to breakeven or trail
// - Logs the partial exit
// ============================================================================

using IdiotProof.Shared;

namespace IdiotProof.Scripting;

/// <summary>
/// Manages multi-target exits with position scaling.
/// </summary>
public sealed class MultiTargetExitManager
{
    private readonly string symbol;
    private readonly List<ManagedTarget> targets = [];
    private readonly MultiTargetConfig config;
    
    // Position tracking
    private int originalQuantity;
    private int remainingQuantity;
    private double entryPrice;
    private double currentStopLoss;
    private double breakEvenPrice;
    
    // State
    private ExitManagerState state = ExitManagerState.Inactive;
    private readonly List<PartialExit> exits = [];
    
    public MultiTargetExitManager(string symbol, MultiTargetConfig? config = null)
    {
        this.symbol = symbol;
        this.config = config ?? new MultiTargetConfig();
    }
    
    /// <summary>
    /// Current state of the exit manager.
    /// </summary>
    public ExitManagerState State => state;
    
    /// <summary>
    /// Remaining quantity to manage.
    /// </summary>
    public int RemainingQuantity => remainingQuantity;
    
    /// <summary>
    /// Gets all configured targets.
    /// </summary>
    public IReadOnlyList<ManagedTarget> Targets => targets.AsReadOnly();
    
    /// <summary>
    /// Gets all completed partial exits.
    /// </summary>
    public IReadOnlyList<PartialExit> Exits => exits.AsReadOnly();
    
    /// <summary>
    /// Gets realized P&L from partial exits.
    /// </summary>
    public double RealizedPnL => exits.Sum(e => e.PnL);
    
    /// <summary>
    /// Initializes the exit manager with position details and targets.
    /// </summary>
    public void Initialize(double entryPrice, int quantity, double stopLoss, IEnumerable<TakeProfitTarget> targets)
    {
        this.entryPrice = entryPrice;
        originalQuantity = quantity;
        remainingQuantity = quantity;
        currentStopLoss = stopLoss;
        breakEvenPrice = entryPrice * 1.001; // Slightly above entry for fees
        
        // Calculate quantities for each target
        var targetList = targets.ToList();
        int totalPercent = targetList.Sum(t => t.PercentToSell);
        
        // Normalize percentages if they don't add up to 100
        var adjustedTargets = targetList.Select(t => new ManagedTarget
        {
            Label = t.Label,
            Price = t.Price,
            PercentToSell = totalPercent > 0 ? (int)Math.Round(t.PercentToSell * 100.0 / totalPercent) : t.PercentToSell,
            QuantityToSell = (int)Math.Round(quantity * t.PercentToSell / 100.0)
        }).ToList();
        
        // Ensure all shares are accounted for
        int totalQty = adjustedTargets.Sum(t => t.QuantityToSell);
        if (totalQty < quantity)
        {
            // Add remainder to last target
            adjustedTargets[^1].QuantityToSell += quantity - totalQty;
        }
        else if (totalQty > quantity)
        {
            // Remove from last target
            adjustedTargets[^1].QuantityToSell -= totalQty - quantity;
        }
        
        this.targets.Clear();
        this.targets.AddRange(adjustedTargets);
        state = ExitManagerState.Active;
    }
    
    /// <summary>
    /// Updates with current price and returns any exit signals.
    /// </summary>
    public ExitSignal? Update(double currentPrice)
    {
        if (state != ExitManagerState.Active || remainingQuantity <= 0)
            return null;
        
        // Check stop loss first
        if (currentPrice <= currentStopLoss)
        {
            return CreateExitSignal(ExitReason.StopLoss, remainingQuantity, currentPrice);
        }
        
        // Check targets (in order)
        foreach (var target in targets.Where(t => !t.IsHit))
        {
            if (currentPrice >= target.Price)
            {
                // Target hit!
                target.IsHit = true;
                target.HitTime = DateTime.UtcNow;
                target.HitPrice = currentPrice;
                
                var quantityToSell = Math.Min(target.QuantityToSell, remainingQuantity);
                
                // Record the partial exit
                var exit = new PartialExit
                {
                    TargetLabel = target.Label,
                    Quantity = quantityToSell,
                    Price = currentPrice,
                    Time = DateTime.UtcNow,
                    PnL = (currentPrice - entryPrice) * quantityToSell
                };
                exits.Add(exit);
                
                remainingQuantity -= quantityToSell;
                
                // Adjust stop loss after target hit
                AdjustStopLossAfterTarget(target);
                
                // Check if fully exited
                if (remainingQuantity <= 0)
                {
                    state = ExitManagerState.Completed;
                }
                
                return CreateExitSignal(ExitReason.TargetHit, quantityToSell, currentPrice, target.Label);
            }
        }
        
        return null;
    }
    
    private void AdjustStopLossAfterTarget(ManagedTarget hitTarget)
    {
        switch (config.StopLossAdjustment)
        {
            case StopLossAdjustmentMode.MoveToBreakeven:
                // After T1, move stop to breakeven
                if (hitTarget.Label == "T1")
                {
                    currentStopLoss = breakEvenPrice;
                }
                break;
                
            case StopLossAdjustmentMode.TrailBelowTarget:
                // Move stop to just below the hit target
                currentStopLoss = hitTarget.Price * 0.98; // 2% below target
                break;
                
            case StopLossAdjustmentMode.Progressive:
                // Progressively tighter stops
                if (hitTarget.Label == "T1")
                    currentStopLoss = breakEvenPrice;
                else if (hitTarget.Label == "T2")
                    currentStopLoss = targets.First(t => t.Label == "T1").Price;
                else
                    currentStopLoss = targets.FirstOrDefault(t => t.Label == "T2")?.Price ?? breakEvenPrice;
                break;
                
            case StopLossAdjustmentMode.None:
            default:
                // Keep original stop
                break;
        }
    }
    
    private ExitSignal CreateExitSignal(ExitReason reason, int quantity, double price, string? targetLabel = null)
    {
        return new ExitSignal
        {
            Symbol = symbol,
            Reason = reason,
            Quantity = quantity,
            Price = price,
            TargetLabel = targetLabel,
            RemainingQuantity = remainingQuantity,
            RealizedPnL = (price - entryPrice) * quantity,
            TotalRealizedPnL = RealizedPnL,
            NewStopLoss = currentStopLoss
        };
    }
    
    /// <summary>
    /// Gets a summary of the exit plan.
    /// </summary>
    public string GetExitPlan()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Exit Plan for {symbol}:");
        sb.AppendLine($"  Entry: ${entryPrice:F2} x {originalQuantity} shares");
        sb.AppendLine($"  Stop Loss: ${currentStopLoss:F2}");
        sb.AppendLine();
        
        foreach (var target in targets)
        {
            var status = target.IsHit ? "[HIT]" : "[---]";
            sb.AppendLine($"  {status} {target.Label}: ${target.Price:F2} - Sell {target.QuantityToSell} shares ({target.PercentToSell}%)");
        }
        
        if (exits.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Partial Exits:");
            foreach (var exit in exits)
            {
                sb.AppendLine($"    {exit.TargetLabel}: {exit.Quantity} @ ${exit.Price:F2} = ${exit.PnL:+0.00;-0.00}");
            }
            sb.AppendLine($"  Total Realized: ${RealizedPnL:+0.00;-0.00}");
        }
        
        sb.AppendLine($"  Remaining: {remainingQuantity} shares");
        
        return sb.ToString();
    }
}

/// <summary>
/// A target being managed for exit.
/// </summary>
public sealed class ManagedTarget
{
    public string Label { get; set; } = "T1";
    public double Price { get; set; }
    public int PercentToSell { get; set; }
    public int QuantityToSell { get; set; }
    public bool IsHit { get; set; }
    public DateTime? HitTime { get; set; }
    public double? HitPrice { get; set; }
}

/// <summary>
/// Record of a partial exit.
/// </summary>
public sealed class PartialExit
{
    public string TargetLabel { get; set; } = "";
    public int Quantity { get; set; }
    public double Price { get; set; }
    public DateTime Time { get; set; }
    public double PnL { get; set; }
}

/// <summary>
/// Signal to execute a partial or full exit.
/// </summary>
public sealed class ExitSignal
{
    public string Symbol { get; set; } = "";
    public ExitReason Reason { get; set; }
    public int Quantity { get; set; }
    public double Price { get; set; }
    public string? TargetLabel { get; set; }
    public int RemainingQuantity { get; set; }
    public double RealizedPnL { get; set; }
    public double TotalRealizedPnL { get; set; }
    public double NewStopLoss { get; set; }
    
    public override string ToString() => Reason switch
    {
        ExitReason.TargetHit => $"[{TargetLabel}] SELL {Quantity} @ ${Price:F2} (+${RealizedPnL:F2}) - {RemainingQuantity} remaining",
        ExitReason.StopLoss => $"[STOP] SELL {Quantity} @ ${Price:F2} ({(RealizedPnL >= 0 ? "+" : "")}{RealizedPnL:F2})",
        ExitReason.Manual => $"[MANUAL] SELL {Quantity} @ ${Price:F2}",
        _ => $"EXIT {Quantity} @ ${Price:F2}"
    };
}

/// <summary>
/// Reason for exit.
/// </summary>
public enum ExitReason
{
    TargetHit,
    StopLoss,
    TrailingStop,
    TimeExit,
    Manual,
    Invalidation
}

/// <summary>
/// State of the exit manager.
/// </summary>
public enum ExitManagerState
{
    Inactive,
    Active,
    Completed,
    StoppedOut
}

/// <summary>
/// How to adjust stop loss after hitting targets.
/// </summary>
public enum StopLossAdjustmentMode
{
    None,               // Keep original stop loss
    MoveToBreakeven,    // Move to breakeven after T1
    TrailBelowTarget,   // Move stop to below each hit target
    Progressive         // T1 -> BE, T2 -> T1, T3 -> T2
}

/// <summary>
/// Configuration for multi-target exits.
/// </summary>
public sealed class MultiTargetConfig
{
    public StopLossAdjustmentMode StopLossAdjustment { get; set; } = StopLossAdjustmentMode.Progressive;
    public bool MoveToBreakevenAfterT1 { get; set; } = true;
    public double BreakevenBuffer { get; set; } = 0.001; // 0.1% above entry for fees
}
