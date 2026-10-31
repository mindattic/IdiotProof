using System.Net;
using System.Text;
using IdiotProof.Brokers;

namespace IdiotProof.Brokers.Tests;

/// <summary>AlpacaApi: the raw REST layer every Hub Alpaca operation builds on.</summary>
public class AlpacaApiTests
{
    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
    {
        private int next;
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            var (status, body) = replies[Math.Min(next++, replies.Length - 1)];
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        }
    }

    [Test]
    public async Task Get_HitsTheTradingHostForTheMode_WithKeysAndQuery()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, """{"status":"ACTIVE"}"""));
        await using var api = new AlpacaApi("KEY", "SECRET", isPaper: false, handler);

        var root = await api.GetAsync("/v2/orders", new Dictionary<string, string?> { ["status"] = "all", ["symbols"] = "AAPL,MSFT", ["after"] = null });

        Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("ACTIVE"));
        var request = handler.Requests.Single();
        Assert.That(request.RequestUri!.ToString(), Is.EqualTo("https://api.alpaca.markets/v2/orders?status=all&symbols=AAPL%2CMSFT"));
        Assert.That(request.Headers.GetValues("APCA-API-KEY-ID").Single(), Is.EqualTo("KEY"));
    }

    [Test]
    public async Task Data_GoesToTheDataHost_AndPagesFollowTheToken()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"news":[{"id":1},{"id":2}],"next_page_token":"abc"}"""),
            (HttpStatusCode.OK, """{"news":[{"id":3}],"next_page_token":null}"""));
        await using var api = new AlpacaApi("K", "S", isPaper: true, handler);

        var ids = new List<int>();
        await foreach (var item in api.GetDataPagedAsync("/v1beta1/news", "news"))
            ids.Add(item.GetProperty("id").GetInt32());

        Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(handler.Requests[0].RequestUri!.Host, Is.EqualTo("data.alpaca.markets"));
        Assert.That(handler.Requests[1].RequestUri!.Query, Does.Contain("page_token=abc"));
    }

    [Test]
    public async Task Send_PostsJson_And204IsDefault()
    {
        var handler = new ScriptedHandler((HttpStatusCode.NoContent, ""));
        await using var api = new AlpacaApi("K", "S", isPaper: true, handler);

        var root = await api.SendAsync(HttpMethod.Post, "/v2/watchlists", new { name = "tech", symbols = new[] { "AAPL" } });

        Assert.That(root.ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Undefined));
        Assert.That(handler.Requests.Single().RequestUri!.Host, Is.EqualTo("paper-api.alpaca.markets"));
        Assert.That(handler.Bodies.Single(), Is.EqualTo("""{"name":"tech","symbols":["AAPL"]}"""));
    }

    [Test]
    public async Task RetriesTooManyRequests_ThenSucceeds()
    {
        var handler = new ScriptedHandler((HttpStatusCode.TooManyRequests, "{}"), (HttpStatusCode.OK, """{"ok":true}"""));
        await using var api = new AlpacaApi("K", "S", isPaper: true, handler);

        var root = await api.GetAsync("/v2/clock");

        Assert.That(root.GetProperty("ok").GetBoolean(), Is.True);
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void NonSuccess_ThrowsWithAlpacasCodeAndMessage()
    {
        var handler = new ScriptedHandler((HttpStatusCode.Forbidden, """{"code":40310000,"message":"insufficient buying power"}"""));
        var api = new AlpacaApi("K", "S", isPaper: true, handler);

        var ex = Assert.ThrowsAsync<AlpacaApiException>(() => api.GetAsync("/v2/account"));

        Assert.That(ex!.Status, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(ex.AlpacaCode, Is.EqualTo(40310000));
        Assert.That(ex.AlpacaMessage, Is.EqualTo("insufficient buying power"));
    }
}
