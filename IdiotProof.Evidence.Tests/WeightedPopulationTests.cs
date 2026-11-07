using IdiotProof.Evidence.Evaluation;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// IP-T448 amendment 01: every weighted estimator equals its unweighted counterpart on rows
/// replicated by integer weight, so a population sample at weight 10 is read exactly as ten copies
/// would be, and the bootstraps resample days rather than treating a weight as ten independent trials.
/// </summary>
public class WeightedPopulationTests
{
    private static readonly DateOnly Day0 = new(2024, 1, 2);

    private static (List<double> Scores, List<bool> Labels, List<double> Weights, List<DateOnly> Days) Sample(int seed, int days = 30, int perDay = 25)
    {
        var rng = new Random(seed);
        var scores = new List<double>();
        var labels = new List<bool>();
        var weights = new List<double>();
        var dayList = new List<DateOnly>();
        for (int d = 0; d < days; d++)
            for (int i = 0; i < perDay; i++)
            {
                bool up = rng.NextDouble() < 0.2;
                scores.Add(Math.Round((up ? 0.6 : 0.4) + rng.NextDouble() * 0.5, 2));
                labels.Add(up);
                weights.Add(rng.NextDouble() < 0.5 ? 1 : 10);
                dayList.Add(Day0.AddDays(d));
            }
        return (scores, labels, weights, dayList);
    }

    [Test]
    public void WeightedAuc_EqualsTheUnweightedAucOfReplicatedRows_TiesIncluded()
    {
        var (scores, labels, weights, _) = Sample(3);
        var repScores = new List<double>();
        var repLabels = new List<bool>();
        for (int i = 0; i < scores.Count; i++)
            for (int c = 0; c < (int)weights[i]; c++) { repScores.Add(scores[i]); repLabels.Add(labels[i]); }

        Assert.Multiple(() =>
        {
            Assert.That(WeightedAuc.Compute(scores, labels, weights), Is.EqualTo(Auc.Compute(repScores, repLabels)).Within(1e-12));
            Assert.That(WeightedAuc.Compute(scores, labels, scores.Select(_ => 1.0).ToList()), Is.EqualTo(Auc.Compute(scores, labels)).Within(1e-12),
                "unit weights reproduce the frozen AUC");
        });
    }

    [Test]
    public void WeightedAucComparison_PointEstimatesMatch_AndAUselessScorerLoses()
    {
        var (scores, labels, weights, days) = Sample(5);
        var noise = new Random(11);
        var useless = scores.Select(_ => noise.NextDouble()).ToList();
        var cmp = WeightedAuc.CompareByDay(scores, useless, labels, weights, days, draws: 2000, seed: 1);
        Assert.Multiple(() =>
        {
            Assert.That(cmp.AucA, Is.EqualTo(WeightedAuc.Compute(scores, labels, weights)).Within(1e-12));
            Assert.That(cmp.AucB, Is.EqualTo(WeightedAuc.Compute(useless, labels, weights)).Within(1e-12));
            Assert.That(cmp.Difference, Is.GreaterThan(0.2), "the informative score beats noise");
            Assert.That(cmp.Low, Is.GreaterThan(0), "and the day-block interval excludes zero");
            Assert.That(cmp.PValueTwoSided, Is.LessThan(0.01));
            Assert.That(cmp.Days, Is.EqualTo(30));
        });
    }

