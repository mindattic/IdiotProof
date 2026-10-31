using IdiotProof.Models;
using IdiotProof.Operations.Options;
using IdiotProof.Shared.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options iv (IP-T193): Alpaca's IV next to IV solved locally at bid, mid, ask and last.</summary>
public class OptionsIvOperationTests
{
    [Test]
    public async Task SolvesAtEachPrice_AndTheMidIvReproducesTheMid()
    {
        var alpaca = new OptionsDataAlpaca()
            .On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot())
            .On("/v2/stocks/AAPL/trades/latest", OptionsDataKit.Spot);
        var result = await new OptionsIvOperation().ExecuteAsync(new OptionsModelArgs { Occ = "AAPL261120C00250000", Rate = 4m }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        var data = OptionsDataKit.Data(result);
        Assert.That(data.GetProperty("alpaca").GetDecimal(), Is.EqualTo(0.3m));
        var solved = data.GetProperty("solved").EnumerateArray().ToDictionary(p => p.GetProperty("price").GetString()!, p => p.GetProperty("implied_volatility").GetDouble());
        Assert.That(solved.Keys, Is.EqualTo(new[] { "Bid", "Mid", "Ask", "Last" }));
        Assert.That(solved["Bid"], Is.LessThan(solved["Mid"]));
        Assert.That(solved["Mid"], Is.LessThan(solved["Ask"]));

        var c = OptionContract.ParseOcc("AAPL261120C00250000")!;
        var t = BlackScholesCalculator.YearsUntil(c.Expiration, OptionsDataKit.Now.UtcDateTime);
        Assert.That(BlackScholesCalculator.TheoreticalPrice(250, 250, t, 0.04, solved["Mid"], OptionRight.Call), Is.EqualTo(5.20).Within(1e-4));
        Assert.That(result.Human, Does.Contain("Alpaca").And.Contain("30.0%"));
    }

    [Test]
    public async Task WithoutSpot_ShowsOnlyAlpacasIv()
    {
        var alpaca = new OptionsDataAlpaca().On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot());
        var result = await new OptionsIvOperation().ExecuteAsync(new OptionsModelArgs { Occ = "AAPL261120C00250000" }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("pass --spot"));
    }
}
