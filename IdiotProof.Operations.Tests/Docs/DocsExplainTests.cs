using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T268: <c>docs explain</c> resolves a symbol and shows its docs, location, members and callers.</summary>
public class DocsExplainTests
{
    private static KnowledgeIndex Sample()
    {
        var index = new KnowledgeIndex();
        var sources = new SourceMap();
        sources.AddFile("IdiotProof.Core/Trading/TransactionGateway.cs",
        [
            "namespace IdiotProof.Core.Trading;",
            "public sealed class TransactionGateway",
            "{",
            "    public Task SubmitAsync(OrderIntent intent) => Task.CompletedTask;",
            "}",
        ]);
        sources.AddFile("IdiotProof.Operations/Trading/TradeOperations.cs",
        [
            "namespace IdiotProof.Operations.Trading;",
            "public sealed class TradeOps",
            "{",
            "    public Task Go(TransactionGateway gateway) => gateway.SubmitAsync(null!);",
            "}",
        ]);
        index.Sources = sources;
        index.Add(new KnowledgeEntry(KnowledgeKind.Symbol, "T:IdiotProof.Core.Trading.TransactionGateway", "TransactionGateway",
            "The one path every order takes.\n\nLive needs a code.", "IdiotProof.Core/Trading/TransactionGateway.cs", 2));
        index.Add(new KnowledgeEntry(KnowledgeKind.Symbol, "M:IdiotProof.Core.Trading.TransactionGateway.SubmitAsync(IdiotProof.Core.Trading.OrderIntent)",
            "TransactionGateway.SubmitAsync", "Submits an intent.", "IdiotProof.Core/Trading/TransactionGateway.cs", 4)
        {
            Parent = "T:IdiotProof.Core.Trading.TransactionGateway",
        });
        index.Add(new KnowledgeEntry(KnowledgeKind.Symbol, "T:IdiotProof.Other.TransactionGateway", "TransactionGateway", "", "Other.cs", 1));
        return index;
    }

    [Test]
    public async Task Type_ShowsDocsLocationMembersAndCallers()
    {
        var result = await new DocsExplainOperation().ExecuteAsync(
            new DocsExplainArgs { Symbol = "IdiotProof.Core.Trading.TransactionGateway" }, DocsOperationsTests.Context(Sample()));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.StartWith("TransactionGateway  (T:IdiotProof.Core.Trading.TransactionGateway)\nDeclared: IdiotProof.Core/Trading/TransactionGateway.cs:2"));
        Assert.That(result.Human, Does.Contain("Live needs a code."));
        Assert.That(result.Human, Does.Contain("SubmitAsync").And.Contain("Submits an intent."));
        Assert.That(result.Human, Does.Contain("IdiotProof.Operations/Trading/TradeOperations.cs:4"));
    }

    [Test]
    public async Task AmbiguousShortName_ListsTheCandidates()
    {
        var result = await new DocsExplainOperation().ExecuteAsync(new DocsExplainArgs { Symbol = "TransactionGateway" }, DocsOperationsTests.Context(Sample()));

        Assert.That(result.Human, Does.StartWith("'TransactionGateway' names 2 symbols"));
    }

    [Test]
    public async Task Member_ByTypeDotMember_CallersOfTheMemberName()
    {
        var result = await new DocsExplainOperation().ExecuteAsync(
            new DocsExplainArgs { Symbol = "transactiongateway.submitasync" }, DocsOperationsTests.Context(Sample()));

        Assert.That(result.Human, Does.StartWith("TransactionGateway.SubmitAsync"));
        Assert.That(result.Human, Does.Contain("Declared: IdiotProof.Core/Trading/TransactionGateway.cs:4"));
        Assert.That(result.Human, Does.Contain("TradeOperations.cs:4").And.Not.Contain("Members:"));
    }

    [Test]
    public void Unknown_FailsWithAHint()
    {
        var ex = Assert.ThrowsAsync<OperationException>(() =>
            new DocsExplainOperation().ExecuteAsync(new DocsExplainArgs { Symbol = "Nope" }, DocsOperationsTests.Context(Sample())));
        Assert.That(ex!.Message, Does.Contain("No type or member 'Nope'"));
    }

    [Test]
    public async Task RealCheckout_ExplainsTheKnowledgeIndex()
    {
        var index = new KnowledgeIndexBuilder(KnowledgeIndexBuilderTests.RealRepo())
        {
            XmlDocDirectories = [TestContext.CurrentContext.TestDirectory],
        }.Build();

        var result = await new DocsExplainOperation().ExecuteAsync(new DocsExplainArgs { Symbol = "KnowledgeIndexBuilder" }, DocsOperationsTests.Context(index));

        Assert.That(result.Human, Does.Contain("Declared: IdiotProof.Operations/Docs/KnowledgeIndexBuilder."), "a partial class lands on one of its files");
        Assert.That(result.Human, Does.Contain("Mentioned at"));
    }
}
