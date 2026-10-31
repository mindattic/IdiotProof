using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests.Docs;

/// <summary>IP-T265: the static catalogs (gapper profiles, index events, Legion panel, settings) in the index.</summary>
public class CatalogIndexTests
{
    private static KnowledgeIndex index = null!;

    [OneTimeSetUp]
    public void Build() => index = new KnowledgeIndexBuilder(KnowledgeIndexBuilderTests.RealRepo())
    {
        XmlDocDirectories = [TestContext.CurrentContext.TestDirectory],
    }.Build();

    [Test]
    public void GapperProfiles_AreEntriesWithTheirLine()
    {
        var classic = index.Get("gapper-profile:classic-gapper")!;
        Assert.That(classic.Kind, Is.EqualTo(KnowledgeKind.Catalog));
        Assert.That(classic.Title, Does.Contain("Classic Gapper"));
        Assert.That(classic.Text, Does.Contain("minGapPercent: 5"));
        Assert.That(classic.Source, Is.EqualTo(KnowledgeIndexBuilder.GapperProfilesFile));
        Assert.That(classic.Line, Is.GreaterThan(1));
    }

    [Test]
    public void IndexEvents_And_LegionPanel_AreIndexed()
    {
        Assert.That(index.OfKind(KnowledgeKind.Catalog).Where(e => e.Id.StartsWith("index-event:")), Is.Not.Empty);
        Assert.That(index.Get("legion:panel")!.Text, Does.Contain("Judge: claude"));
        Assert.That(index.Get("legion:voter:openai"), Is.Not.Null);
    }

    [Test]
    public void Settings_ShowDefaults_ButNeverCredentialValues()
    {
        Assert.That(index.Get("setting:DefaultBroker")!.Text, Does.Contain("Default: \"Sandbox\""));
        Assert.That(index.Get("setting:LlmConsensusThreshold")!.Text, Does.Contain("Default: 0.66"));
        var secret = index.Get("setting:AlpacaApiSecretKey")!;
        Assert.That(secret.Text, Does.Contain("never shown"));
        Assert.That(index.Get("setting:AdminPasswordHash")!.Text, Does.Contain("never shown"));
    }
}
