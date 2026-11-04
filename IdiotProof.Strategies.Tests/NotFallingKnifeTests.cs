using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;
using IdiotProof.Strategies;

namespace IdiotProof.Strategies.Tests;

/// <summary>NotFallingKnife(maxProb, minSamples): a learned, fail-closed falling-knife guard (IP-T372).</summary>
public class NotFallingKnifeTests
{
    private static IndicatorSnapshot Snap(bool? present, double? risk = null, int? samples = null) => new()
    {
        Symbol = "X", Price = 2.0, FallingKnifePresent = present, FallingKnifeRisk = risk, FallingKnifeSamples = samples,
    };

    private static readonly IndicatorCondition Guard = new(IndicatorType.NotFallingKnife, 0.4, 30);

    [Test]
    public void RoundTrips_WithBothParameters()
    {
        var text = Stock.Ticker("X").NotFallingKnife(0.35, 40).Long().ToScript();
        Assert.That(text, Does.Contain("NotFallingKnife(0.35, 40)"));
        var cond = ScriptParser.ParseScript(text)!.EntryConditions.OfType<IndicatorCondition>()
            .Single(c => c.Type == IndicatorType.NotFallingKnife);
        Assert.That(cond.Parameter, Is.EqualTo(0.35));
        Assert.That(cond.Parameter2, Is.EqualTo(40));
    }

    [Test]
    public void Parser_DefaultsWhenArgumentsAreOmitted()
    {
        var cond = ScriptParser.ParseScript("Stock.Ticker(\"X\").NotFallingKnife().Long()")!.EntryConditions
            .OfType<IndicatorCondition>().Single(c => c.Type == IndicatorType.NotFallingKnife);
        Assert.That(cond.Parameter, Is.EqualTo(0.5));
        Assert.That(cond.Parameter2, Is.EqualTo(30));
    }

    [Test] public void NoKnifeSetup_Passes() => Assert.That(Guard.Evaluate(Snap(false)), Is.True);
    [Test] public void UnknownWindow_FailsClosed() => Assert.That(Guard.Evaluate(Snap(null)), Is.False);
    [Test] public void KnifeWithTooLittleHistory_FailsClosed() => Assert.That(Guard.Evaluate(Snap(true, 0.1, 12)), Is.False);
    [Test] public void KnifeWithMissingRisk_FailsClosed() => Assert.That(Guard.Evaluate(Snap(true, null, 100)), Is.False);
    [Test] public void KnifeMeasuredSafe_Passes() => Assert.That(Guard.Evaluate(Snap(true, 0.3, 50)), Is.True);
    [Test] public void KnifeMeasuredDangerous_Blocks() => Assert.That(Guard.Evaluate(Snap(true, 0.7, 50)), Is.False);

    [Test]
    public void SnapshotBuilder_ShortWindow_LeavesKnifeUnknown()
    {
        var snap = IndicatorSnapshotBuilder.Build("X", Candles(Enumerable.Range(0, 20).Select(i => 3.0 - i * 0.01).ToArray()));
        Assert.That(snap.FallingKnifePresent, Is.Null);
    }

    [Test]
    public void SnapshotBuilder_DecliningTail_IsAKnife_WithLearnedRateFromTheWindow()
    {
        var rng = new Random(4);
        var closes = new List<double> { 3.0 };
        for (int i = 1; i < 300; i++) closes.Add(closes[^1] * (1 + 0.01 * (rng.NextDouble() - 0.5)));
        for (int i = 0; i < 12; i++) closes.Add(closes[^1] * 0.985);
        var snap = IndicatorSnapshotBuilder.Build("X", Candles(closes));
        Assert.That(snap.FallingKnifePresent, Is.True);
        Assert.That(snap.FallingKnifeSamples, Is.Not.Null);
        Assert.That(snap.FallingKnifeRisk, Is.InRange(0.0, 1.0));
    }

    [Test]
    public void SnapshotBuilder_RisingTail_IsNotAKnife()
    {
        var closes = Enumerable.Range(0, 80).Select(i => 2.0 + i * 0.01).ToArray();
        Assert.That(IndicatorSnapshotBuilder.Build("X", Candles(closes)).FallingKnifePresent, Is.False);
    }

    private static List<Candle> Candles(IReadOnlyList<double> closes)
    {
        var t0 = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        var list = new List<Candle>();
        for (int i = 0; i < closes.Count; i++)
        {
            decimal o = (decimal)(i == 0 ? closes[0] : closes[i - 1]), c = (decimal)closes[i];
            list.Add(new Candle
            {
                Symbol = "X", StartUtc = t0.AddMinutes(i), EndUtc = t0.AddMinutes(i + 1),
                Open = o, Close = c, High = Math.Max(o, c), Low = Math.Min(o, c), Volume = 1000,
            });
        }
        return list;
    }
}
