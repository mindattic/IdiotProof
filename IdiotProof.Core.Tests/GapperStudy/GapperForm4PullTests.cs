using System.Net;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T442 A2: <c>pull --tier form4</c> turns the Form 4 XML inside a submission text into an Insider
/// claim with its transaction lines, and the snapshot stage reads them back as form4BuysD30.
/// </summary>
[TestFixture]
public class GapperForm4PullTests
{
    private static readonly DateOnly D = new(2026, 7, 17);
    private GapperStudyHost host = null!;

    [SetUp] public void SetUp() => host = new GapperStudyHost();
    [TearDown] public void TearDown() => host.Dispose();

    private const string PurchaseXml = """
        <?xml version="1.0"?>
        <ownershipDocument>
          <issuer><issuerCik>0001234567</issuerCik><issuerTradingSymbol>GAP</issuerTradingSymbol></issuer>
          <reportingOwner>
            <reportingOwnerId><rptOwnerName>Buyer Bob</rptOwnerName></reportingOwnerId>
            <reportingOwnerRelationship><isDirector>1</isDirector></reportingOwnerRelationship>
          </reportingOwner>
          <nonDerivativeTable>
            <nonDerivativeTransaction>
              <transactionDate><value>2026-07-10</value></transactionDate>
              <transactionCoding><transactionCode>P</transactionCode></transactionCoding>
              <transactionAmounts>
                <transactionShares><value>10000</value></transactionShares>
                <transactionPricePerShare><value>1.50</value></transactionPricePerShare>
                <transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>
              </transactionAmounts>
              <postTransactionAmounts><sharesOwnedFollowingTransaction><value>50000</value></sharesOwnedFollowingTransaction></postTransactionAmounts>
            </nonDerivativeTransaction>
          </nonDerivativeTable>
        </ownershipDocument>
        """;

    private static string Submission(string xml) =>
        "<SEC-DOCUMENT>0001234567-26-000001.txt : 20260711\n<SEC-HEADER>ACCESSION NUMBER: 0001234567-26-000001\n</SEC-HEADER>\n<DOCUMENT>\n<TYPE>4\n<TEXT>\n<XML>\n" + xml + "\n</XML>\n</TEXT>\n</DOCUMENT>\n</SEC-DOCUMENT>\n";

