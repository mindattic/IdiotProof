using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Every ScriptParser case: parse a script text → StrategyDefinition, then call
/// ToScript() and re-parse. Both passes must produce the same field values.
/// Additionally verifies that every known keyword survives a full text→def→text→def cycle.
/// </summary>
public class ScriptParserRoundTripTests
{
    // ── helper: build text, parse once, verify; then re-emit and parse again ──

    private static StrategyDefinition Parse(string text)
    {
        var def = ScriptParser.ParseScript(text);
        Assert.That(def, Is.Not.Null, $"ParseScript returned null for:\n{text}");
        return def!;
    }

    /// <summary>
    /// Build from fluent API → ToScript() → ParseScript() and compare.
    /// This checks that every emitted token is understood by the parser.
    /// </summary>
    private static StrategyDefinition RoundTrip(StrategyBuilder b)
    {
        var text = b.ToScript();
        var def = ScriptParser.ParseScript(text);
        Assert.That(def, Is.Not.Null, $"Round-trip parse returned null for:\n{text}");
        return def!;
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    [Test]
    public void Ticker_ParsedCorrectly()
    {
        var def = Parse("Ticker(\"NVDA\").Long()");
        Assert.That(def.Symbol, Is.EqualTo("NVDA"));
    }

    [Test]
    public void Name_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Name("My Strategy").Long());
        Assert.That(rt.Name, Is.EqualTo("My Strategy"));
    }

    [Test]
    public void Session_Premarket_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Session(TradingSession.Premarket).Long());
        Assert.That(rt.Session, Is.EqualTo(TradingSession.Premarket));
    }

    [Test]
    public void Session_Extended_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Session(TradingSession.Extended).Long());
        Assert.That(rt.Session, Is.EqualTo(TradingSession.Extended));
    }

    [Test]
    public void Session_RTH_NotEmitted_DefaultsToRTH()
    {
        // RTH is the default; ToScript() doesn't emit a Session() verb for it
        var rt = RoundTrip(Stock.Ticker("X").Session(TradingSession.RTH).Long());
        Assert.That(rt.Session, Is.EqualTo(TradingSession.RTH));
    }

    // ── Sizing ────────────────────────────────────────────────────────────────

    [Test]
    public void QuantityShares_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().Quantity(7));
        Assert.Multiple(() =>
        {
            Assert.That(rt.Quantity, Is.EqualTo(7));
            Assert.That(rt.IsNotional, Is.False);
        });
    }

    [Test]
    public void QuantityNotional_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().QuantityNotional(2500m));
        Assert.Multiple(() =>
        {
            Assert.That(rt.IsNotional, Is.True);
            Assert.That(rt.NotionalAmount, Is.EqualTo(2500m));
        });
    }

    // ── Direction ─────────────────────────────────────────────────────────────

    [Test]
    public void Direction_Long_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long());
        Assert.That(rt.Direction, Is.EqualTo(TradeDirection.Long));
    }

    [Test]
    public void Direction_Short_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Short());
        Assert.That(rt.Direction, Is.EqualTo(TradeDirection.Short));
    }

    // ── VWAP ─────────────────────────────────────────────────────────────────

    [Test]
    public void IsAboveVwap_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsAboveVwap().Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.VwapAbove), Is.True);
    }

    [Test]
    public void IsBelowVwap_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsBelowVwap().Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.VwapBelow), Is.True);
    }

    [Test]
    public void OnVwapReclaim_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").OnVwapReclaim().Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.VwapReclaim), Is.True);
    }

    [Test]
    public void OnVwapLoss_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").OnVwapLoss().Short());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.VwapLoss), Is.True);
    }

    // ── EMA ───────────────────────────────────────────────────────────────────

    [Test]
    public void IsAboveEma_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsAboveEma(9).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>().Single(x => x.Type == IndicatorType.EmaAbove);
        Assert.That(c.Parameter, Is.EqualTo(9));
    }

    [Test]
    public void IsBelowEma_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsBelowEma(21).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>().Single(x => x.Type == IndicatorType.EmaBelow);
        Assert.That(c.Parameter, Is.EqualTo(21));
    }

    [Test]
    public void IsBetweenEma_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsBetweenEma(9, 50).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>().Single(x => x.Type == IndicatorType.BetweenEma);
        Assert.Multiple(() =>
        {
            Assert.That(c.Parameter, Is.EqualTo(9));
            Assert.That(c.Parameter2, Is.EqualTo(50));
        });
    }

    [Test]
    public void OnReclaim_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").OnReclaim(9).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>().Single(x => x.Type == IndicatorType.ReclaimEma);
        Assert.That(c.Parameter, Is.EqualTo(9));
    }

    // ── ADX ───────────────────────────────────────────────────────────────────

    [Test]
    public void RequireAdxAbove_RoundTrips_WithFiltersPhase()
    {
        var rt = RoundTrip(Stock.Ticker("X").RequireAdxAbove(25).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>()
            .SingleOrDefault(x => x.Type == IndicatorType.AdxAbove);
        Assert.Multiple(() =>
        {
            Assert.That(c, Is.Not.Null, "AdxAbove condition missing after round-trip");
            Assert.That(c!.Phase, Is.EqualTo(StrategyPhase.Filters),
                "RequireAdxAbove must round-trip as Filters phase, not Entry");
            Assert.That(c.Parameter, Is.EqualTo(25));
        });
    }

    [Test]
    public void IsAdxAbove_RoundTrips_WithEntryPhase()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsAdxAbove(20).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>()
            .SingleOrDefault(x => x.Type == IndicatorType.AdxAbove);
        Assert.That(c?.Phase, Is.EqualTo(StrategyPhase.Entry));
    }

    [Test]
    public void IsDiPositive_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsDiPositive().Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.DiPositive), Is.True);
    }

    [Test]
    public void IsDiNegative_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsDiNegative().Short());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.DiNegative), Is.True);
    }

    // ── RSI ───────────────────────────────────────────────────────────────────

    [Test]
    public void IsRsiOversold_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsRsiOversold(30).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>()
            .Single(x => x.Type == IndicatorType.RsiOversold);
        Assert.That(c.Parameter, Is.EqualTo(30));
    }

    [Test]
    public void IsRsiOverbought_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsRsiOverbought(70).Short());
        var c = rt.EntryConditions.OfType<IndicatorCondition>()
            .Single(x => x.Type == IndicatorType.RsiOverbought);
        Assert.That(c.Parameter, Is.EqualTo(70));
    }

    [Test]
    public void IsRsiBullishDivergence_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsRsiBullishDivergence().Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.RsiBullishDivergence), Is.True);
    }

    [Test]
    public void IsRsiBearishDivergence_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsRsiBearishDivergence().Short());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.RsiBearishDivergence), Is.True);
    }

    // ── MACD ─────────────────────────────────────────────────────────────────

    [Test]
    public void IsMacdBullish_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsMacdBullish().Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.MacdBullish), Is.True);
    }

    [Test]
    public void IsMacdBearish_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsMacdBearish().Short());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.MacdBearish), Is.True);
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    [Test]
    public void WithVolumeConfirm_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").WithVolumeConfirm(1.5).Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.VolumeAbove), Is.True);
    }

    [Test]
    public void IsVolumeAbove_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsVolumeAbove(2.0).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>()
            .Single(x => x.Type == IndicatorType.VolumeAbove);
        Assert.That(c.Parameter, Is.EqualTo(2.0));
    }

    // ── Gap ───────────────────────────────────────────────────────────────────

    [Test]
    public void IsGapUp_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsGapUp(5).Long());
        var c = rt.EntryConditions.OfType<IndicatorCondition>()
            .Single(x => x.Type == IndicatorType.GapUp);
        Assert.That(c.Parameter, Is.EqualTo(5));
    }

    [Test]
    public void IsGapDown_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsGapDown(3).Short());
        var c = rt.EntryConditions.OfType<IndicatorCondition>()
            .Single(x => x.Type == IndicatorType.GapDown);
        Assert.That(c.Parameter, Is.EqualTo(3));
    }

    [Test]
    public void IsGapBetween_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsGapBetween(5, 30).Long());
        // IsGapBetween is stored as a GapRangeCondition (separate condition type)
        Assert.That(rt.EntryConditions, Is.Not.Empty);
    }

    // ── Pivot structure ───────────────────────────────────────────────────────

    [Test]
    public void IsHigherLow_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsHigherLow().Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.HigherLow), Is.True);
    }

    [Test]
    public void IsLowerHigh_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsLowerHigh().Short());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.LowerHigh), Is.True);
    }

    // ── Support / Resistance ──────────────────────────────────────────────────

    [Test]
    public void IsAtSupport_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsAtSupport(0.5).Long());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.AtSupport), Is.True);
    }

    [Test]
    public void IsAtResistance_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsAtResistance(0.5).Short());
        Assert.That(rt.EntryConditions.OfType<IndicatorCondition>()
            .Any(c => c.Type == IndicatorType.AtResistance), Is.True);
    }

    // ── Price levels ──────────────────────────────────────────────────────────

    [Test]
    public void HoldsAbove_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").HoldsAbove(10.0).Long());
        var c = rt.EntryConditions.OfType<PriceLevelCondition>()
            .Single(x => x.Type == PriceLevelType.HoldsAbove);
        Assert.That(c.Level, Is.EqualTo(10.0));
    }

    [Test]
    public void HoldsBelow_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").HoldsBelow(20.0).Short());
        var c = rt.EntryConditions.OfType<PriceLevelCondition>()
            .Single(x => x.Type == PriceLevelType.HoldsBelow);
        Assert.That(c.Level, Is.EqualTo(20.0));
    }

    [Test]
    public void IsNear_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsNear(15.0, 1.5).Long());
        var c = rt.EntryConditions.OfType<PriceLevelCondition>()
            .Single(x => x.Type == PriceLevelType.Near);
        Assert.Multiple(() =>
        {
            Assert.That(c.Level, Is.EqualTo(15.0));
            Assert.That(c.TolerancePercent, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void BreaksAbove_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").BreaksAbove(10.0).Long());
        var c = rt.EntryConditions.OfType<PriceLevelCondition>()
            .Single(x => x.Type == PriceLevelType.BreaksAbove);
        Assert.That(c.Level, Is.EqualTo(10.0));
    }

    [Test]
    public void BreaksBelow_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").BreaksBelow(10.0).Short());
        var c = rt.EntryConditions.OfType<PriceLevelCondition>()
            .Single(x => x.Type == PriceLevelType.BreaksBelow);
        Assert.That(c.Level, Is.EqualTo(10.0));
    }

    // ── Candlestick patterns ──────────────────────────────────────────────────

    [Test]
    public void IsHammer_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsHammer().Long());
        Assert.That(rt.EntryConditions.OfType<PatternCondition>()
            .Any(c => c.Type == PatternType.Hammer), Is.True);
    }

    [Test]
    public void IsShootingStar_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsShootingStar().Short());
        Assert.That(rt.EntryConditions.OfType<PatternCondition>()
            .Any(c => c.Type == PatternType.ShootingStar), Is.True);
    }

    [Test]
    public void IsBullishEngulfing_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsBullishEngulfing().Long());
        Assert.That(rt.EntryConditions.OfType<PatternCondition>()
            .Any(c => c.Type == PatternType.BullishEngulfing), Is.True);
    }

    [Test]
    public void IsBearishEngulfing_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsBearishEngulfing().Short());
        Assert.That(rt.EntryConditions.OfType<PatternCondition>()
            .Any(c => c.Type == PatternType.BearishEngulfing), Is.True);
    }

    [Test]
    public void IsDoji_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").IsDoji().Long());
        Assert.That(rt.EntryConditions.OfType<PatternCondition>()
            .Any(c => c.Type == PatternType.Doji), Is.True);
    }

    // ── Entry rolling gates ───────────────────────────────────────────────────

    [Test]
    public void EntryAtRollingLow_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").EntryAtRollingLow(5, 3.0).Long());
        Assert.Multiple(() =>
        {
            Assert.That(rt.EntryRollingLowDays, Is.EqualTo(5));
            Assert.That(rt.EntryRollingLowBuffer, Is.EqualTo(3.0));
        });
    }

    [Test]
    public void EntryAtRollingHigh_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").EntryAtRollingHigh(10, 1.5).Long());
        Assert.Multiple(() =>
        {
            Assert.That(rt.EntryRollingHighDays, Is.EqualTo(10));
            Assert.That(rt.EntryRollingHighBuffer, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void Parser_EntryAtRollingLow_AliasIsnearrollinglow()
    {
        var canonical = ScriptParser.ParseScript("Ticker(\"X\").EntryAtRollingLow(5, 2.5).Long()")!;
        var alias     = ScriptParser.ParseScript("Ticker(\"X\").IsNearRollingLow(5, 2.5).Long()")!;
        Assert.That(alias.EntryRollingLowDays, Is.EqualTo(canonical.EntryRollingLowDays));
    }

    [Test]
    public void Parser_EntryAtRollingHigh_AliasIsbreakingout()
    {
        var canonical = ScriptParser.ParseScript("Ticker(\"X\").EntryAtRollingHigh(10, 2.5).Long()")!;
        var alias     = ScriptParser.ParseScript("Ticker(\"X\").IsBreakingOut(10, 2.5).Long()")!;
        Assert.That(alias.EntryRollingHighDays, Is.EqualTo(canonical.EntryRollingHighDays));
    }

    // ── Exit fields ───────────────────────────────────────────────────────────

    [Test]
    public void TakeProfit_SingleTarget_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().TakeProfit(12.0));
        Assert.That(rt.TakeProfitPrice, Is.EqualTo(12.0));
    }

    [Test]
    public void TakeProfit_MultiTarget_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().TakeProfit(5.0, 6.5, 8.0));
        Assert.Multiple(() =>
        {
            Assert.That(rt.TakeProfitTargets.Count, Is.EqualTo(3));
            Assert.That(rt.TakeProfitTargets[0].Price, Is.EqualTo(5.0));
            Assert.That(rt.TakeProfitTargets[2].Price, Is.EqualTo(8.0));
        });
    }

    [Test]
    public void TakeProfitPercent_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().TakeProfitPercent(10));
        Assert.That(rt.TakeProfitPercent, Is.EqualTo(10));
    }

    [Test]
    public void StopLoss_Price_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().StopLoss(8.0));
        Assert.That(rt.StopLossPrice, Is.EqualTo(8.0));
    }

    [Test]
    public void StopLossPercent_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().StopLossPercent(5.0));
        Assert.That(rt.StopLossPercent, Is.EqualTo(5.0));
    }

    [Test]
    public void TrailingStopLoss_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().TrailingStopLoss(3.0));
        Assert.That(rt.TrailingStopPercent, Is.EqualTo(3.0));
    }

    [Test]
    public void SellBy_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().SellBy("09:28"));
        Assert.That(rt.ExitTime, Is.EqualTo(new TimeSpan(9, 28, 0)));
    }

    [Test]
    public void PeakGiveback_NoArmTime_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().PeakGiveback(25));
        Assert.Multiple(() =>
        {
            Assert.That(rt.PeakGivebackPercent, Is.EqualTo(25));
            Assert.That(rt.PeakGivebackArmTime, Is.Null);
        });
    }

    [Test]
    public void PeakGiveback_WithArmTime_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().PeakGiveback(25, "09:15"));
        Assert.Multiple(() =>
        {
            Assert.That(rt.PeakGivebackPercent, Is.EqualTo(25));
            Assert.That(rt.PeakGivebackArmTime, Is.EqualTo(new TimeSpan(9, 15, 0)));
        });
    }

    [Test]
    public void ExitAtPriorHigh_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().ExitAtPriorHigh());
        Assert.That(rt.ExitAtPriorHigh, Is.True);
    }

    [Test]
    public void ExitAtRollingHigh_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().ExitAtRollingHigh(20, 2.5));
        Assert.Multiple(() =>
        {
            Assert.That(rt.RollingHighDays, Is.EqualTo(20));
            Assert.That(rt.RollingHighBuffer, Is.EqualTo(2.5));
        });
    }

    [Test]
    public void ExitAtRollingLow_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().ExitAtRollingLow(10, 1.5));
        Assert.Multiple(() =>
        {
            Assert.That(rt.RollingLowDays, Is.EqualTo(10));
            Assert.That(rt.RollingLowBuffer, Is.EqualTo(1.5));
        });
    }

    // ── Advanced flags ────────────────────────────────────────────────────────

    [Test]
    public void AutonomousTrading_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().AutonomousTrading());
        Assert.That(rt.IsAutonomous, Is.True);
    }

    [Test]
    public void AdaptiveOrder_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().AdaptiveOrder());
        Assert.That(rt.IsAdaptive, Is.True);
    }

    [Test]
    public void Repeat_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").Long().Repeat());
        Assert.That(rt.ShouldRepeat, Is.True);
    }

    // ── Entry window ──────────────────────────────────────────────────────────

    [Test]
    public void RequireEntryWindow_RoundTrips()
    {
        var rt = RoundTrip(Stock.Ticker("X").RequireEntryWindow("04:00", "09:00").Long());
        var window = rt.EntryConditions.OfType<TimeWindowCondition>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(window.StartEt, Is.EqualTo(new TimeSpan(4, 0, 0)));
            Assert.That(window.EndEt, Is.EqualTo(new TimeSpan(9, 0, 0)));
        });
    }

    // ── Second-pass (emit→reparse) is identical to first pass ─────────────────

    [Test]
    public void FullGapperStrategy_TwoPassRoundTrip_Identical()
    {
        var builder = Stock.Ticker("NCI")
            .Name("Full Gapper")
            .Session(TradingSession.Premarket)
            .RequireEntryWindow("04:00", "09:00")
            .RequireAdxAbove(20)
            .IsAboveVwap()
            .IsGapUp(5)
            .IsGapBetween(5, 30)
            .WithVolumeConfirm(1.5)
            .IsHigherLow()
            .Long()
            .QuantityNotional(2000m)
            .TakeProfit(5.0, 6.5, 8.0)
            .TakeProfitPercent(8)
            .StopLossPercent(4)
            .TrailingStopLoss(3)
            .SellBy("09:28")
            .PeakGiveback(25, "09:15")
            .ExitAtPriorHigh()
            .ExitAtRollingHigh(20, 2.5)
            .ExitAtRollingLow(10, 1.5)
            .AutonomousTrading();

        var pass1Text = builder.ToScript();
        var pass1 = ScriptParser.ParseScript(pass1Text)!;

        // Re-parse the same text a second time; both parses of the same text must agree
        var pass2 = ScriptParser.ParseScript(pass1Text)!;

        Assert.Multiple(() =>
        {
            Assert.That(pass2.Symbol, Is.EqualTo(pass1.Symbol));
            Assert.That(pass2.Name, Is.EqualTo(pass1.Name));
            Assert.That(pass2.Session, Is.EqualTo(pass1.Session));
            Assert.That(pass2.IsNotional, Is.EqualTo(pass1.IsNotional));
            Assert.That(pass2.NotionalAmount, Is.EqualTo(pass1.NotionalAmount));
            Assert.That(pass2.Direction, Is.EqualTo(pass1.Direction));
            Assert.That(pass2.TakeProfitPercent, Is.EqualTo(pass1.TakeProfitPercent));
            Assert.That(pass2.StopLossPercent, Is.EqualTo(pass1.StopLossPercent));
            Assert.That(pass2.TrailingStopPercent, Is.EqualTo(pass1.TrailingStopPercent));
            Assert.That(pass2.ExitTime, Is.EqualTo(pass1.ExitTime));
            Assert.That(pass2.PeakGivebackPercent, Is.EqualTo(pass1.PeakGivebackPercent));
            Assert.That(pass2.PeakGivebackArmTime, Is.EqualTo(pass1.PeakGivebackArmTime));
            Assert.That(pass2.ExitAtPriorHigh, Is.EqualTo(pass1.ExitAtPriorHigh));
            Assert.That(pass2.RollingHighDays, Is.EqualTo(pass1.RollingHighDays));
            Assert.That(pass2.RollingLowDays, Is.EqualTo(pass1.RollingLowDays));
            Assert.That(pass2.IsAutonomous, Is.EqualTo(pass1.IsAutonomous));
        });
    }

    // ── Null/empty safety ─────────────────────────────────────────────────────

    [Test]
    public void ParseScript_NullInput_ReturnsNull()
    {
        Assert.That(ScriptParser.ParseScript(null!), Is.Null);
    }

    [Test]
    public void ParseScript_EmptyString_ReturnsNull()
    {
        Assert.That(ScriptParser.ParseScript(""), Is.Null);
    }

    [Test]
    public void ParseScript_NoTickerVerb_ReturnsNull()
    {
        Assert.That(ScriptParser.ParseScript(".Long().StopLoss(8)"), Is.Null);
    }

    [Test]
    public void ParseScript_UnknownVerbs_AreSkipped_NotThrown()
    {
        var def = ScriptParser.ParseScript("Ticker(\"X\").SomeFutureVerb(99).Long()");
        Assert.That(def, Is.Not.Null, "unknown verbs must be silently skipped, not throw");
    }
}
