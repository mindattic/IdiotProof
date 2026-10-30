using System.Text.Json;

namespace IdiotProof.Cli.Tests;

/// <summary>IP-T64/T83: bootstrap, version and Hub lifecycle commands run in the CLI.</summary>
public class LocalCommandTests
{
    [Test]
    public async Task HubStatus_Running()
    {
        using var cli = new CliHarness();
        Assert.That(await cli.RunAsync("hub", "status"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("Hub running").And.Contain("9.9.9").And.Contain(" ET").And.Contain("not hosted"));
        Assert.That(cli.Hub.Requests, Is.Empty);
    }

    [Test]
    public async Task HubStatus_NotRunning_ExitsOne()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("hub", "status"), Is.EqualTo(1));
        Assert.That(cli.Err.ToString(), Does.Contain("Hub not running"));
    }

    [Test]
    public async Task HubVersion()
    {
        using var cli = new CliHarness();
        Assert.That(await cli.RunAsync("hub", "version"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString().Trim(), Is.EqualTo("9.9.9"));
    }

    [Test]
    public async Task Version_WorksWithHubDown()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("version"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("idiotproof ").And.Contain("not running"));
    }

    [Test]
    public async Task Bootstrap_WorksOffline()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("agent", "bootstrap"), Is.EqualTo(0));
        var text = cli.Out.ToString();
        Assert.That(text, Does.Contain("not running").And.Contain("IDIOTPROOF_PROTOCOL.md").And.Contain("CLI_COMMANDS.md").And.Contain("MCP_TOOLS.md"));
        Assert.That(text, Does.Contain("--confirm").And.Contain("Eastern (ET)"));
    }

    [Test]
    public async Task Bootstrap_Json()
    {
        using var cli = new CliHarness();
        cli.Hub.CatalogJson = SampleCatalog.Hub().ToJson();
        Assert.That(await cli.RunAsync("agent", "bootstrap", "--json"), Is.EqualTo(0));
        using var doc = JsonDocument.Parse(cli.Out.ToString());
        var root = doc.RootElement;
        Assert.That(root.GetProperty("hub").GetProperty("running").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("hub").GetProperty("key").GetString(), Is.EqualTo("present"));
        Assert.That(root.GetProperty("catalog").GetProperty("operations").GetInt32(), Is.EqualTo(SampleCatalog.Hub().Operations.Count));
        Assert.That(root.GetProperty("owner").GetProperty("source").GetString(), Is.EqualTo("hub-default"));
        Assert.That(root.GetProperty("exit_codes").GetArrayLength(), Is.EqualTo(5));
        Assert.That(root.GetProperty("now_et").GetString(), Does.EndWith(" ET"));
    }

    [Test]
    public async Task HubStart_AlreadyRunning_DoesNotSpawn()
    {
        using var cli = new CliHarness();
        Assert.That(await cli.RunAsync("hub", "start"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("Already running"));
        Assert.That(cli.Processes.Started, Is.Empty);
    }

    [Test]
    public async Task HubStart_NothingToStart_ExitsOne()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("hub", "start"), Is.EqualTo(1));
        Assert.That(cli.Err.ToString(), Does.Contain("Can't find a Hub"));
    }

    [Test]
    public async Task HubStart_ProcessExits_ReportsIt_WithThePortFromTheUrl()
    {
        using var cli = new CliHarness(envVars: new() { ["IDIOTPROOF_HUB_MONITOR"] = "0" });
        cli.Hub.Up = false;
        Directory.CreateDirectory(Path.GetDirectoryName(cli.Paths.PublishedHubExe)!);
        File.WriteAllText(cli.Paths.PublishedHubExe, "");
        Assert.That(await cli.RunAsync("hub", "start", "--hub", "http://127.0.0.1:5919/"), Is.EqualTo(1));
        var launch = cli.Processes.Started.Single();
        Assert.That(launch.FileName, Is.EqualTo(cli.Paths.PublishedHubExe));
        Assert.That(launch.Environment["IDIOTPROOF_HUB_PORT"], Is.EqualTo("5919"));
        Assert.That(cli.Out.ToString(), Does.Contain("monitor  off"));
        Assert.That(cli.Err.ToString(), Does.Contain("exited during startup"));
    }

    [Test]
    public void ResolveLaunch_PrefersPublished_ElseRepo()
    {
        using var cli = new CliHarness();
        Assert.That(HubProcessControl.ResolveLaunch(cli.Paths, cli.Root), Is.Null);

        var repo = Path.Combine(cli.Root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "IdiotProof.Hub"));
        var deep = Path.Combine(repo, "IdiotProof.Cli", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(repo, "IdiotProof.slnx"), "<Solution />");
        var fromRepo = HubProcessControl.ResolveLaunch(cli.Paths, deep)!;
        Assert.That(fromRepo.FileName, Is.EqualTo("dotnet"));
        Assert.That(fromRepo.Arguments, Does.Contain(Path.Combine(repo, "IdiotProof.Hub")));
        Assert.That(fromRepo.WorkingDirectory, Is.EqualTo(Path.Combine(repo, "IdiotProof.Hub")));

        Directory.CreateDirectory(Path.GetDirectoryName(cli.Paths.PublishedHubExe)!);
        File.WriteAllText(cli.Paths.PublishedHubExe, "");
        Assert.That(HubProcessControl.ResolveLaunch(cli.Paths, deep)!.FileName, Is.EqualTo(cli.Paths.PublishedHubExe));
    }

    [Test]
    public async Task HubStop_StopsTheProcessThatAnswers()
    {
        using var cli = new CliHarness();
        Assert.That(await cli.RunAsync("hub", "stop"), Is.EqualTo(0));
        Assert.That(cli.Processes.Stopped, Is.EqualTo(new[] { 4242 }));
    }

    [Test]
    public async Task HubStop_NothingAnswers_OthersRunning_NeedsAll()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        cli.Processes.Running.AddRange([11, 12]);
        Assert.That(await cli.RunAsync("hub", "stop"), Is.EqualTo(1));
        Assert.That(cli.Processes.Stopped, Is.Empty);
        Assert.That(cli.Err.ToString(), Does.Contain("--all"));

        Assert.That(await cli.RunAsync("hub", "stop", "--all"), Is.EqualTo(0));
        Assert.That(cli.Processes.Stopped, Is.EqualTo(new[] { 11, 12 }));
    }

    [Test]
    public async Task HubLogs_TailsTheNewestHubLog()
    {
        using var cli = new CliHarness();
        Assert.That(await cli.RunAsync("hub", "logs"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("No Hub logs yet"));

        Directory.CreateDirectory(cli.Paths.LogsDirectory);
        var old = Path.Combine(cli.Paths.LogsDirectory, "hub-20261029-090000.log");
        var fresh = Path.Combine(cli.Paths.LogsDirectory, "hub-20261030-090000.log");
        File.WriteAllText(old, "old");
        File.WriteAllLines(fresh, Enumerable.Range(1, 100).Select(i => $"line {i}"));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-1));

        cli.Out.GetStringBuilder().Clear();
        Assert.That(await cli.RunAsync("hub", "logs", "--lines", "3"), Is.EqualTo(0));
        var text = cli.Out.ToString();
        Assert.That(text, Does.Contain("hub-20261030-090000.log").And.Contain("line 98").And.Contain("line 100"));
        Assert.That(text, Does.Not.Contain("line 97"));

        Assert.That(await cli.RunAsync("hub", "logs", "--lines", "x"), Is.EqualTo(CliExitCodes.Usage));
    }
}
