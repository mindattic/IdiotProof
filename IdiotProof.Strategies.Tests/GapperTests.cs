using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Gapper epic (RFC 0002 / IP-A8) — DSL lifecycle verbs, gap evaluation, the
/// profile→script factory round trip, and the momentum-rollover exit brain.
/// Times: 09:00 ET == 13:00 UTC in July (EDT, UTC-4); these tests pin UTC
/// instants on 2026-07-17 (a Friday) so the ET conversion is deterministic.
/// </summary>
public class GapperTests
{
    // 2026-07-17 (EDT): 04:30 ET = 08:30 UTC, 09:20 ET = 13:20 UTC, etc.
    private static DateTime Utc(int hour, int minute) => new(2026, 7, 17, hour, minute, 0, DateTimeKind.Utc);

    private static IndicatorSnapshot Snap(double price, DateTime timestampUtc, double? previousClose = null) => new()
    {
        Symbol = "TEST",
        Price = price,
        Timestamp = timestampUtc,
        PreviousClose = previousClose,
    };

    private static GapperProfile Profile() => new()
    {
        Id = "test",
        Name = "Test",
        MinGapPercent = 5,
        MaxGapPercent = 20,
        MinVolumeRatio = 2,
        MinPrice = 1,
        MaxPrice = 50,
        EntryWindowStartEt = "04:00",
        EntryWindowEndEt = "09:00",
        StopLossPercent = 5,
        TrailingStopPercent = 8,
        PeakGivebackPercent = 25,
        ArmExitAtEt = "09:15",
        SellByEt = "09:28",
        DefaultNotional = 1000m,
    };

    // ── Gap conditions ──────────────────────────────────────────────────

    [Test]
    public void IsGapUp_FailsClosed_WithoutPreviousClose()
    {
        var cond = new IndicatorCondition(IndicatorType.GapUp, 5);
        Assert.That(cond.Evaluate(Snap(10.60, Utc(8, 30))), Is.False,
            "no PreviousClose → gap unknown → must NOT pass");
    }

    [Test]
    public void IsGapUp_Passes_WhenGapMeetsThreshold()
    {
        var cond = new IndicatorCondition(IndicatorType.GapUp, 5);
        Assert.Multiple(() =>
        {
            Assert.That(cond.Evaluate(Snap(10.60, Utc(8, 30), previousClose: 10.00)), Is.True, "+6% gap passes min 5%");
            Assert.That(cond.Evaluate(Snap(10.20, Utc(8, 30), previousClose: 10.00)), Is.False, "+2% gap fails min 5%");
        });
    }

    [Test]
    public void IsGapBetween_EnforcesBandAndFailsClosed()
    {
        var band = new GapBandCondition(5, 20);
        Assert.Multiple(() =>
        {
            Assert.That(band.Evaluate(Snap(11.00, Utc(8, 30), 10.00)), Is.True, "+10% inside [5,20]");
            Assert.That(band.Evaluate(Snap(13.00, Utc(8, 30), 10.00)), Is.False, "+30% above band — already gone");
            Assert.That(band.Evaluate(Snap(10.30, Utc(8, 30), 10.00)), Is.False, "+3% below band");
            Assert.That(band.Evaluate(Snap(11.00, Utc(8, 30))), Is.False, "no PreviousClose → fail closed");
        });
    }

    // ── Entry window ────────────────────────────────────────────────────

    [Test]
    public void TimeWindowCondition_GatesOnEasternClock()
    {
        var window = new TimeWindowCondition(TimeSpan.FromHours(4), TimeSpan.FromHours(9));
        Assert.Multiple(() =>
        {
            Assert.That(window.Evaluate(Snap(10, Utc(8, 30))), Is.True, "04:30 ET inside 04:00–09:00");
            Assert.That(window.Evaluate(Snap(10, Utc(13, 30))), Is.False, "09:30 ET outside window");
            Assert.That(window.Evaluate(Snap(10, Utc(7, 59))), Is.False, "03:59 ET before window");
        });
    }

