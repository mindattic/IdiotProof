using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Gappers;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T458: the widened EDGAR pull reads its own form list from form.idx and leaves the frozen list alone.</summary>
[TestFixture]
public class DilutionPullTests
{
    private const string FormIdx = """
        Description:           Master Index of EDGAR Dissemination Feed by Form Type
        Last Data Received:    March 31, 2024

        Form Type   Company Name                                                  CIK         Date Filed  File Name
        ---------------------------------------------------------------------------------------------------------------------------------------------
        10-K             BIG CO                                                        1000001     2024-03-01  edgar/data/1000001/0001000001-24-000001.txt
        424B5            SMALL BIO INC                                                 1234567     2024-03-01  edgar/data/1234567/0001234567-24-000050.txt
        6-K              GREEK SHIPPING LTD                                            7654321     2024-03-11  edgar/data/7654321/0007654321-24-000010.txt
        6-K/A            GREEK SHIPPING LTD                                            7654321     2024-03-12  edgar/data/7654321/0007654321-24-000011.txt
        8-K              SMALL BIO INC                                                 1234567     2024-03-04  edgar/data/1234567/0001234567-24-000051.txt
        EFFECT           SMALL BIO INC                                                 1234567     2024-02-01  edgar/data/1234567/9999999995-24-000123.txt
        SC 13G/A         SMALL BIO INC                                                 1234567     2024-03-08  edgar/data/1234567/0000950170-24-000777.txt
        F-1/A            GREEK SHIPPING LTD                                            7654321     2024-01-02  edgar/data/7654321/0007654321-24-000001.txt
        424B7            SMALL BIO INC                                                 1234567     2024-03-12  edgar/data/1234567/0001234567-24-000052.txt
        """;

    [Test]
    public void WidenedFilter_KeepsTheNewFormsAndTheTextForms_NotTheRest()
    {
        var rows = EdgarFormIndex.Parse(new StringReader(FormIdx), DilutionForms.Pulled.ToHashSet(StringComparer.Ordinal)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.FormType), Is.EquivalentTo(new[] { "424B5", "6-K", "6-K/A", "EFFECT", "SC 13G/A", "F-1/A", "424B7" }));
            Assert.That(rows.Single(r => r.FormType == "SC 13G/A").CompanyName, Is.EqualTo("SMALL BIO INC"));
            Assert.That(rows.Single(r => r.FormType == "EFFECT").AccessionNumber, Is.EqualTo("9999999995-24-000123"));
        });
    }

    [Test]
    public void FrozenEdgarForms_AreUnchanged_AndShareNothingWithTheWidenedSet()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Params.DataTiers.EdgarForms, Is.EqualTo(new[] { "8-K", "8-K/A", "4", "3", "S-1", "S-3", "424B3", "424B4", "424B5", "SC 13D", "25-NSE", "15-12G" }));
            Assert.That(DilutionForms.Widened.Intersect(Params.DataTiers.EdgarForms), Is.Empty);
            Assert.That(GapperPullStage.EdgarDilutionTier, Is.Not.EqualTo("edgar"), "the dilution pull is its own tier, never part of `all`");
        });
    }

    [Test]
    public void DilutionDir_HonoursTheOverride()
    {
        var before = Environment.GetEnvironmentVariable("GAPPER_EDGAR_DILUTION_DIR");
        try
        {
            Environment.SetEnvironmentVariable("GAPPER_EDGAR_DILUTION_DIR", @"X:\elsewhere");
            Assert.That(GapperPullStage.DilutionDir(Params), Is.EqualTo(@"X:\elsewhere"));
            Environment.SetEnvironmentVariable("GAPPER_EDGAR_DILUTION_DIR", null);
            Assert.That(Path.GetFileName(GapperPullStage.DilutionDir(Params)), Is.EqualTo("edgar-dilution"));
        }
        finally { Environment.SetEnvironmentVariable("GAPPER_EDGAR_DILUTION_DIR", before); }
    }
}
