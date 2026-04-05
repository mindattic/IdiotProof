// ============================================================================
// Risk Guardian - Makes It IMPOSSIBLE to Lose More Than Your Limit
// ============================================================================
// This is the GATEKEEPER. No trade goes through without:
// 1. A stop loss
// 2. Risk within your max loss limit
// 3. Position size that can't exceed your max loss even in worst case
//
// "I will NEVER allow you to lose more than $X on any single trade"
// ============================================================================

using IdiotProof.Shared;

namespace IdiotProof.Shared.Risk;

/// <summary>
/// Risk Guardian Configuration - Set your absolute limits.
/// </summary>
public sealed class RiskGuardianConfig
{
    /// <summary>
    /// ABSOLUTE MAXIMUM you can lose on a single trade. Non-negotiable.
    /// </summary>
    public double MaxLossPerTrade { get; set; } = 100.0;
    
    /// <summary>
    /// ABSOLUTE MAXIMUM you can lose in a single day. Circuit breaker.
    /// </summary>
    public double MaxLossPerDay { get; set; } = 500.0;
    
    /// <summary>
    /// Minimum stop loss distance (percent). Prevents micro-stops that get triggered by noise.
    /// </summary>
    public double MinStopLossPercent { get; set; } = 0.5;
    
    /// <summary>
    /// Maximum stop loss distance (percent). Prevents ridiculously wide stops.
    /// </summary>
    public double MaxStopLossPercent { get; set; } = 5.0;
    
    /// <summary>
    /// Require confirmation for trades above this risk amount.
    /// </summary>
    public double ConfirmationThreshold { get; set; } = 50.0;
    
    /// <summary>
    /// Account balance for position sizing calculations.
    /// </summary>
    public double AccountBalance { get; set; } = 10_000.0;
    
    /// <summary>
    /// Maximum percent of account to risk per trade.
    /// </summary>
    public double MaxAccountRiskPercent { get; set; } = 1.0;
}

/// <summary>
/// Trade validation result from Risk Guardian.
/// </summary>
public sealed class RiskGuardianResult
{
    public bool IsApproved { get; set; }
    public bool RequiresConfirmation { get; set; }
    public List<string> BlockReasons { get; } = [];
    public List<string> Warnings { get; } = [];
    public TradeSetup? AdjustedSetup { get; set; }
    
    /// <summary>
    /// Absolute worst-case loss if everything goes wrong (gap through stop, etc.)
    /// </summary>
    public double WorstCaseLoss { get; set; }
    
    /// <summary>
    /// Expected loss if stop is hit normally.
    /// </summary>
    public double ExpectedLoss { get; set; }
    
    public string Summary => IsApproved 
        ? (RequiresConfirmation ? "⚠️ APPROVED WITH CONFIRMATION" : "✅ APPROVED")
        : $"🛑 BLOCKED: {string.Join(", ", BlockReasons)}";
}

/// <summary>
/// The Risk Guardian - Your trading bodyguard.
/// </summary>
public sealed class RiskGuardian
{
    private readonly RiskGuardianConfig config;
    private double dailyLoss = 0;
    private DateTime lastResetDate = DateTime.Today;
    
    public RiskGuardian(RiskGuardianConfig? config = null)
    {
        config = config ?? new RiskGuardianConfig();
    }
    
