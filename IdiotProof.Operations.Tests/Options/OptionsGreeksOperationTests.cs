using IdiotProof.Operations.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options greeks (IP-T191): Alpaca's Greeks when sent; the local model, badged Model, when not.</summary>
public class OptionsGreeksOperationTests
{
    [Test]
    public async Task UsesAlpacasGreeks_WhenTheSnapshotHasThem()
    {
        var alpaca = new OptionsDataAlpaca()
            .On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot())
            .On("/v2/stocks/AAPL/trades/latest", OptionsDataKit.Spot);
        var result = await new OptionsGreeksOperation().ExecuteAsync(new OptionsModelArgs { Occ = "AAPL261120C00250000" }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("[Alpaca]").And.Contain("0.5200").And.Contain("+$52.00").And.Contain("-$11.00"));
        var data = OptionsDataKit.Data(result);
        Assert.That(data.GetProperty("source").GetString(), Is.EqualTo("alpaca"));
        Assert.That(data.GetProperty("delta").GetDouble(), Is.EqualTo(0.52).Within(1e-9));
    }

    [Test]
    public async Task NoGreeksFromAlpaca_SolvesIvAndBadgesModel()
    {
        var alpaca = new OptionsDataAlpaca()
            .On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot(greeks: false))
            .On("/v2/stocks/AAPL/trades/latest", OptionsDataKit.Spot);
        var result = await new OptionsGreeksOperation().ExecuteAsync(
            new OptionsModelArgs { Occ = "AAPL261120C00250000", Rate = 4m }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("[Model]").And.Contain("solved from mid $5.20"));
        var data = OptionsDataKit.Data(result);
        Assert.That(data.GetProperty("source").GetString(), Is.EqualTo("model"));
        // ATM 21-day call: delta a touch above 0.5, theta negative, gamma and vega positive.
        Assert.That(data.GetProperty("delta").GetDouble(), Is.InRange(0.5, 0.6));
        Assert.That(data.GetProperty("theta").GetDouble(), Is.LessThan(0));
        Assert.That(data.GetProperty("gamma").GetDouble(), Is.GreaterThan(0));
        Assert.That(data.GetProperty("vega").GetDouble(), Is.GreaterThan(0));
        Assert.That(data.GetProperty("implied_volatility").GetDouble(), Is.InRange(0.15, 0.4));
    }

    [Test]
    public async Task NoGreeksAndNoSpot_AsksForSpot()
    {
        var alpaca = new OptionsDataAlpaca().On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot(greeks: false));
        var result = await new OptionsGreeksOperation().ExecuteAsync(new OptionsModelArgs { Occ = "AAPL261120C00250000" }, OptionsDataKit.Context(alpaca));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("--spot"));
    }
}
