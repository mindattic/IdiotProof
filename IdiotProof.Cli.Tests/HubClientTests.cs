using System.Text.Json;
using IdiotProof.Operations;

namespace IdiotProof.Cli.Tests;

/// <summary>IP-T69: the Hub client fails closed, sends the key, explains 401s, passes results through, hints confirmations.</summary>
public class HubClientTests
{
    [Test]
    public async Task Unreachable_ExitsOne_WithStartHint()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("hub", "ping"), Is.EqualTo(1));
        Assert.That(cli.Err.ToString(), Does.Contain("Hub not running").And.Contain("idiotproof hub start"));
        Assert.That(cli.Out.ToString(), Is.Empty);
    }

    [Test]
    public async Task Unreachable_Json_PrintsAnErrorEnvelope()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("hub", "ping", "--json"), Is.EqualTo(1));
        using var doc = JsonDocument.Parse(cli.Out.ToString());
        Assert.That(doc.RootElement.GetProperty("exit_code").GetInt32(), Is.EqualTo(1));
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("hub-not-running"));
    }

    [Test]
    public async Task SendsTheKey()
    {
        using var cli = new CliHarness(key: "test-key");
        await cli.RunAsync("hub", "ping");
        Assert.That(cli.Hub.KeysSeen.Single(), Is.EqualTo("test-key"));
    }

    [Test]
    public async Task WrongKey_401_ExplainsTheMismatch()
    {
        using var cli = new CliHarness(key: "other-key");
        Assert.That(await cli.RunAsync("hub", "ping"), Is.EqualTo(1));
        Assert.That(cli.Err.ToString(), Does.Contain("rejected").And.Contain(HubWire.KeyEnvVar));
    }

    [Test]
    public async Task NoKey_ExplainsWhere_ItComesFrom_AndSendsNothing()
    {
        using var cli = new CliHarness(key: null);
        Assert.That(await cli.RunAsync("hub", "ping"), Is.EqualTo(1));
        Assert.That(cli.Err.ToString(), Does.Contain("No Hub key").And.Contain("first time it starts"));
        Assert.That(cli.Hub.KeysSeen, Is.Empty);
    }

    [Test]
    public async Task Success_PrintsTheText()
    {
        using var cli = new CliHarness();
        cli.Hub.OnOp = _ => FakeHub.Ok("pong — 2026-10-30 9:31 AM ET");
        Assert.That(await cli.RunAsync("hub", "ping"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString().TrimEnd(), Is.EqualTo("pong — 2026-10-30 9:31 AM ET"));
    }

    [Test]
    public async Task UsageError_GoesToStderr()
    {
        using var cli = new CliHarness();
        cli.Hub.OnOp = r => FakeHub.Respond(OperationResult.Usage("Unknown operation 'strategy lsit'. Did you mean 'strategy list'?"), "strategy lsit", r.Output);
        Assert.That(await cli.RunAsync("strategy", "lsit"), Is.EqualTo(2));
        Assert.That(cli.Err.ToString(), Does.Contain("Did you mean"));
        Assert.That(cli.Out.ToString(), Is.Empty);
    }

    [Test]
    public async Task Blocked_PassesThrough()
    {
        using var cli = new CliHarness();
        cli.Hub.OnOp = r => FakeHub.Respond(OperationResult.Blocked("Market closed."), "trade buy", r.Output);
        Assert.That(await cli.RunAsync("trade", "buy", "AAPL"), Is.EqualTo(3));
        Assert.That(cli.Out.ToString(), Does.Contain("Blocked: Market closed."));
    }

    [Test]
    public async Task ConfirmationRequired_PrintsPreview_AndTheConfirmCommand()
    {
        using var cli = new CliHarness();
        var user = Guid.NewGuid();
        cli.Hub.OnOp = r => FakeHub.Respond(
            OperationResult.NeedsConfirmation("K7Q2ZP", "BUY 1 AAPL @ market (Live)", DateTime.UtcNow.AddMinutes(2)), "trade buy", r.Output);

        Assert.That(await cli.RunAsync("trade", "buy", "AAPL", "--note", "Gap and go", "--user", user.ToString()), Is.EqualTo(4));
        var text = cli.Out.ToString();
        Assert.That(text, Does.Contain("BUY 1 AAPL @ market (Live)"));
        Assert.That(text, Does.Contain($"To execute, run: idiotproof trade buy AAPL --note \"Gap and go\" --user {user:D} --confirm K7Q2ZP"));
    }

    [Test]
    public async Task ConfirmationRequired_Json_HasNoHintLine()
    {
        using var cli = new CliHarness();
        cli.Hub.OnOp = r => FakeHub.Respond(
            OperationResult.NeedsConfirmation("K7Q2ZP", "preview", DateTime.UtcNow.AddMinutes(2)), "trade buy", r.Output);
        Assert.That(await cli.RunAsync("trade", "buy", "--json"), Is.EqualTo(4));
        using var doc = JsonDocument.Parse(cli.Out.ToString());
        Assert.That(doc.RootElement.GetProperty("confirmation").GetProperty("code").GetString(), Is.EqualTo("K7Q2ZP"));
    }

    [Test]
    public async Task SlowCall_SaysItIsStillRunning()
    {
        using var cli = new CliHarness();
        cli.Hub.OpDelay = TimeSpan.FromMilliseconds(600);
        Assert.That(await cli.RunAsync("hub", "ping"), Is.EqualTo(0));
        Assert.That(cli.Err.ToString(), Does.Contain("still running on the Hub"));
    }

    [Test]
    public async Task FastCall_SaysNothingExtra()
    {
        using var cli = new CliHarness();
        await cli.RunAsync("hub", "ping");
        Assert.That(cli.Err.ToString(), Is.Empty);
    }

    [Test]
    public async Task Cancel_ExitsOne()
    {
        using var hub = new FakeHub { OpDelay = TimeSpan.FromSeconds(30) };
        using var client = new HubClient(new Uri("http://127.0.0.1:5999/"), hub, () => "test-key") { SlowNotice = TimeSpan.FromSeconds(10) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var result = await client.RunAsync(new HubOperationRequest { Argv = ["hub", "ping"] }, TextWriter.Null, cts.Token);
        Assert.That(result.Kind, Is.EqualTo(HubCallKind.Cancelled));
        Assert.That(result.ExitCode, Is.EqualTo(1));
    }

    [Test]
    public async Task Timeout_ExitsOne()
    {
        using var hub = new FakeHub { OpDelay = TimeSpan.FromSeconds(30) };
        using var client = new HubClient(new Uri("http://127.0.0.1:5999/"), hub, () => "test-key") { CallTimeout = TimeSpan.FromMilliseconds(200) };
        var result = await client.RunAsync(new HubOperationRequest { Argv = ["hub", "ping"] }, TextWriter.Null);
        Assert.That(result.Kind, Is.EqualTo(HubCallKind.TimedOut));
    }

    [Test]
    public async Task RequestBody_UsesTheHubWireContract()
    {
        using var cli = new CliHarness();
        await cli.RunAsync("hub", "ping", "--json");
        // FakeHub deserializes with OperationJson.Options exactly like the real Hub; kebab enum survived.
        Assert.That(cli.Hub.Requests.Single().Output, Is.EqualTo(OutputMode.Json));
    }
}
