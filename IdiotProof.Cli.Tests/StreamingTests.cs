using System.Text;
using System.Text.Json;
using IdiotProof.Operations;
using IdiotProof.Operations.Streaming;

namespace IdiotProof.Cli.Tests;

/// <summary>IP-T324: streaming operations ride /api/stream and print each line as it arrives; --json keeps /api/op.</summary>
public class StreamingTests
{
    private static string Sse(params (string Event, object Data)[] events)
    {
        var sb = new StringBuilder();
        foreach (var (name, data) in events)
            sb.Append("event: ").Append(name).Append('\n').Append("data: ").Append(JsonSerializer.Serialize(data, OperationJson.Options)).Append("\n\n");
        return sb.ToString();
    }

    private static string TwoLinesThenResult() =>
        ": ping\n\n" + Sse(
            (HubWire.StreamLineEvent, new StreamLine(DateTime.UtcNow, "   101  2026-10-30 9:31 AM ET  entry   bought 1 NVDA")),
            (HubWire.StreamLineEvent, new StreamLine(DateTime.UtcNow, "   102  2026-10-30 9:32 AM ET  exit    sold 1 NVDA")),
            (HubWire.StreamResultEvent, FakeHub.Respond(OperationResult.Ok(human: "2 line(s); stopped."), "audit tail", OutputMode.Human)));

    [Test]
    public void StreamingOperations_AreKnownFromTheBuiltInCatalog()
    {
        Assert.That(CliApp.IsStreaming(["audit", "tail", "--follow"]), Is.True);
        Assert.That(CliApp.IsStreaming(["monitor", "tail"]), Is.True);
        Assert.That(CliApp.IsStreaming(["hub", "ping"]), Is.False);
    }

    [Test]
    public async Task StreamingOperation_PrintsEachLine_ThenTheResult()
    {
        using var cli = new CliHarness();
        cli.Hub.OnStream = _ => TwoLinesThenResult();

        Assert.That(await cli.RunAsync("audit", "tail", "--follow"), Is.EqualTo(0));

        var lines = cli.Out.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[0], Does.Contain("bought 1 NVDA"));
        Assert.That(lines[1], Does.Contain("sold 1 NVDA"));
        Assert.That(lines[2], Is.EqualTo("2 line(s); stopped."));
        Assert.That(cli.Hub.StreamRequests.Single().Argv, Is.EqualTo(new[] { "audit", "tail", "--follow" }));
        Assert.That(cli.Hub.Requests, Is.Empty, "a streaming call does not also hit /api/op");
    }

    [Test]
    public async Task Json_StaysOnTheOneEnvelopeEndpoint()
    {
        using var cli = new CliHarness();
        cli.Hub.OnStream = _ => TwoLinesThenResult();

        await cli.RunAsync("audit", "tail", "--follow", "--json");

        Assert.That(cli.Hub.StreamRequests, Is.Empty);
        Assert.That(cli.Hub.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AStreamThatEndsWithoutAResult_IsAFailure()
    {
        using var cli = new CliHarness();
        cli.Hub.OnStream = _ => Sse((HubWire.StreamLineEvent, new StreamLine(DateTime.UtcNow, "one")));

        Assert.That(await cli.RunAsync("audit", "tail", "--follow"), Is.EqualTo(1));
        Assert.That(cli.Out.ToString(), Does.Contain("one"));
        Assert.That(cli.Err.ToString(), Does.Contain("without a result"));
    }

    [Test]
    public async Task AnOlderHubWithoutTheEndpoint_SaysSo()
    {
        using var cli = new CliHarness();
        cli.Hub.OnStream = null;

        Assert.That(await cli.RunAsync("monitor", "tail", "--follow"), Is.EqualTo(1));
        Assert.That(cli.Err.ToString(), Does.Contain("no streaming endpoint"));
    }

    [Test]
    public async Task CtrlC_StopsQuietly_WithExitZero()
    {
        using var hub = new FakeHub { OnStream = _ => TwoLinesThenResult() };
        using var client = new HubClient(new Uri("http://127.0.0.1:5910/"), hub, () => "test-key");
        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        var result = await client.StreamAsync(new HubOperationRequest { Argv = ["audit", "tail", "--follow"] }, line =>
        {
            seen.Add(line.Text);
            cts.Cancel(); // the person presses Ctrl+C after the first line
            return Task.CompletedTask;
        }, cts.Token);

        Assert.That(result.Kind, Is.EqualTo(HubCallKind.Cancelled));
        Assert.That(seen, Has.Count.EqualTo(1));
    }
}
