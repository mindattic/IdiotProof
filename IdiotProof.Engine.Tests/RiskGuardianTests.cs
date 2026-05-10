using IdiotProof.Models;
using IdiotProof.Shared.Risk;

namespace IdiotProof.Engine.Tests;

/// <summary>
/// RiskGuardian is the final gate before any order placement on every
/// real-money path (Monitor signal-fire, Blazor auto-trade,
/// TickerWorkspace manual fire). These tests pin the veto contract so a
/// future refactor can't silently weaken any block reason — every commit
/// that re-introduces a bypass must update or delete one of these tests
/// to make CI green, which forces the change to surface in code review.
/// </summary>
public class RiskGuardianTests
{
    private static RiskGuardianConfig DefaultConfig() => new()
    {
        MaxLossPerTrade       = 100m,
        MaxLossPerDay         = 500m,
        MinStopLossPercent    = 0.5m,
        MaxStopLossPercent    = 5m,
        AccountBalance        = 10_000m,
        MaxAccountRiskPercent = 1m,
    };

    private static TradeSetup LongSetup(decimal entry = 100m, decimal stop = 99m, decimal takeProfit = 102m, int qty = 10) => new()
    {
        Symbol      = "TEST",
        Direction   = TradeDirection.Long,
        EntryPrice  = entry,
        EntryType   = OrderType.Limit,
        StopLoss    = stop,
        TakeProfit  = takeProfit,
        Quantity    = qty,
        ConfidenceScore = 75,
    };

