using System.Text.Json;
using IdiotProof.Core.Catalysts;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: the news-stream message, the EDGAR latest-filings Atom feed, the filing header and the CIK map parse as the live payloads are shaped.</summary>
[TestFixture]
public sealed class CatalystIntakeParsingTests
{
    private const string NewsFrame = """
        {"T":"n","id":24918784,"headline":"Acme Receives FDA Approval For ZYX-1","summary":"Acme Therapeutics said the FDA approved ZYX-1.",
         "author":"Benzinga Newsdesk","created_at":"2026-11-06T12:30:07Z","updated_at":"2026-11-06T12:30:08Z",
         "url":"https://www.benzinga.com/x/24918784","content":"<p>…</p>","symbols":["ACME","SPY"],"source":"benzinga"}
        """;

    [Test]
    public void NewsMessage_ParsesEveryFieldTheIntakeKeeps()
    {
        var e = JsonDocument.Parse(NewsFrame).RootElement;
        var received = new DateTime(2026, 11, 6, 12, 30, 9, DateTimeKind.Utc);
        Assert.That(CatalystNewsMessage.IsNews(e), Is.True);
        var intake = CatalystNewsMessage.Parse(e, received);

        Assert.That(intake, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(intake!.Source, Is.EqualTo("alpaca-news"));
            Assert.That(intake.ExternalId, Is.EqualTo("24918784"));
            Assert.That(intake.PublishedUtc, Is.EqualTo(new DateTime(2026, 11, 6, 12, 30, 7, DateTimeKind.Utc)));
            Assert.That(intake.ReceivedUtc, Is.EqualTo(received));
            Assert.That(intake.Symbols, Is.EqualTo(new[] { "ACME", "SPY" }));
            Assert.That(intake.Headline, Is.EqualTo("Acme Receives FDA Approval For ZYX-1"));
            Assert.That(intake.Summary, Does.StartWith("Acme Therapeutics"));
            Assert.That(intake.Url, Does.Contain("benzinga.com"));
            Assert.That(intake.SourceName, Is.EqualTo("benzinga"));
            Assert.That(intake.Backfilled, Is.False);
        });
    }

    [Test]
    public void NonNewsFrames_AreIgnored()
    {
        Assert.That(CatalystNewsMessage.IsNews(JsonDocument.Parse("""{"T":"subscription","news":["*"]}""").RootElement), Is.False);
        Assert.That(CatalystNewsMessage.Parse(JsonDocument.Parse("""{"T":"n","id":1}""").RootElement, DateTime.UtcNow), Is.Null, "no headline");
    }

    private const string AtomFeed = """
        <?xml version="1.0" encoding="ISO-8859-1" ?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Latest Filings - Thu, 06 Nov 2026 16:10:00 EST</title>
          <entry>
            <title>8-K - ACME CORP (0001624512) (Filer)</title>
            <link rel="alternate" type="text/html" href="https://www.sec.gov/Archives/edgar/data/1624512/000121390026070452/0001213900-26-070452-index.htm"/>
            <summary type="html">&lt;b&gt;Filed:&lt;/b&gt; 2026-11-06 &lt;b&gt;AccNo:&lt;/b&gt; 0001213900-26-070452 &lt;b&gt;Size:&lt;/b&gt; 120 KB</summary>
            <updated>2026-11-06T16:05:12-05:00</updated>
            <category scheme="https://www.sec.gov/" label="form type" term="8-K"/>
            <id>urn:tag:sec.gov,2008:accession-number=0001213900-26-070452</id>
          </entry>
          <entry>
            <title>4 - DOE JOHN (0001234567) (Reporting)</title>
            <link rel="alternate" type="text/html" href="https://www.sec.gov/Archives/edgar/data/1234567/000112233426000099/0001122334-26-000099-index.htm"/>
            <summary type="html">&lt;b&gt;Filed:&lt;/b&gt; 2026-11-06 &lt;b&gt;AccNo:&lt;/b&gt; 0001122334-26-000099</summary>
            <updated>2026-11-06T16:06:00-05:00</updated>
            <category scheme="https://www.sec.gov/" label="form type" term="4"/>
            <id>urn:tag:sec.gov,2008:accession-number=0001122334-26-000099</id>
          </entry>
          <entry>
            <title>4 - BETA HOLDINGS INC (0000320193) (Issuer)</title>
            <link rel="alternate" type="text/html" href="https://www.sec.gov/Archives/edgar/data/320193/000112233426000099/0001122334-26-000099-index.htm"/>
            <summary type="html">&lt;b&gt;Filed:&lt;/b&gt; 2026-11-06 &lt;b&gt;AccNo:&lt;/b&gt; 0001122334-26-000099</summary>
            <updated>2026-11-06T16:06:00-05:00</updated>
            <category scheme="https://www.sec.gov/" label="form type" term="4"/>
            <id>urn:tag:sec.gov,2008:accession-number=0001122334-26-000099</id>
          </entry>
          <entry>
            <title>424B4 - GAMMA LTD (0000555555) (Filer)</title>
            <link rel="alternate" type="text/html" href="https://www.sec.gov/Archives/edgar/data/555555/000155555526000001/0001555555-26-000001-index.htm"/>
            <summary type="html">&lt;b&gt;Filed:&lt;/b&gt; 2026-11-06</summary>
            <updated>2026-11-06T16:07:00-05:00</updated>
            <category scheme="https://www.sec.gov/" label="form type" term="424B4"/>
            <id>urn:tag:sec.gov,2008:accession-number=0001555555-26-000001</id>
          </entry>
        </feed>
        """;

    [Test]
    public void AtomFeed_ParsesFormCompanyCikRoleAccessionAndTime()
    {
        var entries = EdgarAtomFeed.Parse(AtomFeed);
        Assert.That(entries, Has.Count.EqualTo(4));
        var k = entries[0];
        Assert.Multiple(() =>
        {
            Assert.That(k.FormType, Is.EqualTo("8-K"));
            Assert.That(k.CompanyName, Is.EqualTo("ACME CORP"));
            Assert.That(k.Cik, Is.EqualTo("0001624512"));
            Assert.That(k.Role, Is.EqualTo("Filer"));
            Assert.That(k.AccessionNumber, Is.EqualTo("0001213900-26-070452"));
            Assert.That(k.IndexUrl, Does.EndWith("0001213900-26-070452-index.htm"));
            Assert.That(k.UpdatedUtc, Is.EqualTo(new DateTime(2026, 11, 6, 21, 5, 12, DateTimeKind.Utc)));
            Assert.That(k.Summary, Does.Contain("Filed: 2026-11-06").And.Not.Contain("<b>"));
        });
    }

    [Test]
    public void AtomFeed_OnePerAccession_PrefersTheIssuerEntryOfAForm4()
    {
        var one = EdgarAtomFeed.OnePerAccession(EdgarAtomFeed.Parse(AtomFeed));
        Assert.That(one, Has.Count.EqualTo(3));
        var form4 = one.Single(e => e.FormType == "4");
        Assert.That((form4.CompanyName, form4.Cik, form4.Role), Is.EqualTo(("BETA HOLDINGS INC", "0000320193", "Issuer")));
    }

    [TestCase("8-K", true)]
    [TestCase("8-K/A", true)]
    [TestCase("4", true)]
    [TestCase("S-1", true)]
    [TestCase("424B4", true)]
    [TestCase("424B3", true)]
    [TestCase("10-Q", false)]
    [TestCase("40-F", false)]
    [TestCase("SC 13G", false)]
    public void Poller_AcceptsOnlyTheCatalystForms(string form, bool expected) =>
        Assert.That(EdgarLatestFilingsPoller.Accepts(form), Is.EqualTo(expected));

    [Test]
    public void Poller_FeedUrl_FiltersByTypeAndAsksForAtom() =>
        Assert.That(EdgarLatestFilingsPoller.FeedUrl("424B"), Is.EqualTo("https://www.sec.gov/cgi-bin/browse-edgar?action=getcurrent&type=424B&count=100&output=atom"));

    private const string HeadersHtml = """
        <html><body><pre>
        &lt;SEC-HEADER&gt;0001213900-26-070452.hdr.sgml : 20261106
        &lt;ACCEPTANCE-DATETIME&gt;20261106160512
        ACCESSION NUMBER:		0001213900-26-070452
        CONFORMED SUBMISSION TYPE:	8-K
        PUBLIC DOCUMENT COUNT:		3
        CONFORMED PERIOD OF REPORT:	20261105
        ITEM INFORMATION:		Material Modification to Rights of Security Holders
        ITEM INFORMATION:		Amendments to Articles of Incorporation or Bylaws; Change in Fiscal Year
        ITEM INFORMATION:		Financial Statements and Exhibits
        FILED AS OF DATE:		20261106
        </pre></body></html>
        """;

    [Test]
    public void FilingHeaders_ReadAcceptanceAsEasternAndMapItemTitlesToCodes()
    {
        var h = EdgarFilingHeaders.Parse(HeadersHtml);
        Assert.Multiple(() =>
        {
            Assert.That(h.AcceptanceUtc, Is.EqualTo(new DateTime(2026, 11, 6, 21, 5, 12, DateTimeKind.Utc)), "16:05:12 EST is 21:05:12Z");
            Assert.That(h.Items, Is.EqualTo(new[] { "3.03", "5.03", "9.01" }));
            Assert.That(h.SubmissionType, Is.EqualTo("8-K"));
        });
    }

    [Test]
    public void FilingHeaders_ReadRawItemsTags_AndSummerTime()
    {
        var h = EdgarFilingHeaders.Parse("<ACCEPTANCE-DATETIME>20260722093000\n<ITEMS>2.02\n<ITEMS>9.01\n");
        Assert.That(h.AcceptanceUtc, Is.EqualTo(new DateTime(2026, 7, 22, 13, 30, 0, DateTimeKind.Utc)), "09:30 EDT is 13:30Z");
        Assert.That(h.Items, Is.EqualTo(new[] { "2.02", "9.01" }));
    }

    [Test]
    public void FilingHeaders_Url_UsesTheArchiveLayout() =>
        Assert.That(EdgarFilingHeaders.Url("0001624512", "0001213900-26-070452"),
            Is.EqualTo("https://www.sec.gov/Archives/edgar/data/1624512/000121390026070452/0001213900-26-070452-index-headers.html"));

    [Test]
    public void TickerMap_ParsesCompanyTickersJson_FirstTickerPerCik()
    {
        var map = EdgarTickerMap.Parse("""{"0":{"cik_str":320193,"ticker":"AAPL","title":"Apple Inc."},"1":{"cik_str":320193,"ticker":"AAPL2","title":"dup"},"2":{"cik_str":"0001624512","ticker":"boxl","title":"Boxlight"}}""");
        Assert.Multiple(() =>
        {
            Assert.That(map["320193"], Is.EqualTo("AAPL"));
            Assert.That(map["1624512"], Is.EqualTo("BOXL"));
            Assert.That(EdgarTickerMap.Normalize("0000320193"), Is.EqualTo("320193"));
        });
    }

    [Test]
    public void PollerHeadline_NamesFormCompanyAndItems()
    {
        var entry = new EdgarAtomEntry("8-K", "ACME CORP", "1", "Filer", "0-0-0", "", null, "");
        Assert.That(EdgarLatestFilingsPoller.Headline(entry, ["2.02", "9.01"]), Is.EqualTo("8-K: ACME CORP (items 2.02, 9.01)"));
        Assert.That(EdgarLatestFilingsPoller.Headline(entry, null), Is.EqualTo("8-K: ACME CORP"));
    }
}
