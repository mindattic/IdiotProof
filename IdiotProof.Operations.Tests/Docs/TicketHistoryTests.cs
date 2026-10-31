using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T264: ticket history read through a scripted <c>tickets</c> CLI (read-only commands only).</summary>
public class TicketHistoryTests
{
    /// <summary>Answers the two read-only commands and records every invocation.</summary>
    internal sealed class FakeTickets : ITicketCli
    {
        public List<string> Calls { get; } = [];

        public bool Available { get; init; } = true;

        public Task<string?> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls.Add(string.Join(' ', arguments));
            if (!Available) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>(arguments switch
            {
                ["ticket", "list", "--repo", "IdiotProof", "--json"] => """
                    [
                      { "id": "IP-T1", "date": "2026-10-12", "type": "docs", "status": "closed", "summary": "From the CLI", "ref": "—" },
                      { "id": "IP-T900", "date": "2026-10-31", "type": "fix", "status": "open", "summary": "Only in the database", "ref": "x.cs" }
                    ]
                    """,
                ["ticket", "show", "IP-T264", "--json"] => """
                    {
                      "id": "IP-T264", "date": "2026-10-30", "type": "feature", "status": "open",
                      "summary": "Ticket history", "ref": "—", "description": "Read it.", "approval": "approved",
                      "requires": ["IP-T259"],
                      "links": [
                        { "from": "IP-T264", "kind": "requires", "to": "IP-T259", "note": null },
                        { "from": "IP-T269", "kind": "requires", "to": "IP-T264", "note": null }
                      ],
                      "history": [ { "at": "2026-10-30T19:23:25Z", "field": "created", "old": null, "new": "open" } ],
                      "notes": [],
                      "commits": [ { "shortHash": "abc1234", "date": "2026-10-30 23:10", "subject": "Ticket history (IP-T264)" } ]
                    }
                    """,
                _ => null,
            });
        }
    }

    [Test]
    public async Task Show_ParsesLinksCommitsAndHistory()
    {
        var detail = await new TicketHistory(new FakeTickets()).ShowAsync("IP-T264", CancellationToken.None);

        Assert.That(detail!.Requires, Is.EqualTo(new[] { "IP-T259" }));
        Assert.That(detail.RequiredBy, Is.EqualTo(new[] { "IP-T269" }));
        Assert.That(detail.Commits.Single().ShortHash, Is.EqualTo("abc1234"));
        Assert.That(detail.History.Single(), Is.EqualTo(new TicketEvent("2026-10-30T19:23:25Z", "created", null, "open")));
        Assert.That(detail.Approval, Is.EqualTo("approved"));
    }

    [Test]
    public async Task Show_UnknownTicketIsNull()
    {
        Assert.That(await new TicketHistory(new FakeTickets()).ShowAsync("IP-T5", CancellationToken.None), Is.Null);
    }

    [Test]
    public void Builder_AddsDatabaseOnlyTickets_KeepsTheTableRow_AndOnlyRunsReadCommands()
    {
        var root = Directory.CreateTempSubdirectory("ip-tickets").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "USER_STORIES.md"), "| IP-T1 | 2026-10-12 | docs | closed | From the table | — |\n");
            var cli = new FakeTickets();

            var index = new KnowledgeIndexBuilder(root) { Tickets = cli, XmlDocDirectories = [] }.Build();

            Assert.That(index.Get("IP-T1")!.Title, Is.EqualTo("From the table"));
            Assert.That(index.Get("IP-T900")!.Text, Does.Contain("Ref: x.cs"));
            Assert.That(cli.Calls, Has.All.StartWith("ticket list").Or.StartWith("ticket show"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Builder_WithoutTheCli_Warns()
    {
        var root = Directory.CreateTempSubdirectory("ip-tickets").FullName;
        try
        {
            var index = new KnowledgeIndexBuilder(root) { Tickets = new FakeTickets { Available = false }, XmlDocDirectories = [] }.Build();

            Assert.That(index.Warnings, Has.Some.Contains("tickets CLI not available"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
