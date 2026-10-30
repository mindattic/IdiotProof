using System.Text.Json;

namespace IdiotProof.Cli.Tests;

/// <summary>IP-T63: the saved owner default, and what each Hub call sends.</summary>
public class OwnerTests
{
    private static readonly Guid Saved = Guid.Parse("A99504C3-0759-4256-8ABB-A1F5C643808C");

    [Test]
    public void Store_RoundTrips_AndKeepsOtherKeys()
    {
        using var cli = new CliHarness();
        var store = new OwnerStore(cli.Paths.ConfigFile);
        Assert.That(store.Load(), Is.Null);

        Directory.CreateDirectory(Path.GetDirectoryName(store.ConfigFile)!);
        File.WriteAllText(store.ConfigFile, """{"other": 1}""");
        store.Save(Saved);
        Assert.That(store.Load(), Is.EqualTo(Saved));
        using (var doc = JsonDocument.Parse(File.ReadAllText(store.ConfigFile)))
        {
            Assert.That(doc.RootElement.GetProperty("owner").GetString(), Is.EqualTo(Saved.ToString("D")));
            Assert.That(doc.RootElement.GetProperty("other").GetInt32(), Is.EqualTo(1));
        }

        store.Save(null);
        Assert.That(store.Load(), Is.Null);
    }

    [Test]
    public void Store_JunkFile_ReadsAsNoOwner()
    {
        using var cli = new CliHarness();
        Directory.CreateDirectory(Path.GetDirectoryName(cli.Paths.ConfigFile)!);
        File.WriteAllText(cli.Paths.ConfigFile, "{not json");
        Assert.That(new OwnerStore(cli.Paths.ConfigFile).Load(), Is.Null);
    }

    [Test]
    public void Resolve_FlagThenSavedThenHub()
    {
        using var cli = new CliHarness();
        var store = new OwnerStore(cli.Paths.ConfigFile);
        Assert.That(store.Resolve(null), Is.EqualTo(((Guid?)null, OwnerSource.HubDefault)));
        store.Save(Saved);
        Assert.That(store.Resolve(null), Is.EqualTo(((Guid?)Saved, OwnerSource.Saved)));
        var flag = Guid.NewGuid();
        Assert.That(store.Resolve(flag), Is.EqualTo(((Guid?)flag, OwnerSource.Flag)));
    }

    [Test]
    public async Task UsersUse_Persists_AndEveryCallSendsIt()
    {
        using var cli = new CliHarness();
        Assert.That(await cli.RunAsync("users", "use", Saved.ToString()), Is.EqualTo(0));
        Assert.That(cli.Hub.Requests, Is.Empty, "users use is client-side");

        await cli.RunAsync("hub", "ping");
        Assert.That(cli.Hub.Requests[^1].OwnerId, Is.EqualTo(Saved));

        var other = Guid.NewGuid();
        await cli.RunAsync("hub", "ping", "--user", other.ToString());
        Assert.That(cli.Hub.Requests[^1].OwnerId, Is.EqualTo(other), "--user wins");

        await cli.RunAsync("users", "use", "--clear");
        await cli.RunAsync("hub", "ping");
        Assert.That(cli.Hub.Requests[^1].OwnerId, Is.Null, "no owner → the Hub decides");
    }

    [Test]
    public async Task UsersUse_BadGuid_IsUsage()
    {
        using var cli = new CliHarness();
        Assert.That(await cli.RunAsync("users", "use", "bob"), Is.EqualTo(CliExitCodes.Usage));
        Assert.That(File.Exists(cli.Paths.ConfigFile), Is.False);
    }

    [Test]
    public async Task WhoAmI_SaysWhereTheOwnerCameFrom()
    {
        using var cli = new CliHarness();
        await cli.RunAsync("users", "whoami");
        Assert.That(cli.Out.ToString(), Does.Contain("(no owner)").And.Contain("Hub decides"));

        await cli.RunAsync("users", "use", Saved.ToString());
        cli.Out.GetStringBuilder().Clear();
        await cli.RunAsync("users", "whoami", "--json");
        using var doc = JsonDocument.Parse(cli.Out.ToString());
        Assert.That(doc.RootElement.GetProperty("owner").GetGuid(), Is.EqualTo(Saved));
        Assert.That(doc.RootElement.GetProperty("source").GetString(), Is.EqualTo("saved"));
    }
}
