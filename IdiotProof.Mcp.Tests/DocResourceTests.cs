using System.Text.Json.Nodes;
using IdiotProof.Mcp;

namespace IdiotProof.Mcp.Tests;

/// <summary>IP-T122: the BIBLE, stories, protocol and catalog as MCP resources.</summary>
[TestFixture]
public sealed class DocResourceTests
{
    private string repo = null!;
    private string shipped = null!;

    [SetUp]
    public void Make()
    {
        repo = Directory.CreateTempSubdirectory("ip-mcp-repo").FullName;
        shipped = Directory.CreateTempSubdirectory("ip-mcp-shipped").FullName;
        Directory.CreateDirectory(Path.Combine(repo, "docs", "agent"));
        Directory.CreateDirectory(Path.Combine(shipped, "docs"));
        File.WriteAllText(Path.Combine(repo, "docs", "BIBLE.md"), "# Bible from the checkout");
        File.WriteAllText(Path.Combine(shipped, "docs", "BIBLE.md"), "# Bible shipped");
        File.WriteAllText(Path.Combine(shipped, "docs", "USER_STORIES.md"), "# Stories shipped");
    }

    [TearDown]
    public void Clean()
    {
        foreach (var d in new[] { repo, shipped })
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
    }

    private IReadOnlyList<DocResource> Resources(JsonNode? live) =>
        DocResources.All(repo, shipped, _ => Task.FromResult(live), new JsonObject { ["program"] = "offline" });

    private static Task<string> Read(IReadOnlyList<DocResource> all, string uri) => all.Single(r => r.Uri == uri).Read(CancellationToken.None);

    [Test]
    public async Task Docs_PreferTheCheckout_FallBackToTheShippedCopy_AndSayWhenMissing()
    {
        var all = Resources(null);

        Assert.That(await Read(all, DocResources.BibleUri), Is.EqualTo("# Bible from the checkout"));
        Assert.That(await Read(all, DocResources.StoriesUri), Is.EqualTo("# Stories shipped"));
        Assert.That(await Read(all, DocResources.ProtocolUri), Does.Contain("was not found").And.Contain("IDIOTPROOF_REPO"));
    }

    [Test]
    public async Task Catalog_IsTheLiveOneWhenTheHubAnswers_ElseOffline()
    {
        Assert.That(await Read(Resources(new JsonObject { ["program"] = "live" }), DocResources.CatalogUri), Does.Contain("\"live\""));
        Assert.That(await Read(Resources(null), DocResources.CatalogUri), Does.Contain("\"offline\""));
    }

    [Test]
    public void McpResources_CarryUriNameAndMimeType()
    {
        var mcp = DocResources.ToMcp(Resources(null)).ToList();

        Assert.That(mcp.Select(r => r.ProtocolResource?.Uri), Is.EquivalentTo(new[]
        {
            DocResources.BibleUri, DocResources.StoriesUri, DocResources.ProtocolUri, DocResources.CatalogUri,
        }));
        Assert.That(mcp.Single(r => r.ProtocolResource!.Uri == DocResources.CatalogUri).ProtocolResource!.MimeType, Is.EqualTo("application/json"));
    }

    [Test]
    public void TheRealDocsShipWithTheServer()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        Assert.That(File.Exists(Path.Combine(dir, "docs", "BIBLE.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(dir, "docs", "agent", "IDIOTPROOF_PROTOCOL.md")), Is.True);
    }
}