    private sealed class StubHandler(Func<HttpRequestMessage, string?> body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            var text = body(request);
            return Task.FromResult(text is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Test]
    public void ExtractOwnershipXml_FindsTheDocumentInsideTheSubmission_AndTheFixtureParses()
    {
        var fixture = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "sample-form4.xml"));
        var xml = Form4Submission.ExtractOwnershipXml(Submission(fixture))!;
        var parsed = new Form4Parser(NullLogger<Form4Parser>.Instance).Parse(xml, null);
        Assert.Multiple(() =>
        {
            Assert.That(xml, Does.StartWith("<?xml"));
            Assert.That(xml, Does.EndWith("</ownershipDocument>"));
            Assert.That(parsed, Has.Count.EqualTo(2));
            Assert.That(Form4Submission.ExtractOwnershipXml("<SEC-DOCUMENT>no xml here</SEC-DOCUMENT>"), Is.Null);
            Assert.That(Form4Submission.AccessionOf("https://www.sec.gov/Archives/edgar/data/1234567/000123456726000001/0001234567-26-000001.txt"), Is.EqualTo("0001234567-26-000001"));
            Assert.That(Form4Submission.Url("0001234567", "0001234567-26-000001"), Is.EqualTo("https://www.sec.gov/Archives/edgar/data/1234567/000123456726000001/0001234567-26-000001.txt"));
        });
    }

    [Test]
    public async Task PullForm4_WritesOneClaimPerFiling_WithPurchaseLines_ThenTheSnapshotReadsThem()
    {
        await using (var db = await host.DbAsync())
        {
            db.GapperStudyUniverseDays.Add(new GapperStudyUniverseDay { Symbol = "GAP", DateEt = D, Split = GapperStudyServices.SmokeSplit, AdjPrevClose = 2m, Adv20Shares = 1, Adv20Dollars = 1, IsCandidate = true });
            db.GapperStudyCikMap.Add(new GapperStudyCikMap { Symbol = "GAP", Name = "Gap Inc", Cik = "1234567", CikSource = "company_tickers.json" });
            db.EdgarFilingIndex.Add(new EdgarFilingIndexRow { Cik = "1234567", Symbol = "GAP", FormType = "4", AccessionNumber = "0001234567-26-000001", FiledDate = D.AddDays(-7), AcceptedUtc = new DateTime(2026, 7, 10, 20, 15, 0, DateTimeKind.Utc), CompanyName = "Gap Inc", SourceQuarter = "2026/QTR3" });
            db.EdgarFilingIndex.Add(new EdgarFilingIndexRow { Cik = "1234567", Symbol = "GAP", FormType = "4", AccessionNumber = "0001234567-26-000002", FiledDate = D.AddDays(-60), CompanyName = "Gap Inc", SourceQuarter = "2026/QTR2" });
            db.EdgarFilingIndex.Add(new EdgarFilingIndexRow { Cik = "1234567", Symbol = "GAP", FormType = "8-K", AccessionNumber = "0001234567-26-000003", FiledDate = D.AddDays(-2), CompanyName = "Gap Inc", SourceQuarter = "2026/QTR3" });
            await db.SaveChangesAsync();
        }
        var handler = new StubHandler(req => req.RequestUri!.ToString().EndsWith("0001234567-26-000001.txt") ? Submission(PurchaseXml) : null);
        var stage = new GapperPullStage(host.Get<GapperStudyServices>(), null!, null!, null!, new EdgarRateGate(), new StubFactory(handler), new Form4Parser(NullLogger<Form4Parser>.Instance));

        var first = await stage.RunAsync(new PullStageRequest(GapperPullStage.Form4Tier, Smoke: true, null, 0, null, null), CancellationToken.None);
        var again = await stage.RunAsync(new PullStageRequest(GapperPullStage.Form4Tier, Smoke: true, null, 0, null, null), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(first.Single().Rows, Is.EqualTo(1), "one transaction line");
            Assert.That(first.Single().Calls, Is.EqualTo(1), "only the Form 4 inside [D-30, D] is fetched: not the 60-day-old one, not the 8-K");
            Assert.That(handler.Requests.Single().ToString(), Does.EndWith("/1234567/000123456726000001/0001234567-26-000001.txt"));
            Assert.That(again.Single().Calls, Is.EqualTo(0), "resumable: covered accessions are not fetched again");
        });

        await using (var db = await host.DbAsync())
        {
            var claim = await db.ResearchClaims.SingleAsync();
            var line = await db.InsiderTransactions.SingleAsync();
            Assert.Multiple(() =>
            {
                Assert.That(claim.Ticker, Is.EqualTo("GAP"));
                Assert.That(claim.ClaimType, Is.EqualTo(Form4Submission.ClaimType));
                Assert.That(claim.SourceName, Is.EqualTo(Form4Submission.SourceName));
                Assert.That(claim.SourceTier, Is.EqualTo(1));
                Assert.That(claim.ArticleDate, Is.EqualTo(D.AddDays(-7)));
                Assert.That(claim.ClaimSummary, Does.Contain("Buyer Bob").And.Contain("10,000 shares"));
                Assert.That(line.ClaimId, Is.EqualTo(claim.Id));
                Assert.That(line.TransactionCode, Is.EqualTo("P"));
                Assert.That(line.DollarValue, Is.EqualTo(15_000m));
            });
            var filings = await GapperSnapshotStage.LoadForm4Async(db, host.Params, "GAP", D, CancellationToken.None);
            var unmapped = await GapperSnapshotStage.LoadForm4Async(db, host.Params, "NOPE", D, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(filings, Has.Count.EqualTo(1));
                Assert.That(filings![0].AcceptedUtc, Is.EqualTo(new DateTime(2026, 7, 10, 20, 15, 0, DateTimeKind.Utc)), "the index's acceptance instant, not the filing date");
                Assert.That(filings[0].PurchaseLines, Is.EqualTo(1));
                Assert.That(filings[0].PurchaseDollars, Is.EqualTo(15_000));
                Assert.That(unmapped, Is.Null, "no CIK mapping: form4BuysD30 is null, not zero");
            });
        }
    }
}
