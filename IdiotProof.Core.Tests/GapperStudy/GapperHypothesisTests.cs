using System.Text.Json;
using IdiotProof.Core.Research.Gappers;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T442 A2: the frozen hypotheses load, their thresholds parse from the declared text, and the correction/verdict bookkeeping maps to the four verdicts.</summary>
[TestFixture]
public class GapperHypothesisTests
{
    [Test]
    public void FrozenHypotheses_Load_WithTheirTestsAndFamilies()
    {
        var set = GapperHypothesisSet.Load(GapperStudyTestKit.Paths);
        Assert.Multiple(() =>
        {
            Assert.That(set.Hypotheses, Has.Count.EqualTo(17));
            Assert.That(set.FrozenUtc, Is.Not.Null);
            Assert.That(set.Hypotheses.Count(h => h.Primary), Is.EqualTo(12));
            Assert.That(set.Find("h11").Test, Is.EqualTo("auc-delong"));
            Assert.That(set.Find("H12").MinEffectNumber("minTrades"), Is.EqualTo(150));
            Assert.That(set.Find("H5").MinEffectRange("medianWithin"), Is.EqualTo(new[] { -2.0, 5.0 }));
            Assert.That(set.Find("H11").AllowsSplit("train"), Is.False);
            Assert.That(set.Find("H11").AllowsSplit("validation"), Is.True);
            Assert.That(set.Family("F3").Select(h => h.Id), Is.EquivalentTo(new[] { "H4", "H5", "H13", "H17" }));
            Assert.That(() => set.Find("H99"), Throws.ArgumentException);
        });
    }

    [Test]
    public void FeatureCondition_ReadsThresholdsFromTheDeclaredText()
    {
        var set = GapperHypothesisSet.Load(GapperStudyTestKit.Paths);
        var h9 = FeatureCondition.Parse(set.Find("H9").Feature);
        Assert.Multiple(() =>
        {
            Assert.That(h9.Select(c => $"{c.Name}{c.Operator}{c.Threshold}"), Is.EqualTo(new[] { "v_3>1", "pmVolRatio>5" }));
            Assert.That(FeatureCondition.Require(set.Find("H9").Label, "fwdMax_09:45").Threshold, Is.EqualTo(0.10));
            Assert.That(FeatureCondition.Require(set.Find("H6").Feature, "abnVol_1").Holds(3.0), Is.True);
            Assert.That(FeatureCondition.Require(set.Find("H6").Feature, "abnVol_1").Holds(2.99), Is.False);
            Assert.That(FeatureCondition.Require(set.Find("H17").Feature, "tradesRoc").Holds(-0.5), Is.True);
            Assert.That(FeatureCondition.Require(set.Find("H11").Feature, "pubToTMinutes").Threshold, Is.EqualTo(60));
            Assert.That(FeatureCondition.Require(set.Find("H13").Feature, "ext0945").Holds(null), Is.False, "a missing reading never satisfies a clause");
            Assert.That(FeatureCondition.Parse(set.Find("H4").Label), Is.Empty, "a clock time is not a numeric clause");
            Assert.That(FeatureCondition.Time(set.Find("H4").Label), Is.EqualTo(new TimeSpan(9, 46, 0)));
            Assert.That(FeatureCondition.Parse(set.Find("H5").Label), Is.Empty, "hodBucket = 09:30-09:31 is a bucket name, not a number");
        });
    }

    private static HypothesisRun Run(string id, string family, bool primary, params HypothesisPart[] parts) =>
        new() { Id = id, Family = family, Primary = primary, Split = "train", Parts = parts.ToList() };

    private static HypothesisPart Part(double effect, double min, double? p, double? lo = null, double? hi = null, EffectDirection dir = EffectDirection.AtLeast) =>
        new() { Name = "x", EffectName = "x", Effect = effect, MinEffect = min, RawP = p, CiLow = lo, CiHigh = hi, Direction = dir, MeetsMinimum = dir == EffectDirection.AtLeast ? effect >= min : effect <= min, N = 500, Days = 50 };

