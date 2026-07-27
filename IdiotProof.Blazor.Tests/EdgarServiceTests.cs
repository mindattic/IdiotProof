using System.Net;
using System.Net.Http;
using IdiotProof.Blazor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Locks in the real EDGAR full-text-search JSON schema (confirmed against a live
/// fetch during RFC 0003) — the field names here (<c>form</c>, <c>display_names</c>,
/// <c>ciks</c>, <c>items</c>) previously did NOT match what <see cref="EdgarService"/>
/// parsed (it read the nonexistent <c>form_type</c>/<c>entity_name</c>), so every
/// filing's <c>FormType</c>/<c>EntityName</c> silently came back empty.
/// </summary>
[TestFixture]
public class EdgarServiceTests
{
    // Shape verified live against https://efts.sec.gov/LATEST/search-index for a
    // real Form 4 (AAPL) and a real 8-K (item codes present) — trimmed to the
    // fields EdgarService reads.
    private const string SampleForm4Response = """
        {"hits":{"hits":[{"_id":"0001140361-26-025620:form4.xml","_source":{
            "ciks":["0002100523","0000320193"],
            "display_names":["Borders Ben  (CIK 0002100523)","Apple Inc.  (CIK 0000320193)"],
            "file_date":"2026-06-17","form":"4","adsh":"0001140361-26-025620","items":[]}}]}}
        """;

    private const string SampleEightKResponse = """
        {"hits":{"hits":[{"_id":"0001213900-26-070452:ea0295025-8k_boxlight.htm","_source":{
            "ciks":["0001624512"],
            "display_names":["Boxlight Corp  (BOXL)  (CIK 0001624512)"],
            "file_date":"2026-06-22","form":"8-K","adsh":"0001213900-26-070452",
            "items":["3.03","5.03","7.01"]}}]}}
        """;

    [Test]
    public async Task GetRecentFilingsAsync_Form4_ParsesRealFieldNames()
    {
        var service = BuildService(SampleForm4Response);
        var results = await service.GetRecentFilingsAsync("AAPL", "4");

        Assert.That(results, Has.Count.EqualTo(1));
        var f = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(f.FormType, Is.EqualTo("4"));
            Assert.That(f.FilingDate, Is.EqualTo("2026-06-17"));
            Assert.That(f.EntityName, Is.EqualTo("Apple Inc.  (CIK 0000320193)")); // last display_name = issuer
            Assert.That(f.AccessionNumber, Is.EqualTo("0001140361-26-025620"));
            Assert.That(f.DocumentFileName, Is.EqualTo("form4.xml"));
            Assert.That(f.IssuerCik, Is.EqualTo("0000320193")); // last cik = issuer, verified live
            Assert.That(f.Items, Is.Empty);
        });
    }

    [Test]
    public async Task GetRecentFilingsAsync_EightK_ParsesItemCodes()
    {
        var service = BuildService(SampleEightKResponse);
        var results = await service.GetRecentFilingsAsync("BOXL", "8-K");

        var f = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(f.Items, Is.EquivalentTo(new[] { "3.03", "5.03", "7.01" }));
            Assert.That(f.IssuerCik, Is.EqualTo("0001624512"));
            Assert.That(f.DocumentFileName, Is.EqualTo("ea0295025-8k_boxlight.htm"));
        });
    }

    [Test]
    public async Task GetRecentFilingsAsync_BrowseUrl_IsUniquePerFiling()
    {
        // The dedup logic in ResearchService keys off SourceUrl, so this must be
        // unique per accession, not a shared per-ticker/per-form search page URL.
        var service = BuildService(SampleEightKResponse);
        var results = await service.GetRecentFilingsAsync("BOXL", "8-K");

        Assert.That(results[0].BrowseUrl, Does.Contain("1624512")); // issuer CIK, leading zeros stripped for the archive path
        Assert.That(results[0].BrowseUrl, Does.Contain("0001213900-26-070452")); // accession, with dashes, in the -index.htm filename
    }

    [Test]
    public async Task GetRecentFilingsAsync_NonSuccessStatus_ReturnsEmptyListNotThrow()
    {
        var service = BuildService("", HttpStatusCode.InternalServerError);
        var results = await service.GetRecentFilingsAsync("AAPL", "4");
        Assert.That(results, Is.Empty);
    }

    /// <summary>
    /// Regression test for a real bug: the "edgar" named HttpClient's User-Agent string
    /// registered in Program.cs (IdiotProof.Blazor AND IdiotProof.ResearchScanner) used a
    /// bare email as a second token ("IdiotProof/1 research@idiotproof.app") — invalid per
    /// RFC 7231 grammar (a bare token can't contain "@"), so .NET's strict header parser threw
    /// FormatException on every single call. EdgarService.GetRecentFilingsAsync's own
    /// fail-closed try/catch silently swallowed it and returned an empty list every time — a
    /// live 300-ticker scan run logged this exact FormatException 1,200 times with zero test
    /// or error-count signal that anything was wrong. This test doesn't exercise EdgarService's
    /// mocked HttpClient (which never hits real header parsing) — it directly proves the exact
    /// literal string Program.cs registers is RFC-valid, since that's the only place this class
    /// of bug can hide.
    /// </summary>
    [Test]
    public void EdgarUserAgentString_MatchingProgramCsRegistration_ParsesWithoutThrowing()
    {
        using var client = new HttpClient();
        Assert.DoesNotThrow(() =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IdiotProof/1 (research@idiotproof.app)"));
    }

    private static EdgarService BuildService(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(status, body);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        return new EdgarService(factory, NullLogger<EdgarService>.Instance);
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
