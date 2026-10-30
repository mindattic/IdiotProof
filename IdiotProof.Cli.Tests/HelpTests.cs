using System.Text.Json;
using IdiotProof.Operations;

namespace IdiotProof.Cli.Tests;

/// <summary>IP-T59/T60: generated help, rendered alike from the Hub's catalog JSON, its cache, or the reflected catalog.</summary>
public class HelpTests
{
    [Test]
    public void HelpFromJson_IsIdenticalToDescriptorHelp()
    {
        var catalog = SampleCatalog.Hub();
        var view = CatalogView.FromJson(catalog.ToJson(), "test");
        Assert.That(view.OperationCount, Is.EqualTo(catalog.Operations.Count));
        foreach (var op in catalog.Operations)
            Assert.That(view.Find(op.Name)!.Help, Is.EqualTo(op.Help), op.Name);
    }

    [Test]
    public void ReflectedAndJsonViews_Agree()
    {
        var catalog = SampleCatalog.Hub();
        var reflected = CatalogView.FromCatalog(catalog, "a");
        var json = CatalogView.FromJson(catalog.ToJson(indented: false), "b");
        Assert.That(json.Entries.Select(e => (e.Name, e.Noun, e.Verb, e.Summary, e.Risk, e.Usage, e.Help)),
            Is.EqualTo(reflected.Entries.Select(e => (e.Name, e.Noun, e.Verb, e.Summary, e.Risk, e.Usage, e.Help))));
    }

    [Test]
    public void FromJson_Junk_Throws()
    {
        Assert.Throws<JsonException>(() => CatalogView.FromJson("[1,2]", "x"));
        Assert.Throws<JsonException>(() => CatalogView.FromJson("{\"nope\":1}", "x"));
    }

    [Test]
    public void LocalCommands_WinOverHubOperationsOfTheSameName()
    {
        var hubJson = """{"operations":[{"name":"users use","summary":"server side","usage":"idiotproof users use"}]}""";
        var view = CatalogView.FromJson(hubJson, "hub").With(LocalCommands.All);
        Assert.That(view.Find("users use")!.Local, Is.True);
        Assert.That(view.Find("users use")!.Summary, Does.Not.Contain("server side"));
    }

    [Test]
    public void Overview_GroupsByNoun_WithSummaries()
    {
        var view = CatalogView.FromCatalog(SampleCatalog.Hub(), "test").With(LocalCommands.All);
        var text = HelpRenderer.Overview(view);
        Assert.That(text, Does.Contain("Commands:"));
        Assert.That(text, Does.Contain("  strategy\n").Or.Contain("  strategy\r\n"));
        Assert.That(text, Does.Contain("sample").And.Contain("A sample strategy command."));
        Assert.That(text, Does.Contain("bootstrap").And.Contain("ping").And.Contain("whoami"));
        Assert.That(text, Does.Contain("Times are Eastern (ET)"));
        Assert.That(text, Does.Contain("Catalog: test"));
        Assert.That(text.IndexOf("  agent", StringComparison.Ordinal), Is.LessThan(text.IndexOf("  strategy", StringComparison.Ordinal)));
    }

    [Test]
    public async Task NoArgs_PrintsTheOverview()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync(), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("Usage: idiotproof <noun> <verb>"));
    }

    [TestCase("hub", "--help")]
    [TestCase("help", "hub")]
    [TestCase("--help", "hub")]
    public async Task NounHelp_ListsItsVerbs(params string[] args)
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync(args), Is.EqualTo(0));
        var text = cli.Out.ToString();
        Assert.That(text, Does.Contain("ping").And.Contain("status").And.Contain("start").And.Contain("stop").And.Contain("logs"));
        Assert.That(text, Does.Not.Contain("bootstrap"));
    }

    [Test]
    public async Task VerbHelp_PrintsTheDescriptorHelp()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("hub", "ping", "--help"), Is.EqualTo(0));
        var ping = OperationCatalog.FromAssemblies(typeof(OperationCatalog).Assembly).Find("hub ping")!;
        Assert.That(cli.Out.ToString().TrimEnd(), Is.EqualTo(ping.Help));
        Assert.That(cli.Hub.Requests, Is.Empty, "help never runs the operation");
    }

    [Test]
    public async Task LocalCommandHelp()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("agent", "bootstrap", "-h"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("Usage: idiotproof agent bootstrap").And.Contain("does not need to be running"));
    }

    [Test]
    public async Task LiveHub_HelpShowsHubOnlyOperations_AndCachesTheCatalog()
    {
        using var cli = new CliHarness();
        cli.Hub.CatalogJson = SampleCatalog.Hub().ToJson();
        Assert.That(await cli.RunAsync("help"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("A sample strategy command.").And.Contain("live from the Hub"));
        Assert.That(File.ReadAllText(cli.Paths.CatalogCache), Is.EqualTo(cli.Hub.CatalogJson));

        // Hub goes away: the cached copy still knows the Hub-only operation.
        cli.Out.GetStringBuilder().Clear();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("strategy", "sample", "--help"), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("Usage: idiotproof strategy sample <ticker>"));
        cli.Out.GetStringBuilder().Clear();
        await cli.RunAsync("help");
        Assert.That(cli.Out.ToString(), Does.Contain("cached copy").And.Contain(" ET"));
    }

    [Test]
    public async Task HubDown_NoCache_UsesTheBuiltInCatalog()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        await cli.RunAsync("help");
        Assert.That(cli.Out.ToString(), Does.Contain("built into this CLI").And.Contain("agent").And.Contain("catalog"));
        Assert.That(cli.Out.ToString(), Does.Not.Contain("A sample strategy command."));
    }

    [Test]
    public async Task UnknownTopic_IsUsage_WithSuggestion()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("help", "hub", "pnig"), Is.EqualTo(CliExitCodes.Usage));
        Assert.That(cli.Err.ToString(), Does.Contain("Did you mean 'hub ping'"));
    }

    [Test]
    public async Task HelpJson_PrintsTheCatalog()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        await cli.RunAsync("help", "--json");
        using var doc = JsonDocument.Parse(cli.Out.ToString());
        Assert.That(doc.RootElement.GetProperty("operations").GetArrayLength(), Is.GreaterThanOrEqualTo(2));
    }
}
