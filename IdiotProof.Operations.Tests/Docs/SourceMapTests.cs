using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T263: symbol → path:line from the source text, and text-level callers.</summary>
public class SourceMapTests
{
    private static SourceMap Sample()
    {
        var map = new SourceMap();
        map.AddFile("IdiotProof.Core/Trading/TransactionGateway.cs",
        [
            "namespace IdiotProof.Core.Trading;",                                    // 1
            "",                                                                       // 2
            "/// <summary>class Fake in a comment</summary>",                         // 3
            "public sealed class TransactionGateway",                                 // 4
            "{",                                                                      // 5
            "    public TransactionGateway(IClock clock) { }",                        // 6
            "    public async Task<GatewayResult> SubmitAsync(OrderIntent intent)",   // 7
            "    {",                                                                  // 8
            "        if (intent is null) return null;",                               // 9
            "    }",                                                                  // 10
            "    public int Pending => 0;",                                           // 11
            "    public sealed record Preview(string Text);",                         // 12
            "}",                                                                      // 13
        ]);
        map.AddFile("IdiotProof.Operations/Trading/TradeOperations.cs",
        [
            "namespace IdiotProof.Operations.Trading",
            "{",
            "    internal static class TradeOps",
            "    {",
            "        // TransactionGateway in a comment is not a caller",
            "        static void Go(TransactionGateway g) => g.SubmitAsync(null);",
            "    }",
            "}",
        ]);
        return map;
    }

    [TestCase("IdiotProof.Core.Trading.TransactionGateway", "IdiotProof.Core/Trading/TransactionGateway.cs:4")]
    [TestCase("IdiotProof.Core.Trading.TransactionGateway.#ctor", "IdiotProof.Core/Trading/TransactionGateway.cs:6")]
    [TestCase("IdiotProof.Core.Trading.TransactionGateway.SubmitAsync", "IdiotProof.Core/Trading/TransactionGateway.cs:7")]
    [TestCase("IdiotProof.Core.Trading.TransactionGateway.Pending", "IdiotProof.Core/Trading/TransactionGateway.cs:11")]
    [TestCase("IdiotProof.Core.Trading.TransactionGateway.Preview", "IdiotProof.Core/Trading/TransactionGateway.cs:12")]
    [TestCase("IdiotProof.Operations.Trading.TradeOps", "IdiotProof.Operations/Trading/TradeOperations.cs:3")]
    public void Find_LocatesTypesMembersCtorsAndNestedTypes(string fullName, string expected) =>
        Assert.That(Sample().Find(fullName)?.ToString(), Is.EqualTo(expected));

    [Test]
    public void Find_UnknownIsNull_AndCommentsDeclareNothing()
    {
        var map = Sample();
        Assert.That(map.Find("IdiotProof.Core.Trading.Fake"), Is.Null);
        Assert.That(map.TypesNamed("transactiongateway"), Is.EqualTo(new[] { "IdiotProof.Core.Trading.TransactionGateway" }));
    }

    [Test]
    public void References_SkipTheDeclarationAndComments()
    {
        var map = Sample();
        var refs = map.References("TransactionGateway", declaredAt: map.Find("IdiotProof.Core.Trading.TransactionGateway"));

        Assert.That(refs.Select(r => r.ToString()), Is.EqualTo(new[]
        {
            "IdiotProof.Core/Trading/TransactionGateway.cs:6",
            "IdiotProof.Operations/Trading/TradeOperations.cs:6",
        }));
    }

    [Test]
    public void RealCheckout_LocatesTheGatewayAndTheDocsOperations()
    {
        var index = new KnowledgeIndexBuilder(KnowledgeIndexBuilderTests.RealRepo())
        {
            XmlDocDirectories = [TestContext.CurrentContext.TestDirectory],
        }.Build();

        var gateway = index.Get("T:IdiotProof.Core.Trading.TransactionGateway")!;
        Assert.That(gateway.Source, Is.EqualTo("IdiotProof.Core/Trading/TransactionGateway.cs"));
        Assert.That(gateway.Line, Is.GreaterThan(0));
        Assert.That(index.Get("T:IdiotProof.Operations.Docs.SourceMap")!.Source, Is.EqualTo("IdiotProof.Operations/Docs/SourceMap.cs"));
        Assert.That(index.Sources!.FileCount, Is.GreaterThan(100));
    }
}
