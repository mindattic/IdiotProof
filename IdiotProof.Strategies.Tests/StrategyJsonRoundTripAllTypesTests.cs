using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Every condition type that can appear in a strategy must survive a
/// Serialize→Deserialize round trip with IDENTICAL semantics.
/// The canonical JSON (IP-LAW-8) is the executable form; if a condition is
/// lost or mutated in serialization the strategy silently does the wrong thing
/// on real money.
///
/// One test per concrete condition type, plus combination and edge-case tests.
/// </summary>
public class StrategyJsonRoundTripAllTypesTests
{
    private static StrategyDefinition RoundTrip(StrategyDefinition def)
        => StrategyJson.Deserialize(StrategyJson.Serialize(def));

    private static string Script(ICondition c)
        => c.ToScript();

    // ── Single-field indicator conditions ─────────────────────────────────

    [TestCase(IndicatorType.VwapAbove)]
    [TestCase(IndicatorType.VwapBelow)]
    [TestCase(IndicatorType.VwapReclaim)]
    [TestCase(IndicatorType.VwapLoss)]
    [TestCase(IndicatorType.DiPositive)]
    [TestCase(IndicatorType.DiNegative)]
    [TestCase(IndicatorType.MacdBullish)]
    [TestCase(IndicatorType.MacdBearish)]
    [TestCase(IndicatorType.HigherLow)]
    [TestCase(IndicatorType.LowerHigh)]
    [TestCase(IndicatorType.RsiBullishDivergence)]
    [TestCase(IndicatorType.RsiBearishDivergence)]
    public void IndicatorCondition_NoParam_RoundTrips(IndicatorType type)
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new IndicatorCondition(type);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)),
            $"IndicatorCondition({type}) did not round-trip losslessly");
    }

    [TestCase(IndicatorType.GapUp,       5.0)]
    [TestCase(IndicatorType.GapDown,     3.0)]
    [TestCase(IndicatorType.AdxAbove,    20.0)]
    [TestCase(IndicatorType.RsiOversold, 30.0)]
    [TestCase(IndicatorType.RsiOverbought, 70.0)]
    [TestCase(IndicatorType.VolumeAbove, 1.5)]
    [TestCase(IndicatorType.AtSupport,   0.5)]
    [TestCase(IndicatorType.AtResistance, 0.5)]
    public void IndicatorCondition_P1Param_RoundTrips(IndicatorType type, double p1)
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new IndicatorCondition(type, p1);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)),
            $"IndicatorCondition({type}, {p1}) did not round-trip losslessly");
    }

    [TestCase(IndicatorType.EmaAbove,   9)]
    [TestCase(IndicatorType.EmaAbove,   21)]
    [TestCase(IndicatorType.EmaAbove,   200)]
    [TestCase(IndicatorType.EmaBelow,   9)]
    [TestCase(IndicatorType.ReclaimEma, 9)]
    public void IndicatorCondition_EmaWithPeriod_RoundTrips(IndicatorType type, double period)
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new IndicatorCondition(type, period);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)),
            $"IndicatorCondition({type}, period={period}) did not round-trip losslessly");
    }

    [Test]
    public void IndicatorCondition_BetweenEma_TwoParams_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new IndicatorCondition(IndicatorType.BetweenEma, 9, 21);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    [Test]
    public void IndicatorCondition_EmaStack_TwoParams_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new IndicatorCondition(IndicatorType.EmaStack, 9, 21);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    // ── PatternCondition ──────────────────────────────────────────────────

    [TestCase(PatternType.BullishEngulfing)]
    [TestCase(PatternType.BearishEngulfing)]
    [TestCase(PatternType.Hammer)]
    [TestCase(PatternType.ShootingStar)]
    [TestCase(PatternType.Doji)]
    public void PatternCondition_Candlestick_RoundTrips(PatternType type)
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new PatternCondition(type);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)),
            $"PatternCondition({type}) did not round-trip losslessly");
    }

    [Test]
    public void PatternCondition_Breakout_WithLevel_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new PatternCondition(PatternType.Breakout, 15.75);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    [Test]
    public void PatternCondition_Breakout_NullLevel_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new PatternCondition(PatternType.Breakout, null);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    [Test]
    public void PatternCondition_Pullback_WithSupportLevel_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new PatternCondition(PatternType.Pullback, 10.0);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    // ── PriceLevelCondition ───────────────────────────────────────────────

    [TestCase(PriceLevelType.HoldsAbove, 9.0,  0.0)]
    [TestCase(PriceLevelType.HoldsBelow, 11.0, 0.0)]
    [TestCase(PriceLevelType.BreaksAbove, 10.5, 0.0)]
    [TestCase(PriceLevelType.BreaksBelow, 9.5,  0.0)]
    [TestCase(PriceLevelType.Near,        10.0, 1.5)]
    public void PriceLevelCondition_AllTypes_RoundTrip(PriceLevelType kind, double level, double tolerance)
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new PriceLevelCondition(kind, level, tolerance);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)),
            $"PriceLevelCondition({kind}) did not round-trip losslessly");
    }

    // ── GapBandCondition ──────────────────────────────────────────────────

    [Test]
    public void GapBandCondition_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new GapBandCondition(5.0, 20.0);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    [Test]
    public void GapBandCondition_FractionalBoundaries_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new GapBandCondition(5.5, 19.75);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    // ── PriceBandCondition ────────────────────────────────────────────────

    [Test]
    public void PriceBandCondition_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new PriceBandCondition(5.0, 50.0);
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    // ── TimeWindowCondition ───────────────────────────────────────────────

    [Test]
    public void TimeWindowCondition_RegularWindow_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new TimeWindowCondition(new TimeSpan(4, 0, 0), new TimeSpan(9, 0, 0));
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    [Test]
    public void TimeWindowCondition_OvernightWindow_RoundTrips()
    {
        // startET > endET means the window wraps midnight: 22:00 ET → 02:00 ET
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new TimeWindowCondition(new TimeSpan(22, 0, 0), new TimeSpan(2, 0, 0));
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    // ── Boolean compositors ───────────────────────────────────────────────

    [Test]
    public void AndCondition_TwoLeaves_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new IndicatorCondition(IndicatorType.VwapAbove)
            .And(new IndicatorCondition(IndicatorType.AdxAbove, 20));
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    [Test]
    public void OrCondition_TwoLeaves_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new IndicatorCondition(IndicatorType.VwapAbove)
            .Or(new IndicatorCondition(IndicatorType.GapUp, 5));
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    [Test]
    public void NotCondition_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var cond = new IndicatorCondition(IndicatorType.VwapBelow).Not();
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    [Test]
    public void DeepComposition_AndOrNot_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        // (VwapAbove AND GapUp(5)) OR NOT(MacdBearish)
        var cond = new IndicatorCondition(IndicatorType.VwapAbove)
            .And(new IndicatorCondition(IndicatorType.GapUp, 5))
            .Or(new IndicatorCondition(IndicatorType.MacdBearish).Not());
        def.EntryConditions.Add(cond);

        var restored = RoundTrip(def);

        Assert.That(Script(restored.EntryConditions[0]), Is.EqualTo(Script(cond)));
    }

    // ── Multi-condition strategy ──────────────────────────────────────────

    [Test]
    public void Strategy_MultipleEntryConditions_AllRoundTrip()
    {
        var def = Stock.Ticker("NVDA")
            .IsGapUp(5)
            .IsAboveVwap()
            .IsVolumeAbove(2)
            .IsAdxAbove(20)
            .Long()
            .StopLossPercent(5)
            .TakeProfit(15, 20, 25)
            .Build();

        var restored = RoundTrip(def);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Symbol,           Is.EqualTo("NVDA"));
            Assert.That(restored.Direction,        Is.EqualTo(TradeDirection.Long));
            Assert.That(restored.StopLossPercent,  Is.EqualTo(5));
            Assert.That(restored.TakeProfitTargets, Has.Count.EqualTo(3));
            Assert.That(restored.EntryConditions.Select(c => c.ToScript()),
                Is.EqualTo(def.EntryConditions.Select(c => c.ToScript())));
        });
    }

    // ── Exit fields survival ──────────────────────────────────────────────

    [Test]
    public void Strategy_AllExitFields_RoundTrip()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        def.StopLossPercent      = 5;
        def.TrailingStopPercent  = 8;
        def.PeakGivebackPercent  = 25;
        def.PeakGivebackArmTime  = new TimeSpan(9, 15, 0);
        def.ExitTime             = new TimeSpan(9, 28, 0);
        def.ExitAtPriorHigh      = true;
        def.RollingHighDays      = 3;
        def.RollingHighBuffer    = 0.25;
        def.RollingLowDays       = 5;
        def.RollingLowBuffer     = 0.10;
        def.EntryRollingLowDays  = 7;
        def.EntryRollingLowBuffer = 0.15;
        def.EntryRollingHighDays = 10;
        def.EntryRollingHighBuffer = 0.20;
        def.NotionalAmount       = 2500m;

        var restored = RoundTrip(def);

        Assert.Multiple(() =>
        {
            Assert.That(restored.StopLossPercent,      Is.EqualTo(5),    "StopLossPercent");
            Assert.That(restored.TrailingStopPercent,  Is.EqualTo(8),    "TrailingStopPercent");
            Assert.That(restored.PeakGivebackPercent,  Is.EqualTo(25),   "PeakGivebackPercent");
            Assert.That(restored.PeakGivebackArmTime,  Is.EqualTo(new TimeSpan(9, 15, 0)), "PeakGivebackArmTime");
            Assert.That(restored.ExitTime,             Is.EqualTo(new TimeSpan(9, 28, 0)), "ExitTime");
            Assert.That(restored.ExitAtPriorHigh,      Is.True,          "ExitAtPriorHigh");
            Assert.That(restored.RollingHighDays,      Is.EqualTo(3),    "RollingHighDays");
            Assert.That(restored.RollingHighBuffer,    Is.EqualTo(0.25), "RollingHighBuffer");
            Assert.That(restored.RollingLowDays,       Is.EqualTo(5),    "RollingLowDays");
            Assert.That(restored.RollingLowBuffer,     Is.EqualTo(0.10), "RollingLowBuffer");
            Assert.That(restored.EntryRollingLowDays,  Is.EqualTo(7),    "EntryRollingLowDays");
            Assert.That(restored.EntryRollingLowBuffer, Is.EqualTo(0.15), "EntryRollingLowBuffer");
            Assert.That(restored.EntryRollingHighDays, Is.EqualTo(10),   "EntryRollingHighDays");
            Assert.That(restored.EntryRollingHighBuffer, Is.EqualTo(0.20),"EntryRollingHighBuffer");
            Assert.That(restored.NotionalAmount,       Is.EqualTo(2500m), "NotionalAmount");
        });
    }

    // ── ConditionalBlocks survival ────────────────────────────────────────

    [Test]
    public void ConditionalBlock_AllOverrideableFields_RoundTrip()
    {
        // StrategyOverrides intentionally exposes only the fields a branch
        // can change at run-time: Direction, entry conditions, take-profit
        // targets, and stop/trailing. The 9 exit-only fields on
        // StrategyDefinition (ExitAtPriorHigh, Rolling*) are not overrideable
        // by branch logic — they are set once on the base definition.
        var def = Stock.Ticker("TEST").Long().Build();
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new IndicatorCondition(IndicatorType.VwapAbove),
            Overrides = new StrategyOverrides
            {
                Direction        = TradeDirection.Short,
                TakeProfitPrice  = 12.0,
                StopLossPercent  = 3.0,
                TrailingStopPercent = 7.0,
            },
        });
        block.Branches.Add(new ConditionalBranch
        {
            Condition = null, // else
            Overrides = new StrategyOverrides { Direction = TradeDirection.Long },
        });
        def.ConditionalBlocks.Add(block);

        var restored = RoundTrip(def);

        var o = restored.ConditionalBlocks[0].Branches[0].Overrides;
        Assert.Multiple(() =>
        {
            Assert.That(o.Direction,            Is.EqualTo(TradeDirection.Short), "Direction");
            Assert.That(o.TakeProfitPrice,      Is.EqualTo(12.0), "TakeProfitPrice");
            Assert.That(o.StopLossPercent,      Is.EqualTo(3.0),  "StopLossPercent");
            Assert.That(o.TrailingStopPercent,  Is.EqualTo(7.0),  "TrailingStopPercent");
            Assert.That(restored.ConditionalBlocks[0].Branches[1].Condition, Is.Null, "else branch");
            Assert.That(restored.ConditionalBlocks[0].Branches[1].Overrides.Direction,
                Is.EqualTo(TradeDirection.Long), "else Direction");
        });
    }

    // ── Fail-closed reads ─────────────────────────────────────────────────

    [Test]
    public void Deserialize_UnknownIndicatorType_Throws()
    {
        const string json = """
            { "schemaVersion": 1, "symbol": "TST",
              "entryConditions": [ { "type": "indicator", "indicator": "QuantumEntanglement" } ] }
            """;
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("QuantumEntanglement"));
    }

    [Test]
    public void Deserialize_UnknownPatternType_Throws()
    {
        const string json = """
            { "schemaVersion": 1, "symbol": "TST",
              "entryConditions": [ { "type": "pattern", "pattern": "HolyGrail" } ] }
            """;
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("HolyGrail"));
    }

    [Test]
    public void Deserialize_UnknownPriceLevelKind_Throws()
    {
        const string json = """
            { "schemaVersion": 1, "symbol": "TST",
              "entryConditions": [ { "type": "priceLevel", "kind": "MysteriousLevel", "level": 10 } ] }
            """;
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("MysteriousLevel"));
    }

    [Test]
    public void Deserialize_MissingSchemaVersion_Throws()
    {
        Assert.That(() => StrategyJson.Deserialize("""{ "symbol": "TST" }"""),
            Throws.TypeOf<StrategyJsonException>());
    }

    [Test]
    public void Deserialize_UnknownProperty_Throws()
    {
        const string json = """{ "schemaVersion": 1, "symbol": "TST", "unknownGate": true }""";
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("unknownGate"));
    }

    [Test]
    public void Deserialize_NonFiniteDouble_Throws()
    {
        const string json = """{ "schemaVersion": 1, "symbol": "TST", "stopLossPercent": 1e400 }""";
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>());
    }
}
