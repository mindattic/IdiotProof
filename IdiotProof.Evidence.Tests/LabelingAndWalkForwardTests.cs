namespace IdiotProof.Evidence.Tests;

public class LabelingAndWalkForwardTests
{
    [Test]
    public void Labeler_ComputesForwardReturn_AndLeavesTheLastHorizonBarsUnlabeled()
    {
        var candles = Synthetic.Candles(new double[] { 1.00, 1.10, 1.21, 1.00, 0.90 });
        var labels = ForwardReturnLabeler.Label(candles, horizon: 2, deadBand: 0.001);
        Assert.That(labels, Has.Count.EqualTo(3));
        Assert.That(labels[0].ForwardReturn, Is.EqualTo(0.21).Within(1e-9));
        Assert.That(labels[0].Direction, Is.EqualTo(Direction.Up));
        Assert.That(labels[2].Direction, Is.EqualTo(Direction.Down));
    }

    [Test]
    public void Labeler_HasNoLookahead_ChangingBarsBeyondTheHorizonNeverChangesALabel()
    {
        var a = Synthetic.Candles(new double[] { 1, 2, 3, 4, 5, 6, 7 });
        var b = Synthetic.Candles(new double[] { 1, 2, 3, 4, 5, 60, 70 });
        var la = ForwardReturnLabeler.Label(a, 2);
        var lb = ForwardReturnLabeler.Label(b, 2);
        // Bar 2's label reads bars 3..4 only; bars 5 and 6 differ between a and b.
        Assert.That(lb[2].ForwardReturn, Is.EqualTo(la[2].ForwardReturn));
        Assert.That(lb[2].MaxFavorable, Is.EqualTo(la[2].MaxFavorable));
    }

    [Test]
    public void Labeler_ReportsMaxAdverseAndFavorableExcursion()
    {
        var candles = Synthetic.Candles(new double[] { 2.0, 1.8, 2.4, 2.1 });
        var l = ForwardReturnLabeler.Label(candles, 3)[0];
        Assert.That(l.MaxAdverse, Is.LessThan(0));
        Assert.That(l.MaxFavorable, Is.EqualTo(0.2).Within(1e-9));
    }

    [Test]
    public void WalkForward_TestNeverOverlapsTrainPlusEmbargo_AndFoldsAdvance()
    {
        var folds = WalkForward.Folds(count: 1000, minTrain: 300, testSize: 100, embargo: 10);
        Assert.That(folds, Is.Not.Empty);
        foreach (var f in folds)
        {
            Assert.That(f.TestStart, Is.GreaterThanOrEqualTo(f.TrainEnd + 10));
            Assert.That(f.TestEnd, Is.LessThanOrEqualTo(1000));
            Assert.That(f.TrainStart, Is.EqualTo(0));
        }
        for (int i = 1; i < folds.Count; i++)
            Assert.That(folds[i].TestStart, Is.GreaterThanOrEqualTo(folds[i - 1].TestEnd));
    }

    [Test]
    public void WalkForward_RollingWindowKeepsTrainLengthFixed()
    {
        var folds = WalkForward.Folds(600, 200, 100, 5, expanding: false);
        Assert.That(folds.All(f => f.TrainEnd - f.TrainStart == 200), Is.True);
    }
}
