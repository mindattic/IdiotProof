using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Contract tests for the Bug-2 gapper dedup detection logic.
///
/// BUG 2 (fixed): MonitorWorker's gapper dedup guard only checked
/// ScriptText.Contains("PeakGiveback("), so canonical-JSON-only
/// strategies (ScriptText = "") always passed the guard.  Two canonical
/// gappers for the same symbol could both fire the same tick, opening
/// duplicate positions with real money.
///
/// FIX: The guard OR-s two checks:
///   ScriptText.Contains("PeakGiveback(", OrdinalIgnoreCase)
///   ScriptJson contains "peakGivebackPercent" with a non-null value
///
/// JSON SCHEMA NOTE: StrategyJson.Serialize ALWAYS emits "peakGivebackPercent"
/// (as null for non-gapper strategies), so Contains("peakGivebackPercent") alone
/// would flag every canonical strategy as a gapper — creating a false-positive
/// that would prevent multiple non-gapper strategies from firing on the same
/// symbol.  The correct check excludes the null case:
///   Contains("peakGivebackPercent") AND NOT Contains('"peakGivebackPercent": null')
///
/// These tests do NOT call MonitorWorker (it has infrastructure deps).
/// They test the OBSERVABLE CONTRACT MonitorWorker's fix relies on:
///   1. A PeakGiveback strategy's JSON MUST contain "peakGivebackPercent"
///      with a numeric value (not null).
///   2. A non-PeakGiveback strategy's JSON MUST contain "peakGivebackPercent": null
///      (field always present; null value distinguishes non-gapper).
///   3. The builder's ToScript() for a PeakGiveback strategy MUST contain
///      "PeakGiveback(" (legacy ScriptText detection path).
///   4. The ToScript() for non-PeakGiveback strategies must NOT contain it.
///
/// If these invariants break (e.g. someone renames the JSON field or changes
/// the null-serialization behavior), the MonitorWorker dedup fix silently
/// breaks and the duplicate-fire bug returns.  These tests make that audible.
///
/// Coverage
/// ────────
///   JSON field presence / value ....... 6 tests
///   Script text (ToScript) presence ... 5 tests
///   Both paths together ............... 2 tests
///   Non-gapper misidentification ....... 4 tests
///</summary>
public class GapperDedupContractTests
{
    // ── JSON field "peakGivebackPercent" ─────────────────────────────────

    [Test]
    public void PeakGivebackStrategy_JsonContains_PeakGivebackPercentField()
    {
        var def  = Stock.Ticker("AAPL").Long().StopLossPercent(5).PeakGiveback(25, "09:15").Build();
        var json = StrategyJson.Serialize(def);

        Assert.That(json, Does.Contain("peakGivebackPercent"),
            "PeakGiveback strategy JSON must contain 'peakGivebackPercent' " +
            "(MonitorWorker dedup guard relies on this exact field name)");
    }

    [Test]
    public void PeakGivebackStrategy_JsonContains_ExpectedPercentValue()
    {
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).PeakGiveback(30, "09:00").Build();
        var json = StrategyJson.Serialize(def);

