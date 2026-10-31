using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T270: <c>docs dsl</c> explains a verb from the reflected catalog, the glossary and the XML docs.</summary>
public class DocsDslTests
{
    private static KnowledgeIndex realIndex = null!;

    [OneTimeSetUp]
    public void Build() => realIndex = new KnowledgeIndexBuilder(KnowledgeIndexBuilderTests.RealRepo())
    {
        XmlDocDirectories = [TestContext.CurrentContext.TestDirectory],
    }.Build();

    [Test]
    public async Task StopLoss_PhaseSignaturesMeaningAndAnExampleThatEndsInBuild()
    {
        var result = await new DocsDslOperation().ExecuteAsync(new DocsDslArgs { Verb = "stoploss" }, DocsOperationsTests.Context(realIndex));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.StartWith("StopLoss"));
        Assert.That(result.Human, Does.Contain("Phase: Risk"));
        Assert.That(result.Human, Does.Contain("Signatures:\n  StopLoss("));
        Assert.That(result.Human, Does.Contain("Example:\nStock.Ticker(\"SPY\")").And.Contain(".StopLoss(").And.Contain(".Build();"));
    }

    [Test]
    public async Task ConditionVerb_IsShownInsideIf()
    {
        var result = await new DocsDslOperation().ExecuteAsync(new DocsDslArgs { Verb = "IsAboveVwap" }, DocsOperationsTests.Context(realIndex));

        Assert.That(result.Human, Does.Contain("Above VWAP"), "the glossary title");
        Assert.That(result.Human, Does.Contain("IsAboveVwap("));
    }

    [Test]
    public void EveryReflectedVerb_GetsAnExampleOrIsAKnownShape()
    {
        var forms = DocsDslOperation.Catalog();
        Assert.That(forms, Is.Not.Empty);
        Assert.That(forms.Where(f => !f.IsCondition).Select(f => f.Method), Has.All.Not.Null, "every builder verb resolves to its method");
        Assert.That(forms.Where(f => f.Method is not null).Select(DocsDslOperation.Example), Has.All.Not.Null);
    }

    [Test]
    public void UnknownVerb_SuggestsNearOnes()
    {
        var ex = Assert.ThrowsAsync<OperationException>(() =>
            new DocsDslOperation().ExecuteAsync(new DocsDslArgs { Verb = "StopLos" }, DocsOperationsTests.Context(realIndex)));
        Assert.That(ex!.Message, Does.Contain("Did you mean").And.Contain("StopLoss"));
    }

    [Test]
    public void XmlDocId_MatchesTheCompilersFormat()
    {
        var method = typeof(DocsDslOperation).GetMethod(nameof(DocsDslOperation.XmlDocId), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.That(DocsDslOperation.XmlDocId(method), Is.EqualTo("M:IdiotProof.Operations.Docs.DocsDslOperation.XmlDocId(System.Reflection.MethodInfo)"));
    }
}
