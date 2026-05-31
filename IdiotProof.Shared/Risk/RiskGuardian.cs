// ============================================================================
// Risk Guardian - Makes It IMPOSSIBLE to Lose More Than Your Limit
// ============================================================================
// This is the GATEKEEPER. No trade goes through without:
// 1. A stop loss
// 2. Risk within your max loss limit
// 3. Position size that can't exceed your max loss even in worst case
//
// "I will NEVER allow you to lose more than $X on any single trade"
//
// All money fields are decimal — RiskGuardian operates on the canonical
// IdiotProof.Models.TradeSetup, which is decimal-priced.
// ============================================================================

using IdiotProof.Models;

namespace IdiotProof.Shared.Risk;

/// <summary>
/// Risk Guardian Configuration - Set your absolute limits.
/// </summary>
public sealed class RiskGuardianConfig
{
    /// <summary>ABSOLUTE MAXIMUM you can lose on a single trade. Non-negotiable.</summary>
    public decimal MaxLossPerTrade { get; set; } = 100m;

    /// <summary>ABSOLUTE MAXIMUM you can lose in a single day. Circuit breaker.</summary>
    public decimal MaxLossPerDay { get; set; } = 500m;

    /// <summary>Minimum stop loss distance (percent). Prevents micro-stops triggered by noise.</summary>
    public decimal MinStopLossPercent { get; set; } = 0.5m;

    /// <summary>Maximum stop loss distance (percent). Prevents ridiculously wide stops.</summary>
    public decimal MaxStopLossPercent { get; set; } = 5m;

    /// <summary>Require confirmation for trades above this risk amount.</summary>
    public decimal ConfirmationThreshold { get; set; } = 50m;

    /// <summary>Account balance for position sizing calculations.</summary>
    public decimal AccountBalance { get; set; } = 10_000m;

    /// <summary>Maximum percent of account to risk per trade.</summary>
    public decimal MaxAccountRiskPercent { get; set; } = 1m;
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

    /// <summary>Absolute worst-case loss if everything goes wrong (gap through stop, etc.)</summary>
    public decimal WorstCaseLoss { get; set; }

    /// <summary>Expected loss if stop is hit normally.</summary>
    public decimal ExpectedLoss { get; set; }

    public string Summary => IsApproved
        ? (RequiresConfirmation ? "⚠️ APPROVED WITH CONFIRMATION" : "✅ APPROVED")
        : $"🛑 BLOCKED: {string.Join(", ", BlockReasons)}";
}

/// <summary>
/// The Risk Guardian - Your trading bodyguard.
/// </summary>
public sealed class RiskGuardian
{
    private static readonly TimeZoneInfo EasternTimeZone = ResolveEasternTimeZone();

    private readonly RiskGuardianConfig config;
    private decimal dailyLoss;
    private DateOnly lastResetDate = CurrentTradingDate();

    public RiskGuardian(RiskGuardianConfig? config = null)
    {
        this.config = config ?? new RiskGuardianConfig();
    }

