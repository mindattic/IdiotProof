using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T260..: the knowledge index built from a scratch checkout and from this repo's real docs.</summary>
public class KnowledgeIndexBuilderTests
{
    private string root = null!;

    [SetUp]
    public void MakeRepo()
    {
        root = Path.Combine(Path.GetTempPath(), "ip-knowledge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "IdiotProof.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(root, "docs", "BIBLE.md"), """
            # Bible
            ## 4. Architecture canon {#IP-§4}
            The Hub hosts everything.
            ## 5. The Laws {#IP-§5}
            ### {#IP-LAW-2} Risk Guardian holds the final veto
            No order without a stop.
            """);
        File.WriteAllText(Path.Combine(root, "README.md"), """
            # IdiotProof
            ## Command line, Hub and MCP
            Run idiotproof hub start.
            """);
    }

    [TearDown]
    public void Clean()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    /// <summary>This repo's checkout (the test runs from its bin folder under it).</summary>
    internal static string RealRepo() =>
        KnowledgeIndexBuilder.FindRepoRoot(TestContext.CurrentContext.TestDirectory)
        ?? throw new InvalidOperationException("test is not running inside the IdiotProof checkout");

    [Test]
    public void IndexesBibleSectionsLawsAndReadmeHeadings_WarningOnlyForMissingRequiredFiles()
    {
        var index = new KnowledgeIndexBuilder(root).Build();

        Assert.That(index.Get("IP-§4")!.Text, Is.EqualTo("The Hub hosts everything."));
        Assert.That(index.Get("IP-LAW-2")!.Location, Is.EqualTo("docs/BIBLE.md:5"));
        Assert.That(index.Get("README.md#command-line-hub-and-mcp")!.Text, Does.Contain("hub start"));
        Assert.That(index.Warnings.Where(w => w.Contains("BIBLE") || w.Contains("README") || w.Contains("AMENDMENTS")), Is.Empty,
            "the digest, amendments and agent docs are optional");
    }

    [Test]
    public void MissingBible_IsAWarningNotAFailure()
    {
        File.Delete(Path.Combine(root, "docs", "BIBLE.md"));

        var index = new KnowledgeIndexBuilder(root).Build();

        Assert.That(index.Warnings, Has.Some.Contains("docs/BIBLE.md"));
        Assert.That(index.Get("README.md#idiotproof"), Is.Not.Null);
    }

    [Test]
    public void IndexesStoriesWithStatusAndEpic_AndTicketRows()
    {
        File.WriteAllText(Path.Combine(root, "docs", "USER_STORIES.md"), """
            ## Epic W — Command line & Hub
            - **IP-US-W1 ✅** As an operator, I drive IdiotProof from the CLI. More detail.
            ## Tickets
            | ID | Date | Type | Status | Summary | Ref |
            |---|---|---|---|---|---|
            | IP-T259 | 2026-10-30 | feature | closed | KnowledgeIndex core | — |
            """);

        var index = new KnowledgeIndexBuilder(root).Build();

        var story = index.Get("ip-us-w1")!;
        Assert.That((story.Kind, story.Status, story.Parent, story.Title), Is.EqualTo(
            (KnowledgeKind.Story, "done", "Epic W — Command line & Hub", "As an operator, I drive IdiotProof from the CLI.")));
        var ticket = index.Get("IP-T259")!;
        Assert.That((ticket.Kind, ticket.Status, ticket.Location), Is.EqualTo((KnowledgeKind.Ticket, "closed", "docs/USER_STORIES.md:6")));
        Assert.That(ticket.Text, Does.Contain("Type: feature"));
    }

    [Test]
    public void RealStories_IndexEveryStoryAndTheTicketLog()
    {
        var index = new KnowledgeIndexBuilder(RealRepo()).Build();

        Assert.That(index.Get("IP-US-A1")!.Status, Is.EqualTo("done"));
        Assert.That(index.OfKind(KnowledgeKind.Story).Count(), Is.GreaterThan(50));
        Assert.That(index.Get("IP-T1")!.Kind, Is.EqualTo(KnowledgeKind.Ticket));
    }

    [Test]
    public void FindRepoRoot_WalksUpToTheCheckout()
    {
        var deep = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;

        Assert.That(KnowledgeIndexBuilder.FindRepoRoot(null, deep), Is.EqualTo(root));
    }

    [Test]
    public void RealBible_HasEveryNumberedSectionAndAllElevenLaws()
    {
        var index = new KnowledgeIndexBuilder(RealRepo()).Build();

        Assert.That(Enumerable.Range(1, 9).Select(n => index.Get($"IP-§{n}")), Has.All.Not.Null);
        Assert.That(Enumerable.Range(1, 11).Select(n => index.Get($"IP-LAW-{n}")?.Kind), Has.All.EqualTo(KnowledgeKind.Law));
    }
}
