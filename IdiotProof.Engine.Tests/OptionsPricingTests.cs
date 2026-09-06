using IdiotProof.Models;
using IdiotProof.Shared.Options;

namespace IdiotProof.Engine.Tests;

[TestFixture]
public class OptionContractOccTests
{
    [Test]
    public void ParseOcc_DecodesStandardCall()
    {
        var c = OptionContract.ParseOcc("BE251219C00038000");
        Assert.That(c, Is.Not.Null);
        Assert.That(c!.UnderlyingSymbol, Is.EqualTo("BE"));
        Assert.That(c.Expiration, Is.EqualTo(new DateOnly(2025, 12, 19)));
        Assert.That(c.Right, Is.EqualTo(OptionRight.Call));
        Assert.That(c.Strike, Is.EqualTo(38m));
        Assert.That(c.Multiplier, Is.EqualTo(100));
    }

    [Test]
    public void ParseOcc_DecodesPutWithFractionalStrike()
    {
        var c = OptionContract.ParseOcc("NVDA260116P00182500");
        Assert.That(c, Is.Not.Null);
        Assert.That(c!.UnderlyingSymbol, Is.EqualTo("NVDA"));
        Assert.That(c.Right, Is.EqualTo(OptionRight.Put));
        Assert.That(c.Strike, Is.EqualTo(182.5m));
    }

    [TestCase("")]
    [TestCase("AAPL")]
    [TestCase("AAPL251219X00150000")]   // bad right
    [TestCase("AAPL251319C00150000")]   // bad month
    [TestCase("AAPL251219C0015000A")]   // non-numeric strike
    public void ParseOcc_ReturnsNullOnGarbage(string s) =>
        Assert.That(OptionContract.ParseOcc(s), Is.Null);

    [Test]
    public void BuildOcc_RoundTrips()
    {
        var occ = OptionContract.BuildOcc("be", new DateOnly(2025, 12, 19), OptionRight.Call, 38m);
        Assert.That(occ, Is.EqualTo("BE251219C00038000"));
        var back = OptionContract.ParseOcc(occ)!;
        Assert.That(back.Strike, Is.EqualTo(38m));
        Assert.That(back.Expiration, Is.EqualTo(new DateOnly(2025, 12, 19)));
    }
}

[TestFixture]
public class IntrinsicValueCalculatorTests
{
    [Test]
    public void Call_InTheMoney_SplitsPremiumIntoIntrinsicAndExtrinsic()
    {
        // BE at $45, $38 call quoted $9.50 → $7 real, $2.50 hype
        var intrinsic = IntrinsicValueCalculator.Intrinsic(OptionRight.Call, 45m, 38m);
        Assert.That(intrinsic, Is.EqualTo(7m));
        Assert.That(IntrinsicValueCalculator.Extrinsic(9.5m, intrinsic), Is.EqualTo(2.5m));
        Assert.That(IntrinsicValueCalculator.ExtrinsicPercent(9.5m, intrinsic), Is.EqualTo(26.3m));
        Assert.That(IntrinsicValueCalculator.Breakeven(OptionRight.Call, 38m, 9.5m), Is.EqualTo(47.5m));
    }

    [Test]
    public void Call_OutOfTheMoney_IsAllExtrinsic()
    {
        var intrinsic = IntrinsicValueCalculator.Intrinsic(OptionRight.Call, 30m, 38m);
        Assert.That(intrinsic, Is.EqualTo(0m));
        Assert.That(IntrinsicValueCalculator.ExtrinsicPercent(2m, intrinsic), Is.EqualTo(100m));
    }

    [Test]
    public void Put_InTheMoney()
    {
        var intrinsic = IntrinsicValueCalculator.Intrinsic(OptionRight.Put, 30m, 38m);
        Assert.That(intrinsic, Is.EqualTo(8m));
        Assert.That(IntrinsicValueCalculator.Breakeven(OptionRight.Put, 38m, 9m), Is.EqualTo(29m));
    }

