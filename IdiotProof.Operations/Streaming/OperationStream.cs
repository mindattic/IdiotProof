using System.Text;

namespace IdiotProof.Operations.Streaming;

/// <summary>
/// One line a long-running operation pushes while it runs (IP-T324): the rendered text a person
/// reads and, optionally, the structured payload an agent reads. <see cref="AtUtc"/> is UTC on the
/// wire; <see cref="AtEt"/> is the same instant as ET for display.
/// </summary>
public sealed record StreamLine(DateTime AtUtc, string Text, object? Data = null)
{
    /// <summary><c>9:31:07 AM ET</c>.</summary>
    public string AtEt => EasternClock.ToEastern(AtUtc).ToString("h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture) + " ET";
}

/// <summary>
/// Where pushed lines go while an operation runs: the Hub's server-sent-events response
/// (<see cref="HubWire.StreamPath"/>) or a test's list. Implementations must accept calls from
/// several threads (a websocket callback and a poll loop may emit at once).
/// </summary>
public interface IStreamSink
{
    ValueTask WriteAsync(StreamLine line, CancellationToken ct);
}

/// <summary>
/// Marks an operation that pushes lines through <see cref="OperationStream"/>. The CLI calls such
/// an operation on the Hub's streaming endpoint so its lines print as they happen (Ctrl+C stops
/// it); MCP and <c>--json</c> callers get a bounded window of lines in one result instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StreamingAttribute : Attribute
{
    /// <summary>True when <paramref name="operationType"/> carries the attribute.</summary>
    public static bool IsOn(Type operationType) =>
        operationType.GetCustomAttributes(typeof(StreamingAttribute), inherit: false).Length > 0;
}

/// <summary>Why an <see cref="OperationStream"/> ended.</summary>
public enum StreamEnd
{
    /// <summary>Still running.</summary>
    Running,

    /// <summary>The caller stopped it (Ctrl+C, a closed connection, a cancelled MCP call).</summary>
    Stopped,

    /// <summary>The time window (<c>--seconds</c>, or the bounded default) ran out.</summary>
    Window,

    /// <summary>The line cap (<c>--max</c>) was reached.</summary>
    Max,

    /// <summary>The operation finished on its own (nothing left to watch).</summary>
    Done,
}

/// <summary>
/// The operation-side streaming API (IP-T324). An operation opens one, calls
/// <see cref="EmitAsync"/> for each line, runs its watch loop on <see cref="Token"/>, and returns
/// <see cref="Finish"/>. Two modes, picked by the caller, not the operation:
/// <list type="bullet">
/// <item><b>Live</b> (the CLI on <c>/api/stream</c>): every line goes to the
/// <see cref="OperationContext.Sink"/> as it happens; the watch runs until Ctrl+C, unless the caller
/// gave a time or line limit. The final result carries only a summary.</item>
/// <item><b>Bounded window</b> (MCP, <c>--json</c>, <c>/api/op</c>): lines are collected and the watch
/// ends after <see cref="DefaultWindow"/> (or the caller's seconds, at most <see cref="MaxWindow"/>)
/// or <see cref="DefaultMaxLines"/> lines; the result carries every line.</item>
/// </list>
/// </summary>
public sealed class OperationStream : IDisposable
{
    /// <summary>How long a bounded window watches when the caller names no time.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(15);

    /// <summary>The longest a bounded window may watch: one request/response has to come back.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromSeconds(300);

    /// <summary>Lines a bounded window collects when the caller names no cap.</summary>
    public const int DefaultMaxLines = 200;

    /// <summary>Lines a live stream keeps for its final result (the rest were already printed).</summary>
    public const int LiveTail = 50;

    private readonly OperationContext ctx;
    private readonly CancellationTokenSource stop;
    private readonly List<StreamLine> lines = [];
    private readonly int? maxLines;
    private readonly object gate = new();
    private int count;
    private StreamEnd end = StreamEnd.Running;

    private OperationStream(OperationContext ctx, TimeSpan? window, int? maxLines)
    {
        this.ctx = ctx;
        this.maxLines = maxLines;
        Window = window;
        stop = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        if (window is { } w) stop.CancelAfter(w);
    }

    /// <summary>
    /// Opens a stream for this call. <paramref name="seconds"/> and <paramref name="max"/> are the
    /// caller's limits (null = none named): live streams honour them as given (none = until
    /// stopped); bounded windows fill in <see cref="DefaultWindow"/> / <see cref="DefaultMaxLines"/>
    /// and cap the time at <see cref="MaxWindow"/>.
    /// </summary>
    public static OperationStream Open(OperationContext ctx, int? seconds = null, int? max = null)
    {
        if (ctx.Sink is not null)
            return new OperationStream(ctx, seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : null, max is > 0 ? max : null);
        var window = seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : DefaultWindow;
        if (window > MaxWindow) window = MaxWindow;
        return new OperationStream(ctx, window, max is > 0 ? max : DefaultMaxLines);
    }

