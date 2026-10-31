using IdiotProof.Monitor;

using IdiotProof.Core.Monitoring;
namespace IdiotProof.Monitor.Tests;

/// <summary>IP-T81: idiotproof-monitor.exe with no subcommand finds and starts the Hub.</summary>
public class HubShimTests
{
    private static readonly string Deploy = Path.Combine(Path.GetTempPath(), "Apps", "IdiotProof");

    [Test]
    public void DeployLayout_FindsTheSiblingHub()
    {
        var hub = Path.Combine(Deploy, "Hub", HubShim.HubExeName);
        var launch = HubShim.Resolve(Path.Combine(Deploy, "Monitor") + Path.DirectorySeparatorChar, null, f => f == hub, _ => false);

        Assert.That(launch, Is.Not.Null);
        Assert.That(launch!.FileName, Is.EqualTo(hub));
        Assert.That(launch.WorkingDirectory, Is.EqualTo(Path.GetDirectoryName(hub)));
    }

    [Test]
    public void EnvironmentOverride_WinsWhenTheFileExists()
    {
        var custom = Path.Combine(Path.GetTempPath(), "elsewhere", HubShim.HubExeName);
        var sibling = Path.Combine(Deploy, "Hub", HubShim.HubExeName);

        var launch = HubShim.Resolve(Path.Combine(Deploy, "Monitor"), custom, f => f == custom || f == sibling, _ => false);

        Assert.That(launch!.FileName, Is.EqualTo(custom));
    }

    [Test]
    public void RepoCheckout_FallsBackToDotnetRunOfTheHubProject()
    {
        var repo = Path.Combine(Path.GetTempPath(), "repo");
        var bin = Path.Combine(repo, "IdiotProof.Monitor", "bin", "Debug", "net10.0");
        var project = Path.Combine(repo, "IdiotProof.Hub");

        var launch = HubShim.Resolve(bin, null, f => f == Path.Combine(repo, "IdiotProof.slnx"), d => d == project);

        Assert.That(launch, Is.Not.Null);
        Assert.That(launch!.FileName, Is.EqualTo("dotnet"));
        Assert.That(launch.Arguments, Does.Contain($"run --project \"{project}\""));
        Assert.That(launch.WorkingDirectory, Is.EqualTo(project));
    }

    [Test]
    public void NothingFound_IsNull() =>
        Assert.That(HubShim.Resolve(Path.Combine(Deploy, "Monitor"), null, _ => false, _ => false), Is.Null);
}