    [Test]
    public void Extrinsic_NeverNegative_WhenQuoteBelowParity() =>
        Assert.That(IntrinsicValueCalculator.Extrinsic(6.9m, 7m), Is.EqualTo(0m));

    [TestCase(45, 38, OptionRight.Call, "ITM")]
    [TestCase(30, 38, OptionRight.Call, "OTM")]
    [TestCase(30, 38, OptionRight.Put, "ITM")]
    [TestCase(38.1, 38, OptionRight.Put, "ATM")]
    public void Moneyness(decimal spot, decimal strike, OptionRight right, string expected) =>
        Assert.That(IntrinsicValueCalculator.Moneyness(right, spot, strike), Is.EqualTo(expected));

    [Test]
    public void Breakdown_BundlesEverythingAndScalesCostByMultiplier()
    {
        var contract = new OptionContract { OccSymbol = "BE251219C00038000", UnderlyingSymbol = "BE", Expiration = new DateOnly(2025, 12, 19), Strike = 38m, Right = OptionRight.Call };
        var b = IntrinsicValueCalculator.Breakdown(contract, 45m, 9.5m, new DateOnly(2025, 8, 20));
        Assert.That(b.Intrinsic, Is.EqualTo(7m));
        Assert.That(b.Extrinsic, Is.EqualTo(2.5m));
        Assert.That(b.Breakeven, Is.EqualTo(47.5m));
        Assert.That(b.DaysToExpiration, Is.EqualTo(121));
        Assert.That(b.CostPerContract, Is.EqualTo(950m));
        Assert.That(b.Moneyness, Is.EqualTo("ITM"));
    }
}

[TestFixture]
public class BlackScholesCalculatorTests
{
    // Textbook case: S=100, K=100, T=1y, r=5%, σ=20% → C ≈ 10.4506, P ≈ 5.5735
    private const double S = 100, K = 100, T = 1.0, R = 0.05, Sigma = 0.20;

    [Test]
    public void Call_MatchesTextbookValue() =>
        Assert.That(BlackScholesCalculator.TheoreticalPrice(S, K, T, R, Sigma, OptionRight.Call), Is.EqualTo(10.4506).Within(0.001));

    [Test]
    public void Put_MatchesTextbookValue() =>
        Assert.That(BlackScholesCalculator.TheoreticalPrice(S, K, T, R, Sigma, OptionRight.Put), Is.EqualTo(5.5735).Within(0.001));

    [Test]
    public void PutCallParity_Holds()
    {
        var c = BlackScholesCalculator.TheoreticalPrice(S, K, T, R, Sigma, OptionRight.Call);
        var p = BlackScholesCalculator.TheoreticalPrice(S, K, T, R, Sigma, OptionRight.Put);
        // C − P = S − K·e^(−rT)
        Assert.That(c - p, Is.EqualTo(S - K * Math.Exp(-R * T)).Within(1e-9));
    }

    [Test]
    public void NormalCdf_KnownPoints()
    {
        Assert.That(BlackScholesCalculator.NormalCdf(0), Is.EqualTo(0.5).Within(1e-7));
        Assert.That(BlackScholesCalculator.NormalCdf(1.96), Is.EqualTo(0.9750021).Within(1e-6));
        Assert.That(BlackScholesCalculator.NormalCdf(-1.96), Is.EqualTo(0.0249979).Within(1e-6));
    }

    [Test]
    public void ZeroTime_CollapsesToIntrinsic()
    {
        Assert.That(BlackScholesCalculator.TheoreticalPrice(110, 100, 0, R, Sigma, OptionRight.Call), Is.EqualTo(10));
        Assert.That(BlackScholesCalculator.TheoreticalPrice(90, 100, 0, R, Sigma, OptionRight.Put), Is.EqualTo(10));
    }

    [Test]
    public void Delta_AtmCallNearHalf() =>
        Assert.That(BlackScholesCalculator.Delta(S, K, T, R, Sigma, OptionRight.Call), Is.EqualTo(0.6368).Within(0.001));

