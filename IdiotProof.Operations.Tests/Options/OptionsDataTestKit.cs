using System.Net;
using System.Text;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>
/// A scripted Alpaca for the options read operations: each request is answered by the first route
/// whose text appears in the URL (register specific routes first); anything unrouted is a 404.
/// Records every URL so tests can assert what was asked.
/// </summary>
internal sealed class OptionsDataAlpaca : HttpMessageHandler
{
    private readonly List<(string Match, HttpStatusCode Status, string Body)> routes = [];

    public List<string> Urls { get; } = [];

    public OptionsDataAlpaca On(string match, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        routes.Add((match, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
        Urls.Add(url);
        if (request.Method != HttpMethod.Get)
            throw new InvalidOperationException($"Read operations must only GET; got {request.Method} {url}.");
        var hit = routes.FirstOrDefault(r => url.Contains(r.Match, StringComparison.Ordinal));
        var (status, body) = hit.Match is null ? (HttpStatusCode.NotFound, """{"code":40410000,"message":"no route"}""") : (hit.Status, hit.Body);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

/// <summary>Hands back AlpacaApi clients over the scripted handlers; no live handler means live keys are missing.</summary>
internal sealed class OptionsDataFactory(OptionsDataAlpaca paper, OptionsDataAlpaca? live = null) : AlpacaApiFactory(null!, null!, null!)
{
    public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default) =>
        mode == BrokerMode.Live && live is null
            ? throw new AlpacaAccessException("No live Alpaca keys configured.")
            : Task.FromResult(new AlpacaApi("k", "s", mode == BrokerMode.Paper, mode == BrokerMode.Paper ? paper : live));

    public override Task<AlpacaApi> ForDataAsync(Guid? ownerUserId = null, CancellationToken ct = default) =>
        Task.FromResult(new AlpacaApi("k", "s", true, paper));
}

internal static class OptionsDataKit
{
    /// <summary>Friday 2026-10-30 2:00 PM ET.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);

    public static OperationContext Context(OptionsDataAlpaca paper, OptionsDataAlpaca? live = null) => new()
    {
        Services = new ServiceCollection().AddSingleton<AlpacaApiFactory>(new OptionsDataFactory(paper, live)).BuildServiceProvider(),
        OwnerId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Clock = new Fixtures.FixedClock(Now),
    };

    /// <summary>A snapshot for AAPL 2026-11-20 250 C; <paramref name="greeks"/> false drops Alpaca's IV and Greeks (the 0DTE case).</summary>
    public static string Snapshot(string occ = "AAPL261120C00250000", bool greeks = true) => """
        {"snapshots":{"OCC":{
          "latestQuote":{"t":"2026-10-30T17:59:00Z","bp":5.10,"bs":12,"bx":"C","ap":5.30,"as":8,"ax":"N","c":"A"},
          "latestTrade":{"t":"2026-10-30T17:58:30Z","p":5.20,"s":3,"x":"C","c":"I"},
          "dailyBar":{"o":4.8,"h":5.5,"l":4.7,"c":5.2,"v":1200}GREEKS
        }}}
        """.Replace("OCC", occ).Replace("GREEKS", greeks
            ? ""","impliedVolatility":0.3,"greeks":{"delta":0.52,"gamma":0.03,"theta":-0.11,"vega":0.28,"rho":0.09}"""
            : "");

    /// <summary>The result's payload as the wire sees it (snake_case).</summary>
    public static System.Text.Json.JsonElement Data(OperationResult result) =>
        System.Text.Json.JsonSerializer.SerializeToElement(result.Data, OperationJson.Options);

    /// <summary>Latest AAPL trade at $250.</summary>
    public const string Spot = """{"symbol":"AAPL","trade":{"t":"2026-10-30T17:59:59Z","p":250.00,"s":100}}""";
}
