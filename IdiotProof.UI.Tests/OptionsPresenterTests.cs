using System.Globalization;
using IdiotProof.Models;
using IdiotProof.UI.Components.Options;

namespace IdiotProof.UI.Tests;

/// <summary>
/// The numbers the chain and ticket render come from <see cref="OptionsPresenter"/>. These pin
/// the IV-source precedence (Alpaca → Model → none), the hype-meter buckets, and that every
/// formatter is culture-invariant (a German UI culture must not print "1.234,50").
/// </summary>
[TestFixture]
public class OptionsPresenterTests
{
    private static readonly DateOnly Today = new(2026, 9, 5);
    private static readonly DateTime NowUtc = new(2026, 9, 5, 15, 0, 0, DateTimeKind.Utc);

    private static OptionContract Call(decimal strike = 40m) => new()
    {
        OccSymbol = OptionContract.BuildOcc("BE", new DateOnly(2026, 12, 18), OptionRight.Call, strike),
        UnderlyingSymbol = "BE", Expiration = new DateOnly(2026, 12, 18), Strike = strike, Right = OptionRight.Call,
    };

    private static OptionQuote Quote(decimal bid, decimal ask, decimal? iv = null) =>
        new("X", bid, ask, null, iv, null, NowUtc);

    [Test]
    public void BuildRow_UsesBrokerIv_WhenSupplied()
    {
        var row = OptionsPresenter.BuildRow(Call(), Quote(5.90m, 6.10m, iv: 0.62m), 45m, 0.04, Today, NowUtc);

        Assert.Multiple(() =>
        {
            Assert.That(row.IvSource, Is.EqualTo("Alpaca"));
            Assert.That(row.ImpliedVolatility, Is.EqualTo(0.62m));
            Assert.That(row.ModelPrice, Is.Not.Null, "fair value is computed from the broker IV");
            Assert.That(row.Mid, Is.EqualTo(6.00m));
            Assert.That(row.Breakdown!.Intrinsic, Is.EqualTo(5m));
            Assert.That(row.Breakdown.Extrinsic, Is.EqualTo(1m));
        });
    }

    [Test]
    public void BuildRow_SolvesModelIv_WhenBrokerOmitsIt()
    {
        var row = OptionsPresenter.BuildRow(Call(), Quote(5.90m, 6.10m), 45m, 0.04, Today, NowUtc);

        Assert.Multiple(() =>
        {
            Assert.That(row.IvSource, Is.EqualTo("Model"));
            Assert.That(row.ImpliedVolatility, Is.Not.Null.And.GreaterThan(0m));
            Assert.That(row.ModelPrice, Is.Not.Null);
            Assert.That(Math.Abs(row.ModelPrice!.Value - 6.00m), Is.LessThan(0.05m), "model re-prices the mid at the solved IV");
        });
    }

    [Test]
    public void BuildRow_NoQuote_NoNumbers()
    {
        var row = OptionsPresenter.BuildRow(Call(), null, 45m, 0.04, Today, NowUtc);
        Assert.Multiple(() =>
        {
            Assert.That(row.Mid, Is.Null);
            Assert.That(row.Breakdown, Is.Null);
            Assert.That(row.ImpliedVolatility, Is.Null);
            Assert.That(row.IvSource, Is.EqualTo("—"));
        });
    }

    [Test]
    public void BuildRow_NoUnderlyingPrice_StillShowsQuote_ButNoBreakdown()
    {
        var row = OptionsPresenter.BuildRow(Call(), Quote(5.90m, 6.10m), null, 0.04, Today, NowUtc);
        Assert.That(row.Mid, Is.EqualTo(6.00m));
        Assert.That(row.Breakdown, Is.Null, "real/hype needs the stock price");
    }

    [TestCase(0, 0)]
    [TestCase(9.9, 0)]
    [TestCase(10, 1)]
    [TestCase(34.9, 1)]
    [TestCase(35, 2)]
    [TestCase(64.9, 2)]
    [TestCase(65, 3)]
    [TestCase(89.9, 3)]
    [TestCase(90, 4)]
    [TestCase(100, 4)]
    public void HypeBucket_Boundaries(decimal extrinsicPercent, int expected) =>
        Assert.That(OptionsPresenter.HypeBucket(extrinsicPercent), Is.EqualTo(expected));

    [Test]
    public void Formatters_AreCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Multiple(() =>
            {
                Assert.That(OptionsPresenter.Money(1234.5m), Is.EqualTo("$1,234.50"));
                Assert.That(OptionsPresenter.Money(null), Is.EqualTo("—"));
                Assert.That(OptionsPresenter.Pct(12.345m), Is.EqualTo("12.3%"));
                Assert.That(OptionsPresenter.Pct(12.345m, 0), Is.EqualTo("12%"));
                Assert.That(OptionsPresenter.Iv(0.4567m), Is.EqualTo("46%"));
                Assert.That(OptionsPresenter.Iv(null), Is.EqualTo("—"));
            });
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