    [Test]
    public void TimeWindowCondition_WrapsOvernightWindows()
    {
        var overnight = new TimeWindowCondition(TimeSpan.FromHours(20), TimeSpan.FromHours(4));
        Assert.Multiple(() =>
        {
            Assert.That(overnight.Evaluate(Snap(10, Utc(1, 0))), Is.True, "21:00 ET (prev day) inside 20:00→04:00");
            Assert.That(overnight.Evaluate(Snap(10, Utc(14, 0))), Is.False, "10:00 ET outside overnight window");
        });
    }

    // ── Factory + round trip ────────────────────────────────────────────

    [Test]
    public void GapperScriptFactory_Script_SurvivesParserRoundTrip()
    {
        var script = GapperScriptFactory.ToScript("abcd", Profile());
        var parsed = ScriptParser.ParseScript(script);

        Assert.That(parsed, Is.Not.Null);
        var labels = parsed!.EntryConditions.Select(c => c.ToScript()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Symbol, Is.EqualTo("ABCD"));
            Assert.That(parsed.Name, Is.EqualTo("ABCD Gapper — Test"),
                "Name() must survive parsing (was silently dropped — no parser case)");
            Assert.That(parsed.Session, Is.EqualTo(TradingSession.Premarket), "Session() must survive parsing");
            Assert.That(labels, Does.Contain("RequireEntryWindow(\"04:00\", \"09:00\")"));
            Assert.That(labels, Does.Contain("IsGapBetween(5, 20)"));
            Assert.That(labels, Does.Contain("IsVolumeAbove(2)"));
            Assert.That(labels, Does.Contain("IsPriceBetween(1, 50)"));
            Assert.That(parsed.Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(parsed.NotionalAmount, Is.EqualTo(1000m), "notional sizing survives");
            Assert.That(parsed.StopLossPercent, Is.EqualTo(5), "stop-loss % survives");
            Assert.That(parsed.TrailingStopPercent, Is.EqualTo(8), "trailing stop survives");
            Assert.That(parsed.PeakGivebackPercent, Is.EqualTo(25), "giveback survives");
            Assert.That(parsed.PeakGivebackArmTime, Is.EqualTo(new TimeSpan(9, 15, 0)), "arm time survives");
            Assert.That(parsed.ExitTime, Is.EqualTo(new TimeSpan(9, 28, 0)), "sell-by survives");
        });
    }

    [Test]
    public void GapperScriptFactory_OpenEndedGap_EmitsIsGapUp()
    {
        var p = Profile();
        p.MaxGapPercent = null;
        var parsed = ScriptParser.ParseScript(GapperScriptFactory.ToScript("XYZ", p));
        Assert.That(parsed!.EntryConditions.Select(c => c.ToScript()), Does.Contain("IsGapUp(5)"));
    }

    [Test]
    public void GapperProfile_Validate_CatchesBadDialIns()
    {
        var p = Profile();
        p.StopLossPercent = 0;
        p.SellByEt = "9:99";
        var problems = p.Validate();
        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Contains("Stop loss"));
            Assert.That(problems, Has.Some.Contains("Sell-by"));
        });
        Assert.That(() => GapperScriptFactory.ToScript("XYZ", p), Throws.ArgumentException);
    }

    [Test]
    public void GapperProfile_Validate_RejectsInvertedEntryWindow()
    {
        // An inverted window (start >= end) is evaluated by TimeWindowCondition
        // as an overnight wrap, opening entries OUTSIDE the intended premarket
        // slot — reachable via the LLM transcript interpreter, so it must be
        // caught at validation, not discovered live.
        var p = Profile();
        p.EntryWindowStartEt = "09:00";
        p.EntryWindowEndEt = "04:00";
        Assert.That(p.Validate(), Has.Some.Contains("Entry window start must be before"));
    }

    [Test]
    public void GapperProfile_Validate_RejectsArmTimeAtOrAfterSellBy()
    {
        // Arm 09:28 / sell-by 09:28: the rollover exit can never fire before
        // the hard flatten — the user dialed a dead momentum exit.
        var p = Profile();
        p.ArmExitAtEt = "09:28";
        p.SellByEt = "09:28";
        Assert.That(p.Validate(), Has.Some.Contains("Arm-exit time must be before"));
    }

    // ── Momentum-rollover exit ──────────────────────────────────────────

    private static Candle Bar(DateTime endUtc, double high, double close) => new()
    {
        Symbol = "TEST",
        StartUtc = endUtc.AddMinutes(-1),
        EndUtc = endUtc,
        Open = (decimal)close,
        High = (decimal)high,
        Low = (decimal)Math.Min(close, high),
        Close = (decimal)close,
        Volume = 1000,
    };

    private static StrategyDefinition GapperDef() =>
        ScriptParser.ParseScript(GapperScriptFactory.ToScript("TEST", Profile()))!;

    [Test]
    public void Exit_PeakGiveback_SellsAfterMomentumRollsOver()
    {
        // Entry 10.00 at 05:00 ET; ran to 12.00 (run = 2.00, 25% giveback → floor 11.50).
        var def = GapperDef();
        var entryUtc = Utc(9, 0); // 05:00 ET
        var candles = new[]
        {
            Bar(Utc(12, 0), high: 11.00, close: 10.90),
            Bar(Utc(13, 0), high: 12.00, close: 11.90),
            Bar(Utc(13, 20), high: 11.90, close: 11.45), // 09:20 ET — armed, below 11.50 floor
        };

        var decision = GapperExitEvaluator.Evaluate(def, 10.00, entryUtc, candles, Utc(13, 20));

        Assert.That(decision, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decision!.Reason, Is.EqualTo(GapperExitReason.PeakGiveback));
            Assert.That(decision.PeakPrice, Is.EqualTo(12.00));
        });
    }

    [Test]
    public void Exit_PeakGiveback_NotArmedBeforeArmTime()
    {
        // Same rollover shape but at 08:00 ET — before the 09:15 arm. The
        // trailing stop (8% off peak = 11.04) hasn't tripped either at 11.45.
        var def = GapperDef();
        var entryUtc = Utc(9, 0);
        var candles = new[]
        {
            Bar(Utc(10, 0), high: 12.00, close: 11.90),
            Bar(Utc(12, 0), high: 11.90, close: 11.45),
        };

        var decision = GapperExitEvaluator.Evaluate(def, 10.00, entryUtc, candles, Utc(12, 0));
        Assert.That(decision, Is.Null, "rollover exit must stay dormant until 09:15 ET");
    }

    [Test]
    public void Exit_SellBy_AlwaysFlatBeforeTheBell()
    {
        // Price still strong at 09:28 ET — sell-by fires anyway.
        var def = GapperDef();
        var candles = new[] { Bar(Utc(13, 27), high: 12.00, close: 11.95) };

        var decision = GapperExitEvaluator.Evaluate(def, 10.00, Utc(9, 0), candles, Utc(13, 28));

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Reason, Is.EqualTo(GapperExitReason.SellByTime));
    }

    [Test]
    public void Exit_HardStop_TripsOnEntryDrawdown()
    {
        // Entry 10.00, stop 5% → 9.50. Price collapses to 9.40 with no run.
        var def = GapperDef();
        var candles = new[] { Bar(Utc(10, 0), high: 10.05, close: 9.40) };

        var decision = GapperExitEvaluator.Evaluate(def, 10.00, Utc(9, 0), candles, Utc(10, 0));

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Reason, Is.EqualTo(GapperExitReason.StopLoss));
    }

    [Test]
    public void Exit_TrailingStop_TripsOffPeakBeforeArmTime()
    {
        // Peak 12.00, trailing 8% → 11.04. Close 11.00 at 08:00 ET (rollover not armed yet).
        var def = GapperDef();
        var candles = new[]
        {
            Bar(Utc(10, 0), high: 12.00, close: 11.90),
            Bar(Utc(12, 0), high: 11.90, close: 11.00),
        };

        var decision = GapperExitEvaluator.Evaluate(def, 10.00, Utc(9, 0), candles, Utc(12, 0));

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Reason, Is.EqualTo(GapperExitReason.TrailingStop));
    }

    [Test]
    public void Exit_HoldsWhileMomentumIntact()
    {
        // Armed, but price 11.60 is above the 11.50 giveback floor — keep holding.
        var def = GapperDef();
        var candles = new[]
        {
            Bar(Utc(13, 0), high: 12.00, close: 11.90),
            Bar(Utc(13, 20), high: 11.95, close: 11.60),
        };

        var decision = GapperExitEvaluator.Evaluate(def, 10.00, Utc(9, 0), candles, Utc(13, 20));
        Assert.That(decision, Is.Null);
    }
}