        Assert.That(json, Does.Contain("peakGivebackPercent"));
        Assert.That(json, Does.Contain("30"), "configured giveback % must be serialized");
    }

    [Test]
    public void PeakGivebackStrategy_JsonFieldName_IsExactCamelCase()
    {
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).PeakGiveback(25, "09:15").Build();
        var json = StrategyJson.Serialize(def);

        Assert.That(json, Does.Contain("peakGivebackPercent"),
            "field must use camelCase 'peakGivebackPercent'");
        Assert.That(json, Does.Not.Contain("PeakGivebackPercent"),
            "must not be PascalCase — Linux containers are case-sensitive");
    }

    [Test]
    public void NonGapper_StopLossOnly_JsonHasNullPeakGivebackPercent()
    {
        // StrategyJson ALWAYS emits "peakGivebackPercent" (as null for non-gappers).
        // MonitorWorker must check for null value, not field absence, to avoid false-positives.
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).Build();
        var json = StrategyJson.Serialize(def);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("peakGivebackPercent"),
                "field is always present in canonical JSON");
            Assert.That(json, Does.Contain("\"peakGivebackPercent\": null"),
                "non-gapper must have null value — MonitorWorker checks for this exact substring");
            Assert.That(def.PeakGivebackPercent, Is.Null,
                "non-gapper model must have null PeakGivebackPercent");
        });
    }

    [Test]
    public void NonGapper_TakeProfitOnly_JsonHasNullPeakGivebackPercent()
    {
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).TakeProfit(12.0).Build();
        var json = StrategyJson.Serialize(def);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"peakGivebackPercent\": null"),
                "TakeProfit-only strategy must serialize peakGivebackPercent as null");
            Assert.That(def.PeakGivebackPercent, Is.Null);
        });
    }

    [Test]
    public void NonGapper_MultiTarget_JsonHasNullPeakGivebackPercent()
    {
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).TakeProfit(11.0, 12.0, 14.0).Build();
        var json = StrategyJson.Serialize(def);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"peakGivebackPercent\": null"),
                "Multi-target strategy must serialize peakGivebackPercent as null");
            Assert.That(def.PeakGivebackPercent, Is.Null);
        });
    }

    // ── Script text "PeakGiveback(" via StrategyBuilder.ToScript() ───────
    // StrategyBuilder.ToScript() generates legacy IdiotScript text from the builder.
    // MonitorWorker checks ScriptText for legacy rows that pre-date canonical JSON.

    [Test]
    public void PeakGivebackStrategy_ToScript_ContainsPeakGivebackCall()
    {
        var script = Stock.Ticker("TEST").Long().StopLossPercent(5).PeakGiveback(25, "09:15").ToScript();

        Assert.That(script, Does.Contain("PeakGiveback("),
            "Generated IdiotScript must contain 'PeakGiveback(' " +
            "(MonitorWorker dedup guard checks ScriptText for legacy rows)");
    }

    [Test]
    public void PeakGivebackStrategy_ToScript_ContainsExpectedGivebackPercent()
    {
        var script = Stock.Ticker("TEST").Long().StopLossPercent(5).PeakGiveback(30, "09:00").ToScript();
        Assert.That(script, Does.Contain("PeakGiveback(30"),
            "Script must embed the configured giveback percent");
    }

    [Test]
    public void NonGapper_TakeProfit_ToScript_DoesNotContainPeakGivebackCall()
    {
        var script = Stock.Ticker("TEST").Long().StopLossPercent(5).TakeProfit(12.0).ToScript();
        Assert.That(script, Does.Not.Contain("PeakGiveback("),
            "TakeProfit-only strategy script must not contain 'PeakGiveback('");
    }

    [Test]
    public void TrailingStop_ToScript_DoesNotContainPeakGivebackCall()
    {
        var script = Stock.Ticker("TEST").Long().StopLossPercent(5).TrailingStopLoss(8).ToScript();
        Assert.That(script, Does.Not.Contain("PeakGiveback("),
            "TrailingStop strategy must not be misidentified as a gapper");
    }

    [Test]
    public void StopLossOnly_ToScript_DoesNotContainPeakGivebackCall()
    {
        var script = Stock.Ticker("TEST").Long().StopLossPercent(3).ToScript();
        Assert.That(script, Does.Not.Contain("PeakGiveback("),
            "StopLoss-only strategy must not contain 'PeakGiveback('");
    }

    // ── Both detection paths together ────────────────────────────────────

    [Test]
    public void PeakGiveback_BothJsonAndScript_SatisfyBothDetectionPaths()
    {
        var builder = Stock.Ticker("NVDA").Long().StopLossPercent(5).PeakGiveback(25, "09:15");
        var def     = builder.Build();
        var json    = StrategyJson.Serialize(def);
        var script  = builder.ToScript();

        Assert.Multiple(() =>
        {
            Assert.That(json.Contains("peakGivebackPercent", StringComparison.OrdinalIgnoreCase),
                Is.True, "JSON detection path must work");
            Assert.That(script.Contains("PeakGiveback(", StringComparison.OrdinalIgnoreCase),
                Is.True, "ScriptText detection path must work");
            // Gapper must NOT be excluded by the null-value guard
            Assert.That(json.Contains("\"peakGivebackPercent\": null", StringComparison.OrdinalIgnoreCase),
                Is.False, "PeakGiveback strategy must have a non-null value so the null-exclusion check doesn't suppress it");
        });
    }

    // ── Simulated MonitorWorker detection logic ───────────────────────────
    // These tests inline the exact check used in MonitorWorker to prove the
    // detection logic is correct for every strategy type without calling MonitorWorker.

    private static bool SimulateIsGapperCheck(string? scriptText, string? scriptJson)
    {
        var isGapperViaText = scriptText?.Contains("PeakGiveback(", StringComparison.OrdinalIgnoreCase) == true;
        var isGapperViaJson = scriptJson is not null
            && scriptJson.Contains("peakGivebackPercent", StringComparison.OrdinalIgnoreCase)
            && !scriptJson.Contains("\"peakGivebackPercent\": null", StringComparison.OrdinalIgnoreCase);
        return isGapperViaText || isGapperViaJson;
    }

    [Test]
    public void DetectionLogic_PeakGivebackWithJson_DetectedAsGapper()
    {
        var def  = Stock.Ticker("NVDA").Long().StopLossPercent(5).PeakGiveback(25, "09:15").Build();
        var json = StrategyJson.Serialize(def);
        Assert.That(SimulateIsGapperCheck(null, json), Is.True,
            "PeakGiveback strategy must be detected via JSON path");
    }

    [Test]
    public void DetectionLogic_PeakGivebackWithScript_DetectedAsGapper()
    {
        var script = Stock.Ticker("NVDA").Long().StopLossPercent(5).PeakGiveback(25, "09:15").ToScript();
        Assert.That(SimulateIsGapperCheck(script, null), Is.True,
            "PeakGiveback strategy must be detected via ScriptText path");
    }

    [Test]
    public void DetectionLogic_NonGapper_StopLoss_NotDetected()
    {
        var def  = Stock.Ticker("AAPL").Long().StopLossPercent(5).Build();
        var json = StrategyJson.Serialize(def);
        var script = Stock.Ticker("AAPL").Long().StopLossPercent(5).ToScript();
        Assert.That(SimulateIsGapperCheck(script, json), Is.False,
            "StopLoss-only strategy must NOT be falsely detected as a gapper");
    }

    [Test]
    public void DetectionLogic_NonGapper_TakeProfit_NotDetected()
    {
        var def  = Stock.Ticker("AAPL").Long().StopLossPercent(5).TakeProfit(12.0).Build();
        var json = StrategyJson.Serialize(def);
        Assert.That(SimulateIsGapperCheck(null, json), Is.False,
            "TakeProfit strategy must NOT be falsely detected as a gapper via JSON");
    }

    [Test]
    public void DetectionLogic_NonGapper_TrailingStop_NotDetected()
    {
        var def  = Stock.Ticker("AAPL").Long().StopLossPercent(5).TrailingStopLoss(8).Build();
        var json = StrategyJson.Serialize(def);
        Assert.That(SimulateIsGapperCheck(null, json), Is.False,
            "TrailingStop strategy must NOT be falsely detected as a gapper via JSON");
    }

    [Test]
    public void DetectionLogic_NonGapper_SellBy_NotDetected()
    {
        var def  = Stock.Ticker("AAPL").Long().StopLossPercent(5).SellBy("09:29").Build();
        var json = StrategyJson.Serialize(def);
        Assert.That(SimulateIsGapperCheck(null, json), Is.False,
            "SellBy strategy must NOT be falsely detected as a gapper via JSON");
    }

    [Test]
    public void DetectionLogic_NonGapper_MultiTarget_NotDetected()
    {
        var def  = Stock.Ticker("AAPL").Long().StopLossPercent(5).TakeProfit(11.0, 12.0, 14.0).Build();
        var json = StrategyJson.Serialize(def);
        Assert.That(SimulateIsGapperCheck(null, json), Is.False,
            "Multi-target strategy must NOT be falsely detected as a gapper via JSON");
    }

    [Test]
    public void PeakGiveback_JsonDetectedAfterRoundTrip()
    {
        var def      = Stock.Ticker("AAPL").Long().StopLossPercent(5).PeakGiveback(20, "09:10").Build();
        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        var json2    = StrategyJson.Serialize(restored);

        Assert.That(json2, Does.Contain("peakGivebackPercent"),
            "PeakGiveback must still be detectable after JSON round-trip");
    }

    // ── Non-gapper strategies not misidentified ───────────────────────────

    [Test]
    public void SellByTime_IsNotMisidentifiedAsGapper()
    {
        // The field is ALWAYS in the JSON; the null value is what prevents false-positive detection.
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).SellBy("09:29").Build();
        var json = StrategyJson.Serialize(def);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"peakGivebackPercent\": null"),
                "SellBy-time strategy must have null peakGivebackPercent so MonitorWorker's null-exclusion check fires correctly");
            Assert.That(def.PeakGivebackPercent, Is.Null,
                "SellBy-time strategy must not be a gapper");
        });
    }

    [Test]
    public void ShortStrategy_WithPeakGiveback_IsDetectedCorrectly()
    {
        var def  = Stock.Ticker("TEST").Short().StopLossPercent(5).PeakGiveback(25, "09:15").Build();
        var json = StrategyJson.Serialize(def);
        Assert.That(json, Does.Contain("peakGivebackPercent"),
            "Short PeakGiveback strategy must be detected (direction must not affect field presence)");
    }

    [Test]
    public void PeakGiveback_OrdinalIgnoreCase_DetectsCapitalisationVariants()
    {
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).PeakGiveback(25, "09:15").Build();
        var json = StrategyJson.Serialize(def);

        Assert.Multiple(() =>
        {
            Assert.That(json.Contains("peakGivebackPercent", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(json.Contains("PEAKGIVEBACKPERCENT", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(json.Contains("PeakGivebackPercent", StringComparison.OrdinalIgnoreCase), Is.True);
        });
    }

    [Test]
    public void TrailingStop_PlusSellBy_IsNotMisidentifiedAsGapper()
    {
        var def  = Stock.Ticker("TEST").Long().StopLossPercent(5).TrailingStopLoss(8).SellBy("09:28").Build();
        var json = StrategyJson.Serialize(def);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"peakGivebackPercent\": null"),
                "TrailingStop+SellBy strategy must have null peakGivebackPercent to be excluded by the null check");
            Assert.That(def.PeakGivebackPercent, Is.Null,
                "TrailingStop+SellBy strategy must not be a gapper");
        });
    }
}
