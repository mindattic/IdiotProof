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
    [Test]
    public void RecordTradePnL_AsFirstEverCall_InitializesDailyLossWithoutValidateTrade()
    {
        // RecordTradePnL used to skip the day-rollover check entirely — only
        // ValidateTrade ran it. A Guardian instance whose FIRST interaction
        // of the day is an exit (position held overnight, closed before any
        // new entry is evaluated) must still track the loss correctly rather
        // than silently no-op or crash for lack of prior ValidateTrade setup.
        var guardian = new RiskGuardian(DefaultConfig());

        guardian.RecordTradePnL(-40m);

        Assert.That(guardian.GetRemainingDailyRisk(), Is.EqualTo(460m),
            "a $40 loss recorded with no prior ValidateTrade call must still reduce remaining daily risk from the $500 cap");
    }

    [Test]
    public void RecordTradePnL_ThenValidateTrade_SameDay_ReflectsAccumulatedLoss()
    {
        var guardian = new RiskGuardian(DefaultConfig());

        // LongSetup()'s default risk is $10 (entry 100, stop 99, qty 10);
        // push dailyLoss to $495 so the next $10 of risk crosses the $500 cap.
        guardian.RecordTradePnL(-495m);
        var verdict = guardian.ValidateTrade(LongSetup());

        Assert.That(verdict.BlockReasons, Has.Some.Contains("daily loss limit"),
            "a loss recorded via RecordTradePnL must be visible to the next ValidateTrade call the same day");
    }


    [Test]
    public void UpdateConfig_SwapsLimits_ButPreservesDailyLoss()
    {
        // The Monitor caches one Guardian per user for the process lifetime
        // precisely so dailyLoss survives; UpdateConfig is how it picks up
        // risk-config edits made in the UI. Swapping limits must NOT reset
        // the daily circuit breaker.
        var guardian = new RiskGuardian(DefaultConfig());
        guardian.RecordTradePnL(-300m);

        guardian.UpdateConfig(new RiskGuardianConfig
        {
            MaxLossPerTrade       = 50m,   // tightened in the UI
            MaxLossPerDay         = 400m,  // tightened in the UI
            MinStopLossPercent    = 0.5m,
            MaxStopLossPercent    = 5m,
            AccountBalance        = 10_000m,
            MaxAccountRiskPercent = 1m,
        });

        Assert.Multiple(() =>
        {
            Assert.That(guardian.GetRemainingDailyRisk(), Is.EqualTo(100m),
                "new $400 daily cap minus the PRESERVED $300 daily loss");
            Assert.That(guardian.ValidateTrade(LongSetup(qty: 60)).BlockReasons,
                Has.Some.Contains("exceeds max"),
                "the tightened $50 per-trade limit must apply immediately ($60 risk setup)");
        });
    }

    [Test]
    public void DefaultConfig_AcceptsAWideGapperStop_WithinTheDollarCap()
    {
        // IP-A22: the shipped "Penny Runner" gapper uses an 8% stop; the old
        // default MaxStopLossPercent of 5% silently blocked every fire of that
        // profile out of the box. The default is now 10% — a wide stop is
        // allowed as long as the DOLLAR cap (the binding constraint) holds.
        var guardian = new RiskGuardian(new RiskGuardianConfig()); // pure defaults
        // Entry $2, 8% stop ($1.84), 25 shares → risk 0.16*25 = $4 << $100 cap.
        var setup = new TradeSetup
        {
            Symbol = "PENNY", Direction = TradeDirection.Long,
            EntryPrice = 2.00m, StopLoss = 1.84m, TakeProfit = 2.40m,
            Quantity = 25, ConfidenceScore = 75,
        };
        var verdict = guardian.ValidateTrade(setup);
        Assert.That(verdict.IsApproved, Is.True,
            "an 8% stop within the dollar cap must clear under default limits: " + string.Join("; ", verdict.BlockReasons));
    }

    [Test]
    public void DefaultConfig_StillBlocksARidiculouslyWideStop()
    {
        // The percent guard still exists — a 15% stop exceeds the 10% default.
        var guardian = new RiskGuardian(new RiskGuardianConfig());
        var setup = new TradeSetup
        {
            Symbol = "WILD", Direction = TradeDirection.Long,
            EntryPrice = 10.00m, StopLoss = 8.50m, TakeProfit = 12m, // 15% stop
            Quantity = 1, ConfidenceScore = 75,
        };
        Assert.That(guardian.ValidateTrade(setup).BlockReasons,
            Has.Some.Contains("too wide"));
    }

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

    [Test]
    public void ValidateTrade_NoStopLoss_IsBlocked()
    {
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = new TradeSetup
        {
            Symbol = "TEST", Direction = TradeDirection.Long, EntryPrice = 100m,
            StopLoss = 0m, TakeProfit = 102m, Quantity = 10, ConfidenceScore = 75,
        };

        var verdict = guardian.ValidateTrade(setup);

        Assert.That(verdict.IsApproved, Is.False);
        Assert.That(verdict.BlockReasons, Has.Some.Matches<string>(r => r.Contains("NO STOP LOSS", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
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

        Assert.That(verdict.IsApproved, Is.False);
        Assert.That(verdict.BlockReasons, Has.Some.Matches<string>(r => r.Contains("LONG stop loss must be BELOW", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
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

        Assert.That(verdict.IsApproved, Is.False);
        Assert.That(verdict.BlockReasons, Has.Some.Matches<string>(r => r.Contains("SHORT stop loss must be ABOVE", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void ValidateTrade_RiskExceedsMaxLossPerTrade_IsBlocked_AndSuggestsAdjustedQty()
    {
        // $1/share risk × 200 shares = $200 risk; cap is $100 → blocked,
        // suggested adjusted qty = floor(100 / 1) = 100.
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 105m, qty: 200);

        var verdict = guardian.ValidateTrade(setup);

        Assert.That(verdict.IsApproved, Is.False);
        Assert.That(verdict.BlockReasons, Has.Some.Matches<string>(r => r.Contains("exceeds max", StringComparison.OrdinalIgnoreCase)));
        Assert.That(verdict.AdjustedSetup, Is.Not.Null);
        Assert.That(verdict.AdjustedSetup!.Quantity, Is.EqualTo(100));
    }

    [Test]
    public void ValidateTrade_StopTooTight_IsBlocked()
    {
        // $0.10 / $100 = 0.10% — below MinStopLossPercent of 0.5%
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 99.90m, takeProfit: 100.50m, qty: 10);

        var verdict = guardian.ValidateTrade(setup);

        Assert.That(verdict.IsApproved, Is.False);
        Assert.That(verdict.BlockReasons, Has.Some.Matches<string>(r => r.Contains("too tight", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void ValidateTrade_StopTooWide_IsBlocked()
    {
        // $10 / $100 = 10% — above MaxStopLossPercent of 5%
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 90m, takeProfit: 130m, qty: 1);

        var verdict = guardian.ValidateTrade(setup);

        Assert.That(verdict.IsApproved, Is.False);
        Assert.That(verdict.BlockReasons, Has.Some.Matches<string>(r => r.Contains("too wide", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void ValidateTrade_AccountRiskTooHigh_IsBlocked()
    {
        // Tiny account, normal trade — risk percent of account exceeds cap.
        // $1/share × 50 shares = $50 risk, account $1,000 → 5% > 1% cap.
        var config = DefaultConfig();
        config.AccountBalance = 1_000m;
        var guardian = new RiskGuardian(config);
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 102m, qty: 50);

        var verdict = guardian.ValidateTrade(setup);

        Assert.That(verdict.IsApproved, Is.False);
        Assert.That(verdict.BlockReasons, Has.Some.Matches<string>(r => r.Contains("of account", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void ValidateTrade_DailyLossAlreadyExceeded_IsBlocked()
    {
        // Push the in-memory daily-loss tracker over the cap, then try to
        // place a trade. RecordTradePnL only counts negative PnL.
        var guardian = new RiskGuardian(DefaultConfig());
        guardian.RecordTradePnL(-450m); // 450 of 500 used
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 102m, qty: 60); // $60 risk → would push over

        var verdict = guardian.ValidateTrade(setup);

        Assert.That(verdict.IsApproved, Is.False);
        Assert.That(verdict.BlockReasons, Has.Some.Matches<string>(r => r.Contains("daily loss limit", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void ValidateTrade_WellFormedSetup_IsApproved()
    {
        // $1/share × 10 shares = $10 risk (under $100 cap, under 1% of $10k).
        // Stop 1% — within [0.5, 5]. R:R = 2:1 (good). All checks pass.
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 102m, qty: 10);

        var verdict = guardian.ValidateTrade(setup);

        Assert.That(verdict.IsApproved, Is.True);
        Assert.That(verdict.BlockReasons, Is.Empty);
    }

    [Test]
    public void ValidateTrade_LowRiskRewardRatio_ApprovesWithWarning()
    {
        // R:R below 1.5 is a warning only, not a block.
        var guardian = new RiskGuardian(DefaultConfig());
        var setup = LongSetup(entry: 100m, stop: 99m, takeProfit: 100.5m, qty: 10); // R:R = 0.5

        var verdict = guardian.ValidateTrade(setup);

        Assert.That(verdict.IsApproved, Is.True);
        Assert.That(verdict.Warnings, Has.Some.Matches<string>(w => w.Contains("R:R", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void CalculateMaxQuantity_NoRiskPerShare_ReturnsZero()
    {
        // entry == stop → zero risk per share → caller can't place an order.
        var guardian = new RiskGuardian(DefaultConfig());
        var qty = guardian.CalculateMaxQuantity(entryPrice: 100m, stopLoss: 100m);
        Assert.That(qty, Is.EqualTo(0));
    }

    [Test]
    public void CalculateMaxQuantity_RespectsPerTradeCap()
    {
        // $1/share risk, $100 cap → max 100 shares.
        var guardian = new RiskGuardian(DefaultConfig());
        var qty = guardian.CalculateMaxQuantity(entryPrice: 100m, stopLoss: 99m);
        // The most-restrictive cap also includes account-percent: 1% of $10k = $100,
        // same as MaxLossPerTrade, so the answer is 100.
        Assert.That(qty, Is.EqualTo(100));
    }

    [Test]
    public void CalculateMaxQuantity_RespectsAccountPercent()
    {
        // Tighter account-percent constraint wins: 0.5% of $10k = $50 / $1 = 50 shares.
        var config = DefaultConfig();
        config.MaxAccountRiskPercent = 0.5m;
        var guardian = new RiskGuardian(config);
        var qty = guardian.CalculateMaxQuantity(entryPrice: 100m, stopLoss: 99m);
        Assert.That(qty, Is.EqualTo(50));
    }

    [Test]
    public void CalculateMaxQuantity_RespectsRemainingDailyRisk()
    {
        // Already used $480 of $500 daily → only $20 remaining → 20 shares at $1/share.
        var guardian = new RiskGuardian(DefaultConfig());
        guardian.RecordTradePnL(-480m);
        var qty = guardian.CalculateMaxQuantity(entryPrice: 100m, stopLoss: 99m);
        Assert.That(qty, Is.EqualTo(20));
    }
}
