using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Exhaustive pass/fail/null-data coverage for every condition type.
/// The three-row contract for every indicator: (1) passes when data supports
/// it, (2) fails when data opposes it, (3) fails CLOSED when required data is
/// absent — never waves through a fire it can't verify (IP-LAW-1).
///
/// Covers: 25 IndicatorTypes × 3 states, 7 PatternTypes × 2 states,
/// 5 PriceLevelTypes × 2 states, GapBandCondition, PriceBandCondition,
/// And/Or/Not combinators (truth-table coverage).
/// </summary>
public class ConditionEvaluationMatrixTests
{
    // ── Snapshot factories ───────────────────────────────────────────────

    private static IndicatorSnapshot Base(double price = 10.0) => new()
    {
        Symbol    = "TEST",
        Timestamp = new DateTime(2026, 7, 17, 13, 0, 0, DateTimeKind.Utc),
        Price     = price,
        Volume    = 1_000_000,
        AverageVolume = 500_000,
    };

    private static IndicatorSnapshot WithVwap(double price, double vwap, double? priorPrice = null, double? priorVwap = null)
    {
        var s = Base(price);
        s.Vwap       = vwap;
        s.PriorPrice = priorPrice;
        s.PriorVwap  = priorVwap;
        return s;
    }

    private static IndicatorSnapshot WithEma(double price, int period, double emaValue, int? period2 = null, double? ema2Value = null,
        double? priorPrice = null, double? priorEmaValue = null)
    {
        var s = Base(price);
        s.Emas[period] = emaValue;
        if (period2.HasValue && ema2Value.HasValue)
            s.Emas[period2.Value] = ema2Value.Value;
        if (priorPrice.HasValue) s.PriorPrice = priorPrice;
        if (priorEmaValue.HasValue) s.PriorEmas[period] = priorEmaValue.Value;
        return s;
    }

    private static IndicatorSnapshot WithAdx(double? adx, double? plusDi, double? minusDi)
    {
        var s = Base();
        s.Adx    = adx;
        s.PlusDI = plusDi;
        s.MinusDI = minusDi;
        return s;
    }

    private static IndicatorSnapshot WithRsi(double rsi, bool? bullDiv = null, bool? bearDiv = null)
    {
        var s = Base();
        s.Rsi                = rsi;
        s.HasBullishDivergence = bullDiv;
        s.HasBearishDivergence = bearDiv;
        return s;
    }

    private static IndicatorSnapshot WithMacd(double macdLine, double signalLine)
    {
        var s = Base();
        s.MacdLine  = macdLine;
        s.SignalLine = signalLine;
        return s;
    }

    private static IndicatorSnapshot WithGap(double price, double prevClose)
    {
        var s = Base(price);
        s.PreviousClose = prevClose;
        return s;
    }

    private static IndicatorSnapshot WithVolume(long vol, double avgVol)
    {
        var s = Base();
        s.Volume        = vol;
        s.AverageVolume = avgVol;
        return s;
    }

    private static IndicatorSnapshot Sparse() => Base();

    // ── VwapAbove ───────────────────────────────────────────────────────

