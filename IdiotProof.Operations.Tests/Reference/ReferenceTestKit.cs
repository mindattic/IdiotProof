using System.Net;
using System.Text;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Reference;

/// <summary>
/// A scripted Alpaca: replies are routed by "METHOD path-prefix" (longest match wins), every
/// request and body is recorded, and nothing ever leaves the process.
/// </summary>
internal sealed class RoutedAlpaca : HttpMessageHandler
{
    private readonly List<(string Method, string Prefix, HttpStatusCode Status, string Body)> routes = [];

    public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

    public RoutedAlpaca On(string method, string pathPrefix, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        routes.Add((method, pathPrefix, status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!, body));
        var path = request.RequestUri!.AbsolutePath;
        var route = routes
            .Where(r => r.Method == request.Method.Method && path.StartsWith(r.Prefix, StringComparison.Ordinal))
            .OrderByDescending(r => r.Prefix.Length)
            .FirstOrDefault();
        if (route.Prefix is null)
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"code":40410000,"message":"no route"}""") };
        return new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
    }
}

/// <summary>An <see cref="AlpacaApiFactory"/> that hands back clients over a <see cref="RoutedAlpaca"/>.</summary>
internal sealed class ScriptedAlpacaFactory(RoutedAlpaca handler) : AlpacaApiFactory(null!, null!, null!)
{
    public List<BrokerMode> Modes { get; } = [];

    public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default)
    {
        Modes.Add(mode);
        return Task.FromResult(new AlpacaApi("k", "s", mode == BrokerMode.Paper, handler));
    }

    public override Task<AlpacaApi> ForDataAsync(Guid? ownerUserId = null, CancellationToken ct = default) =>
        Task.FromResult(new AlpacaApi("k", "s", true, handler));
}

internal static class ReferenceTestKit
{
    public static readonly Guid Owner = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public static OperationContext Context(RoutedAlpaca alpaca, out ScriptedAlpacaFactory factory)
    {
        factory = new ScriptedAlpacaFactory(alpaca);
        return new OperationContext
        {
            OwnerId = Owner,
            Services = new ServiceCollection().AddSingleton<AlpacaApiFactory>(factory).BuildServiceProvider(),
            Clock = new Fixtures.FixedClock(Fixtures.FixedClock.MarketOpen), // Fri 2026-10-30 9:31 AM ET
        };
    }

    public static OperationContext Context(RoutedAlpaca alpaca) => Context(alpaca, out _);
}