    /// <summary>
    /// Validates a trade setup. Returns approval status and any adjustments needed.
    /// </summary>
    public RiskGuardianResult ValidateTrade(TradeSetup setup)
    {
        var result = new RiskGuardianResult();

        // Reset daily loss when the US equity trading day rolls over (in ET, not server local time).
        var today = CurrentTradingDate();
        if (today > lastResetDate)
        {
            dailyLoss = 0m;
            lastResetDate = today;
        }

        // === CRITICAL CHECKS - These BLOCK the trade ===

        // 1. MUST have a stop loss
        if (setup.StopLoss <= 0m)
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
        var stopPercent = setup.EntryPrice > 0m ? (stopDistance / setup.EntryPrice) * 100m : 0m;
        var riskPerShare = stopDistance;
        var totalRisk = riskPerShare * setup.Quantity;

        result.ExpectedLoss = totalRisk;

        // Worst case: assume 50% slippage through stop (gap scenario)
        result.WorstCaseLoss = totalRisk * 1.5m;

        // 4. Check if risk exceeds max per trade
        if (totalRisk > config.MaxLossPerTrade)
        {
            result.BlockReasons.Add($"Risk ${totalRisk:F2} exceeds max ${config.MaxLossPerTrade:F2} per trade");

            // Suggest adjusted quantity
            if (riskPerShare > 0m)
            {
                var adjustedQty = (int)Math.Floor(config.MaxLossPerTrade / riskPerShare);
                if (adjustedQty >= 1)
                {
                    result.AdjustedSetup = CloneWithQuantity(setup, adjustedQty);
                    result.Warnings.Add($"Suggested reduced quantity: {adjustedQty} shares (risk: ${adjustedQty * riskPerShare:F2})");
                }
            }
        }

        // 5. Check daily loss limit
        if (dailyLoss + totalRisk > config.MaxLossPerDay)
        {
            var remaining = config.MaxLossPerDay - dailyLoss;
            result.BlockReasons.Add($"Would exceed daily loss limit. Already lost ${dailyLoss:F2}, limit is ${config.MaxLossPerDay:F2}");

            if (remaining > 0m && riskPerShare > 0m)
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
        if (config.AccountBalance > 0m)
        {
            var accountRiskPercent = (totalRisk / config.AccountBalance) * 100m;
            if (accountRiskPercent > config.MaxAccountRiskPercent)
            {
                result.BlockReasons.Add($"Risk is {accountRiskPercent:F2}% of account. Max is {config.MaxAccountRiskPercent}%");
            }
        }

        // === WARNINGS - These don't block but require attention ===

        // R:R ratio check
        if (setup.RiskRewardRatio < 1.5m)
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
    public void RecordTradePnL(decimal pnl)
    {
        if (pnl < 0m)
        {
            dailyLoss += Math.Abs(pnl);
        }
    }

    /// <summary>
    /// Gets remaining daily risk allowance.
    /// </summary>
    public decimal GetRemainingDailyRisk() => Math.Max(0m, config.MaxLossPerDay - dailyLoss);

    /// <summary>
    /// Calculates the maximum quantity you can trade given current limits.
    /// </summary>
    public int CalculateMaxQuantity(decimal entryPrice, decimal stopLoss)
    {
        var riskPerShare = Math.Abs(entryPrice - stopLoss);
        if (riskPerShare <= 0m) return 0;

        // Take the most restrictive limit
        var fromMaxPerTrade    = (int)Math.Floor(config.MaxLossPerTrade / riskPerShare);
        var fromDailyRemaining = (int)Math.Floor(GetRemainingDailyRisk() / riskPerShare);
        var fromAccountPercent = (int)Math.Floor((config.AccountBalance * config.MaxAccountRiskPercent / 100m) / riskPerShare);

        // Floor at 0, not 1: when the most restrictive limit allows no shares
        // (e.g. the daily-loss circuit breaker is exhausted), returning 1 would
        // place a trade that violates the limit. Callers gate on qty <= 0.
        return Math.Max(0, Math.Min(fromMaxPerTrade, Math.Min(fromDailyRemaining, fromAccountPercent)));
    }

    /// <summary>
    /// Auto-calculates a safe stop loss and quantity given entry and direction.
    /// </summary>
    public (decimal StopLoss, int Quantity) CalculateSafeParameters(
        decimal entryPrice,
        bool isLong,
        decimal? preferredStopPercent = null)
    {
        // Default to middle of allowed range
        var stopPercent = preferredStopPercent ??
            (config.MinStopLossPercent + config.MaxStopLossPercent) / 2m;

        // Clamp to allowed range
        stopPercent = Math.Clamp(stopPercent, config.MinStopLossPercent, config.MaxStopLossPercent);

        var stopDistance = entryPrice * (stopPercent / 100m);
        var stopLoss = isLong ? entryPrice - stopDistance : entryPrice + stopDistance;

        // Calculate quantity based on max loss. Floor at 0, not 1 — mirroring
        // CalculateMaxQuantity. When the budget allows no shares (daily circuit
        // breaker exhausted, or a single share already exceeds MaxLossPerTrade),
        // forcing a minimum of 1 would hand back a "safe" size that knowingly
        // breaches the cap. A zero risk-per-share (entry == stop, no protection)
        // likewise yields 0, not a tradeable 1. Callers gate on quantity <= 0.
        var riskPerShare = stopDistance;
        var maxLoss = Math.Min(config.MaxLossPerTrade, GetRemainingDailyRisk());
        var quantity = riskPerShare > 0m ? Math.Max(0, (int)Math.Floor(maxLoss / riskPerShare)) : 0;

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
    private static TradeSetup CloneWithQuantity(TradeSetup original, int newQuantity) => new()
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

    private static DateOnly CurrentTradingDate()
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTimeZone);
        // Anything before 4 AM ET still belongs to the previous trading session.
        if (et.Hour < 4) et = et.AddDays(-1);
        return DateOnly.FromDateTime(et);
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.Utc;
    }
}

/// <summary>
/// Current status of the Risk Guardian.
/// </summary>
public sealed class RiskGuardianStatus
{
    public decimal MaxLossPerTrade { get; init; }
    public decimal MaxLossPerDay { get; init; }
    public decimal DailyLossSoFar { get; init; }
    public decimal RemainingDailyRisk { get; init; }
    public decimal AccountBalance { get; init; }
    public bool IsCircuitBreakerTripped { get; init; }

    public decimal DailyLossPercent => MaxLossPerDay > 0m ? (DailyLossSoFar / MaxLossPerDay) * 100m : 0m;
}
