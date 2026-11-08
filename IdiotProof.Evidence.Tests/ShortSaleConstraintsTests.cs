using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T461: FINRA short-interest parsing and publication-day lookups, and the Rule 201 trigger.</summary>
public class ShortSaleConstraintsTests
{
    private const string Header = "accountingYearMonthNumber|symbolCode|issueName|issuerServicesGroupExchangeCode|marketClassCode|currentShortPositionQuantity|previousShortPositionQuantity|stockSplitFlag|averageDailyVolumeQuantity|daysToCoverQuantity|revisionFlag|changePercent|changePreviousNumber|settlementDate";

    [Test]
    public void ShortInterest_IsUsedOnlyOncePublishedBeforeD()
    {
        var records = ShortInterestHistory.Parse([
            Header,
            "20211115|ABC|Abc Inc|A|NYSE|4739521|4632252||1288553|3.68||2.32|107269|2021-11-15",
            "20211130|ABC|Abc Inc|A|NYSE|5000000|4739521||1000000|5||5.49|260479|2021-11-30",
            "garbage line",
        ]).ToList();
        var h = new ShortInterestHistory(records);
        static DateOnly Published(DateOnly settle) => settle.AddDays(10);
        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records[0].DaysToCover, Is.EqualTo(3.68));
            Assert.That(h.KnownBefore("ABC", new DateOnly(2021, 11, 25), Published), Is.Null, "published 11-25, not strictly before");
            Assert.That(h.KnownBefore("ABC", new DateOnly(2021, 11, 26), Published)!.Settlement, Is.EqualTo(new DateOnly(2021, 11, 15)));
            Assert.That(h.KnownBefore("abc", new DateOnly(2021, 12, 20), Published)!.ShortShares, Is.EqualTo(5_000_000));
            Assert.That(h.KnownBefore("XYZ", new DateOnly(2021, 12, 20), Published), Is.Null);
        });
    }

    [Test]
    public void Rule201_TriggersAtATenPercentDropBelowThePriorClose()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ShortSaleRestriction.PriorDayLowVsClose(9.00m, 10.00m), Is.EqualTo(-0.10).Within(1e-12));
            Assert.That(ShortSaleRestriction.ActiveOn(ShortSaleRestriction.PriorDayLowVsClose(9.00m, 10.00m)), "exactly 10% triggers");
            Assert.That(ShortSaleRestriction.ActiveOn(ShortSaleRestriction.PriorDayLowVsClose(9.01m, 10.00m)), Is.False);
            Assert.That(ShortSaleRestriction.ActiveOn(null), Is.False);
            Assert.That(ShortSaleRestriction.PriorDayLowVsClose(9m, 0m), Is.Null);
        });
    }
}