    [Test]
    public void WeightedLift_MatchesReplicatedRates_AndAFlatGroupIsNotSignificant()
    {
        var rows = new List<WeightedOutcome>();
        var rng = new Random(9);
        for (int d = 0; d < 40; d++)
            for (int i = 0; i < 30; i++)
            {
                bool inGroup = i < 6;
                bool up = rng.NextDouble() < (inGroup ? 0.5 : 0.1);
                rows.Add(new WeightedOutcome(Day0.AddDays(d), inGroup, up, inGroup ? 1 : 10));
            }
        var lift = WeightedPopulation.Lift(rows, draws: 2000, seed: 2);
        double gw = rows.Where(r => r.InGroup).Sum(r => r.Weight), gu = rows.Where(r => r.InGroup && r.Up).Sum(r => r.Weight);
        double aw = rows.Sum(r => r.Weight), au = rows.Where(r => r.Up).Sum(r => r.Weight);

        var flat = rows.Select(r => r with { Up = rng.NextDouble() < 0.1 }).ToList();
        var none = WeightedPopulation.Lift(flat, draws: 2000, seed: 2);
        Assert.Multiple(() =>
        {
            Assert.That(lift.Rate, Is.EqualTo(gu / gw).Within(1e-12));
            Assert.That(lift.BaseRate, Is.EqualTo(au / aw).Within(1e-12));
            Assert.That(lift.Lift, Is.EqualTo(gu / gw / (au / aw)).Within(1e-12));
            Assert.That(lift.LiftLow, Is.GreaterThan(1.5), "a planted 5x rate gap survives the day bootstrap");
            Assert.That(lift.PValueTwoSided, Is.LessThan(0.01));
            Assert.That(lift.GroupRows, Is.EqualTo(240));
            Assert.That(none.PValueTwoSided, Is.GreaterThan(0.05), "no planted effect, no significance");
        });
    }

    [Test]
    public void WeightedMean_IsTheWeightedAverage_WithADayInterval()
    {
        var rows = new List<WeightedValue>();
        for (int d = 0; d < 20; d++)
        {
            rows.Add(new WeightedValue(Day0.AddDays(d), 2.0, 1));
            rows.Add(new WeightedValue(Day0.AddDays(d), -1.0, 10));
        }
        var mean = WeightedPopulation.Mean(rows, draws: 500, seed: 4);
        Assert.Multiple(() =>
        {
            Assert.That(mean.Estimate, Is.EqualTo((2.0 - 10.0) / 11.0).Within(1e-12), "the weight-10 losers dominate the two-dollar winner");
            Assert.That(mean.High, Is.LessThan(0), "every day carries the same mix, so the interval stays below zero");
            Assert.That(mean.Days, Is.EqualTo(20));
        });
    }

    [Test]
    public void FitWeighted_WithIntegerWeights_EqualsTheFitOfReplicatedRows()
    {
        var rng = new Random(13);
        var x = new List<double[]>();
        var y = new List<bool>();
        var w = new List<double>();
        for (int i = 0; i < 300; i++)
        {
            double a = rng.NextDouble() * 4 - 2, b = rng.NextDouble() * 4 - 2;
            x.Add(i % 17 == 0 ? [a, double.NaN] : [a, b]);
            y.Add(rng.NextDouble() < 1 / (1 + Math.Exp(-(1.5 * a - 0.7 * b))));
            w.Add(i % 3 == 0 ? 10 : 1);
        }
        var rx = new List<double[]>();
        var ry = new List<bool>();
        for (int i = 0; i < x.Count; i++)
            for (int c = 0; c < (int)w[i]; c++) { rx.Add(x[i]); ry.Add(y[i]); }

        var weighted = LogisticRegression.FitWeighted(["a", "b"], x, y, w, epochs: 300);
        var replicated = LogisticRegression.Fit(["a", "b"], rx, ry, epochs: 300);
        Assert.Multiple(() =>
        {
            Assert.That(weighted.Weights[0], Is.EqualTo(replicated.Weights[0]).Within(1e-9));
            Assert.That(weighted.Weights[1], Is.EqualTo(replicated.Weights[1]).Within(1e-9));
            Assert.That(weighted.Bias, Is.EqualTo(replicated.Bias).Within(1e-9));
            Assert.That(weighted.Predict([1.0, -1.0]), Is.EqualTo(replicated.Predict([1.0, -1.0])).Within(1e-9));
        });
    }

    [Test]
    public void Replicate_RepeatsByIntegerWeight_AndRefusesFractionalWeights()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WeightedPopulation.Replicate([(1.5, 1), (2.5, 3)]), Is.EqualTo(new[] { 1.5, 2.5, 2.5, 2.5 }));
            Assert.Throws<ArgumentException>(() => WeightedPopulation.Replicate([(1.0, 2.5)]));
        });
    }
}
