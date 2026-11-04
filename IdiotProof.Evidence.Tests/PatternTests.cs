namespace IdiotProof.Evidence.Tests;

public class PatternTests
{
    [Test]
    public void ShapeVector_IsScaleFree_APennyStockAndABigCapTracingTheSamePathMatch()
    {
        var cheap = Synthetic.Candles(new[] { 0.80, 0.82, 0.81, 0.85, 0.90 });
        var rich = Synthetic.Candles(new[] { 80.0, 82, 81, 85, 90 });
        var a = ShapeVector.FromWindow(cheap, 4, 5);
        var b = ShapeVector.FromWindow(rich, 4, 5);
        Assert.That(ShapeVector.Cosine(a, b), Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void ShapeVector_FlatWindow_IsAllZeros()
    {
        var flat = Synthetic.Candles(new[] { 2.0, 2.0, 2.0 });
        Assert.That(ShapeVector.FromWindow(flat, 2, 3), Is.All.EqualTo(0));
    }

    [Test]
    public void Lsh_FindsAnExactDuplicateFirst_AndSeparatesTheMirrorImage()
    {
        var rng = new Random(7);
        var index = new LshIndex<string>(16);
        for (int i = 0; i < 500; i++)
        {
            var v = new double[16];
            for (int d = 0; d < 16; d++) v[d] = Synthetic.Gaussian(rng);
            index.Add(v, $"noise{i}");
        }
        var target = new double[16];
        for (int d = 0; d < 16; d++) target[d] = Math.Sin(d / 2.0);
        index.Add(target, "target");
        var mirror = target.Select(x => -x).ToArray();
        index.Add(mirror, "mirror");

        var hits = index.Query(target, 5);
        Assert.That(hits[0].Payload, Is.EqualTo("target"));
        Assert.That(hits[0].Similarity, Is.EqualTo(1).Within(1e-9));
        Assert.That(hits.Any(h => h.Payload == "mirror"), Is.False, "an anti-correlated shape must not collide");
    }

    [Test]
    public void Lsh_RecallsNearNeighbors_WellAboveChance()
    {
        var rng = new Random(11);
        int dim = 20, found = 0, trials = 50;
        var index = new LshIndex<int>(dim);
        var bases = new List<double[]>();
        for (int i = 0; i < trials; i++)
        {
            var v = new double[dim];
            for (int d = 0; d < dim; d++) v[d] = Synthetic.Gaussian(rng);
            bases.Add(v);
            index.Add(v, i);
        }
        for (int i = 0; i < trials; i++)
        {
            var q = bases[i].Select(x => x + 0.15 * Synthetic.Gaussian(rng)).ToArray();
            var hits = index.Query(q, 1);
            if (hits.Count > 0 && hits[0].Payload == i) found++;
        }
        Assert.That(found, Is.GreaterThanOrEqualTo(45), $"recall {found}/{trials}");
    }

    [Test]
    public void AnalogPredictor_LearnsARepeatingPattern_AndAbstainsOnNoise()
    {
        // A sawtooth: 8 bars up then 2 bars down, repeated; after an up-leg the next 2 bars always fall.
        var closes = new List<double>();
        double p = 3.0;
        for (int cycle = 0; cycle < 60; cycle++)
        {
            for (int i = 0; i < 8; i++) { p += 0.02; closes.Add(p); }
            for (int i = 0; i < 2; i++) { p -= 0.05; closes.Add(p); }
        }
        var candles = Synthetic.Candles(closes);
        var model = new PatternAnalogPredictor(windowLength: 10, horizon: 2);
        int split = candles.Count * 2 / 3;
        model.Train(candles, 0, split, labelLimit: split);
        Assert.That(model.Count, Is.GreaterThan(100));

        var card = new DirectionalScorecard();
        for (int end = split + 10; end + 2 < candles.Count; end++)
        {
            var vote = model.Predict(candles, end, minSimilarity: 0.9);
            double fwd = (double)candles[end + 2].Close / (double)candles[end].Close - 1;
            card.Record(vote.Call, fwd);
        }
        Assert.That(card.HitRate, Is.GreaterThan(0.9), card.ToString());
        Assert.That(card.IsSignificant(), Is.True, card.ToString());
    }

    [Test]
    public void AnalogPredictor_OnPureNoise_IsNotSignificant()
    {
        var closes = Synthetic.PricesFromReturns(Synthetic.RandomWalkReturns(3000, seed: 3));
        var candles = Synthetic.Candles(closes);
        var model = new PatternAnalogPredictor(windowLength: 12, horizon: 3);
        model.Train(candles, 0, 2000, labelLimit: 2000);
        var card = new DirectionalScorecard();
        for (int end = 2010; end + 3 < candles.Count; end++)
        {
            var vote = model.Predict(candles, end, minSimilarity: 0.7, minNeighbors: 5, minAgreement: 0.55);
            card.Record(vote.Call, (double)candles[end + 3].Close / (double)candles[end].Close - 1);
        }
        Assert.That(card.PValue, Is.GreaterThan(0.01), "noise should not look like an edge: " + card);
    }

    [Test]
    public void AnalogPredictor_TrainingRespectsLabelLimit()
    {
        var candles = Synthetic.Candles(Enumerable.Range(1, 100).Select(i => 2.0 + i * 0.01).ToArray());
        var model = new PatternAnalogPredictor(windowLength: 5, horizon: 3);
        model.Train(candles, 0, 100, labelLimit: 50);
        // windows end at 4..46 (end + 3 < 50)
        Assert.That(model.Count, Is.EqualTo(43));
    }
}
