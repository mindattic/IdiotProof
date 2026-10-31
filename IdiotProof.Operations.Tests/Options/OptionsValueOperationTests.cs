using IdiotProof.Models;
using IdiotProof.Operations.Options;
using IdiotProof.Shared.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options value (IP-T192): intrinsic/extrinsic, breakeven, DTE and the Black-Scholes price.</summary>
public class OptionsValueOperationTests
{
    private static OptionsDataAlpaca Alpaca() => new OptionsDataAlpaca()
        .On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot())
        .On("/v2/stocks/AAPL/trades/latest", OptionsDataKit.Spot);

    [Test]
    public async Task SplitsThePremium_AndPricesWithAlpacasIv()
    {
        var result = await new OptionsValueOperation().ExecuteAsync(
            new OptionsValueArgs { Occ = "AAPL261120C00250000", Spot = 252m, Rate = 4m }, OptionsDataKit.Context(Alpaca()));

        Assert.That(result.IsOk, Is.True, result.Message);
        var data = OptionsDataKit.Data(result);
        Assert.That(data.GetProperty("intrinsic").GetDecimal(), Is.EqualTo(2m));
        Assert.That(data.GetProperty("extrinsic").GetDecimal(), Is.EqualTo(3.20m));
        Assert.That(data.GetProperty("breakeven").GetDecimal(), Is.EqualTo(255.20m));
        Assert.That(data.GetProperty("dte").GetInt32(), Is.EqualTo(21));
        Assert.That(data.GetProperty("moneyness").GetString(), Is.EqualTo("ITM"));
        Assert.That(data.GetProperty("iv_source").GetString(), Is.EqualTo("Alpaca"));

        var c = OptionContract.ParseOcc("AAPL261120C00250000")!;
        var expected = BlackScholesCalculator.TheoreticalPrice(252, 250, BlackScholesCalculator.YearsUntil(c.Expiration, OptionsDataKit.Now.UtcDateTime), 0.04, 0.3, OptionRight.Call);
        Assert.That(data.GetProperty("theoretical").GetDouble(), Is.EqualTo(expected).Within(1e-6));
        Assert.That(result.Human, Does.Contain("$255.20 at expiry").And.Contain("--spot").And.Contain("21 DTE"));
    }

    [Test]
    public async Task PremiumOverride_ForAPut()
    {
        var alpaca = new OptionsDataAlpaca()
            .On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot("AAPL261120P00260000", greeks: false))
            .On("/v2/stocks/AAPL/trades/latest", OptionsDataKit.Spot);
        var result = await new OptionsValueOperation().ExecuteAsync(
            new OptionsValueArgs { Occ = "AAPL261120P00260000", Premium = 12.5m }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        var data = OptionsDataKit.Data(result);
        Assert.That(data.GetProperty("intrinsic").GetDecimal(), Is.EqualTo(10m));
        Assert.That(data.GetProperty("extrinsic").GetDecimal(), Is.EqualTo(2.5m));
        Assert.That(data.GetProperty("breakeven").GetDecimal(), Is.EqualTo(247.5m));
        Assert.That(data.GetProperty("moneyness").GetString(), Is.EqualTo("ITM"));
        Assert.That(data.GetProperty("iv_source").GetString(), Does.Contain("solved"));
    }
}
