using System.Text.Json.Nodes;
using IdiotProof.Operations;

namespace IdiotProof.Cli.Tests;

/// <summary>IP-T272: <c>agent export</c> writes the three generated references, and <c>--check</c> reports staleness.</summary>
public class AgentExportTests
{
    private string root = null!;

    [SetUp]
    public void MakeRoot() => root = Directory.CreateTempSubdirectory("ip-export").FullName;

    [TearDown]
    public void Clean()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static OperationCatalog Catalog() => OperationCatalog.FromAssemblies(typeof(OperationCatalog).Assembly);

    [Test]
    public void WritesAllThreeFiles_CoveringEveryOperation()
    {
        var catalog = Catalog();
        var files = AgentExport.Write(catalog, root);

        Assert.That(files.Select(f => f.RelativePath), Is.EquivalentTo(new[] { AgentExport.CommandsFile, AgentExport.ToolsFile, AgentExport.CatalogFile }));
        Assert.That(files, Has.All.Matches<ExportedFile>(f => f.Changed && File.Exists(f.FullPath)));

        var commands = File.ReadAllText(Path.Combine(root, "docs", "CLI_COMMANDS.md"));
        var tools = File.ReadAllText(Path.Combine(root, "docs", "MCP_TOOLS.md"));
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs", "agent", "operation-catalog.json")))!;
        Assert.Multiple(() =>
        {
            foreach (var op in catalog.Operations)
            {
                Assert.That(commands, Does.Contain($"### {op.Name}\n"), op.Name);
                Assert.That(tools, Does.Contain($"## `{op.ToolName}`"), op.ToolName);
            }
            Assert.That(json["operations"]!.AsArray(), Has.Count.EqualTo(catalog.Operations.Count));
            Assert.That(commands, Does.Contain("`idiotproof agent export`"));
            Assert.That(commands + tools, Does.Not.Contain("\r\n"));
        });
    }

    [Test]
    public void SecondRun_ChangesNothing_AndCheckSpotsAStaleFile()
    {
        AgentExport.Write(Catalog(), root);
        Assert.That(AgentExport.Write(Catalog(), root), Has.None.Matches<ExportedFile>(f => f.Changed));

        File.AppendAllText(Path.Combine(root, "docs", "MCP_TOOLS.md"), "hand edit");
        var check = AgentExport.Write(Catalog(), root, checkOnly: true);

        Assert.That(check.Single(f => f.Changed).RelativePath, Is.EqualTo(AgentExport.ToolsFile));
        Assert.That(File.ReadAllText(Path.Combine(root, "docs", "MCP_TOOLS.md")), Does.EndWith("hand edit"), "--check writes nothing");
    }

    [Test]
    public async Task Cli_AgentExport_WithOut_AndCheckExitCodes()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;

        Assert.That(await cli.RunAsync("agent", "export", "--out", root), Is.EqualTo(0));
        Assert.That(cli.Out.ToString(), Does.Contain("docs/MCP_TOOLS.md").And.Contain("written"));

        File.WriteAllText(Path.Combine(root, "docs", "CLI_COMMANDS.md"), "old");
        Assert.That(await cli.RunAsync("agent", "export", "--out", root, "--check"), Is.EqualTo(1));
        Assert.That(await cli.RunAsync("agent", "export", "--bogus"), Is.EqualTo(2));
    }
}