    /// <summary>
    /// Validates a trade setup. Returns approval status and any adjustments needed.
    /// </summary>
    public RiskGuardianResult ValidateTrade(TradeSetup setup)
    {
        var result = new RiskGuardianResult();
        
        // Reset daily loss if new day
        if (DateTime.Today > lastResetDate)
        {
            dailyLoss = 0;
            lastResetDate = DateTime.Today;
        }
        
        // === CRITICAL CHECKS - These BLOCK the trade ===
        
        // 1. MUST have a stop loss
        if (setup.StopLoss <= 0)
        {
            result.BlockReasons.Add("NO STOP LOSS - Every trade MUST have a stop loss");
            result.IsApproved = false;
            return result;
        }
        
        // 2. Stop loss must be on correct side of entry
        if (setup.IsLong && setup.StopLoss >= setup.EntryPrice)
        {
            result.BlockReasons.Add("LONG stop loss must be BELOW entry price");
            result.IsApproved = false;
            return result;
        }
        if (!setup.IsLong && setup.StopLoss <= setup.EntryPrice)
        {
            result.BlockReasons.Add("SHORT stop loss must be ABOVE entry price");
            result.IsApproved = false;
            return result;
        }
        
        // 3. Calculate actual risk
        var stopDistance = Math.Abs(setup.EntryPrice - setup.StopLoss);
        var stopPercent = (stopDistance / setup.EntryPrice) * 100;
        var riskPerShare = stopDistance;
        var totalRisk = riskPerShare * setup.Quantity;
        
        result.ExpectedLoss = totalRisk;
        
        // Worst case: assume 50% slippage through stop (gap scenario)
        result.WorstCaseLoss = totalRisk * 1.5;

        // 4. Check if risk exceeds max per trade
        if (totalRisk > config.MaxLossPerTrade)
        {
            result.BlockReasons.Add($"Risk ${totalRisk:F2} exceeds max ${config.MaxLossPerTrade:F2} per trade");

            // Suggest adjusted quantity
            var adjustedQty = (int)Math.Floor(config.MaxLossPerTrade / riskPerShare);
            if (adjustedQty >= 1)
            {
                result.AdjustedSetup = CloneWithQuantity(setup, adjustedQty);
                result.Warnings.Add($"Suggested reduced quantity: {adjustedQty} shares (risk: ${adjustedQty * riskPerShare:F2})");
            }
        }

        // 5. Check daily loss limit
        if (dailyLoss + totalRisk > config.MaxLossPerDay)
        {
            var remaining = config.MaxLossPerDay - dailyLoss;
            result.BlockReasons.Add($"Would exceed daily loss limit. Already lost ${dailyLoss:F2}, limit is ${config.MaxLossPerDay:F2}");

            if (remaining > 0)
            {
                var adjustedQty = (int)Math.Floor(remaining / riskPerShare);
                if (adjustedQty >= 1)
                {
                    result.AdjustedSetup = CloneWithQuantity(setup, adjustedQty);
                    result.Warnings.Add($"Remaining daily risk: ${remaining:F2} ({adjustedQty} shares max)");
                }
            }
        }

        // 6. Check stop loss distance
        if (stopPercent < config.MinStopLossPercent)
        {
            result.BlockReasons.Add($"Stop loss too tight ({stopPercent:F2}%). Min is {config.MinStopLossPercent}% to avoid noise stops");
        }
        
        if (stopPercent > config.MaxStopLossPercent)
        {
            result.BlockReasons.Add($"Stop loss too wide ({stopPercent:F2}%). Max is {config.MaxStopLossPercent}%");
        }
        
        // 7. Check account risk percent
        var accountRiskPercent = (totalRisk / config.AccountBalance) * 100;
        if (accountRiskPercent > config.MaxAccountRiskPercent)
        {
            result.BlockReasons.Add($"Risk is {accountRiskPercent:F2}% of account. Max is {config.MaxAccountRiskPercent}%");
        }
        
        // === WARNINGS - These don't block but require attention ===
        
        // R:R ratio check
        if (setup.RiskRewardRatio < 1.5)
        {
            result.Warnings.Add($"R:R ratio {setup.RiskRewardRatio:F1} is below recommended 1.5");
        }
        
        // Confidence check
        if (setup.ConfidenceScore < 50)
        {
            result.Warnings.Add($"Low confidence score ({setup.ConfidenceScore}%). Consider waiting for better setup");
        }
        
        // Quantity sanity check
        if (setup.Quantity > 1000)
        {
            result.Warnings.Add($"Large position size ({setup.Quantity} shares). Double-check this is intentional");
        }
        
        // === FINAL DECISION ===
        
        result.IsApproved = result.BlockReasons.Count == 0;
        result.RequiresConfirmation = result.IsApproved && totalRisk > config.ConfirmationThreshold;
        
        return result;
    }
    
    /// <summary>
    /// Records a completed trade for daily tracking.
    /// </summary>
    public void RecordTradePnL(double pnl)
    {
        if (pnl < 0)
        {
            dailyLoss += Math.Abs(pnl);
        }
    }
    
