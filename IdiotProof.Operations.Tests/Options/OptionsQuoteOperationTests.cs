using IdiotProof.Operations.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options quote (IP-T190): bid/ask with sizes, mid, spread, last trade in ET, from the contract's snapshot.</summary>
public class OptionsQuoteOperationTests
{
    [Test]
    public async Task QuotesTheContract_FromItsSnapshot()
    {
        var alpaca = new OptionsDataAlpaca().On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot());
        var result = await new OptionsQuoteOperation().ExecuteAsync(new OptionsOccArgs { Occ = "aapl261120c00250000" }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Urls.Single(), Does.Contain("symbols=AAPL261120C00250000").And.Contain("feed=indicative"));
        Assert.That(result.Human, Does.Contain("$5.10 x 12").And.Contain("$5.30 x 8").And.Contain("$520.00 per contract")
            .And.Contain("3.8% of mid").And.Contain("1:58 PM ET").And.Contain("21 DTE"));
        var data = OptionsDataKit.Data(result);
        Assert.That(data.GetProperty("mid").GetDecimal(), Is.EqualTo(5.20m));
        Assert.That(data.GetProperty("spread").GetDecimal(), Is.EqualTo(0.20m));
    }

    [Test]
    public async Task NotAnOccSymbol_IsAUsageError()
    {
        var ex = Assert.ThrowsAsync<OperationException>(() =>
            new OptionsQuoteOperation().ExecuteAsync(new OptionsOccArgs { Occ = "AAPL" }, OptionsDataKit.Context(new OptionsDataAlpaca())));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public void NoSnapshot_Fails()
    {
        var alpaca = new OptionsDataAlpaca().On("/v1beta1/options/snapshots?", """{"snapshots":{}}""");
        var ex = Assert.ThrowsAsync<OperationException>(() =>
            new OptionsQuoteOperation().ExecuteAsync(new OptionsOccArgs { Occ = "AAPL261120C00250000" }, OptionsDataKit.Context(alpaca)));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.Failed));
    }
}
