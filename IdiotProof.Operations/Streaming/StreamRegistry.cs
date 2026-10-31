using System.Collections.Concurrent;
using IdiotProof.Core.Trading;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Streaming;

/// <summary>
/// The Hub's Alpaca websockets (IP-T325/T326/T206), one registry per host: the long-lived
/// trade-updates listener per (owner, account), started on first use and kept until the Hub stops,
/// and the market data sessions <c>market watch</c> has open right now. <c>market stream status</c>
/// reads <see cref="Health"/>. Its collaborators resolve lazily, so a host without Alpaca still starts.
/// </summary>
public sealed class StreamRegistry(IServiceProvider services) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<(Guid Owner, BrokerMode Mode), TradeUpdatesListener> tradeUpdates = new();
    private readonly ConcurrentDictionary<Guid, (AlpacaStreamSession Session, Func<string> Detail, Guid? Owner)> watches = new();
    private readonly SemaphoreSlim startLock = new(1, 1);

    private IStreamSocketFactory Sockets => services.GetService<IStreamSocketFactory>() ?? new ClientWebSocketFactory();

    /// <summary>
    /// The running trade-updates listener for this account, starting it (with the owner's own key
    /// pair) when there is none or the last one was refused. Throws <see cref="AlpacaAccessException"/>
    /// when the owner has no usable keys for <paramref name="mode"/>.
    /// </summary>
    public async Task<TradeUpdatesListener> TradeUpdatesAsync(Guid owner, BrokerMode mode, CancellationToken ct)
    {
        if (tradeUpdates.TryGetValue((owner, mode), out var running) && running.Running) return running;
        await startLock.WaitAsync(ct);
        try
        {
            if (tradeUpdates.TryGetValue((owner, mode), out running))
            {
                if (running.Running) return running;
                // Out of the map before disposing: if starting the replacement throws (keys gone),
                // the next call must not dispose this one a second time.
                tradeUpdates.TryRemove((owner, mode), out _);
                await running.DisposeAsync();
            }
            var keys = await services.GetRequiredService<AlpacaStreamCredentials>().ForOwnerAsync(owner, mode, ct);
            var session = new AlpacaStreamSession(
                $"trade updates · {mode.ToString().ToLowerInvariant()}",
                new Uri(mode == BrokerMode.Live ? AlpacaStreamSession.LiveTradingStream : AlpacaStreamSession.PaperTradingStream),
                keys,
                () => new Dictionary<string, object?> { ["action"] = "listen", ["data"] = new Dictionary<string, object?> { ["streams"] = new[] { "trade_updates" } } },
                Sockets,
                services.GetService<TimeProvider>());
            var listener = new TradeUpdatesListener(owner, mode, session, services.GetRequiredService<FillRecorder>());
            tradeUpdates[(owner, mode)] = listener;
            listener.Start();
            return listener;
        }
        finally { startLock.Release(); }
    }

    /// <summary>Opens a market data session for <c>market watch</c>; it shows in <see cref="Health"/> until the returned handle is disposed.</summary>
    public (AlpacaStreamSession Session, IDisposable Handle) OpenWatch(string name, Uri endpoint, AlpacaStreamKeys keys,
        Func<object> subscribe, bool msgpack, Func<string> detail, Guid? owner = null)
    {
        var session = new AlpacaStreamSession(name, endpoint, keys, subscribe, Sockets, services.GetService<TimeProvider>(), msgpack);
        var id = Guid.NewGuid();
        watches[id] = (session, detail, owner);
        return (session, new Release(() => watches.TryRemove(id, out _)));
    }

    /// <summary>Every stream the Hub has open: trade-updates listeners first, then market watches.</summary>
    public IReadOnlyList<AlpacaStreamHealth> Health() =>
    [
        .. tradeUpdates.Values.OrderBy(l => l.Mode).Select(l => l.Health()),
        .. watches.Values.Select(w => w.Session.Health(1, w.Detail())),
    ];

    /// <summary>Only <paramref name="owner"/>'s streams: one user never sees another's account listeners or tapes.</summary>
    public IReadOnlyList<AlpacaStreamHealth> Health(Guid owner) =>
    [
        .. tradeUpdates.Where(kv => kv.Key.Owner == owner).OrderBy(kv => kv.Key.Mode).Select(kv => kv.Value.Health()),
        .. watches.Values.Where(w => w.Owner == owner).Select(w => w.Session.Health(1, w.Detail())),
    ];

    /// <summary>The trade-updates listeners the Hub is running (for status and tests).</summary>
    public IReadOnlyList<TradeUpdatesListener> TradeUpdateListeners => [.. tradeUpdates.Values];

    public async ValueTask DisposeAsync()
    {
        foreach (var listener in tradeUpdates.Values) await listener.DisposeAsync();
        tradeUpdates.Clear();
    }

    private sealed class Release(Action release) : IDisposable
    {
        private int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) release();
        }
    }
}
