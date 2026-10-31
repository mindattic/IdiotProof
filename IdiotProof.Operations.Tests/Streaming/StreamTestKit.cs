using System.Text;
using System.Threading.Channels;
using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Streaming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Streaming;

/// <summary>
/// A websocket that never touches the network: it answers each frame the client sends with the
/// next scripted reply (by endpoint), can push frames on its own, records everything sent, and
/// blocks once its script is spent until the call is cancelled.
/// </summary>
internal sealed class FakeSocketFactory : IStreamSocketFactory
{
    /// <summary>Frames to deliver right after connecting, per endpoint (the server's greeting and its answers, in order).</summary>
    public Dictionary<string, List<string>> Scripts { get; } = [];

    public List<(Uri Uri, IReadOnlyDictionary<string, string>? Headers)> Connections { get; } = [];

    public List<(Uri Uri, byte[] Data, bool Binary)> Sent { get; } = [];

    /// <summary>Binary frames to deliver (the options feed's msgpack), per endpoint.</summary>
    public Dictionary<string, List<byte[]>> BinaryScripts { get; } = [];

    public Task<IStreamSocket> ConnectAsync(Uri uri, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        lock (Connections) Connections.Add((uri, headers));
        var frames = Channel.CreateUnbounded<StreamFrame>();
        foreach (var text in Scripts.GetValueOrDefault(uri.ToString()) ?? []) frames.Writer.TryWrite(new StreamFrame(Encoding.UTF8.GetBytes(text), false));
        foreach (var bin in BinaryScripts.GetValueOrDefault(uri.ToString()) ?? []) frames.Writer.TryWrite(new StreamFrame(bin, true));
        return Task.FromResult<IStreamSocket>(new Socket(this, uri, frames));
    }

    public IEnumerable<string> SentText(string uri) =>
        Sent.Where(s => s.Uri.ToString() == uri && !s.Binary).Select(s => Encoding.UTF8.GetString(s.Data));

    private sealed class Socket(FakeSocketFactory owner, Uri uri, Channel<StreamFrame> frames) : IStreamSocket
    {
        public Task SendAsync(byte[] data, bool binary, CancellationToken ct)
        {
            lock (owner.Sent) owner.Sent.Add((uri, data, binary));
            return Task.CompletedTask;
        }

        public async Task<StreamFrame?> ReceiveAsync(CancellationToken ct) => await frames.Reader.ReadAsync(ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Fixed keys; the real class's Vault and settings are never touched.</summary>
internal sealed class FixedStreamCredentials() : AlpacaStreamCredentials(null!, null!)
{
    public HashSet<BrokerMode> Refused { get; } = [];

    public override Task<AlpacaStreamKeys> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default) =>
        Refused.Contains(mode)
            ? throw new AlpacaAccessException($"No {mode.ToString().ToLowerInvariant()} Alpaca keys in Vault.")
            : Task.FromResult(new AlpacaStreamKeys($"key-{mode}", "secret"));

    public override Task<AlpacaStreamKeys> ForDataAsync(Guid? ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(new AlpacaStreamKeys("data-key", "secret"));
}

internal sealed class StreamKit
{
    public static readonly Guid Owner = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public FakeSocketFactory Sockets { get; } = new();

    public FixedStreamCredentials Credentials { get; } = new();

    public ServiceProvider Services { get; }

    public StreamKit()
    {
        Services = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"streams-{Guid.NewGuid():N}"))
            .AddSingleton<AuditLogRepository>()
            .AddSingleton<TradeDiaryRepository>()
            .AddSingleton(sp => new FillRecorder(sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                sp.GetRequiredService<AuditLogRepository>(), sp.GetRequiredService<TradeDiaryRepository>()) { MatchGrace = TimeSpan.Zero })
            .AddSingleton<IStreamSocketFactory>(Sockets)
            .AddSingleton<AlpacaStreamCredentials>(Credentials)
            .AddSingleton<StreamRegistry>()
            .BuildServiceProvider();
    }

    public IDbContextFactory<AppDbContext> Db => Services.GetRequiredService<IDbContextFactory<AppDbContext>>();

    public OperationContext Context(IStreamSink? sink = null, CancellationToken ct = default) => new()
    {
        Services = Services,
        OwnerId = Owner,
        Caller = OperationCaller.Cli,
        Sink = sink,
        CancellationToken = ct,
    };
}
