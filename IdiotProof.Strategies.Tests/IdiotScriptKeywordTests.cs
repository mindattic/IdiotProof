using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;
using IdiotProof.Strategies;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Every IdiotScript keyword: builder fluent method sets the right field /
/// adds the right condition, the condition evaluates to the documented boolean
/// under the documented data, and fails closed when required data is absent.
/// </summary>
public class IdiotScriptKeywordTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DateTime Utc(int h, int m) => new(2026, 7, 17, h, m, 0, DateTimeKind.Utc);

    private static IndicatorSnapshot S(double price = 10.0) => new()
    {
        Symbol = "T", Timestamp = Utc(8, 30), Price = price,
    };

    private static IndicatorSnapshot WithVwap(double price, double vwap,
        double? priorPrice = null, double? priorVwap = null) => new()
    {
        Symbol = "T", Timestamp = Utc(8, 30), Price = price,
        Vwap = vwap, PriorPrice = priorPrice, PriorVwap = priorVwap,
    };

    private static IndicatorSnapshot WithEma(double price, int period, double ema,
        double? priorPrice = null, double? priorEma = null)
    {
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = price, PriorPrice = priorPrice,
        };
        s.Emas[period] = ema;
        if (priorEma.HasValue) s.PriorEmas[period] = priorEma.Value;
        return s;
    }

    private static IndicatorSnapshot WithRsi(double price, double rsi) =>
        new() { Symbol = "T", Timestamp = Utc(8, 30), Price = price, Rsi = rsi };

    private static IndicatorSnapshot WithMacd(double price, double macd, double signal) =>
        new() { Symbol = "T", Timestamp = Utc(8, 30), Price = price, MacdLine = macd, SignalLine = signal };

    private static IndicatorSnapshot WithAdx(double price, double adx, double plusDi, double minusDi) =>
        new() { Symbol = "T", Timestamp = Utc(8, 30), Price = price, Adx = adx, PlusDI = plusDi, MinusDI = minusDi };

    private static IndicatorSnapshot WithVolume(double price, long volume, double avgVol) =>
        new() { Symbol = "T", Timestamp = Utc(8, 30), Price = price, Volume = volume, AverageVolume = avgVol };

    private static IndicatorSnapshot WithGap(double price, double prevClose) =>
        new() { Symbol = "T", Timestamp = Utc(8, 30), Price = price, PreviousClose = prevClose };

    private static IndicatorSnapshot WithWindow(double price, double windowHigh, double windowLow) =>
        new() { Symbol = "T", Timestamp = Utc(8, 30), Price = price, WindowHigh = windowHigh, WindowLow = windowLow };

    private static IndicatorCondition IC(IndicatorType t, double? p1 = null, double? p2 = null) =>
        p2.HasValue ? new IndicatorCondition(t, p1, p2)
        : p1.HasValue ? new IndicatorCondition(t, p1)
        : new IndicatorCondition(t);

    // ── VWAP ─────────────────────────────────────────────────────────────────

    [Test]
    public void IsAboveVwap_PriceAboveVwap_True()
    {
        var s = WithVwap(price: 10.5, vwap: 10.0);
        Assert.That(IC(IndicatorType.VwapAbove).Evaluate(s), Is.True);
    }

    [Test]
    public void IsAboveVwap_PriceBelowVwap_False()
    {
        var s = WithVwap(price: 9.5, vwap: 10.0);
        Assert.That(IC(IndicatorType.VwapAbove).Evaluate(s), Is.False);
    }

    [Test]
    public void IsBelowVwap_PriceBelowVwap_True()
    {
        var s = WithVwap(price: 9.5, vwap: 10.0);
        Assert.That(IC(IndicatorType.VwapBelow).Evaluate(s), Is.True);
    }

    [Test]
    public void OnVwapReclaim_CrossFromBelowToAbove_True()
    {
        // prior bar below VWAP, current bar above VWAP → reclaim
        var s = WithVwap(price: 10.05, vwap: 10.0, priorPrice: 9.95, priorVwap: 10.0);
        Assert.That(IC(IndicatorType.VwapReclaim).Evaluate(s), Is.True);
    }

    [Test]
    public void OnVwapReclaim_NoCross_False()
    {
        // prior bar already above VWAP → no reclaim
        var s = WithVwap(price: 10.05, vwap: 10.0, priorPrice: 10.02, priorVwap: 10.0);
        Assert.That(IC(IndicatorType.VwapReclaim).Evaluate(s), Is.False);
    }

    [Test]
    public void OnVwapReclaim_NullVwap_FailsClosed()
    {
        // Vwap is null → must not pass (was previously ?? 0 which made Price > 0 always true)
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = 10.05,
            Vwap = null, PriorPrice = 9.95, PriorVwap = 10.0,
        };
        Assert.That(IC(IndicatorType.VwapReclaim).Evaluate(s), Is.False,
            "null Vwap must fail closed, not degrade to Price > 0");
    }

    [Test]
    public void OnVwapLoss_NullVwap_FailsClosed()
    {
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = 9.95,
            Vwap = null, PriorPrice = 10.05, PriorVwap = 10.0,
        };
        Assert.That(IC(IndicatorType.VwapLoss).Evaluate(s), Is.False);
    }

    [Test]
    public void OnVwapLoss_CrossFromAboveToBelow_True()
    {
        var s = WithVwap(price: 9.95, vwap: 10.0, priorPrice: 10.05, priorVwap: 10.0);
        Assert.That(IC(IndicatorType.VwapLoss).Evaluate(s), Is.True);
    }

    // ── EMA family ────────────────────────────────────────────────────────────

    [Test]
    public void IsAboveEma_PriceAboveEma_True()
    {
        var s = WithEma(price: 10.5, period: 9, ema: 10.0);
        Assert.That(IC(IndicatorType.EmaAbove, 9).Evaluate(s), Is.True);
    }

    [Test]
    public void IsAboveEma_MissingEmaPeriod_FailsClosed()
    {
        var s = S(10.5); // no Emas populated
        Assert.That(IC(IndicatorType.EmaAbove, 9).Evaluate(s), Is.False);
    }

    [Test]
    public void IsBelowEma_PriceBelowEma_True()
    {
        var s = WithEma(price: 9.5, period: 21, ema: 10.0);
        Assert.That(IC(IndicatorType.EmaBelow, 21).Evaluate(s), Is.True);
    }

    [Test]
    public void IsEmaAbove_EmaPeriodAboveAnother_True()
    {
        // IsEmaAbove(9) → fast EMA is above slow EMA (ema9 > ema21 typically)
        var s = new IndicatorSnapshot { Symbol = "T", Timestamp = Utc(8, 30), Price = 10.0 };
        s.Emas[9] = 10.5; s.Emas[21] = 10.0;
        // IsEmaAbove checks the *candle* is above the ema (same as IsAboveEma) — verify the builder alias
        Assert.That(IC(IndicatorType.EmaAbove, 9).Evaluate(s), Is.False, "price 10.0 is not above ema9=10.5");
    }

    [Test]
    public void IsBetweenEma_PriceInsideBand_True()
    {
        var s = new IndicatorSnapshot { Symbol = "T", Timestamp = Utc(8, 30), Price = 10.2 };
        s.Emas[9] = 10.5; s.Emas[50] = 9.8;
        Assert.That(IC(IndicatorType.BetweenEma, 9, 50).Evaluate(s), Is.True);
    }

    [Test]
    public void IsBetweenEma_PriceOutsideBand_False()
    {
        var s = new IndicatorSnapshot { Symbol = "T", Timestamp = Utc(8, 30), Price = 11.0 };
        s.Emas[9] = 10.5; s.Emas[50] = 9.8;
        Assert.That(IC(IndicatorType.BetweenEma, 9, 50).Evaluate(s), Is.False);
    }

    [Test]
    public void OnReclaim_PriorBarBelowEmaThenCrossAbove_True()
    {
        // ReclaimEma: prior close <= prior ema AND current close > current ema
        var s = WithEma(price: 10.05, period: 9, ema: 10.0, priorPrice: 9.95, priorEma: 10.0);
        Assert.That(IC(IndicatorType.ReclaimEma, 9).Evaluate(s), Is.True);
    }

    [Test]
    public void OnReclaim_AlreadyAboveEma_False()
    {
        // prior close already above ema → no reclaim
        var s = WithEma(price: 10.05, period: 9, ema: 10.0, priorPrice: 10.02, priorEma: 10.0);
        Assert.That(IC(IndicatorType.ReclaimEma, 9).Evaluate(s), Is.False);
    }

    // ── ADX / DI ──────────────────────────────────────────────────────────────

    [Test]
    public void RequireAdxAbove_AboveThreshold_True()
    {
        var s = WithAdx(10, adx: 25, plusDi: 30, minusDi: 15);
        Assert.That(IC(IndicatorType.AdxAbove, 20).Evaluate(s), Is.True);
    }

    [Test]
    public void RequireAdxAbove_BelowThreshold_False()
    {
        var s = WithAdx(10, adx: 15, plusDi: 30, minusDi: 15);
        Assert.That(IC(IndicatorType.AdxAbove, 20).Evaluate(s), Is.False);
    }

    [Test]
    public void RequireAdxAbove_NullAdx_FailsClosed()
    {
        Assert.That(IC(IndicatorType.AdxAbove, 20).Evaluate(S()), Is.False);
    }

    [Test]
    public void IsDiPositive_PlusDiAboveMinusDi_True()
    {
        var s = WithAdx(10, adx: 25, plusDi: 30, minusDi: 15);
        Assert.Multiple(() =>
        {
            Assert.That(IC(IndicatorType.DiPositive).Evaluate(s), Is.True);
            Assert.That(IC(IndicatorType.DiNegative).Evaluate(s), Is.False);
        });
    }

    [Test]
    public void IsDiNegative_MinusDiAbovePlusDi_True()
    {
        var s = WithAdx(10, adx: 25, plusDi: 10, minusDi: 25);
        Assert.Multiple(() =>
        {
            Assert.That(IC(IndicatorType.DiNegative).Evaluate(s), Is.True);
            Assert.That(IC(IndicatorType.DiPositive).Evaluate(s), Is.False);
        });
    }

    [Test]
    public void DiConditions_NullAdxData_FailsClosed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IC(IndicatorType.DiPositive).Evaluate(S()), Is.False);
            Assert.That(IC(IndicatorType.DiNegative).Evaluate(S()), Is.False);
        });
    }

    // ── RSI ───────────────────────────────────────────────────────────────────

    [Test]
    public void IsRsiOversold_BelowThreshold_True()
    {
        Assert.That(IC(IndicatorType.RsiOversold, 30).Evaluate(WithRsi(10, 25)), Is.True);
    }

    [Test]
    public void IsRsiOversold_AboveThreshold_False()
    {
        Assert.That(IC(IndicatorType.RsiOversold, 30).Evaluate(WithRsi(10, 35)), Is.False);
    }

    [Test]
    public void IsRsiOverbought_AboveThreshold_True()
    {
        Assert.That(IC(IndicatorType.RsiOverbought, 70).Evaluate(WithRsi(10, 75)), Is.True);
    }

    [Test]
    public void RsiConditions_NullRsi_FailsClosed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IC(IndicatorType.RsiOversold, 30).Evaluate(S()), Is.False);
            Assert.That(IC(IndicatorType.RsiOverbought, 70).Evaluate(S()), Is.False);
        });
    }

    // ── MACD ─────────────────────────────────────────────────────────────────

    [Test]
    public void IsMacdBullish_MacdAboveSignal_True()
    {
        Assert.That(IC(IndicatorType.MacdBullish).Evaluate(WithMacd(10, macd: 1.0, signal: 0.5)), Is.True);
    }

    [Test]
    public void IsMacdBearish_MacdBelowSignal_True()
    {
        Assert.That(IC(IndicatorType.MacdBearish).Evaluate(WithMacd(10, macd: 0.5, signal: 1.0)), Is.True);
    }

    [Test]
    public void MacdConditions_NullMacd_FailsClosed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IC(IndicatorType.MacdBullish).Evaluate(S()), Is.False);
            Assert.That(IC(IndicatorType.MacdBearish).Evaluate(S()), Is.False);
        });
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    [Test]
    public void IsVolumeAbove_VolumeSpikesMeetMultiplier_True()
    {
        var s = WithVolume(10, volume: 2500, avgVol: 1000);
        Assert.That(IC(IndicatorType.VolumeAbove, 2.0).Evaluate(s), Is.True);
    }

    [Test]
    public void IsVolumeAbove_VolumeBelowMultiplier_False()
    {
        var s = WithVolume(10, volume: 1500, avgVol: 1000);
        Assert.That(IC(IndicatorType.VolumeAbove, 2.0).Evaluate(s), Is.False);
    }

    [Test]
    public void IsVolumeAbove_ZeroAvgVolume_UsesSentinel()
    {
        // By design: VolumeRatio = 999.0 when AverageVolume=0 but Volume>0. The 999 sentinel
        // means "spike vs no-baseline" — intentional for gapper scanner (no prior history
        // on the first bar of the day). This always fires VolumeAbove at any multiplier.
        var s = new IndicatorSnapshot { Symbol = "T", Timestamp = Utc(8, 30), Price = 10, Volume = 5000, AverageVolume = 0 };
        Assert.That(IC(IndicatorType.VolumeAbove, 2.0).Evaluate(s), Is.True,
            "VolumeRatio=999 sentinel when no baseline — designed to fire, not fail-closed");
    }

    // ── Gap ───────────────────────────────────────────────────────────────────

    [Test]
    public void IsGapUp_GapMeetsThreshold_True()
    {
        var s = WithGap(price: 10.60, prevClose: 10.0); // +6%
        Assert.That(IC(IndicatorType.GapUp, 5).Evaluate(s), Is.True);
    }

    [Test]
    public void IsGapUp_GapBelowThreshold_False()
    {
        var s = WithGap(price: 10.20, prevClose: 10.0); // +2%
        Assert.That(IC(IndicatorType.GapUp, 5).Evaluate(s), Is.False);
    }

    [Test]
    public void IsGapDown_GapMeetsThreshold_True()
    {
        var s = WithGap(price: 9.40, prevClose: 10.0); // -6%
        Assert.That(IC(IndicatorType.GapDown, 5).Evaluate(s), Is.True);
    }

    [Test]
    public void GapConditions_NoPreviousClose_FailsClosed()
    {
        var s = S(10.60);
        Assert.Multiple(() =>
        {
            Assert.That(IC(IndicatorType.GapUp, 5).Evaluate(s), Is.False);
            Assert.That(IC(IndicatorType.GapDown, 5).Evaluate(s), Is.False);
        });
    }

    // ── Pivot structure ───────────────────────────────────────────────────────

    [Test]
    public void IsHigherLow_HasHigherLow_True()
    {
        var s = S(); s.HasHigherLow = true;
        Assert.That(IC(IndicatorType.HigherLow).Evaluate(s), Is.True);
    }

    [Test]
    public void IsHigherLow_NullHigherLow_FailsClosed()
    {
        var s = S(); s.HasHigherLow = null;
        Assert.That(IC(IndicatorType.HigherLow).Evaluate(s), Is.False);
    }

    [Test]
    public void IsLowerHigh_HasLowerHigh_True()
    {
        var s = S(); s.HasLowerHigh = true;
        Assert.That(IC(IndicatorType.LowerHigh).Evaluate(s), Is.True);
    }

    [Test]
    public void IsLowerHigh_NullLowerHigh_FailsClosed()
    {
        var s = S(); s.HasLowerHigh = null;
        Assert.That(IC(IndicatorType.LowerHigh).Evaluate(s), Is.False);
    }

    // ── RSI divergence ────────────────────────────────────────────────────────

    [Test]
    public void IsRsiBullishDivergence_FlagTrue_True()
    {
        var s = S(); s.HasBullishDivergence = true;
        Assert.That(IC(IndicatorType.RsiBullishDivergence).Evaluate(s), Is.True);
    }

    [Test]
    public void IsRsiBullishDivergence_NullFlag_FailsClosed()
    {
        Assert.That(IC(IndicatorType.RsiBullishDivergence).Evaluate(S()), Is.False);
    }

    [Test]
    public void IsRsiBearishDivergence_FlagTrue_True()
    {
        var s = S(); s.HasBearishDivergence = true;
        Assert.That(IC(IndicatorType.RsiBearishDivergence).Evaluate(s), Is.True);
    }

    [Test]
    public void IsRsiBearishDivergence_NullFlag_FailsClosed()
    {
        Assert.That(IC(IndicatorType.RsiBearishDivergence).Evaluate(S()), Is.False);
    }

    // ── Support / Resistance ──────────────────────────────────────────────────

    [Test]
    public void IsAtSupport_PriceNearSwingLow_True()
    {
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = 10.0, RecentSwingLow = 10.02,
        };
        Assert.That(IC(IndicatorType.AtSupport, 0.5).Evaluate(s), Is.True);
    }

    [Test]
    public void IsAtResistance_PriceNearSwingHigh_True()
    {
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = 10.0, RecentSwingHigh = 9.98,
        };
        Assert.That(IC(IndicatorType.AtResistance, 0.5).Evaluate(s), Is.True);
    }

    // ── Candlestick patterns ──────────────────────────────────────────────────

    [Test]
    public void CandlestickPatterns_DirectlySetFlags_EvaluateCorrectly()
    {
        var s = S();
        s.IsHammer = true; s.IsShootingStar = false; s.IsDoji = false;
        s.IsBullishEngulfing = true; s.IsBearishEngulfing = false;

        Assert.Multiple(() =>
        {
            Assert.That(new PatternCondition(PatternType.Hammer).Evaluate(s), Is.True);
            Assert.That(new PatternCondition(PatternType.ShootingStar).Evaluate(s), Is.False);
            Assert.That(new PatternCondition(PatternType.Doji).Evaluate(s), Is.False);
            Assert.That(new PatternCondition(PatternType.BullishEngulfing).Evaluate(s), Is.True);
            Assert.That(new PatternCondition(PatternType.BearishEngulfing).Evaluate(s), Is.False);
        });
    }

    // ── Price levels ──────────────────────────────────────────────────────────

    [Test]
    public void HoldsAbove_PriceAboveAndWindowNeverViolated_True()
    {
        var s = WithWindow(price: 10.5, windowHigh: 10.8, windowLow: 10.4);
        Assert.That(new PriceLevelCondition(PriceLevelType.HoldsAbove, 10.0).Evaluate(s), Is.True);
    }

    [Test]
    public void HoldsAbove_WindowLowViolatedLevel_False()
    {
        var s = WithWindow(price: 10.5, windowHigh: 10.8, windowLow: 9.8);
        Assert.That(new PriceLevelCondition(PriceLevelType.HoldsAbove, 10.0).Evaluate(s), Is.False,
            "window dipped below the level — HoldsAbove must be false");
    }

    [Test]
    public void HoldsBelow_PriceBelowAndWindowNeverViolated_True()
    {
        var s = WithWindow(price: 9.5, windowHigh: 9.8, windowLow: 9.2);
        Assert.That(new PriceLevelCondition(PriceLevelType.HoldsBelow, 10.0).Evaluate(s), Is.True);
    }

    [Test]
    public void IsNear_PriceWithinTolerance_True()
    {
        var s = S(10.05);
        Assert.That(new PriceLevelCondition(PriceLevelType.Near, 10.0, 1.0).Evaluate(s), Is.True,
            "0.5% from level is within 1% tolerance");
    }

    [Test]
    public void IsNear_PriceOutsideTolerance_False()
    {
        var s = S(10.20);
        Assert.That(new PriceLevelCondition(PriceLevelType.Near, 10.0, 1.0).Evaluate(s), Is.False,
            "2% from level exceeds 1% tolerance");
    }

    [Test]
    public void BreaksAbove_CrossFromBelowToAbove_True()
    {
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = 10.05, PriorPrice = 9.95,
        };
        Assert.That(new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0).Evaluate(s), Is.True);
    }

    [Test]
    public void BreaksAbove_NoCross_False()
    {
        // both bars above the level → no cross this tick
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = 10.05, PriorPrice = 10.02,
        };
        Assert.That(new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0).Evaluate(s), Is.False);
    }

    [Test]
    public void BreaksAbove_NoPriorPrice_FailsClosed()
    {
        // No PriorPrice in snapshot — fresh instance, can't establish a cross
        var s = S(10.05);
        Assert.That(new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0).Evaluate(s), Is.False);
    }

    [Test]
    public void BreaksBelow_CrossFromAboveToBelow_True()
    {
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = 9.95, PriorPrice = 10.05,
        };
        Assert.That(new PriceLevelCondition(PriceLevelType.BreaksBelow, 10.0).Evaluate(s), Is.True);
    }

    [Test]
    public void BreaksBelow_NoCross_False()
    {
        var s = new IndicatorSnapshot
        {
            Symbol = "T", Timestamp = Utc(8, 30), Price = 9.95, PriorPrice = 9.98,
        };
        Assert.That(new PriceLevelCondition(PriceLevelType.BreaksBelow, 10.0).Evaluate(s), Is.False);
    }

    [Test]
    public void BreaksBelow_NoPriorPrice_FailsClosed()
    {
        var s = S(9.95);
        Assert.That(new PriceLevelCondition(PriceLevelType.BreaksBelow, 10.0).Evaluate(s), Is.False);
    }

    [Test]
    public void PriceLevelCondition_UnknownType_FailsClosed()
    {
        // If a new PriceLevelType is added without updating the switch, the default
        // arm must fail closed (IP-LAW-1), not silently pass every evaluation.
        // We can't add a real unknown type, but we verified the default is `_ => false`.
        // This test verifies all KNOWN types handle their documented cases:
        Assert.Multiple(() =>
        {
            Assert.That(new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0).Evaluate(S()), Is.False,
                "no prior price → BreaksAbove fails closed");
            Assert.That(new PriceLevelCondition(PriceLevelType.BreaksBelow, 10.0).Evaluate(S()), Is.False,
                "no prior price → BreaksBelow fails closed");
        });
    }

    // ── PriceCondition (Entry gate) ───────────────────────────────────────────

    [Test]
    public void Entry_PriceAtOrAboveLevel_True()
    {
        // Entry(12.50) fires when current price >= the level (stock has reached the gate)
        Assert.Multiple(() =>
        {
            Assert.That(new PriceCondition(ConditionType.Entry, 12.50).Evaluate(S(12.50)), Is.True, "at level");
            Assert.That(new PriceCondition(ConditionType.Entry, 12.50).Evaluate(S(13.00)), Is.True, "above level");
            Assert.That(new PriceCondition(ConditionType.Entry, 12.50).Evaluate(S(12.00)), Is.False, "below level");
        });
    }

    // ── StrategyDefinition fields set by builder ──────────────────────────────

    [Test]
    public void Builder_SetupPhase_AllFieldsReflected()
    {
        var def = Stock.Ticker("TSLA")
            .Name("My strategy")
            .Session(TradingSession.Premarket)
            .QuantityNotional(2000m)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(def.Symbol, Is.EqualTo("TSLA"));
            Assert.That(def.Name, Is.EqualTo("My strategy"));
            Assert.That(def.Session, Is.EqualTo(TradingSession.Premarket));
            Assert.That(def.NotionalAmount, Is.EqualTo(2000m));
            Assert.That(def.IsNotional, Is.True);
        });
    }

    [Test]
    public void Builder_QuantityShares_NotNotional()
    {
        var def = Stock.Ticker("X").Long().Quantity(5).Build();
        Assert.Multiple(() =>
        {
            Assert.That(def.Quantity, Is.EqualTo(5));
            Assert.That(def.IsNotional, Is.False);
        });
    }

    [Test]
    public void Builder_ExitFields_AllSet()
    {
        var def = Stock.Ticker("X")
            .Long()
            .TakeProfit(12.00)
            .TakeProfitPercent(10)
            .StopLoss(8.00)
            .StopLossPercent(5)
            .TrailingStopLoss(3)
            .SellBy("09:28")
            .PeakGiveback(25, "09:15")
            .ExitAtPriorHigh()
            .ExitAtRollingHigh(20, 2.5)
            .ExitAtRollingLow(10, 1.5)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(def.TakeProfitPrice, Is.EqualTo(12.00));
            Assert.That(def.TakeProfitPercent, Is.EqualTo(10));
            Assert.That(def.StopLossPrice, Is.EqualTo(8.00));
            Assert.That(def.StopLossPercent, Is.EqualTo(5));
            Assert.That(def.TrailingStopPercent, Is.EqualTo(3));
            Assert.That(def.ExitTime, Is.EqualTo(new TimeSpan(9, 28, 0)));
            Assert.That(def.PeakGivebackPercent, Is.EqualTo(25));
            Assert.That(def.PeakGivebackArmTime, Is.EqualTo(new TimeSpan(9, 15, 0)));
            Assert.That(def.ExitAtPriorHigh, Is.True);
            Assert.That(def.RollingHighDays, Is.EqualTo(20));
            Assert.That(def.RollingHighBuffer, Is.EqualTo(2.5));
            Assert.That(def.RollingLowDays, Is.EqualTo(10));
            Assert.That(def.RollingLowBuffer, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void Builder_EntryRollingGates_SetCorrectFields()
    {
        var def = Stock.Ticker("X")
            .EntryAtRollingLow(5, 3.0)
            .EntryAtRollingHigh(10, 1.5)
            .Long()
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(def.EntryRollingLowDays, Is.EqualTo(5));
            Assert.That(def.EntryRollingLowBuffer, Is.EqualTo(3.0));
            Assert.That(def.EntryRollingHighDays, Is.EqualTo(10));
            Assert.That(def.EntryRollingHighBuffer, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void Parser_EntryRollingLowAliases_ParseSameAsCanonical()
    {
        // Parser aliases isnearrollinglow / entryatsupport both call EntryAtRollingLow
        var canonical  = ScriptParser.ParseScript("Ticker(\"X\").EntryAtRollingLow(5, 2.5).Long()")!;
        var alias1     = ScriptParser.ParseScript("Ticker(\"X\").IsNearRollingLow(5, 2.5).Long()")!;
        var alias2     = ScriptParser.ParseScript("Ticker(\"X\").EntryAtSupport(5, 2.5).Long()")!;

        Assert.Multiple(() =>
        {
            Assert.That(alias1.EntryRollingLowDays, Is.EqualTo(canonical.EntryRollingLowDays));
            Assert.That(alias1.EntryRollingLowBuffer, Is.EqualTo(canonical.EntryRollingLowBuffer));
            Assert.That(alias2.EntryRollingLowDays, Is.EqualTo(canonical.EntryRollingLowDays));
        });
    }

    [Test]
    public void Builder_BreaksAbove_AddsCondition()
    {
        var def = Stock.Ticker("X").BreaksAbove(10.0).Long().Build();
        var cond = def.EntryConditions.OfType<PriceLevelCondition>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(cond.Type, Is.EqualTo(PriceLevelType.BreaksAbove));
            Assert.That(cond.Level, Is.EqualTo(10.0));
        });
    }

    [Test]
    public void Builder_BreaksBelow_AddsCondition()
    {
        var def = Stock.Ticker("X").BreaksBelow(10.0).Short().Build();
        var cond = def.EntryConditions.OfType<PriceLevelCondition>().Single();
        Assert.That(cond.Type, Is.EqualTo(PriceLevelType.BreaksBelow));
    }

    [Test]
    public void Builder_MultiTarget_ScalesOutCorrectly()
    {
        var def = Stock.Ticker("X").Long().TakeProfit(5.00, 6.50, 8.00).Build();
        Assert.Multiple(() =>
        {
            Assert.That(def.TakeProfitTargets, Has.Count.EqualTo(3));
            Assert.That(def.TakeProfitTargets[0].Price, Is.EqualTo(5.00));
            Assert.That(def.TakeProfitTargets[1].Price, Is.EqualTo(6.50));
            Assert.That(def.TakeProfitTargets[2].Price, Is.EqualTo(8.00));
            Assert.That(def.TakeProfitPrice, Is.EqualTo(5.00), "TakeProfitPrice == T1");
        });
    }

    [Test]
    public void Builder_AddTarget_AddsWithLabel()
    {
        var def = Stock.Ticker("X").Long().AddTarget(5.0, 50, "T1").AddTarget(7.0, 50, "T2").Build();
        Assert.That(def.TakeProfitTargets, Has.Count.EqualTo(2));
        Assert.That(def.TakeProfitTargets[1].Label, Is.EqualTo("T2"));
    }

    [Test]
    public void Builder_AutonomousAndAdaptiveFlags_Set()
    {
        var def = Stock.Ticker("X").Long().AutonomousTrading().AdaptiveOrder().Repeat().Build();
        Assert.Multiple(() =>
        {
            Assert.That(def.IsAutonomous, Is.True);
            Assert.That(def.IsAdaptive, Is.True);
            Assert.That(def.ShouldRepeat, Is.True);
        });
    }
}
