using System.Text;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T459: shares outstanding from XBRL company facts, known from the filing date only.</summary>
public class SharesOutstandingTests
{
    private static Stream Json(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    [Test]
    public void Parse_PrefersTheCoverPageConcept_AndLookupsUseOnlyEarlierFilings()
    {
        var facts = SharesOutstandingHistory.Parse(Json("""
            {"cik": 1234, "entityName": "Abc", "facts": {
              "dei": {"EntityCommonStockSharesOutstanding": {"units": {"shares": [
                {"end": "2023-03-31", "val": 10000000, "filed": "2023-05-10", "form": "10-Q"},
                {"end": "2023-06-30", "val": 25000000, "filed": "2023-08-09", "form": "10-Q"}]}}},
              "us-gaap": {"CommonStockSharesOutstanding": {"units": {"shares": [
                {"end": "2023-06-30", "val": 1, "filed": "2023-08-09"}]}}}}}
            """));
        var h = new SharesOutstandingHistory(facts);
        Assert.Multiple(() =>
        {
            Assert.That(facts, Has.Count.EqualTo(2));
            Assert.That(facts.All(f => f.Source == "dei:EntityCommonStockSharesOutstanding"));
            Assert.That(h.KnownBefore(1234, new DateOnly(2023, 5, 10)), Is.Null, "filed that day: not known before it");
            Assert.That(h.KnownBefore(1234, new DateOnly(2023, 8, 9))!.Shares, Is.EqualTo(10_000_000));
            Assert.That(h.KnownBefore(1234, new DateOnly(2023, 8, 10))!.Shares, Is.EqualTo(25_000_000));
            Assert.That(h.GrowthOverYear(1234, new DateOnly(2023, 9, 1)), Is.EqualTo(1.5).Within(1e-12), "10M to 25M");
            Assert.That(h.KnownBefore(999, new DateOnly(2024, 1, 1)), Is.Null);
        });
    }

    [Test]
    public void Parse_FallsBackToUsGaap_WhenTheCoverConceptIsMissing()
    {
        var facts = SharesOutstandingHistory.Parse(Json("""
            {"cik": "0000005678", "facts": {"us-gaap": {"CommonStockSharesOutstanding": {"units": {"shares": [
              {"end": "2022-12-31", "val": 5000000, "filed": "2023-03-15"}]}}}}}
            """));
        Assert.Multiple(() =>
        {
            Assert.That(facts.Single().Cik, Is.EqualTo(5678));
            Assert.That(facts.Single().Source, Is.EqualTo("us-gaap:CommonStockSharesOutstanding"));
        });
    }
}
