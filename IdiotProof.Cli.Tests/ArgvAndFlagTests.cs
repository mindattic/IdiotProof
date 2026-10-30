using System.Text.Json;
using IdiotProof.Operations;

namespace IdiotProof.Cli.Tests;

/// <summary>IP-T57/T58: argv goes to the Hub with the globals peeled off and turned into request fields.</summary>
public class ArgvAndFlagTests
{
    [Test]
    public async Task Argv_IsSentWithoutGlobals_WhereverTheyAppear()
    {
        using var cli = new CliHarness();
        var user = Guid.NewGuid();
        await cli.RunAsync("--json", "strategy", "sample", "AAPL", "--user", user.ToString(), "--side", "short-sale", "--confirm", "ABC123");

        var sent = cli.Hub.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Argv, Is.EqualTo(new[] { "strategy", "sample", "AAPL", "--side", "short-sale" }));
            Assert.That(sent.Output, Is.EqualTo(OutputMode.Json));
            Assert.That(sent.OwnerId, Is.EqualTo(user));
            Assert.That(sent.ConfirmationCode, Is.EqualTo("ABC123"));
            Assert.That(sent.Caller, Is.EqualTo(OperationCaller.Cli));
        });
    }

    [Test]
    public async Task TokensAfterDoubleDash_AreNeverGlobals()
    {
        using var cli = new CliHarness();
        await cli.RunAsync("strategy", "sample", "--", "--json", "--color");
        var sent = cli.Hub.Requests.Single();
        Assert.That(sent.Argv, Is.EqualTo(new[] { "strategy", "sample", "--", "--json", "--color" }));
        Assert.That(sent.Output, Is.EqualTo(OutputMode.Human));
    }

    [Test]
    public async Task Json_PrintsTheHubText_ToStdout()
    {
        using var cli = new CliHarness();
        cli.Hub.OnOp = r => FakeHub.Respond(OperationResult.Ok(new { Pong = true }, "pong"), "hub ping", r.Output);
        Assert.That(await cli.RunAsync("hub", "ping", "--json"), Is.EqualTo(0));
        using var doc = JsonDocument.Parse(cli.Out.ToString());
        Assert.That(doc.RootElement.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(doc.RootElement.GetProperty("data").GetProperty("pong").GetBoolean(), Is.True);
    }

    [Test]
    public async Task Color_OffWhenRedirected()
    {
        using var cli = new CliHarness(outputRedirected: true);
        await cli.RunAsync("hub", "ping");
        Assert.That(cli.Hub.Requests.Single().Color, Is.False);
        Assert.That(cli.Hub.Requests.Single().Width, Is.Null);
    }

    [Test]
    public async Task Color_OnAtATerminal_WithWidth()
    {
        using var cli = new CliHarness(outputRedirected: false);
        await cli.RunAsync("hub", "ping");
        Assert.That(cli.Hub.Requests.Single().Color, Is.True);
        Assert.That(cli.Hub.Requests.Single().Width, Is.EqualTo(120));
    }

    [Test]
    public async Task Color_NoColorEnv_TurnsItOff()
    {
        using var cli = new CliHarness(outputRedirected: false, envVars: new() { ["NO_COLOR"] = "1" });
        await cli.RunAsync("hub", "ping");
        Assert.That(cli.Hub.Requests.Single().Color, Is.False);
    }

    [Test]
    public async Task Color_FlagForcesIt_NoColorFlagWins()
    {
        using var forced = new CliHarness(outputRedirected: true);
        await forced.RunAsync("hub", "ping", "--color");
        Assert.That(forced.Hub.Requests.Single().Color, Is.True);
        Assert.That(forced.Hub.Requests.Single().Argv, Is.EqualTo(new[] { "hub", "ping" }));

        using var off = new CliHarness(outputRedirected: false);
        await off.RunAsync("hub", "ping", "--color", "--no-color");
        Assert.That(off.Hub.Requests.Single().Color, Is.False);

        using var json = new CliHarness(outputRedirected: false);
        await json.RunAsync("hub", "ping", "--json");
        Assert.That(json.Hub.Requests.Single().Color, Is.False);
    }

    [Test]
    public async Task HubFlag_OverridesTheUrl()
    {
        using var cli = new CliHarness();
        await cli.RunAsync("version", "--json", "--hub", "http://127.0.0.1:5999/");
        Assert.That(cli.Out.ToString(), Does.Contain("http://127.0.0.1:5999/"));
    }

    [Test]
    public async Task HubUrl_ComesFromEnv_ThenPort_ThenDefault()
    {
        using var byUrl = new CliHarness(envVars: new() { [HubWire.UrlEnvVar] = "http://127.0.0.1:5919/" });
        await byUrl.RunAsync("version", "--json");
        Assert.That(byUrl.Out.ToString(), Does.Contain("5919"));

        using var byPort = new CliHarness(envVars: new() { [HubWire.PortEnvVar] = "5920" });
        await byPort.RunAsync("version", "--json");
        Assert.That(byPort.Out.ToString(), Does.Contain("5920"));

        using var byDefault = new CliHarness();
        await byDefault.RunAsync("version", "--json");
        Assert.That(byDefault.Out.ToString(), Does.Contain($":{HubWire.DefaultPort}/"));
    }

    [Test]
    public void StripColorFlag_LeavesEverythingAfterDoubleDash()
    {
        var (tokens, force) = CliApp.StripColorFlag(["a", "--color", "b", "--", "--color"]);
        Assert.That(force, Is.True);
        Assert.That(tokens, Is.EqualTo(new[] { "a", "b", "--", "--color" }));
    }

    [Test]
    public void Quote_OnlyWhenNeeded()
    {
        Assert.That(CliApp.Quote("AAPL"), Is.EqualTo("AAPL"));
        Assert.That(CliApp.Quote("Gap and go"), Is.EqualTo("\"Gap and go\""));
        Assert.That(CliApp.Quote("say \"hi\""), Is.EqualTo("\"say \\\"hi\\\"\""));
        Assert.That(CliApp.Quote(""), Is.EqualTo("\"\""));
    }

    [Test]
    public async Task BareNoun_ListsItsVerbs_AsUsage()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("hub"), Is.EqualTo(CliExitCodes.Usage));
        Assert.That(cli.Err.ToString(), Does.Contain("needs a verb").And.Contain("ping"));
    }
}
