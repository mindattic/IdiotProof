using IdiotProof.Operations.Options;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options bars / trades / latest / snapshot / exchanges / conditions (IP-T310) over the v1beta1 options data API.</summary>
public class OptionsMarketDataOperationsTests
{
    private const string Occ = "AAPL261120C00250000";

    [Test]
    public async Task Bars_DailyWindowInEt_AndPaged()
    {
        var alpaca = new OptionsDataAlpaca()
            .On("page_token=n1", """{"bars":{"OCC":[{"t":"2026-10-29T04:00:00Z","o":5.0,"h":5.4,"l":4.9,"c":5.1,"v":800,"n":40,"vw":5.12}]},"next_page_token":null}""".Replace("OCC", Occ))
            .On("/v1beta1/options/bars", """{"bars":{"OCC":[{"t":"2026-10-28T04:00:00Z","o":4.5,"h":5.0,"l":4.4,"c":4.9,"v":600,"n":30,"vw":4.8}]},"next_page_token":"n1"}""".Replace("OCC", Occ));
        var result = await new OptionsBarsOperation().ExecuteAsync(new OptionsBarsArgs { Occ = Occ }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Urls[0], Does.Contain("symbols=" + Occ).And.Contain("timeframe=1Day")
            .And.Contain("start=2026-09-30T04:00:00Z").And.Contain("end=2026-10-31T04:00:00Z"));
        Assert.That(result.Human, Does.Contain("2026-10-28").And.Contain("2026-10-29").And.Contain("$5.12"));
        Assert.That(OptionsDataKit.Data(result).GetProperty("bars").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task Bars_RejectsABadTimeframe()
    {
        var result = await new OptionsBarsOperation().ExecuteAsync(new OptionsBarsArgs { Occ = Occ, Timeframe = "7Fortnights" }, OptionsDataKit.Context(new OptionsDataAlpaca()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task Trades_NewestFirst_InEt()
    {
        var alpaca = new OptionsDataAlpaca().On("/v1beta1/options/trades",
            """{"trades":{"OCC":[{"t":"2026-10-30T17:58:30Z","p":5.2,"s":3,"x":"C","c":"I"},{"t":"2026-10-30T15:00:00Z","p":5.0,"s":7,"x":"N","c":"a"}]},"next_page_token":null}""".Replace("OCC", Occ));
        var result = await new OptionsTradesOperation().ExecuteAsync(new OptionsTradesArgs { Occ = Occ }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Urls[0], Does.Contain("sort=desc").And.Contain("start=2026-10-23T04:00:00Z"));
        Assert.That(result.Human, Does.Contain("2 trades, 10 contracts").And.Contain("2026-10-30 1:58 PM ET").And.Contain("11:00 AM ET"));
    }

    [Test]
    public async Task Latest_QuoteAndTrade()
    {
        var alpaca = new OptionsDataAlpaca()
            .On("/v1beta1/options/quotes/latest", """{"quotes":{"OCC":{"t":"2026-10-30T17:59:00Z","bp":5.1,"bs":12,"bx":"C","ap":5.3,"as":8,"ax":"N","c":"A"}}}""".Replace("OCC", Occ))
            .On("/v1beta1/options/trades/latest", """{"trades":{"OCC":{"t":"2026-10-30T17:58:30Z","p":5.2,"s":3,"x":"C","c":"I"}}}""".Replace("OCC", Occ));
        var result = await new OptionsLatestOperation().ExecuteAsync(new OptionsOccArgs { Occ = Occ, Feed = OptionsFeed.Opra }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Urls, Has.All.Contains("feed=opra"));
        Assert.That(result.Human, Does.Contain("$5.10 x 12 (C)").And.Contain("$5.30 x 8 (N)").And.Contain("$5.20 x 3 (C, cond I)"));
    }

    [Test]
    public async Task Snapshot_ShowsGreeksAndBars()
    {
        var alpaca = new OptionsDataAlpaca().On("/v1beta1/options/snapshots?", OptionsDataKit.Snapshot());
        var result = await new OptionsSnapshotOperation().ExecuteAsync(new OptionsOccArgs { Occ = Occ }, OptionsDataKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("delta 0.5200").And.Contain("O $4.80").And.Contain("30.0%"));
    }

    [Test]
    public async Task ExchangesAndConditions_AreCodeTables()
    {
        var alpaca = new OptionsDataAlpaca()
            .On("/v1beta1/options/meta/exchanges", """{"C":"Cboe Options Exchange","A":"NYSE American Options"}""")
            .On("/v1beta1/options/meta/conditions/quote", """{"A":"Regular"}""");
        var exchanges = await new OptionsExchangesOperation().ExecuteAsync(new NoArgs(), OptionsDataKit.Context(alpaca));
        var conditions = await new OptionsConditionsOperation().ExecuteAsync(new OptionsConditionsArgs { Tick = OptionsTickType.Quote }, OptionsDataKit.Context(alpaca));

        Assert.That(exchanges.Human!.IndexOf("NYSE American", StringComparison.Ordinal), Is.LessThan(exchanges.Human.IndexOf("Cboe", StringComparison.Ordinal)));
        Assert.That(conditions.Human, Does.Contain("Regular"));
        Assert.That(alpaca.Urls[1], Does.EndWith("/conditions/quote"));
    }
}
