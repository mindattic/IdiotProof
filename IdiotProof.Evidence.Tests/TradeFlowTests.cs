using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T460: sub-penny retail signing, minute aggregation and the imbalance-collapse footprint.</summary>
public class TradeFlowTests
{
    private static readonly DateTime T0 = new(2026, 7, 17, 12, 30, 0, DateTimeKind.Utc);

    [Test]
    public void RetailSign_UsesTheSubPennyFractionOnTheTrfOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TradeFlow.RetailSign(10.0065, "D"), Is.EqualTo(1), "0.65 of a cent: retail buy");
            Assert.That(TradeFlow.RetailSign(10.0021, "D"), Is.EqualTo(-1), "0.21 of a cent: retail sell");
            Assert.That(TradeFlow.RetailSign(10.0050, "D"), Is.EqualTo(0), "mid-cent is unsigned");
            Assert.That(TradeFlow.RetailSign(10.01, "D"), Is.EqualTo(0), "round cent is unsigned");
            Assert.That(TradeFlow.RetailSign(10.0065, "Q"), Is.EqualTo(0), "not off-exchange");
        });
    }

    [Test]
    public void Aggregate_SumsPerMinute_AndRoundTripsThroughCsv()
    {
        var minutes = TradeFlow.Aggregate([
            (T0.AddSeconds(5), 10.0065, 50, "D"),
            (T0.AddSeconds(40), 10.01, 200, "Q"),
            (T0.AddMinutes(1).AddSeconds(1), 10.0021, 300, "D"),
        ]);
        var back = TradeFlow.FromCsv(TradeFlow.ToCsv(minutes).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Multiple(() =>
        {
            Assert.That(minutes, Has.Count.EqualTo(2));
            Assert.That(minutes[0].Prints, Is.EqualTo(2));
            Assert.That(minutes[0].Shares, Is.EqualTo(250));
            Assert.That(minutes[0].OddLotShares, Is.EqualTo(50));
            Assert.That(minutes[0].OffExchangeShares, Is.EqualTo(50));
            Assert.That(minutes[0].RetailImbalance, Is.EqualTo(1));
            Assert.That(minutes[1].RetailImbalance, Is.EqualTo(-1));
            Assert.That(back, Is.EqualTo(minutes));
        });
    }

    [Test]
    public void Collapse_NeedsBuyersFirst_ThenANegativeWindow()
    {
        var prints = new List<(DateTime, double, double, string)>();
        for (int m = 0; m < 10; m++) prints.Add((T0.AddMinutes(m), 10.0065, 100, "D"));
        for (int m = 10; m < 20; m++) prints.Add((T0.AddMinutes(m), 10.0021, 100, "D"));
        var minutes = TradeFlow.Aggregate(prints);
        var collapse = TradeFlow.ImbalanceCollapse(minutes, T0, T0.AddMinutes(30));
        var neverArmed = TradeFlow.ImbalanceCollapse(TradeFlow.Aggregate(prints.Skip(10)), T0, T0.AddMinutes(30));
        Assert.Multiple(() =>
        {
            Assert.That(collapse, Is.EqualTo(T0.AddMinutes(13)), "the window 08:38-08:42 is 2 buys vs 3 sells");
            Assert.That(neverArmed, Is.Null, "selling alone never arms");
        });
    }
}
