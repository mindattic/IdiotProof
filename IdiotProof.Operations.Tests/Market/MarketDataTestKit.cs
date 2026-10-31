using System.Net;
using System.Text;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>
/// A scripted Alpaca Market Data host for the <c>market ...</c> operation tests: replies are
/// matched by request path (first registered prefix that matches wins), every request is recorded,
/// and an unmatched path is a 404 so a test never silently reaches the real API.
/// </summary>
internal sealed class ScriptedMarketHandler : HttpMessageHandler
{
    private readonly List<(string Path, HttpStatusCode Status, byte[] Body, string ContentType)> routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public ScriptedMarketHandler On(string path, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(path, Encoding.UTF8.GetBytes(json), "application/json", status);

    public ScriptedMarketHandler On(string path, byte[] body, string contentType, HttpStatusCode status = HttpStatusCode.OK)
    {
        routes.Add((path, status, body, contentType));
        return this;
    }

    /// <summary>The first recorded request whose path starts with <paramref name="path"/>.</summary>
    public HttpRequestMessage Request(string path) => Requests.First(r => r.RequestUri!.AbsolutePath.StartsWith(path, StringComparison.Ordinal));

    /// <summary>A query parameter of a recorded request, unescaped.</summary>
    public string? Query(string path, string name)
    {
        var query = Request(path).RequestUri!.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (Uri.UnescapeDataString(kv[0]) == name) return kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var path = request.RequestUri!.AbsolutePath;
        foreach (var route in routes)
        {
            if (!path.StartsWith(route.Path, StringComparison.Ordinal)) continue;
            var content = new ByteArrayContent(route.Body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(route.ContentType);
            return Task.FromResult(new HttpResponseMessage(route.Status) { Content = content });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($$"""{"code":40410000,"message":"unscripted {{path}}"}""", Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>An <see cref="AlpacaApiFactory"/> that hands back an <see cref="AlpacaApi"/> over the scripted handler.</summary>
internal sealed class ScriptedMarketFactory(HttpMessageHandler handler) : AlpacaApiFactory(null!, null!, null!)
{
    public override Task<AlpacaApi> ForDataAsync(Guid? ownerUserId = null, CancellationToken ct = default) =>
        Task.FromResult(new AlpacaApi("k", "s", isPaper: true, handler));

    public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default) =>
        throw new AssertionException("market data operations must not open a Trading API client");
}

internal static class MarketDataTestKit
{
    /// <summary>Fri 2026-10-30 2:00 PM ET (EDT, UTC-4).</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);

    public static OperationContext Context(ScriptedMarketHandler handler, DateTimeOffset? now = null) => new()
    {
        Services = new ServiceCollection().AddSingleton<AlpacaApiFactory>(new ScriptedMarketFactory(handler)).BuildServiceProvider(),
        Clock = new Fixtures.FixedClock(now ?? Now),
        OwnerId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
    };
}
