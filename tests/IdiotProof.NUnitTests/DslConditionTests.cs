using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.NUnitTests;

/// <summary>
/// Coverage for the DSL condition algebra and the new condition verbs added
/// in this session. Each test builds a tiny IndicatorSnapshot, evaluates a
/// single condition (or composed expression), asserts the boolean result.
///
/// We intentionally don't go through StrategyBuilder.Build() — this fixture
/// targets the condition-evaluation layer in isolation so failures point
/// directly at the wrong evaluator case.
/// </summary>
[TestFixture]
public class DslConditionTests
{
    // ── VWAP ─────────────────────────────────────────────────────────────

    [Test]
    public void IsAboveVwap_PriceAboveVwap_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { Price = 105, Vwap = 100 };
        Assert.That(new IndicatorCondition(IndicatorType.VwapAbove).Evaluate(s), Is.True);
    }

    [Test]
    public void IsAboveVwap_PriceBelowVwap_ReturnsFalse()
    {
        var s = new IndicatorSnapshot { Price = 95, Vwap = 100 };
        Assert.That(new IndicatorCondition(IndicatorType.VwapAbove).Evaluate(s), Is.False);
    }

    [Test]
    public void OnVwapReclaim_PriorBelowAndCurrentAbove_ReturnsTrue()
    {
        var s = new IndicatorSnapshot
        {
            Price = 101, Vwap = 100,
            PriorPrice = 99, PriorVwap = 100
        };
        Assert.That(new IndicatorCondition(IndicatorType.VwapReclaim).Evaluate(s), Is.True);
    }

    [Test]
    public void OnVwapReclaim_BothAbove_ReturnsFalse()
    {
        var s = new IndicatorSnapshot
        {
            Price = 105, Vwap = 100,
            PriorPrice = 102, PriorVwap = 100
        };
        Assert.That(new IndicatorCondition(IndicatorType.VwapReclaim).Evaluate(s), Is.False);
    }

    // ── EMA family ───────────────────────────────────────────────────────

    [Test]
    public void IsBetweenEma_PriceInside_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { Price = 105 };
        s.Emas[9]  = 110;
        s.Emas[31] = 100;
        var c = new IndicatorCondition(IndicatorType.BetweenEma, 9, 31);
        Assert.That(c.Evaluate(s), Is.True);
    }

    [Test]
    public void IsBetweenEma_PriceAboveBoth_ReturnsFalse()
    {
        var s = new IndicatorSnapshot { Price = 115 };
        s.Emas[9]  = 110;
        s.Emas[31] = 100;
        var c = new IndicatorCondition(IndicatorType.BetweenEma, 9, 31);
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test]
    public void RequireEmaStack_FastAboveSlow_ReturnsTrue()
    {
        var s = new IndicatorSnapshot();
        s.Emas[9]  = 110;
        s.Emas[31] = 100;
        var c = new IndicatorCondition(IndicatorType.EmaStack, 9, 31, StrategyPhase.Filters);
        Assert.That(c.Evaluate(s), Is.True);
        Assert.That(c.Phase, Is.EqualTo(StrategyPhase.Filters));
    }

    [Test]
    public void RequireEmaStack_FastBelowSlow_ReturnsFalse()
    {
        var s = new IndicatorSnapshot();
        s.Emas[9]  = 95;
        s.Emas[31] = 100;
        var c = new IndicatorCondition(IndicatorType.EmaStack, 9, 31);
        Assert.That(c.Evaluate(s), Is.False);
    }

    [Test]
    public void OnReclaim_PriorBelowAndCurrentAbove_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { Price = 105, PriorPrice = 99 };
        s.Emas[9]      = 100;
        s.PriorEmas[9] = 100;
        var c = new IndicatorCondition(IndicatorType.ReclaimEma, 9);
        Assert.That(c.Evaluate(s), Is.True);
    }

    [Test]
    public void OnReclaim_BothAbove_ReturnsFalse()
    {
        var s = new IndicatorSnapshot { Price = 105, PriorPrice = 102 };
        s.Emas[9]      = 100;
        s.PriorEmas[9] = 100;
        var c = new IndicatorCondition(IndicatorType.ReclaimEma, 9);
        Assert.That(c.Evaluate(s), Is.False);
    }

    // ── ADX / RSI / Volume ────────────────────────────────────────────────

    [Test]
    public void RequireAdxAbove_AdxGreater_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { Adx = 25 };
        Assert.That(new IndicatorCondition(IndicatorType.AdxAbove, 20, null, StrategyPhase.Filters).Evaluate(s), Is.True);
    }

    [Test]
    public void RsiBullishDivergence_FlagSet_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { HasBullishDivergence = true };
        Assert.That(new IndicatorCondition(IndicatorType.RsiBullishDivergence).Evaluate(s), Is.True);
    }

    [Test]
    public void VolumeAbove_RatioMeetsMultiplier_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { Volume = 200, AverageVolume = 100 };
        Assert.That(new IndicatorCondition(IndicatorType.VolumeAbove, 1.5).Evaluate(s), Is.True);
    }

    // ── Support / Resistance ──────────────────────────────────────────────

    [Test]
    public void IsAtSupport_WithinTolerance_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { Price = 100.3, RecentSwingLow = 100.0 };
        Assert.That(new IndicatorCondition(IndicatorType.AtSupport, 0.5).Evaluate(s), Is.True);
    }

    [Test]
    public void IsAtSupport_OutsideTolerance_ReturnsFalse()
    {
        var s = new IndicatorSnapshot { Price = 102, RecentSwingLow = 100.0 };
        Assert.That(new IndicatorCondition(IndicatorType.AtSupport, 0.5).Evaluate(s), Is.False);
    }

    // ── Condition algebra ─────────────────────────────────────────────────

    [Test]
    public void And_BothTrue_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { Price = 105, Vwap = 100, Adx = 30 };
        var expr = Conditions.IsAboveVwap.And(Conditions.IsAdxAbove(20));
        Assert.That(expr.Evaluate(s), Is.True);
    }

    [Test]
    public void And_OneFalse_ReturnsFalse()
    {
        var s = new IndicatorSnapshot { Price = 95, Vwap = 100, Adx = 30 };
        var expr = Conditions.IsAboveVwap.And(Conditions.IsAdxAbove(20));
        Assert.That(expr.Evaluate(s), Is.False);
    }

    [Test]
    public void Or_OneTrue_ReturnsTrue()
    {
        var s = new IndicatorSnapshot { Price = 105, Vwap = 100, Adx = 10 };
        var expr = Conditions.IsAboveVwap.Or(Conditions.IsAdxAbove(20));
        Assert.That(expr.Evaluate(s), Is.True);
    }

    [Test]
    public void Not_True_ReturnsFalse()
    {
        var s = new IndicatorSnapshot { Price = 105, Vwap = 100 };
        Assert.That(Conditions.IsAboveVwap.Not().Evaluate(s), Is.False);
    }

    // ── Phase tagging ─────────────────────────────────────────────────────

    [Test]
    public void Default_ConditionPhase_IsEntry()
    {
        var c = new IndicatorCondition(IndicatorType.VwapAbove);
        Assert.That(c.Phase, Is.EqualTo(StrategyPhase.Entry));
    }

    [Test]
    public void RequireEmaStack_DefaultsToFilters()
    {
        var c = (IndicatorCondition)Conditions.RequireEmaStack(9, 31);
        Assert.That(c.Phase, Is.EqualTo(StrategyPhase.Filters));
    }
}