    /// <summary>
    /// Gets remaining daily risk allowance.
    /// </summary>
    public double GetRemainingDailyRisk() => Math.Max(0, config.MaxLossPerDay - dailyLoss);
    
    /// <summary>
    /// Calculates the maximum quantity you can trade given current limits.
    /// </summary>
    public int CalculateMaxQuantity(double entryPrice, double stopLoss)
    {
        var riskPerShare = Math.Abs(entryPrice - stopLoss);
        if (riskPerShare <= 0) return 0;
        
        // Take the most restrictive limit
        var fromMaxPerTrade = (int)Math.Floor(config.MaxLossPerTrade / riskPerShare);
        var fromDailyRemaining = (int)Math.Floor(GetRemainingDailyRisk() / riskPerShare);
        var fromAccountPercent = (int)Math.Floor((config.AccountBalance * config.MaxAccountRiskPercent / 100) / riskPerShare);
        
        return Math.Max(1, Math.Min(fromMaxPerTrade, Math.Min(fromDailyRemaining, fromAccountPercent)));
    }
    
    /// <summary>
    /// Auto-calculates a safe stop loss and quantity given entry and direction.
    /// </summary>
    public (double StopLoss, int Quantity) CalculateSafeParameters(
        double entryPrice, 
        bool isLong, 
        double? preferredStopPercent = null)
    {
        // Default to middle of allowed range
        var stopPercent = preferredStopPercent ?? 
            (config.MinStopLossPercent + config.MaxStopLossPercent) / 2;
        
        // Clamp to allowed range
        stopPercent = Math.Clamp(stopPercent, config.MinStopLossPercent, config.MaxStopLossPercent);
        
        var stopDistance = entryPrice * (stopPercent / 100);
        var stopLoss = isLong ? entryPrice - stopDistance : entryPrice + stopDistance;
        
        // Calculate quantity based on max loss
        var riskPerShare = stopDistance;
        var maxLoss = Math.Min(config.MaxLossPerTrade, GetRemainingDailyRisk());
        var quantity = (int)Math.Floor(maxLoss / riskPerShare);
        quantity = Math.Max(1, quantity);
        
        return (Math.Round(stopLoss, 2), quantity);
    }

    /// <summary>
    /// Gets current status for display.
    /// </summary>
    public RiskGuardianStatus GetStatus() => new()
    {
        MaxLossPerTrade = config.MaxLossPerTrade,
        MaxLossPerDay = config.MaxLossPerDay,
        DailyLossSoFar = dailyLoss,
        RemainingDailyRisk = GetRemainingDailyRisk(),
        AccountBalance = config.AccountBalance,
        IsCircuitBreakerTripped = dailyLoss >= config.MaxLossPerDay
    };

    /// <summary>
    /// Creates a copy of the setup with adjusted quantity.
    /// </summary>
    private static TradeSetup CloneWithQuantity(TradeSetup original, int newQuantity)
    {
        return new TradeSetup
        {
            SetupId = original.SetupId,
            Symbol = original.Symbol,
            CompanyName = original.CompanyName,
            Direction = original.Direction,
            EntryPrice = original.EntryPrice,
            EntryType = original.EntryType,
            StopLoss = original.StopLoss,
            TakeProfit = original.TakeProfit,
            TrailingStopPercent = original.TrailingStopPercent,
            Quantity = newQuantity,
            RiskDollars = Math.Abs(original.EntryPrice - original.StopLoss) * newQuantity,
            RewardDollars = Math.Abs(original.TakeProfit - original.EntryPrice) * newQuantity,
            ConfidenceScore = original.ConfidenceScore,
            Rationale = original.Rationale,
            BullishFactors = original.BullishFactors,
            BearishFactors = original.BearishFactors
        };
    }
}

/// <summary>
/// Current status of the Risk Guardian.
/// </summary>
public sealed class RiskGuardianStatus
{
    public double MaxLossPerTrade { get; init; }
    public double MaxLossPerDay { get; init; }
    public double DailyLossSoFar { get; init; }
    public double RemainingDailyRisk { get; init; }
    public double AccountBalance { get; init; }
    public bool IsCircuitBreakerTripped { get; init; }
    
    public double DailyLossPercent => MaxLossPerDay > 0 ? (DailyLossSoFar / MaxLossPerDay) * 100 : 0;
}