    /// <summary>True when lines are pushed to the caller as they happen.</summary>
    public bool Live => ctx.Sink is not null;

    /// <summary>The time limit in force, or null for "until stopped".</summary>
    public TimeSpan? Window { get; }

    /// <summary>Cancelled when the caller stops, the window ends, the line cap is hit, or <see cref="Complete"/> is called.</summary>
    public CancellationToken Token => stop.Token;

    /// <summary>Lines emitted so far.</summary>
    public int Count => Volatile.Read(ref count);

    /// <summary>Why the stream ended (<see cref="StreamEnd.Running"/> until it has).</summary>
    public StreamEnd End
    {
        get
        {
            lock (gate)
            {
                if (end != StreamEnd.Running) return end;
                if (ctx.CancellationToken.IsCancellationRequested) return StreamEnd.Stopped;
                return stop.IsCancellationRequested ? StreamEnd.Window : StreamEnd.Running;
            }
        }
    }

    /// <summary>The lines kept for the result: every line in a bounded window, the last <see cref="LiveTail"/> when live.</summary>
    public IReadOnlyList<StreamLine> Lines
    {
        get { lock (gate) return [.. lines]; }
    }

    /// <summary>
    /// Pushes one line (live) or collects it (bounded). Ignored once the stream has ended; reaching
    /// the line cap ends it.
    /// </summary>
    public async ValueTask EmitAsync(string text, object? data = null)
    {
        StreamLine line;
        lock (gate)
        {
            if (end != StreamEnd.Running || stop.IsCancellationRequested) return;
            line = new StreamLine(ctx.UtcNow, text, data);
            lines.Add(line);
            if (Live && lines.Count > LiveTail) lines.RemoveAt(0);
            count++;
        }
        if (ctx.Sink is { } sink)
        {
            try { await sink.WriteAsync(line, stop.Token); }
            catch (OperationCanceledException) { /* the caller went away; End reports why */ }
        }
        if (maxLines is { } cap && Count >= cap) Close(StreamEnd.Max);
    }

    /// <summary>Ends the stream because the operation has nothing more to watch.</summary>
    public void Complete() => Close(StreamEnd.Done);

    private void Close(StreamEnd why)
    {
        lock (gate)
        {
            if (end != StreamEnd.Running) return;
            end = why;
        }
        try { stop.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Runs <paramref name="watch"/> until it returns or the stream ends; the cancellation that ends
    /// the stream is not an error. Any other exception propagates.
    /// </summary>
    public async Task RunAsync(Func<CancellationToken, Task> watch)
    {
        try { await watch(Token); }
        catch (OperationCanceledException) when (Token.IsCancellationRequested) { }
        if (End == StreamEnd.Running) Complete();
    }

    /// <summary>
    /// The operation's result: live streams return a one-line summary (the lines were already
    /// printed); bounded windows return <paramref name="header"/> followed by every line.
    /// <see cref="OperationResult.Data"/> carries <paramref name="data"/> plus the stream's lines.
    /// </summary>
    public OperationResult Finish(string header, object? data = null)
    {
        var why = End == StreamEnd.Running ? StreamEnd.Done : End;
        var kept = Lines;
        var summary = $"{Count} line(s); {Describe(why)}.";
        var payload = new StreamResult(Live, why, Count, Window is { } w ? (int)w.TotalSeconds : null, maxLines, kept, data);
        if (Live) return OperationResult.Ok(payload, summary);

        var text = new StringBuilder(header);
        if (kept.Count == 0) text.Append("\n  (nothing in this window)");
        foreach (var line in kept) text.Append('\n').Append(line.Text);
        text.Append("\n\n").Append(summary);
        return OperationResult.Ok(payload, text.ToString());
    }

    private string Describe(StreamEnd why) => why switch
    {
        StreamEnd.Stopped => "stopped",
        StreamEnd.Window => Live
            ? $"ran {Window?.TotalSeconds:0}s"
            : $"a {Window?.TotalSeconds:0}s window (the CLI streams live until Ctrl+C; --seconds widens the window up to {MaxWindow.TotalSeconds:0}s)",
        StreamEnd.Max => $"stopped at {maxLines} lines",
        _ => "done",
    };

    public void Dispose() => stop.Dispose();

    /// <summary>The <c>data</c> of a streamed result.</summary>
    public sealed record StreamResult(bool Live, StreamEnd Ended, int Lines, int? WindowSeconds, int? MaxLines,
        IReadOnlyList<StreamLine> Kept, object? Detail);
}
