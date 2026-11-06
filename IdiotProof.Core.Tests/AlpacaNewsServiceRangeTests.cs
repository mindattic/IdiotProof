using System.Net;
using System.Net.Http;
using IdiotProof.Core.Services;
using IdiotProof.Engine.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: the date-range news overload walks Alpaca's <c>next_page_token</c> pages without touching <c>GetNewsAsync</c>.</summary>
[TestFixture]
public sealed class AlpacaNewsServiceRangeTests
{
    private const string Page1 = """
        {"news":[
          {"id":1,"headline":"First","summary":"s1","author":"a","created_at":"2026-10-01T12:00:00Z","updated_at":"2026-10-01T12:00:00Z","url":"https://x/1","symbols":["ABCD"],"source":"benzinga"},
          {"id":2,"headline":"Second","summary":"s2","author":"a","created_at":"2026-10-01T13:00:00Z","updated_at":"2026-10-01T13:00:00Z","url":"https://x/2","symbols":["ABCD","EFGH"],"source":"benzinga"}
         ],"next_page_token":"tok-2"}
        """;

    private const string Page2 = """
        {"news":[
          {"id":3,"headline":"Third","summary":"s3","author":"a","created_at":"2026-10-02T09:00:00Z","updated_at":null,"url":"https://x/3","symbols":["EFGH"],"source":"globenewswire"}
         ],"next_page_token":null}
        """;

    [Test]
    public async Task GetNewsRangeAsync_FollowsThePageToken_OldestFirst()
    {
        var handler = new PagingHandler();
        var service = new AlpacaNewsService(new StubFactory(handler),
            new AppSettings { AlpacaApiKeyId = "k", AlpacaApiSecretKey = "s" }, NullLogger<AlpacaNewsService>.Instance);

        var items = new List<AlpacaNewsItem>();
        await foreach (var item in service.GetNewsRangeAsync(["abcd", "EFGH"], new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc)))
            items.Add(item);

        Assert.That(items.Select(i => i.Id).ToArray(), Is.EqualTo(new[] { "1", "2", "3" }));
        Assert.That(items[1].Symbols, Is.EqualTo(new[] { "ABCD", "EFGH" }));
        Assert.That(items[2].UpdatedAt, Is.Null);
        Assert.That(items[0].CreatedAt, Is.EqualTo(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)));
        Assert.That(handler.Urls, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(handler.Urls[0], Does.Contain("symbols=ABCD%2CEFGH").And.Contain("start=2026-10-01T00:00:00Z").And.Contain("end=2026-10-03T00:00:00Z").And.Contain("sort=ASC").And.Not.Contain("page_token"));
            Assert.That(handler.Urls[1], Does.Contain("page_token=tok-2"));
            Assert.That(handler.Keys[0], Is.EqualTo("k"));
        });
    }

    [Test]
    public async Task GetNewsPageAsync_UsesTheCallersKeys_WhenGiven()
    {
        var handler = new PagingHandler();
        var service = new AlpacaNewsService(new StubFactory(handler), new AppSettings(), NullLogger<AlpacaNewsService>.Instance);

        var none = await service.GetNewsPageAsync([], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        var (items, next) = await service.GetNewsPageAsync([], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, keys: ("owner-key", "owner-secret"));

        Assert.That(none.Items, Is.Empty, "no keys at all → no call");
        Assert.That(handler.Urls, Has.Count.EqualTo(1));
        Assert.That(handler.Urls[0], Does.Not.Contain("symbols="));
        Assert.That(handler.Keys[0], Is.EqualTo("owner-key"));
        Assert.That((items.Count, next), Is.EqualTo((2, "tok-2")));
    }

    private sealed class PagingHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public List<string> Keys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            Keys.Add(request.Headers.GetValues("APCA-API-KEY-ID").First());
            var body = url.Contains("page_token=tok-2") ? Page2 : Page1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    /// <summary>A fresh client per call, like the real factory: the service disposes each one after its page.</summary>
    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
