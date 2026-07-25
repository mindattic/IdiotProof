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

    /// <summary>
    /// Maximum stop loss distance (percent). Prevents ridiculously wide stops.
    /// Default 10% (not 5%): the flagship gapper profiles legitimately need
    /// wide stops — the shipped "Penny Runner" uses 8% because pennies collapse
    /// fast — and a 5% ceiling silently blocked every fire of that profile
    /// under default limits. The BINDING money constraint remains the DOLLAR
    /// cap (<see cref="MaxLossPerTrade"/>), which sizes the position so a wider
    /// stop can't exceed the loss limit; this percent guard is a secondary
    /// sanity bound on stop WIDTH, not the primary protection.
    /// </summary>
    public decimal MaxStopLossPercent { get; set; } = 10m;

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

    // The Monitor can evaluate two strategies for the same user back-to-back
    // (RiskGuardianService comment: "may hit two strategies from the same user").
    // decimal is 128-bit and not guaranteed atomic; UpdateConfig can race with
    // ValidateTrade reads. All mutable state goes through this lock.
    private readonly Lock sync = new();
    private const decimal SlippageFactor = 1.5m;

    private RiskGuardianConfig config;
    private decimal dailyLoss;
    private DateOnly lastResetDate = CurrentTradingDate();

    public RiskGuardian(RiskGuardianConfig? config = null)
    {
        this.config = config ?? new RiskGuardianConfig();
    }

    /// <summary>
    /// Swaps in fresh limits WITHOUT touching the in-memory daily-loss
    /// counter. This is how a long-lived Guardian (the Monitor caches one per
    /// user for the process lifetime, precisely so dailyLoss survives) picks
    /// up risk-config edits made in the UI — rebuilding the instance instead
    /// would silently reset the daily circuit breaker.
    /// </summary>
    public void UpdateConfig(RiskGuardianConfig newConfig)
    {
        ArgumentNullException.ThrowIfNull(newConfig);
        lock (sync) config = newConfig;
    }

    /// <summary>
    /// Validates a trade setup. Returns approval status and any adjustments needed.
    /// </summary>
    public RiskGuardianResult ValidateTrade(TradeSetup setup)
    {
        var result = new RiskGuardianResult();
        decimal dailyLossSnapshot;
        RiskGuardianConfig cfg;
        lock (sync)
        {
            ResetDailyLossIfNewTradingDay();
            dailyLossSnapshot = dailyLoss;
            cfg = config;
        }

        // === CRITICAL CHECKS - These BLOCK the trade ===

        // 0. Quantity must be positive — zero/negative makes totalRisk 0 or negative,
        //    which would pass every dollar-threshold check below.
        if (setup.Quantity <= 0)
        {
            result.BlockReasons.Add($"INVALID QUANTITY {setup.Quantity} — must be at least 1 share");
            result.IsApproved = false;
            return result;
        }

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

        // 3. Entry price must be positive before any division
        if (setup.EntryPrice <= 0m)
        {
            result.BlockReasons.Add("INVALID ENTRY PRICE — must be greater than zero");
            result.IsApproved = false;
            return result;
        }

        var stopDistance = Math.Abs(setup.EntryPrice - setup.StopLoss);
        var stopPercent = (stopDistance / setup.EntryPrice) * 100m;
        var riskPerShare = stopDistance;
        var totalRisk = riskPerShare * setup.Quantity;

        result.ExpectedLoss = totalRisk;

        // Worst case: assume 50% slippage through stop (gap scenario). This is
        // the number the class's own contract promises never to exceed
        // ("Position size that can't exceed your max loss even in worst
        // case") — gating on the un-slipped totalRisk instead let a trade
        // through whose worst case could run up to 1.5x MaxLossPerTrade.
        result.WorstCaseLoss = totalRisk * SlippageFactor;

        // 4. Check if worst-case risk exceeds max per trade
        if (result.WorstCaseLoss > cfg.MaxLossPerTrade)
        {
            result.BlockReasons.Add($"Worst-case risk ${result.WorstCaseLoss:F2} exceeds max ${cfg.MaxLossPerTrade:F2} per trade");

            // Suggest adjusted quantity — sized off the SAME worst-case factor
            // used to block, so the suggestion is actually safe to take.
            if (riskPerShare > 0m)
            {
                var adjustedQty = (int)Math.Floor(cfg.MaxLossPerTrade / (riskPerShare * SlippageFactor));
                if (adjustedQty >= 1)
                {
                    result.AdjustedSetup = CloneWithQuantity(setup, adjustedQty);
                    result.Warnings.Add($"Suggested reduced quantity: {adjustedQty} shares (worst-case risk: ${adjustedQty * riskPerShare * SlippageFactor:F2})");
                }
            }
        }

        // 5. Check daily loss limit
        if (dailyLossSnapshot + result.WorstCaseLoss > cfg.MaxLossPerDay)
        {
            var remaining = cfg.MaxLossPerDay - dailyLossSnapshot;
            result.BlockReasons.Add($"Would exceed daily loss limit. Already lost ${dailyLossSnapshot:F2}, limit is ${cfg.MaxLossPerDay:F2}");

            if (remaining > 0m && riskPerShare > 0m)
            {
                var adjustedQty = (int)Math.Floor(remaining / (riskPerShare * SlippageFactor));
                // Cap against per-trade suggestion so the final recommendation
                // never exceeds the most restrictive active constraint.
                if (result.AdjustedSetup is not null)
                    adjustedQty = Math.Min(adjustedQty, result.AdjustedSetup.Quantity);
                if (adjustedQty >= 1)
                {
                    result.AdjustedSetup = CloneWithQuantity(setup, adjustedQty);
                    result.Warnings.Add($"Remaining daily risk: ${remaining:F2} ({adjustedQty} shares max)");
                }
            }
        }

        // 6. Check stop loss distance
        if (stopPercent < cfg.MinStopLossPercent)
        {
            result.BlockReasons.Add($"Stop loss too tight ({stopPercent:F2}%). Min is {cfg.MinStopLossPercent}% to avoid noise stops");
        }

        if (stopPercent > cfg.MaxStopLossPercent)
        {
            result.BlockReasons.Add($"Stop loss too wide ({stopPercent:F2}%). Max is {cfg.MaxStopLossPercent}%");
        }

        // 7. Check account risk percent. An unset/zero AccountBalance must fail
        // CLOSED (this check can't be computed, so it can't be satisfied) —
        // silently skipping it let a user disable the whole account-risk-%
        // gate just by leaving Account Balance at 0.
        if (cfg.AccountBalance <= 0m)
        {
            result.BlockReasons.Add("Account balance is not set — cannot verify risk is within your account-risk % limit");
        }
        else
        {
            var accountRiskPercent = (totalRisk / cfg.AccountBalance) * 100m;
            if (accountRiskPercent > cfg.MaxAccountRiskPercent)
            {
                result.BlockReasons.Add($"Risk is {accountRiskPercent:F2}% of account. Max is {cfg.MaxAccountRiskPercent}%");
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
        result.RequiresConfirmation = result.IsApproved && totalRisk > cfg.ConfirmationThreshold;

        return result;
    }

    /// <summary>
    /// Records a completed trade for daily tracking. Rolls the daily-loss
    /// counter over first — without this, an exit that lands before this
    /// Guardian instance's first <see cref="ValidateTrade"/> call of the new
    /// trading day (e.g. a position held overnight and closed before any new
    /// entry is evaluated) added straight onto yesterday's stale total,
    /// letting a prior day's losses falsely trip today's circuit breaker.
    /// </summary>
    public void RecordTradePnL(decimal pnl)
    {
        lock (sync)
        {
            ResetDailyLossIfNewTradingDay();
            if (pnl < 0m) dailyLoss += Math.Abs(pnl);
        }
    }

    /// <summary>Resets the daily-loss counter when the US equity trading day rolls over (ET, not server local time).</summary>
    private void ResetDailyLossIfNewTradingDay()
    {
        var today = CurrentTradingDate();
        if (today > lastResetDate)
        {
            dailyLoss = 0m;
            lastResetDate = today;
        }
    }

    /// <summary>
    /// Gets remaining daily risk allowance.
    /// </summary>
    public decimal GetRemainingDailyRisk()
    {
        lock (sync) return Math.Max(0m, config.MaxLossPerDay - dailyLoss);
    }

    /// <summary>
    /// Calculates the maximum quantity you can trade given current limits.
    /// </summary>
    public int CalculateMaxQuantity(decimal entryPrice, decimal stopLoss)
    {
        var riskPerShare = Math.Abs(entryPrice - stopLoss);
        if (riskPerShare <= 0m) return 0;

        decimal maxPerTrade, remaining, balance, accountRiskPct;
        lock (sync)
        {
            ResetDailyLossIfNewTradingDay();
            maxPerTrade    = config.MaxLossPerTrade;
            remaining      = Math.Max(0m, config.MaxLossPerDay - dailyLoss);
            balance        = config.AccountBalance;
            accountRiskPct = config.MaxAccountRiskPercent;
        }

        var fromMaxPerTrade    = (int)Math.Floor(maxPerTrade / (riskPerShare * SlippageFactor));
        var fromDailyRemaining = (int)Math.Floor(remaining / (riskPerShare * SlippageFactor));
        var fromAccountPercent = (int)Math.Floor((balance * accountRiskPct / 100m) / (riskPerShare * SlippageFactor));

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
        decimal minStop, maxStop, maxPerTrade, remaining;
        lock (sync)
        {
            ResetDailyLossIfNewTradingDay();
            minStop    = config.MinStopLossPercent;
            maxStop    = config.MaxStopLossPercent;
            maxPerTrade = config.MaxLossPerTrade;
            remaining  = Math.Max(0m, config.MaxLossPerDay - dailyLoss);
        }

        var stopPercent = preferredStopPercent ?? (minStop + maxStop) / 2m;
        stopPercent = Math.Clamp(stopPercent, minStop, maxStop);

        var stopDistance = entryPrice * (stopPercent / 100m);
        var stopLoss = isLong ? entryPrice - stopDistance : entryPrice + stopDistance;

        var riskPerShare = stopDistance;
        var maxLoss = Math.Min(maxPerTrade, remaining);
        var quantity = riskPerShare > 0m ? Math.Max(0, (int)Math.Floor(maxLoss / (riskPerShare * SlippageFactor))) : 0;

        return (Math.Round(stopLoss, 2), quantity);
    }

    /// <summary>
    /// Gets current status for display.
    /// </summary>
    public RiskGuardianStatus GetStatus()
    {
        lock (sync) return new()
        {
            MaxLossPerTrade = config.MaxLossPerTrade,
            MaxLossPerDay = config.MaxLossPerDay,
            DailyLossSoFar = dailyLoss,
            RemainingDailyRisk = Math.Max(0m, config.MaxLossPerDay - dailyLoss),
            AccountBalance = config.AccountBalance,
            IsCircuitBreakerTripped = dailyLoss >= config.MaxLossPerDay
        };
    }

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
