using IdiotProof.Core.Monitoring;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;

namespace IdiotProof.Core.Tests;

/// <summary>
/// IP-US-R11: the rsireversal family buys capitulation, not a falling knife. A knife prints
/// lower RSI lows alongside its lower price lows; capitulation prints a confirmed bullish
/// divergence (lower price low, higher RSI low), and a pivot only counts once 3 bars confirm it.
/// </summary>
public class RsiReversalFamilyTests
{
    private static Candle Bar(double close, int bar, double? low = null, double? high = null) => new()
    {
        Symbol = "T",
        StartUtc = new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc).AddMinutes(bar),
        EndUtc = new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc).AddMinutes(bar + 1),
        Open = (decimal)close,
        High = (decimal)(high ?? close * 1.005),
        Low = (decimal)(low ?? close * 0.995),
        Close = (decimal)close,
        Volume = 50_000,
    };

    /// <summary>Steep leg to a first low, bounce, shallow leg to a slightly lower low; returns the bars and the index of the second pivot.</summary>
    private static (List<Candle> Bars, int SecondPivot) Capitulation()
    {
        var bars = new List<Candle>();
        int i = 0;
        for (; i < 14; i++) bars.Add(Bar(100, i));
        for (int j = 0; j < 7; j++, i++) bars.Add(Bar(100 - (j + 1), i));
        bars.Add(Bar(90, i++, low: 89.5, high: 91));
        for (int j = 0; j < 3; j++, i++) bars.Add(Bar(91 + j, i));
        for (int j = 0; j < 3; j++, i++) bars.Add(Bar(94 + j, i));
        for (int j = 0; j < 3; j++, i++) bars.Add(Bar(97 - (j + 1) * 2.5, i));
        int pivot = i;
        bars.Add(Bar(89.5, i++, low: 89.0, high: 90.5));
        for (int j = 0; j < 3; j++, i++) bars.Add(Bar(90 + j, i));
        return (bars, pivot);
    }

    /// <summary>A knife: each leg lower and faster than the last, with only token bounces between.</summary>
    private static List<Candle> Knife()
    {
        var bars = new List<Candle>();
        int i = 0;
        double p = 100;
        for (; i < 14; i++) bars.Add(Bar(p, i));
        foreach (var (legBars, step) in new[] { (4, 1.0), (5, 2.0), (6, 3.0) })
        {
            for (int j = 0; j < legBars; j++, i++) { p -= step; bars.Add(Bar(p, i)); }
            for (int j = 0; j < 3; j++, i++) { p += 0.3; bars.Add(Bar(p, i)); }
        }
        return bars;
    }

    private static ICondition DivergenceGate(StrategyDefinition def) =>
        def.EntryConditions.Single(c => c.ToScript().Contains("IsRsiBullishDivergence", StringComparison.Ordinal));

    [Test]
    public void RsiReversalFamily_RequiresConfirmedBullishDivergence_AlongsideOversoldAtSupport()
    {
        var def = StrategyReplay.ResolveFamily("rsireversal", "T")!;
        var scripts = def.EntryConditions.Select(c => c.ToScript()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(scripts, Has.Some.Contains("IsRsiBullishDivergence"));
            Assert.That(scripts, Has.Some.Contains("IsRsiOversold"));
            Assert.That(scripts, Has.Some.Contains("IsAtSupport"));
        });
    }

    [Test]
    public void RsiReversalFamily_Capitulation_PassesTheDivergenceGate()
    {
        var gate = DivergenceGate(StrategyReplay.ResolveFamily("rsireversal", "T")!);
        var snap = IndicatorSnapshotBuilder.Build("T", Capitulation().Bars);
        Assert.That(gate.Evaluate(snap), Is.True, "lower price low with a higher RSI low is capitulation");
    }

    [Test]
    public void RsiReversalFamily_FallingKnife_FailsTheDivergenceGate()
    {
        var gate = DivergenceGate(StrategyReplay.ResolveFamily("rsireversal", "T")!);
        var snap = IndicatorSnapshotBuilder.Build("T", Knife());
        Assert.That(gate.Evaluate(snap), Is.False, "a knife's RSI lows fall with its price lows: no entry");
    }

    [Test]
    public void Divergence_IsNotSeen_UntilTheSecondPivotIsConfirmed_NoLookahead()
    {
        var (bars, pivot) = Capitulation();
        var gate = DivergenceGate(StrategyReplay.ResolveFamily("rsireversal", "T")!);

        for (int confirmations = 0; confirmations < 3; confirmations++)
        {
            var prefix = bars.Take(pivot + 1 + confirmations).ToList();
            Assert.That(gate.Evaluate(IndicatorSnapshotBuilder.Build("T", prefix)), Is.False,
                $"with only {confirmations} bar(s) after the second low the pivot isn't confirmed yet");
        }

        var confirmed = bars.Take(pivot + 4).ToList();
        Assert.That(gate.Evaluate(IndicatorSnapshotBuilder.Build("T", confirmed)), Is.True,
            "the third bar after the low confirms the pivot and the divergence");
    }
}
