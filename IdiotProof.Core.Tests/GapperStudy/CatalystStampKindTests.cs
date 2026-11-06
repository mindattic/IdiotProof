using IdiotProof.Core.Catalysts;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>fiscal.ai and wire-domain copies carry half-hour batch ingestion stamps (upper bounds); Benzinga-authored items and EDGAR acceptance stamps are real time.</summary>
[TestFixture]
public class CatalystStampKindTests
{
    [TestCase("fiscal.ai", "https://www.globenewswire.com/news-release/2026/11/06/123/0/en/x.html", CatalystStampKind.Batch)]
    [TestCase("Fiscal.ai", null, CatalystStampKind.Batch)]
    [TestCase("benzinga", "https://www.benzinga.com/news/26/11/123/x", CatalystStampKind.Realtime)]
    [TestCase("Benzinga Newsdesk", null, CatalystStampKind.Realtime)]
    [TestCase("Some Aggregator", "https://www.prnewswire.com/news-releases/x-123.html", CatalystStampKind.Batch)]
    [TestCase("Some Aggregator", "https://www.accesswire.com/123/x", CatalystStampKind.Batch)]
    [TestCase("Some Aggregator", "https://www.example.com/x", CatalystStampKind.Unknown)]
    public void News_IsClassifiedBySourceThenWireDomain(string source, string? url, string expected) =>
        Assert.That(CatalystStampKind.Classify(CatalystIntake.AlpacaNews, source, url), Is.EqualTo(expected));

    [Test]
    public void Edgar_IsAlwaysRealtime() =>
        Assert.That(CatalystStampKind.Classify(CatalystIntake.Edgar, "edgar", "https://www.sec.gov/Archives/x"), Is.EqualTo(CatalystStampKind.Realtime));

    [Test]
    public void OnlyBatch_IsAnUpperBound()
    {
        Assert.That(CatalystStampKind.IsUpperBound(CatalystStampKind.Batch), Is.True);
        Assert.That(CatalystStampKind.IsUpperBound(CatalystStampKind.Realtime), Is.False);
        Assert.That(CatalystStampKind.IsUpperBound(null), Is.False);
    }
}