    [Fact]
    public void ValidateTrade_NoStopLoss_IsBlocked()
    {
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = new TradeSetup
        {
            Symbol = "TEST", Direction = TradeDirection.Long, EntryPrice = 100m,
            StopLoss = 0m, TakeProfit = 102m, Quantity = 10, ConfidenceScore = 75,
        };

        var verdict = guardian.ValidateTrade(setup);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.BlockReasons, r => r.Contains("NO STOP LOSS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTrade_LongStopAboveEntry_IsBlocked()
    {
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = new TradeSetup
        {
            Symbol = "TEST", Direction = TradeDirection.Long,
            EntryPrice = 100m, StopLoss = 101m /* WRONG SIDE */, TakeProfit = 105m,
            Quantity = 10, ConfidenceScore = 75,
        };

        var verdict = guardian.ValidateTrade(setup);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.BlockReasons, r => r.Contains("LONG stop loss must be BELOW", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTrade_ShortStopBelowEntry_IsBlocked()
    {
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = new TradeSetup
        {
            Symbol = "TEST", Direction = TradeDirection.Short,
            EntryPrice = 100m, StopLoss = 99m /* WRONG SIDE */, TakeProfit = 95m,
            Quantity = 10, ConfidenceScore = 75,
        };

        var verdict = guardian.ValidateTrade(setup);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.BlockReasons, r => r.Contains("SHORT stop loss must be ABOVE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTrade_RiskExceedsMaxLossPerTrade_IsBlocked_AndSuggestsAdjustedQty()
    {
        // $1/share risk × 200 shares = $200 risk; cap is $100 → blocked,
        // suggested adjusted qty = floor(100 / 1) = 100.
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 105m, qty: 200);

        var verdict = guardian.ValidateTrade(setup);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.BlockReasons, r => r.Contains("exceeds max", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(verdict.AdjustedSetup);
        Assert.Equal(100, verdict.AdjustedSetup!.Quantity);
    }

    [Fact]
    public void ValidateTrade_StopTooTight_IsBlocked()
    {
        // $0.10 / $100 = 0.10% — below MinStopLossPercent of 0.5%
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 99.90m, takeProfit: 100.50m, qty: 10);

        var verdict = guardian.ValidateTrade(setup);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.BlockReasons, r => r.Contains("too tight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTrade_StopTooWide_IsBlocked()
    {
        // $10 / $100 = 10% — above MaxStopLossPercent of 5%
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 90m, takeProfit: 130m, qty: 1);

        var verdict = guardian.ValidateTrade(setup);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.BlockReasons, r => r.Contains("too wide", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTrade_AccountRiskTooHigh_IsBlocked()
    {
        // Tiny account, normal trade — risk percent of account exceeds cap.
        // $1/share × 50 shares = $50 risk, account $1,000 → 5% > 1% cap.
        var config = DefaultConfig();
        config.AccountBalance = 1_000m;
        var guardian = new RiskGuardian(config);
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 102m, qty: 50);

        var verdict = guardian.ValidateTrade(setup);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.BlockReasons, r => r.Contains("of account", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTrade_DailyLossAlreadyExceeded_IsBlocked()
    {
        // Push the in-memory daily-loss tracker over the cap, then try to
        // place a trade. RecordTradePnL only counts negative PnL.
        var guardian = new RiskGuardian(DefaultConfig());
        guardian.RecordTradePnL(-450m); // 450 of 500 used
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 102m, qty: 60); // $60 risk → would push over

        var verdict = guardian.ValidateTrade(setup);

        Assert.False(verdict.IsApproved);
        Assert.Contains(verdict.BlockReasons, r => r.Contains("daily loss limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTrade_WellFormedSetup_IsApproved()
    {
        // $1/share × 10 shares = $10 risk (under $100 cap, under 1% of $10k).
        // Stop 1% — within [0.5, 5]. R:R = 2:1 (good). All checks pass.
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 102m, qty: 10);

        var verdict = guardian.ValidateTrade(setup);

        Assert.True(verdict.IsApproved);
        Assert.Empty(verdict.BlockReasons);
    }

    [Fact]
    public void ValidateTrade_LowRiskRewardRatio_ApprovesWithWarning()
    {
        // R:R below 1.5 is a warning only, not a block.
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 100.5m, qty: 10); // R:R = 0.5

        var verdict = guardian.ValidateTrade(setup);

        Assert.True(verdict.IsApproved);
        Assert.Contains(verdict.Warnings, w => w.Contains("R:R", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CalculateMaxQuantity_NoRiskPerShare_ReturnsZero()
    {
        // entry == stop → zero risk per share → caller can't place an order.
        var guardian = new RiskGuardian(DefaultConfig());
        var qty = guardian.CalculateMaxQuantity(entryPrice: 100m, stopLoss: 100m);
        Assert.Equal(0, qty);
    }

    [Fact]
    public void CalculateMaxQuantity_RespectsPerTradeCap()
    {
        // $1/share risk, $100 cap → max 100 shares.
        var guardian = new RiskGuardian(DefaultConfig());
        var qty = guardian.CalculateMaxQuantity(entryPrice: 100m, stopLoss: 99m);
        // The most-restrictive cap also includes account-percent: 1% of $10k = $100,
        // same as MaxLossPerTrade, so the answer is 100.
        Assert.Equal(100, qty);
    }

    [Fact]
    public void CalculateMaxQuantity_RespectsAccountPercent()
    {
        // Tighter account-percent constraint wins: 0.5% of $10k = $50 / $1 = 50 shares.
        var config = DefaultConfig();
        config.MaxAccountRiskPercent = 0.5m;
        var guardian = new RiskGuardian(config);
        var qty = guardian.CalculateMaxQuantity(entryPrice: 100m, stopLoss: 99m);
        Assert.Equal(50, qty);
    }

    [Fact]
    public void CalculateMaxQuantity_RespectsRemainingDailyRisk()
    {
        // Already used $480 of $500 daily → only $20 remaining → 20 shares at $1/share.
        var guardian = new RiskGuardian(DefaultConfig());
        guardian.RecordTradePnL(-480m);
        var qty = guardian.CalculateMaxQuantity(entryPrice: 100m, stopLoss: 99m);
        Assert.Equal(20, qty);
    }
}
