using System.Net;
using IdiotProof.Blazor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

[TestFixture]
public class CorporateActionDetectorTests
{
    private static EdgarFiling Filing(string[]? items, string documentFileName = "doc.htm", string issuerCik = "0000320193") =>
        new(
            FormType: "8-K",
            FilingDate: "2026-06-22",
            EntityName: "Test Corp",
            AccessionNumber: "0001213900-26-070452",
            BrowseUrl: "https://example.test/filing",
            DocumentFileName: documentFileName,
            IssuerCik: issuerCik,
            Items: items);

    private static CorporateActionDetector BuildDetector(HttpStatusCode status, string body = "")
    {
        var handler = new StubHandler(status, body);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var edgar = new EdgarService(factory, NullLogger<EdgarService>.Instance);
        return new CorporateActionDetector(edgar, NullLogger<CorporateActionDetector>.Instance);
    }

    [Test]
    public async Task DetectAsync_LowPriorityItemsOnly_SkipsFetchAndUsesBoilerplate()
    {
        var detector = BuildDetector(HttpStatusCode.OK, "should not be fetched");
        var results = await detector.DetectAsync([Filing(["5.02", "9.01"])]);

        Assert.That(results, Has.Count.EqualTo(1));
        var r = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(r.IsHighPriority, Is.False);
            Assert.That(r.Text, Does.Contain("SEC 8-K filing by Test Corp"));
            Assert.That(r.Reason, Does.Contain("no high-value trigger codes"));
        });
    }

    [Test]
    public async Task DetectAsync_UnknownItemShape_TreatsAsHighPriority()
    {
        var detector = BuildDetector(HttpStatusCode.OK, "real filing text");
        var results = await detector.DetectAsync([Filing(null)]);

        Assert.That(results[0].IsHighPriority, Is.True);
        Assert.That(results[0].Reason, Does.Contain("item codes unavailable"));
        Assert.That(results[0].Text, Is.EqualTo("real filing text"));
    }

    [Test]
    public async Task DetectAsync_SplitSignatureItems_UsesSpecialCasedReasonAndFetchesDocument()
    {
        var detector = BuildDetector(HttpStatusCode.OK, "charter amendment full text");
        var results = await detector.DetectAsync([Filing(["3.03", "5.03", "9.01"])]);

        var r = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(r.IsHighPriority, Is.True);
            Assert.That(r.Reason, Does.Contain("reverse/forward stock split signature"));
            Assert.That(r.Text, Is.EqualTo("charter amendment full text"));
        });
    }

    [Test]
    public async Task DetectAsync_HighPriorityButFetchFails_FallsBackToBoilerplateAndNotesFailure()
    {
        var detector = BuildDetector(HttpStatusCode.NotFound);
        var results = await detector.DetectAsync([Filing(["2.01"])]);

        var r = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(r.IsHighPriority, Is.True);
            Assert.That(r.Text, Does.Contain("SEC 8-K filing by Test Corp"));
            Assert.That(r.Reason, Does.Contain("document fetch failed"));
        });
    }

    [Test]
    public async Task DetectAsync_MultipleFilings_ProcessesAllIndependently()
    {
        var detector = BuildDetector(HttpStatusCode.OK, "text");
        var results = await detector.DetectAsync([Filing(["5.02"]), Filing(["2.01"]), Filing(null)]);
        Assert.That(results, Has.Count.EqualTo(3));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