    [TestCase(100, 100, 1.0, 0.20, OptionRight.Call)]
    [TestCase(100, 100, 1.0, 0.20, OptionRight.Put)]
    [TestCase(45, 38, 0.33, 0.85, OptionRight.Call)]   // BE-style high-vol ITM call
    [TestCase(45, 60, 0.33, 0.85, OptionRight.Call)]   // deep OTM
    [TestCase(45, 30, 0.10, 0.60, OptionRight.Put)]    // OTM put, short dated
    [TestCase(100, 100, 0.01, 0.30, OptionRight.Call)] // ~4 DTE
    public void ImpliedVolatility_RoundTrips(double s, double k, double t, double sigma, OptionRight right)
    {
        var price = BlackScholesCalculator.TheoreticalPrice(s, k, t, R, sigma, right);
        var iv = BlackScholesCalculator.ImpliedVolatility(s, k, t, R, price, right);
        Assert.That(iv, Is.Not.Null);
        Assert.That(iv!.Value, Is.EqualTo(sigma).Within(1e-4));
    }

    [Test]
    public void ImpliedVolatility_NullWhenPriceBelowIntrinsic() =>
        Assert.That(BlackScholesCalculator.ImpliedVolatility(110, 100, T, R, 5.0, OptionRight.Call), Is.Null);

    [Test]
    public void ImpliedVolatility_NullWhenPriceExceedsUnderlying() =>
        Assert.That(BlackScholesCalculator.ImpliedVolatility(100, 100, T, R, 150, OptionRight.Call), Is.Null);

    [Test]
    public void YearsUntil_FloorsAtPositive()
    {
        var expired = new DateOnly(2020, 1, 1);
        Assert.That(BlackScholesCalculator.YearsUntil(expired, DateTime.UtcNow), Is.GreaterThan(0));
        Assert.That(BlackScholesCalculator.YearsUntil(new DateOnly(2026, 9, 5), new DateTime(2025, 9, 5, 20, 0, 0, DateTimeKind.Utc)), Is.EqualTo(1.0).Within(0.001));
    }
}

[TestFixture]
public class SellSignalEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 15, 0, 0, DateTimeKind.Utc);

    private static BullishClaimSummary Claim(string ticker, int daysAgo, int score = 70, string headline = "Big contract win") =>
        new(ticker, Now.AddDays(-daysAgo), score, headline);

    [Test]
    public void Fires_WhenExtrinsicAtHigh_AndRecentBullishNews()
    {
        var signal = SellSignalEvaluator.Evaluate("BE", 2.5m, 26m, [1.8m, 2.1m, 2.4m], [Claim("BE", 2, 82, "Bloom lands data-center power deal")], Now);
        Assert.That(signal, Is.Not.Null);
        Assert.That(signal!.BullishClaimCount, Is.EqualTo(1));
        Assert.That(signal.TopSignificance, Is.EqualTo(82));
        Assert.That(signal.Message, Does.Contain("BE").And.Contain("consider taking profit"));
    }

    [Test]
    public void Fires_WhenWithinToleranceOfHigh() =>
        Assert.That(SellSignalEvaluator.Evaluate("BE", 2.4m, 25m, [2.2m, 2.5m, 2.3m], [Claim("BE", 1)], Now), Is.Not.Null);

    [Test]
    public void Silent_WhenExtrinsicWellBelowHigh() =>
        Assert.That(SellSignalEvaluator.Evaluate("BE", 1.0m, 12m, [2.5m, 2.4m, 2.5m], [Claim("BE", 1)], Now), Is.Null);

    [Test]
    public void Silent_WhenNoRecentNews() =>
        Assert.That(SellSignalEvaluator.Evaluate("BE", 2.5m, 26m, [2.0m, 2.1m, 2.2m], [Claim("BE", 30)], Now), Is.Null);

    [Test]
    public void Silent_WhenNewsIsForAnotherTicker() =>
        Assert.That(SellSignalEvaluator.Evaluate("BE", 2.5m, 26m, [2.0m, 2.1m, 2.2m], [Claim("NVDA", 1)], Now), Is.Null);

    [Test]
    public void Silent_WhenNoExtrinsicLeft() =>
        Assert.That(SellSignalEvaluator.Evaluate("BE", 0m, 0m, [2.0m, 2.0m, 2.0m], [Claim("BE", 1)], Now), Is.Null);

    [Test]
    public void Silent_UntilMinObservations_ThenFires()
    {
        // With no history the current value is trivially "the high" — that used to nag on the
        // very first refresh after opening a position. Now it needs MinObservations samples first.
        var claims = new[] { Claim("BE", 1) };
        Assert.Multiple(() =>
        {
            Assert.That(SellSignalEvaluator.Evaluate("be", 2.5m, 26m, [], claims, Now), Is.Null, "0 samples");
            Assert.That(SellSignalEvaluator.Evaluate("be", 2.5m, 26m, [2.4m], claims, Now), Is.Null, "1 sample");
            Assert.That(SellSignalEvaluator.Evaluate("be", 2.5m, 26m, [2.4m, 2.45m], claims, Now), Is.Null, "2 samples");
            Assert.That(SellSignalEvaluator.Evaluate("be", 2.5m, 26m, [2.4m, 2.45m, 2.48m], claims, Now), Is.Not.Null, "3 samples = MinObservations");
        });
        Assert.That(SellSignalEvaluator.MinObservations, Is.EqualTo(3));
    }

    [Test]
    public void CaseInsensitiveTicker_StillMatches() =>
        Assert.That(SellSignalEvaluator.Evaluate("be", 2.5m, 26m, [2.4m, 2.45m, 2.48m], [Claim("BE", 1)], Now), Is.Not.Null);
}

