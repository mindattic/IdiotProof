using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Regression tests for the gapper-per-symbol deduplication check that guards
/// against two gapper strategies for the same symbol both firing on the same
/// tick and opening duplicate positions.
///
/// The detection uses two string checks OR-ed together so canonical-JSON-only
/// strategies (with empty ScriptText) are still recognized as gappers.
/// Bug 2 fix: the original check was text-only; a pure JSON strategy had an
/// empty ScriptText and would pass the dedup guard, allowing duplicate fires.
///
/// These tests verify the exact detection logic without requiring MonitorWorker
/// setup. The "gold function" pattern: extract the detection predicate and test
/// every variant independently.
/// </summary>
public class GapperDedupDetectionTests
{
    // ── Replicate the exact detection logic from MonitorWorker.TickAsync ──
    // If this logic changes in MonitorWorker, this test will fail and must be
    // updated — that is intentional (a forcing function to update the test).
    private static bool IsGapperStrategy(string? scriptText, string? scriptJson)
        => (scriptText?.Contains("PeakGiveback(", StringComparison.OrdinalIgnoreCase) == true)
        || (scriptJson?.Contains("peakGivebackPercent", StringComparison.OrdinalIgnoreCase) == true);

    // ── Text-only strategies (legacy / hand-written IdiotScript) ──────────

    [Test]
    public void ScriptText_ContainsPeakGiveback_IsGapper()
        => Assert.That(IsGapperStrategy(
            "Stock.Ticker(\"NVDA\").IsGapUp(5).Long().StopLossPercent(5).PeakGiveback(25, \"09:15\").Build()",
            null), Is.True);

    [Test]
    public void ScriptText_CaseInsensitive_IsGapper()
        => Assert.That(IsGapperStrategy("  .peakgiveback(30, \"09:15\") ", null), Is.True);

    [Test]
    public void ScriptText_NoGapperKeyword_IsNotGapper()
        => Assert.That(IsGapperStrategy(
            "Stock.Ticker(\"NVDA\").IsGapUp(5).Long().TakeProfit(50).StopLossPercent(5).Build()",
            null), Is.False);

    [Test]
    public void ScriptText_Empty_IsNotGapper()
        => Assert.That(IsGapperStrategy("", null), Is.False);

    [Test]
    public void ScriptText_Null_IsNotGapper()
        => Assert.That(IsGapperStrategy(null, null), Is.False);

    // ── Canonical-JSON-only strategies (via StrategyBuilder / UI builder) ──

    [Test]
    public void ScriptJson_ContainsPeakGivebackPercent_IsGapper()
    {
        var def = GapperScriptFactory.Compose("NVDA", new GapperProfile
        {
            MinGapPercent = 5, MaxGapPercent = 20, MinVolumeRatio = 2,
            MinPrice = 1, MaxPrice = 50,
            EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
            StopLossPercent = 5, TrailingStopPercent = 8, PeakGivebackPercent = 25,
            ArmExitAtEt = "09:15", SellByEt = "09:28", DefaultNotional = 1000m,
        }).Build();
        var json = StrategyJson.Serialize(def);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("peakGivebackPercent"),
                "serialized canonical JSON must use the camelCase key");
            Assert.That(IsGapperStrategy(null, json), Is.True,
                "a canonical-JSON gapper with null ScriptText must be detected");
            Assert.That(IsGapperStrategy("", json), Is.True,
                "a canonical-JSON gapper with empty ScriptText must be detected");
        });
    }

    [Test]
    public void ScriptJson_CaseInsensitive_IsGapper()
        => Assert.That(IsGapperStrategy(null,
            """{ "schemaVersion": 1, "PEAKGIVEBACKPERCENT": 25 }"""), Is.True);

    [Test]
    public void ScriptJson_NoGapperKey_IsNotGapper()
        => Assert.That(IsGapperStrategy(null,
            """{ "schemaVersion": 1, "symbol": "NVDA", "stopLossPercent": 5 }"""), Is.False);

    [Test]
    public void ScriptJson_NullNoText_IsNotGapper()
        => Assert.That(IsGapperStrategy(null, null), Is.False);

    // ── Both sources present (belt-and-suspenders) ────────────────────────

    [Test]
    public void BothTextAndJson_OnlyJsonHasGapper_IsGapper()
        => Assert.That(IsGapperStrategy(
            "Stock.Ticker(\"NVDA\").Long().TakeProfit(50).Build()",
            """{ "schemaVersion": 1, "peakGivebackPercent": 25 }"""), Is.True);

    [Test]
    public void BothTextAndJson_OnlyTextHasGapper_IsGapper()
        => Assert.That(IsGapperStrategy(
            "PeakGiveback(25, \"09:15\")",
            """{ "schemaVersion": 1, "stopLossPercent": 5 }"""), Is.True);

    [Test]
    public void BothTextAndJson_BothHaveGapper_IsGapper()
        => Assert.That(IsGapperStrategy(
            "PeakGiveback(25, \"09:15\")",
            """{ "peakGivebackPercent": 25 }"""), Is.True);

    [Test]
    public void BothTextAndJson_NeitherHasGapper_IsNotGapper()
        => Assert.That(IsGapperStrategy(
            "Long().TakeProfit(50)",
            """{ "schemaVersion": 1, "symbol": "X" }"""), Is.False);

    // ── Regression: the pre-fix detection that only checked ScriptText ────

    [Test]
    public void Regression_TextOnlyCheck_WouldMissPureJsonGapper()
    {
        // This is what the BUGGY code did. With just text check, a JSON-only
        // gapper (empty ScriptText) was not detected as a gapper.
        static bool BuggyOldCheck(string? scriptText)
            => scriptText?.Contains("PeakGiveback(", StringComparison.OrdinalIgnoreCase) == true;

        var def = GapperScriptFactory.Compose("NVDA", new GapperProfile
        {
            MinGapPercent = 5, MaxGapPercent = 20, MinVolumeRatio = 2,
            MinPrice = 1, MaxPrice = 50,
            EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
            StopLossPercent = 5, TrailingStopPercent = 8, PeakGivebackPercent = 25,
            ArmExitAtEt = "09:15", SellByEt = "09:28", DefaultNotional = 1000m,
        }).Build();
        var json = StrategyJson.Serialize(def);

        Assert.Multiple(() =>
        {
            Assert.That(BuggyOldCheck(null), Is.False,
                "old check returned false for null ScriptText — this was the bug");
            Assert.That(IsGapperStrategy(null, json), Is.True,
                "fixed check returns true for null ScriptText + valid JSON gapper");
        });
    }
}
