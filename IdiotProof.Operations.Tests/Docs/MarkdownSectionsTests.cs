using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T259: the knowledge index's markdown parser (anchors, bodies, stories, ticket rows).</summary>
public class MarkdownSectionsTests
{
    private const string Bible = """
        ---
        codex: 1
        ---
        # IdiotProof — Project Bible

        ## 1. The one sentence {#IP-§1}
        It trades gaps.

        ## 4. Architecture canon {#IP-§4}
        Intro.

        ### 4.4 Subsystems
        The Gapper.

        ```text
        # not a heading
        ```

        ## 5. The Laws {#IP-§5}

        ### {#IP-LAW-3} Sandbox is the always-safe default broker
        New strategies default to Sandbox.

        ## 6. Verified state {#IP-§6}
        Tests.
        """;

    [Test]
    public void Parse_FindsHeadingsAnchorsAndLines_SkippingFrontMatterAndFences()
    {
        var sections = MarkdownSections.Parse(Bible);

        Assert.That(sections.Select(s => s.Anchor), Is.EqualTo(new[]
        {
            "idiotproof--project-bible", "IP-§1", "IP-§4", "44-subsystems", "IP-§5", "IP-LAW-3", "IP-§6",
        }));
        var law = sections.Single(s => s.Anchor == "IP-LAW-3");
        Assert.That(law.Title, Is.EqualTo("Sandbox is the always-safe default broker"));
        Assert.That(law.Line, Is.EqualTo(21));
        Assert.That(law.Body, Is.EqualTo("New strategies default to Sandbox."));

        var four = sections.Single(s => s.Anchor == "IP-§4");
        Assert.That(four.Body, Does.Contain("The Gapper.").And.Contain("# not a heading"), "a section's body includes its subsections");
        Assert.That(four.Body, Does.Not.Contain("The Laws"));
    }

    [Test]
    public void AddMarkdown_Bible_MakesSectionsAndLaws()
    {
        var index = new KnowledgeIndex();
        index.AddMarkdown("docs/BIBLE.md", Bible, bible: true);

        Assert.That(index.Get("IP-§4")!.Kind, Is.EqualTo(KnowledgeKind.Section));
        Assert.That(index.Get("ip-§4.4")!.Parent, Is.EqualTo("IP-§4"));
        Assert.That(index.Get("IP-LAW-3")!.Kind, Is.EqualTo(KnowledgeKind.Law));
        Assert.That(index.Get("IP-LAW-3")!.Location, Is.EqualTo("docs/BIBLE.md:21"));
        Assert.That(index.Get("docs/BIBLE.md#idiotproof--project-bible")!.Kind, Is.EqualTo(KnowledgeKind.Doc));
    }

    [Test]
    public void ParseStories_ReadsIdStatusContinuationAndEpic()
    {
        const string text = """
            ## Epic K — Gapper {#Epic-K}
            - **IP-US-K1 ✅** As a trader, I queue a gapper
              before 4AM. *(verified by `Queue_Works`.)*
            - **IP-US-K2 🟡** As a trader, partial.
            - **IP-US-K3 ⬜** Planned one.

            ## Epic A — Risk
            - **IP-US-A1 ✅** Blocked without a stop.
            """;

        var stories = MarkdownSections.ParseStories(text);

        Assert.That(stories.Select(s => (s.Id, s.Status)), Is.EqualTo(new[]
        {
            ("IP-US-K1", "done"), ("IP-US-K2", "partial"), ("IP-US-K3", "planned"), ("IP-US-A1", "done"),
        }));
        Assert.That(stories[0].Text, Is.EqualTo("As a trader, I queue a gapper before 4AM. *(verified by `Queue_Works`.)*"));
        Assert.That(stories[0].Epic, Is.EqualTo("Epic K — Gapper"));
        Assert.That(stories[3].Line, Is.EqualTo(8));
    }

    [Test]
    public void ParseTicketRows_ReadsEveryColumn()
    {
        const string text = """
            | ID | Date | Type | Status | Summary | Ref |
            |---|---|---|---|---|---|
            | IP-T1 | 2026-10-12 | docs | closed | Start the log | — |
            | IP-T22 | 2026-10-16 | docs | open | README a \| b | user request |
            """;

        var rows = MarkdownSections.ParseTicketRows(text);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[1], Is.EqualTo(new MarkdownTicketRow("IP-T22", "2026-10-16", "docs", "open", "README a \\| b", "user request", 4)));
    }

    [TestCase("4", "IP-§4")]
    [TestCase("§4.4", "IP-§4.4")]
    [TestCase("IP-§6", "IP-§6")]
    [TestCase("#IP-§1", "IP-§1")]
    [TestCase("law", null)]
    public void SectionId_Normalizes(string input, string? expected) => Assert.That(KnowledgeIndex.SectionId(input), Is.EqualTo(expected));

    [TestCase("3", "IP-LAW-3")]
    [TestCase("ip-law-11", "IP-LAW-11")]
    [TestCase("LAW-07", "IP-LAW-7")]
    [TestCase("x", null)]
    public void LawId_Normalizes(string input, string? expected) => Assert.That(KnowledgeIndex.LawId(input), Is.EqualTo(expected));

    [TestCase("k1", "IP-US-K1")]
    [TestCase("IP-US-U10", "IP-US-U10")]
    [TestCase("259", null)]
    public void StoryId_Normalizes(string input, string? expected) => Assert.That(KnowledgeIndex.StoryId(input), Is.EqualTo(expected));

    [TestCase("259", "IP-T259")]
    [TestCase("ip-t22", "IP-T22")]
    [TestCase("K1", null)]
    public void TicketId_Normalizes(string input, string? expected) => Assert.That(KnowledgeIndex.TicketId(input), Is.EqualTo(expected));
}