[TestFixture]
public class OptionsTradingLevelTests
{
    [TestCase(0, false, false)]
    [TestCase(1, false, true)]
    [TestCase(2, true, true)]
    [TestCase(3, true, true)]
    public void Permissions_FollowAlpacaSemantics(int level, bool buyToOpen, bool sellToOpen)
    {
        Assert.That(OptionsTradingLevel.AllowsBuyToOpen(level), Is.EqualTo(buyToOpen), "level 1 is covered-only: no long calls/puts");
        Assert.That(OptionsTradingLevel.AllowsSellToOpen(level), Is.EqualTo(sellToOpen));
        Assert.That(OptionsTradingLevel.AllowsClosing(level), Is.EqualTo(level >= 1));
    }

    [Test]
    public void Blocker_Level0_BlocksEverything_WithPlainReason()
    {
        foreach (var intent in new[] { "buy_to_open", "sell_to_close", "sell_to_open", "buy_to_close" })
            Assert.That(OptionsTradingLevel.Blocker(0, intent), Does.Contain("isn't approved").And.Contain("Sandbox"), intent);
    }

    [Test]
    public void Blocker_Level1_BlocksOnlyBuyToOpen()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OptionsTradingLevel.Blocker(1, "buy_to_open"), Does.Contain("level 1").And.Contain("level 2"));
            Assert.That(OptionsTradingLevel.Blocker(1, "sell_to_open"), Is.Null);
            Assert.That(OptionsTradingLevel.Blocker(1, "sell_to_close"), Is.Null);
            Assert.That(OptionsTradingLevel.Blocker(1, "buy_to_close"), Is.Null);
        });
    }

    [Test]
    public void Blocker_Level2AndUp_AllowsEverySingleLegIntent()
    {
        foreach (var level in new[] { 2, 3 })
            foreach (var intent in new[] { "buy_to_open", "sell_to_close", "sell_to_open", "buy_to_close" })
                Assert.That(OptionsTradingLevel.Blocker(level, intent), Is.Null, $"{level}/{intent}");
    }

    [Test]
    public void Describe_NeverEmpty_AndNamesTheLevel()
    {
        for (var level = 0; level <= 3; level++)
        {
            Assert.That(OptionsTradingLevel.Describe(level), Does.StartWith($"Level {level}"));
            Assert.That(OptionsTradingLevel.Short(level), Does.StartWith($"Level {level}"));
        }
    }
}
