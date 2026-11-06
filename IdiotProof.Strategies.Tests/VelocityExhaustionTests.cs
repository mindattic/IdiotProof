using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The VelocityExhaustion exit through the DSL and the gapper ladder: where it sits (after stops and
/// targets, before PeakGiveback), that it mirrors for shorts, that the verbs round-trip through text
/// and canonical JSON, that IndicatorSnapshotBuilder carries the velocity fields, and that a strategy
/// without a velocity exit serializes exactly as before (IP-LAW-8).
/// All times 2026-07-17 (EDT, UTC-4): entry at 04:30 ET = 08:30 UTC.
/// </summary>
public class VelocityExhaustionTests
{
    private static readonly DateTime EntryUtc = new(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);

    private static Candle Bar(int minutesAfterEntry, double close, double? high = null, double? low = null, double volume = 10_000)
    {
        var start = EntryUtc.AddMinutes(minutesAfterEntry);
        return new Candle
        {
            Symbol = "T", StartUtc = start, EndUtc = start.AddMinutes(1),
            Open = (decimal)close, High = (decimal)(high ?? close), Low = (decimal)(low ?? close), Close = (decimal)close,
            Volume = (decimal)volume,
        };
    }

    /// <summary>Five bars climbing from 10 to <paramref name="peak"/>, then <paramref name="stall"/> bars flat at <paramref name="last"/> (default: at the peak).</summary>
    private static List<Candle> RunThenStall(double peak, int stall = 4, double? last = null)
    {
        var bars = new List<Candle>();
        for (int i = 1; i <= 5; i++) bars.Add(Bar(i - 1, 10 + (peak - 10) * i / 5));
        for (int i = 0; i < stall; i++) bars.Add(Bar(5 + i, last ?? peak, high: peak));
        return bars;
    }

    private static DateTime Now(List<Candle> bars) => bars[^1].EndUtc;

    private static StrategyBuilder Velocity(double pctPerMin = 0.5, int window = 3, string? arm = null) =>
        Stock.Ticker("T").Long().VelocityExhaustion(pctPerMin, window, arm);

    // ── Ladder order ──────────────────────────────────────────────────────

    [Test]
    public void Evaluate_VelocityDied_ReturnsVelocityExhaustion()
    {
        var bars = RunThenStall(10.5);
        var decision = GapperExitEvaluator.Evaluate(Velocity().Build(), 10, EntryUtc, bars, Now(bars));
        Assert.That(decision?.Reason, Is.EqualTo(GapperExitReason.VelocityExhaustion));
        Assert.That(decision!.Detail, Does.Contain("velocity"));
    }

    [Test]
    public void Evaluate_NoVelocityExit_IgnoresADeadVelocity()
    {
        var bars = RunThenStall(10.5);
        Assert.That(GapperExitEvaluator.Evaluate(Stock.Ticker("T").Long().Build(), 10, EntryUtc, bars, Now(bars)), Is.Null);
    }

    [Test]
    public void Evaluate_StopLossPreemptsVelocity()
    {
        var bars = RunThenStall(10.5, last: 9.4);
        var def = Velocity().StopLossPercent(5).Build();
        Assert.That(GapperExitEvaluator.Evaluate(def, 10, EntryUtc, bars, Now(bars))?.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    [Test]
    public void Evaluate_TrailingStopPreemptsVelocity()
    {
        var bars = RunThenStall(11, last: 10.2);
        var def = Velocity().TrailingStopLoss(5).Build();
        Assert.That(GapperExitEvaluator.Evaluate(def, 10, EntryUtc, bars, Now(bars))?.Reason, Is.EqualTo(GapperExitReason.TrailingStop));
    }

    [Test]
    public void Evaluate_TakeProfitPreemptsVelocity()
    {
        var bars = RunThenStall(11);
        var def = Velocity().TakeProfitPercent(8).Build();
        Assert.That(GapperExitEvaluator.Evaluate(def, 10, EntryUtc, bars, Now(bars))?.Reason, Is.EqualTo(GapperExitReason.TargetHit));
    }

    [Test]
    public void Evaluate_VelocityPreemptsPeakGiveback()
    {
        // Peak 11, now 10.5: a 50% giveback (PeakGiveback(25) fires) AND a negative 3-minute velocity.
        var bars = RunThenStall(11, stall: 0);
        bars.Add(Bar(5, 10.8, high: 10.8));
        bars.Add(Bar(6, 10.6, high: 10.6));
        bars.Add(Bar(7, 10.5, high: 10.5));
        bars.Add(Bar(8, 10.5, high: 10.5));
        var def = Velocity().PeakGiveback(25).Build();
        var decision = GapperExitEvaluator.Evaluate(def, 10, EntryUtc, bars, Now(bars));
        Assert.That(decision?.Reason, Is.EqualTo(GapperExitReason.VelocityExhaustion));
        Assert.That(decision!.PeakPrice, Is.EqualTo(11));
    }

    [Test]
    public void Evaluate_SellByPreemptsVelocity()
    {
        var bars = RunThenStall(10.5);
        var def = Velocity().SellBy("04:35").Build();
        Assert.That(GapperExitEvaluator.Evaluate(def, 10, EntryUtc, bars, Now(bars))?.Reason, Is.EqualTo(GapperExitReason.SellByTime));
    }

    [Test]
    public void Evaluate_Guards_HoldTheExit()
    {
        var bars = RunThenStall(10.5);
        Assert.Multiple(() =>
        {
            Assert.That(GapperExitEvaluator.Evaluate(Velocity().VelocityExhaustionMinRun(8).Build(), 10, EntryUtc, bars, Now(bars)), Is.Null, "5% run under the 8% minimum");
            Assert.That(GapperExitEvaluator.Evaluate(Velocity(arm: "09:00").Build(), 10, EntryUtc, bars, Now(bars)), Is.Null, "not armed at 04:39 ET");
            Assert.That(GapperExitEvaluator.Evaluate(Velocity().VelocityExhaustionRequireLowerHigh().Build(), 10, EntryUtc, bars, Now(bars)), Is.Null, "no lower high");
            Assert.That(GapperExitEvaluator.Evaluate(Velocity().VelocityExhaustionRequireVwapLoss().Build(), 10, EntryUtc, bars, Now(bars)), Is.Null, "still above the session VWAP");
        });
    }

    [Test]
    public void Evaluate_TooFewBars_FailsClosed()
    {
        var bars = new List<Candle> { Bar(0, 10.5) };
        Assert.That(GapperExitEvaluator.Evaluate(Velocity().Build(), 10, EntryUtc, bars, Now(bars)), Is.Null);
    }

    [Test]
    public void EvaluateShort_VelocityOfTheDropDied_ReturnsVelocityExhaustion()
    {
        var bars = new List<Candle>();
        for (int i = 1; i <= 5; i++) bars.Add(Bar(i - 1, 10 - 0.1 * i));
        for (int i = 0; i < 4; i++) bars.Add(Bar(5 + i, 9.5));
        var def = Stock.Ticker("T").Short().VelocityExhaustion(0.5, 3).VelocityExhaustionMinRun(3).Build();
        var decision = GapperExitEvaluator.EvaluateShort(def, 10, EntryUtc, bars, Now(bars));
        Assert.That(decision?.Reason, Is.EqualTo(GapperExitReason.VelocityExhaustion));
        Assert.That(decision!.Detail, Does.StartWith("Short:"));
    }

    [Test]
    public void EvaluateShort_StopPreemptsVelocity()
    {
        var bars = new List<Candle>();
        for (int i = 0; i < 9; i++) bars.Add(Bar(i, 10.6, high: 10.6, low: 10.6));
        var def = Stock.Ticker("T").Short().VelocityExhaustion(0.5, 3).StopLossPercent(5).Build();
        Assert.That(GapperExitEvaluator.EvaluateShort(def, 10, EntryUtc, bars, Now(bars))?.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    // ── DSL round trips ───────────────────────────────────────────────────

    private static StrategyDefinition FullVelocityStrategy() => Stock.Ticker("GAPT")
        .Session(TradingSession.Premarket)
        .IsGapUp(10).VelocityAbove(0.75, 1).Decelerating().VolumeRocBelow(0.8).VelocityBelow(0.2, 5)
        .Long().Quantity(1)
        .StopLossPercent(5)
        .VelocityExhaustion(0.5, 3, "08:30")
        .VelocityExhaustionMinRun(4)
        .VelocityExhaustionRequireLowerHigh()
        .VelocityExhaustionRequireVwapLoss()
        .VelocityExhaustionExtensionSwitch(10)
        .SellBy("09:28")
        .Build();

    [Test]
    public void VelocityVerbs_RoundTripThroughScriptText()
    {
        var original = FullVelocityStrategy();
        var script = original.ToScript();
        Assert.That(script, Does.Contain("VelocityExhaustion(0.5, 3, \"08:30\")")
            .And.Contain("VelocityExhaustionMinRun(4)")
            .And.Contain("VelocityExhaustionRequireLowerHigh()")
            .And.Contain("VelocityExhaustionRequireVwapLoss()")
            .And.Contain("VelocityExhaustionExtensionSwitch(10)")
            .And.Contain("VelocityAbove(0.75, 1)")
            .And.Contain("Decelerating()")
            .And.Contain("VolumeRocBelow(0.8)")
            .And.Contain("VelocityBelow(0.2, 5)"));

        var parsed = ScriptParser.ParseScript(script)!;
        Assert.Multiple(() =>
        {
            Assert.That(StrategyJson.Serialize(parsed), Is.EqualTo(StrategyJson.Serialize(original)));
            Assert.That(ScriptDiagnostics.Analyze(script).Where(d => d.IsError), Is.Empty);
        });
    }

    [Test]
    public void VelocityExitFields_RoundTripThroughCanonicalJson()
    {
        var original = FullVelocityStrategy();
        var json = StrategyJson.Serialize(original);
        var restored = StrategyJson.Deserialize(json);
        Assert.Multiple(() =>
        {
            Assert.That(restored.VelocityExhaustionPctPerMin, Is.EqualTo(0.5));
            Assert.That(restored.VelocityExhaustionWindowMinutes, Is.EqualTo(3));
            Assert.That(restored.VelocityExhaustionArmTime, Is.EqualTo(new TimeSpan(8, 30, 0)));
            Assert.That(restored.VelocityExhaustionMinRunPercent, Is.EqualTo(4));
            Assert.That(restored.VelocityExhaustionRequireLowerHigh, Is.True);
            Assert.That(restored.VelocityExhaustionRequireVwapLoss, Is.True);
            Assert.That(restored.VelocityExhaustionExtensionSwitchPct, Is.EqualTo(10));
            Assert.That(StrategyJson.Serialize(restored), Is.EqualTo(json));
            Assert.That(GapperExitEvaluator.VelocityParameters(restored), Is.EqualTo(new VelocityExitParameters(0.5, 3, new TimeSpan(8, 30, 0), 4, true, true, 10)));
        });
    }

    [Test]
    public void Deserialize_BadVelocityWindow_Throws()
    {
        const string json = """{ "schemaVersion": 1, "symbol": "TST", "velocityExhaustionPctPerMin": 0.5, "velocityExhaustionWindowMinutes": 2 }""";
        Assert.That(() => StrategyJson.Deserialize(json), Throws.TypeOf<StrategyJsonException>().With.Message.Contains("1, 3 or 5"));
        Assert.That(() => Stock.Ticker("T").VelocityExhaustion(0.5, 4), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // ── Byte-identical canon for a strategy without the new verbs ─────────

    [Test]
    public void Serialize_StrategyWithoutVelocityExit_IsByteIdenticalToTheOldCanon()
    {
        var def = Stock.Ticker("NVDA").IsGapUp(5).IsAboveVwap().Long()
            .StopLossPercent(5).TakeProfitPercent(10).PeakGiveback(25, "09:15").SellBy("09:28").Build();

        var expected = string.Join(Environment.NewLine,
        [
            "{",
            "  \"schemaVersion\": 1,",
            "  \"symbol\": \"NVDA\",",
            "  \"name\": null,",
            "  \"session\": \"RTH\",",
            "  \"quantity\": 0,",
            "  \"notionalAmount\": null,",
            "  \"direction\": \"Long\",",
            "  \"entryConditions\": [",
            "    {",
            "      \"type\": \"indicator\",",
            "      \"indicator\": \"GapUp\",",
            "      \"p1\": 5,",
            "      \"p2\": null,",
            "      \"phase\": \"Entry\"",
            "    },",
            "    {",
            "      \"type\": \"indicator\",",
            "      \"indicator\": \"VwapAbove\",",
            "      \"p1\": null,",
            "      \"p2\": null,",
            "      \"phase\": \"Entry\"",
            "    }",
            "  ],",
            "  \"takeProfitPrice\": null,",
            "  \"takeProfitPercent\": 10,",
            "  \"takeProfitTargets\": [],",
            "  \"stopLossPrice\": null,",
            "  \"stopLossPercent\": 5,",
            "  \"trailingStopPercent\": null,",
            "  \"exitTime\": \"09:28\",",
            "  \"peakGivebackPercent\": 25,",
            "  \"peakGivebackArmTime\": \"09:15\",",
            "  \"exitAtPriorHigh\": false,",
            "  \"rollingHighDays\": null,",
            "  \"rollingHighBuffer\": null,",
            "  \"rollingLowDays\": null,",
            "  \"rollingLowBuffer\": null,",
            "  \"entryRollingLowDays\": null,",
            "  \"entryRollingLowBuffer\": null,",
            "  \"entryRollingHighDays\": null,",
            "  \"entryRollingHighBuffer\": null,",
            "  \"isAutonomous\": false,",
            "  \"isAdaptive\": false,",
            "  \"shouldRepeat\": false,",
            "  \"conditionalBlocks\": []",
            "}",
        ]);

        var json = StrategyJson.Serialize(def);
        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo(expected));
            Assert.That(json, Does.Not.Contain("velocity"));
            Assert.That(def.ToScript(), Does.Not.Contain("Velocity"));
            Assert.That(def.ToStamp(), Does.Not.Contain("velocity").IgnoreCase);
        });
    }

    // ── IndicatorSnapshotBuilder ──────────────────────────────────────────

    [Test]
    public void SnapshotBuilder_FillsVelocityFields_FromWallClockWindows()
    {
        var bars = new List<Candle>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(i, 10 + 0.1 * i, volume: i >= 7 ? 2000 : 1000));
        var snap = IndicatorSnapshotBuilder.Build("T", bars);
        var now = bars[^1].EndUtc;
        Assert.Multiple(() =>
        {
            Assert.That(snap.Velocity1m, Is.EqualTo(MinuteBarMath.Velocity(bars, now, 1)));
            Assert.That(snap.Velocity3m, Is.EqualTo((10.9 / 10.6 - 1) * 100 / 3).Within(1e-9));
            Assert.That(snap.Velocity5m, Is.EqualTo((10.9 / 10.4 - 1) * 100 / 5).Within(1e-9));
            Assert.That(snap.Acceleration, Is.EqualTo(MinuteBarMath.Acceleration(bars, now)));
            Assert.That(snap.VolumeRoc, Is.EqualTo(2.0).Within(1e-12));
            Assert.That(snap.VolumeRatioTod, Is.Null, "no profile supplied");
            Assert.That(snap.VolumeRatio, Is.EqualTo(2000.0 / ((7 * 1000.0 + 2 * 2000.0) / 9)).Within(1e-9), "IsVolumeAbove's 20-bar baseline is unchanged");
        });
    }

    [Test]
    public void SnapshotBuilder_OneBar_LeavesVelocityNull()
    {
        var snap = IndicatorSnapshotBuilder.Build("T", [Bar(0, 10)]);
        Assert.Multiple(() =>
        {
            Assert.That(snap.Velocity1m, Is.Null);
            Assert.That(snap.Velocity3m, Is.Null);
            Assert.That(snap.Acceleration, Is.Null);
            Assert.That(snap.VolumeRoc, Is.Null);
        });
    }

    [Test]
    public void SnapshotBuilder_WithProfile_FillsVolumeRatioTod()
    {
        var day = new List<Candle>();
        for (int m = 0; m < TimeOfDayProfile.MinutesPerDay; m++)
        {
            var start = new DateTime(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc).AddMinutes(m);
            day.Add(new Candle { Symbol = "T", StartUtc = start, EndUtc = start.AddMinutes(1), Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 });
        }
        var profile = TimeOfDayProfile.Build([(2, day)]);
        var bars = new List<Candle>();
        for (int i = 0; i < 10; i++) bars.Add(Bar(i, 10, volume: 100));
        var snap = IndicatorSnapshotBuilder.BuildWithEmas("T", bars, [], null, new VolumeProfileContext(profile, 96_000, 2));
        // 1,000 shares by 04:40 ET against ADV 96,000 x the 40/960 of the day normally printed by then.
        Assert.That(snap.VolumeRatioTod, Is.EqualTo(1000.0 / (96_000 * 40.0 / 960)).Within(1e-9));
    }

    // ── Conditions evaluate off the snapshot ──────────────────────────────

    [Test]
    public void VelocityConditions_PickTheWindow_AndFailClosed()
    {
        var s = new Shared.IndicatorSnapshot { Symbol = "T", Price = 10, Velocity1m = 1.0, Velocity3m = 0.4, Velocity5m = null, Acceleration = -0.1, VolumeRoc = 0.5 };
        Assert.Multiple(() =>
        {
            Assert.That(Conditions.VelocityBelow(0.5).Evaluate(s), Is.True, "3-minute window by default");
            Assert.That(Conditions.VelocityBelow(0.5, 1).Evaluate(s), Is.False);
            Assert.That(Conditions.VelocityAbove(0.5, 1).Evaluate(s), Is.True);
            Assert.That(Conditions.VelocityAbove(0.1, 5).Evaluate(s), Is.False, "unknown 5-minute velocity fails closed");
            Assert.That(Conditions.VelocityBelow(0.5, 2).Evaluate(s), Is.False, "a window the snapshot doesn't carry fails closed");
            Assert.That(Conditions.Decelerating.Evaluate(s), Is.True);
            Assert.That(Conditions.VolumeRocBelow(0.8).Evaluate(s), Is.True);
            Assert.That(Conditions.VolumeRocBelow(0.4).Evaluate(s), Is.False);
        });
    }
}