    [Test]
    public void Corrections_ApplyBhWithinFamily_HolmAcrossPrimaries_AndMapVerdicts()
    {
        var p = GapperStudyTestKit.Params;
        var strong = Run("H1", "F1", true, Part(4.0, 3.0, 0.001, 3.5, 4.5));
        var weakEffect = Run("H9", "F1", true, Part(1.2, 2.0, 0.002, 1.0, 1.4));
        var noisy = Run("H15", "F1", false, Part(2.5, 1.5, 0.4, 0.8, 4.0));
        var structural = Run("H7", "F5", true, Part(-3.0, -2.0, 0.01, -4.0, -2.5, EffectDirection.AtMost), Part(-1.0, -5.0, 0.3, -3.0, 1.0, EffectDirection.AtMost));
        var runs = new[] { strong, weakEffect, noisy, structural };
        HypothesisCorrections.Apply(runs, p, "test");
        Assert.Multiple(() =>
        {
            Assert.That(strong.Parts[0].FamilyQ, Is.EqualTo(0.003).Within(1e-12), "BH over the three F1 p-values: 0.001 x 3 / 1");
            Assert.That(weakEffect.Parts[0].FamilyQ, Is.EqualTo(0.003).Within(1e-12), "0.002 x 3 / 2, floored at the next q");
            Assert.That(noisy.Parts[0].FamilyQ, Is.EqualTo(0.4).Within(1e-12));
            Assert.That(structural.Parts[0].FamilyQ, Is.EqualTo(0.02).Within(1e-12), "BH over the two F5 parts");
            Assert.That(strong.Parts[0].HolmP, Is.EqualTo(0.003).Within(1e-12), "Holm over the three primaries' p: 0.001 x 3");
            Assert.That(structural.Parts[0].HolmP, Is.EqualTo(0.3).Within(1e-12), "Holm sees H7's largest part p (0.3), rank 3 x 1");
            Assert.That(noisy.Parts[0].HolmP, Is.Null, "secondaries never enter Holm");
            Assert.That(strong.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(weakEffect.Verdict, Is.EqualTo(HypothesisVerdict.Refuted), "the interval tops out under the minimum lift");
            Assert.That(noisy.Verdict, Is.EqualTo(HypothesisVerdict.Undecided));
            Assert.That(structural.Parts[0].Verdict, Is.EqualTo(HypothesisVerdict.Undecided), "effect met and q passes, but Holm (0.3) fails for a primary");
            Assert.That(structural.Parts[1].Verdict, Is.EqualTo(HypothesisVerdict.Refuted), "an interval of [-3, 1] sits entirely above the -5 the claim needs");
            Assert.That(structural.Verdict, Is.EqualTo(HypothesisVerdict.Refuted), "one refuted part refutes the hypothesis");
            Assert.That(strong.CorrectionScope, Is.EqualTo("test"));
        });
    }

    [Test]
    public void Verdicts_F3IntervalsOnly_H12SpaAndDsr_VoidStaysVoid()
    {
        var p = GapperStudyTestKit.Params;
        var timing = Run("H4", "F3", true, Part(0.7, 0.6, null, 0.65, 0.75));
        var timingNo = Run("H13", "F3", false, Part(0.5, 0.85, null, 0.4, 0.6));
        var grid = Run("H12", "F4", true, Part(1.5, 1.0, 0.01, 0.4, 2.6));
        grid.Parts[0].Statistic[HypothesisCorrections.SpaKey] = 0.01;
        grid.Parts[0].Statistic[HypothesisCorrections.DsrKey] = 0.99;
        var gridLucky = Run("H12", "F4", true, Part(1.5, 1.0, 0.2, 0.4, 2.6));
        gridLucky.Parts[0].Statistic[HypothesisCorrections.SpaKey] = 0.2;
        gridLucky.Parts[0].Statistic[HypothesisCorrections.DsrKey] = 0.5;
        var dead = Run("H8", "F5", true, HypothesisPart.VoidPart("lift", "dropped"));
        HypothesisCorrections.Apply([timing, timingNo, grid, gridLucky, dead], p, "test");
        Assert.Multiple(() =>
        {
            Assert.That(timing.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(timing.Parts[0].Reasons.Any(r => r.Contains("descriptive")), Is.True);
            Assert.That(timing.Parts[0].Reasons.Any(r => r.Contains("significant")), Is.False, "F3 never uses the word");
            Assert.That(timingNo.Verdict, Is.EqualTo(HypothesisVerdict.Refuted));
            Assert.That(grid.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
            Assert.That(gridLucky.Verdict, Is.EqualTo(HypothesisVerdict.Undecided));
            Assert.That(dead.Verdict, Is.EqualTo(HypothesisVerdict.Void));
            Assert.That(HypothesisVerdict.Combine([HypothesisVerdict.Supported, HypothesisVerdict.Refuted]), Is.EqualTo(HypothesisVerdict.Refuted));
            Assert.That(HypothesisVerdict.Combine([HypothesisVerdict.Supported, HypothesisVerdict.Undecided]), Is.EqualTo(HypothesisVerdict.Undecided));
            Assert.That(HypothesisVerdict.Combine([]), Is.EqualTo(HypothesisVerdict.Void));
        });
    }

    [Test]
    public void RunFile_RoundTripsThroughJson()
    {
        var run = Run("H3", "F2", true, Part(12, 10, 0.004, 8, 16));
        run.Parts[0].Statistic["placeboP"] = 0.002;
        run.ComputedUtc = new DateTime(2026, 11, 7, 1, 0, 0, DateTimeKind.Utc);
        var back = HypothesisRun.FromJson(run.ToJson())!;
        Assert.Multiple(() =>
        {
            Assert.That(back.Id, Is.EqualTo("H3"));
            Assert.That(back.Parts[0].RawP, Is.EqualTo(0.004));
            Assert.That(back.Parts[0].Direction, Is.EqualTo(EffectDirection.AtLeast));
            Assert.That(((JsonElement)back.Parts[0].Statistic["placeboP"]!).GetDouble(), Is.EqualTo(0.002));
            Assert.That(HypothesisRun.FileName("H3", "train", run.ComputedUtc), Is.EqualTo("test-H3-train-20261107-0100.json"));
            Assert.That(HypothesisRun.FromJson("not json"), Is.Null);
        });
    }
}
