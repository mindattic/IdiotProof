using IdiotProof.Blazor.Services;
using IdiotProof.Scripting;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Enforces the actual guarantee behind the visualization fix: every DSL verb the
/// strategy builder can produce must resolve to a <see cref="DslGlossary"/> entry.
/// This is what stops a future verb from shipping invisible (a strategy card
/// rendering a blank row, or a Learning Center chip that's a dead click) — the
/// fixed lists below are just how the test enumerates "every verb today"; the
/// enum-driven cases (Indicator/Pattern/PriceLevel) auto-extend as those enums grow.
/// </summary>
[TestFixture]
public sealed class DslGlossaryTests
{
    private static IEnumerable<IndicatorType> AllIndicatorTypes() => Enum.GetValues<IndicatorType>();
    private static IEnumerable<PatternType> AllPatternTypes() => Enum.GetValues<PatternType>();
    private static IEnumerable<PriceLevelType> AllPriceLevelTypes() => Enum.GetValues<PriceLevelType>();

    [TestCaseSource(nameof(AllIndicatorTypes))]
    public void EveryIndicatorType_HasGlossaryEntry(IndicatorType t) =>
        Assert.That(DslGlossary.Find(new IndicatorCondition(t, 1, 1)), Is.Not.Null, $"{t} has no glossary entry");

    [TestCaseSource(nameof(AllPatternTypes))]
    public void EveryPatternType_HasGlossaryEntry(PatternType t) =>
        Assert.That(DslGlossary.Find(new PatternCondition(t, 1)), Is.Not.Null, $"{t} has no glossary entry");

    [TestCaseSource(nameof(AllPriceLevelTypes))]
    public void EveryPriceLevelType_HasGlossaryEntry(PriceLevelType t) =>
        Assert.That(DslGlossary.Find(new PriceLevelCondition(t, 1)), Is.Not.Null, $"{t} has no glossary entry");

    [Test]
    public void GapAndPriceBandAndTimeWindow_HaveGlossaryEntries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DslGlossary.Find(new GapBandCondition(1, 2)), Is.Not.Null);
            Assert.That(DslGlossary.Find(new PriceBandCondition(1, 2)), Is.Not.Null);
            Assert.That(DslGlossary.Find(new TimeWindowCondition(TimeSpan.Zero, TimeSpan.FromHours(1))), Is.Not.Null);
        });
    }

    // StrategyDefinition scalar exit/risk/order fields have no ICondition wrapper
    // and thus no enum to reflect over — add to this array whenever a new scalar
    // verb is introduced. The AllReflectedVerbs test below is the real backstop.
    private static readonly string[] ScalarVerbKeys =
    [
        "Name", "Session", "Entry", "Order", "Long", "Short", "Quantity", "WithVolumeConfirm",
        "TakeProfit", "AddTarget", "TakeProfitPercent", "StopLoss", "StopLossPercent",
        "TrailingStopLoss", "ExitStrategy", "PeakGiveback", "ExitAtPriorHigh",
        "ExitAtRollingHigh", "ExitAtRollingLow", "EntryAtRollingLow", "EntryAtRollingHigh",
        "AutonomousTrading", "AdaptiveOrder", "Repeat", "Then",
    ];

    [TestCaseSource(nameof(ScalarVerbKeys))]
    public void EveryScalarVerbKey_HasGlossaryEntry(string key) =>
        Assert.That(DslGlossary.Find(key), Is.Not.Null, $"{key} has no glossary entry");

    /// <summary>
    /// The actual backstop: every verb name <c>StrategyScriptGenerator.GetVerbsByPhase()</c>
    /// reflects off <c>StrategyBuilder</c> — the canonical, drift-proof verb catalog
    /// (IP-LAW-4) — must resolve in the glossary, directly or via an alias. If a new
    /// builder verb ships without a matching glossary entry, this fails immediately
    /// instead of silently rendering a dead Learning Center chip.
    /// </summary>
    [Test]
    public void AllReflectedVerbs_ResolveInGlossary()
    {
        var missing = StrategyScriptGenerator.GetVerbsByPhase()
            .SelectMany(g => g.Verbs)
            .Select(v => v.Split('(')[0].Trim())
            .Distinct()
            .Where(k => k is not ("Build" or "If")) // structural, not glossary-worthy
            .Where(k => DslGlossary.Find(k) is null)
            .ToList();

        Assert.That(missing, Is.Empty, $"Unresolved verb chips: {string.Join(", ", missing)}");
    }
}
