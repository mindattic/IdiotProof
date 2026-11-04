using IdiotProof.Evidence.Microstructure;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class MicrostructureTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static List<TradePrint> Prints(params double[] prices) =>
        prices.Select((p, i) => new TradePrint(T0.AddSeconds(i), p, 100)).ToList();

    [Test]
    public void TickRule_SignsAKnownSequence_ZeroTicksRepeatTheLastDirection()
    {
        var signs = TradeSigner.TickRule(Prints(10.0, 10.1, 10.1, 10.0, 10.0, 10.2));
        Assert.That(signs, Is.EqualTo(new[] { 0, 1, 1, -1, -1, 1 }));
    }

    [Test]
    public void LeeReady_UsesTheQuoteMidpoint_AndFallsBackToTheTickRule()
    {
        var trades = new List<TradePrint>
        {
            new(T0, 10.00, 100),               // before any quote: tick rule (0)
            new(T0.AddSeconds(2), 10.05, 100), // above mid 10.00: buy
            new(T0.AddSeconds(3), 9.95, 100),  // below mid: sell
            new(T0.AddSeconds(4), 10.00, 100), // at mid: tick rule (uptick from 9.95) = buy
        };
        var quotes = new List<QuoteTick> { new(T0.AddSeconds(1), 9.90, 10.10) };
        Assert.That(TradeSigner.LeeReady(trades, quotes), Is.EqualTo(new[] { 0, 1, -1, 1 }));
    }

    [Test]
    public void LeeReady_NeverUsesAQuoteThatArrivedAfterTheTrade()
    {
        var trades = new List<TradePrint> { new(T0, 10.0, 100), new(T0.AddSeconds(1), 9.0, 100) };
        var later = new List<QuoteTick> { new(T0.AddSeconds(5), 10.5, 10.6) };
        // With the future quote the 9.0 print would still read as a sell either way; the 10.0 print would flip to a sell.
        Assert.That(TradeSigner.LeeReady(trades, later), Is.EqualTo(TradeSigner.TickRule(trades)));
    }

    [Test]
    public void PerBar_AssignsTradesToTheirBar_AndComputesImbalance()
    {
        var candles = Synthetic.Candles(new[] { 10.0, 10.1 });
        var trades = new List<TradePrint>
        {
            new(T0.AddSeconds(10), 10.0, 100), new(T0.AddSeconds(20), 10.1, 300),
            new(T0.AddSeconds(70), 10.0, 200),
        };
        var flows = OrderFlow.PerBar(candles, trades, new[] { 0, 1, -1 });
        Assert.That(flows[0].BuyVolume, Is.EqualTo(300));
        Assert.That(flows[0].UnsignedVolume, Is.EqualTo(100));
        Assert.That(flows[0].Imbalance, Is.EqualTo(1));
        Assert.That(flows[1].SellVolume, Is.EqualTo(200));
        Assert.That(flows[1].Imbalance, Is.EqualTo(-1));
    }

    [Test]
    public void NormalCdf_MatchesKnownValues()
    {
        Assert.That(OrderFlow.NormalCdf(0), Is.EqualTo(0.5).Within(1e-7));
        Assert.That(OrderFlow.NormalCdf(1.959964), Is.EqualTo(0.975).Within(1e-6));
        Assert.That(OrderFlow.NormalCdf(-1.959964), Is.EqualTo(0.025).Within(1e-6));
    }

    [Test]
    public void BulkClassify_GivesUpBarsMoreBuyVolume_AndKeepsTotalVolume()
    {
        var candles = Synthetic.Candles(new[] { 10.0, 10.1, 10.05, 10.3, 10.2 });
        var flows = OrderFlow.BulkClassify(candles, 10);
        Assert.That(flows[3].BuyVolume, Is.GreaterThan(flows[3].SellVolume));
        Assert.That(flows[4].SellVolume, Is.GreaterThan(flows[4].BuyVolume));
        foreach (var f in flows) Assert.That(f.BuyVolume + f.SellVolume, Is.EqualTo(1000).Within(1e-9));
    }

    [Test]
    public void Vpin_StaysInUnitRange_IsOneOnOneSidedFlow_AndZeroOnBalancedFlow()
    {
        var rng = new Random(4);
        var random = Enumerable.Range(0, 300).Select(_ => new BarFlow(rng.Next(0, 1000), rng.Next(0, 1000), rng.Next(0, 200))).ToList();
        var v = FlowMetrics.Vpin(random, 500, 10);
        Assert.That(v.Where(x => !double.IsNaN(x)), Is.All.InRange(0.0, 1.0));
        Assert.That(v.Count(x => !double.IsNaN(x)), Is.GreaterThan(200));

        var oneSided = Enumerable.Repeat(new BarFlow(1000, 0, 0), 50).ToList();
        Assert.That(FlowMetrics.Vpin(oneSided, 500, 10)[^1], Is.EqualTo(1).Within(1e-9));

        var balanced = Enumerable.Repeat(new BarFlow(500, 500, 0), 50).ToList();
        Assert.That(FlowMetrics.Vpin(balanced, 500, 10)[^1], Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void Vpin_IsNaNUntilEnoughBuckets_AndHasNoLookahead()
    {
        var a = Enumerable.Repeat(new BarFlow(800, 200, 0), 40).ToList();
        var b = a.Take(25).Concat(Enumerable.Repeat(new BarFlow(0, 5000, 0), 15)).ToList();
        var va = FlowMetrics.Vpin(a, 1000, 10);
        var vb = FlowMetrics.Vpin(b, 1000, 10);
        Assert.That(double.IsNaN(va[8]), Is.True);
        for (int i = 0; i < 25; i++)
            Assert.That(vb[i], Is.EqualTo(va[i]).Or.NaN, $"bar {i} changed when only later bars did");
        Assert.That(va[24], Is.EqualTo(0.6).Within(1e-9));
    }

    [Test]
    public void KylesLambda_RecoversAPlantedPriceImpact()
    {
        var rng = new Random(8);
        const double lambda = 0.00002;
        var q = new double[2000];
        var dp = new double[2000];
        for (int i = 0; i < q.Length; i++)
        {
            q[i] = rng.Next(-5000, 5000);
            dp[i] = lambda * q[i] + 0.005 * Synthetic.Gaussian(rng);
        }
        Assert.That(FlowMetrics.KylesLambda(dp, q), Is.EqualTo(lambda).Within(lambda * 0.05));
    }

    [Test]
    public void RollingKylesLambda_ReadsOnlyTrailingBars()
    {
        var closes = Enumerable.Range(0, 60).Select(i => 3.0 + (i % 3) * 0.01).ToArray();
        var candles = Synthetic.Candles(closes);
        var flows = Enumerable.Range(0, 60).Select(i => new BarFlow(i % 3 * 100, 50, 0)).ToList();
        var full = FlowMetrics.RollingKylesLambda(candles, flows, 20);
        var cut = FlowMetrics.RollingKylesLambda(candles.Take(40).ToList(), flows.Take(40).ToList(), 20);
        Assert.That(double.IsNaN(full[19]), Is.True);
        for (int i = 20; i < 40; i++) Assert.That(cut[i], Is.EqualTo(full[i]));
    }

    [Test]
    public void Absorption_FiresWhenHeavySellingFailsToMovePrice_NotWhenPriceCaves()
    {
        static (List<Candle> Candles, List<BarFlow> Flows) Build(double finalDrift)
        {
            var closes = new List<double>();
            var vols = new List<decimal>();
            var flows = new List<BarFlow>();
            for (int i = 0; i < 40; i++)
            {
                closes.Add(3.0 + (i % 2) * 0.02);
                vols.Add(1000);
                flows.Add(new BarFlow(500, 500, 0));
            }
            for (int i = 0; i < 5; i++)
            {
                closes.Add(3.01 + finalDrift * (i + 1));
                vols.Add(6000);
                flows.Add(new BarFlow(1000, 5000, 0));
            }
            var candles = Synthetic.Candles(closes);
            for (int i = 0; i < candles.Count; i++)
                candles[i] = new Candle
                {
                    Symbol = candles[i].Symbol, StartUtc = candles[i].StartUtc, EndUtc = candles[i].EndUtc,
                    Open = candles[i].Open, High = candles[i].High + 0.01m, Low = candles[i].Low - 0.01m,
                    Close = candles[i].Close, Volume = vols[i],
                };
            return (candles, flows);
        }

        var (held, heldFlows) = Build(0.0);
        Assert.That(FlowMetrics.Absorption(held, heldFlows)[^1], Is.EqualTo(1));
        Assert.That(FlowMetrics.Absorption(held, heldFlows).Take(40), Is.All.EqualTo(0));

        var (caved, cavedFlows) = Build(-0.03);
        Assert.That(FlowMetrics.Absorption(caved, cavedFlows)[^1], Is.EqualTo(0));
    }

    [Test]
    public void FlowEvaluator_RecoversAPlantedRelationship_InTheHeldOutPart()
    {
        var rng = new Random(12);
        int n = 3000;
        var metric = new double[n];
        var closes = new double[n + 1];
        closes[0] = 3.0;
        for (int i = 0; i < n; i++)
        {
            metric[i] = rng.NextDouble();
            bool up = rng.NextDouble() < (metric[i] > 0.5 ? 0.8 : 0.2);
            closes[i + 1] = closes[i] * (up ? 1.01 : 0.99);
        }
        var candles = Synthetic.Candles(closes);
        var labels = ForwardReturnLabeler.Label(candles, 1);
        var eval = FlowEvaluator.Evaluate("planted", metric, labels, horizon: 1, trainEnd: 2000, bins: 2);

        Assert.That(eval.Bins, Is.EqualTo(2));
        Assert.That(eval.Buckets[1].Test.Probability, Is.EqualTo(0.8).Within(0.05));
        Assert.That(eval.Buckets[0].Test.Probability, Is.EqualTo(0.2).Within(0.05));
        Assert.That(eval.TrainSamples, Is.EqualTo(1999), "a training label must resolve before trainEnd");
        Assert.That(eval.TestSamples, Is.EqualTo(1000));
    }
}
