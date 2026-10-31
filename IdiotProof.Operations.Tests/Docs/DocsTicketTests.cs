using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T269: <c>docs ticket</c> through a scripted tickets CLI, and the USER_STORIES fallback.</summary>
public class DocsTicketTests
{
    [Test]
    public async Task ShowsDetailCommitsAndHistoryInEt()
    {
        var result = await new DocsTicketOperation().ExecuteAsync(new DocsTicketArgs { Id = "264", History = true },
            DocsOperationsTests.Context(tickets: new TicketHistoryTests.FakeTickets()));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.StartWith("IP-T264 — Ticket history"));
        Assert.That(result.Human, Does.Contain("open · approved").And.Contain("IP-T259").And.Contain("IP-T269"));
        Assert.That(result.Human, Does.Contain("abc1234").And.Contain("Read it."));
        Assert.That(result.Human, Does.Contain("3:23 PM ET"), "19:23Z on 2026-10-30 is 3:23 PM EDT");
        Assert.That(result.Human, Does.Contain("created: → open"));
    }

    [Test]
    public async Task WithoutTheCli_FallsBackToTheStoriesTable()
    {
        var result = await new DocsTicketOperation().ExecuteAsync(new DocsTicketArgs { Id = "IP-T259" },
            DocsOperationsTests.Context(tickets: new TicketHistoryTests.FakeTickets { Available = false }));

        Assert.That(result.Human, Does.StartWith("IP-T259 — KnowledgeIndex core").And.Contain("tickets CLI was not available"));
    }

    [Test]
    public void BadAndUnknownIds()
    {
        var usage = Assert.ThrowsAsync<OperationException>(() => new DocsTicketOperation().ExecuteAsync(new DocsTicketArgs { Id = "K1" },
            DocsOperationsTests.Context(tickets: new TicketHistoryTests.FakeTickets())));
        Assert.That(usage!.Status, Is.EqualTo(OperationStatus.UsageError));

        var missing = Assert.ThrowsAsync<OperationException>(() => new DocsTicketOperation().ExecuteAsync(new DocsTicketArgs { Id = "IP-T5" },
            DocsOperationsTests.Context(tickets: new TicketHistoryTests.FakeTickets())));
        Assert.That(missing!.Status, Is.EqualTo(OperationStatus.Failed));
    }
}
