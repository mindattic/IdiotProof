using System.Text;
using IdiotProof.Operations.Audit;
using IdiotProof.Operations.Streaming;
using IdiotProof.Operations.Strategies;
using IdiotProof.Operations.Tests.Audit;
using IdiotProof.Operations.Tests.Diary;
using IdiotProof.Operations.Tests.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Streaming;

/// <summary>A sink that records every pushed line, and can stop the call after N lines (a person pressing Ctrl+C).</summary>
internal sealed class ListSink(CancellationTokenSource? stopAfter = null, int stopAt = int.MaxValue) : IStreamSink
{
    public List<StreamLine> Lines { get; } = [];

    public ValueTask WriteAsync(StreamLine line, CancellationToken ct)
    {
        lock (Lines)
        {
            Lines.Add(line);
            if (Lines.Count >= stopAt) stopAfter?.Cancel();
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>IP-T324: the operation-side streaming API, the SSE writer, and the streamed operations.</summary>
public class OperationStreamTests
{
    private static OperationContext Ctx(IStreamSink? sink = null, CancellationToken ct = default) => new()
    {
        Services = new ServiceCollection().BuildServiceProvider(),
        Sink = sink,
        CancellationToken = ct,
    };

    [Test]
    public async Task Bounded_CollectsLines_AndEndsAtTheCap()
    {
        using var stream = OperationStream.Open(Ctx(), seconds: 30, max: 3);
        Assert.That(stream.Live, Is.False);
        for (var i = 1; i <= 5; i++) await stream.EmitAsync($"line {i}");

        Assert.That(stream.End, Is.EqualTo(StreamEnd.Max));
        Assert.That(stream.Token.IsCancellationRequested, Is.True);
        var result = stream.Finish("Header");
        Assert.That(result.Human, Does.StartWith("Header\nline 1\nline 2\nline 3").And.Contain("3 line(s); stopped at 3 lines."));
        Assert.That(result.Human, Does.Not.Contain("line 4"));
    }

    [Test]
    public async Task Bounded_DefaultsToAWindow_AndCapsWhatTheCallerAsks()
    {
        using var quick = OperationStream.Open(Ctx());
        Assert.That(quick.Window, Is.EqualTo(OperationStream.DefaultWindow));
        using var huge = OperationStream.Open(Ctx(), seconds: 100_000);
        Assert.That(huge.Window, Is.EqualTo(OperationStream.MaxWindow));

        using var tiny = OperationStream.Open(Ctx(), seconds: 1);
        await tiny.RunAsync(ct => Task.Delay(Timeout.Infinite, ct));
        Assert.That(tiny.End, Is.EqualTo(StreamEnd.Window));
        Assert.That(tiny.Finish("H").Human, Does.Contain("(nothing in this window)").And.Contain("a 1s window"));
    }

    [Test]
    public async Task Live_PushesEveryLine_UntilStopped_AndReturnsOnlyASummary()
    {
        using var cts = new CancellationTokenSource();
        var sink = new ListSink(cts, stopAt: 2);
        using var stream = OperationStream.Open(Ctx(sink, cts.Token));
        Assert.That(stream.Live, Is.True);
        Assert.That(stream.Window, Is.Null, "a live stream runs until stopped");

        await stream.RunAsync(async ct =>
        {
            for (var i = 1; ; i++)
            {
                await stream.EmitAsync($"tick {i}", new { i });
                await Task.Delay(10, ct);
            }
        });

        Assert.That(sink.Lines.Select(l => l.Text), Is.EqualTo(new[] { "tick 1", "tick 2" }));
        Assert.That(stream.End, Is.EqualTo(StreamEnd.Stopped));
        var result = stream.Finish("ignored when live");
        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Is.EqualTo("2 line(s); stopped."));
    }

    [Test]
    public async Task Sse_WritesNamedEvents_OneJsonLineEach()
    {
        using var body = new MemoryStream();
        var sink = new SseStreamSink(body);
        await sink.WriteAsync(new StreamLine(new DateTime(2026, 10, 30, 13, 31, 7, DateTimeKind.Utc), "hello\nworld"), CancellationToken.None);
        await sink.WriteHeartbeatAsync(CancellationToken.None);
        await sink.WriteEventAsync(HubWire.StreamResultEvent, "{\"exit_code\":0}", CancellationToken.None);

        var text = Encoding.UTF8.GetString(body.ToArray());
        var events = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(events, Has.Length.EqualTo(3));
        Assert.That(events[0], Does.StartWith("event: line\ndata: {").And.Contain("\"text\":\"hello\\nworld\"").And.Contain("\"at_et\":\"9:31:07 AM ET\""));
        Assert.That(events[1], Is.EqualTo(": ping"));
        Assert.That(events[2], Is.EqualTo("event: result\ndata: {\"exit_code\":0}"));
    }

    [Test]
    public void TheLongRunningOperations_AreMarkedStreaming()
    {
        var catalog = OperationCatalog.FromAssemblies(typeof(OperationCatalog).Assembly);
        foreach (var name in new[] { "audit tail", "monitor tail", "strategy progress" })
            Assert.That(StreamingAttribute.IsOn(catalog.Find(name)!.OperationType), Is.True, name);
        Assert.That(StreamingAttribute.IsOn(catalog.Find("audit list")!.OperationType), Is.False);
    }

    [Test]
    public async Task AuditTailFollow_Live_PushesTheStartingRows_ThenEachNewRow()
    {
        var kit = new DiaryKit();
        await kit.AddAsync(AuditOperationsTests.Row(kit.Owner, "system", "before", DateTime.UtcNow));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var sink = new ListSink(cts, stopAt: 4); // before, the banner, new one, new two
        var ctx = new OperationContext
        {
            Services = kit.Context().Services, OwnerId = kit.Owner, Caller = OperationCaller.Cli,
            Sink = sink, CancellationToken = cts.Token,
        };

        var tail = new AuditTailOperation().ExecuteAsync(new AuditTailArgs { Follow = true, Lines = 1 }, ctx);
        await Task.Delay(300);
        await kit.AddAsync(AuditOperationsTests.Row(Guid.NewGuid(), "entry", "someone else's", DateTime.UtcNow));
        await kit.AddAsync(AuditOperationsTests.Row(kit.Owner, "entry", "new one", DateTime.UtcNow));
        await kit.AddAsync(AuditOperationsTests.Row(kit.Owner, "exit", "new two", DateTime.UtcNow));
        var result = await tail;

        var texts = sink.Lines.Select(l => l.Text).ToList();
        Assert.That(texts[0], Does.Contain("before"));
        Assert.That(texts[1], Does.Contain("following the audit log"));
        Assert.That(texts.Skip(2), Has.Some.Contains("new one").And.Some.Contains("new two"));
        Assert.That(texts, Has.None.Contains("someone else's"));
        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.EndWith("stopped."));
    }

    [Test]
    public async Task StrategyProgressWatch_Live_StreamsEachChange_AndEndsWhenEverythingPasses()
    {
        var kit = new StrategyKit();
        var s = await kit.AddAsync("NVDA", active: true);
        await kit.AddProgressAsync(s.Id, 0, 1, "IsAboveVwap", 99m);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var sink = new ListSink();
        var ctx = new OperationContext { Services = kit.Context().Services, OwnerId = kit.Owner, Sink = sink, CancellationToken = cts.Token };

        var watch = new StrategyProgressOperation().ExecuteAsync(new StrategyProgressArgs { Strategy = "NVDA", Watch = true, Every = 1 }, ctx);
        await Task.Delay(300);
        await kit.AddProgressAsync(s.Id, 1, 1, null, 101m);
        var result = await watch;

        Assert.That(result.IsOk, Is.True, result.Message);
        var texts = sink.Lines.Select(l => l.Text).ToList();
        Assert.That(texts[0], Does.Contain("watching every 1s"));
        Assert.That(texts[1], Does.Contain("0/1").And.Contain("waiting on IsAboveVwap"));
        Assert.That(texts[^1], Does.Contain("1/1").And.Contain("ALL PASS"));
        Assert.That(result.Human, Does.EndWith("done."));
    }
}
