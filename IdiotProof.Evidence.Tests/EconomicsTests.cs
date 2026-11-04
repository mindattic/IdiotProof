using IdiotProof.Evidence.Economics;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T403: Kelly sizing under uncertainty and the spread/impact/fee cost model.</summary>
public class EconomicsTests
{
    [Test]
    public void PlugInKelly_MatchesClosedForm()
    {
        Assert.That(KellySizing.PlugIn(0.6, 1), Is.EqualTo(0.2).Within(1e-12));
        Assert.That(KellySizing.PlugIn(0.5, 2), Is.EqualTo(0.25).Within(1e-12));
        Assert.That(KellySizing.PlugIn(0.4, 1), Is.EqualTo(0), "no edge, no bet");
    }

    [Test]
    public void PlugInKelly_IsTheGrowthMaximum()
    {
        double f = KellySizing.PlugIn(0.55, 1.5);
        double g = KellySizing.Growth(0.55, 1.5, f);
        Assert.That(g, Is.GreaterThan(KellySizing.Growth(0.55, 1.5, f - 0.01)));
        Assert.That(g, Is.GreaterThan(KellySizing.Growth(0.55, 1.5, f + 0.01)));
    }

    [Test]
    public void PosteriorKelly_ShrinksForSmallSamples_AndConvergesAsEvidenceGrows()
    {
        var small = KellySizing.Advise(6, 4, 1);
        var large = KellySizing.Advise(6000, 4000, 1);
        Assert.That(small.PlugInFraction, Is.EqualTo(0.2).Within(1e-12));
        Assert.That(small.PosteriorFraction, Is.LessThan(small.PlugInFraction));
        Assert.That(small.ConservativeFraction, Is.LessThan(small.PosteriorFraction));
        Assert.That(large.PosteriorFraction, Is.EqualTo(0.2).Within(1e-3));
        Assert.That(large.PlugInFraction - large.PosteriorFraction, Is.LessThan(small.PlugInFraction - small.PosteriorFraction));
        Assert.That(small.RecommendedFraction, Is.EqualTo(0.5 * small.PosteriorFraction).Within(1e-12));
    }

    [Test]
    public void PayoffRatio_IsMeanWinOverMeanLoss()
    {
        Assert.That(KellySizing.PayoffRatio(new[] { 0.04, 0.02, -0.01, -0.03 }), Is.EqualTo(1.5).Within(1e-12));
        Assert.That(KellySizing.PayoffRatio(new[] { 0.04 }), Is.NaN);
    }

    [Test]
    public void BetaDistribution_UniformAndSymmetricCases()
    {
        Assert.That(BetaDistribution.Cdf(0.3, 1, 1), Is.EqualTo(0.3).Within(1e-9));
        Assert.That(BetaDistribution.Cdf(0.5, 5, 5), Is.EqualTo(0.5).Within(1e-9));
        Assert.That(BetaDistribution.Quantile(0.25, 1, 1), Is.EqualTo(0.25).Within(1e-9));
        // Beta(2,1) CDF is x².
        Assert.That(BetaDistribution.Cdf(0.6, 2, 1), Is.EqualTo(0.36).Within(1e-9));
    }

    [Test]
    public void CorwinSchultz_RecoversAPlantedSpread()
    {
        // Each bar: 100 trades on a random-walk midpoint; highs print at the ask, lows at the bid.
        const double spread = 0.02;
        var rng = new Random(5);
        var bars = new List<Candle>();
        double mid = 3.0;
        var t0 = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        for (int b = 0; b < 2000; b++)
        {
            double hi = double.MinValue, lo = double.MaxValue;
            for (int k = 0; k < 100; k++)
            {
                mid *= 1 + 0.0005 * Synthetic.Gaussian(rng);
                hi = Math.Max(hi, mid);
                lo = Math.Min(lo, mid);
            }
            decimal h = (decimal)(hi * (1 + spread / 2)), l = (decimal)(lo * (1 - spread / 2)), c = (decimal)mid;
            bars.Add(new Candle { Symbol = "CS", StartUtc = t0.AddMinutes(b), EndUtc = t0.AddMinutes(b + 1), Open = c, High = h, Low = l, Close = c, Volume = 1000 });
        }
        double estimate = TradingCosts.CorwinSchultzSpread(bars);
        Assert.That(estimate, Is.InRange(spread * 0.7, spread * 1.3), $"estimate {estimate:P3}");
    }

    [Test]
    public void CorwinSchultz_NoSpread_IsNearZero()
    {
        var closes = Synthetic.PricesFromReturns(Synthetic.RandomWalkReturns(500, 6));
        Assert.That(TradingCosts.CorwinSchultzSpread(Synthetic.Candles(closes)), Is.LessThan(0.003));
    }

    [Test]
    public void SquareRootImpact_GrowsWithTheSquareRootOfParticipation()
    {
        double one = TradingCosts.SquareRootImpact(1_000, 1_000_000, 0.05);
        double four = TradingCosts.SquareRootImpact(4_000, 1_000_000, 0.05);
        Assert.That(four, Is.EqualTo(2 * one).Within(1e-12));
        Assert.That(one, Is.EqualTo(0.05 * Math.Sqrt(0.001)).Within(1e-12));
        Assert.That(TradingCosts.SquareRootImpact(0, 1_000_000, 0.05), Is.EqualTo(0));
    }

    [Test]
    public void QuotedSpread_PrefersQuotes_AndRoundTripAddsImpactAndFees()
    {
        Assert.That(TradingCosts.QuotedSpread(new[] { (1.99, 2.01), (2.98, 3.02) }), Is.EqualTo((0.01 + 0.04 / 3) / 2).Within(1e-12));
        var bars = Synthetic.Candles(Synthetic.PricesFromReturns(Synthetic.RandomWalkReturns(780, 7)));
        var est = TradingCosts.Estimate(bars, 2, shares: 10_000, new CostParameters { FeePerOrder = 1 }, quotes: new[] { (2.99, 3.01) });
        Assert.That(est.SpreadSource, Is.EqualTo("quotes"));
        Assert.That(est.ImpactPerSide, Is.GreaterThan(0));
        Assert.That(est.FeesRoundTrip, Is.GreaterThan(0));
        Assert.That(est.RoundTrip, Is.EqualTo(est.Spread + 2 * est.ImpactPerSide + est.FeesRoundTrip).Within(1e-15));
        Assert.That(TradingCosts.Estimate(Array.Empty<Candle>(), 0, 1).SpreadSource, Is.EqualTo("fallback"));
    }

    [Test]
    public void Scorecard_EdgeAfterCost_UsesEachCallsOwnCost()
    {
        var card = new DirectionalScorecard();
        card.Record(Direction.Up, 0.02, 0.004);
        card.Record(Direction.Down, -0.01, 0.002);
        card.Record(Direction.Flat, 0.5, 0.9);
        Assert.That(card.MeanCost, Is.EqualTo(0.003).Within(1e-12), "abstentions carry no cost");
        Assert.That(card.MeanEdgeAfterCost(), Is.EqualTo(0.015 - 0.003).Within(1e-12));
        var other = new DirectionalScorecard();
        other.Record(Direction.Up, 0.01, 0.01);
        card.Merge(other);
        Assert.That(card.MeanCost, Is.EqualTo(0.016 / 3).Within(1e-12));
    }
}