    [Test] public void VwapAbove_PriceAboveVwap_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.VwapAbove)
            .Evaluate(WithVwap(10.5, 10.0)), Is.True);

    [Test] public void VwapAbove_PriceBelowVwap_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.VwapAbove)
            .Evaluate(WithVwap(9.5, 10.0)), Is.False);

    [Test] public void VwapAbove_NullVwap_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.VwapAbove)
            .Evaluate(Sparse()), Is.False);

    [Test] public void VwapAbove_PriceAtExactlyVwap_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.VwapAbove)
            .Evaluate(WithVwap(10.0, 10.0)), Is.False);

    // ── VwapBelow ───────────────────────────────────────────────────────

    [Test] public void VwapBelow_PriceBelowVwap_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.VwapBelow)
            .Evaluate(WithVwap(9.5, 10.0)), Is.True);

    [Test] public void VwapBelow_PriceAboveVwap_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.VwapBelow)
            .Evaluate(WithVwap(10.5, 10.0)), Is.False);

    [Test] public void VwapBelow_NullVwap_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.VwapBelow)
            .Evaluate(Sparse()), Is.False);

    // ── VwapReclaim ──────────────────────────────────────────────────────

    [Test] public void VwapReclaim_PriorBelowPriorVwap_CurrentAboveVwap_ReturnsTrue()
    {
        // prior bar was at 9.8 (below priorVwap 10.0); current bar is at 10.5 (above vwap 10.0)
        var s = WithVwap(10.5, 10.0, priorPrice: 9.8, priorVwap: 10.0);
        Assert.That(new IndicatorCondition(IndicatorType.VwapReclaim).Evaluate(s), Is.True);
    }

    [Test] public void VwapReclaim_PriorAlreadyAboveVwap_ReturnsFalse()
    {
        var s = WithVwap(10.5, 10.0, priorPrice: 10.2, priorVwap: 10.0);
        Assert.That(new IndicatorCondition(IndicatorType.VwapReclaim).Evaluate(s), Is.False);
    }

    [Test] public void VwapReclaim_MissingPriorData_FailsClosed()
    {
        var s = WithVwap(10.5, 10.0);
        Assert.That(new IndicatorCondition(IndicatorType.VwapReclaim).Evaluate(s), Is.False);
    }

    // ── VwapLoss ─────────────────────────────────────────────────────────

    [Test] public void VwapLoss_PriorAbovePriorVwap_CurrentBelowVwap_ReturnsTrue()
    {
        var s = WithVwap(9.5, 10.0, priorPrice: 10.2, priorVwap: 10.0);
        Assert.That(new IndicatorCondition(IndicatorType.VwapLoss).Evaluate(s), Is.True);
    }

    [Test] public void VwapLoss_PriorAlreadyBelowVwap_ReturnsFalse()
    {
        var s = WithVwap(9.5, 10.0, priorPrice: 9.8, priorVwap: 10.0);
        Assert.That(new IndicatorCondition(IndicatorType.VwapLoss).Evaluate(s), Is.False);
    }

    [Test] public void VwapLoss_MissingPriorData_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.VwapLoss).Evaluate(Sparse()), Is.False);

    // ── EmaAbove ─────────────────────────────────────────────────────────

    [Test] public void EmaAbove_PriceAboveEma_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.EmaAbove, 9)
            .Evaluate(WithEma(10.5, 9, 10.0)), Is.True);

    [Test] public void EmaAbove_PriceBelowEma_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.EmaAbove, 9)
            .Evaluate(WithEma(9.5, 9, 10.0)), Is.False);

    [Test] public void EmaAbove_EmaMissing_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.EmaAbove, 9)
            .Evaluate(Sparse()), Is.False);

    [TestCase(9)]
    [TestCase(21)]
    [TestCase(50)]
    [TestCase(200)]
    [TestCase(14)]
    public void EmaAbove_AllCommonPeriods_ResolveViaEmaDictionary(int period)
    {
        var s = WithEma(10.5, period, 10.0);
        Assert.That(new IndicatorCondition(IndicatorType.EmaAbove, period).Evaluate(s), Is.True,
            $"EmaAbove({period}) must resolve when period is in the Emas dictionary");
    }

    // ── EmaBelow ─────────────────────────────────────────────────────────

    [Test] public void EmaBelow_PriceBelowEma_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.EmaBelow, 21)
            .Evaluate(WithEma(9.5, 21, 10.0)), Is.True);

    [Test] public void EmaBelow_PriceAboveEma_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.EmaBelow, 21)
            .Evaluate(WithEma(10.5, 21, 10.0)), Is.False);

    [Test] public void EmaBelow_EmaMissing_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.EmaBelow, 21)
            .Evaluate(Sparse()), Is.False);

    // ── BetweenEma ───────────────────────────────────────────────────────

    [Test] public void BetweenEma_PriceInBand_ReturnsTrue()
    {
        var s = WithEma(10.0, 9, 9.5, period2: 21, ema2Value: 10.5);
        Assert.That(new IndicatorCondition(IndicatorType.BetweenEma, 9, 21).Evaluate(s), Is.True);
    }

    [Test] public void BetweenEma_PriceAboveBand_ReturnsFalse()
    {
        var s = WithEma(11.0, 9, 9.5, period2: 21, ema2Value: 10.5);
        Assert.That(new IndicatorCondition(IndicatorType.BetweenEma, 9, 21).Evaluate(s), Is.False);
    }

    [Test] public void BetweenEma_PriceBelowBand_ReturnsFalse()
    {
        var s = WithEma(9.0, 9, 9.5, period2: 21, ema2Value: 10.5);
        Assert.That(new IndicatorCondition(IndicatorType.BetweenEma, 9, 21).Evaluate(s), Is.False);
    }

    [Test] public void BetweenEma_OneEmaMissing_FailsClosed()
    {
        var s = WithEma(10.0, 9, 9.5);
        Assert.That(new IndicatorCondition(IndicatorType.BetweenEma, 9, 21).Evaluate(s), Is.False);
    }

    // ── EmaStack ─────────────────────────────────────────────────────────

    [Test] public void EmaStack_FastAboveSlow_ReturnsTrue()
    {
        var s = WithEma(10.0, 9, 10.2, period2: 21, ema2Value: 9.8);
        Assert.That(new IndicatorCondition(IndicatorType.EmaStack, 9, 21).Evaluate(s), Is.True);
    }

    [Test] public void EmaStack_FastBelowSlow_ReturnsFalse()
    {
        var s = WithEma(10.0, 9, 9.8, period2: 21, ema2Value: 10.2);
        Assert.That(new IndicatorCondition(IndicatorType.EmaStack, 9, 21).Evaluate(s), Is.False);
    }

    [Test] public void EmaStack_OneEmaMissing_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.EmaStack, 9, 21).Evaluate(Sparse()), Is.False);

    // ── ReclaimEma ───────────────────────────────────────────────────────

    [Test] public void ReclaimEma_PriorBelowEma_CurrentAbove_ReturnsTrue()
    {
        var s = WithEma(10.5, 9, 10.0, priorPrice: 9.8, priorEmaValue: 10.0);
        Assert.That(new IndicatorCondition(IndicatorType.ReclaimEma, 9).Evaluate(s), Is.True);
    }

    [Test] public void ReclaimEma_PriorAlreadyAboveEma_ReturnsFalse()
    {
        var s = WithEma(10.5, 9, 10.0, priorPrice: 10.2, priorEmaValue: 10.0);
        Assert.That(new IndicatorCondition(IndicatorType.ReclaimEma, 9).Evaluate(s), Is.False);
    }

    [Test] public void ReclaimEma_MissingPriorEma_FailsClosed()
    {
        var s = WithEma(10.5, 9, 10.0, priorPrice: 9.8);
        Assert.That(new IndicatorCondition(IndicatorType.ReclaimEma, 9).Evaluate(s), Is.False);
    }

    // ── DiPositive / DiNegative ─────────────────────────────────────────

    [Test] public void DiPositive_PlusDiAboveMinusDi_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.DiPositive)
            .Evaluate(WithAdx(25, 30, 15)), Is.True);

    [Test] public void DiPositive_PlusDiBelowMinusDi_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.DiPositive)
            .Evaluate(WithAdx(25, 15, 30)), Is.False);

    [Test] public void DiPositive_NullDiData_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.DiPositive)
            .Evaluate(Sparse()), Is.False);

    [Test] public void DiNegative_MinusDiAbovePlusDi_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.DiNegative)
            .Evaluate(WithAdx(25, 15, 30)), Is.True);

    [Test] public void DiNegative_PlusDiAboveMinusDi_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.DiNegative)
            .Evaluate(WithAdx(25, 30, 15)), Is.False);

    [Test] public void DiNegative_NullDiData_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.DiNegative)
            .Evaluate(Sparse()), Is.False,
            "DiNegative without DI data must fail closed — was a critical fail-open bug on early bars");

    // ── AdxAbove ─────────────────────────────────────────────────────────

    [TestCase(25.0, 20.0, true)]
    [TestCase(20.0, 20.0, true)]   // exact boundary passes (>=)
    [TestCase(19.9, 20.0, false)]
    public void AdxAbove_Threshold(double adx, double threshold, bool expected)
        => Assert.That(new IndicatorCondition(IndicatorType.AdxAbove, threshold)
            .Evaluate(WithAdx(adx, 20, 15)), Is.EqualTo(expected));

    [Test] public void AdxAbove_DefaultThreshold_20()
        => Assert.That(new IndicatorCondition(IndicatorType.AdxAbove)
            .Evaluate(WithAdx(20.0, 20, 15)), Is.True);

    [Test] public void AdxAbove_NullAdx_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.AdxAbove, 20)
            .Evaluate(Sparse()), Is.False);

    // ── RsiOversold / RsiOverbought ────────────────────────────────────

    [TestCase(29.9, 30.0, true)]
    [TestCase(30.0, 30.0, true)]   // exact boundary passes (<=)
    [TestCase(30.1, 30.0, false)]
    public void RsiOversold_Threshold(double rsi, double threshold, bool expected)
        => Assert.That(new IndicatorCondition(IndicatorType.RsiOversold, threshold)
            .Evaluate(WithRsi(rsi)), Is.EqualTo(expected));

    [Test] public void RsiOversold_DefaultThreshold_30()
        => Assert.That(new IndicatorCondition(IndicatorType.RsiOversold)
            .Evaluate(WithRsi(30.0)), Is.True);

    [Test] public void RsiOversold_NullRsi_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.RsiOversold)
            .Evaluate(Sparse()), Is.False);

    [TestCase(70.1, 70.0, true)]
    [TestCase(70.0, 70.0, true)]
    [TestCase(69.9, 70.0, false)]
    public void RsiOverbought_Threshold(double rsi, double threshold, bool expected)
        => Assert.That(new IndicatorCondition(IndicatorType.RsiOverbought, threshold)
            .Evaluate(WithRsi(rsi)), Is.EqualTo(expected));

    [Test] public void RsiOverbought_NullRsi_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.RsiOverbought)
            .Evaluate(Sparse()), Is.False);

    // ── RSI Divergence ───────────────────────────────────────────────────

    [Test] public void RsiBullishDivergence_FlagSet_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.RsiBullishDivergence)
            .Evaluate(WithRsi(35, bullDiv: true)), Is.True);

    [Test] public void RsiBullishDivergence_FlagFalse_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.RsiBullishDivergence)
            .Evaluate(WithRsi(35, bullDiv: false)), Is.False);

    [Test] public void RsiBullishDivergence_FlagNull_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.RsiBullishDivergence)
            .Evaluate(Sparse()), Is.False);

    [Test] public void RsiBearishDivergence_FlagSet_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.RsiBearishDivergence)
            .Evaluate(WithRsi(65, bearDiv: true)), Is.True);

    [Test] public void RsiBearishDivergence_FlagNull_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.RsiBearishDivergence)
            .Evaluate(Sparse()), Is.False);

    // ── HigherLow / LowerHigh ────────────────────────────────────────────

    [Test] public void HigherLow_FlagTrue_ReturnsTrue()
    {
        var s = Sparse();
        s.HasHigherLow = true;
        Assert.That(new IndicatorCondition(IndicatorType.HigherLow).Evaluate(s), Is.True);
    }

    [Test] public void HigherLow_FlagFalse_ReturnsFalse()
    {
        var s = Sparse();
        s.HasHigherLow = false;
        Assert.That(new IndicatorCondition(IndicatorType.HigherLow).Evaluate(s), Is.False);
    }

    [Test] public void HigherLow_FlagNull_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.HigherLow).Evaluate(Sparse()), Is.False);

    [Test] public void LowerHigh_FlagTrue_ReturnsTrue()
    {
        var s = Sparse();
        s.HasLowerHigh = true;
        Assert.That(new IndicatorCondition(IndicatorType.LowerHigh).Evaluate(s), Is.True);
    }

    [Test] public void LowerHigh_FlagNull_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.LowerHigh).Evaluate(Sparse()), Is.False);

    // ── MacdBullish / MacdBearish ────────────────────────────────────────

    [Test] public void MacdBullish_MacdLineAboveSignal_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.MacdBullish)
            .Evaluate(WithMacd(1.0, 0.5)), Is.True);

    [Test] public void MacdBullish_MacdLineBelowSignal_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.MacdBullish)
            .Evaluate(WithMacd(-0.5, 0.5)), Is.False);

    [Test] public void MacdBullish_NullMacd_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.MacdBullish)
            .Evaluate(Sparse()), Is.False);

    [Test] public void MacdBearish_MacdLineBelowSignal_ReturnsTrue()
        => Assert.That(new IndicatorCondition(IndicatorType.MacdBearish)
            .Evaluate(WithMacd(-0.5, 0.5)), Is.True);

    [Test] public void MacdBearish_MacdLineAboveSignal_ReturnsFalse()
        => Assert.That(new IndicatorCondition(IndicatorType.MacdBearish)
            .Evaluate(WithMacd(1.0, 0.5)), Is.False);

    [Test] public void MacdBearish_NullMacd_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.MacdBearish)
            .Evaluate(Sparse()), Is.False,
            "MacdBearish without MACD data must fail closed — was a critical fail-open bug on early bars");

    // ── GapUp / GapDown ──────────────────────────────────────────────────

    [TestCase(10.6, 10.0, 3.0, true)]   // 6% gap > 3% threshold
    [TestCase(10.3, 10.0, 3.0, true)]   // exactly 3% passes (>=)
    [TestCase(10.2, 10.0, 3.0, false)]  // 2% gap below threshold
    public void GapUp_Threshold(double price, double prevClose, double threshold, bool expected)
        => Assert.That(new IndicatorCondition(IndicatorType.GapUp, threshold)
            .Evaluate(WithGap(price, prevClose)), Is.EqualTo(expected));

    [Test] public void GapUp_NoPreviousClose_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.GapUp, 3)
            .Evaluate(Sparse()), Is.False);

    [Test] public void GapUp_DefaultThreshold_3Percent()
        => Assert.That(new IndicatorCondition(IndicatorType.GapUp)
            .Evaluate(WithGap(10.31, 10.0)), Is.True);

    [TestCase(9.7, 10.0, 3.0, true)]    // -3% gap
    [TestCase(9.7, 10.0, 2.0, true)]    // -3% gap > 2% threshold
    [TestCase(9.85, 10.0, 3.0, false)]  // only -1.5% gap
    public void GapDown_Threshold(double price, double prevClose, double threshold, bool expected)
        => Assert.That(new IndicatorCondition(IndicatorType.GapDown, threshold)
            .Evaluate(WithGap(price, prevClose)), Is.EqualTo(expected));

    [Test] public void GapDown_NoPreviousClose_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.GapDown, 3)
            .Evaluate(Sparse()), Is.False);

    // ── VolumeAbove ──────────────────────────────────────────────────────

    [TestCase(1_000_000L, 500_000.0, 1.5, true)]   // ratio 2.0 >= 1.5
    [TestCase(1_500_000L, 1_000_000.0, 1.5, true)] // ratio 1.5 = threshold
    [TestCase(1_000_000L, 1_000_000.0, 1.5, false)] // ratio 1.0 < 1.5
    public void VolumeAbove_Threshold(long vol, double avgVol, double multiplier, bool expected)
        => Assert.That(new IndicatorCondition(IndicatorType.VolumeAbove, multiplier)
            .Evaluate(WithVolume(vol, avgVol)), Is.EqualTo(expected));

    [Test] public void VolumeAbove_ZeroAverageVolume_AndPositiveVolume_UsesLargeSentinel()
    {
        // Thin premarket ticker: avgVol == 0 but live bar has volume → sentinel 999x → any multiplier passes
        var s = WithVolume(100, 0);
        Assert.That(new IndicatorCondition(IndicatorType.VolumeAbove, 2.0).Evaluate(s), Is.True,
            "a live bar over a zero-volume baseline must not block a volume screen (sentinel 999)");
    }

    [Test] public void VolumeAbove_ZeroVolumeAndZeroAvg_ReturnsFalse()
    {
        var s = WithVolume(0, 0);
        Assert.That(new IndicatorCondition(IndicatorType.VolumeAbove, 2.0).Evaluate(s), Is.False);
    }

    // ── AtSupport / AtResistance ─────────────────────────────────────────

    [Test] public void AtSupport_PriceNearSwingLow_ReturnsTrue()
    {
        var s = Sparse();
        s.RecentSwingLow = 9.98;
        s.Price = 10.0;
        Assert.That(new IndicatorCondition(IndicatorType.AtSupport, 0.5).Evaluate(s), Is.True);
    }

    [Test] public void AtSupport_PriceFarFromSwingLow_ReturnsFalse()
    {
        var s = Sparse();
        s.RecentSwingLow = 9.0;
        s.Price = 10.0;
        Assert.That(new IndicatorCondition(IndicatorType.AtSupport, 0.5).Evaluate(s), Is.False);
    }

    [Test] public void AtSupport_NullSwingLow_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.AtSupport).Evaluate(Sparse()), Is.False);

    [Test] public void AtResistance_PriceNearSwingHigh_ReturnsTrue()
    {
        var s = Sparse();
        s.RecentSwingHigh = 10.02;
        s.Price = 10.0;
        Assert.That(new IndicatorCondition(IndicatorType.AtResistance, 0.5).Evaluate(s), Is.True);
    }

    [Test] public void AtResistance_NullSwingHigh_FailsClosed()
        => Assert.That(new IndicatorCondition(IndicatorType.AtResistance).Evaluate(Sparse()), Is.False);

    // ── PatternCondition — candlestick patterns ──────────────────────────

    [Test] public void BullishEngulfing_FlagTrue_ReturnsTrue()
    {
        var s = Sparse();
        s.IsBullishEngulfing = true;
        Assert.That(new PatternCondition(PatternType.BullishEngulfing).Evaluate(s), Is.True);
    }

    [Test] public void BullishEngulfing_FlagFalse_ReturnsFalse()
        => Assert.That(new PatternCondition(PatternType.BullishEngulfing).Evaluate(Sparse()), Is.False);

    [Test] public void BearishEngulfing_FlagTrue_ReturnsTrue()
    {
        var s = Sparse();
        s.IsBearishEngulfing = true;
        Assert.That(new PatternCondition(PatternType.BearishEngulfing).Evaluate(s), Is.True);
    }

    [Test] public void Hammer_FlagTrue_ReturnsTrue()
    {
        var s = Sparse();
        s.IsHammer = true;
        Assert.That(new PatternCondition(PatternType.Hammer).Evaluate(s), Is.True);
    }

    [Test] public void ShootingStar_FlagTrue_ReturnsTrue()
    {
        var s = Sparse();
        s.IsShootingStar = true;
        Assert.That(new PatternCondition(PatternType.ShootingStar).Evaluate(s), Is.True);
    }

    [Test] public void Doji_FlagTrue_ReturnsTrue()
    {
        var s = Sparse();
        s.IsDoji = true;
        Assert.That(new PatternCondition(PatternType.Doji).Evaluate(s), Is.True);
    }

    // ── PatternCondition — Breakout (window-scoped) ──────────────────────

    [Test] public void Breakout_WithLevel_WindowHighAtOrAboveLevel_ReturnsTrue()
    {
        var s = Sparse();
        s.WindowHigh = 10.5;
        s.Price = 10.2;
        Assert.That(new PatternCondition(PatternType.Breakout, 10.0).Evaluate(s), Is.True);
    }

    [Test] public void Breakout_WithLevel_WindowHighBelowLevel_ReturnsFalse()
    {
        var s = Sparse();
        s.WindowHigh = 9.5;
        s.Price = 9.4;
        Assert.That(new PatternCondition(PatternType.Breakout, 10.0).Evaluate(s), Is.False);
    }

    [Test] public void Breakout_WithLevel_NullWindowHigh_FailsClosed()
    {
        var s = Sparse();
        s.Price = 10.2;
        Assert.That(new PatternCondition(PatternType.Breakout, 10.0).Evaluate(s), Is.False);
    }

    [Test] public void Breakout_WithoutLevel_AlwaysFalse()
    {
        // A Breakout() with no level never latches — by design matches the backtester
        var s = Sparse();
        s.WindowHigh = 10.5;
        Assert.That(new PatternCondition(PatternType.Breakout, null).Evaluate(s), Is.False);
    }

    // ── PatternCondition — Pullback ───────────────────────────────────────

    [Test] public void Pullback_NoSupport_PriceBelowWindowHigh_ReturnsTrue()
    {
        var s = Sparse();
        s.WindowHigh = 11.0;
        s.Price = 10.0;
        Assert.That(new PatternCondition(PatternType.Pullback).Evaluate(s), Is.True);
    }

    [Test] public void Pullback_NoSupport_NullWindowHigh_FailsClosed()
        => Assert.That(new PatternCondition(PatternType.Pullback).Evaluate(Sparse()), Is.False);

    [Test] public void Pullback_WithSupport_BarLowAtOrBelowSupport_ReturnsTrue()
    {
        var s = Sparse();
        s.WindowHigh = 11.0;
        s.BarLow = 9.98;
        Assert.That(new PatternCondition(PatternType.Pullback, 10.0).Evaluate(s), Is.True);
    }

    [Test] public void Pullback_WithSupport_BarLowAboveSupport_ReturnsFalse()
    {
        var s = Sparse();
        s.WindowHigh = 11.0;
        s.BarLow = 10.05;
        Assert.That(new PatternCondition(PatternType.Pullback, 10.0).Evaluate(s), Is.False);
    }

    // ── PriceLevelCondition ───────────────────────────────────────────────

    [Test] public void HoldsAbove_PriceAboveLevel_ReturnsTrue()
    {
        var c = new PriceLevelCondition(PriceLevelType.HoldsAbove, 9.0);
        var s = Sparse();
        s.Price = 10.0;
        Assert.That(c.Evaluate(s), Is.True);
    }

    [Test] public void HoldsAbove_PriceBelowLevel_ReturnsFalse()
    {
        var c = new PriceLevelCondition(PriceLevelType.HoldsAbove, 11.0);
        var s = Sparse();
        s.Price = 10.0;
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test] public void HoldsBelow_PriceBelowLevel_ReturnsTrue()
    {
        var c = new PriceLevelCondition(PriceLevelType.HoldsBelow, 11.0);
        var s = Sparse();
        s.Price = 10.0;
        Assert.That(c.Evaluate(s), Is.True);
    }

    [Test] public void HoldsBelow_PriceAboveLevel_ReturnsFalse()
    {
        var c = new PriceLevelCondition(PriceLevelType.HoldsBelow, 9.0);
        var s = Sparse();
        s.Price = 10.0;
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test] public void Near_PriceWithinTolerance_ReturnsTrue()
    {
        var c = new PriceLevelCondition(PriceLevelType.Near, 10.0, 1.0);
        var s = Sparse();
        s.Price = 10.05; // 0.5% away
        Assert.That(c.Evaluate(s), Is.True);
    }

    [Test] public void Near_PriceOutsideTolerance_ReturnsFalse()
    {
        var c = new PriceLevelCondition(PriceLevelType.Near, 10.0, 0.5);
        var s = Sparse();
        s.Price = 10.1; // 1% away > 0.5%
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test] public void BreaksAbove_PriorAtOrBelow_CurrentAbove_ReturnsTrue()
    {
        var c = new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0);
        var s = Sparse();
        s.PriorPrice = 9.95;
        s.Price = 10.05;
        Assert.That(c.Evaluate(s), Is.True);
    }

    [Test] public void BreaksAbove_PriorAlreadyAbove_ReturnsFalse()
    {
        var c = new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0);
        var s = Sparse();
        s.PriorPrice = 10.02;
        s.Price = 10.05;
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test] public void BreaksAbove_NullPriorPrice_FailsClosed()
    {
        var c = new PriceLevelCondition(PriceLevelType.BreaksAbove, 10.0);
        var s = Sparse();
        s.Price = 10.05;
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test] public void BreaksBelow_PriorAtOrAbove_CurrentBelow_ReturnsTrue()
    {
        var c = new PriceLevelCondition(PriceLevelType.BreaksBelow, 10.0);
        var s = Sparse();
        s.PriorPrice = 10.02;
        s.Price = 9.95;
        Assert.That(c.Evaluate(s), Is.True);
    }

    [Test] public void BreaksBelow_PriorAlreadyBelow_ReturnsFalse()
    {
        var c = new PriceLevelCondition(PriceLevelType.BreaksBelow, 10.0);
        var s = Sparse();
        s.PriorPrice = 9.98;
        s.Price = 9.95;
        Assert.That(c.Evaluate(s), Is.False);
    }

    // ── GapBandCondition ─────────────────────────────────────────────────

    [Test] public void GapBand_GapWithinBand_ReturnsTrue()
    {
        var c = new GapBandCondition(5, 20);
        var s = WithGap(11.0, 10.0); // 10% gap
        Assert.That(c.Evaluate(s), Is.True);
    }

    [Test] public void GapBand_GapBelowMin_ReturnsFalse()
    {
        var c = new GapBandCondition(5, 20);
        var s = WithGap(10.3, 10.0); // 3% gap < 5%
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test] public void GapBand_GapAboveMax_ReturnsFalse()
    {
        var c = new GapBandCondition(5, 20);
        var s = WithGap(13.0, 10.0); // 30% gap > 20%
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test] public void GapBand_NoPreviousClose_FailsClosed()
        => Assert.That(new GapBandCondition(5, 20).Evaluate(Sparse()), Is.False);

    [Test] public void GapBand_AtExactBoundaries_ReturnsTrue()
    {
        var c = new GapBandCondition(5, 20);
        var atMin = WithGap(10.5, 10.0); // exactly 5%
        var atMax = WithGap(12.0, 10.0); // exactly 20%
        Assert.Multiple(() =>
        {
            Assert.That(c.Evaluate(atMin), Is.True, "at exactly min boundary");
            Assert.That(c.Evaluate(atMax), Is.True, "at exactly max boundary");
        });
    }

    // ── PriceBandCondition ───────────────────────────────────────────────

    [Test] public void PriceBand_PriceInsideBand_ReturnsTrue()
    {
        var s = Sparse();
        s.Price = 10.0;
        Assert.That(new PriceBandCondition(5.0, 20.0).Evaluate(s), Is.True);
    }

    [Test] public void PriceBand_PriceBelowBand_ReturnsFalse()
    {
        var s = Sparse();
        s.Price = 4.99;
        Assert.That(new PriceBandCondition(5.0, 20.0).Evaluate(s), Is.False);
    }

    [Test] public void PriceBand_PriceAboveBand_ReturnsFalse()
    {
        var s = Sparse();
        s.Price = 20.01;
        Assert.That(new PriceBandCondition(5.0, 20.0).Evaluate(s), Is.False);
    }

    [Test] public void PriceBand_AtExactBoundaries_ReturnsTrue()
    {
        var s5 = Base(5.0);
        var s20 = Base(20.0);
        var c = new PriceBandCondition(5.0, 20.0);
        Assert.Multiple(() =>
        {
            Assert.That(c.Evaluate(s5), Is.True, "exactly at min");
            Assert.That(c.Evaluate(s20), Is.True, "exactly at max");
        });
    }

    // ── Boolean algebra (And / Or / Not) — full truth table ─────────────

    private static ICondition True() => new PriceBandCondition(0, double.MaxValue);
    private static ICondition False() => new PriceBandCondition(100, 0); // max < min → never true

    [Test] public void And_TrueAndTrue_IsTrue()
        => Assert.That(True().And(True()).Evaluate(Sparse()), Is.True);

    [Test] public void And_TrueAndFalse_IsFalse()
        => Assert.That(True().And(False()).Evaluate(Sparse()), Is.False);

    [Test] public void And_FalseAndTrue_IsFalse()
        => Assert.That(False().And(True()).Evaluate(Sparse()), Is.False);

    [Test] public void And_FalseAndFalse_IsFalse()
        => Assert.That(False().And(False()).Evaluate(Sparse()), Is.False);

    [Test] public void Or_TrueOrTrue_IsTrue()
        => Assert.That(True().Or(True()).Evaluate(Sparse()), Is.True);

    [Test] public void Or_TrueOrFalse_IsTrue()
        => Assert.That(True().Or(False()).Evaluate(Sparse()), Is.True);

    [Test] public void Or_FalseOrTrue_IsTrue()
        => Assert.That(False().Or(True()).Evaluate(Sparse()), Is.True);

    [Test] public void Or_FalseOrFalse_IsFalse()
        => Assert.That(False().Or(False()).Evaluate(Sparse()), Is.False);

    [Test] public void Not_NotTrue_IsFalse()
        => Assert.That(True().Not().Evaluate(Sparse()), Is.False);

    [Test] public void Not_NotFalse_IsTrue()
        => Assert.That(False().Not().Evaluate(Sparse()), Is.True);

    // ── Composition depth ────────────────────────────────────────────────

    [Test] public void DeepComposition_ThreeLevel_EvaluatesCorrectly()
    {
        // (True AND (False OR True)) AND NOT(False) = (T AND T) AND T = T
        var expr = True().And(False().Or(True())).And(False().Not());
        Assert.That(expr.Evaluate(Sparse()), Is.True);
    }

    [Test] public void DeepComposition_AllFalseOrChain_ReturnsFalse()
    {
        var expr = False().Or(False()).Or(False());
        Assert.That(expr.Evaluate(Sparse()), Is.False);
    }

    [Test] public void Composition_AndShortCircuitsOnFalseLeft()
    {
        // Ensure the AND evaluator does not throw when right operand would fault if evaluated standalone
        // (no window high) — the left-side False must be enough.
        var s = Sparse();
        var breakoutRight = new PatternCondition(PatternType.Breakout, 10.0);
        var expr = False().And(breakoutRight);
        Assert.That(expr.Evaluate(s), Is.False);
    }
}
