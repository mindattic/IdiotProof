using System.Net;
using System.Text;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Account;

/// <summary>
/// Replies to Alpaca requests by route ("GET /v2/account" matches that method and path, query
/// ignored); each route's replies are served in order, the last one repeating.
/// Records every request with its body. Nothing reaches the network.
/// </summary>
internal sealed class RouteHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> routes = [];

    public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

    public RouteHandler On(string route, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (!routes.TryGetValue(route, out var queue)) routes[route] = queue = new();
        queue.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        lock (Requests) Requests.Add((request.Method, request.RequestUri!, body));
        var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        if (!routes.TryGetValue(key, out var queue)) throw new InvalidOperationException($"No scripted reply for {key}");
        (HttpStatusCode Status, string Body) reply;
        lock (queue) reply = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        return new HttpResponseMessage(reply.Status) { Content = new StringContent(reply.Body, Encoding.UTF8, "application/json") };
    }
}

/// <summary>An <see cref="AlpacaApiFactory"/> that hands back clients over scripted handlers, one per mode.</summary>
internal sealed class ScriptedAlpacaFactory() : AlpacaApiFactory(null!, null!, null!)
{
    public RouteHandler Paper { get; } = new();

    public RouteHandler Live { get; } = new();

    /// <summary>Modes that refuse access, as a missing-keys owner would.</summary>
    public HashSet<BrokerMode> Refused { get; } = [];

    public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default)
    {
        if (mode == BrokerMode.Sandbox) throw new AlpacaAccessException("Sandbox is not Alpaca.");
        if (Refused.Contains(mode)) throw new AlpacaAccessException($"No {mode.ToString().ToLowerInvariant()} Alpaca keys configured.");
        var isPaper = mode == BrokerMode.Paper;
        return Task.FromResult(new AlpacaApi("k", "s", isPaper, isPaper ? Paper : Live));
    }

    public override Task<AlpacaApi> ForDataAsync(Guid? ownerUserId = null, CancellationToken ct = default) =>
        Task.FromResult(new AlpacaApi("k", "s", true, Paper));
}

internal static class AccountTestKit
{
    public static readonly Guid Owner = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>A context for <see cref="Owner"/> with <paramref name="factory"/> (and any extra services) registered.</summary>
    public static OperationContext Context(ScriptedAlpacaFactory factory, Action<IServiceCollection>? more = null,
        DateTimeOffset? now = null, string? confirm = null, int? width = 120)
    {
        var services = new ServiceCollection().AddSingleton<AlpacaApiFactory>(factory);
        more?.Invoke(services);
        return new OperationContext
        {
            OwnerId = Owner,
            Services = services.BuildServiceProvider(),
            Clock = new Fixtures.FixedClock(now ?? Fixtures.FixedClock.MarketOpen),
            ConfirmationCode = confirm,
            Width = width,
        };
    }
}
