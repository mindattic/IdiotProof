using System.Xml.Linq;
using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T262: compiler XML doc comments read into plain-text symbol entries.</summary>
public class XmlDocsTests
{
    private const string Doc = """
        <?xml version="1.0"?>
        <doc>
          <assembly><name>IdiotProof.Core</name></assembly>
          <members>
            <member name="T:IdiotProof.Core.Trading.TransactionGateway">
              <summary>
              The one path every order takes: see <see cref="T:IdiotProof.Core.Trading.OrderIntent"/> and
              <c>SubmitAsync</c>.
              </summary>
              <remarks>Live needs a code.</remarks>
            </member>
            <member name="M:IdiotProof.Core.Trading.TransactionGateway.SubmitAsync(IdiotProof.Core.Trading.OrderIntent,System.Threading.CancellationToken)">
              <summary>Submits <paramref name="intent"/>.</summary>
              <param name="intent">What to trade.</param>
              <returns>The outcome.</returns>
            </member>
            <member name="T:IdiotProof.Core.Things`1"><summary>Generic.</summary></member>
          </members>
        </doc>
        """;

    [Test]
    public void Parse_TurnsMarkupIntoText()
    {
        var members = XmlDocs.Parse(XDocument.Parse(Doc), "IdiotProof.Core").ToList();

        Assert.That(members[0].Summary, Is.EqualTo("The one path every order takes: see OrderIntent and\n`SubmitAsync`."));
        Assert.That(members[0].Remarks, Is.EqualTo("Live needs a code."));
        Assert.That(members[0].DisplayName, Is.EqualTo("TransactionGateway"));
        Assert.That(members[1].DisplayName, Is.EqualTo("TransactionGateway.SubmitAsync"));
        Assert.That(members[1].FullName, Is.EqualTo("IdiotProof.Core.Trading.TransactionGateway.SubmitAsync"));
        Assert.That(members[1].Summary, Is.EqualTo("Submits intent."));
        Assert.That(members[1].Remarks, Does.Contain("Parameter intent: What to trade.").And.Contain("Returns: The outcome."));
        Assert.That(members[2].DisplayName, Is.EqualTo("Things"));
    }

    [Test]
    public void Load_ReadsIdiotProofDocFiles_SkippingTestsAndOthers()
    {
        var dir = Directory.CreateTempSubdirectory("ip-xmldocs").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "IdiotProof.Core.xml"), Doc);
            File.WriteAllText(Path.Combine(dir, "IdiotProof.Core.Tests.xml"), Doc);
            File.WriteAllText(Path.Combine(dir, "Newtonsoft.Json.xml"), Doc);
            File.WriteAllText(Path.Combine(dir, "IdiotProof.Broken.xml"), "<not xml");

            var members = XmlDocs.Load([dir, dir]);

            Assert.That(members, Has.Count.EqualTo(3), "one copy of IdiotProof.Core; tests, third parties and broken files skipped");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void Builder_IndexesThisBuildsRealDocFiles()
    {
        var index = new KnowledgeIndexBuilder(KnowledgeIndexBuilderTests.RealRepo())
        {
            XmlDocDirectories = [TestContext.CurrentContext.TestDirectory],
        }.Build();

        var gateway = index.Get("T:IdiotProof.Core.Trading.TransactionGateway");
        Assert.That(gateway?.Kind, Is.EqualTo(KnowledgeKind.Symbol));
        Assert.That(gateway!.Text, Is.Not.Empty);
        Assert.That(index.Get("T:IdiotProof.Operations.Docs.KnowledgeIndex")!.Title, Is.EqualTo("KnowledgeIndex"));
    }
}
