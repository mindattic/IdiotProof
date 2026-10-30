using IdiotProof.Blazor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

[TestFixture]
public class Form4ParserTests
{
    private Form4Parser parser = null!;
    private string sampleXml = null!;

    [SetUp]
    public void SetUp()
    {
        parser = new Form4Parser(NullLogger<Form4Parser>.Instance);
        sampleXml = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "sample-form4.xml"));
    }

    [Test]
    public void Parse_ExtractsBothNonDerivativeTransactions()
    {
        var result = parser.Parse(sampleXml, "https://example.test/filing");
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void Parse_AcquiredTransaction_HasNoPriceButPositivePct()
    {
        var result = parser.Parse(sampleXml, null);
        var acquired = result[0];

        Assert.Multiple(() =>
        {
            Assert.That(acquired.TransactionCode, Is.EqualTo("M"));
            Assert.That(acquired.SharesTransacted, Is.EqualTo(240m));
            Assert.That(acquired.PricePerShare, Is.Null); // only a footnoteId, no <value>
            Assert.That(acquired.DollarValue, Is.Null);
            Assert.That(acquired.SharesOwnedAfter, Is.EqualTo(38953m));
            Assert.That(acquired.PctOfHoldingsChanged, Is.Not.Null);
            Assert.That(acquired.PctOfHoldingsChanged!.Value, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Parse_DisposedTransaction_HasPriceAndNegativePct()
    {
        var result = parser.Parse(sampleXml, null);
        var disposed = result[1];

        Assert.Multiple(() =>
        {
            Assert.That(disposed.TransactionCode, Is.EqualTo("F"));
            Assert.That(disposed.SharesTransacted, Is.EqualTo(124m));
            Assert.That(disposed.PricePerShare, Is.EqualTo(296.42m));
            Assert.That(disposed.DollarValue, Is.EqualTo(124m * 296.42m));
            Assert.That(disposed.SharesOwnedAfter, Is.EqualTo(38829m));
            Assert.That(disposed.PctOfHoldingsChanged, Is.Not.Null);
            Assert.That(disposed.PctOfHoldingsChanged!.Value, Is.LessThan(0));
        });
    }

    [Test]
    public void Parse_ReadsFilerNameAndOfficerRole()
    {
        var result = parser.Parse(sampleXml, null);
        Assert.That(result[0].FilerName, Is.EqualTo("Borders Ben"));
        Assert.That(result[0].FilerRole, Is.EqualTo("Officer"));
    }

    [Test]
    public void Parse_MalformedXml_ReturnsEmptyListInsteadOfThrowing()
    {
        var result = parser.Parse("not xml at all <<<", null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_EmptyNonDerivativeTable_ReturnsEmptyList()
    {
        const string noTable = "<ownershipDocument><reportingOwner/></ownershipDocument>";
        var result = parser.Parse(noTable, null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Summarize_DisposedTransaction_OmitsNoNullClausesAndUsesDisposedVerb()
    {
        var result = parser.Parse(sampleXml, null);
        var text = parser.Summarize(result[1]);

        Assert.That(text, Does.Contain("disposed of 124 shares"));
        Assert.That(text, Does.Contain("$296.42/share"));
        Assert.That(text, Does.Contain("now holds 38,829 shares"));
    }

    [Test]
    public void Summarize_AcquiredTransactionWithNullPrice_DoesNotPrintPriceClause()
    {
        var result = parser.Parse(sampleXml, null);
        var text = parser.Summarize(result[0]);

        Assert.That(text, Does.Contain("acquired 240 shares"));
        Assert.That(text, Does.Not.Contain("at $"));
        Assert.That(text, Does.Not.Contain("null"));
    }
}
