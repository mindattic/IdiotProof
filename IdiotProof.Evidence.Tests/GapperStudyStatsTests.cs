using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.EventStudy;
using IdiotProof.Evidence.Search;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T442 A2: the hypothesis-test primitives on synthetic data with a planted answer — Holm
/// bookkeeping, the day-clustered bootstrap, the logistic detector and the day-block AUC bootstrap,
/// the same-day BMP group study with its within-day placebo, and the SPA behaviour of a grid where
/// exactly one configuration is net-positive versus a grid of pure noise.
/// </summary>
public class GapperStudyStatsTests
{
    private static readonly DateOnly Day0 = new(2024, 1, 2);

    [Test]
    public void Holm_StepDown_KeepsTheRunningMaximum()
    {
        var adjusted = MultipleTesting.Holm([0.01, 0.04, 0.03]);
        Assert.Multiple(() =>
        {
            Assert.That(adjusted[0], Is.EqualTo(0.03).Within(1e-12), "smallest p times 3");
            Assert.That(adjusted[2], Is.EqualTo(0.06).Within(1e-12), "second smallest times 2");
            Assert.That(adjusted[1], Is.EqualTo(0.06).Within(1e-12), "largest keeps the running maximum, never below the one before it");
            Assert.That(MultipleTesting.Holm([]), Is.Empty);
            Assert.That(MultipleTesting.Holm([0.9])[0], Is.EqualTo(0.9));
        });
    }

    [Test]
    public void DayClusteredBootstrap_FindsAPlantedGroupDifference_AndNotANullOne()
    {
        var rng = new Random(7);
        var planted = new List<ClusterObservation>();
        var nothing = new List<ClusterObservation>();
        for (int d = 0; d < 40; d++)
        {
            double dayShock = rng.NextDouble() - 0.5;
            for (int i = 0; i < 10; i++)
            {
                int group = i % 2;
                planted.Add(new ClusterObservation(Day0.AddDays(d), dayShock + 2.0 * group + rng.NextDouble() * 0.2, group));
                nothing.Add(new ClusterObservation(Day0.AddDays(d), dayShock + rng.NextDouble() * 0.2, group));
            }
        }
        var hit = DayClusteredBootstrap.Run(planted, DayClusteredBootstrap.MeanDifference, 2000, 1337);
        var miss = DayClusteredBootstrap.Run(nothing, DayClusteredBootstrap.MeanDifference, 2000, 1337);
        Assert.Multiple(() =>
        {
            Assert.That(hit.Estimate, Is.EqualTo(2.0).Within(0.1));
            Assert.That(hit.Low, Is.GreaterThan(1.8));
            Assert.That(hit.PValueTwoSided, Is.LessThan(0.01));
            Assert.That(hit.Days, Is.EqualTo(40));
            Assert.That(miss.PValueTwoSided, Is.GreaterThan(0.2));
            Assert.That(miss.Low, Is.LessThan(0).And.Not.NaN);
            Assert.That(miss.High, Is.GreaterThan(0));
        });
    }

    [Test]
    public void DayClusteredBootstrap_TooFewDays_ReportsNoInterval()
    {
        var obs = Enumerable.Range(0, 30).Select(i => new ClusterObservation(Day0, i, i % 2)).ToList();
        var r = DayClusteredBootstrap.Run(obs, DayClusteredBootstrap.MeanDifference, 200, 1);
        Assert.That(r.PValueTwoSided, Is.EqualTo(1));
        Assert.That(double.IsNaN(r.Low), Is.True);
    }

    [Test]
    public void Auc_MatchesTheTextbookExample_AndHandlesTies()
    {
        Assert.That(Auc.Compute([0.1, 0.4, 0.35, 0.8], [false, false, true, true]), Is.EqualTo(0.75).Within(1e-12));
        Assert.That(Auc.Compute([0.5, 0.5, 0.5, 0.5], [false, true, false, true]), Is.EqualTo(0.5).Within(1e-12), "all ties: coin flip");
        Assert.That(double.IsNaN(Auc.Compute([0.1, 0.2], [true, true])), Is.True, "no negatives: undefined");
    }

