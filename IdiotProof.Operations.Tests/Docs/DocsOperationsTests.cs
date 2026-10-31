using IdiotProof.Operations.Docs;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>The <c>docs</c> operations (IP-T266..IP-T270) over a small hand-built index.</summary>
public class DocsOperationsTests
{
    internal static KnowledgeIndex SampleIndex()
    {
        var index = new KnowledgeIndex { RepoRoot = "C:/repo" };
        index.AddMarkdown("docs/BIBLE.md", """
            # Bible
            ## 4. Architecture canon {#IP-§4}
            The Hub hosts the transaction gateway; every order goes through it.
            ### 4.4 Subsystems
            The Gapper buys the premarket gap.
            ## 5. The Laws {#IP-§5}
            ### {#IP-LAW-2} Risk Guardian holds the final veto
            No order without a stop loss. The Risk Guardian can veto any order.
            ### {#IP-LAW-3} Sandbox is the always-safe default broker
            New strategies default to Sandbox.
            """, bible: true);
        index.Add(new KnowledgeEntry(KnowledgeKind.Story, "IP-US-K1", "As a trader, I queue a gapper.", "As a trader, I queue a gapper at 4AM with a peak giveback exit.", "docs/USER_STORIES.md", 90)
        {
            Status = "done",
            Parent = "Epic K — Gapper",
        });
        index.Add(new KnowledgeEntry(KnowledgeKind.Ticket, "IP-T259", "KnowledgeIndex core", "KnowledgeIndex core\nType: feature", "docs/USER_STORIES.md", 600) { Status = "closed" });
        index.Add(new KnowledgeEntry(KnowledgeKind.Symbol, "T:IdiotProof.Core.Trading.TransactionGateway", "TransactionGateway",
            "The one path every order takes.", "IdiotProof.Core/Trading/TransactionGateway.cs", 40));
        return index;
    }

    internal static OperationContext Context(KnowledgeIndex? index = null, ITicketCli? tickets = null) => new()
    {
        Services = new ServiceCollection().AddSingleton(KnowledgeService.Fixed(index ?? SampleIndex(), tickets)).BuildServiceProvider(),
        Width = 140,
    };

    [Test]
    public async Task Search_RanksTheLawAboveTheBody_AndRendersATable()
    {
        var result = await new DocsSearchOperation().ExecuteAsync(new DocsSearchArgs { Text = ["risk", "guardian"] }, Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("result").And.Contain("for \"risk guardian\""));
        var firstRow = result.Human!.Split('\n')[4];
        Assert.That(firstRow, Does.Contain("IP-LAW-2").And.Contain("docs/BIBLE.md:7"), "the law outranks the sections that merely contain it");
    }

    [Test]
    public async Task Search_ExactIdComesFirst()
    {
        var hits = KnowledgeSearch.Search(SampleIndex(), "law-3");
        Assert.That(hits[0].Entry.Id, Is.EqualTo("IP-LAW-3"));

        var symbol = KnowledgeSearch.Search(SampleIndex(), "TransactionGateway");
        Assert.That(symbol[0].Entry.Kind, Is.EqualTo(KnowledgeKind.Symbol));
        Assert.That(KnowledgeSearch.Search(SampleIndex(), "transaction gateway")[0].Entry.Id, Is.EqualTo("T:IdiotProof.Core.Trading.TransactionGateway").Or.EqualTo("IP-§4"));
    }

    [Test]
    public async Task Search_KindFilter_AndNothingFound()
    {
        var stories = await new DocsSearchOperation().ExecuteAsync(new DocsSearchArgs { Text = ["gapper"], Kind = KnowledgeKind.Story }, Context());
        Assert.That(stories.Human, Does.Contain("IP-US-K1").And.Contain("[done]").And.Not.Contain("IP-§4.4"));

        var none = await new DocsSearchOperation().ExecuteAsync(new DocsSearchArgs { Text = ["zebra"] }, Context());
        Assert.That(none.IsOk, Is.True);
        Assert.That(none.Human, Does.StartWith("Nothing matches"));

        var empty = await new DocsSearchOperation().ExecuteAsync(new DocsSearchArgs(), Context());
        Assert.That(empty.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public void Search_PartialMatchesWhenNoEntryHasEveryWord()
    {
        var hits = KnowledgeSearch.Search(SampleIndex(), "sandbox default unicorn");
        Assert.That(hits.Select(h => h.Entry.Id), Does.Contain("IP-LAW-3"));
    }

    [Test]
    public async Task NoCheckout_FailsWithTheEnvVarHint()
    {
        var ctx = new OperationContext
        {
            Services = new ServiceCollection().AddSingleton(new KnowledgeService(_ => null)).BuildServiceProvider(),
        };
        var ex = Assert.ThrowsAsync<OperationException>(() => new DocsSearchOperation().ExecuteAsync(new DocsSearchArgs { Text = ["x"] }, ctx));
        Assert.That(ex!.Message, Does.Contain(KnowledgeIndexBuilder.RepoEnvVar));
        await Task.CompletedTask;
    }
}