    [Test]
    public void Logistic_RecoversAPlantedSeparation_AndTheAucGapIsFound()
    {
        var rng = new Random(11);
        var x = new List<double[]>();
        var y = new List<bool>();
        var days = new List<DateOnly>();
        var benchmark = new List<double>();
        for (int d = 0; d < 60; d++)
            for (int i = 0; i < 20; i++)
            {
                double signal = rng.NextDouble() * 4 - 2;
                double noise = rng.NextDouble() * 2 - 1;
                double junk = rng.NextDouble();
                x.Add([signal, junk, i % 3 == 0 ? double.NaN : noise]);
                y.Add(signal + 0.5 * noise > 0);
                days.Add(Day0.AddDays(d));
                benchmark.Add(rng.NextDouble() < 0.3 ? 1 : 0);
            }
        var model = LogisticRegression.Fit(["signal", "junk", "noise"], x, y, l2: 1.0, epochs: 400, learningRate: 0.5);
        var scores = x.Select(model.Predict).ToList();
        var cmp = Auc.CompareByDay(scores, benchmark, y, days, draws: 500, seed: 1337);
        Assert.Multiple(() =>
        {
            Assert.That(model.Weights[0], Is.GreaterThan(1.0), "the planted signal gets the large positive weight");
            Assert.That(Math.Abs(model.Weights[1]), Is.LessThan(0.3), "junk stays near zero");
            Assert.That(cmp.AucA, Is.GreaterThan(0.9));
            Assert.That(cmp.AucB, Is.EqualTo(0.5).Within(0.08));
            Assert.That(cmp.Difference, Is.GreaterThan(0.35));
            Assert.That(cmp.Low, Is.GreaterThan(0.3));
            Assert.That(cmp.PValueTwoSided, Is.LessThan(0.01));
            Assert.That(model.Predict([double.NaN, double.NaN, double.NaN]), Is.EqualTo(1.0 / (1.0 + Math.Exp(-model.Bias))).Within(1e-12), "all-missing scores at the bias");
        });
    }

    [Test]
    public void SameDayGroupStudy_FindsAPlantedCar_AndThePlaceboRejectsNoise()
    {
        var rng = new Random(3);
        var planted = new List<GroupEvent>();
        var noise = new List<GroupEvent>();
        for (int d = 0; d < 50; d++)
        {
            double common = rng.NextDouble() * 10 - 5;
            for (int i = 0; i < 8; i++)
            {
                bool treated = i < 3;
                planted.Add(new GroupEvent(Day0.AddDays(d), common + (treated ? 3 : 0) + rng.NextDouble(), treated));
                noise.Add(new GroupEvent(Day0.AddDays(d), common + rng.NextDouble(), treated));
            }
        }
        var hit = SameDayGroupStudy.Run(planted, 500, 1337);
        var miss = SameDayGroupStudy.Run(noise, 500, 1337);
        Assert.Multiple(() =>
        {
            Assert.That(hit.MeanDifference, Is.EqualTo(3.0).Within(0.2));
            Assert.That(hit.BmpP, Is.LessThan(0.001));
            Assert.That(hit.PlaceboP, Is.LessThan(0.01));
            Assert.That(hit.DaysWithBothGroups, Is.EqualTo(50));
            Assert.That(Math.Abs(miss.MeanDifference), Is.LessThan(0.3), "the common day move cancels inside the day");
            Assert.That(miss.PlaceboP, Is.GreaterThan(0.05));
        });
    }

    [Test]
    public void Spa_RejectsWhenOneConfigIsNetPositive_AndNotWhenAllAreNoise()
    {
        var rng = new Random(5);
        int cells = 400;
        var real = new List<double[]>();
        var noise = new List<double[]>();
        for (int k = 0; k < 24; k++)
        {
            var r = new double[cells];
            var n = new double[cells];
            for (int i = 0; i < cells; i++)
            {
                double draw = rng.NextDouble() * 0.04 - 0.02;
                r[i] = k == 7 ? draw + 0.012 : draw;
                n[i] = rng.NextDouble() * 0.04 - 0.02;
            }
            real.Add(r);
            noise.Add(n);
        }
        var hit = RealityCheck.Run(real, 500, 5, 1337);
        var miss = RealityCheck.Run(noise, 500, 5, 1337);
        Assert.Multiple(() =>
        {
            Assert.That(hit.BestConfig, Is.EqualTo(7));
            Assert.That(hit.SpaPValue, Is.LessThan(0.05));
            Assert.That(miss.SpaPValue, Is.GreaterThan(0.05), "the best of 24 noise configs does not survive the search");
        });
    }
}
